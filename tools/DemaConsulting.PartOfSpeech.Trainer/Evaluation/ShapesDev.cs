using System.Globalization;
using System.Text;

namespace DemaConsulting.PartOfSpeech.Trainer;

/// <summary>
///     One judged word of the hard-shape development set: a sentence, the index of the word, its gold class and the shape it was chosen for.
/// </summary>
internal sealed class ShapesDevItem
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ShapesDevItem"/> class.
    /// </summary>
    /// <param name="id">The sentence id of the corpus.</param>
    /// <param name="sourceId">The id of the document of the sentence (the authored pseudo-document for an authored sentence).</param>
    /// <param name="tokens">The tokens as written.</param>
    /// <param name="target">The zero-based index of the judged word.</param>
    /// <param name="goldClass">The class of the gold tag of the judged word (<c>Noun</c> or <c>Verb</c>).</param>
    /// <param name="shape">The shape id.</param>
    /// <param name="role">The role, <c>target</c> or <c>control</c>.</param>
    /// <param name="lemma">The lemma of the judged word.</param>
    /// <param name="origin">The origin, <c>real</c> or <c>authored</c>.</param>
    public ShapesDevItem(string id, string sourceId, string[] tokens, int target, string goldClass, string shape, string role, string lemma, string origin)
    {
        Id = id;
        SourceId = sourceId;
        Tokens = tokens;
        Target = target;
        GoldClass = goldClass;
        Shape = shape;
        Role = role;
        Lemma = lemma;
        Origin = origin;
    }

    /// <summary>
    ///     Gets the sentence id of the corpus.
    /// </summary>
    public string Id { get; }

    /// <summary>
    ///     Gets the id of the document of the sentence.
    /// </summary>
    public string SourceId { get; }

    /// <summary>
    ///     Gets the tokens as written.
    /// </summary>
    public string[] Tokens { get; }

    /// <summary>
    ///     Gets the zero-based index of the judged word.
    /// </summary>
    public int Target { get; }

    /// <summary>
    ///     Gets the class (<c>Noun</c> or <c>Verb</c>) of the gold tag of the judged word.
    /// </summary>
    public string GoldClass { get; }

    /// <summary>
    ///     Gets the shape id the sentence was chosen for.
    /// </summary>
    public string Shape { get; }

    /// <summary>
    ///     Gets the role: <c>target</c> (an instance of the shape) or <c>control</c> (the same spelling in the other class).
    /// </summary>
    public string Role { get; }

    /// <summary>
    ///     Gets the lemma of the judged word.
    /// </summary>
    public string Lemma { get; }

    /// <summary>
    ///     Gets the origin of the sentence: <c>real</c> text or <c>authored</c>.
    /// </summary>
    public string Origin { get; }

    /// <summary>
    ///     Gets a value indicating whether the item is a target (and not a control).
    /// </summary>
    public bool IsTarget => string.Equals(Role, ShapesDevSets.TargetRole, StringComparison.Ordinal);
}

/// <summary>
///     Parses the hard-shape development set: the corpus block format with the comments <c>shape</c>, <c>role</c>, <c>target</c>,
///     <c>lemma</c> and <c>origin</c> after the three standard comments of every sentence.
/// </summary>
/// <remarks>
///     <para>
///         Tokens, tags and status cells are read by <see cref="CorpusReader.Parse"/> (one grammar for every corpus file), and the
///         extra comments are read by a scan of the same lines. The judged word must be tagged NOUN, PROPN or VERB, must be in the
///         class that its role asks for (the class of the shape for a target, the other class for a control) and must not be
///         disputed. An authored sentence belongs to the authored pseudo-document and no other sentence does.
///     </para>
///     <para>
///         The class is stateless and thread-safe.
///     </para>
/// </remarks>
internal static class ShapesDevSets
{
    /// <summary>
    ///     The role of an item that is an instance of its shape.
    /// </summary>
    public const string TargetRole = "target";

    /// <summary>
    ///     The role of an item that is the same spelling in the other class.
    /// </summary>
    public const string ControlRole = "control";

    /// <summary>
    ///     The origin of a sentence from a source document.
    /// </summary>
    public const string RealOrigin = "real";

    /// <summary>
    ///     The origin of a sentence that the project wrote.
    /// </summary>
    public const string AuthoredOrigin = "authored";

    /// <summary>
    ///     The document id that all authored sentences share.
    /// </summary>
    public const string AuthoredSourceId = "shapes-dev-authored";

