using System.Globalization;
using System.Text;

namespace DemaConsulting.PartOfSpeech.Trainer;

/// <summary>
///     Builds the summaries of the held-out sets and renders the generated reports and the per-token dump.
/// </summary>
/// <remarks>
///     <para>
///         The reports are deterministic: the same results give the same text. Every figure is formatted here and nowhere
///         else. The treebank is rendered as aggregate numbers and tag-level confusion only: no word, sentence or sentence id of
///         the treebank enters a report (its license does not allow that in this repository). The per-token dump is the one
///         place that holds treebank words; it is refused inside the repository.
///     </para>
///     <para>
///         Stateless and thread-safe.
///     </para>
/// </remarks>
internal static class EvaluationReport
{
    /// <summary>
    ///     The header that every generated Markdown report starts with.
    /// </summary>
    private const string MarkdownHeader = "<!-- markdownlint-disable MD013 MD060 -->\n\n";

    /// <summary>
    ///     Summarizes one set: figures, 95 percent intervals over documents and the operating points with their paired difference to 0.45.
    /// </summary>
    /// <param name="name">The name of the set.</param>
    /// <param name="tokens">The judged tokens.</param>
    /// <param name="selected">The selected operating points of the artifact (points without a threshold are skipped).</param>
    /// <param name="resamples">The number of bootstrap resamples.</param>
    /// <param name="seed">The seed of the bootstrap.</param>
    /// <returns>The summary.</returns>
    public static SetRecord Summarize(string name, IReadOnlyList<EvalToken> tokens, IEnumerable<PointRecord> selected, int resamples, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(selected);
        var tally = EvalTally.Of(tokens);
        var clusters = EvalTally.ByDocument(tokens);
        var record = new SetRecord
        {
            Name = name,
            Tokens = tally.Tokens,
            Documents = clusters.Count,
            Accuracy = tally.Accuracy,
            AccuracyLow = tally.Accuracy,
            AccuracyHigh = tally.Accuracy,
            AmbiguousTokens = tally.Ambiguous,
            AmbiguousAccuracy = tally.AmbiguousAccuracy,
            AmbiguousAccuracyLow = tally.AmbiguousAccuracy,
            AmbiguousAccuracyHigh = tally.AmbiguousAccuracy,
            Decisions = tally.Decisions,
            Harmful = tally.Harmful,
            HarmfulPer1000 = tally.HarmfulPer1000,
            HarmfulPer1000Low = tally.HarmfulPer1000,
            HarmfulPer1000High = tally.HarmfulPer1000,
            Ece = tally.Ece,
            EceLow = tally.Ece,
            EceHigh = tally.Ece,
            Nll = tally.Nll,
            NllLow = tally.Nll,
            NllHigh = tally.Nll,
            OutsideCandidates = tally.OutsideCandidates,
        };

        // The reference row (0.45) and one row for every selected point; each point gets the interval of its rate and the paired difference
        var rows = new List<(string Name, double Threshold)> { ("reference", Metrics.HarmfulConfidence) };
        rows.AddRange(selected.Where(p => p.Threshold.HasValue).Select(p => (p.Name, p.Threshold!.Value)));
        var statistics = new List<Func<EvalTally, double>> { t => t.Accuracy, t => t.HarmfulPer1000, t => t.Ece };
        foreach (var (_, threshold) in rows)
        {
            var index = ConfidenceCurve.IndexOf(threshold);
            statistics.Add(t => DevSelection.HarmfulPerAnswered(t, index));
            statistics.Add(t => DevSelection.HarmfulPerDecisions(t, index) - DevSelection.HarmfulPerDecisions(t, 0));
        }

        // The statistics of the other figures follow the points on the same draws: the ambiguous accuracy, the log-likelihood and
        // the precision and recall of every label that has a gold token or was chosen
        var otherStart = statistics.Count;
        statistics.Add(t => t.AmbiguousAccuracy);
        statistics.Add(t => t.Nll);
        var labels = Enumerable.Range(0, EvalTally.Labels.Count).Where(i => tally.TruePositives[i] + tally.FalseNegatives[i] + tally.FalsePositives[i] > 0).ToList();
        foreach (var label in labels)
        {
            var captured = label;
            statistics.Add(t => t.Precision(captured));
            statistics.Add(t => t.Recall(captured));
        }

        var intervals = clusters.Count == 0 ? null : ClusterBootstrap.Intervals(clusters, statistics, resamples, seed);
        if (intervals != null)
        {
            record.AccuracyLow = intervals[0].Low;
            record.AccuracyHigh = intervals[0].High;
            record.HarmfulPer1000Low = intervals[1].Low;
            record.HarmfulPer1000High = intervals[1].High;
            record.EceLow = intervals[2].Low;
            record.EceHigh = intervals[2].High;
            record.AmbiguousAccuracyLow = intervals[otherStart].Low;
            record.AmbiguousAccuracyHigh = intervals[otherStart].High;
            record.NllLow = intervals[otherStart + 1].Low;
            record.NllHigh = intervals[otherStart + 1].High;
        }

        for (var l = 0; l < labels.Count; l++)
        {
            var label = labels[l];
            var tag = new TagRecord
            {
                Tag = EvalTally.Labels[label],
                GoldTokens = tally.TruePositives[label] + tally.FalseNegatives[label],
                Precision = tally.Precision(label),
                Recall = tally.Recall(label),
            };
            tag.PrecisionLow = tag.Precision;
            tag.PrecisionHigh = tag.Precision;
            tag.RecallLow = tag.Recall;
            tag.RecallHigh = tag.Recall;
            if (intervals != null)
            {
                tag.PrecisionLow = intervals[otherStart + 2 + (2 * l)].Low;
                tag.PrecisionHigh = intervals[otherStart + 2 + (2 * l)].High;
                tag.RecallLow = intervals[otherStart + 3 + (2 * l)].Low;
                tag.RecallHigh = intervals[otherStart + 3 + (2 * l)].High;
            }

            record.Tags.Add(tag);
        }

        record.NounVerbConfusion.AddRange(tally.Confusion);

        for (var r = 0; r < rows.Count; r++)
        {
            var index = ConfidenceCurve.IndexOf(rows[r].Threshold);
            var point = new PointResult
            {
                Name = rows[r].Name,
                Threshold = rows[r].Threshold,
                Answered = tally.AnsweredAt[index],
                Harmful = tally.HarmfulAt[index],
                CoveragePercent = DevSelection.Coverage(tally, index),
                HarmfulPer1000Answered = DevSelection.HarmfulPerAnswered(tally, index),
                DeltaHarmfulPer1000Decisions = DevSelection.HarmfulPerDecisions(tally, index) - DevSelection.HarmfulPerDecisions(tally, 0),
            };
            point.HarmfulPer1000AnsweredLow = point.HarmfulPer1000Answered;
            point.HarmfulPer1000AnsweredHigh = point.HarmfulPer1000Answered;
            point.DeltaLow = point.DeltaHarmfulPer1000Decisions;
            point.DeltaHigh = point.DeltaHarmfulPer1000Decisions;
            if (intervals != null)
            {
                point.HarmfulPer1000AnsweredLow = intervals[3 + (2 * r)].Low;
                point.HarmfulPer1000AnsweredHigh = intervals[3 + (2 * r)].High;
                point.DeltaLow = intervals[4 + (2 * r)].Low;
                point.DeltaHigh = intervals[4 + (2 * r)].High;
            }

            record.Points.Add(point);
        }

        return record;
    }

