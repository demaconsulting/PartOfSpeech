namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     How strictly a <see cref="Glossary"/> entry decides the tag of the head word of a matched term.
/// </summary>
/// <remarks>
///     The mode is chosen per glossary with <see cref="GlossaryOptions.Mode"/> and may be overridden per entry with
///     <see cref="GlossaryEntry.WithMode(GlossaryMode)"/>. Hard mode, the default, trusts the entries completely: a
///     wrong entry forces a wrong tag. It fits curated lists, such as the technical names and verbs of ASD-STE100.
///     <see cref="Soft"/> mode is for lists that are not curated or may be wrong, because the model can still disagree when it
///     is sure. With wrong entries, hard mode makes confident errors, while soft mode stays near the accuracy of tagging
///     without a glossary.
/// </remarks>
public enum GlossaryMode
{
    /// <summary>
    ///     The tag of the head word is limited to the tags of the entry (see <see cref="TokenConstraint.Only(UposSet)"/>); the model cannot disagree.
    /// </summary>
    Hard = 0,

    /// <summary>
    ///     The tags of the entry get a boost of the score (see <see cref="TokenConstraint.Prefer"/>) so that the model decides only when it is clearly sure of another tag.
    /// </summary>
    Soft = 1,
}

/// <summary>
///     Settings of a <see cref="Glossary"/> that apply to every entry that does not say otherwise.
/// </summary>
/// <remarks>
///     A glossary copies the values when it is created, so changing an options object afterwards has no effect. The
///     values are validated when the <see cref="GlossaryBuilder"/> or the glossary is created, not when a property is set.
///     Instances are not thread-safe while you modify them, but nothing shares them: every glossary keeps its own copy.
/// </remarks>
/// <example>
///     Create a glossary whose entries are case-sensitive and soft by default:
///     <code>
///     var options = new GlossaryOptions
///     {
///         CaseSensitive = true,
///         Mode = GlossaryMode.Soft,
///     };
///     Glossary glossary = Glossary.Create(new[] { new GlossaryEntry("LRU", Upos.Noun) }, options);
///     </code>
/// </example>
public sealed class GlossaryOptions
{
    /// <summary>
    ///     The default of <see cref="SoftStrength"/>, in units of the model temperature.
    /// </summary>
    /// <remarks>
    ///     The value is the largest strength of a grid of strengths whose paired 95 percent interval of the overall
    ///     accuracy change still reaches zero (its upper bound is at least zero) and whose confident noun-verb swaps do not
    ///     rise, when 30 percent of the entries are wrong and every term is covered. A
    ///     stronger value trusts the glossary more.
    /// </remarks>
    public const double DefaultSoftStrength = 2.0;

    /// <summary>
    ///     Initializes a new instance of the <see cref="GlossaryOptions"/> class with the default settings
    ///     (case-insensitive, hard mode, inflection on, <see cref="DefaultSoftStrength"/>).
    /// </summary>
    public GlossaryOptions()
    {
        // Nothing to do: every setting has its default as a property initializer
    }

    /// <summary>
    ///     Gets a value that indicates whether terms are matched with their exact letter case, for every entry that does not say otherwise.
    /// </summary>
    /// <remarks>
    ///     <c>false</c> (the default) lets a term at the start of a sentence match when it is capitalized. Acronyms such
    ///     as <c>IT</c> or <c>US</c> whose lower-case spellings are other words need a case-sensitive entry
    ///     (<see cref="GlossaryEntry.WithCaseSensitive(bool)"/>). A case-sensitive and a case-insensitive entry for the
    ///     same term may coexist; where both match, the case-sensitive one wins.
    /// </remarks>
    /// <value>The default is <c>false</c>.</value>
    public bool CaseSensitive { get; init; }

    /// <summary>
    ///     Gets the mode of every entry that does not say otherwise.
    /// </summary>
    /// <remarks>
    ///     Hard mode trusts the entries completely: a wrong entry forces a wrong tag. It fits curated lists such as the
    ///     ASD-STE100 technical names and verbs. Use soft mode for lists that are not curated or may be wrong: with
    ///     wrong entries, hard mode makes confident errors, while soft mode stays near the accuracy of tagging without a
    ///     glossary.
    /// </remarks>
    /// <value>The default is <see cref="GlossaryMode.Hard"/>. Another value than <see cref="GlossaryMode.Hard"/> and <see cref="GlossaryMode.Soft"/> is rejected with an <see cref="ArgumentOutOfRangeException"/> when the glossary is created.</value>
    public GlossaryMode Mode { get; init; }

    /// <summary>
    ///     Gets the strength of a soft boost, in units of the model temperature.
    /// </summary>
    /// <remarks>
    ///     A soft entry adds <c>SoftStrength</c> times the temperature of the tagger's model to the raw score of each
    ///     of its tags. Because the unit is the temperature, the value means the same shift of the log-odds for every
    ///     weights file: 1.0 multiplies the odds of the preferred tags by about 2.7, 2.0 by about 7.4. See
    ///     [Terminology](../DemaConsulting.PartOfSpeech.md#terminology) for raw score and temperature. Only soft
    ///     entries use it. The shift actually applied is limited to the range 1e-9 to 1e9 raw score units, so an extremely
    ///     small or large (but valid) strength never fails at tagging time; the limits change no result a double can show.
    /// </remarks>
    /// <value>Finite and greater than zero; the default is <see cref="DefaultSoftStrength"/>. Other values are rejected with an <see cref="ArgumentOutOfRangeException"/> when the glossary is created.</value>
    public double SoftStrength { get; init; } = DefaultSoftStrength;

    /// <summary>
    ///     Gets a value that indicates whether the inflected forms of the head word of every term are matched too, for every entry that does not say otherwise.
    /// </summary>
    /// <remarks>
    ///     On by default, because a glossary lists lemmas (<c>valve</c>, <c>isolate</c>) while text has
    ///     <c>valves</c>, <c>isolated</c>, <c>isolating</c>. The rules cover regular English noun plurals and verb forms
    ///     of the head word only; irregular forms are listed with <see cref="GlossaryEntry.WithVariants(string[])"/>.
    ///     The generated forms are described on <see cref="Glossary"/>.
    /// </remarks>
    /// <value>The default is <c>true</c>.</value>
    public bool Inflect { get; init; } = true;

    /// <summary>
    ///     Validates options and returns a private copy, so that a caller cannot change them afterwards.
    /// </summary>
    /// <param name="options">The options or <c>null</c> for the defaults.</param>
    /// <returns>A validated copy.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The mode is undefined or the strength is not finite and greater than zero.</exception>
    internal static GlossaryOptions CopyOf(GlossaryOptions? options)
    {
        // Validate every value that the matcher relies on; a bad value must fail here, never inside the tagging path
        var source = options ?? new GlossaryOptions();
        if (source.Mode != GlossaryMode.Hard && source.Mode != GlossaryMode.Soft)
        {
            throw new ArgumentOutOfRangeException(nameof(options), source.Mode, "The mode must be Hard or Soft.");
        }

        if (double.IsNaN(source.SoftStrength) || double.IsInfinity(source.SoftStrength) || source.SoftStrength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), source.SoftStrength, "SoftStrength must be finite and greater than zero.");
        }

        return new GlossaryOptions
        {
            CaseSensitive = source.CaseSensitive,
            Mode = source.Mode,
            SoftStrength = source.SoftStrength,
            Inflect = source.Inflect,
        };
    }
}
