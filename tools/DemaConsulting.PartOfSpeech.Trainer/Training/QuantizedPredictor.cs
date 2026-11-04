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
///         optimizes is what the runtime executes.
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
    private readonly int[] _buckets = new int[FeatureExtractor.MaxFeatures];

    /// <summary>
    ///     Initializes a new instance of the <see cref="QuantizedPredictor"/> class.
    /// </summary>
    /// <param name="hashBits">The bucket space exponent.</param>
    /// <param name="rows">The rows as the writer takes them.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rows"/> is <c>null</c>.</exception>
    public QuantizedPredictor(int hashBits, IEnumerable<KeyValuePair<int, short[]>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        HashBits = hashBits;
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
    ///     Tags a sentence greedily from left to right with the predicted history.
    /// </summary>
    /// <param name="tokens">The tokens.</param>
    /// <param name="lexicon">The lexicon that restricts the candidates, or <c>null</c> to choose among all 17 tags.</param>
    /// <param name="scores">When not <c>null</c>, receives the integer score vector of every token.</param>
    /// <returns>The chosen tag ordinal of every token.</returns>
    public int[] Predict(string[] tokens, Lexicon? lexicon, List<int[]>? scores = null)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        FeatureExtractor.Prepare(_workspace, tokens, HashBits);
        var result = new int[tokens.Length];
        var previous1 = FeatureExtractor.StartTag1;
        var previous2 = FeatureExtractor.StartTag2;
        for (var i = 0; i < tokens.Length; i++)
        {
            // Sum the integer weights of the features of the token for every tag
            var count = FeatureExtractor.Extract(_workspace, i, previous1, previous2, HashBits, _buckets);
            var sums = new int[UposExtensions.Count];
            for (var k = 0; k < count; k++)
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
            previous2 = previous1;
            previous1 = best;
        }

        return result;
    }
}