    /// <summary>
    ///     Checks the stop conditions of the protocol against the results.
    /// </summary>
    /// <param name="results">The results, with the open test summary under the name of the open test split.</param>
    /// <param name="selection">The validated development selection (it holds the development rates the stop conditions compare with).</param>
    /// <returns>A message for every stop condition that fired; empty when none did.</returns>
    public static List<string> Tripwires(ResultsData results, SelectedOperatingPoints selection)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(selection);
        var fired = new List<string>();

        // A gold label outside the candidates for more than 1% of the tokens of a set
        foreach (var set in results.Sets.Where(s => s.Tokens > 0 && s.OutsideCandidates * 100.0 / s.Tokens > 1.0))
        {
            fired.Add($"{set.Name}: the gold label is outside the decoder's candidates for {ReportFormat.F(set.OutsideCandidates * 100.0 / set.Tokens, 2)}% of the tokens (limit 1%).");
        }

        var open = results.Sets.FirstOrDefault(s => s.Name == HeldOutEvaluator.Names.OpenTest);
        if (open != null)
        {
            // The open test accuracy more than 2.0 points below the development accuracy, with an interval that excludes the development value
            var dev = selection.Data.DevOpenAccuracyPercent;
            if (dev - open.Accuracy > 2.0 && open.AccuracyHigh < dev)
            {
                fired.Add($"{open.Name}: accuracy {ReportFormat.F(open.Accuracy, 2)}% is more than 2.0 points below the development accuracy {ReportFormat.F(dev, 2)}% and its interval excludes it.");
            }

            // A selected point whose harmful rate on the open test split is more than twice its development rate
            foreach (var point in selection.SelectedPoints)
            {
                var test = open.Points.FirstOrDefault(p => p.Name == point.Name);
                if (test != null && test.HarmfulPer1000Answered > 2.0 * point.HarmfulPer1000Answered)
                {
                    fired.Add($"{open.Name}: the harmful rate of the point '{point.Name}' ({ReportFormat.F(test.HarmfulPer1000Answered, 1)} per 1,000 answered) is more than twice its development rate ({ReportFormat.F(point.HarmfulPer1000Answered, 1)}).");
                }
            }

            if (open.Ece > 3.0)
            {
                fired.Add($"{open.Name}: the expected calibration error {ReportFormat.F(open.Ece, 2)}% is above 3%.");
            }
        }

