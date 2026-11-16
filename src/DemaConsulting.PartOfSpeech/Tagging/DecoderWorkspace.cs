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
///         before it takes the workspace and runs no caller code while it holds it, so nested use cannot corrupt it. It erases
///         the workspace (<see cref="Clear"/>) before it returns or lets an exception go, so no text of a request stays in it.
///     </para>
///     <para>
///         The class is plain data for <see cref="FeatureExtractor"/> and <see cref="TagDecoder"/>; it is not
///         thread-safe and must be used by one thread at a time.
///     </para>
/// </remarks>
internal sealed class DecoderWorkspace
{
    /// <summary>
    ///     The most buckets that one token can have without a group (see <see cref="FeatureExtractor.MaxFeatures"/>).
    /// </summary>
    public const int MaxFeatures = FeatureExtractor.MaxFeatures;

    /// <summary>
    ///     The groups of context features that this workspace extracts (set by <see cref="Configure"/>).
    /// </summary>
    public FeatureGroups Groups;

    /// <summary>
    ///     The lexicon that the groups read (set by <see cref="Configure"/>), or <c>null</c>.
    /// </summary>
    public Lexicon? Lexicon;

    /// <summary>
    ///     The number of slots that the tag-independent buckets of one token take in <see cref="StaticBuckets"/>.
    /// </summary>
    public int StaticStride = FeatureExtractor.StaticFeatureCount;

    /// <summary>
    ///     Whether <see cref="LexKnown"/> and <see cref="LexMask"/> hold the lexicon facts of the tokens of the current sentence.
    /// </summary>
    public bool LexCached;

    /// <summary>
    ///     Whether the preview features are active in the current decoding pass (see <see cref="FeatureExtractor.StartPass"/>).
    /// </summary>
    public bool PreviewActive;

    /// <summary>
    ///     The clause state before the next token (bit 0: a subject-like tag, bit 1: a verb tag since the last clause boundary).
    /// </summary>
    public int ClauseState;

    /// <summary>
    ///     The index of the first token that is not function-only, or the token count when there is none (position cues).
    /// </summary>
    public int FirstContent;

    /// <summary>
    ///     Whether the lower-cased token is in the lexicon (1) or not (0), for every token; valid when <see cref="LexCached"/> is set.
    /// </summary>
    public byte[] LexKnown = Array.Empty<byte>();

    /// <summary>
    ///     The tag mask that the lexicon lists for every token (0 when it is not listed); valid when <see cref="LexCached"/> is set.
    /// </summary>
    public int[] LexMask = Array.Empty<int>();

    /// <summary>
    ///     The <see cref="TokenClass"/> of every token (clause cues).
    /// </summary>
    public byte[] Classes = Array.Empty<byte>();

    /// <summary>
    ///     Whether a token starts a new clause by itself (1) or not (0), for every token (clause cues).
    /// </summary>
    public byte[] ClauseBoundary = Array.Empty<byte>();

    /// <summary>
    ///     The stem value of every token, 0 for none (inflection).
    /// </summary>
    public byte[] StemValue = Array.Empty<byte>();

    /// <summary>
    ///     The tags of the first decoding pass for every token (right preview); the second pass reads them.
    /// </summary>
    public int[] Preview = Array.Empty<int>();

    /// <summary>
    ///     Holds, for every token, its folded characters followed by its lower-cased characters.
    /// </summary>
    public char[] Chars = new char[256];

    /// <summary>
    ///     The number of characters of <see cref="Chars"/> in use.
    /// </summary>
    public int CharsUsed;

    /// <summary>
    ///     The end of the area of <see cref="Chars"/> that was written behind <see cref="CharsUsed"/> (the candidate spellings of the inflection lookups), so that <see cref="Clear"/> reaches it.
    /// </summary>
    public int CharsHigh;

    /// <summary>
    ///     The number of bytes of <see cref="LemmaBytes"/> in use.
    /// </summary>
    public int LemmaUsed;

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
    ///     The tag-independent buckets of every token, <see cref="StaticStride"/> slots each (<see cref="FeatureExtractor.StaticFeatureCount"/> without a group).
    /// </summary>
    public int[] StaticBuckets = new int[32 * FeatureExtractor.StaticFeatureCount];

    /// <summary>
    ///     The number of tag-independent buckets that every token really has.
    /// </summary>
    public int[] StaticCount = new int[32];

