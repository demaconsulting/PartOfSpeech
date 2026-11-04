using System.Globalization;
using System.Text;

namespace DemaConsulting.PartOfSpeech.Trainer;

/// <summary>
///     The development selection: the curves of both development sets, the pooled curve, the rule's operating points and the
///     bootstrap intervals of the selected points.
/// </summary>
/// <remarks>
///     Everything here is computed from development outcomes only; the class has no data access, so tests exercise it with
///     fixture outcomes. The pooled set is the union of the open and the gold v2 decisions (the document ids of the two sets
///     cannot collide because their prefixes differ).
/// </remarks>
internal sealed class DevSelection
{
    /// <summary>
    ///     Gets the SHA-256 of the uncompressed image of the model.
    /// </summary>
    public string ModelImageSha256 { get; private init; } = string.Empty;

    /// <summary>
    ///     Gets the temperature of the model.
    /// </summary>
    public double Temperature { get; private init; }

    /// <summary>
    ///     Gets the tally of the open development split.
    /// </summary>
    public EvalTally Open { get; private init; } = new();

    /// <summary>
    ///     Gets the tally of the gold v2 development split (without <c>to</c> and <c>not</c>).
    /// </summary>
    public EvalTally Gold { get; private init; } = new();

    /// <summary>
    ///     Gets the tally of both development sets together.
    /// </summary>
    public EvalTally Pooled { get; private init; } = new();

    /// <summary>
    ///     Gets the points selected by the rule.
    /// </summary>
    public List<SelectedPoint> Points { get; private init; } = new();

    /// <summary>
    ///     Gets the 95 percent interval of the harmful errors per 1,000 answered decisions of each selected point (empty for a point that is none).
    /// </summary>
    public List<BootstrapResult?> HarmfulIntervals { get; private init; } = new();

    /// <summary>
    ///     Gets the 95 percent interval of the coverage of each selected point (null for a point that is none).
    /// </summary>
    public List<BootstrapResult?> CoverageIntervals { get; private init; } = new();

    /// <summary>
    ///     Gets the number of bootstrap resamples.
    /// </summary>
    public int Resamples { get; private init; }

    /// <summary>
    ///     Gets the seed of the bootstrap.
    /// </summary>
    public ulong Seed { get; private init; }

    /// <summary>
    ///     Computes the selection.
    /// </summary>
    /// <param name="modelImageSha256">The SHA-256 of the uncompressed image of the model.</param>
    /// <param name="temperature">The temperature of the model.</param>
    /// <param name="open">The outcomes of the open development split.</param>
    /// <param name="gold">The outcomes of the gold v2 development split without <c>to</c> and <c>not</c>.</param>
    /// <param name="resamples">The number of bootstrap resamples.</param>
    /// <param name="seed">The seed of the bootstrap.</param>
    /// <returns>The selection.</returns>
    public static DevSelection Create(string modelImageSha256, double temperature, IReadOnlyList<Outcome> open, IReadOnlyList<Outcome> gold, int resamples, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(gold);
        var openTally = EvalTally.Of(open);
        var goldTally = EvalTally.Of(gold);
        var pooled = new EvalTally();
        pooled.Add(openTally);
        pooled.Add(goldTally);
        var points = OperatingPointRule.Select(ConfidenceCurve.Of(pooled));

        // The intervals of the selected points over the pooled documents
        var clusters = EvalTally.ByDocument(open);
        clusters.AddRange(EvalTally.ByDocument(gold));
        var harmful = new List<BootstrapResult?>();
        var coverage = new List<BootstrapResult?>();
        foreach (var row in points.Select(p => p.Row))
        {
            if (row == null || clusters.Count == 0)
            {
                harmful.Add(null);
                coverage.Add(null);
                continue;
            }

            var index = ConfidenceCurve.IndexOf(row.Threshold);
            var result = ClusterBootstrap.Intervals(
                clusters,
                new Func<EvalTally, double>[] { t => HarmfulPerAnswered(t, index), t => Coverage(t, index) },
                resamples,
                seed);
            harmful.Add(result[0]);
            coverage.Add(result[1]);
        }

        return new DevSelection
        {
            ModelImageSha256 = modelImageSha256,
            Temperature = temperature,
            Open = openTally,
            Gold = goldTally,
            Pooled = pooled,
            Points = points,
            HarmfulIntervals = harmful,
            CoverageIntervals = coverage,
            Resamples = resamples,
            Seed = seed,
        };
    }

