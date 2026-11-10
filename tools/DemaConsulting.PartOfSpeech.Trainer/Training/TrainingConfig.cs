namespace DemaConsulting.PartOfSpeech.Trainer;

/// <summary>
///     How the candidate tags of a token are limited while the perceptron is trained.
/// </summary>
/// <remarks>
///     The runtime decoder only chooses among the tags that the lexicon and the token-shape rules allow. The modes say
///     how much of that restriction the training sees; the best mode is selected on the development sets.
/// </remarks>
internal enum RestrictionMode
{
    /// <summary>
    ///     Mode A (the spike): the guess is the best tag of all 17; the restriction is applied only at run time.
    /// </summary>
    None = 0,

    /// <summary>
    ///     Mode B: the update uses the unrestricted guess, the history takes the restricted guess (what the decoder would write).
    /// </summary>
    History = 1,

    /// <summary>
    ///     Mode C: the guess is the best allowed tag, for the update and for the history, exactly as at run time. The
    ///     gold tag counts as allowed when the lexicon misses it, so that the update stays defined.
    /// </summary>
    Full = 2,
}

/// <summary>
///     Which previous tags enter the history features of the next token while training.
/// </summary>
internal enum HistoryMode
{
    /// <summary>
    ///     The tag that the current weights guessed (the spike and the runtime decoder; no train and decode drift).
    /// </summary>
    Predicted = 0,

    /// <summary>
    ///     The gold tag of the previous tokens (teacher forcing); a masked token has no gold tag and uses the guess.
    /// </summary>
    Gold = 1,
}

/// <summary>
///     One training corpus: where it is, how often it is repeated and which of its tokens give a training signal.
/// </summary>
internal sealed class SourceConfig
{
    /// <summary>
    ///     Gets or sets the name that experiments use to set the weight of the source on the command line.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the repository-relative path of the corpus file (forward slashes).
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets how many times the sentences of the source enter every epoch; 0 leaves the source out.
    /// </summary>
    public int Weight { get; set; } = 1;

    /// <summary>
    ///     Gets or sets a value indicating whether tokens whose status is <c>disputed</c> are masked (no update, history only).
    /// </summary>
    public bool MaskDisputed { get; set; } = true;

    /// <summary>
    ///     Gets or sets the smallest model probability in percent that a silver token needs; a token below it is masked.
    ///     0 keeps every labeled token. Tokens whose source tag carries no probability are not affected.
    /// </summary>
    public int MinSilverProbability { get; set; }

    /// <summary>
    ///     Gets or sets the share of the sentences to use in permille (1 to 1000); a sentence is selected by the hash of its id, so the subset is stable.
    /// </summary>
    public int SamplePermille { get; set; } = 1000;

    /// <summary>
    ///     Gets or sets the largest number of sentences to use (after sampling, in file order); 0 means no limit.
    /// </summary>
    public int MaxSentences { get; set; }

    /// <summary>
    ///     Creates a copy of the source settings.
    /// </summary>
    /// <returns>The copy.</returns>
    public SourceConfig Clone() => (SourceConfig)MemberwiseClone();
}

/// <summary>
///     The complete, committed description of one training run: with the corpora and the library code, it determines
///     the weights file byte for byte.
/// </summary>
/// <remarks>
///     The file form is <c>training.json</c> (see <c>ConfigFile</c>); this class holds the values and validates them
///     so that the engine never meets a setting that it cannot honor. Every numeric setting is an integer or a value
///     that the weights file stores exactly: the weight scale is given as its reciprocal <see cref="QuantScale"/>
///     (a power of two), and the temperature is not fitted by the training but chosen once on the development sets.
/// </remarks>
internal sealed class TrainingConfig
{
    /// <summary>
    ///     The largest number of epochs that is accepted.
    /// </summary>
    public const int MaxEpochs = 100;

    /// <summary>
    ///     The default number of jackknife folds of the <c>rightPreview</c> group.
    /// </summary>
    public const int DefaultPreviewFolds = 5;

    /// <summary>
    ///     The names of the feature groups in the order of the bits of <see cref="FeatureGroups"/>: <c>positionCues</c> (F1),
    ///     <c>clauseCues</c> (F2), <c>rightPreview</c> (F3), <c>inflection</c> (F4) and <c>titleCaseLine</c> (F5).
    /// </summary>
    public static readonly IReadOnlyList<string> FeatureGroupNames = new[] { "positionCues", "clauseCues", "rightPreview", "inflection", "titleCaseLine" };

    /// <summary>
    ///     The group names that the harness uses as short names, in the same order as <see cref="FeatureGroupNames"/>.
    /// </summary>
    private static readonly string[] FeatureGroupShortNames = { "F1", "F2", "F3", "F4", "F5" };

    /// <summary>
    ///     Gets or sets the corpora to train on.
    /// </summary>
    public List<SourceConfig> Sources { get; set; } = new();