    /// <summary>
    ///     The buckets of the token that is being scored.
    /// </summary>
    public int[] Buckets = new int[FeatureExtractor.BucketCapacity];

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
    ///     The number of bytes at the start of <see cref="LexiconScratch"/> that the lookups of the current request may have written (the key
    ///     and the spellings decoded while scanning a block), so that <see cref="Clear"/> reaches all of it and no more.
    /// </summary>
    /// <remarks>
    ///     Every lookup in the scratch buffer must go through <see cref="TryFindKey"/> or <see cref="TryGetTags"/>, which raise this mark.
    /// </remarks>
    public int LexiconHigh;

    /// <summary>
    ///     The UTF-8 bytes of the base word that the word lookup compares with the base words of the analyses (see <see cref="TagDecoder"/>).
    /// </summary>
    /// <remarks>
    ///     It has the size of the longest base word that the lexicon format allows, so it never grows and is filled for
    ///     one lookup only.
    /// </remarks>
    public byte[] LemmaBytes = new byte[Lexicon.MaxLemmaBytes];

    /// <summary>
    ///     Selects the groups of context features and the lexicon they read, before a sentence is prepared.
    /// </summary>
    /// <remarks>
    ///     Call this before <see cref="FeatureExtractor.Prepare"/> (or <see cref="Begin"/>) for every sentence whose groups
    ///     or lexicon may differ from the previous use of the workspace; it is cheap and allocates only when the groups need
    ///     more room than the workspace has. With <see cref="FeatureGroups.None"/> the workspace behaves exactly as before
    ///     the groups existed (a workspace that was never configured is such a workspace).
    /// </remarks>
    /// <param name="groups">The groups.</param>
    /// <param name="lexicon">The lexicon for the lexicon facts of the groups, or <c>null</c> to treat every token as not in the lexicon.</param>
    public void Configure(FeatureGroups groups, Lexicon? lexicon)
    {
        Groups = groups;
        Lexicon = lexicon;
        StaticStride = FeatureExtractor.StaticStrideFor(groups);
        LexCached = false;
        PreviewActive = false;
        ClauseState = 0;
        if (StaticBuckets.Length < Start.Length * StaticStride)
        {
            StaticBuckets = new int[Start.Length * StaticStride];
        }

        if (groups != FeatureGroups.None && lexicon != null)
        {
            EnsureLexiconScratch(lexicon.ScratchSize);
        }
    }

    /// <summary>
    ///     Sets the tags of the first decoding pass that the preview features of the second pass read.
    /// </summary>
    /// <param name="tags">The first-pass tag of every token as <see cref="Upos"/> ordinal; at least <see cref="Count"/> elements.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tags"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="tags"/> has fewer elements than the sentence has tokens.</exception>
    public void SetPreview(int[] tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        if (tags.Length < Count)
        {
            throw new ArgumentException($"Expected at least {Count} preview tags but got {tags.Length}.", nameof(tags));
        }

        EnsureGroupArrays(Count);
        Array.Copy(tags, Preview, Count);
    }

    /// <summary>
    ///     Makes the per-token arrays of the groups large enough for a sentence.
    /// </summary>
    /// <param name="count">The number of tokens.</param>
    public void EnsureGroupArrays(int count)
    {
        if (Classes.Length >= count)
        {
            return;
        }

        var size = Math.Max(count, Classes.Length * 2);
        LexKnown = new byte[size];
        LexMask = new int[size];
        Classes = new byte[size];
        ClauseBoundary = new byte[size];
        StemValue = new byte[size];
        Preview = new int[size];
    }