    /// <summary>
    ///     The shape ids, in report order. The id starts with <c>n-</c> for a noun shape and with <c>v-</c> for a verb shape.
    /// </summary>
    public static readonly IReadOnlyList<string> Shapes = new[]
    {
        "n-compound-head",
        "n-subject-initial",
        "v-imperative-initial",
        "n-measure",
        "v-after-modal",
    };

    /// <summary>
    ///     Gets the class of the judged word of a target of a shape.
    /// </summary>
    /// <param name="shape">The shape id.</param>
    /// <returns><c>Noun</c> for a noun shape, <c>Verb</c> for a verb shape.</returns>
    public static string ClassOfShape(string shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        return shape.StartsWith("n-", StringComparison.Ordinal) ? "Noun" : "Verb";
    }

    /// <summary>
    ///     Parses the lines of a development set.
    /// </summary>
    /// <param name="lines">The lines, without line terminators.</param>
    /// <param name="origin">The origin of the lines for error messages.</param>
    /// <returns>The items in file order.</returns>
    /// <exception cref="InvalidDataException">A row, a comment or a judged word breaks a rule of the format.</exception>
    public static List<ShapesDevItem> Parse(IEnumerable<string> lines, string origin)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(origin);
        var all = lines.ToList();
        var sentences = CorpusReader.Parse(all, new SourceConfig { Name = "shapes-dev", MaskDisputed = false }, origin);
        var blocks = ReadBlocks(all);
        if (blocks.Count != sentences.Count)
        {
            throw new InvalidDataException($"{origin}: {blocks.Count} sentence blocks were found but {sentences.Count} sentences were read.");
        }

        var items = new List<ShapesDevItem>(sentences.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < sentences.Count; i++)
        {
            if (!seen.Add(sentences[i].SentenceId))
            {
                throw new InvalidDataException($"{origin}: sentence id {sentences[i].SentenceId} is used twice.");
            }

            items.Add(CreateItem(sentences[i], blocks[i], origin));
        }

