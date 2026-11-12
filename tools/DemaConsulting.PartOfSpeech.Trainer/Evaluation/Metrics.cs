namespace DemaConsulting.PartOfSpeech.Trainer;

/// <summary>
///     The judgment of one evaluated token: whether the tagger was right, how confident it was and how much probability it gave to the gold answer.
/// </summary>
internal sealed class Outcome
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="Outcome"/> class.
    /// </summary>
    /// <param name="documentId">The id of the document of the token (the unit of the bootstrap).</param>
    /// <param name="correct">Whether the chosen label equals the gold label.</param>
    /// <param name="confidence">The confidence of the chosen tag.</param>
    /// <param name="goldProbability">The probability that the tagger gave to the gold label; 0 when no candidate has it.</param>
    /// <param name="ambiguous">Whether the lexicon lists more than one tag for the token.</param>
    /// <param name="nounVerb">Whether the gold label is a noun or a verb and both were candidates, so that a noun-verb error was possible.</param>
    /// <param name="harmful">Whether the token is a noun-verb error with a confidence at or above the harmful limit.</param>
    public Outcome(string documentId, bool correct, double confidence, double goldProbability, bool ambiguous, bool nounVerb, bool harmful)
    {
        DocumentId = documentId;
        Correct = correct;
        Confidence = confidence;
        GoldProbability = goldProbability;
        Ambiguous = ambiguous;
        NounVerb = nounVerb;
        Harmful = harmful;
    }

    /// <summary>
    ///     Gets the id of the document of the token.
    /// </summary>
    public string DocumentId { get; }

    /// <summary>
    ///     Gets a value indicating whether the chosen label equals the gold label.
    /// </summary>
    public bool Correct { get; }

    /// <summary>
    ///     Gets the confidence of the chosen tag.
    /// </summary>
    public double Confidence { get; }

    /// <summary>
    ///     Gets the probability that the tagger gave to the gold label (0 when no candidate has it).
    /// </summary>
    public double GoldProbability { get; }

    /// <summary>
    ///     Gets a value indicating whether the lexicon lists more than one tag for the token.
    /// </summary>
    public bool Ambiguous { get; }

    /// <summary>
    ///     Gets a value indicating whether the gold label is a noun or a verb and both were candidates.
    /// </summary>
    public bool NounVerb { get; }

    /// <summary>
    ///     Gets a value indicating whether the token is a noun-verb error at or above the harmful confidence.
    /// </summary>
    public bool Harmful { get; }
}

/// <summary>
///     The summary numbers of a set of outcomes.
/// </summary>
internal sealed class DevMetrics
{
    /// <summary>
    ///     Gets or sets the number of judged tokens.
    /// </summary>
    public int Tokens { get; set; }

    /// <summary>
    ///     Gets or sets the share of correct tokens in percent.
    /// </summary>
    public double Accuracy { get; set; }

    /// <summary>
    ///     Gets or sets the number of tokens with more than one lexicon tag.
    /// </summary>
    public int AmbiguousTokens { get; set; }

    /// <summary>
    ///     Gets or sets the share of correct tokens among the ambiguous ones in percent.
    /// </summary>
    public double AmbiguousAccuracy { get; set; }

    /// <summary>
    ///     Gets or sets the number of noun-verb decisions (gold noun or verb with both as candidates).
    /// </summary>
    public int NounVerbDecisions { get; set; }

    /// <summary>
    ///     Gets or sets the number of harmful errors (noun-verb errors with a confidence at or above the limit).
    /// </summary>
    public int HarmfulErrors { get; set; }

    /// <summary>
    ///     Gets or sets the harmful errors per 1,000 noun-verb decisions.
    /// </summary>
    public double HarmfulPer1000 { get; set; }

    /// <summary>
    ///     Gets or sets the mean negative log-likelihood of the gold label (natural logarithm, probability floor applied).
    /// </summary>
    public double Nll { get; set; }

    /// <summary>
    ///     Gets or sets the expected calibration error in percent (top-label confidence against accuracy over equal-width bins).
    /// </summary>
    public double Ece { get; set; }

    /// <summary>
    ///     Gets or sets the number of tokens whose gold label was not among the candidates.
    /// </summary>
    public int OutsideCandidates { get; set; }
}

