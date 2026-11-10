using System.Globalization;
using System.Text;

namespace DemaConsulting.PartOfSpeech.Trainer;

/// <summary>
///     The settings of the training run that made an arm, as the result file echoes them (the groups, the bucket space and the
///     scale are read from the weights header, not from here).
/// </summary>
internal sealed class ArmConfigEcho
{
    /// <summary>
    ///     Gets or sets the number of jackknife folds of the <c>rightPreview</c> group, written only for an arm with that group (0 otherwise).
    /// </summary>
    public int PreviewFolds { get; set; }

    /// <summary>
    ///     Gets or sets the number of training epochs of the committed settings.
    /// </summary>
    public int Epochs { get; set; }

    /// <summary>
    ///     Gets or sets the pruning threshold of the committed settings.
    /// </summary>
    public int Prune { get; set; }
}

/// <summary>
///     The identity of the inputs and of the code that produced a result.
/// </summary>
internal sealed class ArmInputs
{
    /// <summary>
    ///     Gets or sets the SHA-256 of the standard tranche of the shapes development set.
    /// </summary>
    public string DevSha256 { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the SHA-256 of the hard tranche of the shapes development set.
    /// </summary>
    public string HardSha256 { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the SHA-256 of the mapped table of the development part of the external treebank.
    /// </summary>
    public string UdDevSha256 { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the SHA-256 of the open development split.
    /// </summary>
    public string OpenDevSha256 { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the SHA-256 of the gold v2 development split.
    /// </summary>
    public string GoldDevSha256 { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the SHA-256 of the compound-head tranche of the shapes development set.
    /// </summary>
    public string CompoundHeadSha256 { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the SHA-256 of the headings tranche of the shapes development set.
    /// </summary>
    public string HeadingsSha256 { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the SHA-256 of the named-state tranche of the shapes development set.
    /// </summary>
    public string NamedStateSha256 { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the commit of the repository (<c>unknown</c> when git does not answer).
    /// </summary>
    public string Commit { get; set; } = "unknown";

    /// <summary>
    ///     Gets or sets a value indicating whether the working tree has uncommitted changes to tracked files.
    /// </summary>
    public bool Dirty { get; set; }
}

/// <summary>
///     Every development set that <c>eval-arm</c> scores, parsed, with the hashes that identify the files.
/// </summary>
/// <remarks>
///     There is no member that takes a path: <see cref="Load"/> reads the fixed development files through the guarded readers
///     and nothing else, so no held-out set (the open and gold v2 test splits, the shape probe, the challenge set, the test part of
///     the treebank) can reach the evaluation.
/// </remarks>
internal sealed class ArmEvaluationData
{
    /// <summary>
    ///     Gets the open development sentences.
    /// </summary>
    public IReadOnlyList<TrainingSentence> Open { get; set; } = Array.Empty<TrainingSentence>();

    /// <summary>
    ///     Gets the gold v2 development targets.
    /// </summary>
    public IReadOnlyList<GoldTarget> Gold { get; set; } = Array.Empty<GoldTarget>();

    /// <summary>
    ///     Gets the sentences of the development part of the treebank.
    /// </summary>
    public IReadOnlyList<UdSentence> UdDev { get; set; } = Array.Empty<UdSentence>();

    /// <summary>
    ///     Gets the items of the standard and the hard tranche of the shapes development set (standard items first).
    /// </summary>
    public IReadOnlyList<ShapesDevItem> Shapes { get; set; } = Array.Empty<ShapesDevItem>();

    /// <summary>
    ///     Gets the items of the compound-head tranche of the shapes development set.
    /// </summary>
    public IReadOnlyList<ShapesDevItem> CompoundHead { get; set; } = Array.Empty<ShapesDevItem>();

    /// <summary>
    ///     Gets the items of the headings tranche of the shapes development set.
    /// </summary>
    public IReadOnlyList<ShapesDevItem> Headings { get; set; } = Array.Empty<ShapesDevItem>();

    /// <summary>
    ///     Gets the items of the named-state tranche of the shapes development set.
    /// </summary>
    public IReadOnlyList<ShapesDevItem> NamedState { get; set; } = Array.Empty<ShapesDevItem>();

    /// <summary>
    ///     Gets the hashes of the inputs (the commit and the dirty flag are set by the caller).
    /// </summary>
    public ArmInputs Inputs { get; set; } = new();

    /// <summary>
    ///     Reads the development sets of the arm evaluation: open dev, gold v2 dev, the standard and the hard tranche of the shapes
    ///     development set, its compound-head, headings and named-state tranches, and the mapped development table of the treebank.
    /// </summary>
    /// <param name="repositoryRoot">The full path of the repository root.</param>
    /// <returns>The data.</returns>
    /// <exception cref="InvalidDataException">A file is missing, does not announce a development split or has an unexpected hash.</exception>
    public static ArmEvaluationData Load(string repositoryRoot)
    {
        ArgumentNullException.ThrowIfNull(repositoryRoot);
        var items = DevSets.ReadShapesDev(repositoryRoot, ShapesDevTranche.Standard, out var standardSha);
        items.AddRange(DevSets.ReadShapesDev(repositoryRoot, ShapesDevTranche.Hard, out var hardSha));
        var compound = DevSets.ReadShapesDev(repositoryRoot, ShapesDevTranche.CompoundHead, out var compoundSha);
        var headings = DevSets.ReadShapesDev(repositoryRoot, ShapesDevTranche.Headings, out var headingsSha);
        var namedState = DevSets.ReadShapesDev(repositoryRoot, ShapesDevTranche.NamedState, out var namedStateSha);
        var ud = DevSets.ReadUdDev(repositoryRoot, out var udSha);
        return new ArmEvaluationData
        {
            Open = DevSets.ReadOpenDev(repositoryRoot),
            Gold = DevSets.ReadGoldDev(repositoryRoot),
            UdDev = ud,
            Shapes = items,
            CompoundHead = compound,
            Headings = headings,
            NamedState = namedState,
            Inputs = new ArmInputs
            {
                DevSha256 = standardSha,
                HardSha256 = hardSha,
                UdDevSha256 = udSha,
                OpenDevSha256 = HashOf(repositoryRoot, DataGuard.DevelopmentFiles[0]),
                GoldDevSha256 = HashOf(repositoryRoot, DataGuard.DevelopmentFiles[1]),
                CompoundHeadSha256 = compoundSha,
                HeadingsSha256 = headingsSha,
                NamedStateSha256 = namedStateSha,
            },
        };
    }

    /// <summary>
    ///     Computes the SHA-256 of one of the development files.
    /// </summary>
    /// <param name="repositoryRoot">The full path of the repository root.</param>
    /// <param name="relative">The file, one of <see cref="DataGuard.DevelopmentFiles"/>.</param>
    /// <returns>The hash as 64 lower-case hexadecimal digits.</returns>
    private static string HashOf(string repositoryRoot, string relative) =>
        ModelBuilder.Sha256Hex(File.ReadAllBytes(DataGuard.EnsureDevelopmentFile(repositoryRoot, Path.Combine(repositoryRoot, relative.Replace('/', Path.DirectorySeparatorChar)))));
}

/// <summary>
///     Scores a trained weights file on the development sets of the context-feature experiment and renders one result as JSON.
/// </summary>
/// <remarks>
///     <para>
///         <b>Scope.</b> Only <see cref="ArmEvaluationData"/> (development sets) is read. Everything is judged through the real
///         runtime decoder (<c>Tagger.Load</c> of the in-memory file, all 16 alternatives) with the definitions of the development
///         evaluation: a noun-verb decision is a judged word whose gold class is noun or verb and whose candidates include both, and an
///         error is harmful when the class is swapped at a confidence of at least 0.45. The UD-EWT development table is judged
///         against the convention-mapped tags, like every token of the held-out view of the treebank in <c>evaluate</c>.
///     </para>
///     <para>
///         <b>Throughput.</b> Tokens per second of the default tagger (a tagger loaded from the same file with default options), on
///         one thread (the calling thread; nothing runs in parallel), over the tokens of the open development split followed by those of
///         the development part of the treebank, one <c>Tag(tokens)</c> call per sentence: one warm-up pass over all sentences that is not counted,
///         then five timed passes, each over all sentences; the result is the median of the five rates. The score does not include
///         tokenization, loading or the judged pass. It depends on the machine and its load; the experiment compares arms measured under
///         the same conditions.
///     </para>
///     <para>
///         <b>Items.</b> <c>items.standard</c> and <c>items.hard</c> hold, for every target item of the tranche, <c>d</c> (1 when it is a
///         noun-verb decision), <c>c</c> (1 when it is correct) and <c>h</c> (1 when it is harmful); the sums of <c>d</c> and <c>h</c>
///         over a tranche are the decisions and harmful errors of its pooled row. Control items are not listed.
///     </para>
///     <para>
///         <b>New tranches.</b> <c>shapesNew</c> holds, for each of the compound-head, headings and named-state tranches of the shapes
///         development set, the row of its shape (<c>shape</c>, with the target items that are wrong at a confidence of at least
///         <see cref="Metrics.ConfidentWrongConfidence"/> as <c>confidentWrong</c>) and the rows of its kinds (<c>kinds</c>, the headings
///         only); <c>pooledPrimary</c> pools the compound-head and the headings rows: the harmful errors per 1,000 noun-verb decisions
///         over both and the total of their confident wrong answers.
///     </para>
///     <para>
///         The JSON has no time stamp and no machine name, only invariant-culture numbers, and is written by hand because the engine
///         is compiled for .NET Framework by the tests. Stateless and thread-safe apart from the timed measurement.
///     </para>
/// </remarks>
internal static class ArmEvaluation
{
    /// <summary>
    ///     The version of the result schema.
    /// </summary>
    public const int Schema = 1;

    /// <summary>
    ///     The number of timed throughput passes.
    /// </summary>
    public const int ThroughputPasses = 5;

    /// <summary>
    ///     Scores a weights file and renders the result.
    /// </summary>
    /// <param name="weights">The weights file (compressed or not).</param>
    /// <param name="label">The label of the run.</param>
    /// <param name="config">The settings that the result echoes (the groups, the bucket space and the scale come from the weights header).</param>
    /// <param name="data">The development sets.</param>
    /// <param name="timedPasses">The number of timed throughput passes (<see cref="ThroughputPasses"/> for a result; tests use fewer).</param>
    /// <returns>The JSON text of the result (one object, LF at the end).</returns>
    /// <exception cref="InvalidDataException"><paramref name="weights"/> is not a valid weights file of this build.</exception>
    public static string Evaluate(byte[] weights, string label, ArmConfigEcho config, ArmEvaluationData data, int timedPasses = ThroughputPasses)
    {
        ArgumentNullException.ThrowIfNull(weights);
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(data);
        var model = PerceptronModel.Load(weights);
        var tagger = DevEvaluator.CreateTagger(weights);
        var open = Metrics.Compute(DevEvaluator.EvaluateOpen(tagger, data.Open));
        var gold = Metrics.Compute(DevEvaluator.EvaluateGold(tagger, data.Gold, false));
        var ud = Metrics.Compute(JudgeUd(tagger, data.UdDev));
        var items = data.Shapes;
        var outcomes = ShapesDevEvaluator.Judge(tagger, items);
        var shapes = ShapesDevEvaluator.SummarizeTranches(items, outcomes);
        var compoundHead = ShapesDevTrancheEvaluator.Evaluate(tagger, ShapesDevTranche.CompoundHead, data.CompoundHead);
        var headings = ShapesDevTrancheEvaluator.Evaluate(tagger, ShapesDevTranche.Headings, data.Headings);
        var namedState = ShapesDevTrancheEvaluator.Evaluate(tagger, ShapesDevTranche.NamedState, data.NamedState);

        var sentences = data.Open.Select(s => s.Tokens).Concat(data.UdDev.Select(s => s.Tokens)).ToList();
        var tokens = sentences.Sum(s => (long)s.Length);
        double rate;
        using (var stream = new MemoryStream(weights))
        {
            rate = HeldOutEvaluator.Throughput(Tagger.Load(stream), sentences, timedPasses);
        }

        var json = new JsonText();
        json.Open(null, '{');
        json.Number("schema", Schema);
        json.Text("label", label);
        json.Strings("groups", TrainingConfig.NamesOf(model.FeatureGroups));
        json.Number("temperature", double.Parse(model.Temperature.ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture));
        json.Open("config", '{');
        json.Strings("features", TrainingConfig.NamesOf(model.FeatureGroups));
        json.Number("previewFolds", (model.FeatureGroups & FeatureGroups.RightPreview) != 0 ? config.PreviewFolds : 0);
        json.Number("epochs", config.Epochs);
        json.Number("hashBits", model.HashBits);
        json.Number("quantScale", (int)Math.Round(1.0 / model.Scale));
        json.Number("prune", config.Prune);
        json.Close('}');
        json.Open("weights", '{');
        json.Text("sha256", ModelBuilder.Sha256Hex(weights));
        json.Number("bytesDeflated", weights.Length);
        json.Number("bytesRaw", PerceptronModel.ToUncompressedImage(weights).Length);
        json.Number("rows", model.RowCount);
        json.Number("weights", model.WeightCount);
        json.Close('}');
        json.Open("throughput", '{');
        json.Number("tokensPerSecond", rate);
        json.Number("tokens", tokens);
        json.Number("passes", timedPasses);
        json.Close('}');
        WriteOpen(json, "openDev", open, true);
        WriteOpen(json, "goldDev", gold, false);
        json.Open("udDev", '{');
        json.Number("tokens", ud.Tokens);
        json.Number("accuracy", ud.Accuracy);
        json.Number("decisions", ud.NounVerbDecisions);
        json.Number("harmful", ud.HarmfulErrors);
        json.Number("harmfulPer1k", ud.HarmfulPer1000);
        json.Close('}');
        json.Open("shapes", '{');
        WriteTranche(json, "standard", shapes.Standard);
        WriteTranche(json, "hard", shapes.Hard);
        json.Close('}');
        json.Open("items", '{');
        WriteItems(json, "hard", items, outcomes, ShapesDevTranche.Hard);
        WriteItems(json, "standard", items, outcomes, ShapesDevTranche.Standard);
        json.Close('}');
        json.Open("shapesNew", '{');
        WriteSingleShape(json, "compoundHead", compoundHead);
        WriteSingleShape(json, "headings", headings);
        WriteSingleShape(json, "namedState", namedState);
        WritePooledPrimary(json, compoundHead.Overall, headings.Overall);
        json.Close('}');
        json.Open("inputs", '{');
        json.Text("devSha256", data.Inputs.DevSha256);
        json.Text("hardSha256", data.Inputs.HardSha256);
        json.Text("udDevSha256", data.Inputs.UdDevSha256);
        json.Text("openDevSha256", data.Inputs.OpenDevSha256);
        json.Text("goldDevSha256", data.Inputs.GoldDevSha256);
        json.Text("commit", data.Inputs.Commit);
        json.Bool("dirty", data.Inputs.Dirty);
        json.Text("compoundHeadSha256", data.Inputs.CompoundHeadSha256);
        json.Text("headingsSha256", data.Inputs.HeadingsSha256);
        json.Text("namedStateSha256", data.Inputs.NamedStateSha256);
        json.Close('}');
        json.Close('}');
        return json.ToString() + "\n";
    }

    /// <summary>
    ///     Judges every word of the development part of the treebank against the convention-mapped tags.
    /// </summary>
    /// <param name="tagger">The tagger created with all alternatives.</param>
    /// <param name="sentences">The sentences.</param>
    /// <returns>One outcome per word.</returns>
    public static List<Outcome> JudgeUd(Tagger tagger, IReadOnlyList<UdSentence> sentences)
    {
        ArgumentNullException.ThrowIfNull(tagger);
        ArgumentNullException.ThrowIfNull(sentences);
        var lexicon = EmbeddedModel.Lexicon;
        var scratch = new byte[lexicon.ScratchSize];
        var outcomes = new List<Outcome>();
        foreach (var sentence in sentences)
        {
            var tagged = tagger.Tag(sentence.Tokens);
            for (var i = 0; i < sentence.Tokens.Length; i++)
            {
                var ambiguous = DevEvaluator.IsLexiconAmbiguous(lexicon, scratch, sentence.Tokens[i]);
                outcomes.Add(Metrics.Judge("ud-dev:" + sentence.DocumentId, sentence.Mapped[i].ToTagString(), tagged.Tokens[i], t => t.ToTagString(), "NOUN", "VERB", ambiguous));
            }
        }

        return outcomes;
    }

    /// <summary>
    ///     Writes the metrics of a split of the open or the gold v2 development set.
    /// </summary>
    /// <param name="json">The writer.</param>
    /// <param name="name">The name of the member.</param>
    /// <param name="metrics">The metrics.</param>
    /// <param name="ambiguous">Whether the member carries the accuracy on ambiguous words (the open split).</param>
    private static void WriteOpen(JsonText json, string name, DevMetrics metrics, bool ambiguous)
    {
        json.Open(name, '{');
        json.Number("tokens", metrics.Tokens);
        json.Number("accuracy", metrics.Accuracy);
        if (ambiguous)
        {
            json.Number("ambiguousAccuracy", metrics.AmbiguousAccuracy);
        }

        json.Number("nounVerbDecisions", metrics.NounVerbDecisions);
        json.Number("harmful", metrics.HarmfulErrors);
        json.Number("harmfulPer1k", metrics.HarmfulPer1000);
        json.Number("nll", metrics.Nll);
        json.Number("ece", metrics.Ece);
        json.Close('}');
    }

    /// <summary>
    ///     Writes the per-shape and the pooled results of a tranche (shapes without an item are left out).
    /// </summary>
    /// <param name="json">The writer.</param>
    /// <param name="name">The name of the member.</param>
    /// <param name="result">The result of the tranche.</param>
    private static void WriteTranche(JsonText json, string name, ShapesDevResult result)
    {
        json.Open(name, '{');
        json.Open("shapes", '{');
        foreach (var shape in result.Shapes.Where(s => s.TargetItems + s.ControlItems > 0))
        {
            WriteShape(json, shape.Shape, shape);
        }

        json.Close('}');
        WriteShape(json, "pooled", result.Pooled);
        json.Close('}');
    }

    /// <summary>
    ///     Writes the counts and rates of one shape.
    /// </summary>
    /// <param name="json">The writer.</param>
    /// <param name="name">The name of the member.</param>
    /// <param name="shape">The result of the shape.</param>
    /// <param name="confidentWrong">Whether to write the count of the confident wrong answers (only the new tranches count them).</param>
    private static void WriteShape(JsonText json, string name, ShapesDevShapeResult shape, bool confidentWrong = false)
    {
        json.Open(name, '{');
        json.Number("targetItems", shape.TargetItems);
        json.Number("targetCorrect", shape.TargetCorrect);
        json.Number("targetAccuracy", shape.TargetAccuracy);
        json.Number("decisions", shape.NounVerbDecisions);
        json.Number("harmful", shape.HarmfulErrors);
        json.Number("harmfulPer1k", shape.HarmfulPer1000);
        if (confidentWrong)
        {
            json.Number("confidentWrong", shape.ConfidentWrong);
        }

        json.Number("controlItems", shape.ControlItems);
        json.Number("controlCorrect", shape.ControlCorrect);
        json.Number("controlAccuracy", shape.ControlAccuracy);
        json.Close('}');
    }

    /// <summary>
    ///     Writes the result of one single-shape tranche: the row of its shape and the rows of its kinds (an empty object for a shape without kinds).
    /// </summary>
    /// <param name="json">The writer.</param>
    /// <param name="name">The name of the member.</param>
    /// <param name="result">The result of the tranche.</param>
    private static void WriteSingleShape(JsonText json, string name, ShapesDevSingleShapeResult result)
    {
        json.Open(name, '{');
        WriteShape(json, "shape", result.Overall, true);
        json.Open("kinds", '{');
        for (var i = 0; i < result.Kinds.Count; i++)
        {
            WriteShape(json, result.Spec.Kinds[i].Name, result.Kinds[i], true);
        }

        json.Close('}');
        json.Close('}');
    }

    /// <summary>
    ///     Writes the pooled counts of the compound-head and the headings tranche, from which the primary metric of the experiment is read.
    /// </summary>
    /// <param name="json">The writer.</param>
    /// <param name="compoundHead">The row of the compound-head tranche.</param>
    /// <param name="headings">The row of the headings tranche.</param>
    private static void WritePooledPrimary(JsonText json, ShapesDevShapeResult compoundHead, ShapesDevShapeResult headings)
    {
        var decisions = compoundHead.NounVerbDecisions + headings.NounVerbDecisions;
        var harmful = compoundHead.HarmfulErrors + headings.HarmfulErrors;
        var targets = compoundHead.TargetItems + headings.TargetItems;
        var correct = compoundHead.TargetCorrect + headings.TargetCorrect;
        json.Open("pooledPrimary", '{');
        json.Number("targetItems", targets);
        json.Number("targetCorrect", correct);
        json.Number("targetAccuracy", targets == 0 ? 0 : 100.0 * correct / targets);
        json.Number("decisions", decisions);
        json.Number("harmful", harmful);
        json.Number("harmfulPer1k", decisions == 0 ? 0 : 1000.0 * harmful / decisions);
        json.Number("confidentWrongTotal", compoundHead.ConfidentWrong + headings.ConfidentWrong);
        json.Close('}');
    }

    /// <summary>
    ///     Writes the per-item outcomes of the target items of a tranche.
    /// </summary>
    /// <param name="json">The writer.</param>
    /// <param name="name">The name of the member.</param>
    /// <param name="items">All items.</param>
    /// <param name="outcomes">The outcome of every item.</param>
    /// <param name="tranche">The tranche to write.</param>
    /// <exception cref="InvalidDataException">An item id occurs twice in the tranche.</exception>
    private static void WriteItems(JsonText json, string name, IReadOnlyList<ShapesDevItem> items, IReadOnlyList<Outcome> outcomes, ShapesDevTranche tranche)
    {
        json.Open(name, '{');
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Tranche != tranche || !items[i].IsTarget)
            {
                continue;
            }

            if (!seen.Add(items[i].Id))
            {
                throw new InvalidDataException($"The item id '{items[i].Id}' occurs twice in the {name} tranche.");
            }

            json.Open(items[i].Id, '{');
            json.Number("d", outcomes[i].NounVerb ? 1 : 0);
            json.Number("c", outcomes[i].Correct ? 1 : 0);
            json.Number("h", outcomes[i].Harmful ? 1 : 0);
            json.Close('}');
        }

        json.Close('}');
    }

    /// <summary>
    ///     A minimal JSON writer: objects, arrays of strings, strings, numbers and booleans, no white space.
    /// </summary>
    private sealed class JsonText
    {
        /// <summary>
        ///     The text so far.
        /// </summary>
        private readonly StringBuilder _text = new();

        /// <summary>
        ///     Whether the next member needs a comma before it.
        /// </summary>
        private bool _comma;

        /// <summary>
        ///     Opens an object or an array.
        /// </summary>
        /// <param name="name">The member name, or <c>null</c> for the root or an array element.</param>
        /// <param name="bracket">The opening bracket.</param>
        public void Open(string? name, char bracket)
        {
            Key(name);
            _text.Append(bracket);
            _comma = false;
        }

        /// <summary>
        ///     Closes an object or an array.
        /// </summary>
        /// <param name="bracket">The closing bracket.</param>
        public void Close(char bracket)
        {
            _text.Append(bracket);
            _comma = true;
        }

        /// <summary>
        ///     Writes a string member.
        /// </summary>
        /// <param name="name">The member name.</param>
        /// <param name="value">The value.</param>
        public void Text(string name, string value)
        {
            Key(name);
            Quote(value);
            _comma = true;
        }

        /// <summary>
        ///     Writes an array of strings.
        /// </summary>
        /// <param name="name">The member name.</param>
        /// <param name="values">The values.</param>
        public void Strings(string name, IEnumerable<string> values)
        {
            Open(name, '[');
            foreach (var value in values)
            {
                Key(null);
                Quote(value);
                _comma = true;
            }

            Close(']');
        }

        /// <summary>
        ///     Writes an integer member.
        /// </summary>
        /// <param name="name">The member name.</param>
        /// <param name="value">The value.</param>
        public void Number(string name, long value)
        {
            Key(name);
            _text.Append(value.ToString(CultureInfo.InvariantCulture));
            _comma = true;
        }

        /// <summary>
        ///     Writes a floating-point member (a value that is not finite is written as 0).
        /// </summary>
        /// <param name="name">The member name.</param>
        /// <param name="value">The value.</param>
        public void Number(string name, double value)
        {
            Key(name);
            _text.Append(!double.IsNaN(value) && !double.IsInfinity(value) ? value.ToString("R", CultureInfo.InvariantCulture) : "0");
            _comma = true;
        }

        /// <summary>
        ///     Writes a boolean member.
        /// </summary>
        /// <param name="name">The member name.</param>
        /// <param name="value">The value.</param>
        public void Bool(string name, bool value)
        {
            Key(name);
            _text.Append(value ? "true" : "false");
            _comma = true;
        }

        /// <inheritdoc />
        public override string ToString() => _text.ToString();

        /// <summary>
        ///     Writes the separator and the member name.
        /// </summary>
        /// <param name="name">The member name, or <c>null</c> for none.</param>
        private void Key(string? name)
        {
            if (_comma)
            {
                _text.Append(',');
            }

            if (name != null)
            {
                Quote(name);
                _text.Append(':');
            }
        }

        /// <summary>
        ///     Writes a quoted and escaped string.
        /// </summary>
        /// <param name="value">The string.</param>
        private void Quote(string value)
        {
            _text.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"':
                        _text.Append("\\\"");
                        break;
                    case '\\':
                        _text.Append("\\\\");
                        break;
                    case '\n':
                        _text.Append("\\n");
                        break;
                    case '\r':
                        _text.Append("\\r");
                        break;
                    case '\t':
                        _text.Append("\\t");
                        break;
                    default:
                        if (c < ' ')
                        {
                            _text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            _text.Append(c);
                        }

                        break;
                }
            }

            _text.Append('"');
        }
    }
}
