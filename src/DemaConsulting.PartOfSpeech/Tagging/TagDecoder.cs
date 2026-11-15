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
///         scores into probabilities with a softmax at the temperature stored in the model. The result token also
///         reports whether the lexicon has an entry for the word; that is the result of the lexicon lookup that
///         already decides the allowed tags, so it costs no further lookup and no allocation.
///     </para>
///     <para>
///         The groups of context features of the model (<see cref="PerceptronModel.FeatureGroups"/>) decide which extra
///         features the decoder extracts. A model with the <see cref="FeatureGroups.RightPreview"/> group is decoded in two passes:
///         the first pass chooses tags with the same rules but without the preview features (and without probabilities);
///         the second pass decodes again with the first-pass tags of the next two tokens as features and produces the result.
///         Constraints apply in both passes. A model without that group is decoded in one pass, exactly as before the groups existed.
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
    ///     The groups of context features of the model.
    /// </summary>
    private readonly FeatureGroups _groups;

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
        _groups = model.FeatureGroups;
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
        workspace.Configure(_groups, _lexicon);
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

        // With the right preview group the sentence is decoded twice: the first pass (without the preview features) only
        // chooses tags, the second pass sees them for the two tokens ahead and produces the result
        if ((_groups & FeatureGroups.RightPreview) != 0)
        {
            PreviewPass(workspace, count, snapshot);
        }

        var previous1 = FeatureExtractor.StartTag1;
        var previous2 = FeatureExtractor.StartTag2;
        for (var i = 0; i < count; i++)
        {
            var constraint = snapshot == null ? default : snapshot[i];
            var best = DecodeToken(workspace, i, previous1, previous2, constraint, slots, alternatives, false, out var confidence, out var alternativeCount, out var known);
            result[i] = new TaggedToken(tokens[i].Text, tokens[i].Start, (Upos)best, confidence, alternatives, i * slots, alternativeCount, !constraint.IsNone, known);

            // The decision becomes history for the next two tokens, like any other decision
            FeatureExtractor.Advance(workspace, i, best);
            previous2 = previous1;
            previous1 = best;
        }

        return result;
    }

    /// <summary>
    ///     Runs the first decoding pass of the right preview group: chooses a tag for every token without the preview
    ///     features, stores the tags in the workspace and starts the second pass.
    /// </summary>
    /// <param name="workspace">The prepared workspace.</param>
    /// <param name="count">The number of tokens.</param>
    /// <param name="constraints">The resolved constraints, or <c>null</c>.</param>
    private void PreviewPass(DecoderWorkspace workspace, int count, TokenConstraint[]? constraints)
    {
        FeatureExtractor.StartPass(workspace, false);
        var previous1 = FeatureExtractor.StartTag1;
        var previous2 = FeatureExtractor.StartTag2;
        for (var i = 0; i < count; i++)
        {
            var constraint = constraints == null ? default : constraints[i];
            var best = DecodeToken(workspace, i, previous1, previous2, constraint, 0, null, true, out _, out _, out _);
            workspace.Preview[i] = best;
            FeatureExtractor.Advance(workspace, i, best);
            previous2 = previous1;
            previous1 = best;
        }

        FeatureExtractor.StartPass(workspace, true);
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
    /// <param name="choiceOnly">Whether only the tag is wanted (the first pass of the right preview group): the probabilities are not computed.</param>
    /// <param name="confidence">Receives the probability of the chosen tag.</param>
    /// <param name="alternativeCount">Receives the number of alternatives written for the token.</param>
    /// <param name="known">Receives whether the lexicon has an entry for the token; it is the result of the lookup that decides the allowed tags, so no further lookup is made.</param>
    /// <returns>The chosen tag ordinal.</returns>
    private int DecodeToken(
        DecoderWorkspace workspace,
        int index,
        int previous1,
        int previous2,
        TokenConstraint constraint,
        int slots,
        TagScore[]? alternatives,
        bool choiceOnly,
        out double confidence,
        out int alternativeCount,
        out bool known)
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
        var allowed = AllowedTags(workspace, index, out known);
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
        if (choiceOnly)
        {
            confidence = 0;
            alternativeCount = 0;
            return best;
        }

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
    /// <param name="known">Receives whether the lexicon has an entry for the token (the lookup result that is used here).</param>
    /// <returns>The allowed tags.</returns>
    private UposSet AllowedTags(DecoderWorkspace workspace, int index, out bool known)
    {
        // The groups that read the lexicon already looked every token up when the sentence was prepared
        var flags = workspace.Flags[index];
        UposSet tags;
        if (workspace.LexCached)
        {
            known = workspace.LexKnown[index] != 0;
            tags = UposSet.FromMask(workspace.LexMask[index]);
        }
        else
        {
            // The lexicon key is the lower-cased folded token, which follows the folded form in the character buffer
            var length = workspace.Length[index];
            var lowerStart = workspace.Start[index] + length;
            known = _lexicon.TryGetTags(workspace.Chars, lowerStart, length, workspace.LexiconScratch, out tags);
        }

        return ApplyShapeRules(flags, known, tags);
    }

    /// <summary>
    ///     Applies the token-shape rules to the lexicon tags of a token: the one place that turns the facts of a folded token into its allowed tags.
    /// </summary>
    /// <remarks>
    ///     The decoder (<see cref="AllowedTags"/>) and the word lookup (<see cref="GetPossibleTags(string, PossibleTagsMode)"/>)
    ///     both call this, so the tags that the lookup reports in <see cref="PossibleTagsMode.WithShapeRules"/> mode are the
    ///     allowed tags of the decoder by construction.
    /// </remarks>
    /// <param name="flags">The shape facts that <see cref="FeatureExtractor.SetFolded"/> recorded for the token.</param>
    /// <param name="known">Whether the lower-cased token is in the lexicon.</param>
    /// <param name="tags">The lexicon tags of the token; ignored when <paramref name="known"/> is <c>false</c>.</param>
    /// <returns>The allowed tags; never empty.</returns>
    private static UposSet ApplyShapeRules(byte flags, bool known, UposSet tags) =>
        Lexicon.AllowedTags(
            known,
            tags,
            (flags & FeatureExtractor.FlagLetterOrDigit) != 0,
            (flags & FeatureExtractor.FlagDigit) != 0,
            (flags & FeatureExtractor.FlagFirstUpper) != 0);

    /// <summary>
    ///     Looks up one word on its own: the tags it can take, whether the lexicon knows it, and its analyses.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The word is folded exactly like a token of the decoder (NFC, typographic quotes, dashes and ellipsis, lower
    ///         case) through the same code, so the result does not depend on the position of the word in a sentence or on
    ///         the model. The caller (<see cref="Tagger"/>) has already rejected null, empty and white-space input and an
    ///         undefined mode.
    ///     </para>
    ///     <para>
    ///         Non-ASCII text is normalized first, before the thread's workspace is touched, so that the length test below
    ///         sees the real length. ASCII text allocates nothing.
    ///     </para>
    /// </remarks>
    /// <param name="word">The word as written.</param>
    /// <param name="mode">Which tags to report.</param>
    /// <returns>The result of the lookup; see <see cref="WordTags"/>.</returns>
    internal WordTags GetPossibleTags(string word, PossibleTagsMode mode) => Lookup(NormalizeForLookup(word), mode);

#if NET
    /// <summary>
    ///     Looks up one word given as a span; see <see cref="GetPossibleTags(string, PossibleTagsMode)"/>.
    /// </summary>
    /// <remarks>
    ///     A span of pure ASCII is looked up without a copy. A span with a character above U+007F is copied to a string
    ///     first (the normalization needs one), which allocates.
    /// </remarks>
    /// <param name="word">The word as written.</param>
    /// <param name="mode">Which tags to report.</param>
    /// <returns>The result of the lookup; see <see cref="WordTags"/>.</returns>
    internal WordTags GetPossibleTags(ReadOnlySpan<char> word, PossibleTagsMode mode)
    {
        for (var i = 0; i < word.Length; i++)
        {
            if (word[i] >= 0x80)
            {
                return Lookup(FeatureExtractor.NormalizeNonAscii(word.ToString()), mode);
            }
        }

        return Lookup(word, mode);
    }
#endif

    /// <summary>
    ///     Tells whether a word is a form of a base word with a tag, using the analyses of the lexicon.
    /// </summary>
    /// <remarks>
    ///     Word and base word are folded like a token of the decoder. The word is found with the rule of the word lookup
    ///     (the whole spelling, otherwise the last part of a hyphenated compound), and the base word is compared as UTF-8
    ///     bytes with the base words that the templates of the analyses compose, so the call allocates nothing for text
    ///     that is pure ASCII.
    /// </remarks>
    /// <param name="word">The word as written.</param>
    /// <param name="lemma">The base word as written.</param>
    /// <param name="tag">The tag; the caller has checked that it is defined.</param>
    /// <returns><c>true</c> if an analysis of the word has this base word and this tag.</returns>
    internal bool IsFormOf(string word, string lemma, Upos tag)
    {
        // Normalize first and test the lengths: a base word is at most MaxLemmaBytes bytes
        var wordText = NormalizeForLookup(word);
        var lemmaText = NormalizeForLookup(lemma);
        if (lemmaText.Length > Lexicon.MaxLemmaBytes)
        {
            return false;
        }

        // A word longer than the longest spelling is not a spelling; only the last part of a compound can match (nothing else is folded)
        var head = 0;
        if (wordText.Length > _lexicon.MaxSpellingBytes)
        {
            head = OverlongHeadStart(wordText);
            if (head < 0)
            {
                return false;
            }
        }

        // Fold both into the workspace: token 0 is the word (or its last part), token 1 the base word
        var workspace = GetWorkspace();
        workspace.Configure(_groups, _lexicon);
        workspace.Begin(2);
#if NET
        FeatureExtractor.SetFolded(workspace, 0, wordText.AsSpan(head));
#else
        FeatureExtractor.SetFoldedPart(workspace, 0, wordText, head, wordText.Length - head);
#endif
        FeatureExtractor.SetFolded(workspace, 1, lemmaText);
        workspace.EnsureLexiconScratch(_lexicon.ScratchSize);

        // Find the spelling whose analyses count (the key stays UTF-8 encoded in the scratch buffer)
        var ordinal = -1;
        var keyLength = 0;
        var found = head > 0
            ? _lexicon.TryFindKey(workspace.Chars, workspace.Start[0] + workspace.Length[0], workspace.Length[0], workspace.LexiconScratch, out ordinal, out _, out keyLength)
            : Find(workspace, 0, out ordinal, out keyLength, out _) != WordMatch.NotRecognized;
        if (!found)
        {
            return false;
        }

        // Encode the lower-cased base word and compare it with the base word that each analysis of the right tag composes
        var lemmaLength = workspace.Length[1];
        var lemmaBytes = Lexicon.EncodeUtf8(workspace.Chars, workspace.Start[1] + lemmaLength, lemmaLength, workspace.LemmaBytes, Lexicon.MaxLemmaBytes);
        return lemmaBytes >= 0
            && _lexicon.HasAnalysis(ordinal, tag, workspace.LexiconScratch, 0, keyLength, workspace.LemmaBytes, 0, lemmaBytes);
    }

    /// <summary>
    ///     Normalizes non-ASCII text to NFC for a lookup; ASCII text is returned as it is.
    /// </summary>
    /// <param name="text">The text as written.</param>
    /// <returns>The text itself when it is pure ASCII, otherwise the normalized, well-formed text.</returns>
    private static string NormalizeForLookup(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] >= 0x80)
            {
                return FeatureExtractor.NormalizeNonAscii(text);
            }
        }

        return text;
    }

    /// <summary>
    ///     Looks up a normalized word: folds it into the thread's workspace, finds it and builds the result.
    /// </summary>
    /// <remarks>
    ///     A word longer than the longest spelling of the lexicon cannot be a spelling (folding and lower-casing never
    ///     shorten it and a character takes at least one UTF-8 byte), so the whole-spelling lookup is skipped for it and
    ///     it is never folded as a whole: a huge input never makes the per-thread buffers grow. Its last part can still be
    ///     a spelling, so the compound rule runs on the last part alone (see <see cref="LookupOverlong"/>).
    /// </remarks>
    /// <param name="text">The word, NFC-normalized (or pure ASCII).</param>
    /// <param name="mode">Which tags to report.</param>
    /// <returns>The result of the lookup.</returns>