/// <summary>
///     Computes the evaluation metrics and the paired bootstrap from token outcomes.
/// </summary>
/// <remarks>
///     <para>
///         The metrics follow the selection protocol of the trainer: accuracy (all tokens and the lexicon-ambiguous
///         ones), the harmful errors (gold noun answered verb or gold verb answered noun with a confidence of at
///         least <see cref="HarmfulConfidence"/>) in total and per 1,000 noun-verb decisions, the negative
///         log-likelihood and the expected calibration error.
///     </para>
///     <para>
///         Stateless and thread-safe.
///     </para>
/// </remarks>
internal static class Metrics
{
    /// <summary>
    ///     The confidence at and above which a noun-verb error counts as harmful.
    /// </summary>
    public const double HarmfulConfidence = 0.45;

    /// <summary>
    ///     The confidence at and above which a wrong answer counts as confidently wrong (a measure of the single-shape tranches
    ///     of the shapes development set).
    /// </summary>
    public const double ConfidentWrongConfidence = 0.95;

    /// <summary>
    ///     Tells whether an outcome is a wrong answer given with a confidence of at least <see cref="ConfidentWrongConfidence"/>.
    /// </summary>
    /// <param name="outcome">The outcome.</param>
    /// <returns><c>true</c> when the answer is wrong and its confidence is at least <see cref="ConfidentWrongConfidence"/>.</returns>
    public static bool IsConfidentlyWrong(Outcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return !outcome.Correct && outcome.Confidence >= ConfidentWrongConfidence;
    }

    /// <summary>
    ///     The lower confidence at and above which a wrong answer is counted as well, beside <see cref="ConfidentWrongConfidence"/>
    ///     (a measure of the technical tranches of the shapes development set and of the calibration measurement).
    /// </summary>
    public const double ConfidentWrong90 = 0.90;

    /// <summary>
    ///     Tells whether an outcome is a wrong answer given with a confidence of at least <see cref="ConfidentWrong90"/>.
    /// </summary>
    /// <param name="outcome">The outcome.</param>
    /// <returns><c>true</c> when the answer is wrong and its confidence is at least <see cref="ConfidentWrong90"/>.</returns>
    public static bool IsConfidentlyWrong90(Outcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return !outcome.Correct && outcome.Confidence >= ConfidentWrong90;
    }

    /// <summary>
    ///     The probability that is used for the gold label when no candidate has it (keeps the log-likelihood finite).
    /// </summary>
    public const double ProbabilityFloor = 1e-6;

    /// <summary>
    ///     The number of equal-width confidence bins of the calibration error.
    /// </summary>
    public const int EceBins = 15;

    /// <summary>
    ///     Computes the metrics of a set of outcomes.
    /// </summary>
    /// <param name="outcomes">The outcomes.</param>
    /// <returns>The metrics; all zero for an empty set.</returns>
    public static DevMetrics Compute(IReadOnlyList<Outcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        var metrics = new DevMetrics { Tokens = outcomes.Count };
        if (outcomes.Count == 0)
        {
            return metrics;
        }

        var correct = 0;
        var ambiguousCorrect = 0;
        var nllSum = 0.0;
        var binCount = new int[EceBins];
        var binCorrect = new int[EceBins];
        var binConfidence = new double[EceBins];
        foreach (var outcome in outcomes)
        {
            correct += outcome.Correct ? 1 : 0;
            if (outcome.Ambiguous)
            {
                metrics.AmbiguousTokens++;
                ambiguousCorrect += outcome.Correct ? 1 : 0;
            }

            metrics.NounVerbDecisions += outcome.NounVerb ? 1 : 0;
            metrics.HarmfulErrors += outcome.Harmful ? 1 : 0;
            metrics.OutsideCandidates += outcome.GoldProbability <= 0 ? 1 : 0;
            nllSum -= Math.Log(Math.Max(outcome.GoldProbability, ProbabilityFloor));
            var bin = Math.Min(EceBins - 1, (int)(outcome.Confidence * EceBins));
            binCount[bin]++;
            binCorrect[bin] += outcome.Correct ? 1 : 0;
            binConfidence[bin] += outcome.Confidence;
        }

        metrics.Accuracy = 100.0 * correct / outcomes.Count;
        metrics.AmbiguousAccuracy = metrics.AmbiguousTokens == 0 ? 0 : 100.0 * ambiguousCorrect / metrics.AmbiguousTokens;
        metrics.HarmfulPer1000 = metrics.NounVerbDecisions == 0 ? 0 : 1000.0 * metrics.HarmfulErrors / metrics.NounVerbDecisions;
        metrics.Nll = nllSum / outcomes.Count;
        var ece = 0.0;
        for (var b = 0; b < EceBins; b++)
        {
            if (binCount[b] > 0)
            {
                ece += binCount[b] * Math.Abs(((double)binCorrect[b] / binCount[b]) - (binConfidence[b] / binCount[b]));
            }
        }

        metrics.Ece = 100.0 * ece / outcomes.Count;
        return metrics;
    }

