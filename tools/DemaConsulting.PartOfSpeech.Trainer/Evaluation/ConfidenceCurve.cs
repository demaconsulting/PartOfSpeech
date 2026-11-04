using System.Globalization;

namespace DemaConsulting.PartOfSpeech.Trainer;

/// <summary>
///     One row of the confidence curve: what answering only the decisions at or above a threshold gives.
/// </summary>
internal sealed class CurveRow
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="CurveRow"/> class.
    /// </summary>
    /// <param name="threshold">The confidence threshold.</param>
    /// <param name="decisions">The number of noun-verb decisions.</param>
    /// <param name="answered">The number of decisions answered at the threshold.</param>
    /// <param name="harmful">The number of harmful errors among the answered decisions.</param>
    public CurveRow(double threshold, long decisions, long answered, long harmful)
    {
        Threshold = threshold;
        Decisions = decisions;
        Answered = answered;
        Harmful = harmful;
    }

    /// <summary>
    ///     Gets the confidence threshold.
    /// </summary>
    public double Threshold { get; }

    /// <summary>
    ///     Gets the number of noun-verb decisions.
    /// </summary>
    public long Decisions { get; }

    /// <summary>
    ///     Gets the number of decisions answered at the threshold.
    /// </summary>
    public long Answered { get; }

    /// <summary>
    ///     Gets the number of harmful errors among the answered decisions.
    /// </summary>
    public long Harmful { get; }

    /// <summary>
    ///     Gets the share of the decisions that is answered, in percent.
    /// </summary>
    public double CoveragePercent => Decisions == 0 ? 0 : 100.0 * Answered / Decisions;

    /// <summary>
    ///     Gets the harmful errors per 1,000 answered decisions (0 when nothing is answered).
    /// </summary>
    public double HarmfulPer1000Answered => Answered == 0 ? 0 : 1000.0 * Harmful / Answered;

    /// <summary>
    ///     Gets the harmful errors per 1,000 decisions (answered or not).
    /// </summary>
    public double HarmfulPer1000Decisions => Decisions == 0 ? 0 : 1000.0 * Harmful / Decisions;
}

/// <summary>
///     One bin of the reliability table: the noun-verb decisions whose confidence lies in a range.
/// </summary>
internal sealed class ReliabilityRow
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ReliabilityRow"/> class.
    /// </summary>
    /// <param name="low">The inclusive lower edge of the bin.</param>
    /// <param name="high">The exclusive upper edge of the bin (1 for the last bin, which includes 1).</param>
    /// <param name="decisions">The number of decisions in the bin.</param>
    /// <param name="meanConfidence">The mean confidence of the decisions.</param>
    /// <param name="correctPercent">The share of correct decisions in percent.</param>
    /// <param name="harmful">The number of harmful errors in the bin.</param>
    public ReliabilityRow(double low, double high, long decisions, double meanConfidence, double correctPercent, long harmful)
    {
        Low = low;
        High = high;
        Decisions = decisions;
        MeanConfidence = meanConfidence;
        CorrectPercent = correctPercent;
        Harmful = harmful;
    }

    /// <summary>
    ///     Gets the inclusive lower edge.
    /// </summary>
    public double Low { get; }

    /// <summary>
    ///     Gets the upper edge.
    /// </summary>
    public double High { get; }

    /// <summary>
    ///     Gets the number of decisions in the bin.
    /// </summary>
    public long Decisions { get; }

    /// <summary>
    ///     Gets the mean confidence of the decisions.
    /// </summary>
    public double MeanConfidence { get; }

    /// <summary>
    ///     Gets the share of correct decisions in percent.
    /// </summary>
    public double CorrectPercent { get; }

    /// <summary>
    ///     Gets the number of harmful errors in the bin.
    /// </summary>
    public long Harmful { get; }
}

