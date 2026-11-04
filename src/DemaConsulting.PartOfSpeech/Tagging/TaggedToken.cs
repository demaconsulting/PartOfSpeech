namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     A token with its predicted tag, a confidence and ranked alternatives.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="Confidence"/> is the probability of <see cref="Tag"/> among the tags that were allowed for the
///         token, computed with a softmax at the model temperature. When the lexicon and the shape rules leave only one
///         tag, the confidence is 1.0 whatever the model thinks: it measures the choice between the allowed tags, so
///         read it on tokens that had several. The full explanation, including when the confidence and the
///         alternatives add up to 1 and why this reference gives no threshold, is in
///         [Confidence and alternatives](../DemaConsulting.PartOfSpeech.md#confidence-and-alternatives); the words
///         softmax, temperature and raw score are defined in
///         [Terminology](../DemaConsulting.PartOfSpeech.md#terminology).
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
    ///     Gets the probability of <see cref="Tag"/> among the allowed tags, from 0 to 1.
    /// </summary>
    /// <remarks>
    ///     It is a softmax over the tags that the lexicon, the shape rules and any constraint allowed for the token, at
    ///     the temperature of the model; it is exactly 1.0 when only one tag was allowed. Together with the
    ///     probabilities in <see cref="Alternatives"/> it adds up to 1 only when
    ///     <see cref="TaggerOptions.MaxAlternatives"/> did not cut any alternative off. This reference recommends no
    ///     threshold: what confidence is low enough to double-check a token is the choice of your application. See
    ///     [Confidence and alternatives](../../DemaConsulting.PartOfSpeech.md#confidence-and-alternatives) for the
    ///     details and [Terminology](../../DemaConsulting.PartOfSpeech.md#terminology) for the words used.
    /// </remarks>
    /// <seealso cref="Alternatives"/>
    /// <seealso cref="TagScore"/>
    public double Confidence { get; }

    /// <summary>
    ///     Gets the other allowed tags in descending probability, at most <see cref="TaggerOptions.MaxAlternatives"/>.
    /// </summary>
    /// <remarks>
    ///     The list never contains <see cref="Tag"/> itself. It has the smaller of
    ///     <see cref="TaggerOptions.MaxAlternatives"/> and the number of allowed tags minus one entries, so it is
    ///     complete (and <see cref="Confidence"/> plus all probabilities add up to 1) only when
    ///     <see cref="TaggerOptions.MaxAlternatives"/> is large enough; it is empty when only one tag was allowed or when
    ///     no alternatives were requested. See
    ///     [Confidence and alternatives](../../DemaConsulting.PartOfSpeech.md#confidence-and-alternatives).
    /// </remarks>
    /// <seealso cref="Confidence"/>
    /// <seealso cref="TagScore"/>
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
