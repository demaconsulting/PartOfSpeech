namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     One token of a text: its characters and where they are in the source string.
/// </summary>
/// <remarks>
///     The text is exactly the source characters (never normalized or case-folded), and
///     <see cref="Start"/> indexes the string that was given to the tokenizer, so
///     <c>source.Substring(token.Start, token.Length) == token.Text</c> always holds. The struct is immutable and
///     therefore thread-safe.
/// </remarks>
public readonly struct Token : IEquatable<Token>
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="Token"/> struct.
    /// </summary>
    /// <param name="text">The characters of the token.</param>
    /// <param name="start">The offset of the first character in the source string.</param>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="start"/> is negative.</exception>
    public Token(string text, int start)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (start < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(start), start, "The start offset must not be negative.");
        }

        Text = text;
        Start = start;
    }

    /// <summary>
    ///     Gets the characters of the token, exactly as they are in the source string.
    /// </summary>
    public string Text { get; }

    /// <summary>
    ///     Gets the offset of the first character of the token in the source string.
    /// </summary>
    public int Start { get; }

    /// <summary>
    ///     Gets the number of characters of the token (the length of <see cref="Text"/>).
    /// </summary>
    public int Length => Text?.Length ?? 0;

    /// <summary>
    ///     Determines whether two tokens have the same text and offset.
    /// </summary>
    /// <param name="left">The first token.</param>
    /// <param name="right">The second token.</param>
    /// <returns><c>true</c> if the tokens are equal.</returns>
    public static bool operator ==(Token left, Token right) => left.Equals(right);

    /// <summary>
    ///     Determines whether two tokens differ in text or offset.
    /// </summary>
    /// <param name="left">The first token.</param>
    /// <param name="right">The second token.</param>
    /// <returns><c>true</c> if the tokens are not equal.</returns>
    public static bool operator !=(Token left, Token right) => !left.Equals(right);

    /// <inheritdoc />
    public bool Equals(Token other) => Start == other.Start && string.Equals(Text, other.Text, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Token other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => unchecked((((Text?.GetHashCode() ?? 0) * 397) ^ Start));

    /// <summary>
    ///     Formats the token for diagnostics.
    /// </summary>
    /// <returns>The text followed by the offset, for example <c>cat@4</c>.</returns>
    public override string ToString() => $"{Text}@{Start}";
}
