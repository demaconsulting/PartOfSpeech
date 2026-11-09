using System.Globalization;

namespace DemaConsulting.PartOfSpeech.Trainer;

/// <summary>
///     The results of a tagger on one single-shape tranche of the hard-shape development set: the row of its shape and, for the headings, the rows of the kinds.
/// </summary>
internal sealed class ShapesDevSingleShapeResult
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ShapesDevSingleShapeResult"/> class.
    /// </summary>
    /// <param name="spec">The specification of the shape of the tranche.</param>
    /// <param name="overall">The row of the shape: all items of the tranche.</param>
    /// <param name="kinds">The rows of the kinds, in the order of <see cref="ShapesDevShapeSpec.Kinds"/> (empty for a shape without kinds).</param>
    public ShapesDevSingleShapeResult(ShapesDevShapeSpec spec, ShapesDevShapeResult overall, IReadOnlyList<ShapesDevShapeResult> kinds)
    {
        Spec = spec;
        Overall = overall;
        Kinds = kinds;
    }

    /// <summary>
    ///     Gets the specification of the shape of the tranche.
    /// </summary>
    public ShapesDevShapeSpec Spec { get; }

    /// <summary>
    ///     Gets the row of the shape: all items of the tranche.
    /// </summary>
    public ShapesDevShapeResult Overall { get; }

    /// <summary>
    ///     Gets the rows of the kinds (noun targets, imperative targets and PROPN controls for the headings; empty otherwise).
    /// </summary>
    public IReadOnlyList<ShapesDevShapeResult> Kinds { get; }
}

/// <summary>
///     Runs a tagger through the real decoder on one single-shape tranche of the hard-shape development set and summarizes the outcomes.
/// </summary>
/// <remarks>
///     <para>
///         The judged word is scored with <see cref="Metrics.Judge"/> under the scoring of the shape (<see cref="ShapesDevShapeSpec"/>): by
///         class for the two noun shapes, by exact tag for the headings. A harmful error keeps its definition: the answer swaps noun and
///         verb (the class under class scoring, NOUN and VERB under exact scoring) with a confidence of at least
///         <see cref="Metrics.HarmfulConfidence"/>, and only where both were candidates. Under exact scoring a capitalized common noun that is
///         tagged PROPN is wrong but not harmful (it is not a noun-verb swap), and a PROPN control is never a noun-verb decision, so it can
///         be wrong but never harmful. A confident wrong answer is a target item that is wrong with a confidence of at least
///         <see cref="Metrics.ConfidentWrongConfidence"/>.
///     </para>
///     <para>
///         Stateless and thread-safe.
///     </para>
/// </remarks>
internal static class ShapesDevTrancheEvaluator
{
    /// <summary>
    ///     Tags every item and judges its judged word under the scoring of its tranche.
    /// </summary>
    /// <param name="tagger">The tagger (created with all alternatives, see <see cref="DevEvaluator.CreateTagger"/>).</param>
    /// <param name="items">The items, all of single-shape tranches.</param>
    /// <returns>The outcomes, one per item in the order of <paramref name="items"/>.</returns>
    /// <exception cref="ArgumentException">An item does not belong to a single-shape tranche.</exception>
    public static List<Outcome> Judge(Tagger tagger, IReadOnlyList<ShapesDevItem> items)
    {
        ArgumentNullException.ThrowIfNull(tagger);
        ArgumentNullException.ThrowIfNull(items);
        var lexicon = EmbeddedModel.Lexicon;
        var scratch = new byte[lexicon.ScratchSize];
        var outcomes = new List<Outcome>(items.Count);
        foreach (var item in items)
        {
            var spec = ShapesDevSets.SpecOf(item.Tranche) ?? throw new ArgumentException($"The item {item.Id} does not belong to a single-shape tranche.", nameof(items));
            var tagged = tagger.Tag(item.Tokens);
            var ambiguous = DevEvaluator.IsLexiconAmbiguous(lexicon, scratch, item.Tokens[item.Target]);
            outcomes.Add(Metrics.Judge("shapes-dev:" + item.SourceId, spec.GoldLabelOf(item), tagged.Tokens[item.Target], spec.LabelOf, spec.NounLabel, spec.VerbLabel, ambiguous));
        }

        return outcomes;
    }

    /// <summary>
    ///     Tags every item of one tranche and summarizes the outcomes.
    /// </summary>
    /// <param name="tagger">The tagger.</param>
    /// <param name="tranche">The single-shape tranche the items were read from.</param>
    /// <param name="items">The items of the tranche.</param>
    /// <returns>The results.</returns>
    public static ShapesDevSingleShapeResult Evaluate(Tagger tagger, ShapesDevTranche tranche, IReadOnlyList<ShapesDevItem> items) =>
        Summarize(tranche, items, Judge(tagger, items));