        return items;
    }

    /// <summary>
    ///     Reads the comments and the status cells of every block that has token rows.
    /// </summary>
    /// <param name="lines">The lines of the file.</param>
    /// <returns>One block per sentence, in file order.</returns>
    private static List<Block> ReadBlocks(List<string> lines)
    {
        var blocks = new List<Block>();
        var current = new Block();
        foreach (var line in lines.Concat(new[] { string.Empty }))
        {
            if (line.Length == 0)
            {
                if (current.Statuses.Count > 0)
                {
                    blocks.Add(current);
                }

                current = new Block();
            }
            else if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                var text = line.Substring(2);
                var split = text.IndexOf(" = ", StringComparison.Ordinal);
                if (split >= 0)
                {
                    current.Comments[text.Substring(0, split)] = text.Substring(split + 3);
                }
            }
            else
            {
                var cells = line.Split('\t');
                current.Statuses.Add(cells.Length > 2 ? cells[2] : string.Empty);
            }
        }

        return blocks;
    }

    /// <summary>
    ///     Creates and validates the item of one sentence.
    /// </summary>
    /// <param name="sentence">The sentence as the corpus reader read it.</param>
    /// <param name="block">The comments and status cells of the sentence.</param>
    /// <param name="origin">The origin for error messages.</param>
    /// <returns>The item.</returns>
    private static ShapesDevItem CreateItem(TrainingSentence sentence, Block block, string origin)
    {
        var id = sentence.SentenceId;
        string Comment(string name) =>
            block.Comments.TryGetValue(name, out var value) && value.Length > 0
                ? value
                : throw Fail(origin, id, $"the comment '{name}' is missing.");

        var shape = Comment("shape");
        if (!Shapes.Contains(shape))
        {
            throw Fail(origin, id, $"the shape '{shape}' is not one of {string.Join(", ", Shapes)}.");
        }

        var role = Comment("role");
        if (role != TargetRole && role != ControlRole)
        {
            throw Fail(origin, id, $"the role '{role}' is not {TargetRole} or {ControlRole}.");
        }

        var sourceOrigin = Comment("origin");
        if (sourceOrigin != RealOrigin && sourceOrigin != AuthoredOrigin)
        {
            throw Fail(origin, id, $"the origin '{sourceOrigin}' is not {RealOrigin} or {AuthoredOrigin}.");
        }

        if ((sourceOrigin == AuthoredOrigin) != (sentence.SourceId == AuthoredSourceId))
        {
            throw Fail(origin, id, $"the origin '{sourceOrigin}' does not match the document '{sentence.SourceId}'.");
        }

        if (!int.TryParse(Comment("target"), NumberStyles.None, CultureInfo.InvariantCulture, out var target) || target >= sentence.Tokens.Length)
        {
            throw Fail(origin, id, "the target is not a token index of the sentence.");
        }

        var goldClass = ReadTargetClass(sentence, target, shape, role, block, origin);
        return new ShapesDevItem(id, sentence.SourceId, sentence.Tokens, target, goldClass, shape, role, Comment("lemma"), sourceOrigin);
    }

    /// <summary>
    ///     Checks the tag and the status of the judged word and returns its class.
    /// </summary>
    /// <param name="sentence">The sentence.</param>
    /// <param name="target">The index of the judged word.</param>
    /// <param name="shape">The shape id.</param>
    /// <param name="role">The role.</param>
    /// <param name="block">The comments and status cells of the sentence.</param>
    /// <param name="origin">The origin for error messages.</param>
    /// <returns>The class of the gold tag, <c>Noun</c> or <c>Verb</c>.</returns>
    private static string ReadTargetClass(TrainingSentence sentence, int target, string shape, string role, Block block, string origin)
    {
        var id = sentence.SentenceId;
        var gold = sentence.Gold[target];
        var tag = gold >= 0 ? (Upos)gold : Upos.X;
        if (gold < 0 || (tag != Upos.Noun && tag != Upos.Propn && tag != Upos.Verb))
        {
            throw Fail(origin, id, "the judged word must be tagged NOUN, PROPN or VERB.");
        }

        var goldClass = DevSets.GoldClassOf(tag);
        var shapeClass = ClassOfShape(shape);
        var wanted = role == TargetRole ? shapeClass : OtherClass(shapeClass);
        if (goldClass != wanted)
        {
            throw Fail(origin, id, $"the judged word of a {role} of shape {shape} must be a {wanted} but is tagged {tag.ToTagString()}.");
        }

        if (CorpusStatus.BaseStatus(block.Statuses[target]) == "disputed")
        {
            throw Fail(origin, id, "the judged word must not be disputed.");
        }

        return goldClass;
    }

    /// <summary>
    ///     Gets the other class of the noun-verb pair.
    /// </summary>
    /// <param name="goldClass">The class, <c>Noun</c> or <c>Verb</c>.</param>
    /// <returns>The other class.</returns>
    private static string OtherClass(string goldClass)
    {
        if (goldClass == "Noun")
        {
            return "Verb";
        }

        return "Noun";
    }

    /// <summary>
    ///     Creates the error of a sentence.
    /// </summary>
    /// <param name="origin">The origin of the lines.</param>
    /// <param name="id">The sentence id.</param>
    /// <param name="message">The message.</param>
    /// <returns>The exception to throw.</returns>
    private static InvalidDataException Fail(string origin, string id, string message) =>
        new($"{origin}: sentence {id}: {message}");

    /// <summary>
    ///     The comments and the status cells of one sentence block.
    /// </summary>
    private sealed class Block
    {
        /// <summary>
        ///     Gets the <c># key = value</c> comments.
        /// </summary>
        public Dictionary<string, string> Comments { get; } = new(StringComparer.Ordinal);

        /// <summary>
        ///     Gets the status cell of every token row.
        /// </summary>
        public List<string> Statuses { get; } = new();
    }
}

