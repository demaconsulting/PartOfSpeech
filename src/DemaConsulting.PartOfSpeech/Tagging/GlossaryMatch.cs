namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     One occurrence of a glossary term in a sentence.
/// </summary>
/// <remarks>
///     <para>
///         A match tells which entry was found (so that an application can show its definition), which tokens of the
///         sentence it covers, which of them is the head word, and where the matched text is in the source string. The
///         token indexes are positions in the <see cref="Sentence.Tokens"/> (and in the
///         <see cref="TaggedSentence.Tokens"/>) of the sentence that was searched; <see cref="Start"/> and
///         <see cref="Length"/> index the source string like <see cref="Token.Start"/>, so
///         <c>source.Substring(match.Start, match.Length) == match.Text</c> for a sentence of <see cref="Tokenizer"/>.
///     </para>
///     <para>
///         <strong>Thread safety:</strong> instances are immutable and may be used from any number of threads.
///     </para>
/// </remarks>
/// <example>
///     Find the terms of a glossary in a sentence without tagging it, and read what was matched:
///     <code>
///     Glossary glossary = Glossary.Create(new[]
///     {
///         new GlossaryEntry("bleed valve", Upos.Noun, "A valve that releases trapped air."),
///     });
///     Sentence sentence = Tokenizer.Tokenize("Close the bleed valves.")[0];
///
///     foreach (GlossaryMatch match in glossary.FindMatches(sentence))
///     {
///         // bleed valves: tokens 2 to 3, head token 3, an inflected form (not the term as written)
///         Console.WriteLine($"{match.Text}: tokens {match.FirstToken}-{match.FirstToken + match.TokenCount - 1}, head {match.HeadToken}, variant {match.IsVariant}");
///         Console.WriteLine(match.Entry.Definition);
///     }
///     </code>
/// </example>
public sealed class GlossaryMatch
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="GlossaryMatch"/> class.
    /// </summary>
    /// <param name="entry">The matched entry.</param>
    /// <param name="entryIndex">The index of the entry in its glossary.</param>
    /// <param name="tagSetId">The identifier of the tag set of the matched form (the head tags; derived for an inflected form).</param>
    /// <param name="firstToken">The index of the first token.</param>
    /// <param name="tokenCount">The number of tokens.</param>
    /// <param name="headToken">The index of the head token.</param>
    /// <param name="start">The offset of the first character.</param>
    /// <param name="length">The number of characters.</param>
    /// <param name="text">The matched source text.</param>
    /// <param name="mode">The effective mode.</param>
    /// <param name="isVariant">Whether an inflected or listed variant was matched.</param>
    /// <param name="headStart">The offset of the head token in the source string.</param>
    internal GlossaryMatch(
        GlossaryEntry entry,
        int entryIndex,
        int tagSetId,
        int firstToken,
        int tokenCount,
        int headToken,
        int start,
        int length,
        string text,
        GlossaryMode mode,
        bool isVariant,
        int headStart)
    {
        Entry = entry;
        EntryIndex = entryIndex;
        TagSetId = tagSetId;
        FirstToken = firstToken;
        TokenCount = tokenCount;
        HeadToken = headToken;
        Start = start;
        Length = length;
        Text = text;
        Mode = mode;
        IsVariant = isVariant;
        HeadStart = headStart;
    }

    /// <summary>
    ///     Gets the matched entry, with its term, tags and definition.
    /// </summary>
    public GlossaryEntry Entry { get; }

    /// <summary>
    ///     Gets the index in the tokens of the sentence of the first token of the match.
    /// </summary>
    public int FirstToken { get; }

    /// <summary>
    ///     Gets the number of tokens that the match covers.
    /// </summary>
    /// <remarks>
    ///     It is the number of tokens of the matched spelling, which is the number of tokens of the term; only a term
    ///     that ends in a period and is matched at the end of a text can cover one token more (<c>etc</c> and <c>.</c>).
    /// </remarks>
    public int TokenCount { get; }

    /// <summary>
    ///     Gets the index in the tokens of the sentence of the head token, the token that received the tags of the entry.
    /// </summary>
    public int HeadToken { get; }

    /// <summary>
    ///     Gets the offset of the first character of the match in the source string.
    /// </summary>
    public int Start { get; }

    /// <summary>
    ///     Gets the number of characters from the start of the first token to the end of the last token, including any white space between them.
    /// </summary>
    public int Length { get; }

    /// <summary>
    ///     Gets the matched source text, exactly as written (not case-folded).
    /// </summary>
    public string Text { get; }

    /// <summary>
    ///     Gets the effective mode of the matched entry: its own mode, or the mode of the glossary.
    /// </summary>
    public GlossaryMode Mode { get; }

    /// <summary>
    ///     Gets a value that indicates whether an inflected form or a listed variant was matched instead of the term as written.
    /// </summary>
    public bool IsVariant { get; }

    /// <summary>
    ///     Gets the index of the matched entry in <see cref="Glossary.Entries"/>.
    /// </summary>
    internal int EntryIndex { get; }

    /// <summary>
    ///     Gets the identifier of the tag set of the matched form in its glossary.
    /// </summary>
    internal int TagSetId { get; }

    /// <summary>
    ///     Gets the offset in the source string of the head token (tokens do not know their index).
    /// </summary>
    internal int HeadStart { get; }

    /// <summary>
    ///     Formats the match for diagnostics.
    /// </summary>
    /// <returns>For example <c>bleed valves@6 [bleed valve]</c>.</returns>
    public override string ToString() => $"{Text}@{Start} [{Entry.Term}]";
}