    /// <summary>
    ///     Summarizes the outcomes of the items of one single-shape tranche.
    /// </summary>
    /// <param name="tranche">The single-shape tranche.</param>
    /// <param name="items">The items of the tranche.</param>
    /// <param name="outcomes">The outcome of every item, in the same order.</param>
    /// <returns>The row of the shape and, for the headings, the rows of the kinds (zero counts without items).</returns>
    /// <exception cref="ArgumentException">The tranche is not a single-shape tranche, the lists differ in length, or an item is not of the tranche or has another shape.</exception>
    public static ShapesDevSingleShapeResult Summarize(ShapesDevTranche tranche, IReadOnlyList<ShapesDevItem> items, IReadOnlyList<Outcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(outcomes);
        var spec = ShapesDevSets.SpecOf(tranche) ?? throw new ArgumentException($"The tranche {tranche} is not a single-shape tranche.", nameof(tranche));
        if (items.Count != outcomes.Count)
        {
            throw new ArgumentException("There must be one outcome per item.", nameof(outcomes));
        }

        if (items.Any(i => i.Tranche != tranche || i.Shape != spec.Shape))
        {
            throw new ArgumentException($"An item is not of the {DevSets.ShapesDevNameOf(tranche)} tranche or has another shape than {spec.Shape}.", nameof(items));
        }

        var overall = Count(spec.Shape, items, outcomes, _ => true);
        var kinds = spec.Kinds
            .Select(kind => Count(
                $"{spec.Shape}: {(kind.Role == ShapesDevSets.TargetRole ? kind.Name + " targets" : kind.Tag.ToTagString() + " controls")}",
                items,
                outcomes,
                item => item.Kind == kind.Name))
            .ToList();
        return new ShapesDevSingleShapeResult(spec, overall, kinds);
    }

    /// <summary>
    ///     Counts the items that a filter selects.
    /// </summary>
    /// <param name="label">The label of the row.</param>
    /// <param name="items">All items.</param>
    /// <param name="outcomes">All outcomes.</param>
    /// <param name="selected">Tells whether an item is part of the row.</param>
    /// <returns>The counts of the row.</returns>
    private static ShapesDevShapeResult Count(string label, IReadOnlyList<ShapesDevItem> items, IReadOnlyList<Outcome> outcomes, Func<ShapesDevItem, bool> selected)
    {
        int targets = 0, targetCorrect = 0, decisions = 0, harmful = 0, confident = 0, controls = 0, controlCorrect = 0, authoredTargets = 0, authoredControls = 0;
        for (var i = 0; i < items.Count; i++)
        {
            if (!selected(items[i]))
            {
                continue;
            }

            var authored = items[i].Origin == ShapesDevSets.AuthoredOrigin ? 1 : 0;
            if (items[i].IsTarget)
            {
                targets++;
                targetCorrect += outcomes[i].Correct ? 1 : 0;
                decisions += outcomes[i].NounVerb ? 1 : 0;
                harmful += outcomes[i].Harmful ? 1 : 0;
                confident += Metrics.IsConfidentlyWrong(outcomes[i]) ? 1 : 0;
                authoredTargets += authored;
            }
            else
            {
                controls++;
                controlCorrect += outcomes[i].Correct ? 1 : 0;
                authoredControls += authored;
            }
        }

        return new ShapesDevShapeResult(label, targets, targetCorrect, decisions, harmful, controls, controlCorrect, authoredTargets, authoredControls, confident);
    }
}

/// <summary>
///     Renders the single-shape tranches of the hard-shape development set as Markdown.
/// </summary>
/// <remarks>
///     The sections are new text blocks; the sections of the standard and the hard tranche are not touched. Like them, the text depends on
///     the results only (invariant culture, fixed column order, no time stamp or machine name).
/// </remarks>
internal static partial class ShapesDevReport
{
    /// <summary>
    ///     Renders the section of one single-shape tranche: heading, item counts, the file hashes, the scoring note and the table.
    /// </summary>
    /// <param name="label">The label of the run (for example the weights file name).</param>
    /// <param name="tranche">The single-shape tranche.</param>
    /// <param name="datasetSha256">The SHA-256 of the development file, or an empty text for none.</param>
    /// <param name="result">The results of the tranche.</param>
    /// <param name="weightsSha256">The SHA-256 of the weights file, or an empty text for none.</param>
    /// <returns>The lines of the section, without line terminators.</returns>
    public static List<string> RenderTranche(string label, ShapesDevTranche tranche, string datasetSha256, ShapesDevSingleShapeResult result, string weightsSha256 = "")
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(datasetSha256);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(weightsSha256);
        var lines = new List<string>
        {
            "## Shapes development set, " + DevSets.ShapesDevNameOf(tranche) + " tranche: " + label,
            string.Empty,
            ItemsLine(result.Overall),
        };
        if (datasetSha256.Length > 0)
        {
            lines.Add($"- Development file SHA-256: `{datasetSha256}`.");
        }