    /// <summary>
    ///     Resets the workspace for a sentence and makes the per-token arrays large enough.
    /// </summary>
    /// <param name="count">The number of tokens of the sentence.</param>
    public void Begin(int count)
    {
        Count = count;
        CharsUsed = 0;
        CharsHigh = 0;
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
            StaticBuckets = new int[size * StaticStride];
            StaticCount = new int[size];
        }
    }

    /// <summary>
    ///     Erases everything that the current request left in the workspace, so that no text of it (nor anything derived from the text) stays in memory.
    /// </summary>
    /// <remarks>
    ///     The decoder calls this in a <c>finally</c> block before it returns or lets an exception go, so it must not throw and must not
    ///     allocate. It zeroes only the part of every array that the request used (the arrays keep their capacity), so its cost is proportional to the
    ///     sentence and not to the largest sentence the thread has seen. That includes the lexicon scratch buffer, which can be far larger than one
    ///     lookup needs: only the range up to <see cref="LexiconHigh"/> is zeroed. The few small arrays of fixed size (buckets and scores) are zeroed in full.
    ///     The workspace is configured again before the next use.
    /// </remarks>
    public void Clear()
    {
        Array.Clear(Chars, 0, Math.Min(Chars.Length, Math.Max(CharsUsed, CharsHigh)));
        var tokens = Math.Min(Count, Start.Length);
        Array.Clear(Start, 0, tokens);
        Array.Clear(Length, 0, tokens);
        Array.Clear(Flags, 0, tokens);
        Array.Clear(LowerHash, 0, tokens);
        Array.Clear(WordHash, 0, tokens);
        Array.Clear(Shape, 0, tokens);
        Array.Clear(StaticCount, 0, tokens);
        Array.Clear(StaticBuckets, 0, Math.Min(tokens * StaticStride, StaticBuckets.Length));

        var grouped = Math.Min(Count, Classes.Length);
        Array.Clear(LexKnown, 0, grouped);
        Array.Clear(LexMask, 0, grouped);
        Array.Clear(Classes, 0, grouped);
        Array.Clear(ClauseBoundary, 0, grouped);
        Array.Clear(StemValue, 0, grouped);
        Array.Clear(Preview, 0, grouped);

        Array.Clear(Buckets, 0, Buckets.Length);
        Array.Clear(Scores, 0, Scores.Length);
        Array.Clear(Real, 0, Real.Length);
        Array.Clear(LexiconScratch, 0, Math.Min(LexiconHigh, LexiconScratch.Length));
        Array.Clear(LemmaBytes, 0, Math.Min(LemmaUsed, LemmaBytes.Length));

        Count = 0;
        CharsUsed = 0;
        CharsHigh = 0;
        LexiconHigh = 0;
        LemmaUsed = 0;
        FirstAlphanumeric = -1;
        FirstContent = 0;
        LexCached = false;
        PreviewActive = false;
        ClauseState = 0;
    }

    /// <summary>
    ///     Looks up a lower-cased key of <see cref="Chars"/> in a lexicon and records what the lookup wrote to <see cref="LexiconScratch"/>.
    /// </summary>
    /// <remarks>
    ///     This is the one door to the lexicon scratch buffer, so that <see cref="LexiconHigh"/> always covers what <see cref="Clear"/> must erase.
    ///     The scratch buffer must already be large enough (see <see cref="EnsureLexiconScratch"/>).
    /// </remarks>
    /// <param name="lexicon">The lexicon.</param>
    /// <param name="offset">The index in <see cref="Chars"/> of the first key character.</param>
    /// <param name="length">The number of key characters.</param>
    /// <param name="ordinal">Receives the position of the spelling, -1 when it is not in the lexicon.</param>
    /// <param name="tags">Receives the tags of the spelling; the empty set when it is not in the lexicon.</param>
    /// <param name="keyLength">Receives the number of UTF-8 bytes of the key, which stays at the start of <see cref="LexiconScratch"/> when the result is <c>true</c>.</param>
    /// <returns><c>true</c> if the spelling is in the lexicon.</returns>
    public bool TryFindKey(Lexicon lexicon, int offset, int length, out int ordinal, out UposSet tags, out int keyLength) =>
        lexicon.TryFindKey(Chars, offset, length, LexiconScratch, ref LexiconHigh, out ordinal, out tags, out keyLength);

    /// <summary>
    ///     Looks up a lower-cased key of <see cref="Chars"/> in a lexicon for its tags only (see <see cref="TryFindKey"/>).
    /// </summary>
    /// <param name="lexicon">The lexicon.</param>
    /// <param name="offset">The index in <see cref="Chars"/> of the first key character.</param>
    /// <param name="length">The number of key characters.</param>
    /// <param name="tags">Receives the tags of the spelling; the empty set when it is not in the lexicon.</param>
    /// <returns><c>true</c> if the spelling is in the lexicon.</returns>
    public bool TryGetTags(Lexicon lexicon, int offset, int length, out UposSet tags) =>
        TryFindKey(lexicon, offset, length, out _, out tags, out _);

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
