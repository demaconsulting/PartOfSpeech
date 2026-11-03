namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     A token with its predicted tag, a calibrated confidence and ranked alternatives.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="Confidence"/> is the probability of <see cref="Tag"/> among the tags that were allowed for the
///         token, calibrated by the model temperature. When the lexicon and the shape rules leave only one tag, the
///         confidence is 1.0 whatever the model thinks: it measures the choice between the allowed tags, so read it on
///         tokens that had several.
///     </para>
///     <para>
///         The struct is immutable. The alternatives are a view of one array that the tagger allocated per sentence;
///         the property creates a small wrapper when it is read.
///     </para>
/// </remarks>
/// <example>
///     Read the tag, the confidence and the alternatives of a token, and cut the source string with its offsets:
///     <code>
///     string text = "The cat sat.";
///     TaggedToken token = tagger.Tag(text)[0].Tokens[1];
///
///     Console.WriteLine(token.Text == text.Substring(token.Start, token.Length));   // True
///     Console.WriteLine($"{token.Text}: {token.Tag.ToTagString()} ({token.Confidence:P0})");
///     foreach (TagScore alternative in token.Alternatives)
///     {
///         Console.WriteLine($"  {alternative.Tag.ToTagString()} {alternative.Probability:P0}");
///     }
///     </code>
/// </example>
public readonly struct TaggedToken
{
    /// <summary>
    ///     The empty array behind the alternatives of a token that has none.
    /// </summary>
    private static readonly TagScore[] NoScores = new TagScore[0];

    /// <summary>
    ///     The array that holds the alternatives of this and the other tokens of the sentence.
    /// </summary>
    private readonly TagScore[]? _scores;

    /// <summary>
    ///     The index in <see cref="_scores"/> of the first alternative of this token.
    /// </summary>
    private readonly int _scoreOffset;

    /// <summary>
    ///     The number of alternatives of this token.
    /// </summary>
    private readonly int _scoreCount;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TaggedToken"/> struct.
    /// </summary>
    /// <param name="text">The token text.</param>
    /// <param name="start">The offset of the token in its source string.</param>
    /// <param name="tag">The predicted tag.</param>
    /// <param name="confidence">The probability of the tag.</param>
    /// <param name="scores">The array that holds the alternatives.</param>
    /// <param name="scoreOffset">The index of the first alternative of this token in <paramref name="scores"/>.</param>
    /// <param name="scoreCount">The number of alternatives.</param>
    /// <param name="isConstrained">Whether a constraint was applied to the token.</param>
    internal TaggedToken(string text, int start, Upos tag, double confidence, TagScore[]? scores, int scoreOffset, int scoreCount, bool isConstrained)
    {
        Text = text;
        Start = start;
        Tag = tag;
        Confidence = confidence;
        _scores = scores;
        _scoreOffset = scoreOffset;
        _scoreCount = scoreCount;
        IsConstrained = isConstrained;
    }

    /// <summary>
    ///     Gets the token text, exactly as in the source string.
    /// </summary>
    public string Text { get; }

    /// <summary>
    ///     Gets the offset of the token in the source string.
    /// </summary>
    public int Start { get; }

    /// <summary>
    ///     Gets the number of characters of the token.
    /// </summary>
    public int Length => Text?.Length ?? 0;

    /// <summary>
    ///     Gets the predicted tag.
    /// </summary>
    public Upos Tag { get; }

    /// <summary>
    ///     Gets the probability of <see cref="Tag"/> among the allowed tags, calibrated by the model temperature.
    /// </summary>
    public double Confidence { get; }

    /// <summary>
    ///     Gets the other allowed tags in descending probability, at most <see cref="TaggerOptions.MaxAlternatives"/>.
    /// </summary>
    /// <value>Empty when only one tag was allowed or no alternatives were requested.</value>
    public IReadOnlyList<TagScore> Alternatives =>
        _scores == null ? new ScoreSlice(NoScores, 0, 0) : new ScoreSlice(_scores, _scoreOffset, _scoreCount);

    /// <summary>
    ///     Gets a value that indicates whether a <see cref="TokenConstraint"/> restricted or adjusted this token.
    /// </summary>
    public bool IsConstrained { get; }

    /// <summary>
    ///     Formats the token for diagnostics.
    /// </summary>
    /// <returns>For example <c>cat/NOUN</c>.</returns>
    public override string ToString() => $"{Text}/{Tag.ToTagString()}";
}