/// <summary>
///     The results of one shape (or of all shapes pooled) on the hard-shape development set.
/// </summary>
internal sealed class ShapesDevShapeResult
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ShapesDevShapeResult"/> class.
    /// </summary>
    /// <param name="shape">The shape id, or a label for the pooled row.</param>
    /// <param name="targetItems">The number of target items.</param>
    /// <param name="targetCorrect">The number of target items whose judged word was tagged with the gold class.</param>
    /// <param name="nounVerbDecisions">The target items that were noun-verb decisions (both classes possible).</param>
    /// <param name="harmfulErrors">The target items with a harmful error (swapped class, confidence at least the harmful limit).</param>
    /// <param name="controlItems">The number of control items of the shape.</param>
    /// <param name="controlCorrect">The number of control items whose judged word was tagged with the gold class.</param>
    /// <param name="authoredTargets">The number of authored target items.</param>
    /// <param name="authoredControls">The number of authored control items.</param>
    public ShapesDevShapeResult(
        string shape,
        int targetItems,
        int targetCorrect,
        int nounVerbDecisions,
        int harmfulErrors,
        int controlItems,
        int controlCorrect,
        int authoredTargets,
        int authoredControls)
    {
        Shape = shape;
        TargetItems = targetItems;
        TargetCorrect = targetCorrect;
        NounVerbDecisions = nounVerbDecisions;
        HarmfulErrors = harmfulErrors;
        ControlItems = controlItems;
        ControlCorrect = controlCorrect;
        AuthoredTargets = authoredTargets;
        AuthoredControls = authoredControls;
    }

    /// <summary>
    ///     Gets the shape id, or the label of the pooled row.
    /// </summary>
    public string Shape { get; }

    /// <summary>
    ///     Gets the number of target items.
    /// </summary>
    public int TargetItems { get; }

    /// <summary>
    ///     Gets the number of target items tagged with the gold class.
    /// </summary>
    public int TargetCorrect { get; }

    /// <summary>
    ///     Gets the number of target items that were noun-verb decisions.
    /// </summary>
    public int NounVerbDecisions { get; }

    /// <summary>
    ///     Gets the number of harmful errors among the target items.
    /// </summary>
    public int HarmfulErrors { get; }

    /// <summary>
    ///     Gets the number of control items.
    /// </summary>
    public int ControlItems { get; }

    /// <summary>
    ///     Gets the number of control items tagged with the gold class.
    /// </summary>
    public int ControlCorrect { get; }

    /// <summary>
    ///     Gets the number of authored target items.
    /// </summary>
    public int AuthoredTargets { get; }

    /// <summary>
    ///     Gets the number of authored control items.
    /// </summary>
    public int AuthoredControls { get; }

    /// <summary>
    ///     Gets the target-token accuracy in percent (0 for a shape without items).
    /// </summary>
    public double TargetAccuracy => TargetItems == 0 ? 0 : 100.0 * TargetCorrect / TargetItems;

    /// <summary>
    ///     Gets the harmful errors per 1,000 noun-verb decisions of the targets (0 without decisions).
    /// </summary>
    public double HarmfulPer1000 => NounVerbDecisions == 0 ? 0 : 1000.0 * HarmfulErrors / NounVerbDecisions;

    /// <summary>
    ///     Gets the accuracy of the control items in percent (0 without controls).
    /// </summary>
    public double ControlAccuracy => ControlItems == 0 ? 0 : 100.0 * ControlCorrect / ControlItems;
}

/// <summary>
///     The results of a tagger on the hard-shape development set: one row per shape in report order and the pooled row.
/// </summary>
internal sealed class ShapesDevResult
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ShapesDevResult"/> class.
    /// </summary>
    /// <param name="shapes">The per-shape results, in the order of <see cref="ShapesDevSets.Shapes"/>.</param>
    /// <param name="pooled">The pooled result.</param>
    public ShapesDevResult(IReadOnlyList<ShapesDevShapeResult> shapes, ShapesDevShapeResult pooled)
    {
        Shapes = shapes;
        Pooled = pooled;
    }

    /// <summary>
    ///     Gets the per-shape results.
    /// </summary>
    public IReadOnlyList<ShapesDevShapeResult> Shapes { get; }

    /// <summary>
    ///     Gets the pooled result over all shapes.
    /// </summary>
    public ShapesDevShapeResult Pooled { get; }
}

/// <summary>
///     Runs a weights file through the real decoder on the hard-shape development set and summarizes the outcomes per shape.
/// </summary>
/// <remarks>
///     The judged word of every item is scored like a gold v2 target: the classes are Noun (NOUN, PROPN) and Verb (VERB), a
///     noun-verb decision is a judged word whose candidates include both classes, and an error is harmful when the class is swapped with
///     a confidence of at least <see cref="Metrics.HarmfulConfidence"/>. Stateless and thread-safe.
/// </remarks>
internal static class ShapesDevEvaluator
{
    /// <summary>
    ///     Tags every item and judges its judged word.
    /// </summary>
    /// <param name="tagger">The tagger (created with all alternatives, see <see cref="DevEvaluator.CreateTagger"/>).</param>
    /// <param name="items">The items.</param>
    /// <returns>The outcomes, one per item in the order of <paramref name="items"/>.</returns>
    public static List<Outcome> Judge(Tagger tagger, IReadOnlyList<ShapesDevItem> items)
    {
        ArgumentNullException.ThrowIfNull(tagger);
        ArgumentNullException.ThrowIfNull(items);
        var lexicon = EmbeddedModel.Lexicon;
        var scratch = new byte[lexicon.ScratchSize];
        var outcomes = new List<Outcome>(items.Count);
        foreach (var item in items)
        {
            var tagged = tagger.Tag(item.Tokens);
            var ambiguous = DevEvaluator.IsLexiconAmbiguous(lexicon, scratch, item.Tokens[item.Target]);
            outcomes.Add(Metrics.Judge("shapes-dev:" + item.SourceId, item.GoldClass, tagged.Tokens[item.Target], DevSets.GoldClassOf, "Noun", "Verb", ambiguous));
        }

        return outcomes;
    }