    /// <summary>
    ///     Computes the harmful errors per 1,000 answered decisions at a grid threshold from a tally.
    /// </summary>
    /// <param name="tally">The tally.</param>
    /// <param name="index">The index of the threshold on the grid.</param>
    /// <returns>The rate, 0 when nothing is answered.</returns>
    public static double HarmfulPerAnswered(EvalTally tally, int index) =>
        tally.AnsweredAt[index] == 0 ? 0 : 1000.0 * tally.HarmfulAt[index] / tally.AnsweredAt[index];

    /// <summary>
    ///     Computes the harmful errors per 1,000 decisions at a grid threshold from a tally.
    /// </summary>
    /// <param name="tally">The tally.</param>
    /// <param name="index">The index of the threshold on the grid.</param>
    /// <returns>The rate, 0 when there is no decision.</returns>
    public static double HarmfulPerDecisions(EvalTally tally, int index) =>
        tally.Decisions == 0 ? 0 : 1000.0 * tally.HarmfulAt[index] / tally.Decisions;

    /// <summary>
    ///     Computes the coverage at a grid threshold from a tally.
    /// </summary>
    /// <param name="tally">The tally.</param>
    /// <param name="index">The index of the threshold on the grid.</param>
    /// <returns>The share of the decisions that is answered, in percent.</returns>
    public static double Coverage(EvalTally tally, int index) =>
        tally.Decisions == 0 ? 0 : 100.0 * tally.AnsweredAt[index] / tally.Decisions;

    /// <summary>
    ///     Builds the selection artifact.
    /// </summary>
    /// <param name="devFiles">The development files with their hashes.</param>
    /// <returns>The artifact.</returns>
    public OperatingPointsData ToArtifact(IEnumerable<DevFileRecord> devFiles) =>
        OperatingPointsData.Build(ModelImageSha256, Temperature, Open.Accuracy, Open.Decisions, Gold.Decisions, devFiles, Points);
}

/// <summary>
///     The result of the bootstrap of one held-out set for one operating point.
/// </summary>
internal sealed class PointResult
{
    /// <summary>
    ///     Gets or sets the name of the point (<c>reference</c> for the 0.45 row that every report starts with).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the threshold.
    /// </summary>
    public double Threshold { get; set; }

    /// <summary>
    ///     Gets or sets the number of decisions answered.
    /// </summary>
    public long Answered { get; set; }

    /// <summary>
    ///     Gets or sets the number of harmful errors among the answered decisions.
    /// </summary>
    public long Harmful { get; set; }

    /// <summary>
    ///     Gets or sets the coverage in percent.
    /// </summary>
    public double CoveragePercent { get; set; }

    /// <summary>
    ///     Gets or sets the harmful errors per 1,000 answered decisions.
    /// </summary>
    public double HarmfulPer1000Answered { get; set; }

    /// <summary>
    ///     Gets or sets the lower end of the 95 percent interval of the harmful errors per 1,000 answered decisions.
    /// </summary>
    public double HarmfulPer1000AnsweredLow { get; set; }

    /// <summary>
    ///     Gets or sets the upper end of the 95 percent interval of the harmful errors per 1,000 answered decisions.
    /// </summary>
    public double HarmfulPer1000AnsweredHigh { get; set; }

    /// <summary>
    ///     Gets or sets the harmful errors per 1,000 decisions of this point minus those at 0.45 (paired).
    /// </summary>
    public double DeltaHarmfulPer1000Decisions { get; set; }

    /// <summary>
    ///     Gets or sets the lower end of the 95 percent interval of the paired difference.
    /// </summary>
    public double DeltaLow { get; set; }

    /// <summary>
    ///     Gets or sets the upper end of the 95 percent interval of the paired difference.
    /// </summary>
    public double DeltaHigh { get; set; }
}

/// <summary>
///     The precision and recall of one tag in one held-out set (or view of a set), with their 95 percent intervals.
/// </summary>
/// <remarks>
///     A precision or recall that is undefined (the tag was never chosen, or has no gold token) is 0, in the figure and in
///     every resample, as in <see cref="EvalTally.Precision"/> and <see cref="EvalTally.Recall"/>.
/// </remarks>
internal sealed class TagRecord
{
    /// <summary>
    ///     Gets or sets the label of the tag.
    /// </summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the number of tokens whose gold label is the tag.
    /// </summary>
    public long GoldTokens { get; set; }