        if (weightsSha256.Length > 0)
        {
            lines.Add($"- Weights file SHA-256: `{weightsSha256}`.");
        }

        lines.Add(ScoringLine(result.Spec));
        lines.Add("- The set is for development: it selects settings and is never a held-out figure.");
        lines.Add(string.Empty);
        lines.Add("| shape | target items | target accuracy | 95% interval | noun-verb decisions | harmful errors | harmful per 1,000 decisions | wrong at confidence >= 0.95 | control items | control accuracy |");
        lines.Add("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var row in new[] { result.Overall }.Concat(result.Kinds))
        {
            lines.Add(TrancheRow(row));
        }

        return lines;
    }

    /// <summary>
    ///     Renders the note that a single-shape tranche is not scored because its file is not present.
    /// </summary>
    /// <param name="tranche">The single-shape tranche.</param>
    /// <returns>The line.</returns>
    public static string RenderTrancheMissing(ShapesDevTranche tranche) =>
        $"- The {DevSets.ShapesDevNameOf(tranche)} tranche {DevSets.ShapesDevFileOf(tranche)} is not present; it is not scored.";

    /// <summary>
    ///     Renders the scoring note of a shape.
    /// </summary>
    /// <param name="spec">The specification of the shape.</param>
    /// <returns>The line.</returns>
    private static string ScoringLine(ShapesDevShapeSpec spec)
    {
        var how = spec.Scoring == ShapesDevScoring.Class
            ? "by class (Noun: NOUN and PROPN; Verb: VERB); a noun-verb decision is a target whose candidates include both classes; a harmful error is a swapped class"
            : "by exact tag (a NOUN or VERB heading answered PROPN is wrong, but it is not harmful, and a PROPN control is never a noun-verb decision); a noun-verb decision is a target whose candidates include NOUN and VERB; a harmful error is NOUN answered for VERB or VERB for NOUN";
        return string.Format(
            CultureInfo.InvariantCulture,
            "- Scoring: the judged word of every item through the real decoder with all alternatives, {0} with a confidence of at least {1:0.00}; wrong at confidence >= {2:0.00} counts the target items answered wrongly with a confidence of at least {2:0.00}.",
            how,
            Metrics.HarmfulConfidence,
            Metrics.ConfidentWrongConfidence);
    }

    /// <summary>
    ///     Renders one table row of a single-shape tranche: a column that does not apply to the row (no target or no control items) is <c>-</c>.
    /// </summary>
    /// <param name="row">The counts of the row.</param>
    /// <returns>The row.</returns>
    private static string TrancheRow(ShapesDevShapeResult row)
    {
        var hasTargets = row.TargetItems > 0;
        var harmful = row.NounVerbDecisions == 0 ? "-" : row.HarmfulPer1000.ToString("0.0", CultureInfo.InvariantCulture);
        return string.Format(
            CultureInfo.InvariantCulture,
            "| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} | {9} |",
            row.Shape,
            hasTargets ? row.TargetItems.ToString(CultureInfo.InvariantCulture) : "-",
            hasTargets ? Percent(row.TargetAccuracy) : "-",
            hasTargets ? Interval(row.TargetCorrect, row.TargetItems) : "-",
            hasTargets ? row.NounVerbDecisions.ToString(CultureInfo.InvariantCulture) : "-",
            hasTargets ? row.HarmfulErrors.ToString(CultureInfo.InvariantCulture) : "-",
            hasTargets ? harmful : "-",
            hasTargets ? row.ConfidentWrong.ToString(CultureInfo.InvariantCulture) : "-",
            row.ControlItems > 0 ? row.ControlItems.ToString(CultureInfo.InvariantCulture) : "-",
            row.ControlItems > 0 ? Percent(row.ControlAccuracy) : "-");
    }
}

/// <summary>
///     The <c>eval-shapes</c> command without the console: selects the tranches that <c>--tranche</c> names, reads them behind the
///     development guard, scores them and returns the Markdown lines.
/// </summary>
/// <remarks>
///     The command is a function of the repository root, a tagger and the option, so a test can run it on a temporary root. The standard
///     and hard sections are the ones <see cref="ShapesDevReport"/> has always written; the single-shape tranches add sections after them.
///     Stateless and thread-safe.
/// </remarks>
internal static class ShapesDevCommand
{
    /// <summary>
    ///     The values of the <c>--tranche</c> option.
    /// </summary>
    public static readonly IReadOnlyList<string> Selections = new[] { "standard", "hard", "both", "compound-head", "headings", "named-state", "new" };

    /// <summary>
    ///     The single-shape tranches, in report order; <c>new</c> selects all of them.
    /// </summary>
    public static readonly IReadOnlyList<ShapesDevTranche> NewTranches = new[]
    {
        ShapesDevTranche.CompoundHead,
        ShapesDevTranche.Headings,
        ShapesDevTranche.NamedState,
    };