    /// <summary>
    ///     Judges one token against its gold label.
    /// </summary>
    /// <remarks>
    ///     Labels are compared as strings so that one routine serves the UPOS tags of the open corpus and the coarser
    ///     classes of gold v2: a <paramref name="label"/> function maps a tag to its label. The candidates are the chosen
    ///     tag and its alternatives, so the tagger must have been created with all 16 alternatives. Ambiguity is a fact
    ///     about the lexicon, not about the candidates: the shape rules add tags to punctuation and capitalized words,
    ///     which would make nearly every token look ambiguous.
    /// </remarks>
    /// <param name="documentId">The id of the document of the token.</param>
    /// <param name="goldLabel">The gold label.</param>
    /// <param name="token">The tagged token.</param>
    /// <param name="label">Maps a tag to its label.</param>
    /// <param name="nounLabel">The label that stands for nouns.</param>
    /// <param name="verbLabel">The label that stands for verbs.</param>
    /// <param name="lexiconAmbiguous">Whether the lexicon lists more than one tag for the token (shape rules do not count).</param>
    /// <returns>The outcome.</returns>
    public static Outcome Judge(string documentId, string goldLabel, TaggedToken token, Func<Upos, string> label, string nounLabel, string verbLabel, bool lexiconAmbiguous)
    {
        // Gather the candidates with their probabilities and look for the gold answer among them
        var chosenLabel = label(token.Tag);
        var goldProbability = chosenLabel == goldLabel ? token.Confidence : 0.0;
        var hasNoun = chosenLabel == nounLabel;
        var hasVerb = chosenLabel == verbLabel;
        foreach (var alternative in token.Alternatives)
        {
            var alternativeLabel = label(alternative.Tag);
            goldProbability += alternativeLabel == goldLabel ? alternative.Probability : 0.0;
            hasNoun |= alternativeLabel == nounLabel;
            hasVerb |= alternativeLabel == verbLabel;
        }

        // A noun-verb decision is one where the gold answer is a noun or a verb and both were possible
        var correct = chosenLabel == goldLabel;
        var nounVerb = (goldLabel == nounLabel || goldLabel == verbLabel) && hasNoun && hasVerb;
        var swapped = (goldLabel == nounLabel && chosenLabel == verbLabel) || (goldLabel == verbLabel && chosenLabel == nounLabel);
        var harmful = nounVerb && swapped && token.Confidence >= HarmfulConfidence;
        return new Outcome(documentId, correct, token.Confidence, goldProbability, lexiconAmbiguous, nounVerb, harmful);
    }
}

/// <summary>
///     The result of a paired bootstrap of the difference between two sets of outcomes.
/// </summary>
internal sealed class BootstrapResult
{
    /// <summary>
    ///     Gets or sets the observed difference (first minus second).
    /// </summary>
    public double Difference { get; set; }

    /// <summary>
    ///     Gets or sets the lower end of the 95 percent interval.
    /// </summary>
    public double Low { get; set; }

    /// <summary>
    ///     Gets or sets the upper end of the 95 percent interval.
    /// </summary>
    public double High { get; set; }

    /// <summary>
    ///     Gets a value indicating whether the interval contains zero (the two runs are statistically tied).
    /// </summary>
    public bool ContainsZero => Low <= 0 && High >= 0;
}

/// <summary>
///     A paired bootstrap that resamples whole documents, because the tokens of one document are not independent.
/// </summary>
/// <remarks>
///     The two outcome lists must describe the same tokens in the same order (two models on one dev set). A resample
///     draws as many documents as there are, with replacement, and recomputes both statistics on the same draw, so the
///     difference is paired. The generator is <see cref="DeterministicRandom"/> with a fixed seed. Stateless and thread-safe.
/// </remarks>
internal static class Bootstrap
{
    /// <summary>
    ///     The number of resamples of the selection protocol.
    /// </summary>
    public const int DefaultResamples = 1000;