    /// <summary>
    ///     Gets or sets the precision in percent.
    /// </summary>
    public double Precision { get; set; }

    /// <summary>
    ///     Gets or sets the lower end of the 95 percent interval of the precision.
    /// </summary>
    public double PrecisionLow { get; set; }

    /// <summary>
    ///     Gets or sets the upper end of the 95 percent interval of the precision.
    /// </summary>
    public double PrecisionHigh { get; set; }

    /// <summary>
    ///     Gets or sets the recall in percent.
    /// </summary>
    public double Recall { get; set; }

    /// <summary>
    ///     Gets or sets the lower end of the 95 percent interval of the recall.
    /// </summary>
    public double RecallLow { get; set; }

    /// <summary>
    ///     Gets or sets the upper end of the 95 percent interval of the recall.
    /// </summary>
    public double RecallHigh { get; set; }
}

/// <summary>
///     The summary of one held-out set (or view of a set) in the results.
/// </summary>
internal sealed class SetRecord
{
    /// <summary>
    ///     Gets or sets the name of the set.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the number of judged tokens.
    /// </summary>
    public long Tokens { get; set; }

    /// <summary>
    ///     Gets or sets the number of documents (the bootstrap clusters).
    /// </summary>
    public int Documents { get; set; }

    /// <summary>
    ///     Gets or sets the accuracy in percent.
    /// </summary>
    public double Accuracy { get; set; }

    /// <summary>
    ///     Gets or sets the lower end of the 95 percent interval of the accuracy.
    /// </summary>
    public double AccuracyLow { get; set; }

    /// <summary>
    ///     Gets or sets the upper end of the 95 percent interval of the accuracy.
    /// </summary>
    public double AccuracyHigh { get; set; }

    /// <summary>
    ///     Gets or sets the number of lexicon-ambiguous tokens.
    /// </summary>
    public long AmbiguousTokens { get; set; }

    /// <summary>
    ///     Gets or sets the accuracy on the lexicon-ambiguous tokens in percent.
    /// </summary>
    public double AmbiguousAccuracy { get; set; }

    /// <summary>
    ///     Gets or sets the lower end of the 95 percent interval of the accuracy on the lexicon-ambiguous tokens.
    /// </summary>
    public double AmbiguousAccuracyLow { get; set; }

    /// <summary>
    ///     Gets or sets the upper end of the 95 percent interval of the accuracy on the lexicon-ambiguous tokens.
    /// </summary>
    public double AmbiguousAccuracyHigh { get; set; }

    /// <summary>
    ///     Gets or sets the number of noun-verb decisions.
    /// </summary>
    public long Decisions { get; set; }

    /// <summary>
    ///     Gets or sets the number of harmful errors at 0.45.
    /// </summary>
    public long Harmful { get; set; }

    /// <summary>
    ///     Gets or sets the harmful errors per 1,000 decisions at 0.45.
    /// </summary>
    public double HarmfulPer1000 { get; set; }

    /// <summary>
    ///     Gets or sets the lower end of the 95 percent interval of the harmful errors per 1,000 decisions.
    /// </summary>
    public double HarmfulPer1000Low { get; set; }

    /// <summary>
    ///     Gets or sets the upper end of the 95 percent interval of the harmful errors per 1,000 decisions.
    /// </summary>
    public double HarmfulPer1000High { get; set; }

    /// <summary>
    ///     Gets or sets the expected calibration error in percent.
    /// </summary>
    public double Ece { get; set; }

    /// <summary>
    ///     Gets or sets the lower end of the 95 percent interval of the calibration error.
    /// </summary>
    public double EceLow { get; set; }

    /// <summary>
    ///     Gets or sets the upper end of the 95 percent interval of the calibration error.
    /// </summary>
    public double EceHigh { get; set; }

    /// <summary>
    ///     Gets or sets the mean negative log-likelihood of the gold label.
    /// </summary>
    public double Nll { get; set; }

    /// <summary>
    ///     Gets or sets the lower end of the 95 percent interval of the mean negative log-likelihood.
    /// </summary>
    public double NllLow { get; set; }

    /// <summary>
    ///     Gets or sets the upper end of the 95 percent interval of the mean negative log-likelihood.
    /// </summary>
    public double NllHigh { get; set; }

