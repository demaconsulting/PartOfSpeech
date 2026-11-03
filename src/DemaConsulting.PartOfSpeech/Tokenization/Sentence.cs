namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     One sentence of a text: its position in the source string and its tokens.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="Text"/> is the part of the source string from the first character of the first token to the
///         last character of the last token. The offsets of the tokens are offsets into the <em>whole source
///         string</em> (not into <see cref="Text"/>); <see cref="Start"/> is the offset of the sentence itself, so a
///         token offset relative to the sentence is <c>token.Start - sentence.Start</c>.
///     </para>
///     <para>
///         Instances are created by <see cref="Tokenizer"/> only. They are immutable, so they can be shared between
///         threads.
///     </para>
/// </remarks>
public sealed class Sentence
{
    /// <summary>
    ///     The tokens in source order.
    /// </summary>
    private readonly Token[] _tokens;

    /// <summary>
    ///     The read-only view of the tokens that callers see (a caller cannot cast it back to a writable array).
    /// </summary>
    private readonly IReadOnlyList<Token> _view;

    /// <summary>
    ///     Initializes a new instance of the <see cref="Sentence"/> class.
    /// </summary>
    /// <param name="source">The source string that the offsets refer to.</param>
    /// <param name="tokens">The tokens of the sentence in source order; at least one.</param>
    internal Sentence(string source, Token[] tokens)
    {
        _tokens = tokens;
        _view = Array.AsReadOnly(tokens);
        Start = tokens.Length == 0 ? 0 : tokens[0].Start;
        var last = tokens.Length == 0 ? default : tokens[tokens.Length - 1];
        Length = tokens.Length == 0 ? 0 : last.Start + last.Length - Start;
        Text = source.Substring(Start, Length);
    }

    /// <summary>
    ///     Gets the text of the sentence, from its first to its last token character.
    /// </summary>
    public string Text { get; }

    /// <summary>
    ///     Gets the offset of the sentence in the source string.
    /// </summary>
    public int Start { get; }

    /// <summary>
    ///     Gets the number of characters of the sentence (the length of <see cref="Text"/>).
    /// </summary>
    public int Length { get; }

    /// <summary>
    ///     Gets the tokens in source order; their offsets index the source string, not <see cref="Text"/>.
    /// </summary>
    public IReadOnlyList<Token> Tokens => _view;

    /// <summary>
    ///     Gets the tokens as the array that backs <see cref="Tokens"/> (not copied; callers must not modify it).
    /// </summary>
    internal Token[] TokenArray => _tokens;
}
