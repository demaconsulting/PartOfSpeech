namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     A tag together with its probability, used for the alternatives of a token.
/// </summary>
/// <remarks>
///     The probability is calibrated by the temperature of the model and normalized over the tags that were allowed
///     for the token. The struct is immutable and thread-safe.
/// </remarks>
public readonly struct TagScore : IEquatable<TagScore>
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="TagScore"/> struct.
    /// </summary>
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

    /// <inheritdoc />
    public bool Equals(TagScore other) => Tag == other.Tag && Probability.CompareTo(other.Probability) == 0;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is TagScore other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => unchecked(((int)Tag * 397) ^ Probability.GetHashCode());

    /// <summary>
    ///     Formats the score for diagnostics.
    /// </summary>
    /// <returns>For example <c>NOUN:0.9100</c>.</returns>
    public override string ToString() => $"{Tag.ToTagString()}:{Probability.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)}";
}