#if NET
    private WordTags Lookup(ReadOnlySpan<char> text, PossibleTagsMode mode)
#else
    private WordTags Lookup(string text, PossibleTagsMode mode)
#endif
    {
        if (text.Length > _lexicon.MaxSpellingBytes)
        {
            return LookupOverlong(text, mode);
        }
        // Prepare the workspace for one token; Configure first, because it clears the lexicon facts of an earlier sentence
        var workspace = GetWorkspace();
        workspace.Configure(_groups, _lexicon);
        workspace.Begin(1);
        FeatureExtractor.SetFolded(workspace, 0, text);
        workspace.EnsureLexiconScratch(_lexicon.ScratchSize);

        // The tags are those of the whole token only, as the decoder sees it; a compound head adds analyses but never tags
        var match = Find(workspace, 0, out var ordinal, out _, out var wholeTags);
        var known = match == WordMatch.Spelling;
        var tags = mode == PossibleTagsMode.WithShapeRules
            ? ApplyShapeRules(workspace.Flags[0], known, wholeTags)
            : wholeTags;
        return match == WordMatch.NotRecognized
            ? new WordTags(null, -1, tags, match, UposSet.Empty)
            : new WordTags(_lexicon, ordinal, tags, match, _lexicon.AnalysedTags(ordinal));
    }

    /// <summary>
    ///     Looks up a word that is longer than the longest spelling of the lexicon: it cannot be a spelling as a whole, but
    ///     the last part of a hyphenated compound can be.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The compound rule is the one of <see cref="Find"/>: the word must contain the hyphen-minus and no empty part
    ///         between hyphens, and the last part must be a spelling. Only the last part is folded into the workspace and only
    ///         when it is no longer than the longest spelling, so the per-thread buffers never grow beyond what a word that
    ///         fits needs, whatever the length of the whole word. Finding the last part costs one scan of the word and
    ///         allocates nothing.
    ///     </para>
    ///     <para>
    ///         The tags are those of the whole word, which is not a spelling: empty in the lexicon mode and all tags with
    ///         the shape rules, as for any word that is not a spelling.
    ///     </para>
    /// </remarks>
    /// <param name="text">The word, NFC-normalized (or pure ASCII), longer than the longest spelling.</param>
    /// <param name="mode">Which tags to report.</param>
    /// <returns>The compound-head result, or the not-recognized result.</returns>