    /// <summary>
    ///     Gets or sets the number of tokens whose gold label was not among the decoder's candidates.
    /// </summary>
    public long OutsideCandidates { get; set; }

    /// <summary>
    ///     Gets or sets the six cells of the noun-verb confusion: gold noun answered with another tag, a noun or a verb, then
    ///     gold verb answered with another tag, a noun or a verb.
    /// </summary>
    public List<long> NounVerbConfusion { get; set; } = new();

    /// <summary>
    ///     Gets or sets the precision and recall of every tag that has a gold token or was chosen, with 95 percent intervals.
    /// </summary>
    public List<TagRecord> Tags { get; set; } = new();

    /// <summary>
    ///     Gets or sets the reference row (0.45) and one row for every selected operating point.
    /// </summary>
    public List<PointResult> Points { get; set; } = new();
}

/// <summary>
///     The input file of a held-out run, identified by its hash.
/// </summary>
internal sealed class InputRecord
{
    /// <summary>
    ///     Gets or sets the name of the input.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the SHA-256 of the input.
    /// </summary>
    public string Sha256 { get; set; } = string.Empty;
}

/// <summary>
///     The machine-readable results of the one held-out run (<c>test-results.json</c>): what was run, on which inputs, and the numbers.
/// </summary>
internal sealed class ResultsData
{
    /// <summary>
    ///     Gets or sets the schema version.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    ///     Gets or sets the SHA-256 of the uncompressed image of the model that was scored.
    /// </summary>
    public string ModelImageSha256 { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the SHA-256 of the exact bytes of the selection artifact that was applied.
    /// </summary>
    public string SelectionSha256 { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the identifier of the selection rule.
    /// </summary>
    public string RuleId { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the number of bootstrap resamples.
    /// </summary>
    public int Resamples { get; set; }

    /// <summary>
    ///     Gets or sets the seed of the bootstrap.
    /// </summary>
    public ulong Seed { get; set; }

    /// <summary>
    ///     Gets or sets the description of the .NET runtime of the run.
    /// </summary>
    public string Runtime { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the throughput of the default tagger on the open test split in tokens per second (one thread).
    /// </summary>
    public double TokensPerSecond { get; set; }

    /// <summary>
    ///     Gets or sets the inputs with their hashes.
    /// </summary>
    public List<InputRecord> Inputs { get; set; } = new();

    /// <summary>
    ///     Gets or sets the summary of every set and view.
    /// </summary>
    public List<SetRecord> Sets { get; set; } = new();

    /// <summary>
    ///     Gets or sets the stop conditions that fired (empty when none did).
    /// </summary>
    public List<string> TripwiresFired { get; set; } = new();
}

/// <summary>
///     Formats figures and tables for the generated reports; the only place that decides how a number is spelled.
/// </summary>
internal static class ReportFormat
{
    /// <summary>
    ///     Formats a number with a number of decimals, invariant culture.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <param name="decimals">The decimals.</param>
    /// <returns>The text.</returns>
    public static string F(double value, int decimals) => value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    /// <summary>
    ///     Formats an integer with thousands separators, invariant culture.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <returns>The text.</returns>
    public static string N(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>
    ///     Formats a value with its 95 percent interval.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="low">The lower end.</param>
    /// <param name="high">The upper end.</param>
    /// <param name="decimals">The decimals.</param>
    /// <returns>The text, for example <c>96.07 [95.80, 96.33]</c>.</returns>
    public static string WithInterval(double value, double low, double high, int decimals) =>
        $"{F(value, decimals)} [{F(low, decimals)}, {F(high, decimals)}]";

    /// <summary>
    ///     Appends a Markdown table.
    /// </summary>
    /// <param name="text">The text to append to.</param>
    /// <param name="header">The column titles.</param>
    /// <param name="rows">The rows; each has one cell for each column.</param>
    public static void Table(StringBuilder text, IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string>> rows)
    {
        text.Append("| ").Append(string.Join(" | ", header)).Append(" |\n");
        text.Append("| ").Append(string.Join(" | ", header.Select((h, i) => i == 0 ? "---" : "---:"))).Append(" |\n");
        foreach (var row in rows)
        {
            text.Append("| ").Append(string.Join(" | ", row)).Append(" |\n");
        }

        text.Append('\n');
    }
}
