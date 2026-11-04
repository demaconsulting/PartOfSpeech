using System.Globalization;

namespace DemaConsulting.PartOfSpeech.Trainer;

/// <summary>
///     Everything that one row of the run log records about a run.
/// </summary>
internal sealed class RunSummary
{
    /// <summary>
    ///     Gets or sets the label of the run.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the one-line description of the settings.
    /// </summary>
    public string Settings { get; set; } = "-";

    /// <summary>
    ///     Gets or sets the development outcomes.
    /// </summary>
    public DevResult Result { get; set; } = new(new List<Outcome>(), new List<Outcome>(), new List<Outcome>());

    /// <summary>
    ///     Gets or sets the number of rows of the model.
    /// </summary>
    public int Rows { get; set; }

    /// <summary>
    ///     Gets or sets the number of weights of the model.
    /// </summary>
    public int Weights { get; set; }

    /// <summary>
    ///     Gets or sets the size of the uncompressed file in bytes.
    /// </summary>
    public int RawBytes { get; set; }

    /// <summary>
    ///     Gets or sets the size of the compressed file in bytes (0 when unknown).
    /// </summary>
    public int DeflatedBytes { get; set; }

    /// <summary>
    ///     Gets or sets the training time in seconds (0 for an evaluation only).
    /// </summary>
    public double TrainSeconds { get; set; }
}

/// <summary>
///     Formats and appends the rows of the run log (<c>RUN-LOG.md</c>), so that every experiment is recorded the moment it finishes.
/// </summary>
/// <remarks>
///     The log is a Markdown table that must be the last thing in the file; rows are appended. All numbers are
///     measured through the real decoder on the development sets only. Stateless; the append is not safe against
///     concurrent writers (the experiments run one at a time).
/// </remarks>
internal static class RunLog
{
    /// <summary>
    ///     The header of the table (two lines), written when a log file is created.
    /// </summary>
    public const string Header =
        "| Run | Settings | Open acc % | Open ambiguous acc % | Open harmful | Open harmful per 1k N/V | Gold acc % | Gold harmful | Pooled harmful per 1k N/V | NLL | ECE % | Rows | Weights | Raw KB | Deflated KB | Train s |\n" +
        "| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |";

    /// <summary>
    ///     Formats the row of a run.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <returns>The Markdown table row.</returns>
    public static string Row(RunSummary run)
    {
        var open = Metrics.Compute(run.Result.Open);
        var gold = Metrics.Compute(run.Result.Gold);
        var pooledDecisions = open.NounVerbDecisions + gold.NounVerbDecisions;
        var pooled = pooledDecisions == 0 ? 0 : 1000.0 * (open.HarmfulErrors + gold.HarmfulErrors) / pooledDecisions;
        string F(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);
        return string.Join(
            " | ",
            "| " + run.Label,
            run.Settings,
            F(open.Accuracy, "0.00"),
            F(open.AmbiguousAccuracy, "0.00"),
            open.HarmfulErrors.ToString(CultureInfo.InvariantCulture),
            F(open.HarmfulPer1000, "0.0"),
            F(gold.Accuracy, "0.00"),
            gold.HarmfulErrors.ToString(CultureInfo.InvariantCulture),
            F(pooled, "0.0"),
            F(open.Nll, "0.0000"),
            F(open.Ece, "0.00"),
            run.Rows.ToString(CultureInfo.InvariantCulture),
            run.Weights.ToString(CultureInfo.InvariantCulture),
            F(run.RawBytes / 1024.0, "0"),
            run.DeflatedBytes == 0 ? "-" : F(run.DeflatedBytes / 1024.0, "0"),
            F(run.TrainSeconds, "0.0") + " |");
    }

    /// <summary>
    ///     Appends the row of a run to a log file, creating the file with its header when it does not exist.
    /// </summary>
    /// <param name="path">The full path of the log file.</param>
    /// <param name="run">The run.</param>
    /// <exception cref="IOException">The file cannot be written.</exception>
    public static void Append(string path, RunSummary run)
    {
        if (!File.Exists(path))
        {
            File.WriteAllText(path, Header + "\n");
        }
        else if (!File.ReadAllText(path).EndsWith("\n", StringComparison.Ordinal))
        {
            File.AppendAllText(path, "\n");
        }

        File.AppendAllText(path, Row(run) + "\n");
    }
}
