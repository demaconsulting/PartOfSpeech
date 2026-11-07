using System.Reflection;

namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     Lazy, thread-safe access to the models that are embedded in the assembly.
/// </summary>
/// <remarks>
///     <para>
///         The compiled lexicon (<c>Resources/lexicon.bin</c>) and the trained weights
///         (<c>Resources/weights.bin</c>, written by the Trainer tool) are both embedded; a package without either
///         resource is broken, so a missing resource is an error and never a silent fallback. Each model is parsed
///         once on first use (about a megabyte of inflate and validation work for each), never at assembly load, so a
///         program that only tokenizes pays nothing.
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
    ///     The logical name of the embedded weights resource (fixed in the project file, independent of namespaces).
    /// </summary>
    public const string WeightsResourceName = "DemaConsulting.PartOfSpeech.Resources.weights.bin";

    /// <summary>
    ///     The lazily loaded lexicon.
    /// </summary>
    private static readonly Lazy<Lexicon> LazyLexicon = new(LoadLexicon, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    ///     The lazily loaded weights.
    /// </summary>
    private static readonly Lazy<PerceptronModel> LazyWeights = new(LoadWeights, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    ///     Gets the embedded lexicon, loading it on first access.
    /// </summary>
    /// <exception cref="InvalidOperationException">The assembly has no lexicon resource.</exception>
    /// <exception cref="InvalidDataException">The embedded lexicon is corrupt.</exception>
    public static Lexicon Lexicon => LazyLexicon.Value;

    /// <summary>
    ///     Gets the embedded weights, loading them on first access.
    /// </summary>
    /// <exception cref="InvalidOperationException">The assembly has no weights resource.</exception>
    /// <exception cref="InvalidDataException">The embedded weights are corrupt or from another feature set.</exception>
    public static PerceptronModel Weights => LazyWeights.Value;

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
    ///     Loads the weights resource.
    /// </summary>
    /// <returns>The model.</returns>
    private static PerceptronModel LoadWeights()
    {
        using var stream = OpenResource(WeightsResourceName)
            ?? throw new InvalidOperationException($"The embedded resource '{WeightsResourceName}' is missing from the assembly.");
        return PerceptronModel.Load(stream);
    }
}
