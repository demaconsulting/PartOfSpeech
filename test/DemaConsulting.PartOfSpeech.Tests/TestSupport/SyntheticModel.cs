namespace DemaConsulting.PartOfSpeech.Tests;

/// <summary>
///     Creates a random sparse weights file of the shape that a real model has, for the throughput and size tests.
/// </summary>
/// <remarks>
///     A real model has about 150,000 occupied buckets with about 2.7 weights each in a bucket space of 2^21. To make
///     the lookups behave like production (a realistic share of the features of real text hits an occupied bucket) the
///     occupied buckets are the ones that sample sentences produce; the weights themselves are random. The generator
///     is seeded, so the file is identical on every run.
/// </remarks>
internal static class SyntheticModel
{
    /// <summary>
    ///     The exponent of the bucket space of the synthetic model.
    /// </summary>
    public const int HashBits = 21;

    /// <summary>
    ///     The number of occupied buckets of the synthetic model.
    /// </summary>
    public const int RowCount = 150000;

    /// <summary>
    ///     Creates the weights file.
    /// </summary>
    /// <param name="sentences">Sample sentences whose features decide which buckets are occupied.</param>
    /// <param name="compress">Whether to Deflate the payload.</param>
    /// <returns>The weights file.</returns>
    public static byte[] Create(IEnumerable<string[]> sentences, bool compress = true)
    {
        var random = new Random(42);
        var occupied = new HashSet<int>();
        var workspace = new DecoderWorkspace();
        var buckets = new int[FeatureExtractor.MaxFeatures];

        // First the buckets that sample text really produces (with random tag history)
        foreach (var tokens in sentences)
        {
            FeatureExtractor.Prepare(workspace, tokens, HashBits);
            for (var i = 0; i < tokens.Length && occupied.Count < RowCount; i++)
            {
                var count = FeatureExtractor.Extract(workspace, i, random.Next(UposExtensions.Count), random.Next(UposExtensions.Count), HashBits, buckets);
                for (var k = 0; k < count && occupied.Count < RowCount; k++)
                {
                    occupied.Add(buckets[k]);
                }
            }

            if (occupied.Count >= RowCount)
            {
                break;
            }
        }

        // Then random buckets up to the target size
        while (occupied.Count < RowCount)
        {
            occupied.Add(random.Next(1 << HashBits));
        }

        // Random sparse weights: one to seventeen per row, about 2.7 on average
        var rows = new List<KeyValuePair<int, short[]>>(RowCount);
        foreach (var bucket in occupied)
        {
            var weights = new short[UposExtensions.Count];
            var size = Math.Min(UposExtensions.Count, 1 + (int)(-Math.Log(1 - random.NextDouble()) * 1.7));
            for (var n = 0; n < size; n++)
            {
                var value = random.Next(1, 3000) * (random.Next(2) == 0 ? -1 : 1);
                weights[random.Next(UposExtensions.Count)] = (short)value;
            }

            rows.Add(new KeyValuePair<int, short[]>(bucket, weights));
        }

        return PerceptronModelWriter.Compile(HashBits, 5.4f, 0.001f, FeatureExtractor.FeatureSetVersion, rows, compress);
    }
}
