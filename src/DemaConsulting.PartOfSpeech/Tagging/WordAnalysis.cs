namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     One reading of a word form: the base word (lemma), the Universal POS tag and the grammatical features that the
///     lexicon records for it.
/// </summary>
/// <remarks>
///     <para>
///         For the spelling <c>tests</c> the lexicon has two analyses: <c>test NOUN Number=Plur</c> (the plural of the noun)
///         and <c>test VERB Person=3|Tense=Pres</c> (the third person singular of the verb). Get the analyses of a word from
///         <see cref="WordTags.GetAnalyses"/>, or ask for one reading directly with
///         <see cref="Tagger.IsFormOf(string, string, Upos)"/>.
///     </para>
///     <para>
///         <strong>Lemma:</strong> the base word in lower case and Unicode normalization form C, taken from the sources of the
///         lexicon (<c>go</c> for <c>went</c>, <c>be</c> for <c>is</c>); a spelling that is its own base word has itself as
///         lemma. A proper noun's capitalization is not kept. <strong>Features:</strong> Universal Dependencies notation
///         with names in ascending order, joined by <c>|</c> (<c>Person=3|Tense=Pres</c>), and empty when the sources record
///         none. Only the features that the sources record exist (<c>Degree</c>, <c>Number</c>, <c>Person</c>, <c>Tense</c> and
///         <c>VerbForm</c>), which is a subset of what Universal Dependencies defines; nothing is inferred.
///     </para>
///     <para>
///         The struct is immutable and thread-safe. Two analyses are equal when lemma, tag and features are equal (the
///         comparison is ordinal); <c>default(WordAnalysis)</c> has an empty lemma and empty features.
///     </para>
/// </remarks>
/// <seealso cref="WordTags"/>
public readonly struct WordAnalysis : IEquatable<WordAnalysis>
{
    /// <summary>
    ///     The base word, or <c>null</c> for <c>default</c>.
    /// </summary>
    private readonly string? _lemma;

    /// <summary>
    ///     The features in notation, or <c>null</c> for none.
    /// </summary>
    private readonly string? _features;

    /// <summary>
    ///     Initializes a new instance of the <see cref="WordAnalysis"/> struct.
    /// </summary>
    /// <remarks>
    ///     The lookup creates these values itself, from analyses that were validated when the lexicon was loaded.
    /// </remarks>
    /// <param name="lemma">The base word.</param>
    /// <param name="tag">The tag.</param>
    /// <param name="features">The features in notation, or empty.</param>
    internal WordAnalysis(string lemma, Upos tag, string features)
    {
        _lemma = lemma;
        Tag = tag;
        _features = features;
    }

    /// <summary>
    ///     Gets the base word in lower case, for example <c>go</c> for the form <c>went</c>.
    /// </summary>
    public string Lemma => _lemma ?? string.Empty;

    /// <summary>
    ///     Gets the Universal POS tag of this reading.
    /// </summary>
    public Upos Tag { get; }

    /// <summary>
    ///     Gets the features of this reading in Universal Dependencies notation, for example <c>Person=3|Tense=Pres</c>.
    /// </summary>
    /// <remarks>
    ///     The names are in ascending order and joined by <c>|</c>. The value is empty when the lexicon records no feature
    ///     for this reading; use <see cref="GetFeature(string)"/> to read a single feature.
    /// </remarks>
    public string Features => _features ?? string.Empty;

    /// <summary>
    ///     Determines whether two analyses are equal.
    /// </summary>
    /// <param name="left">The first analysis.</param>
    /// <param name="right">The second analysis.</param>
    /// <returns><c>true</c> if lemma, tag and features are equal.</returns>
    public static bool operator ==(WordAnalysis left, WordAnalysis right) => left.Equals(right);

    /// <summary>
    ///     Determines whether two analyses differ.
    /// </summary>
    /// <param name="left">The first analysis.</param>
    /// <param name="right">The second analysis.</param>
    /// <returns><c>true</c> if lemma, tag or features differ.</returns>
    public static bool operator !=(WordAnalysis left, WordAnalysis right) => !left.Equals(right);

    /// <summary>
    ///     Gets the value of one feature.
    /// </summary>
    /// <remarks>
    ///     The name is case-sensitive, as in Universal Dependencies (<c>Number</c>, not <c>number</c>). The call allocates
    ///     the returned string.
    /// </remarks>
    /// <param name="name">The feature name, for example <c>Number</c>.</param>
    /// <returns>The value, for example <c>Plur</c>; <c>null</c> when this reading has no such feature.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <c>null</c>.</exception>
    public string? GetFeature(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        // Walk the "Name=Value" pairs; a name never contains '=' or '|', so a name that has one cannot match
        var features = Features;
        var position = 0;
        while (position < features.Length)
        {
            var end = features.IndexOf('|', position);
            if (end < 0)
            {
                end = features.Length;
            }

            var separator = features.IndexOf('=', position, end - position);
            if (separator >= 0
                && separator - position == name.Length
                && string.CompareOrdinal(features, position, name, 0, name.Length) == 0)
            {
                return features.Substring(separator + 1, end - separator - 1);
            }

            position = end + 1;
        }

        return null;
    }

    /// <summary>
    ///     Determines whether this analysis equals another one.
    /// </summary>
    /// <param name="other">The analysis to compare with.</param>
    /// <returns><c>true</c> if lemma, tag and features are equal (ordinal comparison).</returns>
    public bool Equals(WordAnalysis other) =>
        Tag == other.Tag
        && string.Equals(Lemma, other.Lemma, StringComparison.Ordinal)
        && string.Equals(Features, other.Features, StringComparison.Ordinal);

    /// <summary>
    ///     Determines whether this analysis is equal to an object.
    /// </summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><c>true</c> if <paramref name="obj"/> is a <see cref="WordAnalysis"/> with the same lemma, tag and features.</returns>
    public override bool Equals(object? obj) => obj is WordAnalysis other && Equals(other);

    /// <summary>
    ///     Gets a hash code for the analysis.
    /// </summary>
    /// <returns>A hash code of lemma, tag and features; equal analyses have equal hash codes.</returns>
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = (int)Tag;
            hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(Lemma);
            return (hash * 397) ^ StringComparer.Ordinal.GetHashCode(Features);
        }
    }

    /// <summary>
    ///     Formats the analysis for diagnostics.
    /// </summary>
    /// <returns>The lemma, the tag and the features separated by spaces, for example <c>test VERB Person=3|Tense=Pres</c>; the features are left out when there are none.</returns>
    public override string ToString() =>
        Features.Length == 0
            ? $"{Lemma} {Tag.ToTagString()}"
            : $"{Lemma} {Tag.ToTagString()} {Features}";
}