    /// <summary>
    ///     Checks the value of the <c>--tranche</c> option.
    /// </summary>
    /// <param name="which">The value.</param>
    /// <exception cref="InvalidOperationException">The value is not one of <see cref="Selections"/>.</exception>
    public static void EnsureSelection(string which)
    {
        ArgumentNullException.ThrowIfNull(which);
        if (!Selections.Contains(which))
        {
            throw new InvalidOperationException("--tranche must be standard, hard, both, compound-head, headings, named-state or new.");
        }
    }

    /// <summary>
    ///     Scores the tranches that the option names and renders the report.
    /// </summary>
    /// <param name="root">The repository root.</param>
    /// <param name="tagger">The tagger (created with all alternatives, see <see cref="DevEvaluator.CreateTagger"/>).</param>
    /// <param name="label">The label of the run.</param>
    /// <param name="weightsSha256">The SHA-256 of the weights file.</param>
    /// <param name="which">The value of <c>--tranche</c>: <c>standard</c>, <c>hard</c> or <c>both</c> (the original sections), one single-shape tranche, or <c>new</c> (the three single-shape tranches).</param>
    /// <returns>The lines of the report, without line terminators.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="which"/> is not a valid selection, or a link lies on the way to a development file.</exception>
    /// <exception cref="FileNotFoundException">The file of an explicitly requested tranche does not exist (a missing file is only noted for <c>both</c> and <c>new</c>).</exception>
    /// <exception cref="InvalidDataException">A file does not announce its tranche or is malformed.</exception>
    public static List<string> Run(string root, Tagger tagger, string label, string weightsSha256, string which)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(tagger);
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(weightsSha256);
        EnsureSelection(which);
        if (which is "standard" or "hard" or "both")
        {
            return RunOriginal(root, tagger, label, weightsSha256, which);
        }

        // An explicit single-shape tranche needs its file; with "new" a missing file is noted and the others are scored
        var lines = new List<string>();
        foreach (var tranche in NewTranches)
        {
            if (which != "new" && DevSets.ShapesDevNameOf(tranche) != which)
            {
                continue;
            }

            if (lines.Count > 0)
            {
                lines.Add(string.Empty);
            }

            if (which == "new" && !DevSets.ShapesDevExists(root, tranche))
            {
                lines.Add(ShapesDevReport.RenderTrancheMissing(tranche));
                continue;
            }

            var items = DevSets.ReadShapesDev(root, tranche, out var sha256);
            lines.AddRange(ShapesDevReport.RenderTranche(label, tranche, sha256, ShapesDevTrancheEvaluator.Evaluate(tagger, tranche, items), weightsSha256));
        }

        return lines;
    }

    /// <summary>
    ///     Scores the standard and the hard tranche as the command always did (<c>standard</c>, <c>hard</c> or <c>both</c>).
    /// </summary>
    /// <param name="root">The repository root.</param>
    /// <param name="tagger">The tagger.</param>
    /// <param name="label">The label of the run.</param>
    /// <param name="weightsSha256">The SHA-256 of the weights file.</param>
    /// <param name="which">The selection.</param>
    /// <returns>The lines of the report.</returns>
    private static List<string> RunOriginal(string root, Tagger tagger, string label, string weightsSha256, string which)
    {
        var items = new List<ShapesDevItem>();
        string standardSha256 = string.Empty, hardSha256 = string.Empty;
        if (which != "hard")
        {
            items.AddRange(DevSets.ReadShapesDev(root, ShapesDevTranche.Standard, out standardSha256));
        }

        // The hard tranche is read when it is asked for or present; a missing file is an error only for an explicit --tranche hard
        var hardPresent = which == "hard" || (which == "both" && DevSets.ShapesDevExists(root, ShapesDevTranche.Hard));
        if (hardPresent)
        {
            items.AddRange(DevSets.ReadShapesDev(root, ShapesDevTranche.Hard, out hardSha256));
        }

        var results = ShapesDevEvaluator.EvaluateTranches(tagger, items);
        List<string> lines;
        if (which == "hard")
        {
            lines = ShapesDevReport.RenderHard(label, hardSha256, results.Hard, weightsSha256);
        }
        else if (hardPresent)
        {
            lines = ShapesDevReport.RenderTranches(label, standardSha256, hardSha256, results, weightsSha256);
        }
        else
        {
            lines = ShapesDevReport.Render(label, standardSha256, results.Standard, weightsSha256);
            if (which == "both")
            {
                lines.Add(string.Empty);
                lines.Add($"- The hard tranche {DataGuard.ShapesDevelopmentHardFile} is not present; only the standard tranche is scored.");
            }
        }

        return lines;
    }
}
