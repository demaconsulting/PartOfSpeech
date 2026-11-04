namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     The scratch memory of one thread for matching a glossary against a sentence.
/// </summary>
/// <remarks>
///     <para>
///         Matching must not allocate per token, so everything that it needs lives here and is reused: the folded
///         spellings of the tokens of the sentence (for each token the exact folded form followed by the lower-case
///         form, in one character buffer), their lengths and hashes, and the matches that were found. The arrays only
///         ever grow, so after a short warm-up a thread matches without allocating.
///     </para>
///     <para>
///         A workspace belongs to one thread at a time (<see cref="GlossaryIndex.GetThreadWorkspace"/> hands out a
///         per-thread instance). It is plain data like <see cref="DecoderWorkspace"/>: not thread-safe, and its contents
///         are only valid until the next match on the same workspace, so a caller copies the matches out before it runs
///         any code that might match again.
///     </para>
/// </remarks>
internal sealed class GlossaryWorkspace
{
    /// <summary>
    ///     Holds, for every token, its exact folded characters followed by its lower-case folded characters.
    /// </summary>
    public char[] Chars = new char[512];

    /// <summary>
    ///     The index in <see cref="Chars"/> of the exact folded characters of every token (the lower-case copy follows them).
    /// </summary>
    public int[] Start = new int[32];

    /// <summary>
    ///     The length of the folded form of every token (the exact and the lower-case form have the same length).
    /// </summary>
    public int[] Length = new int[32];

    /// <summary>
    ///     The hash of the lower-case folded form of every token.
    /// </summary>
    public ulong[] Hash = new ulong[32];

    /// <summary>
    ///     The form index of every match found.
    /// </summary>
    public int[] MatchForm = new int[32];

    /// <summary>
    ///     The index of the first token of every match found.
    /// </summary>
    public int[] MatchFirst = new int[32];

    /// <summary>
    ///     The number of tokens of every match found.
    /// </summary>
    public int[] MatchCount = new int[32];

    /// <summary>
    ///     The number of matches found.
    /// </summary>
    public int MatchTotal;

    /// <summary>
    ///     Prepares the workspace for a sentence: grows the token arrays when needed and clears the matches.
    /// </summary>
    /// <param name="tokenCount">The number of tokens of the sentence.</param>
    public void Begin(int tokenCount)
    {
        if (Start.Length < tokenCount)
        {
            var capacity = Math.Max(tokenCount, Start.Length * 2);
            Start = new int[capacity];
            Length = new int[capacity];
            Hash = new ulong[capacity];
            MatchForm = new int[capacity];
            MatchFirst = new int[capacity];
            MatchCount = new int[capacity];
        }

        MatchTotal = 0;
    }

    /// <summary>
    ///     Makes sure that the character buffer has room for a number of characters from a position on.
    /// </summary>
    /// <remarks>
    ///     The buffer is replaced by a larger copy that keeps the characters written so far, so the start offsets that
    ///     were recorded remain valid.
    /// </remarks>
    /// <param name="position">The first position that will be written.</param>
    /// <param name="needed">The number of characters that will be written from <paramref name="position"/> on.</param>
    public void EnsureChars(int position, int needed)
    {
        var required = position + needed;
        if (Chars.Length >= required)
        {
            return;
        }

        var larger = new char[Math.Max(required, Chars.Length * 2)];
        Array.Copy(Chars, larger, position);
        Chars = larger;
    }

    /// <summary>
    ///     Records a match.
    /// </summary>
    /// <remarks>
    ///     Matches never overlap, so there are never more matches than tokens and the arrays that
    ///     <see cref="Begin"/> sized for the tokens are large enough.
    /// </remarks>
    /// <param name="form">The index of the matched form.</param>
    /// <param name="first">The index of the first token.</param>
    /// <param name="count">The number of tokens.</param>
    public void AddMatch(int form, int first, int count)
    {
        MatchForm[MatchTotal] = form;
        MatchFirst[MatchTotal] = first;
        MatchCount[MatchTotal] = count;
        MatchTotal++;
    }
}
