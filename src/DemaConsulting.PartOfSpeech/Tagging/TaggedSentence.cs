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
    ///     Initializes a new instance of the <see cref="TaggedSentence"/> class.
    /// </summary>
    /// <param name="sentence">The sentence that was tagged.</param>
    /// <param name="tokens">The tagged tokens, one per token of the sentence.</param>
    internal TaggedSentence(Sentence sentence, TaggedToken[] tokens)
    {
        Text = sentence.Text;
        Start = sentence.Start;
        Length = sentence.Length;
        _view = Array.AsReadOnly(tokens);
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
}