#if NET
    private WordTags LookupOverlong(ReadOnlySpan<char> text, PossibleTagsMode mode)
#else
    private WordTags LookupOverlong(string text, PossibleTagsMode mode)
#endif
    {
        var tags = mode == PossibleTagsMode.WithShapeRules ? UposSet.All : UposSet.Empty;
        var head = OverlongHeadStart(text);
        if (head >= 0)
        {
            // Fold the last part alone and look it up as a whole
            var workspace = GetWorkspace();
            workspace.Configure(_groups, _lexicon);
            workspace.Begin(1);
#if NET
            FeatureExtractor.SetFolded(workspace, 0, text.Slice(head));
#else
            FeatureExtractor.SetFoldedPart(workspace, 0, text, head, text.Length - head);
#endif
            workspace.EnsureLexiconScratch(_lexicon.ScratchSize);
            var length = workspace.Length[0];
            if (_lexicon.TryFindKey(workspace.Chars, workspace.Start[0] + length, length, workspace.LexiconScratch, out var ordinal, out _, out _))
            {
                return new WordTags(_lexicon, ordinal, tags, WordMatch.CompoundHead, _lexicon.AnalysedTags(ordinal));
            }
        }

        return new WordTags(null, -1, tags, WordMatch.NotRecognized, UposSet.Empty);
    }

    /// <summary>
    ///     Finds where the last part of an over-long hyphenated compound starts, without folding the word.
    /// </summary>
    /// <remarks>
    ///     The scan applies the compound rule of <see cref="Find"/> to the characters as written: only the hyphen-minus is a
    ///     hyphen, and a character that folds to <c>--</c> (see <see cref="FeatureExtractor.FoldsToDoubleHyphen"/>) makes an
    ///     empty part, exactly as it does in the folded word. A last part longer than the longest spelling cannot be a
    ///     spelling (folding never shortens it), so it is refused here, before anything is folded.
    /// </remarks>
    /// <param name="text">The word, NFC-normalized (or pure ASCII).</param>
    /// <returns>The index of the first character of the last part, or -1 when the word is not a compound or its last part cannot be a spelling.</returns>
