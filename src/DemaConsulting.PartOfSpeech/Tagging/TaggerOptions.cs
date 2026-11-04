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
    ///     Initializes a new instance of the <see cref="TaggerOptions"/> class with the default settings (three alternatives per token).
    /// </summary>
    public TaggerOptions()
    {
        // Nothing to do: every setting has its default as a property initializer
    }

    /// <summary>
    ///     Gets the maximum number of alternative tags that each token reports.
    /// </summary>
    /// <remarks>
    ///     A token reports the smaller of this number and the number of its allowed tags minus one; the chosen tag is
    ///     never counted. The probabilities of the alternatives are those of the softmax that also gives the confidence,
    ///     so the confidence and all alternatives add up to 1 only when this number is not smaller than the number of
    ///     allowed tags minus one (an unknown word has 17 allowed tags, so that needs
    ///     <see cref="MaxAlternativesLimit"/>); with 0 no alternatives are reported and the confidence stands alone.
    ///     See [Confidence and alternatives](../../DemaConsulting.PartOfSpeech.md#confidence-and-alternatives).
    /// </remarks>
    /// <value>From 0 to 16; the default is 3. A tagger rejects other values with an <see cref="ArgumentOutOfRangeException"/> when it is created.</value>
    public int MaxAlternatives { get; init; } = 3;
}