        // The shape probe: more than 5% of its rows are harmful errors at 0.45
        var probe = results.Sets.FirstOrDefault(s => s.Name == HeldOutEvaluator.Names.Probe);
        if (probe != null && probe.Harmful > 0.05 * probe.Tokens)
        {
            fired.Add($"{probe.Name}: {probe.Harmful} harmful errors at 0.45 are more than 5% of its {probe.Tokens} rows.");
        }

        return fired;
    }

    /// <summary>
    ///     Renders the report of the development selection (<c>confidence-thresholds.md</c>).
    /// </summary>
    /// <param name="selection">The selection.</param>
    /// <returns>The Markdown text with LF line endings.</returns>
    public static string RenderDev(DevSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        var text = new StringBuilder(MarkdownHeader);
        text.Append("# Confidence Thresholds (Development Selection)\n\n");
        text.Append("Generated by the `thresholds` command of the trainer from the two development sets. It reads no held-out file. ");
        text.Append("The operating points below were chosen by the rule that was pre-registered in `tools/DemaConsulting.PartOfSpeech.Trainer/RUN-LOG.md` ");
        text.Append("before this report existed, and are the only thresholds that the held-out evaluation applies.\n\n");
        text.Append("## Model and data\n\n");
        text.Append($"- Model: uncompressed image SHA-256 `{selection.ModelImageSha256}`, softmax temperature {selection.Temperature.ToString("0.###", CultureInfo.InvariantCulture)}.\n");
        text.Append($"- Open development split: {ReportFormat.N(selection.Open.Tokens)} tokens, {ReportFormat.N(selection.Open.Decisions)} noun-verb decisions, accuracy {ReportFormat.F(selection.Open.Accuracy, 2)}%.\n");
        text.Append($"- Gold v2 development split without `to` and `not`: {ReportFormat.N(selection.Gold.Tokens)} targets, {ReportFormat.N(selection.Gold.Decisions)} noun-verb decisions, accuracy {ReportFormat.F(selection.Gold.Accuracy, 2)}%.\n");
        text.Append($"- Pooled: {ReportFormat.N(selection.Pooled.Decisions)} noun-verb decisions.\n");
        text.Append($"- At the harmful limit 0.45 the harmful errors are {selection.Open.Harmful} (open), {selection.Gold.Harmful} (gold v2) and {selection.Pooled.Harmful} (pooled), the figures of the frozen model in the run log.\n\n");
        text.Append("A decision is a token whose gold tag is a noun or a verb and for which both were candidates. A decision is answered at a threshold t when ");
        text.Append("the confidence of the chosen tag is at least t. A harmful error is an answered decision where the noun was tagged as a verb or the reverse. ");
        text.Append("Coverage is the share of the decisions that is answered.\n\n");

        text.Append($"## Selection rule `{OperatingPointRule.Id}`\n\n");
        text.Append("On the pooled development decisions the thresholds of the grid (0.45 to 0.95 in steps of 0.05, then 0.96 to 0.99) are scanned in ascending order ");
        text.Append("and the **first** one that meets both the harmful-error target and the coverage floor is the operating point. If none does, the point is none; ");
        text.Append("the targets are not relaxed afterwards.\n\n");
        ReportFormat.Table(
            text,
            new[] { "Point", "Harmful per 1,000 answered at most", "Coverage at least (%)" },
            selection.Points.Select(p => (IReadOnlyList<string>)new[] { p.Name, ReportFormat.F(p.MaxHarmfulPer1000Answered, 1), ReportFormat.F(p.MinCoveragePercent, 0) }));

        text.Append("## Selected operating points\n\n");
        var pointRows = new List<IReadOnlyList<string>>();
        for (var i = 0; i < selection.Points.Count; i++)
        {
            var point = selection.Points[i];
            if (point.Row == null)
            {
                pointRows.Add(new[] { point.Name, "none", "-", "-", "-", "-", "-" });
                continue;
            }

            var harmful = selection.HarmfulIntervals[i];
            var coverage = selection.CoverageIntervals[i];
            pointRows.Add(new[]
            {
                point.Name,
                OperatingPointRule.Format(point.Row.Threshold),
                ReportFormat.F(point.Row.CoveragePercent, 1),
                coverage == null ? "-" : $"[{ReportFormat.F(coverage.Low, 1)}, {ReportFormat.F(coverage.High, 1)}]",
                ReportFormat.F(point.Row.HarmfulPer1000Answered, 1),
                harmful == null ? "-" : $"[{ReportFormat.F(harmful.Low, 1)}, {ReportFormat.F(harmful.High, 1)}]",
                $"{ReportFormat.N(point.Row.Harmful)} of {ReportFormat.N(point.Row.Answered)}",
            });
        }

        ReportFormat.Table(text, new[] { "Point", "Threshold", "Coverage (%)", "Coverage 95% interval", "Harmful per 1,000 answered", "Harmful 95% interval", "Harmful of answered" }, pointRows);
        text.Append($"The intervals are percentile intervals of {ReportFormat.N(selection.Resamples)} bootstrap resamples over documents (seed {selection.Seed}). ");
        text.Append("They show the noise of the selection: the gold v2 development split alone has few harmful errors. ");
        text.Append("A threshold chosen on development data is optimistic; the held-out evaluation reports what it does on data that were not used to choose it.\n\n");

        Curve(text, "Pooled development decisions", selection.Pooled);
        Curve(text, "Open development split", selection.Open);
        Curve(text, "Gold v2 development split", selection.Gold);

        text.Append("## Reliability of the pooled decisions\n\n");
        text.Append("The decisions grouped by the confidence of the chosen tag. A calibrated model has a share of correct decisions close to the mean confidence.\n\n");
        ReportFormat.Table(
            text,
            new[] { "Confidence", "Decisions", "Mean confidence", "Correct (%)", "Harmful at 0.45" },
            ConfidenceCurve.Reliability(selection.Pooled).Select(r => (IReadOnlyList<string>)new[]
            {
                $"{ReportFormat.F(r.Low, 2)} to {(r.High >= 1.0 ? "1.00" : "below " + ReportFormat.F(r.High, 2))}",
                ReportFormat.N(r.Decisions),
                ReportFormat.F(r.MeanConfidence, 3),
                ReportFormat.F(r.CorrectPercent, 1),
                ReportFormat.N(r.Harmful),
            }));
        return Finish(text);
    }

    /// <summary>
    ///     Renders the report of the held-out run (<c>test-results.md</c>).
    /// </summary>
    /// <param name="results">The results.</param>
    /// <param name="scores">The judged tokens of the run.</param>
    /// <returns>The Markdown text with LF line endings.</returns>
    public static string RenderResults(ResultsData results, HeldOutScores scores)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(scores);
        var text = new StringBuilder(MarkdownHeader);
        text.Append("# Held-Out Evaluation Results\n\n");
        text.Append("Generated by the `evaluate` command of the trainer, once, for the frozen model and for the operating points of ");
        text.Append("`confidence-thresholds.md` (selected on development data before this run). Nothing was tuned on these sets. ");
        text.Append("The shape probe and the challenge set have labels written by language models and are small; they are reported separately and never merged into a headline number.\n\n");
        text.Append("## Run\n\n");
        text.Append($"- Model image SHA-256: `{results.ModelImageSha256}`\n");
        text.Append($"- Selection artifact SHA-256: `{results.SelectionSha256}` (rule `{results.RuleId}`)\n");
        text.Append($"- Bootstrap: {ReportFormat.N(results.Resamples)} resamples over documents, seed {results.Seed}; 95% percentile intervals. Sets without documents resample rows or sentences.\n");
        text.Append($"- Runtime: {results.Runtime}\n");
        text.Append($"- Throughput of the default tagger on the open test split, one thread: {ReportFormat.N((long)results.TokensPerSecond)} tokens per second.\n\n");
        text.Append("Inputs:\n\n");
        ReportFormat.Table(text, new[] { "Input", "SHA-256" }, results.Inputs.Select(i => (IReadOnlyList<string>)new[] { i.Name, "`" + i.Sha256 + "`" }));
        text.Append("Stop conditions of the protocol: ");
        text.Append(results.TripwiresFired.Count == 0 ? "none fired.\n\n" : "**fired**:\n\n" + string.Join("\n", results.TripwiresFired.Select(t => "- " + t)) + "\n\n");

        text.Append("## Accuracy and harmful errors\n\n");
        ReportFormat.Table(
            text,
            new[] { "Set", "Tokens", "Documents", "Accuracy (%)", "Ambiguous accuracy (%)", "Decisions", "Harmful at 0.45", "Harmful per 1,000 decisions", "ECE (%)", "NLL", "Gold outside candidates" },
            results.Sets.Select(s => (IReadOnlyList<string>)new[]
            {
                s.Name,
                ReportFormat.N(s.Tokens),
                ReportFormat.N(s.Documents),
                ReportFormat.WithInterval(s.Accuracy, s.AccuracyLow, s.AccuracyHigh, 2),
                ReportFormat.WithInterval(s.AmbiguousAccuracy, s.AmbiguousAccuracyLow, s.AmbiguousAccuracyHigh, 2),
                ReportFormat.N(s.Decisions),
                ReportFormat.N(s.Harmful),
                ReportFormat.WithInterval(s.HarmfulPer1000, s.HarmfulPer1000Low, s.HarmfulPer1000High, 1),
                ReportFormat.WithInterval(s.Ece, s.EceLow, s.EceHigh, 2),
                ReportFormat.WithInterval(s.Nll, s.NllLow, s.NllHigh, 4),
                ReportFormat.N(s.OutsideCandidates),
            }));

        text.Append("## Operating points on the held-out sets\n\n");
        text.Append("The thresholds are the ones selected on development data. Coverage is the share of the noun-verb decisions answered; the paired difference is the harmful errors per 1,000 decisions at the point minus those at 0.45 on the same decisions.\n\n");
        var pointRows = new List<IReadOnlyList<string>>();
        foreach (var set in results.Sets)
        {
            foreach (var point in set.Points)
            {
                pointRows.Add(new[]
                {
                    set.Name,
                    point.Name,
                    OperatingPointRule.Format(point.Threshold),
                    ReportFormat.F(point.CoveragePercent, 1),
                    ReportFormat.WithInterval(point.HarmfulPer1000Answered, point.HarmfulPer1000AnsweredLow, point.HarmfulPer1000AnsweredHigh, 1),
                    $"{ReportFormat.N(point.Harmful)} of {ReportFormat.N(point.Answered)}",
                    ReportFormat.WithInterval(point.DeltaHarmfulPer1000Decisions, point.DeltaLow, point.DeltaHigh, 1),
                });
            }
        }

        ReportFormat.Table(text, new[] { "Set", "Point", "Threshold", "Coverage (%)", "Harmful per 1,000 answered", "Harmful of answered", "Paired difference to 0.45" }, pointRows);

        Confusion(text, scores);

        Subsets(text, scores);
        Probe(text, scores);
        Challenge(text, scores);
        foreach (var set in results.Sets)
        {
            Tags(text, set);
        }

        text.Append("## Treebank views\n\n");
        text.Append("The treebank is scored after the convention mapping of `data/CONVENTIONS.md` (the primary view, which makes the gold follow the conventions of this library's training data) and against the raw treebank tags (the second view). ");
        text.Append($"The mapping changed {ReportFormat.N(scores.UdMappedWords)} of {ReportFormat.N(scores.UdMapped.Count)} words. Only aggregate figures and tag-level confusion are published; no word or sentence of the treebank is part of this repository.\n\n");
        text.Append("The largest tag-level confusions of the mapped view (gold tag, tag chosen, count):\n\n");
        ReportFormat.Table(text, new[] { "Gold", "Chosen", "Count" }, TopPairs(EvalTally.Of(scores.UdMapped), 15));
        return Finish(text);
    }

    /// <summary>
    ///     Finishes a Markdown report: no trailing blank line and exactly one final line feed.
    /// </summary>
    /// <param name="text">The report.</param>
    /// <returns>The text.</returns>
    private static string Finish(StringBuilder text) => text.ToString().TrimEnd('\n') + "\n";

    /// <summary>
    ///     Writes the per-token dump of a set as a tab-separated file.
    /// </summary>
    /// <param name="directory">The folder, which must be outside the repository (checked by the caller with <see cref="DataGuard.EnsureOutsideRepository"/>).</param>
    /// <param name="name">The name of the set (the file is <c>name.tsv</c>).</param>
    /// <param name="tokens">The judged tokens.</param>
    public static void WriteDump(string directory, string name, IEnumerable<EvalToken> tokens)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(tokens);
        var text = new StringBuilder(string.Join("\t", "set", "doc", "sid", "idx", "form", "gold", "tag", "confidence"));
        for (var i = 0; i < UposExtensions.Count; i++)
        {
            text.Append("\tp_").Append(((Upos)i).ToTagString());
        }

        text.Append('\n');
        foreach (var token in tokens)
        {
            text.Append(token.Set).Append('\t').Append(token.DocumentId).Append('\t').Append(token.SentenceId).Append('\t')
                .Append(token.Index.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(token.Form).Append('\t')
                .Append(token.Gold).Append('\t').Append(token.Chosen).Append('\t').Append(token.Confidence.ToString("R", CultureInfo.InvariantCulture));
            foreach (var p in token.Probabilities)
            {
                text.Append('\t').Append(p.ToString("R", CultureInfo.InvariantCulture));
            }

            text.Append('\n');
        }

        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name + ".tsv"), text.ToString(), new UTF8Encoding(false));
    }

    /// <summary>
    ///     Lists the sets and views of a run in the order of the report.
    /// </summary>
    /// <param name="scores">The judged tokens.</param>
    /// <returns>The name and the tokens of every view.</returns>
    public static List<(string Name, List<EvalToken> Tokens)> Views(HeldOutScores scores)
    {
        ArgumentNullException.ThrowIfNull(scores);
        return new List<(string, List<EvalToken>)>
        {
            (HeldOutEvaluator.Names.OpenTest, scores.OpenTest),
            (HeldOutEvaluator.Names.GoldTest, scores.Gold),
            (HeldOutEvaluator.Names.Probe, scores.Probe),
            (HeldOutEvaluator.Names.Challenge, scores.Challenge),
            (HeldOutEvaluator.Names.UdMapped, scores.UdMapped),
            (HeldOutEvaluator.Names.UdRaw, scores.UdRaw),
        };
    }

    /// <summary>
    ///     Appends the noun-verb confusion of every view that has noun-verb decisions: one row for each view, with the cells in
    ///     the order that <see cref="EvalTally.Confusion"/> stores them (gold noun, then gold verb, each answered with another
    ///     tag, a noun or a verb).
    /// </summary>
    /// <param name="text">The text to append to.</param>
    /// <param name="scores">The judged tokens.</param>
    internal static void Confusion(StringBuilder text, HeldOutScores scores)
    {
        text.Append("## Noun-verb confusion\n\n");
        var confusion = new List<IReadOnlyList<string>>();
        foreach (var (name, tokens) in Views(scores))
        {
            var tally = EvalTally.Of(tokens);
            if (tally.Decisions > 0)
            {
                confusion.Add(new[]
                {
                    name,
                    ReportFormat.N(tally.Confusion[0]),
                    ReportFormat.N(tally.Confusion[1]),
                    ReportFormat.N(tally.Confusion[2]),
                    ReportFormat.N(tally.Confusion[3]),
                    ReportFormat.N(tally.Confusion[4]),
                    ReportFormat.N(tally.Confusion[5]),
                });
            }
        }

        ReportFormat.Table(text, new[] { "Set", "Noun as other", "Noun as noun", "Noun as verb", "Verb as other", "Verb as noun", "Verb as verb" }, confusion);
    }

    /// <summary>
    ///     Appends the curve table of one tally.
    /// </summary>
    /// <param name="text">The text to append to.</param>
    /// <param name="title">The title of the section.</param>
    /// <param name="tally">The tally.</param>
    private static void Curve(StringBuilder text, string title, EvalTally tally)
    {
        text.Append("## Curve: ").Append(title).Append("\n\n");
        ReportFormat.Table(
            text,
            new[] { "Threshold", "Answered", "Coverage (%)", "Harmful", "Harmful per 1,000 answered", "Harmful per 1,000 decisions" },
            ConfidenceCurve.Of(tally).Select(r => (IReadOnlyList<string>)new[]
            {
                OperatingPointRule.Format(r.Threshold),
                ReportFormat.N(r.Answered),
                ReportFormat.F(r.CoveragePercent, 1),
                ReportFormat.N(r.Harmful),
                ReportFormat.F(r.HarmfulPer1000Answered, 1),
                ReportFormat.F(r.HarmfulPer1000Decisions, 1),
            }));
    }

    /// <summary>
    ///     Appends the gold v2 subsets (the cleaner subset and the convention words).
    /// </summary>
    /// <param name="text">The text to append to.</param>
    /// <param name="scores">The judged tokens.</param>
    private static void Subsets(StringBuilder text, HeldOutScores scores)
    {
        text.Append("## Gold v2 subsets\n\n");
        var rows = new List<IReadOnlyList<string>>();
        foreach (var (name, tokens) in new[]
        {
            ("all targets without `to` and `not`", scores.Gold),
            ("labelers agreed fully (cleaner subset)", scores.GoldFullAgreement),
            ("`to` and `not` (convention difference, reported separately)", scores.GoldConvention),
        })
        {
            var tally = EvalTally.Of(tokens);
            rows.Add(new[] { name, ReportFormat.N(tally.Tokens), ReportFormat.F(tally.Accuracy, 2), ReportFormat.N(tally.Decisions), ReportFormat.N(tally.Harmful) });
        }

        ReportFormat.Table(text, new[] { "Subset", "Targets", "Accuracy (%)", "Decisions", "Harmful at 0.45" }, rows);
    }

    /// <summary>
    ///     Appends the shape probe by shape.
    /// </summary>
    /// <param name="text">The text to append to.</param>
    /// <param name="scores">The judged tokens.</param>
    private static void Probe(StringBuilder text, HeldOutScores scores)
    {
        text.Append("## Shape probe by shape\n\n");
        ReportFormat.Table(
            text,
            new[] { "Shape", "Rows", "Accuracy (%)", "Harmful at 0.45" },
            scores.ProbeShapes.Select(p =>
            {
                var tally = EvalTally.Of(p.Value);
                return (IReadOnlyList<string>)new[] { p.Key, ReportFormat.N(tally.Tokens), ReportFormat.F(tally.Accuracy, 2), ReportFormat.N(tally.Harmful) };
            }));
    }

    /// <summary>
    ///     Appends the challenge set by sentence.
    /// </summary>
    /// <param name="text">The text to append to.</param>
    /// <param name="scores">The judged tokens.</param>
    private static void Challenge(StringBuilder text, HeldOutScores scores)
    {
        text.Append("## Challenge set by sentence\n\n");
        text.Append("The tokens that the model did not tag as the gold says (gold tag, chosen tag, confidence); 32 sentences are a smoke test, not a measurement of accuracy.\n\n");
        var rows = new List<IReadOnlyList<string>>();
        foreach (var sentence in scores.Challenge.GroupBy(t => t.SentenceId, StringComparer.Ordinal))
        {
            var misses = sentence.Where(t => !t.Outcome.Correct).ToList();
            rows.Add(new[]
            {
                sentence.Key,
                ReportFormat.N(sentence.Count()),
                ReportFormat.N(misses.Count),
                string.Join("; ", misses.Select(m => $"{m.Form} {m.Gold}/{m.Chosen} {ReportFormat.F(m.Confidence, 2)}")),
            });
        }

        ReportFormat.Table(text, new[] { "Sentence", "Tokens", "Misses", "Misses in detail" }, rows);
    }

    /// <summary>
    ///     Appends the precision and recall of every tag of a set or view, with 95 percent intervals.
    /// </summary>
    /// <param name="text">The text to append to.</param>
    /// <param name="set">The summary of the set or view.</param>
    private static void Tags(StringBuilder text, SetRecord set)
    {
        text.Append("## Precision and recall by tag: ").Append(set.Name).Append("\n\n");
        ReportFormat.Table(
            text,
            new[] { "Tag", "Gold tokens", "Precision (%)", "Recall (%)" },
            set.Tags.Select(t => (IReadOnlyList<string>)new[]
            {
                t.Tag,
                ReportFormat.N(t.GoldTokens),
                ReportFormat.WithInterval(t.Precision, t.PrecisionLow, t.PrecisionHigh, 2),
                ReportFormat.WithInterval(t.Recall, t.RecallLow, t.RecallHigh, 2),
            }));
    }

    /// <summary>
    ///     Lists the most frequent label pairs where the model differs from the gold.
    /// </summary>
    /// <param name="tally">The tally.</param>
    /// <param name="count">The number of pairs.</param>
    /// <returns>The rows (gold, chosen, count).</returns>
    private static List<IReadOnlyList<string>> TopPairs(EvalTally tally, int count)
    {
        var n = EvalTally.Labels.Count;
        return Enumerable.Range(0, n * n)
            .Where(i => i / n != i % n && tally.Pairs[i] > 0)
            .OrderByDescending(i => tally.Pairs[i])
            .ThenBy(i => i)
            .Take(count)
            .Select(i => (IReadOnlyList<string>)new[] { EvalTally.Labels[i / n], EvalTally.Labels[i % n], ReportFormat.N(tally.Pairs[i]) })
            .ToList();
    }
}
