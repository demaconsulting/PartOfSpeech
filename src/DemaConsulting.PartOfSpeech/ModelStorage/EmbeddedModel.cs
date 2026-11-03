using System.Reflection;

namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     Lazy, thread-safe access to the models that are embedded in the assembly.
/// </summary>
/// <remarks>
///     <para>
///         The compiled lexicon (<c>Resources/lexicon.bin</c>) is always embedded; the trained weights
///         (<c>Resources/weights.bin</c>) are added by a later release, so <see cref="Weights"/> is <c>null</c> in a
///         build that has none. Each model is parsed once on first use (about a megabyte of inflate and validation
///         work), never at assembly load, so a program that only tokenizes pays nothing.
///     </para>
///     <para>
///         The initialization uses <see cref="Lazy{T}"/> in publication-safe mode: concurrent first accesses run the
///         loader once and all callers receive the same instance. A failed load is remembered and rethrown, which is
///         the right behavior for a resource that cannot change at run time. The loaded models are immutable.
///     </para>
/// </remarks>
internal static class EmbeddedModel
{
    /// <summary>
    ///     The logical name of the embedded lexicon resource (fixed in the project file, independent of namespaces).
    /// </summary>
    public const string LexiconResourceName = "DemaConsulting.PartOfSpeech.Resources.lexicon.bin";

    /// <summary>
    ///     The logical name of the embedded weights resource (absent until a trained model ships).
    /// </summary>
    public const string WeightsResourceName = "DemaConsulting.PartOfSpeech.Resources.weights.bin";

    /// <summary>
    ///     The lazily loaded lexicon.
    /// </summary>
    private static readonly Lazy<Lexicon> LazyLexicon = new(LoadLexicon, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    ///     The lazily loaded weights.
    /// </summary>
    private static readonly Lazy<PerceptronModel?> LazyWeights = new(LoadWeights, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    ///     Gets the embedded lexicon, loading it on first access.
    /// </summary>
    /// <exception cref="InvalidOperationException">The assembly has no lexicon resource.</exception>
    /// <exception cref="InvalidDataException">The embedded lexicon is corrupt.</exception>
    public static Lexicon Lexicon => LazyLexicon.Value;

    /// <summary>
    ///     Gets the embedded weights, loading them on first access.
    /// </summary>
    /// <value>The model, or <c>null</c> if this build embeds no weights.</value>
    /// <exception cref="InvalidDataException">The embedded weights are corrupt or from another feature set.</exception>
    public static PerceptronModel? Weights => LazyWeights.Value;

    /// <summary>
    ///     Opens an embedded resource of this assembly.
    /// </summary>
    /// <param name="name">The logical resource name.</param>
    /// <returns>The stream, or <c>null</c> if the resource does not exist.</returns>
    internal static Stream? OpenResource(string name) => typeof(EmbeddedModel).Assembly.GetManifestResourceStream(name);

    /// <summary>
    ///     Loads the lexicon resource.
    /// </summary>
    /// <returns>The lexicon.</returns>
    private static Lexicon LoadLexicon()
    {
        using var stream = OpenResource(LexiconResourceName)
            ?? throw new InvalidOperationException($"The embedded resource '{LexiconResourceName}' is missing from the assembly.");
        return Lexicon.Load(stream);
    }

    /// <summary>
    ///     Loads the weights resource if it exists.
    /// </summary>
    /// <returns>The model, or <c>null</c> if the resource does not exist.</returns>
    private static PerceptronModel? LoadWeights()
    {
        using var stream = OpenResource(WeightsResourceName);
        return stream == null ? null : PerceptronModel.Load(stream, FeatureExtractor.FeatureSetVersion);
    }
}
