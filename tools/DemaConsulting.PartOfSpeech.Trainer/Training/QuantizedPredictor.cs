namespace DemaConsulting.PartOfSpeech.Trainer;

/// <summary>
///     The trainer's own greedy predictor over quantized weights: the same computation as the runtime decoder, written
///     with the trainer's building blocks, so that a test can prove the two never drift.
/// </summary>
/// <remarks>
///     <para>
///         The predictor holds the quantized rows in a dense table (no occupancy bit set, no rank table) and decodes
///         with <see cref="FeatureExtractor"/> and <see cref="TagSelection"/> exactly as the trainer does. If it gives
///         the same tags and the same integer scores as <c>TagDecoder</c> on a real weights file, then what the trainer
///         optimizes is what the runtime executes. It takes the feature groups of the model and, with the right preview
///         group, decodes twice like the decoder (the first pass without the preview features).
///     </para>
///     <para>
///         Not thread-safe (it owns a workspace); the table is read-only after construction.
///     </para>
/// </remarks>
internal sealed class QuantizedPredictor
{
    /// <summary>
    ///     The quantized weights of every bucket and tag.
    /// </summary>
    private readonly short[] _weights;

    /// <summary>
    ///     The scratch workspace.
    /// </summary>
    private readonly DecoderWorkspace _workspace = new();

    /// <summary>
    ///     The scratch buffer for the buckets of a token.
    /// </summary>
    private readonly int[] _buckets = new int[FeatureExtractor.BucketCapacity];

    /// <summary>
    ///     Initializes a new instance of the <see cref="QuantizedPredictor"/> class.
    /// </summary>
    /// <param name="hashBits">The bucket space exponent.</param>
    /// <param name="rows">The rows as the writer takes them.</param>
    /// <param name="groups">The feature groups of the model (the mask of its header); none is the shipped feature set.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rows"/> is <c>null</c>.</exception>
    public QuantizedPredictor(int hashBits, IEnumerable<KeyValuePair<int, short[]>> rows, FeatureGroups groups = FeatureGroups.None)
    {
        ArgumentNullException.ThrowIfNull(rows);
        HashBits = hashBits;
        Groups = groups;
        _weights = new short[(1 << hashBits) * UposExtensions.Count];
        foreach (var row in rows)
        {
            Array.Copy(row.Value, 0, _weights, row.Key * UposExtensions.Count, UposExtensions.Count);
        }
    }

    /// <summary>
    ///     Gets the bucket space exponent.
    /// </summary>
    public int HashBits { get; }

    /// <summary>
    ///     Gets the feature groups of the model.
    /// </summary>
    public FeatureGroups Groups { get; }

    /// <summary>
    ///     Tags a sentence greedily from left to right with the predicted history; with the right preview group the sentence is
    ///     decoded twice, as the runtime decoder does.
    /// </summary>
    /// <remarks>
    ///     The first pass runs without the preview features and only chooses tags; the second pass reads them for the two tokens
    ///     ahead and gives the result and the scores.
    /// </remarks>
    /// <param name="tokens">The tokens.</param>
    /// <param name="lexicon">The lexicon that restricts the candidates, or <c>null</c> to choose among all 17 tags.</param>
    /// <param name="scores">When not <c>null</c>, receives the integer score vector of every token (of the second pass with the preview group).</param>
    /// <returns>The chosen tag ordinal of every token.</returns>
    public int[] Predict(string[] tokens, Lexicon? lexicon, List<int[]>? scores = null)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        _workspace.Configure(Groups, lexicon);
        FeatureExtractor.Prepare(_workspace, tokens, HashBits);
        var result = new int[tokens.Length];
        if ((Groups & FeatureGroups.RightPreview) != 0)
        {
            FeatureExtractor.StartPass(_workspace, false);
            Decode(tokens.Length, lexicon, result, null);
            _workspace.SetPreview(result);
            FeatureExtractor.StartPass(_workspace, true);
            result = new int[tokens.Length];
        }

        Decode(tokens.Length, lexicon, result, scores);
        return result;
    }

    /// <summary>
    ///     Decodes the prepared sentence once.
    /// </summary>
    /// <param name="count">The number of tokens.</param>
    /// <param name="lexicon">The lexicon, or <c>null</c>.</param>
    /// <param name="result">Receives the chosen tag of every token.</param>
    /// <param name="scores">Receives the score vectors, or <c>null</c>.</param>
    private void Decode(int count, Lexicon? lexicon, int[] result, List<int[]>? scores)
    {
        var previous1 = FeatureExtractor.StartTag1;
        var previous2 = FeatureExtractor.StartTag2;
        for (var i = 0; i < count; i++)
        {
            // Sum the integer weights of the features of the token for every tag
            var featureCount = FeatureExtractor.Extract(_workspace, i, previous1, previous2, HashBits, _buckets);
            var sums = new int[UposExtensions.Count];
            for (var k = 0; k < featureCount; k++)
            {
                var offset = _buckets[k] * UposExtensions.Count;
                for (var tag = 0; tag < sums.Length; tag++)
                {
                    sums[tag] += _weights[offset + tag];
                }
            }

            // Choose among the allowed tags, or among all of them without a lexicon
            var mask = lexicon == null ? TagSelection.AllTags : TagSelection.AllowedMask(_workspace, i, lexicon);
            var best = TagSelection.ArgMax(sums, mask);
            scores?.Add(sums);
            result[i] = best;
            FeatureExtractor.Advance(_workspace, i, best);
            previous2 = previous1;
            previous1 = best;
        }
    }
}
