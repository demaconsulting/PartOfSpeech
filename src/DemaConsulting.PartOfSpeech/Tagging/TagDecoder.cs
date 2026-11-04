namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     Tags the tokens of one sentence with a greedy left-to-right decoder over the averaged-perceptron scores.
/// </summary>
/// <remarks>
///     <para>
///         For each token the decoder extracts the hashed features (using the tags chosen for the two previous
///         tokens), sums the integer weights of every tag, restricts the candidates to the tags that the lexicon and
///         the token-shape rules allow (and to what a <see cref="TokenConstraint"/> demands), picks the best-scoring
///         candidate (the lowest tag index wins a tie, so results do not depend on the platform) and converts the
///         scores into probabilities with a softmax at the temperature stored in the model.
///     </para>
///     <para>
///         The decoder is immutable and thread-safe: its model and lexicon are read-only and the scratch memory is a
///         per-thread <see cref="DecoderWorkspace"/> that is reused, so decoding allocates only the result arrays (one
///         array of tokens and one flat array of alternatives per sentence).
///     </para>
/// </remarks>
internal sealed class TagDecoder
{
    /// <summary>
    ///     The scratch memory of the current thread.
    /// </summary>
    [ThreadStatic]
    private static DecoderWorkspace? _threadWorkspace;

    /// <summary>
    ///     The perceptron weights.
    /// </summary>
    private readonly PerceptronModel _model;

    /// <summary>
    ///     The word lexicon.
    /// </summary>
    private readonly Lexicon _lexicon;

    /// <summary>
    ///     The softmax temperature as a double.
    /// </summary>
    private readonly double _temperature;

