using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace DemaConsulting.PartOfSpeech.Benchmarks;

/// <summary>
///     A summary column that converts the mean time of one run over the development sentences into tokens per second.
/// </summary>
internal sealed class TokensPerSecondColumn : IColumn
{
    /// <inheritdoc />
    public string Id => nameof(TokensPerSecondColumn);

    /// <inheritdoc />
    public string ColumnName => "Tokens/s";

    /// <inheritdoc />
    public bool AlwaysShow => true;

    /// <inheritdoc />
    public ColumnCategory Category => ColumnCategory.Custom;

    /// <inheritdoc />
    public int PriorityInCategory => 0;

    /// <inheritdoc />
    public bool IsNumeric => true;

    /// <inheritdoc />
    public UnitType UnitType => UnitType.Dimensionless;

    /// <inheritdoc />
    public string Legend => "Tokens of the open development set processed per second (one thread)";

    /// <inheritdoc />
    public string GetValue(Summary summary, BenchmarkCase benchmarkCase) =>
        GetValue(summary, benchmarkCase, SummaryStyle.Default);

    /// <inheritdoc />
    public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style)
    {
        var statistics = summary[benchmarkCase]?.ResultStatistics;
        return statistics == null || statistics.Mean <= 0
            ? "-"
            : (BenchmarkData.TokenCount / (statistics.Mean / 1e9)).ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public bool IsAvailable(Summary summary) => true;

    /// <inheritdoc />
    public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;
}
