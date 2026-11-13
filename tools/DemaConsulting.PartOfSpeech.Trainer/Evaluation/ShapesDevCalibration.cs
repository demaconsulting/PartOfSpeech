using System.Globalization;

namespace DemaConsulting.PartOfSpeech.Trainer;

/// <summary>
///     The answers of one scope at one confidence threshold: how many were given with at least that confidence and how many of them were wrong.
/// </summary>
internal sealed class CalibrationThresholdRow
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="CalibrationThresholdRow"/> class.
    /// </summary>
    /// <param name="threshold">The confidence threshold (inclusive).</param>
    /// <param name="answers">The judged words whose confidence is at least the threshold.</param>
    /// <param name="wrong">The wrong answers among them.</param>
    public CalibrationThresholdRow(double threshold, int answers, int wrong)
    {
        Threshold = threshold;
        Answers = answers;
        Wrong = wrong;
        var (low, high) = ShapesDevCalibration.Wilson(wrong, answers);
        ErrorPer1000 = answers == 0 ? 0 : 1000.0 * wrong / answers;
        IntervalLowPer1000 = 1000.0 * low;
        IntervalHighPer1000 = 1000.0 * high;
    }

    /// <summary>
    ///     Gets the confidence threshold (inclusive).
    /// </summary>
    public double Threshold { get; }

    /// <summary>
    ///     Gets the number of judged words whose confidence is at least the threshold.
    /// </summary>
    public int Answers { get; }

    /// <summary>
    ///     Gets the number of wrong answers among them.
    /// </summary>
    public int Wrong { get; }

    /// <summary>
    ///     Gets the wrong answers per 1,000 answers at or above the threshold (0 without answers).
    /// </summary>
    public double ErrorPer1000 { get; }

    /// <summary>
    ///     Gets the lower end of the 95% Wilson interval of the error rate, per 1,000 answers (0 without answers).
    /// </summary>
    public double IntervalLowPer1000 { get; }

    /// <summary>
    ///     Gets the upper end of the 95% Wilson interval of the error rate, per 1,000 answers (0 without answers).
    /// </summary>
    public double IntervalHighPer1000 { get; }
}

/// <summary>
///     The calibration measurement of one scope (all selected tranches pooled, or one tranche): the error at the thresholds and the reliability bins.
/// </summary>
internal sealed class CalibrationScope
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="CalibrationScope"/> class.
    /// </summary>
    /// <param name="name">The name of the scope.</param>
    /// <param name="judged">The number of judged words.</param>
    /// <param name="thresholds">The rows of the thresholds, in the order of <see cref="ShapesDevCalibration.Thresholds"/>.</param>
    /// <param name="bins">The reliability bins, in confidence order.</param>
    public CalibrationScope(string name, int judged, IReadOnlyList<CalibrationThresholdRow> thresholds, IReadOnlyList<ReliabilityRow> bins)
    {
        Name = name;
        Judged = judged;
        Thresholds = thresholds;
        Bins = bins;
    }

    /// <summary>
    ///     Gets the name of the scope.
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     Gets the number of judged words (targets and controls).
    /// </summary>
    public int Judged { get; }

    /// <summary>
    ///     Gets the rows of the thresholds.
    /// </summary>
    public IReadOnlyList<CalibrationThresholdRow> Thresholds { get; }

    /// <summary>
    ///     Gets the reliability bins: count, mean confidence, correct share and harmful errors of the judged words whose confidence lies in the bin.
    /// </summary>
    public IReadOnlyList<ReliabilityRow> Bins { get; }
}

/// <summary>
///     The calibration measurement of the selected single-shape tranches: the pooled scope and one scope per tranche.
/// </summary>
internal sealed class CalibrationReport
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="CalibrationReport"/> class.
    /// </summary>
    /// <param name="pooled">The scope of all judged words of the selected tranches.</param>
    /// <param name="tranches">The scope of each tranche, in the order of the selection.</param>
    public CalibrationReport(CalibrationScope pooled, IReadOnlyList<CalibrationScope> tranches)
    {
        Pooled = pooled;
        Tranches = tranches;
    }

    /// <summary>
    ///     Gets the scope of all judged words of the selected tranches.
    /// </summary>
    public CalibrationScope Pooled { get; }

    /// <summary>
    ///     Gets the scope of each tranche.
    /// </summary>
    public IReadOnlyList<CalibrationScope> Tranches { get; }
}