    /// <summary>
    ///     Bootstraps the difference of the accuracies in percentage points and of the harmful errors per 1,000 noun-verb decisions.
    /// </summary>
    /// <param name="first">The outcomes of the first model.</param>
    /// <param name="second">The outcomes of the second model, for the same tokens in the same order.</param>
    /// <param name="resamples">The number of resamples.</param>
    /// <param name="seed">The seed of the generator.</param>
    /// <param name="accuracy">Receives the accuracy difference (first minus second, percentage points).</param>
    /// <param name="harmfulPer1000">Receives the difference of the harmful errors per 1,000 noun-verb decisions.</param>
    /// <exception cref="ArgumentException">The lists differ in length.</exception>
    public static void Compare(
        IReadOnlyList<Outcome> first,
        IReadOnlyList<Outcome> second,
        int resamples,
        ulong seed,
        out BootstrapResult accuracy,
        out BootstrapResult harmfulPer1000)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        if (first.Count != second.Count)
        {
            throw new ArgumentException("A paired bootstrap needs the same tokens in both lists.", nameof(second));
        }

        // Per document: tokens, correct counts of both models, noun-verb decisions and harmful errors of both models
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        var tally = new List<long[]>();
        for (var i = 0; i < first.Count; i++)
        {
            if (!index.TryGetValue(first[i].DocumentId, out var document))
            {
                document = tally.Count;
                index[first[i].DocumentId] = document;
                tally.Add(new long[6]);
            }

            var row = tally[document];
            row[0]++;
            row[1] += first[i].Correct ? 1 : 0;
            row[2] += second[i].Correct ? 1 : 0;
            row[3] += first[i].NounVerb ? 1 : 0;
            row[4] += first[i].Harmful ? 1 : 0;
            row[5] += second[i].Harmful ? 1 : 0;
        }

        // The observed differences, then the same statistics over resampled documents
        var all = new long[6];
        foreach (var row in tally)
        {
            Add(all, row);
        }

        var random = new DeterministicRandom(seed);
        var accuracyDifferences = new double[resamples];
        var harmfulDifferences = new double[resamples];
        var sample = new long[6];
        for (var r = 0; r < resamples; r++)
        {
            Array.Clear(sample, 0, sample.Length);
            for (var d = 0; d < tally.Count; d++)
            {
                Add(sample, tally[random.NextInt(tally.Count)]);
            }

            accuracyDifferences[r] = AccuracyDifference(sample);
            harmfulDifferences[r] = HarmfulDifference(sample);
        }

        accuracy = Interval(AccuracyDifference(all), accuracyDifferences);
        harmfulPer1000 = Interval(HarmfulDifference(all), harmfulDifferences);
    }

    /// <summary>
    ///     Adds a document tally to a running tally.
    /// </summary>
    /// <param name="total">The running tally.</param>
    /// <param name="row">The tally to add.</param>
    private static void Add(long[] total, long[] row)
    {
        for (var i = 0; i < total.Length; i++)
        {
            total[i] += row[i];
        }
    }

    /// <summary>
    ///     Computes the accuracy difference in percentage points from a tally.
    /// </summary>
    /// <param name="tally">The tally.</param>
    /// <returns>The difference.</returns>
    private static double AccuracyDifference(long[] tally) =>
        tally[0] == 0 ? 0 : 100.0 * (tally[1] - tally[2]) / tally[0];

    /// <summary>
    ///     Computes the difference of the harmful errors per 1,000 noun-verb decisions from a tally.
    /// </summary>
    /// <param name="tally">The tally.</param>
    /// <returns>The difference.</returns>
    private static double HarmfulDifference(long[] tally) =>
        tally[3] == 0 ? 0 : 1000.0 * (tally[4] - tally[5]) / tally[3];

    /// <summary>
    ///     Builds a 95 percent percentile interval from the resampled differences.
    /// </summary>
    /// <param name="observed">The observed difference.</param>
    /// <param name="samples">The resampled differences (sorted in place).</param>
    /// <returns>The result.</returns>
    internal static BootstrapResult Interval(double observed, double[] samples)
    {
        Array.Sort(samples);
        var low = samples.Length == 0 ? observed : samples[(int)(0.025 * (samples.Length - 1))];
        var high = samples.Length == 0 ? observed : samples[(int)Math.Ceiling(0.975 * (samples.Length - 1))];
        return new BootstrapResult { Difference = observed, Low = low, High = high };
    }
}