    /// <summary>
    ///     Gets or sets the number of passes over the shuffled sentences.
    /// </summary>
    public int Epochs { get; set; } = 10;

    /// <summary>
    ///     Gets or sets the exponent of the bucket space (18 to 24).
    /// </summary>
    public int HashBits { get; set; } = 21;

    /// <summary>
    ///     Gets or sets the number of times a tag-independent feature must occur in the training signal to be trained; 1 trains every feature.
    /// </summary>
    public int MinCount { get; set; } = 1;

    /// <summary>
    ///     Gets or sets the largest quantized weight magnitude that is dropped after averaging; 0 drops nothing but exact zeros.
    /// </summary>
    public int PruneThreshold { get; set; }

    /// <summary>
    ///     Gets or sets the quantization factor <c>S</c>: the stored integer is <c>round(S * averaged weight)</c> and the
    ///     file scale is <c>1 / S</c>. Must be a power of two so that the scale is exact in the 32-bit header field.
    /// </summary>
    public int QuantScale { get; set; } = 16;

    /// <summary>
    ///     Gets or sets the seed of the shuffle generator.
    /// </summary>
    public long Seed { get; set; } = 20260101;

    /// <summary>
    ///     Gets or sets the restriction mode of the training.
    /// </summary>
    public RestrictionMode Restriction { get; set; } = RestrictionMode.None;

    /// <summary>
    ///     Gets or sets the history mode of the training.
    /// </summary>
    public HistoryMode History { get; set; } = HistoryMode.Predicted;

    /// <summary>
    ///     Gets or sets the names of the context feature groups that the model uses (<see cref="FeatureGroupNames"/>); empty is the shipped
    ///     feature set. The file form leaves the member out while it is empty.
    /// </summary>
    public List<string> Features { get; set; } = new();

    /// <summary>
    ///     Gets or sets the number of jackknife folds of the <c>rightPreview</c> group (2 to 10); used only with that group.
    /// </summary>
    public int PreviewFolds { get; set; } = DefaultPreviewFolds;

    /// <summary>
    ///     Gets or sets the softmax temperature stored in the weights file (chosen by <c>calibrate</c>, not by the training).
    /// </summary>
    public double Temperature { get; set; } = 1.0;

    /// <summary>
    ///     Gets or sets the expected SHA-256 (lower-case hexadecimal) of the uncompressed weights image; empty until a run is committed.
    /// </summary>
    public string ImageSha256 { get; set; } = string.Empty;

    /// <summary>
    ///     Gets the weights-file scale: the reciprocal of <see cref="QuantScale"/> (exact, because that is a power of two).
    /// </summary>
    public float Scale => 1f / QuantScale;

    /// <summary>
    ///     Creates a deep copy, so that an experiment can change settings without touching the loaded configuration.
    /// </summary>
    /// <returns>The copy.</returns>
    public TrainingConfig Clone()
    {
        var copy = (TrainingConfig)MemberwiseClone();
        copy.Sources = Sources.Select(s => s.Clone()).ToList();
        copy.Features = Features == null ? new List<string>() : new List<string>(Features);
        return copy;
    }

    /// <summary>
    ///     Resolves the configured group names to the group flags of the feature extractor.
    /// </summary>
    /// <returns>The groups; <see cref="FeatureGroups.None"/> for the shipped feature set.</returns>
    /// <exception cref="InvalidDataException">A name is unknown or repeated.</exception>
    public FeatureGroups ResolveGroups() =>
        TryResolveGroups(Features, out var groups, out var error)
            ? groups
            : throw new InvalidDataException("Invalid training configuration: " + error + ".");

    /// <summary>
    ///     Resolves group names (the canonical camel-case names, or <c>F1</c> to <c>F4</c>; case is ignored) to group flags.
    /// </summary>
    /// <param name="names">The names, or <c>null</c> (invalid).</param>
    /// <param name="groups">Receives the groups when the names are valid.</param>
    /// <param name="error">Receives what is wrong when they are not.</param>
    /// <returns><c>true</c> when every name is known and none is given twice (by either spelling).</returns>
    public static bool TryResolveGroups(IEnumerable<string>? names, out FeatureGroups groups, out string error)
    {
        groups = FeatureGroups.None;
        error = string.Empty;
        if (names == null)
        {
            error = "features must be a list of group names";
            return false;
        }

        foreach (var name in names)
        {
            var index = name == null ? -1 : IndexOfGroup(name);
            if (index < 0)
            {
                error = $"features names an unknown group '{name}' (known: {string.Join(", ", FeatureGroupNames)}, or F1 to F5)";
                groups = FeatureGroups.None;
                return false;
            }

            var flag = (FeatureGroups)(1u << index);
            if ((groups & flag) != 0)
            {
                error = $"features names the group '{FeatureGroupNames[index]}' twice";
                groups = FeatureGroups.None;
                return false;
            }

            groups |= flag;
        }

        return true;
    }

