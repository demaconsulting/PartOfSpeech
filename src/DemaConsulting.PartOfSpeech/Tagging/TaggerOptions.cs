namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     Settings of a <see cref="Tagger"/>.
/// </summary>
/// <remarks>
///     The tagger copies the values when it is created, so changing an options object afterwards has no effect.
/// </remarks>
/// <example>
///     Request more alternatives than the default of three, or none at all:
///     <code>
///     var many = new TaggerOptions { MaxAlternatives = TaggerOptions.MaxAlternativesLimit };
///     var none = new TaggerOptions { MaxAlternatives = 0 };
///     </code>
/// </example>
public sealed class TaggerOptions
{
    /// <summary>
    ///     The largest number of alternatives that can be requested (all other tags).
    /// </summary>
    public const int MaxAlternativesLimit = UposExtensions.Count - 1;

    /// <summary>
    ///     Gets the maximum number of alternative tags that each token reports.
    /// </summary>
    /// <value>From 0 to 16; the default is 3. A tagger rejects other values when it is created.</value>
    public int MaxAlternatives { get; init; } = 3;
}