    /// <summary>
    ///     Tags every item and summarizes the outcomes per shape.
    /// </summary>
    /// <param name="tagger">The tagger.</param>
    /// <param name="items">The items.</param>
    /// <returns>The results.</returns>
    public static ShapesDevResult Evaluate(Tagger tagger, IReadOnlyList<ShapesDevItem> items) =>
        Summarize(items, Judge(tagger, items));

    /// <summary>
    ///     Summarizes the outcomes of the items per shape (target accuracy, harmful errors and control accuracy).
    /// </summary>
    /// <param name="items">The items.</param>
    /// <param name="outcomes">The outcome of every item, in the same order.</param>
    /// <returns>The results: one row for each shape of <see cref="ShapesDevSets.Shapes"/> (a shape without items has zero counts) and the pooled row.</returns>
    /// <exception cref="ArgumentException">The lists differ in length or an item has an unknown shape.</exception>
    public static ShapesDevResult Summarize(IReadOnlyList<ShapesDevItem> items, IReadOnlyList<Outcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(outcomes);
        if (items.Count != outcomes.Count)
        {
            throw new ArgumentException("There must be one outcome per item.", nameof(outcomes));
        }

        var shapes = new List<ShapesDevShapeResult>();
        foreach (var shape in ShapesDevSets.Shapes)
        {
            shapes.Add(Summarize(shape, items, outcomes));
        }

        if (items.Any(i => !ShapesDevSets.Shapes.Contains(i.Shape)))
        {
            throw new ArgumentException("An item has an unknown shape.", nameof(items));
        }

        var pooled = new ShapesDevShapeResult(
            "all shapes",
            shapes.Sum(s => s.TargetItems),
            shapes.Sum(s => s.TargetCorrect),
            shapes.Sum(s => s.NounVerbDecisions),
            shapes.Sum(s => s.HarmfulErrors),
            shapes.Sum(s => s.ControlItems),
            shapes.Sum(s => s.ControlCorrect),
            shapes.Sum(s => s.AuthoredTargets),
            shapes.Sum(s => s.AuthoredControls));
        return new ShapesDevResult(shapes, pooled);
    }

    /// <summary>
    ///     Summarizes the items of one shape.
    /// </summary>
    /// <param name="shape">The shape id.</param>
    /// <param name="items">All items.</param>
    /// <param name="outcomes">All outcomes.</param>
    /// <returns>The result of the shape.</returns>
    private static ShapesDevShapeResult Summarize(string shape, IReadOnlyList<ShapesDevItem> items, IReadOnlyList<Outcome> outcomes)
    {
        int targets = 0, targetCorrect = 0, decisions = 0, harmful = 0, controls = 0, controlCorrect = 0, authoredTargets = 0, authoredControls = 0;
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Shape != shape)
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
                authoredTargets += authored;
            }
            else
            {
                controls++;
                controlCorrect += outcomes[i].Correct ? 1 : 0;
                authoredControls += authored;
            }
        }

        return new ShapesDevShapeResult(shape, targets, targetCorrect, decisions, harmful, controls, controlCorrect, authoredTargets, authoredControls);
    }
}

/// <summary>
///     Renders the results on the hard-shape development set as Markdown.
/// </summary>
/// <remarks>
///     The text depends on the results only: invariant culture, fixed column order, no time stamp or machine name, so two runs on
///     the same model and file give the same bytes. Stateless and thread-safe.
/// </remarks>
internal static class ShapesDevReport
{
    /// <summary>
    ///     The z value of a two-sided 95% interval.
    /// </summary>
    private const double Z95 = 1.959964;

