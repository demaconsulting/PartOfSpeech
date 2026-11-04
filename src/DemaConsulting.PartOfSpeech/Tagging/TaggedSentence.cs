namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     A sentence whose tokens carry tags.
/// </summary>
/// <remarks>
///     The positions mirror <see cref="Sentence"/>: <see cref="Start"/> and the token offsets index the source string
///     that was tagged. Instances are immutable and may be shared between threads.
/// </remarks>
public sealed class TaggedSentence
{
    /// <summary>
    ///     The read-only view of the tagged tokens that callers see (a caller cannot cast it back to a writable array).
    /// </summary>
    private readonly IReadOnlyList<TaggedToken> _view;

    /// <summary>
    ///     The shared empty list of glossary matches.
    /// </summary>
    private static readonly IReadOnlyList<GlossaryMatch> NoMatches = Array.AsReadOnly(new GlossaryMatch[0]);

    /// <summary>
    ///     The glossary matches of the sentence in text order, or <c>null</c> when there are none.
    /// </summary>
    private readonly IReadOnlyList<GlossaryMatch>? _glossaryMatches;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TaggedSentence"/> class.
    /// </summary>
    /// <param name="sentence">The sentence that was tagged.</param>
    /// <param name="tokens">The tagged tokens, one per token of the sentence.</param>
    /// <param name="glossaryMatches">The glossary matches in text order, or <c>null</c> for none.</param>
    internal TaggedSentence(Sentence sentence, TaggedToken[] tokens, IReadOnlyList<GlossaryMatch>? glossaryMatches = null)
    {
        Text = sentence.Text;
        Start = sentence.Start;
        Length = sentence.Length;
        _view = Array.AsReadOnly(tokens);
        _glossaryMatches = glossaryMatches is { Count: > 0 } ? glossaryMatches : null;
    }

    /// <summary>
    ///     Gets the text of the sentence.
    /// </summary>
    public string Text { get; }

    /// <summary>
    ///     Gets the offset of the sentence in the source string.
    /// </summary>
    public int Start { get; }

    /// <summary>
    ///     Gets the number of characters of the sentence.
    /// </summary>
    public int Length { get; }

    /// <summary>
    ///     Gets the tagged tokens in source order; their offsets index the source string, not <see cref="Text"/>.
    /// </summary>
    public IReadOnlyList<TaggedToken> Tokens => _view;

    /// <summary>
    ///     Gets the glossary terms that were found in the sentence, in text order.
    /// </summary>
    /// <remarks>
    ///     The list is empty when the sentence was tagged without a glossary or no term occurs in it. Each match tells
    ///     which entry was found, which tokens it covers (indexes into <see cref="Tokens"/>) and which of them is the head
    ///     word. The matches never overlap. See <see cref="Glossary"/>.
    /// </remarks>
    /// <value>Empty when there is no match.</value>
    public IReadOnlyList<GlossaryMatch> GlossaryMatches => _glossaryMatches ?? NoMatches;
}