#if NET
    private int OverlongHeadStart(ReadOnlySpan<char> text)
#else
    private int OverlongHeadStart(string text)
#endif
    {
        var lastHyphen = -1;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '-')
            {
                // A leading or doubled hyphen leaves an empty part
                if (i == lastHyphen + 1)
                {
                    return -1;
                }

                lastHyphen = i;
            }
            else if (FeatureExtractor.FoldsToDoubleHyphen(c))
            {
                return -1;
            }
        }

        // No hyphen, a trailing hyphen or a last part that cannot fit is not a compound head
        if (lastHyphen < 0 || lastHyphen == text.Length - 1 || text.Length - lastHyphen - 1 > _lexicon.MaxSpellingBytes)
        {
            return -1;
        }

        return lastHyphen + 1;
    }

    /// <summary>
    ///     Finds a folded token in the lexicon: the whole spelling first, otherwise the last part of a hyphenated compound.
    /// </summary>
    /// <remarks>
    ///     A hyphenated compound contains the hyphen-minus and no empty part between hyphens; only its last part is looked
    ///     up, as a whole and not split any further. The whole spelling always wins.
    /// </remarks>
    /// <param name="workspace">The workspace that holds the folded token.</param>
    /// <param name="index">The token.</param>
    /// <param name="ordinal">Receives the position of the spelling that matched (the whole word or the last part), or -1.</param>
    /// <param name="keyLength">Receives the number of UTF-8 bytes of the matched key, which stays in the lexicon scratch buffer.</param>
    /// <param name="wholeTags">Receives the lexicon tags of the whole token; the empty set unless the whole token is a spelling.</param>
    /// <returns>How the word matched.</returns>
    private WordMatch Find(DecoderWorkspace workspace, int index, out int ordinal, out int keyLength, out UposSet wholeTags)
    {
        // The whole spelling: the same lookup the decoder makes
        var length = workspace.Length[index];
        var lowerStart = workspace.Start[index] + length;
        var chars = workspace.Chars;
        var scratch = workspace.LexiconScratch;
        if (_lexicon.TryFindKey(chars, lowerStart, length, scratch, out ordinal, out wholeTags, out keyLength))
        {
            return WordMatch.Spelling;
        }

        // A compound: every part between hyphen-minus characters must be non-empty (a leading, trailing or doubled hyphen is not one)
        var lastHyphen = -1;
        for (var i = 0; i < length; i++)
        {
            if (chars[lowerStart + i] != '-')
            {
                continue;
            }

            if (i == lastHyphen + 1)
            {
                return WordMatch.NotRecognized;
            }

            lastHyphen = i;
        }

        if (lastHyphen < 0 || lastHyphen == length - 1)
        {
            return WordMatch.NotRecognized;
        }

        // Look up the last part as it is (no recursion); ordinal is -1 again when it is not a spelling
        return _lexicon.TryFindKey(chars, lowerStart + lastHyphen + 1, length - lastHyphen - 1, scratch, out ordinal, out _, out keyLength)
            ? WordMatch.CompoundHead
            : WordMatch.NotRecognized;
    }
}
