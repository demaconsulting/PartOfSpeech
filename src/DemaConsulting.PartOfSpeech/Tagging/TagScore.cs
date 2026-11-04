namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     A tag together with its probability, used for the alternatives of a token.
/// </summary>
/// <remarks>
///     The probability is a softmax over the tags that were allowed for the token, at the temperature of the model, so
///     it is relative to those tags only; it is the same kind of number as <see cref="TaggedToken.Confidence"/> and
///     follows the same rules. They are explained once in
///     [Confidence and alternatives](../DemaConsulting.PartOfSpeech.md#confidence-and-alternatives), and the words
///     softmax and temperature are defined in [Terminology](../DemaConsulting.PartOfSpeech.md#terminology). The struct
///     is immutable and thread-safe; two scores are equal when both the tag and the probability are equal.
/// </remarks>
/// <seealso cref="TaggedToken.Alternatives"/>
public readonly struct TagScore : IEquatable<TagScore>
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="TagScore"/> struct.
    /// </summary>
    /// <remarks>
    ///     The tagger creates these values itself; the constructor does not check its arguments.
    /// </remarks>
    /// <param name="tag">The tag.</param>
    /// <param name="probability">The probability of the tag, from 0 to 1.</param>
    public TagScore(Upos tag, double probability)
    {
        Tag = tag;
        Probability = probability;
    }

    /// <summary>
    ///     Gets the tag.
    /// </summary>
    public Upos Tag { get; }

    /// <summary>
    ///     Gets the probability of the tag among the allowed tags, from 0 to 1.
    /// </summary>
    /// <remarks>
    ///     See [Confidence and alternatives](../../DemaConsulting.PartOfSpeech.md#confidence-and-alternatives) for what
    ///     the probability is relative to and when the probabilities of a token add up to 1.
    /// </remarks>
    public double Probability { get; }

    /// <summary>
    ///     Determines whether two scores are equal.
    /// </summary>
    /// <param name="left">The first score.</param>
    /// <param name="right">The second score.</param>
    /// <returns><c>true</c> if tag and probability are equal.</returns>
    public static bool operator ==(TagScore left, TagScore right) => left.Equals(right);

    /// <summary>
    ///     Determines whether two scores differ.
    /// </summary>
    /// <param name="left">The first score.</param>
    /// <param name="right">The second score.</param>
    /// <returns><c>true</c> if tag or probability differ.</returns>
    public static bool operator !=(TagScore left, TagScore right) => !left.Equals(right);

    /// <summary>
    ///     Determines whether this score has the same tag and probability as another score.
    /// </summary>
    /// <param name="other">The score to compare with.</param>
    /// <returns><c>true</c> if the tags are equal and the probabilities are equal.</returns>
    public bool Equals(TagScore other) => Tag == other.Tag && Probability.CompareTo(other.Probability) == 0;

    /// <summary>
    ///     Determines whether this score is equal to an object.
    /// </summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><c>true</c> if <paramref name="obj"/> is a <see cref="TagScore"/> with the same tag and probability.</returns>
    public override bool Equals(object? obj) => obj is TagScore other && Equals(other);

    /// <summary>
    ///     Gets a hash code for the score.
    /// </summary>
    /// <returns>A hash code of the tag and the probability; equal scores have equal hash codes.</returns>
    public override int GetHashCode() => unchecked(((int)Tag * 397) ^ Probability.GetHashCode());

    /// <summary>
    ///     Formats the score for diagnostics.
    /// </summary>
    /// <returns>For example <c>NOUN:0.9100</c>.</returns>
    public override string ToString() => $"{Tag.ToTagString()}:{Probability.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)}";
}
