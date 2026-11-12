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
        int targets = 0, targetCorrect = 0, decisions = 0, harmful = 0, confident = 0, confident90 = 0, controls = 0, controlCorrect = 0, authoredTargets = 0, authoredControls = 0;
        int controlConfident = 0, controlConfident90 = 0;
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
                confident90 += Metrics.IsConfidentlyWrong90(outcomes[i]) ? 1 : 0;
                authoredTargets += authored;
            }
            else
            {
                controls++;
                controlCorrect += outcomes[i].Correct ? 1 : 0;
                controlConfident += Metrics.IsConfidentlyWrong(outcomes[i]) ? 1 : 0;
                controlConfident90 += Metrics.IsConfidentlyWrong90(outcomes[i]) ? 1 : 0;
                authoredControls += authored;
            }
        }

        return new ShapesDevShapeResult(label, targets, targetCorrect, decisions, harmful, controls, controlCorrect, authoredTargets, authoredControls, confident, confident90, controlConfident90, controlConfident);
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
    ///     Renders the counts table of the technical measure: for every scored tranche and every kind, the target and control items, the target accuracy,
    ///     the noun-verb decisions, the harmful errors at the harmful confidence, and the wrong answers at confidence of at least 0.90 and 0.95.
    /// </summary>
    /// <param name="label">The label of the run.</param>
    /// <param name="results">The results of the scored tranches, in report order.</param>
    /// <returns>The lines of the section, without line terminators.</returns>
    /// <remarks>
    ///     The counts are the ones of <see cref="ShapesDevShapeResult"/>: a harmful error is a swapped noun-verb decision at a confidence of at least
    ///     <see cref="Metrics.HarmfulConfidence"/>; a wrong answer at confidence of at least <see cref="Metrics.ConfidentWrong90"/> or
    ///     <see cref="Metrics.ConfidentWrongConfidence"/> is any wrong answer (a target or, in the last two columns, a control) with that confidence.
    ///     No threshold is chosen or changed here.
    /// </remarks>
    public static List<string> RenderCounts(string label, IReadOnlyList<ShapesDevSingleShapeResult> results)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(results);
        var lines = new List<string>
        {
            "## Shapes development set, counts at the confidence limits: " + label,
            string.Empty,
            string.Format(
                CultureInfo.InvariantCulture,
                "- Harmful: a swapped noun-verb decision of a target item with a confidence of at least {0:0.00}. Wrong at confidence >= {1:0.00} and >= {2:0.00}: the items answered wrongly (under the scoring of the tranche) with a confidence of at least that limit; the columns of the targets come first, then those of the controls.",
                Metrics.HarmfulConfidence,
                Metrics.ConfidentWrong90,
                Metrics.ConfidentWrongConfidence),
            "- The counts are measurements at fixed limits; no threshold is chosen or changed here.",
            string.Empty,
            "| tranche | row | target items | target accuracy | noun-verb decisions | harmful at >= 0.45 | wrong at >= 0.90 | wrong at >= 0.95 | control items | control wrong at >= 0.90 | control wrong at >= 0.95 |",
            "| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |",
        };
        foreach (var result in results)
        {
            var name = DevSets.ShapesDevNameOf(result.Spec.Tranche);
            lines.Add(CountsRow(name, "all items", result.Overall));
            for (var i = 0; i < result.Kinds.Count; i++)
            {
                var kind = result.Spec.Kinds[i];
                lines.Add(CountsRow(name, kind.Name + " (" + kind.Role + ")", result.Kinds[i]));
            }
        }

        return lines;
    }

    /// <summary>
    ///     Renders one row of the counts table: a column that does not apply to the row (no target or no control items) is <c>-</c>.
    /// </summary>
    /// <param name="tranche">The name of the tranche.</param>
    /// <param name="rowName">The name of the row.</param>
    /// <param name="row">The counts of the row.</param>
    /// <returns>The row.</returns>
    private static string CountsRow(string tranche, string rowName, ShapesDevShapeResult row)
    {
        var hasTargets = row.TargetItems > 0;
        var hasControls = row.ControlItems > 0;
        return string.Format(
            CultureInfo.InvariantCulture,
            "| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} | {9} | {10} |",
            tranche,
            rowName,
            hasTargets ? row.TargetItems.ToString(CultureInfo.InvariantCulture) : "-",
            hasTargets ? Percent(row.TargetAccuracy) : "-",
            hasTargets ? row.NounVerbDecisions.ToString(CultureInfo.InvariantCulture) : "-",
            hasTargets ? row.HarmfulErrors.ToString(CultureInfo.InvariantCulture) : "-",
            hasTargets ? row.ConfidentWrong90.ToString(CultureInfo.InvariantCulture) : "-",
            hasTargets ? row.ConfidentWrong.ToString(CultureInfo.InvariantCulture) : "-",
            hasControls ? row.ControlItems.ToString(CultureInfo.InvariantCulture) : "-",
            hasControls ? row.ControlConfidentWrong90.ToString(CultureInfo.InvariantCulture) : "-",
            hasControls ? row.ControlConfidentWrong.ToString(CultureInfo.InvariantCulture) : "-");
    }

    /// <summary>
    ///     Renders the scoring note of a shape.
    /// </summary>
    /// <param name="spec">The specification of the shape.</param>
    /// <returns>The line.</returns>
    private static string ScoringLine(ShapesDevShapeSpec spec)
    {
        string how;
        if (spec.Scoring == ShapesDevScoring.Class)
        {
            how = "by class (Noun: NOUN and PROPN; Verb: VERB); a noun-verb decision is a target whose candidates include both classes; a harmful error is a swapped class";
        }
        else if (spec.Shape == "heading-titlecase")
        {
            how = "by exact tag (a NOUN or VERB heading answered PROPN is wrong, but it is not harmful, and a PROPN control is never a noun-verb decision); a noun-verb decision is a target whose candidates include NOUN and VERB; a harmful error is NOUN answered for VERB or VERB for NOUN";
        }
        else
        {
            how = "by exact tag (the answer is right only when it is the gold tag; a wrong tag other than NOUN for VERB or VERB for NOUN is wrong but not harmful); a noun-verb decision is a target whose gold tag is NOUN or VERB and whose candidates include NOUN and VERB; a harmful error is NOUN answered for VERB or VERB for NOUN";
        }

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
    public static readonly IReadOnlyList<string> Selections = new[]
    {
        "standard", "hard", "both", "compound-head", "headings", "named-state", "new", "compound-head-2", "headings-2", "followup",
        "deverbal-heads", "defined-terms", "step-lines", "clause-verbs", "technical", "all-single",
    };

    /// <summary>
    ///     The single-shape tranches of the first generation, in report order; <c>new</c> selects all of them.
    /// </summary>
    public static readonly IReadOnlyList<ShapesDevTranche> NewTranches = new[]
    {
        ShapesDevTranche.CompoundHead,
        ShapesDevTranche.Headings,
        ShapesDevTranche.NamedState,
    };

    /// <summary>
    ///     The single-shape tranches of the follow-up (the second compound-head and headings tranches), in report order; <c>followup</c>
    ///     selects both. <c>new</c> does not select them, so its output does not change.
    /// </summary>
    public static readonly IReadOnlyList<ShapesDevTranche> FollowupTranches = new[]
    {
        ShapesDevTranche.CompoundHead2,
        ShapesDevTranche.Headings2,
    };

    /// <summary>
    ///     The four technical single-shape tranches (deverbal heads, defined terms, step lines, clause verbs), in report order; <c>technical</c>
    ///     selects all of them. <c>new</c> and <c>followup</c> do not select them, so their output does not change.
    /// </summary>
    public static readonly IReadOnlyList<ShapesDevTranche> TechnicalTranches = new[]
    {
        ShapesDevTranche.DeverbalHeads,
        ShapesDevTranche.DefinedTerms,
        ShapesDevTranche.StepLines,
        ShapesDevTranche.ClauseVerbs,
    };

    /// <summary>
    ///     All nine single-shape tranches of the three generations, in report order (first generation, follow-up, technical); <c>all-single</c> selects all of them.
    /// </summary>
    public static readonly IReadOnlyList<ShapesDevTranche> AllSingleTranches = NewTranches.Concat(FollowupTranches).Concat(TechnicalTranches).ToList();

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
            throw new InvalidOperationException("--tranche must be standard, hard, both, compound-head, headings, named-state, new, compound-head-2, headings-2, followup, deverbal-heads, defined-terms, step-lines, clause-verbs, technical or all-single.");
        }
    }

    /// <summary>
    ///     Gets the single-shape tranches that a selection names.
    /// </summary>
    /// <param name="which">The value of <c>--tranche</c>, a valid selection other than <c>standard</c>, <c>hard</c> and <c>both</c>.</param>
    /// <returns>The tranches in report order: the group that <c>new</c>, <c>followup</c>, <c>technical</c> or <c>all-single</c> names, or the one tranche of its own name.</returns>
    public static IReadOnlyList<ShapesDevTranche> TranchesOf(string which)
    {
        ArgumentNullException.ThrowIfNull(which);
        return which switch
        {
            "new" => NewTranches,
            "followup" => FollowupTranches,
            "technical" => TechnicalTranches,
            "all-single" => AllSingleTranches,
            _ => AllSingleTranches.Where(t => DevSets.ShapesDevNameOf(t) == which).ToList(),
        };
    }

    /// <summary>
    ///     Tells whether a selection names a group of tranches (a missing file is noted, not an error) and not one tranche.
    /// </summary>
    /// <param name="which">The value of <c>--tranche</c>.</param>
    /// <returns><c>true</c> for <c>new</c>, <c>followup</c>, <c>technical</c> and <c>all-single</c>.</returns>
    public static bool IsGroup(string which) => which is "new" or "followup" or "technical" or "all-single";

    /// <summary>
    ///     Scores the tranches that the option names and renders the report.
    /// </summary>
    /// <param name="root">The repository root.</param>
    /// <param name="tagger">The tagger (created with all alternatives, see <see cref="DevEvaluator.CreateTagger"/>).</param>
    /// <param name="label">The label of the run.</param>
    /// <param name="weightsSha256">The SHA-256 of the weights file.</param>
    /// <param name="which">The value of <c>--tranche</c>: <c>standard</c>, <c>hard</c> or <c>both</c> (the original sections), one single-shape tranche, or a group: <c>new</c> (the three single-shape tranches of the first generation), <c>followup</c> (the two of the follow-up), <c>technical</c> (the four technical tranches) or <c>all-single</c> (all nine).</param>
    /// <param name="calibration">Whether to append the calibration section (see <see cref="ShapesDevCalibration"/>); it applies to the single-shape tranches only.</param>
    /// <returns>The lines of the report, without line terminators.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="which"/> is not a valid selection, or a link lies on the way to a development file, or <paramref name="calibration"/> is set for <c>standard</c>, <c>hard</c> or <c>both</c>.</exception>
    /// <exception cref="FileNotFoundException">The file of an explicitly requested tranche does not exist (a missing file is only noted for <c>both</c> and the groups).</exception>
    /// <exception cref="InvalidDataException">A file does not announce its tranche or is malformed.</exception>
    /// <remarks>
    ///     A selection that includes a technical tranche adds the counts table of the technical measure (harmful at 0.45, wrong at 0.90 and
    ///     0.95) after the tranche sections; <c>new</c>, <c>followup</c> and the first-generation and follow-up names print what they always
    ///     printed, byte for byte.
    /// </remarks>
    public static List<string> Run(string root, Tagger tagger, string label, string weightsSha256, string which, bool calibration = false)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(tagger);
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(weightsSha256);
        EnsureSelection(which);
        if (which is "standard" or "hard" or "both")
        {
            if (calibration)
            {
                throw new InvalidOperationException("--calibration applies to the single-shape tranches only, not to standard, hard or both.");
            }

            return RunOriginal(root, tagger, label, weightsSha256, which);
        }

        // An explicit single-shape tranche needs its file; with a group a missing file is noted and the others are scored
        var lines = new List<string>();
        var group = IsGroup(which);
        var results = new List<ShapesDevSingleShapeResult>();
        var judged = new List<KeyValuePair<string, IReadOnlyList<Outcome>>>();
        foreach (var tranche in TranchesOf(which))
        {
            if (lines.Count > 0)
            {
                lines.Add(string.Empty);
            }

            if (group && !DevSets.ShapesDevExists(root, tranche))
            {
                lines.Add(ShapesDevReport.RenderTrancheMissing(tranche));
                continue;
            }

            var items = DevSets.ReadShapesDev(root, tranche, out var sha256);
            var outcomes = ShapesDevTrancheEvaluator.Judge(tagger, items);
            var result = ShapesDevTrancheEvaluator.Summarize(tranche, items, outcomes);
            lines.AddRange(ShapesDevReport.RenderTranche(label, tranche, sha256, result, weightsSha256));
            results.Add(result);
            judged.Add(new KeyValuePair<string, IReadOnlyList<Outcome>>(DevSets.ShapesDevNameOf(tranche), outcomes));
        }

        if (results.Any(r => TechnicalTranches.Contains(r.Spec.Tranche)))
        {
            lines.Add(string.Empty);
            lines.AddRange(ShapesDevReport.RenderCounts(label, results));
        }

        if (calibration)
        {
            lines.Add(string.Empty);
            lines.AddRange(ShapesDevCalibration.Render(label, ShapesDevCalibration.Measure(judged)));
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