    /// <summary>
    ///     Renders the report.
    /// </summary>
    /// <param name="label">The label of the run (for example the weights file name).</param>
    /// <param name="datasetSha256">The SHA-256 of the development file, or an empty text for none.</param>
    /// <param name="result">The results.</param>
    /// <param name="weightsSha256">The SHA-256 of the weights file, or an empty text for none.</param>
    /// <returns>The lines of the Markdown report, without line terminators.</returns>
    public static List<string> Render(string label, string datasetSha256, ShapesDevResult result, string weightsSha256 = "")
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(datasetSha256);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(weightsSha256);
        var pooled = result.Pooled;
        var lines = new List<string>
        {
            "## Shapes development set: " + label,
            string.Empty,
            string.Format(
                CultureInfo.InvariantCulture,
                "- Items: {0} target items and {1} control items; authored: {2} target items ({3}) and {4} control items ({5}).",
                pooled.TargetItems,
                pooled.ControlItems,
                pooled.AuthoredTargets,
                Share(pooled.AuthoredTargets, pooled.TargetItems),
                pooled.AuthoredControls,
                Share(pooled.AuthoredControls, pooled.ControlItems)),
        };
        if (datasetSha256.Length > 0)
        {
            lines.Add($"- Development file SHA-256: `{datasetSha256}`.");
        }

        if (weightsSha256.Length > 0)
        {
            lines.Add($"- Weights file SHA-256: `{weightsSha256}`.");
        }

        lines.Add(string.Format(
            CultureInfo.InvariantCulture,
            "- Scoring: the judged word of every item through the real decoder with all alternatives; a noun-verb decision is a target whose candidates include both classes; a harmful error is a swapped class with a confidence of at least {0:0.00}.",
            Metrics.HarmfulConfidence));
        lines.Add("- The set is for development: it selects settings and is never a held-out figure.");
        lines.Add(string.Empty);
        lines.Add("| shape | target items | target accuracy | 95% interval | noun-verb decisions | harmful errors | harmful per 1,000 decisions | control items | control accuracy |");
        lines.Add("| --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var row in result.Shapes.Concat(new[] { pooled }))
        {
            lines.Add(Row(row));
        }

        return lines;
    }

    /// <summary>
    ///     Renders one table row.
    /// </summary>
    /// <param name="row">The result of a shape.</param>
    /// <returns>The row.</returns>
    private static string Row(ShapesDevShapeResult row)
    {
        var interval = row.TargetItems == 0 ? "-" : Interval(row.TargetCorrect, row.TargetItems);
        var accuracy = row.TargetItems == 0 ? "-" : Percent(row.TargetAccuracy);
        var harmful = row.NounVerbDecisions == 0 ? "-" : row.HarmfulPer1000.ToString("0.0", CultureInfo.InvariantCulture);
        var control = row.ControlItems == 0 ? "-" : Percent(row.ControlAccuracy);
        return string.Format(
            CultureInfo.InvariantCulture,
            "| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} |",
            row.Shape,
            row.TargetItems,
            accuracy,
            interval,
            row.NounVerbDecisions,
            row.HarmfulErrors,
            harmful,
            row.ControlItems,
            control);
    }

    /// <summary>
    ///     Formats a percentage with one decimal.
    /// </summary>
    /// <param name="value">The percentage.</param>
    /// <returns>The text.</returns>
    private static string Percent(double value) => value.ToString("0.0", CultureInfo.InvariantCulture) + "%";

    /// <summary>
    ///     Formats the share of a part in a whole.
    /// </summary>
    /// <param name="part">The part.</param>
    /// <param name="whole">The whole.</param>
    /// <returns>The percentage, or <c>-</c> for an empty whole.</returns>
    private static string Share(int part, int whole) => whole == 0 ? "-" : Percent(100.0 * part / whole);

    /// <summary>
    ///     Formats the Wilson score interval (95%) of a share.
    /// </summary>
    /// <param name="successes">The number of successes.</param>
    /// <param name="total">The number of trials (positive).</param>
    /// <returns>The interval in percent, such as <c>[71.2%, 88.4%]</c>.</returns>
    public static string Interval(int successes, int total)
    {
        var n = (double)total;
        var p = successes / n;
        var z2 = Z95 * Z95;
        var center = (p + (z2 / (2 * n))) / (1 + (z2 / n));
        var half = Z95 * Math.Sqrt((p * (1 - p) / n) + (z2 / (4 * n * n))) / (1 + (z2 / n));
        return "[" + Percent(100.0 * Math.Max(0, center - half)) + ", " + Percent(100.0 * Math.Min(1, center + half)) + "]";
    }
}