/// <summary>
///     Measures the calibration of the confidence of a tagger on the single-shape tranches of the hard-shape development set: the error per
///     1,000 answers at the thresholds 0.90 and 0.95 with a Wilson interval, and a reliability table.
/// </summary>
/// <remarks>
///     <para>
///         The measurement is a function of outcomes: every judged word (targets and controls) of the selected tranches, scored under the
///         scoring of its tranche (<see cref="ShapesDevTrancheEvaluator.Judge"/>: class or exact tag), through the shipped decoder with its
///         temperature. An answer is a judged word; it is wrong when its outcome is not correct, whatever its kind of error, and its
///         confidence is the confidence of the chosen tag. The error per 1,000 answers at a threshold is the wrong answers per 1,000 of the
///         answers with a confidence of at least the threshold; the interval is the 95% Wilson score interval of that share.
///     </para>
///     <para>
///         The reliability bins are 0.5 to 0.6, 0.6 to 0.7, 0.7 to 0.8, 0.8 to 0.9, 0.9 to 0.95 and 0.95 to 1.0 (the lower edge is inclusive and
///         the last bin includes 1.0), preceded by a bin of the confidences below 0.5 so that the counts add up to the judged words. Each bin has
///         its count, mean confidence, correct share and harmful errors.
///     </para>
///     <para>
///         The measurement reports what the model does. It does not choose, change or propose a threshold. Stateless and thread-safe.
///     </para>
/// </remarks>
internal static class ShapesDevCalibration
{
    /// <summary>
    ///     The z value of a two-sided 95% interval (the one of <see cref="ShapesDevReport"/>).
    /// </summary>
    private const double Z95 = 1.959964;

    /// <summary>
    ///     The thresholds of the error measurement: <see cref="Metrics.ConfidentWrong90"/> and <see cref="Metrics.ConfidentWrongConfidence"/>.
    /// </summary>
    public static readonly IReadOnlyList<double> Thresholds = new[] { Metrics.ConfidentWrong90, Metrics.ConfidentWrongConfidence };

    /// <summary>
    ///     The lower edges of the reliability bins: below 0.5, then 0.5, 0.6, 0.7, 0.8, 0.9 and 0.95 (the last bin ends at 1.0).
    /// </summary>
    public static readonly IReadOnlyList<double> BinEdges = new[] { 0.0, 0.5, 0.6, 0.7, 0.8, 0.9, 0.95 };

    /// <summary>
    ///     Computes the 95% Wilson score interval of a share.
    /// </summary>
    /// <param name="successes">The number of successes.</param>
    /// <param name="total">The number of trials.</param>
    /// <returns>The lower and upper end as shares between 0 and 1; (0, 0) without trials.</returns>
    public static (double Low, double High) Wilson(int successes, int total)
    {
        if (total <= 0)
        {
            return (0, 0);
        }

        var n = (double)total;
        var p = successes / n;
        var z2 = Z95 * Z95;
        var center = (p + (z2 / (2 * n))) / (1 + (z2 / n));
        var half = Z95 * Math.Sqrt((p * (1 - p) / n) + (z2 / (4 * n * n))) / (1 + (z2 / n));
        return (Math.Max(0, center - half), Math.Min(1, center + half));
    }

    /// <summary>
    ///     Measures the calibration of one scope.
    /// </summary>
    /// <param name="name">The name of the scope.</param>
    /// <param name="outcomes">The outcomes of the judged words of the scope.</param>
    /// <returns>The scope with its threshold rows and reliability bins.</returns>
    public static CalibrationScope MeasureScope(string name, IReadOnlyList<Outcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(outcomes);
        var thresholds = new List<CalibrationThresholdRow>(Thresholds.Count);
        foreach (var threshold in Thresholds)
        {
            var answers = 0;
            var wrong = 0;
            foreach (var outcome in outcomes)
            {
                if (outcome.Confidence >= threshold)
                {
                    answers++;
                    wrong += outcome.Correct ? 0 : 1;
                }
            }

            thresholds.Add(new CalibrationThresholdRow(threshold, answers, wrong));
        }

        var count = new long[BinEdges.Count];
        var correct = new long[BinEdges.Count];
        var harmful = new long[BinEdges.Count];
        var confidence = new double[BinEdges.Count];
        foreach (var outcome in outcomes)
        {
            var bin = 0;
            while (bin + 1 < BinEdges.Count && outcome.Confidence >= BinEdges[bin + 1])
            {
                bin++;
            }

            count[bin]++;
            correct[bin] += outcome.Correct ? 1 : 0;
            harmful[bin] += outcome.Harmful ? 1 : 0;
            confidence[bin] += outcome.Confidence;
        }

        var bins = new List<ReliabilityRow>(BinEdges.Count);
        for (var i = 0; i < BinEdges.Count; i++)
        {
            bins.Add(new ReliabilityRow(
                BinEdges[i],
                i + 1 < BinEdges.Count ? BinEdges[i + 1] : 1.0,
                count[i],
                count[i] == 0 ? 0 : confidence[i] / count[i],
                count[i] == 0 ? 0 : 100.0 * correct[i] / count[i],
                harmful[i]));
        }

        return new CalibrationScope(name, outcomes.Count, thresholds, bins);
    }