    /// <summary>
    ///     The weight scale as a double.
    /// </summary>
    private readonly double _scale;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TagDecoder"/> class.
    /// </summary>
    /// <param name="model">The perceptron weights.</param>
    /// <param name="lexicon">The word lexicon.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    public TagDecoder(PerceptronModel model, Lexicon lexicon)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(lexicon);
        _model = model;
        _lexicon = lexicon;
        _temperature = model.Temperature;
        _scale = model.Scale;
    }

    /// <summary>
    ///     Gets the softmax temperature of the model.
    /// </summary>
    /// <remarks>
    ///     Exposed (internally only) so that the glossary can state a soft boost in units of the temperature and convert it
    ///     into the raw score units that the decoder adds to the scores.
    /// </remarks>
    internal double Temperature => _temperature;

    /// <summary>
    ///     Tags the tokens of a sentence.
    /// </summary>
    /// <param name="tokens">The tokens of the sentence.</param>
    /// <param name="constraints">One constraint per token, or <c>null</c> for none; the length must equal the token count.</param>
    /// <param name="maxAlternatives">The maximum number of alternatives per token (0 to 16).</param>
    /// <returns>One tagged token per input token.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tokens"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="constraints"/> has another length than <paramref name="tokens"/>.</exception>
    public TaggedToken[] Decode(Token[] tokens, IReadOnlyList<TokenConstraint>? constraints, int maxAlternatives)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        // Resolve the caller's list before the workspace is taken: its indexer is caller code and may itself tag text
        var snapshot = SnapshotConstraints(constraints, tokens.Length);

        var count = tokens.Length;
        var result = new TaggedToken[count];
        if (count == 0)
        {
            return result;
        }

        // Fold and hash all tokens once; the features that do not depend on tags are ready after this
        var workspace = GetWorkspace();
        workspace.Begin(count);
        for (var i = 0; i < count; i++)
        {
            FeatureExtractor.SetToken(workspace, i, tokens[i].Text);
        }

        FeatureExtractor.Finish(workspace, _model.HashBits);
        workspace.EnsureLexiconScratch(_lexicon.ScratchSize);

        // One flat array holds the alternatives of all tokens, a fixed number of slots per token
        var slots = Math.Min(maxAlternatives, UposExtensions.Count - 1);
        var alternatives = slots > 0 ? new TagScore[count * slots] : null;

        var previous1 = FeatureExtractor.StartTag1;
        var previous2 = FeatureExtractor.StartTag2;
        for (var i = 0; i < count; i++)
        {
            var constraint = snapshot == null ? default : snapshot[i];
            var best = DecodeToken(workspace, i, previous1, previous2, constraint, slots, alternatives, out var confidence, out var alternativeCount);
            result[i] = new TaggedToken(tokens[i].Text, tokens[i].Start, (Upos)best, confidence, alternatives, i * slots, alternativeCount, !constraint.IsNone);

            // The decision becomes history for the next two tokens, like any other decision
            previous2 = previous1;
            previous1 = best;
        }

        return result;
    }

    /// <summary>
    ///     Resolves the constraints of the caller to an array that the decoder reads without running caller code.
    /// </summary>
    /// <param name="constraints">The constraints of the caller, or <c>null</c>.</param>
    /// <param name="tokenCount">The number of tokens of the sentence.</param>
    /// <returns><c>null</c> for no constraints, the array itself when the caller passed an array, otherwise a copy.</returns>
    /// <exception cref="ArgumentException"><paramref name="constraints"/> has another length than <paramref name="tokenCount"/>.</exception>
    private static TokenConstraint[]? SnapshotConstraints(IReadOnlyList<TokenConstraint>? constraints, int tokenCount)
    {
        if (constraints == null)
        {
            return null;
        }

        // An array indexer runs no user code, so the array is used as is without a copy
        if (constraints is TokenConstraint[] array)
        {
            if (array.Length != tokenCount)
            {
                throw new ArgumentException($"Expected {tokenCount} constraints (one per token) but got {array.Length}.", nameof(constraints));
            }

            return array;
        }

        // Read Count once and copy exactly that many items, so the decode sees one consistent snapshot
        var listCount = constraints.Count;
        if (listCount != tokenCount)
        {
            throw new ArgumentException($"Expected {tokenCount} constraints (one per token) but got {listCount}.", nameof(constraints));
        }

        var copy = new TokenConstraint[listCount];
        for (var i = 0; i < listCount; i++)
        {
            copy[i] = constraints[i];
        }

        return copy;
    }

    /// <summary>
    ///     Gets the scratch memory of the current thread, creating it on first use.
    /// </summary>
    /// <returns>The workspace; only the calling thread may use it.</returns>
    private static DecoderWorkspace GetWorkspace()
    {
        var workspace = _threadWorkspace;
        if (workspace == null)
        {
            workspace = new DecoderWorkspace();
            _threadWorkspace = workspace;
        }

        return workspace;
    }

    /// <summary>
    ///     Chooses the tag of one token.
    /// </summary>
    /// <param name="workspace">The workspace that was prepared for the sentence.</param>
    /// <param name="index">The token.</param>
    /// <param name="previous1">The previous tag code.</param>
    /// <param name="previous2">The tag code before that.</param>
    /// <param name="constraint">The constraint of the token.</param>
    /// <param name="slots">The number of alternative slots per token.</param>
    /// <param name="alternatives">The flat alternatives array, or <c>null</c> when no alternatives are requested.</param>
    /// <param name="confidence">Receives the probability of the chosen tag.</param>
    /// <param name="alternativeCount">Receives the number of alternatives written for the token.</param>
    /// <returns>The chosen tag ordinal.</returns>
    private int DecodeToken(
        DecoderWorkspace workspace,
        int index,
        int previous1,
        int previous2,
        TokenConstraint constraint,
        int slots,
        TagScore[]? alternatives,
        out double confidence,
        out int alternativeCount)
    {
        // Sum the integer weights of all features of the token for every tag
        var scores = workspace.Scores;
        Array.Clear(scores, 0, scores.Length);
        var buckets = workspace.Buckets;
        var featureCount = FeatureExtractor.Extract(workspace, index, previous1, previous2, _model.HashBits, buckets);
        for (var k = 0; k < featureCount; k++)
        {
            _model.AddScores(buckets[k], scores);
        }

        // Candidates: what the lexicon and the shape allow, narrowed by the constraint (the constraint wins if nothing is left)
        var allowed = AllowedTags(workspace, index);
        var effective = constraint.Allowed.Intersect(allowed);
        if (effective.IsEmpty)
        {
            effective = constraint.Allowed;
        }

        // Real scores of the candidates (soft constraints are added in raw score units) and the best of them
        var real = workspace.Real;
        var best = -1;
        var bestScore = 0.0;
        var mask = effective.Mask;
        var candidates = 0;
        for (var tag = 0; tag < UposExtensions.Count; tag++)
        {
            if ((mask & (1 << tag)) == 0)
            {
                continue;
            }

            var score = (scores[tag] * _scale) + constraint.AdjustmentOf(tag);
            real[tag] = score;
            candidates++;
            if (best < 0 || score > bestScore)
            {
                best = tag;
                bestScore = score;
            }
        }

        // Softmax over the candidates at the model temperature (shifted by the maximum for numeric stability)
        var sum = 0.0;
        for (var tag = 0; tag < UposExtensions.Count; tag++)
        {
            if ((mask & (1 << tag)) != 0)
            {
                var weight = Math.Exp((real[tag] - bestScore) / _temperature);
                real[tag] = weight;
                sum += weight;
            }
        }

        confidence = real[best] / sum;

        // Alternatives: repeatedly take the most probable remaining candidate (lower tag index wins a tie)
        alternativeCount = Math.Min(slots, candidates - 1);
        for (var slot = 0; slot < alternativeCount; slot++)
        {
            var pick = -1;
            for (var tag = 0; tag < UposExtensions.Count; tag++)
            {
                if ((mask & (1 << tag)) != 0 && tag != best && (pick < 0 || real[tag] > real[pick]))
                {
                    pick = tag;
                }
            }

            alternatives![(index * slots) + slot] = new TagScore((Upos)pick, real[pick] / sum);

            // Remove the pick from the remaining candidates
            mask &= ~(1 << pick);
        }

        return best;
    }

    /// <summary>
    ///     Computes the tags that the lexicon and the token-shape rules allow for a token of the workspace.
    /// </summary>
    /// <param name="workspace">The workspace.</param>
    /// <param name="index">The token.</param>
    /// <returns>The allowed tags.</returns>
    private UposSet AllowedTags(DecoderWorkspace workspace, int index)
    {
        // The lexicon key is the lower-cased folded token, which follows the folded form in the character buffer
        var length = workspace.Length[index];
        var lowerStart = workspace.Start[index] + length;
        var known = _lexicon.TryGetTags(workspace.Chars, lowerStart, length, workspace.LexiconScratch, out var tags);
        var flags = workspace.Flags[index];
        return Lexicon.AllowedTags(
            known,
            tags,
            (flags & FeatureExtractor.FlagLetterOrDigit) != 0,
            (flags & FeatureExtractor.FlagDigit) != 0,
            (flags & FeatureExtractor.FlagFirstUpper) != 0);
    }
}