/// <summary>
///     The outcome of applying the selection rule for one named operating point.
/// </summary>
internal sealed class SelectedPoint
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="SelectedPoint"/> class.
    /// </summary>
    /// <param name="name">The name of the point (<c>strict</c> or <c>balanced</c>).</param>
    /// <param name="maxHarmfulPer1000Answered">The target: the most harmful errors per 1,000 answered decisions.</param>
    /// <param name="minCoveragePercent">The coverage floor in percent.</param>
    /// <param name="row">The curve row of the selected threshold, or <c>null</c> when no grid threshold qualifies.</param>
    public SelectedPoint(string name, double maxHarmfulPer1000Answered, double minCoveragePercent, CurveRow? row)
    {
        Name = name;
        MaxHarmfulPer1000Answered = maxHarmfulPer1000Answered;
        MinCoveragePercent = minCoveragePercent;
        Row = row;
    }

    /// <summary>
    ///     Gets the name of the point.
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     Gets the target for the harmful errors per 1,000 answered decisions.
    /// </summary>
    public double MaxHarmfulPer1000Answered { get; }

    /// <summary>
    ///     Gets the coverage floor in percent.
    /// </summary>
    public double MinCoveragePercent { get; }

    /// <summary>
    ///     Gets the curve row of the selected threshold, or <c>null</c> when no grid threshold qualifies.
    /// </summary>
    public CurveRow? Row { get; }

    /// <summary>
    ///     Gets the selected threshold, or <c>null</c> when no grid threshold qualifies.
    /// </summary>
    public double? Threshold => Row?.Threshold;
}

/// <summary>
///     The confidence curve of the noun-verb decisions and the grid it is computed on.
/// </summary>
/// <remarks>
///     The curve answers the question a consumer has: if I accept only the decisions whose confidence is at least t, how
///     many of the decisions do I keep (coverage) and how many harmful errors (noun for verb or the reverse) remain per
///     1,000 answered? The grid is fixed by the protocol of the run log. Stateless and thread-safe.
/// </remarks>
internal static class ConfidenceCurve
{
    /// <summary>
    ///     The thresholds that are candidates for an operating point: 0.45 to 0.95 in steps of 0.05, then 0.96 to 0.99.
    /// </summary>
    public static readonly IReadOnlyList<double> Grid = BuildGrid();

    /// <summary>
    ///     Computes the curve of a tally.
    /// </summary>
    /// <param name="tally">The tally of a set of judged tokens.</param>
    /// <returns>One row for each threshold of the grid.</returns>
    public static List<CurveRow> Of(EvalTally tally)
    {
        ArgumentNullException.ThrowIfNull(tally);
        var rows = new List<CurveRow>(Grid.Count);
        for (var i = 0; i < Grid.Count; i++)
        {
            rows.Add(new CurveRow(Grid[i], tally.Decisions, tally.AnsweredAt[i], tally.HarmfulAt[i]));
        }

        return rows;
    }

    /// <summary>
    ///     Computes the reliability table of the noun-verb decisions of a tally.
    /// </summary>
    /// <param name="tally">The tally.</param>
    /// <returns>One row for each confidence bin.</returns>
    public static List<ReliabilityRow> Reliability(EvalTally tally)
    {
        ArgumentNullException.ThrowIfNull(tally);
        var edges = EvalTally.ReliabilityEdges;
        var rows = new List<ReliabilityRow>(edges.Count);
        for (var i = 0; i < edges.Count; i++)
        {
            var n = tally.BinDecisions[i];
            rows.Add(new ReliabilityRow(
                edges[i],
                i + 1 < edges.Count ? edges[i + 1] : 1.0,
                n,
                n == 0 ? 0 : tally.BinDecisionConfidence[i] / n,
                n == 0 ? 0 : 100.0 * tally.BinDecisionsCorrect[i] / n,
                tally.BinHarmful[i]));
        }

        return rows;
    }

