// cspell:ignore Contoso
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
///         alternatives add up to 1 and which thresholds are recommended, is in
///         [Confidence and alternatives](../DemaConsulting.PartOfSpeech.md#confidence-and-alternatives) and
///         [Confidence thresholds](../DemaConsulting.PartOfSpeech.md#confidence-thresholds); the words
///         softmax, temperature and raw score are defined in
///         [Terminology](../DemaConsulting.PartOfSpeech.md#terminology).
///     </para>
///     <para>
///         <see cref="Margin"/> is the confidence minus the probability of the best alternative: how far the chosen
///         tag is ahead of the runner-up. <see cref="IsKnownWord"/> tells whether the lexicon has an entry for the
///         word. A rule checker reads both next to the confidence; see
///         [Using the tagger in a rule checker](../DemaConsulting.PartOfSpeech.md#using-the-tagger-in-a-rule-checker).
///     </para>
///     <para>
///         The struct is immutable. The alternatives are a view of one array that the tagger allocated per sentence;
///         the property creates a small wrapper when it is read. The margin and the known-word flag allocate nothing.
///     </para>
/// </remarks>
/// <example>
///     Read the tag, the confidence, the margin, the known-word flag and the alternatives of a token, and cut the
///     source string with its offsets:
///     <code>
///     string text = "The cat sat.";
///     TaggedToken token = tagger.Tag(text)[0].Tokens[1];
///
///     Console.WriteLine(token.Text == text.Substring(token.Start, token.Length));   // True
///     Console.WriteLine($"{token.Text}: {token.Tag.ToTagString()} ({token.Confidence:P0})");
///     Console.WriteLine($"margin {token.Margin:P0}, known word: {token.IsKnownWord}");
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
    ///     The glossary match that this token belongs to, or <c>null</c>.
    /// </summary>
    private readonly GlossaryMatch? _glossaryMatch;

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
    /// <param name="isKnownWord">Whether the lexicon has an entry for the token.</param>
    /// <param name="glossaryMatch">The glossary match that the token belongs to, or <c>null</c>.</param>
    internal TaggedToken(string text, int start, Upos tag, double confidence, TagScore[]? scores, int scoreOffset, int scoreCount, bool isConstrained, bool isKnownWord, GlossaryMatch? glossaryMatch = null)
    {
        Text = text;
        Start = start;
        Tag = tag;
        Confidence = confidence;
        _scores = scores;
        _scoreOffset = scoreOffset;
        _scoreCount = scoreCount;
        IsConstrained = isConstrained;
        IsKnownWord = isKnownWord;
        _glossaryMatch = glossaryMatch;
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
    ///     the temperature of the model; it is exactly 1.0 when only one tag was allowed (by the lexicon, a hard constraint
    ///     or a hard glossary entry that leaves one tag). A hard glossary entry with several tags, or a soft entry, gives
    ///     the softmax over the tags that remain, so a soft boost raises the confidence of a preferred tag. For a glossary
    ///     token the confidence therefore measures the choice among the tags that the constraints still allow; it is
    ///     <em>not</em> evidence that the entry is correct, and the confidence of a head word that a hard entry limits to
    ///     one tag is always 1.0, also when the entry is wrong. Together with the
    ///     probabilities in <see cref="Alternatives"/> it adds up to 1 only when
    ///     <see cref="TaggerOptions.MaxAlternatives"/> did not cut any alternative off. Measured thresholds for
    ///     the confidence of the embedded model, below which a token should not be trusted, are in
    ///     [Confidence thresholds](../../DemaConsulting.PartOfSpeech.md#confidence-thresholds). See
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
    ///     Gets how far the chosen tag is ahead of the best alternative, from 0 to 1.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The margin is <see cref="Confidence"/> minus the probability of the first entry of
    ///         <see cref="Alternatives"/>, which is the runner-up because the alternatives are in descending order. A margin
    ///         near 0 means a close call between two tags, and a margin near the confidence means that the runner-up is
    ///         negligible. It is 0 when two tags tie.
    ///     </para>
    ///     <para>
    ///         When no alternative is reported (only one tag was allowed, or <see cref="TaggerOptions.MaxAlternatives"/>
    ///         is 0) the margin is the smallest value that the confidence allows, which is
    ///         <c>max(0, 2 * Confidence - 1)</c>: the unknown runner-up has at most the probability
    ///         <c>1 - Confidence</c>. The margin is therefore never larger than the true margin, and it is exactly 1.0
    ///         for a token with one allowed tag (a forced tag has the confidence 1.0). The margin is exact whenever at
    ///         least one alternative is reported, so keep <see cref="TaggerOptions.MaxAlternatives"/> at 1 or more when
    ///         you read it. In every case <c>2 * Confidence - 1 &lt;= Margin &lt;= Confidence</c>.
    ///     </para>
    ///     <para>
    ///         The margin is read from the stored alternatives without creating the list of
    ///         <see cref="Alternatives"/>, so reading it allocates nothing. For a token that a glossary entry or a
    ///         constraint limited to one tag it is 1.0 and says nothing about the entry. The package gives no measured
    ///         limit for the margin; a limit that a checker chooses needs its own measurements. See
    ///         [Confidence and alternatives](../../DemaConsulting.PartOfSpeech.md#confidence-and-alternatives) and
    ///         [Using the tagger in a rule checker](../../DemaConsulting.PartOfSpeech.md#using-the-tagger-in-a-rule-checker).
    ///     </para>
    /// </remarks>
    /// <seealso cref="Confidence"/>
    /// <seealso cref="Alternatives"/>
    /// <seealso cref="IsKnownWord"/>
    public double Margin
    {
        get
        {
            // The runner-up is the first alternative; the array is read directly so that nothing is allocated
            if (_scores != null && _scoreCount > 0)
            {
                return Confidence - _scores[_scoreOffset].Probability;
            }

            // No runner-up is known: it has at most 1 - Confidence, so this is the smallest margin that is possible
            return Math.Max(0.0, (2.0 * Confidence) - 1.0);
        }
    }

    /// <summary>
    ///     Gets a value that indicates whether the lexicon has an entry for the spelling of this token.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         It is the result of the lexicon lookup that the tagger makes anyway to decide the allowed tags, so it is
    ///         free. The lookup key is the token text after the tagger folds it (Unicode normalization and typographic
    ///         quotes and dashes mapped to ASCII) and lower-cases it, so <c>Cat</c>, <c>CAT</c> and <c>cat</c> give the
    ///         same value. The lexicon is the embedded one or the one that was passed to <c>Tagger.Load</c>.
    ///     </para>
    ///     <para>
    ///         <c>false</c> means that the lexicon has no evidence about the word: before any constraint all 17 tags were
    ///         allowed, including for capitalized words, numbers and punctuation that the lexicon does not list. A
    ///         capital letter or a digit never makes a word known. The flag is about the lexicon, not about being a word:
    ///         punctuation and numbers that the lexicon lists are known.
    ///     </para>
    ///     <para>
    ///         The flag does not depend on a glossary or a <see cref="TokenConstraint"/>. A head word of a hard glossary
    ///         entry that the lexicon does not list is <c>false</c> and still has <see cref="IsConstrained"/> and
    ///         <see cref="IsGlossaryHead"/> set and the confidence 1.0. A token that a constraint forced to one tag (the
    ///         confidence 1.0 with <see cref="IsConstrained"/>) is not inconclusive because the lexicon does not list it;
    ///         <see cref="IsConstrained"/> alone does not mean that, because a soft glossary entry or a
    ///         <see cref="TokenConstraint.Prefer"/> leaves the model a choice. A default token has <c>false</c>.
    ///         See [Using the tagger in a rule checker](../../DemaConsulting.PartOfSpeech.md#using-the-tagger-in-a-rule-checker)
    ///         and [Terminology](../../DemaConsulting.PartOfSpeech.md#terminology).
    ///     </para>
    /// </remarks>
    /// <seealso cref="Margin"/>
    /// <seealso cref="IsConstrained"/>
    public bool IsKnownWord { get; }

    /// <summary>
    ///     Gets a value that indicates whether a <see cref="TokenConstraint"/> restricted or adjusted this token.
    /// </summary>
    /// <remarks>
    ///     It is <c>true</c> for a token that a caller constraint or a glossary constrained, and <c>false</c> for a token
    ///     whose constraint was <see cref="TokenConstraint.None"/>, also when the token is inside a glossary match (for
    ///     example the final period of a term such as <c>etc.</c> at the end of a text, which has no constraint).
    /// </remarks>
    public bool IsConstrained { get; }

    /// <summary>
    ///     Gets the glossary match that this token belongs to, or <c>null</c> when the token is not part of a match.
    /// </summary>
    /// <remarks>
    ///     Every token of a matched term reports the same match object; <see cref="IsGlossaryHead"/> tells which of them
    ///     is the head word that received the tags of the entry (the word that takes the entry's tags: by default the last word of the term, the first
    ///     word of a phrasal verb such as <c>shut down</c>). The match is <c>null</c> for every token of a sentence
    ///     that was tagged without a glossary. See <see cref="Glossary"/>.
    /// </remarks>
    public GlossaryMatch? GlossaryMatch => _glossaryMatch;

    /// <summary>
    ///     Gets a value that indicates whether this token is the head word of a glossary match.
    /// </summary>
    /// <remarks>
    ///     The head word is the token that received the tags of the glossary entry (the head of <c>Contoso Hub</c> is
    ///     <c>Hub</c>, the head of <c>shut down</c> is <c>shut</c>). It is <c>false</c> for the other
    ///     tokens of a matched term and for tokens outside any match.
    /// </remarks>
    public bool IsGlossaryHead => _glossaryMatch != null && _glossaryMatch.HeadStart == Start;

    /// <summary>
    ///     Gets a copy of the token that belongs to a glossary match.
    /// </summary>
    /// <param name="match">The match.</param>
    /// <returns>The copy.</returns>
    internal TaggedToken WithGlossaryMatch(GlossaryMatch match) =>
        new(Text, Start, Tag, Confidence, _scores, _scoreOffset, _scoreCount, IsConstrained, IsKnownWord, match);

    /// <summary>
    ///     Formats the token for diagnostics.
    /// </summary>
    /// <returns>For example <c>cat/NOUN</c>.</returns>
    public override string ToString() => $"{Text}/{Tag.ToTagString()}";
}