    /// <summary>
    ///     Measures the calibration of the selected tranches: all their judged words pooled, and each tranche.
    /// </summary>
    /// <param name="tranches">The name of each tranche with the outcomes of its judged words (targets and controls), in report order.</param>
    /// <returns>The report.</returns>
    public static CalibrationReport Measure(IReadOnlyList<KeyValuePair<string, IReadOnlyList<Outcome>>> tranches)
    {
        ArgumentNullException.ThrowIfNull(tranches);
        var pooled = MeasureScope("all selected tranches", tranches.SelectMany(t => t.Value).ToList());
        return new CalibrationReport(pooled, tranches.Select(t => MeasureScope(t.Key, t.Value)).ToList());
    }

    /// <summary>
    ///     Renders the calibration section as Markdown.
    /// </summary>
    /// <param name="label">The label of the run.</param>
    /// <param name="report">The measurement.</param>
    /// <returns>The lines of the section, without line terminators.</returns>
    public static List<string> Render(string label, CalibrationReport report)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(report);
        var lines = new List<string>
        {
            "## Shapes development set, calibration of the confidence: " + label,
            string.Empty,
            "- Judged words: every target and control item of the scored tranches, scored under the scoring of its tranche (by class or by exact tag) through the real decoder with all alternatives; the confidence is the confidence of the chosen tag.",
            "- Error per 1,000 answers at a threshold: the wrong answers per 1,000 of the judged words whose confidence is at least the threshold, with the 95% Wilson score interval of that share.",
            "- The measurement reports what the model does; no threshold is chosen, changed or proposed here.",
            string.Empty,
            "| scope | judged words | " + string.Join(" | ", Thresholds.Select(t => string.Format(CultureInfo.InvariantCulture, "answers at >= {0:0.00} | wrong at >= {0:0.00} | errors per 1,000 answers at >= {0:0.00} | 95% interval at >= {0:0.00}", t))) + " |",
            "| --- | --- | " + string.Join(" | ", Thresholds.Select(_ => "--- | --- | --- | ---")) + " |",
            ThresholdLine(report.Pooled),
        };
        foreach (var tranche in report.Tranches)
        {
            lines.Add(ThresholdLine(tranche));
        }

        AddReliability(lines, report.Pooled);
        foreach (var tranche in report.Tranches)
        {
            AddReliability(lines, tranche);
        }

        return lines;
    }

    /// <summary>
    ///     Renders the row of one scope of the threshold table.
    /// </summary>
    /// <param name="scope">The scope.</param>
    /// <returns>The row.</returns>
    private static string ThresholdLine(CalibrationScope scope)
    {
        var cells = new List<string> { scope.Name, scope.Judged.ToString(CultureInfo.InvariantCulture) };
        foreach (var row in scope.Thresholds)
        {
            cells.Add(row.Answers.ToString(CultureInfo.InvariantCulture));
            cells.Add(row.Wrong.ToString(CultureInfo.InvariantCulture));
            cells.Add(row.Answers == 0 ? "-" : row.ErrorPer1000.ToString("0.0", CultureInfo.InvariantCulture));
            cells.Add(row.Answers == 0
                ? "-"
                : "[" + row.IntervalLowPer1000.ToString("0.0", CultureInfo.InvariantCulture) + ", " + row.IntervalHighPer1000.ToString("0.0", CultureInfo.InvariantCulture) + "]");
        }

        return "| " + string.Join(" | ", cells) + " |";
    }

    /// <summary>
    ///     Adds the reliability table of one scope.
    /// </summary>
    /// <param name="lines">The lines to extend.</param>
    /// <param name="scope">The scope.</param>
    private static void AddReliability(List<string> lines, CalibrationScope scope)
    {
        lines.Add(string.Empty);
        lines.Add("### Reliability: " + scope.Name);
        lines.Add(string.Empty);
        lines.Add("| confidence | judged words | mean confidence | correct | harmful errors |");
        lines.Add("| --- | --- | --- | --- | --- |");
        foreach (var bin in scope.Bins)
        {
            var range = bin.Low <= 0
                ? string.Format(CultureInfo.InvariantCulture, "below {0:0.00}", bin.High)
                : string.Format(CultureInfo.InvariantCulture, "{0:0.00} to {1:0.00}", bin.Low, bin.High);
            lines.Add(string.Format(
                CultureInfo.InvariantCulture,
                "| {0} | {1} | {2} | {3} | {4} |",
                range,
                bin.Decisions,
                bin.Decisions == 0 ? "-" : bin.MeanConfidence.ToString("0.000", CultureInfo.InvariantCulture),
                bin.Decisions == 0 ? "-" : bin.CorrectPercent.ToString("0.0", CultureInfo.InvariantCulture) + "%",
                bin.Harmful));
        }
    }
}
