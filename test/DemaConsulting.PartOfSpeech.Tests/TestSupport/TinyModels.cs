using System.Text;

namespace DemaConsulting.PartOfSpeech.Tests;

/// <summary>
///     Builds small lexicon and weights files in memory so that tests can drive the decoder with models whose every
///     number is known.
/// </summary>
/// <remarks>
///     The weights are addressed through <see cref="FeatureExtractor.ReferenceBucket"/>, so a test says "the lower-case
///     word <c>cat</c> adds 5 to NOUN" without knowing any hash. All helpers are stateless.
/// </remarks>
internal static class TinyModels
{
    /// <summary>
    ///     The bucket space exponent of tiny models (large enough that the features of a test word do not collide with each other).
    /// </summary>
    public const int HashBits = 20;

    /// <summary>
    ///     Compiles a lexicon from rows of the form <c>spelling TAG TAG</c>.
    /// </summary>
    /// <param name="rows">The rows; the spellings are sorted into the order that the format requires.</param>
    /// <returns>The lexicon file.</returns>
    public static byte[] CreateLexicon(params string[] rows)
    {
        var entries = new List<KeyValuePair<string, UposSet>>();
        foreach (var row in rows)
        {
            var parts = row.Split(' ');
            var tags = UposSet.Empty;
            foreach (var part in parts.Skip(1))
            {
                Assert.True(UposExtensions.TryParse(part, out var tag), "unknown tag " + part);
                tags = tags.Add(tag);
            }

            entries.Add(new KeyValuePair<string, UposSet>(parts[0], tags));
        }

        return CreateLexicon(entries);
    }

    /// <summary>
    ///     Compiles a lexicon from entries in any order.
    /// </summary>
    /// <param name="entries">The spellings and tags.</param>
    /// <param name="compress">Whether to Deflate the payload.</param>
    /// <returns>The lexicon file.</returns>
    public static byte[] CreateLexicon(IEnumerable<KeyValuePair<string, UposSet>> entries, bool compress = false)
    {
        var sorted = entries
            .Select(e => new { e.Key, e.Value, Bytes = Encoding.UTF8.GetBytes(e.Key) })
            .OrderBy(e => e.Bytes, Comparer<byte[]>.Create((x, y) => BinaryFormat.Compare(x, 0, x.Length, y, 0, y.Length)))
            .Select(e => new KeyValuePair<string, UposSet>(e.Key, e.Value))
            .ToList();
        return LexiconWriter.Compile(sorted, compress);
    }

    /// <summary>
    ///     Creates a tagger from in-memory model files.
    /// </summary>
    /// <param name="weights">The weights file.</param>
    /// <param name="lexicon">The lexicon file, or <c>null</c> for the embedded lexicon.</param>
    /// <param name="options">The options.</param>
    /// <returns>The tagger.</returns>
    public static Tagger CreateTagger(byte[] weights, byte[]? lexicon = null, TaggerOptions? options = null)
    {
        using var weightStream = new MemoryStream(weights);
        if (lexicon == null)
        {
            return Tagger.Load(weightStream, options);
        }

        using var lexiconStream = new MemoryStream(lexicon);
        return Tagger.Load(weightStream, lexiconStream, options);
    }

    /// <summary>
    ///     Creates a decoder from in-memory model files.
    /// </summary>
    /// <param name="weights">The weights file.</param>
    /// <param name="lexicon">The lexicon file.</param>
    /// <returns>The decoder.</returns>
    public static TagDecoder CreateDecoder(byte[] weights, byte[] lexicon) =>
        new(PerceptronModel.Load(weights, FeatureExtractor.FeatureSetVersion), DemaConsulting.PartOfSpeech.Lexicon.Load(lexicon));

    /// <summary>
    ///     Collects hand-set weights keyed by feature and writes them as a weights file.
    /// </summary>
    public sealed class WeightsBuilder
    {
        /// <summary>
        ///     The weights of every touched bucket.
        /// </summary>
        private readonly Dictionary<int, int[]> _rows = new();

        /// <summary>
        ///     Adds to the weight of one tag for one feature instance.
        /// </summary>
        /// <param name="kind">The feature template.</param>
        /// <param name="a">The first value (see <see cref="FeatureExtractor.ReferenceBucket"/>).</param>
        /// <param name="b">The second value.</param>
        /// <param name="tag">The tag.</param>
        /// <param name="weight">The amount to add (an integer weight).</param>
        /// <returns>This builder.</returns>
        public WeightsBuilder Add(FeatureKind kind, string? a, string? b, Upos tag, int weight)
        {
            var bucket = FeatureExtractor.ReferenceBucket(kind, a, b, HashBits);
            if (!_rows.TryGetValue(bucket, out var row))
            {
                row = new int[UposExtensions.Count];
                _rows[bucket] = row;
            }

            row[(int)tag] += weight;
            return this;
        }

        /// <summary>
        ///     Adds to the weight of a tag for a lower-cased word.
        /// </summary>
        /// <param name="lowerWord">The lower-cased word.</param>
        /// <param name="tag">The tag.</param>
        /// <param name="weight">The amount to add.</param>
        /// <returns>This builder.</returns>
        public WeightsBuilder Word(string lowerWord, Upos tag, int weight)
        {
            // A hand-set weight is only meaningful if no other feature of the word shares its bucket (then it would count twice)
            var bucket = FeatureExtractor.ReferenceBucket(FeatureKind.Lower, lowerWord, null, HashBits);
            var own = FeatureExtractor.ReferenceBuckets(new[] { lowerWord }, 0, "-S-", "-S2-", HashBits).Count(b => b == bucket);
            if (own != 1)
            {
                throw new InvalidOperationException($"The features of '{lowerWord}' collide in the tiny bucket space; pick another word.");
            }

            return Add(FeatureKind.Lower, lowerWord, null, tag, weight);
        }

        /// <summary>
        ///     Adds to the weight of a tag for a given previous tag.
        /// </summary>
        /// <param name="previous">The spelling of the previous tag (<c>NOUN</c>, <c>-S-</c>).</param>
        /// <param name="tag">The tag.</param>
        /// <param name="weight">The amount to add.</param>
        /// <returns>This builder.</returns>
        public WeightsBuilder AfterTag(string previous, Upos tag, int weight) => Add(FeatureKind.PrevTag1, previous, null, tag, weight);

        /// <summary>
        ///     Adds to the weight of a tag for every token (the bias feature).
        /// </summary>
        /// <param name="tag">The tag.</param>
        /// <param name="weight">The amount to add.</param>
        /// <returns>This builder.</returns>
        public WeightsBuilder Bias(Upos tag, int weight) => Add(FeatureKind.Bias, null, null, tag, weight);

        /// <summary>
        ///     Writes the weights file.
        /// </summary>
        /// <param name="temperature">The softmax temperature.</param>
        /// <param name="scale">The weight scale.</param>
        /// <param name="compress">Whether to Deflate the payload.</param>
        /// <returns>The file.</returns>
        public byte[] Build(float temperature = 1f, float scale = 1f, bool compress = false)
        {
            var rows = _rows.Select(r => new KeyValuePair<int, short[]>(r.Key, r.Value.Select(w => (short)w).ToArray()));
            return PerceptronModelWriter.Compile(HashBits, temperature, scale, FeatureExtractor.FeatureSetVersion, rows, compress);
        }
    }
}