    /// <summary>
    ///     Finds the index of a threshold on the grid.
    /// </summary>
    /// <param name="threshold">The threshold.</param>
    /// <returns>The index, or -1 when the threshold is not a grid point.</returns>
    public static int IndexOf(double threshold)
    {
        for (var i = 0; i < Grid.Count; i++)
        {
            if (Math.Abs(Grid[i] - threshold) < 1e-9)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    ///     Builds the grid from integer hundredths, so that every value is the double nearest to its decimal spelling.
    /// </summary>
    /// <returns>The thresholds in ascending order.</returns>
    private static double[] BuildGrid()
    {
        var grid = new List<double>();
        for (var hundredths = 45; hundredths <= 95; hundredths += 5)
        {
            grid.Add(hundredths / 100.0);
        }

        for (var hundredths = 96; hundredths <= 99; hundredths++)
        {
            grid.Add(hundredths / 100.0);
        }

        return grid.ToArray();
    }
}

/// <summary>
///     The pre-registered rule that turns the development curve into operating points.
/// </summary>
/// <remarks>
///     <para>
///         The rule is fixed in the protocol section of the run log before any curve exists: on the pooled development
///         decisions, scan the grid in ascending order and take the <b>first</b> threshold whose harmful errors per 1,000
///         answered decisions are at most the target of the point and whose coverage is at least the floor of the point.
///         The point <c>strict</c> has the target 5.0 and the floor 60 percent, <c>balanced</c> 10.0 and 85 percent. When
///         no grid threshold qualifies the point is recorded as none; neither the targets nor the grid are relaxed.
///     </para>
///     <para>
///         Stateless and thread-safe.
///     </para>
/// </remarks>
internal static class OperatingPointRule
{
    /// <summary>
    ///     The identifier and version of the rule, recorded in the selection artifact and checked before a held-out run.
    /// </summary>
    public const string Id = "nv-pooled-dev-v1";

    /// <summary>
    ///     The harmful errors per 1,000 answered decisions of the strict point.
    /// </summary>
    public const double StrictMaxHarmful = 5.0;

    /// <summary>
    ///     The coverage floor of the strict point in percent.
    /// </summary>
    public const double StrictMinCoverage = 60.0;

    /// <summary>
    ///     The harmful errors per 1,000 answered decisions of the balanced point.
    /// </summary>
    public const double BalancedMaxHarmful = 10.0;

    /// <summary>
    ///     The coverage floor of the balanced point in percent.
    /// </summary>
    public const double BalancedMinCoverage = 85.0;

    /// <summary>
    ///     Applies the rule to the pooled development curve.
    /// </summary>
    /// <param name="pooled">The curve rows of the pooled development decisions, in grid order.</param>
    /// <returns>The strict and the balanced point, in this order.</returns>
    public static List<SelectedPoint> Select(IReadOnlyList<CurveRow> pooled)
    {
        ArgumentNullException.ThrowIfNull(pooled);
        return new List<SelectedPoint>
        {
            SelectFirst("strict", StrictMaxHarmful, StrictMinCoverage, pooled),
            SelectFirst("balanced", BalancedMaxHarmful, BalancedMinCoverage, pooled),
        };
    }

    /// <summary>
    ///     Finds the first row that meets a target and a floor.
    /// </summary>
    /// <param name="name">The name of the point.</param>
    /// <param name="maxHarmful">The target for the harmful errors per 1,000 answered decisions.</param>
    /// <param name="minCoverage">The coverage floor in percent.</param>
    /// <param name="rows">The curve rows in ascending threshold order.</param>
    /// <returns>The point; its row is <c>null</c> when none qualifies.</returns>
    private static SelectedPoint SelectFirst(string name, double maxHarmful, double minCoverage, IReadOnlyList<CurveRow> rows)
    {
        foreach (var row in rows)
        {
            if (row.Answered > 0 && row.HarmfulPer1000Answered <= maxHarmful && row.CoveragePercent >= minCoverage)
            {
                return new SelectedPoint(name, maxHarmful, minCoverage, row);
            }
        }

        return new SelectedPoint(name, maxHarmful, minCoverage, null);
    }

    /// <summary>
    ///     Formats a threshold the way the artifact and the reports spell it.
    /// </summary>
    /// <param name="threshold">The threshold.</param>
    /// <returns>The text with two decimals.</returns>
    public static string Format(double threshold) => threshold.ToString("0.00", CultureInfo.InvariantCulture);
}
