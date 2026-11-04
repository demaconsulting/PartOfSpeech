namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     The reusable per-thread scratch memory of the decoder: the folded characters of a sentence, the tag-independent
///     feature buckets of every token and the score buffers.
/// </summary>
/// <remarks>
///     <para>
///         Tagging a sentence needs a handful of arrays whose size depends on the sentence. Allocating them per call
///         would dominate the cost of a short sentence, so one workspace per thread grows to the largest sentence seen
///         and is then reused for ever (the decoder keeps it in a thread-static field). The decoder resolves caller-supplied lists
///         before it takes the workspace and runs no caller code while it holds it, so nested use cannot corrupt it.
///     </para>
///     <para>
///         The class is plain data for <see cref="FeatureExtractor"/> and <see cref="TagDecoder"/>; it is not
///         thread-safe and must be used by one thread at a time.
///     </para>
/// </remarks>
internal sealed class DecoderWorkspace
{
    /// <summary>
    ///     The most buckets that one token can have (see <see cref="FeatureExtractor.MaxFeatures"/>).
    /// </summary>
    public const int MaxFeatures = FeatureExtractor.MaxFeatures;

    /// <summary>
    ///     Holds, for every token, its folded characters followed by its lower-cased characters.
    /// </summary>
    public char[] Chars = new char[256];

    /// <summary>
    ///     The number of characters of <see cref="Chars"/> in use.
    /// </summary>
    public int CharsUsed;

    /// <summary>
    ///     The number of tokens of the current sentence.
    /// </summary>
    public int Count;

    /// <summary>
    ///     The index of the first token that contains a letter or a digit, or -1.
    /// </summary>
    public int FirstAlphanumeric;

    /// <summary>
    ///     The index in <see cref="Chars"/> of the folded characters of every token (the lower-case copy follows them).
    /// </summary>
    public int[] Start = new int[32];

    /// <summary>
    ///     The length in characters of the folded form of every token (the lower-case form has the same length).
    /// </summary>
    public int[] Length = new int[32];

    /// <summary>
    ///     The facts about every token that the allowed-tag rules need (see <see cref="FeatureExtractor.FlagLetterOrDigit"/>).
    /// </summary>
    public byte[] Flags = new byte[32];

    /// <summary>
    ///     The FNV-1a hash of the lower-cased form of every token.
    /// </summary>
    public uint[] LowerHash = new uint[32];

    /// <summary>
    ///     The hash of the folded (cased) form of every token.
    /// </summary>
    public uint[] WordHash = new uint[32];

    /// <summary>
    ///     The shape bits of every token (see <see cref="FeatureExtractor.ShapeUpper"/> and the other <c>Shape</c> constants).
    /// </summary>
    public byte[] Shape = new byte[32];

    /// <summary>
    ///     The tag-independent buckets of every token, <see cref="FeatureExtractor.StaticFeatureCount"/> slots each.
    /// </summary>
    public int[] StaticBuckets = new int[32 * FeatureExtractor.StaticFeatureCount];

    /// <summary>
    ///     The number of tag-independent buckets that every token really has.
    /// </summary>
    public int[] StaticCount = new int[32];

    /// <summary>
    ///     The buckets of the token that is being scored.
    /// </summary>
    public int[] Buckets = new int[MaxFeatures];

    /// <summary>
    ///     The integer score of every tag for the token that is being scored.
    /// </summary>
    public int[] Scores = new int[UposExtensions.Count];

    /// <summary>
    ///     The real-valued score of every tag, then its unnormalized probability.
    /// </summary>
    public double[] Real = new double[UposExtensions.Count];

    /// <summary>
    ///     The buffer for lexicon lookups; it is sized for the lexicon on first use.
    /// </summary>
    public byte[] LexiconScratch = new byte[0];

    /// <summary>
    ///     Resets the workspace for a sentence and makes the per-token arrays large enough.
    /// </summary>
    /// <param name="count">The number of tokens of the sentence.</param>
    public void Begin(int count)
    {
        Count = count;
        CharsUsed = 0;
        FirstAlphanumeric = -1;

        // Grow geometrically so a long run of larger sentences does not reallocate every time
        if (Start.Length < count)
        {
            var size = Math.Max(count, Start.Length * 2);
            Start = new int[size];
            Length = new int[size];
            Flags = new byte[size];
            LowerHash = new uint[size];
            WordHash = new uint[size];
            Shape = new byte[size];
            StaticBuckets = new int[size * FeatureExtractor.StaticFeatureCount];
            StaticCount = new int[size];
        }
    }

    /// <summary>
    ///     Makes sure that <see cref="Chars"/> can take more characters.
    /// </summary>
    /// <param name="extra">The number of characters about to be added after <see cref="CharsUsed"/>.</param>
    public void EnsureChars(int extra)
    {
        if (CharsUsed + extra > Chars.Length)
        {
            Array.Resize(ref Chars, Math.Max(Chars.Length * 2, CharsUsed + extra));
        }
    }

    /// <summary>
    ///     Makes sure that the lexicon scratch buffer has at least a size.
    /// </summary>
    /// <param name="size">The required size in bytes.</param>
    public void EnsureLexiconScratch(int size)
    {
        if (LexiconScratch.Length < size)
        {
            LexiconScratch = new byte[size];
        }
    }
}
