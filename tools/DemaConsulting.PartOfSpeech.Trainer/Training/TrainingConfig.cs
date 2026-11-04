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
        return copy;
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