    /// <summary>
    ///     Gets the canonical names of a set of groups in the order of <see cref="FeatureGroupNames"/>.
    /// </summary>
    /// <param name="groups">The groups.</param>
    /// <returns>The names; empty for <see cref="FeatureGroups.None"/>.</returns>
    public static List<string> NamesOf(FeatureGroups groups)
    {
        var names = new List<string>();
        for (var i = 0; i < FeatureGroupNames.Count; i++)
        {
            if (((uint)groups & (1u << i)) != 0)
            {
                names.Add(FeatureGroupNames[i]);
            }
        }

        return names;
    }

    /// <summary>
    ///     Parses the value of the <c>--features</c> option.
    /// </summary>
    /// <param name="text"><c>none</c> (no group), or group names separated by commas.</param>
    /// <returns>The canonical names in the order of <see cref="FeatureGroupNames"/>, so that the order of the option does not matter.</returns>
    /// <exception cref="ArgumentException">A name is unknown or repeated, or <c>none</c> is combined with a name.</exception>
    public static List<string> ParseFeatureList(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var parts = text.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        if (parts.Count == 0)
        {
            throw new ArgumentException("--features expects none or a comma-separated list of group names.");
        }

        if (parts.Any(p => string.Equals(p, "none", StringComparison.OrdinalIgnoreCase)))
        {
            return parts.Count == 1
                ? new List<string>()
                : throw new ArgumentException("--features: none cannot be combined with group names.");
        }

        return TryResolveGroups(parts, out var groups, out var error)
            ? NamesOf(groups)
            : throw new ArgumentException("--features: " + error + ".");
    }

    /// <summary>
    ///     Finds the group that a name stands for.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>The index into <see cref="FeatureGroupNames"/>, or -1.</returns>
    private static int IndexOfGroup(string name)
    {
        for (var i = 0; i < FeatureGroupNames.Count; i++)
        {
            if (string.Equals(name, FeatureGroupNames[i], StringComparison.OrdinalIgnoreCase) || string.Equals(name, FeatureGroupShortNames[i], StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    ///     Checks every setting and throws a message that names the offending one.
    /// </summary>
    /// <exception cref="InvalidDataException">A setting is out of range or inconsistent.</exception>
    public void Validate()
    {
        Require(Epochs >= 1 && Epochs <= MaxEpochs, $"epochs must be between 1 and {MaxEpochs}");
        Require(HashBits >= PerceptronModel.MinHashBits && HashBits <= PerceptronModel.MaxHashBits, "hashBits must be between 18 and 24");
        Require(MinCount >= 1, "minCount must be at least 1");
        Require(PruneThreshold >= 0 && PruneThreshold < 32767, "pruneThreshold must be between 0 and 32766");
        Require(QuantScale >= 1 && QuantScale <= 4096 && (QuantScale & (QuantScale - 1)) == 0, "quantScale must be a power of two from 1 to 4096");
        Require(Seed >= 0, "seed must not be negative");
        Require(Temperature > 0 && Temperature < 1000 && !double.IsNaN(Temperature), "temperature must be greater than zero and below 1000");
        Require(Enum.IsDefined(typeof(RestrictionMode), Restriction), "restriction is not a known mode");
        Require(Enum.IsDefined(typeof(HistoryMode), History), "history is not a known mode");
        Require(PreviewFolds >= 2 && PreviewFolds <= 10, "previewFolds must be between 2 and 10");
        Require(TryResolveGroups(Features, out _, out var groupError), groupError);
        Require(Sources.Count > 0, "at least one source is needed");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in Sources)
        {
            Require(!string.IsNullOrEmpty(source.Name) && names.Add(source.Name), "every source needs a unique name");
            Require(!string.IsNullOrEmpty(source.Path), $"source '{source.Name}' needs a path");
            Require(source.Weight >= 0 && source.Weight <= 16, $"the weight of source '{source.Name}' must be between 0 and 16");
            Require(source.MinSilverProbability >= 0 && source.MinSilverProbability <= 100, $"minSilverProbability of '{source.Name}' must be between 0 and 100");
            Require(source.SamplePermille >= 1 && source.SamplePermille <= 1000, $"samplePermille of '{source.Name}' must be between 1 and 1000");
            Require(source.MaxSentences >= 0, $"maxSentences of '{source.Name}' must not be negative");
        }

        Require(Sources.Any(s => s.Weight > 0), "at least one source needs a weight above zero");
    }

    /// <summary>
    ///     Throws when a condition does not hold.
    /// </summary>
    /// <param name="condition">The condition that must hold.</param>
    /// <param name="message">What is wrong when it does not.</param>
    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException("Invalid training configuration: " + message + ".");
        }
    }
}
