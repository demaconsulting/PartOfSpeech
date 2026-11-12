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
    ///     Gets or sets the SHA-256 of the compound-head-2 tranche of the shapes development set.
    /// </summary>
    public string CompoundHead2Sha256 { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the SHA-256 of the headings-2 tranche of the shapes development set.
    /// </summary>
    public string Headings2Sha256 { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the SHA-256 of the deverbal-heads tranche of the shapes development set.
    /// </summary>
    public string DeverbalHeadsSha256 { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the SHA-256 of the defined-terms tranche of the shapes development set.
    /// </summary>
    public string DefinedTermsSha256 { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the SHA-256 of the step-lines tranche of the shapes development set.
    /// </summary>
    public string StepLinesSha256 { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the SHA-256 of the clause-verbs tranche of the shapes development set.
    /// </summary>
    public string ClauseVerbsSha256 { get; set; } = string.Empty;

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
    ///     Gets the items of the compound-head-2 tranche of the shapes development set.
    /// </summary>
    public IReadOnlyList<ShapesDevItem> CompoundHead2 { get; set; } = Array.Empty<ShapesDevItem>();

    /// <summary>
    ///     Gets the items of the headings-2 tranche of the shapes development set.
    /// </summary>
    public IReadOnlyList<ShapesDevItem> Headings2 { get; set; } = Array.Empty<ShapesDevItem>();

    /// <summary>
    ///     Gets the items of the deverbal-heads tranche of the shapes development set.
    /// </summary>
    public IReadOnlyList<ShapesDevItem> DeverbalHeads { get; set; } = Array.Empty<ShapesDevItem>();

    /// <summary>
    ///     Gets the items of the defined-terms tranche of the shapes development set.
    /// </summary>
    public IReadOnlyList<ShapesDevItem> DefinedTerms { get; set; } = Array.Empty<ShapesDevItem>();

    /// <summary>
    ///     Gets the items of the step-lines tranche of the shapes development set.
    /// </summary>
    public IReadOnlyList<ShapesDevItem> StepLines { get; set; } = Array.Empty<ShapesDevItem>();

    /// <summary>
    ///     Gets the items of the clause-verbs tranche of the shapes development set.
    /// </summary>
    public IReadOnlyList<ShapesDevItem> ClauseVerbs { get; set; } = Array.Empty<ShapesDevItem>();

    /// <summary>
    ///     Gets the hashes of the inputs (the commit and the dirty flag are set by the caller).
    /// </summary>
    public ArmInputs Inputs { get; set; } = new();

    /// <summary>
    ///     Reads the development sets of the arm evaluation: open dev, gold v2 dev, the standard and the hard tranche of the shapes
    ///     development set, its compound-head, headings and named-state tranches, its compound-head-2 and headings-2 tranches, its four technical
    ///     tranches (deverbal-heads, defined-terms, step-lines, clause-verbs), and the mapped development table of the treebank.
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
        var compound2 = DevSets.ReadShapesDev(repositoryRoot, ShapesDevTranche.CompoundHead2, out var compound2Sha);
        var headings2 = DevSets.ReadShapesDev(repositoryRoot, ShapesDevTranche.Headings2, out var headings2Sha);
        var deverbal = DevSets.ReadShapesDev(repositoryRoot, ShapesDevTranche.DeverbalHeads, out var deverbalSha);
        var terms = DevSets.ReadShapesDev(repositoryRoot, ShapesDevTranche.DefinedTerms, out var termsSha);
        var steps = DevSets.ReadShapesDev(repositoryRoot, ShapesDevTranche.StepLines, out var stepsSha);
        var clauses = DevSets.ReadShapesDev(repositoryRoot, ShapesDevTranche.ClauseVerbs, out var clausesSha);
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
            CompoundHead2 = compound2,
            Headings2 = headings2,
            DeverbalHeads = deverbal,
            DefinedTerms = terms,
            StepLines = steps,
            ClauseVerbs = clauses,
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
                CompoundHead2Sha256 = compound2Sha,
                Headings2Sha256 = headings2Sha,
                DeverbalHeadsSha256 = deverbalSha,
                DefinedTermsSha256 = termsSha,
                StepLinesSha256 = stepsSha,
                ClauseVerbsSha256 = clausesSha,
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
///         <b>Follow-up tranches.</b> <c>shapesFollowup</c> is an additional block (<c>shapesNew</c> and its members are unchanged): it holds the
///         rows of the four tranches of the follow-up measure (<c>compoundHead</c>, <c>compoundHead2</c>, <c>headings</c>, <c>headings2</c>,
///         each as in <c>shapesNew</c>), <c>pooledPrimary</c> (the harmful errors per 1,000 noun-verb decisions over the target items of the
///         enlarged compound dev, that is compound-head and compound-head-2, and of all heading dev items, headings and headings-2, with the
///         counts it is derived from and the total of the confident wrong answers of the four tranches), <c>imperativeHeadings</c> (the items of kind
///         <c>imperative</c> of both headings tranches pooled, with their accuracy, decisions, harmful errors per 1,000 decisions and confident
///         wrong answers) and <c>compoundControls</c> (the controls of both compound-head tranches pooled). The two new file hashes are
///         appended to <c>inputs</c>.
///     </para>
///     <para>
///         <b>Technical tranches.</b> <c>shapesTechnical</c> is a further additional block (every earlier block and key is unchanged, the schema
///         stays 1): it holds the rows of the four technical tranches (<c>deverbalHeads</c>, <c>definedTerms</c>, <c>stepLines</c>,
///         <c>clauseVerbs</c>, each as in <c>shapesNew</c> with the target items wrong at 0.90 as <c>wrong90</c> and the controls wrong at
///         0.90 and 0.95 as <c>controlWrong90</c> and <c>controlConfidentWrong</c>), <c>pooledTechnical</c> (the target items, the noun-verb
///         decisions and the harmful errors of the deverbal-heads, step-lines and clause-verbs tranches with the harmful errors per 1,000 decisions
///         derived from them), <c>termError</c> (the exact-tag errors of the defined-terms targets per 1,000 target items) and <c>calibration</c>
///         (see <see cref="ShapesDevCalibration"/>: the pooled scope of the four technical tranches, the pooled scope of all nine single-shape
///         tranches and one scope per technical tranche). The four new file hashes are appended to <c>inputs</c>. No threshold is read, changed or written.
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
        var scores = ShapesDevCommand.AllSingleTranches.ToDictionary(t => t, t => Score(tagger, t, ItemsOf(data, t)));
        var compoundHead = scores[ShapesDevTranche.CompoundHead].Result;
        var headings = scores[ShapesDevTranche.Headings].Result;
        var namedState = scores[ShapesDevTranche.NamedState].Result;
        var compoundHead2 = scores[ShapesDevTranche.CompoundHead2].Result;
        var headings2 = scores[ShapesDevTranche.Headings2].Result;
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
        json.Open("shapesFollowup", '{');
        WriteSingleShape(json, "compoundHead", compoundHead);
        WriteSingleShape(json, "compoundHead2", compoundHead2);
        WriteSingleShape(json, "headings", headings);
        WriteSingleShape(json, "headings2", headings2);
        WriteFollowupPrimary(json, compoundHead, compoundHead2, headings, headings2);
        WriteImperativeHeadings(json, headings, headings2);
        WriteCompoundControls(json, compoundHead.Overall, compoundHead2.Overall);
        json.Close('}');
        WriteTechnical(json, scores);
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
        json.Text("compoundHead2Sha256", data.Inputs.CompoundHead2Sha256);
        json.Text("headings2Sha256", data.Inputs.Headings2Sha256);
        json.Text("deverbalHeadsSha256", data.Inputs.DeverbalHeadsSha256);
        json.Text("definedTermsSha256", data.Inputs.DefinedTermsSha256);
        json.Text("stepLinesSha256", data.Inputs.StepLinesSha256);
        json.Text("clauseVerbsSha256", data.Inputs.ClauseVerbsSha256);
        json.Close('}');
        json.Close('}');
        return json.ToString() + "\n";
    }

    /// <summary>
    ///     The result and the outcomes of one single-shape tranche.
    /// </summary>
    private sealed class TrancheScore
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="TrancheScore"/> class.
        /// </summary>
        /// <param name="result">The summary of the tranche.</param>
        /// <param name="outcomes">The outcome of every item, in the order of the items.</param>
        public TrancheScore(ShapesDevSingleShapeResult result, List<Outcome> outcomes)
        {
            Result = result;
            Outcomes = outcomes;
        }

        /// <summary>
        ///     Gets the summary of the tranche.
        /// </summary>
        public ShapesDevSingleShapeResult Result { get; }

        /// <summary>
        ///     Gets the outcome of every item, in the order of the items.
        /// </summary>
        public List<Outcome> Outcomes { get; }
    }

    /// <summary>
    ///     Judges and summarizes the items of one single-shape tranche.
    /// </summary>
    /// <param name="tagger">The tagger.</param>
    /// <param name="tranche">The tranche.</param>
    /// <param name="items">The items of the tranche.</param>
    /// <returns>The summary and the outcomes.</returns>
    private static TrancheScore Score(Tagger tagger, ShapesDevTranche tranche, IReadOnlyList<ShapesDevItem> items)
    {
        var outcomes = ShapesDevTrancheEvaluator.Judge(tagger, items);
        return new TrancheScore(ShapesDevTrancheEvaluator.Summarize(tranche, items, outcomes), outcomes);
    }

    /// <summary>
    ///     Gets the items that the data holds for a single-shape tranche.
    /// </summary>
    /// <param name="data">The development sets.</param>
    /// <param name="tranche">The single-shape tranche.</param>
    /// <returns>The items.</returns>
    private static IReadOnlyList<ShapesDevItem> ItemsOf(ArmEvaluationData data, ShapesDevTranche tranche) => tranche switch
    {
        ShapesDevTranche.CompoundHead => data.CompoundHead,
        ShapesDevTranche.Headings => data.Headings,
        ShapesDevTranche.NamedState => data.NamedState,
        ShapesDevTranche.CompoundHead2 => data.CompoundHead2,
        ShapesDevTranche.Headings2 => data.Headings2,
        ShapesDevTranche.DeverbalHeads => data.DeverbalHeads,
        ShapesDevTranche.DefinedTerms => data.DefinedTerms,
        ShapesDevTranche.StepLines => data.StepLines,
        ShapesDevTranche.ClauseVerbs => data.ClauseVerbs,
        _ => throw new ArgumentOutOfRangeException(nameof(tranche)),
    };

    /// <summary>
    ///     Writes the <c>shapesTechnical</c> block: the four technical tranches, the pooled counts and the calibration measurement.
    /// </summary>
    /// <param name="json">The writer.</param>
    /// <param name="scores">The scores of the nine single-shape tranches.</param>
    private static void WriteTechnical(JsonText json, IReadOnlyDictionary<ShapesDevTranche, TrancheScore> scores)
    {
        var deverbal = scores[ShapesDevTranche.DeverbalHeads].Result;
        var terms = scores[ShapesDevTranche.DefinedTerms].Result;
        var steps = scores[ShapesDevTranche.StepLines].Result;
        var clauses = scores[ShapesDevTranche.ClauseVerbs].Result;
        json.Open("shapesTechnical", '{');
        WriteSingleShape(json, "deverbalHeads", deverbal, true);
        WriteSingleShape(json, "definedTerms", terms, true);
        WriteSingleShape(json, "stepLines", steps, true);
        WriteSingleShape(json, "clauseVerbs", clauses, true);
        WritePooledTechnical(json, deverbal.Overall, steps.Overall, clauses.Overall);
        WriteTermError(json, terms.Overall);
        json.Open("calibration", '{');
        var technicalNames = ShapesDevCommand.TechnicalTranches.Select(t => new KeyValuePair<string, IReadOnlyList<Outcome>>(DevSets.ShapesDevNameOf(t), scores[t].Outcomes)).ToList();
        var allNames = ShapesDevCommand.AllSingleTranches.Select(t => new KeyValuePair<string, IReadOnlyList<Outcome>>(DevSets.ShapesDevNameOf(t), scores[t].Outcomes)).ToList();
        WriteCalibrationScope(json, "technical", ShapesDevCalibration.Measure(technicalNames).Pooled);
        WriteCalibrationScope(json, "allSingle", ShapesDevCalibration.Measure(allNames).Pooled);
        json.Open("tranches", '{');
        var names = new[] { "deverbalHeads", "definedTerms", "stepLines", "clauseVerbs" };
        var perTranche = ShapesDevCalibration.Measure(technicalNames).Tranches;
        for (var i = 0; i < names.Length; i++)
        {
            WriteCalibrationScope(json, names[i], perTranche[i]);
        }

        json.Close('}');
        json.Close('}');
        json.Close('}');
    }

    /// <summary>
    ///     Writes the pooled counts of the three noun-verb technical tranches (deverbal heads, step lines, clause verbs), from which an analyzer
    ///     re-derives the harmful errors per 1,000 noun-verb decisions: the target items, the correct ones, the decisions and the harmful errors,
    ///     and the target items that are wrong at 0.90 and 0.95.
    /// </summary>
    /// <param name="json">The writer.</param>
    /// <param name="deverbal">The row of the deverbal-heads tranche.</param>
    /// <param name="steps">The row of the step-lines tranche.</param>
    /// <param name="clauses">The row of the clause-verbs tranche.</param>
    private static void WritePooledTechnical(JsonText json, ShapesDevShapeResult deverbal, ShapesDevShapeResult steps, ShapesDevShapeResult clauses)
    {
        var rows = new[] { deverbal, steps, clauses };
        var decisions = rows.Sum(r => r.NounVerbDecisions);
        var harmful = rows.Sum(r => r.HarmfulErrors);
        var targets = rows.Sum(r => r.TargetItems);
        var correct = rows.Sum(r => r.TargetCorrect);
        json.Open("pooledTechnical", '{');
        json.Number("targetItems", targets);
        json.Number("targetCorrect", correct);
        json.Number("targetAccuracy", targets == 0 ? 0 : 100.0 * correct / targets);
        json.Number("decisions", decisions);
        json.Number("harmful", harmful);
        json.Number("harmfulPer1k", decisions == 0 ? 0 : 1000.0 * harmful / decisions);
        json.Number("confidentWrongTotal", rows.Sum(r => r.ConfidentWrong));
        json.Number("wrong90Total", rows.Sum(r => r.ConfidentWrong90));
        json.Close('}');
    }

    /// <summary>
    ///     Writes the exact-tag error of the defined-terms targets (a tranche with almost no noun-verb decisions): the target items, the wrong ones
    ///     and the wrong ones per 1,000 target items, with the wrong answers at 0.90 and 0.95.
    /// </summary>
    /// <param name="json">The writer.</param>
    /// <param name="terms">The row of the defined-terms tranche.</param>
    private static void WriteTermError(JsonText json, ShapesDevShapeResult terms)
    {
        var errors = terms.TargetItems - terms.TargetCorrect;
        json.Open("termError", '{');
        json.Number("targetItems", terms.TargetItems);
        json.Number("targetCorrect", terms.TargetCorrect);
        json.Number("errors", errors);
        json.Number("errorPer1k", terms.TargetItems == 0 ? 0 : 1000.0 * errors / terms.TargetItems);
        json.Number("confidentWrong", terms.ConfidentWrong);
        json.Number("wrong90", terms.ConfidentWrong90);
        json.Close('}');
    }

    /// <summary>
    ///     Writes the calibration measurement of one scope: the judged words, the rows of the thresholds (answers, wrong, error per 1,000 answers,
    ///     Wilson interval per 1,000) and the reliability bins.
    /// </summary>
    /// <param name="json">The writer.</param>
    /// <param name="name">The name of the member.</param>
    /// <param name="scope">The scope.</param>
    private static void WriteCalibrationScope(JsonText json, string name, CalibrationScope scope)
    {
        json.Open(name, '{');
        json.Number("judged", scope.Judged);
        json.Open("thresholds", '[');
        foreach (var row in scope.Thresholds)
        {
            json.Open(null, '{');
            json.Number("threshold", row.Threshold);
            json.Number("answers", row.Answers);
            json.Number("wrong", row.Wrong);
            json.Number("errorPer1k", row.ErrorPer1000);
            json.Number("intervalLowPer1k", row.IntervalLowPer1000);
            json.Number("intervalHighPer1k", row.IntervalHighPer1000);
            json.Close('}');
        }

        json.Close(']');
        json.Open("bins", '[');
        foreach (var bin in scope.Bins)
        {
            json.Open(null, '{');
            json.Number("low", bin.Low);
            json.Number("high", bin.High);
            json.Number("count", bin.Decisions);
            json.Number("meanConfidence", bin.MeanConfidence);
            json.Number("correctPercent", bin.CorrectPercent);
            json.Number("harmful", bin.Harmful);
            json.Close('}');
        }

        json.Close(']');
        json.Close('}');
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
    /// <param name="technical">Whether to write the counts of the technical measure (<c>wrong90</c>, <c>controlWrong90</c> and <c>controlConfidentWrong</c>) as well.</param>
    private static void WriteShape(JsonText json, string name, ShapesDevShapeResult shape, bool confidentWrong = false, bool technical = false)
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

        if (technical)
        {
            json.Number("wrong90", shape.ConfidentWrong90);
        }

        json.Number("controlItems", shape.ControlItems);
        json.Number("controlCorrect", shape.ControlCorrect);
        json.Number("controlAccuracy", shape.ControlAccuracy);
        if (technical)
        {
            json.Number("controlWrong90", shape.ControlConfidentWrong90);
            json.Number("controlConfidentWrong", shape.ControlConfidentWrong);
        }

        json.Close('}');
    }

    /// <summary>
    ///     Writes the result of one single-shape tranche: the row of its shape and the rows of its kinds (an empty object for a shape without kinds).
    /// </summary>
    /// <param name="json">The writer.</param>
    /// <param name="name">The name of the member.</param>
    /// <param name="result">The result of the tranche.</param>
    /// <param name="technical">Whether to write the counts of the technical measure (<c>wrong90</c> and the wrong controls) as well.</param>
    private static void WriteSingleShape(JsonText json, string name, ShapesDevSingleShapeResult result, bool technical = false)
    {
        json.Open(name, '{');
        WriteShape(json, "shape", result.Overall, true, technical);
        json.Open("kinds", '{');
        for (var i = 0; i < result.Kinds.Count; i++)
        {
            WriteShape(json, result.Spec.Kinds[i].Name, result.Kinds[i], true, technical);
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
    ///     Writes the primary and co-primary counts of the follow-up measure: the target items of the enlarged compound dev (compound-head
    ///     and compound-head-2) and of all heading dev items (headings and headings-2), the noun-verb decisions and harmful errors that the
    ///     rate is derived from, and the target items that are wrong at a confidence of at least 0.95 summed over the four tranches.
    /// </summary>
    /// <param name="json">The writer.</param>
    /// <param name="compoundHead">The result of the compound-head tranche.</param>
    /// <param name="compoundHead2">The result of the compound-head-2 tranche.</param>
    /// <param name="headings">The result of the headings tranche.</param>
    /// <param name="headings2">The result of the headings-2 tranche.</param>
    private static void WriteFollowupPrimary(JsonText json, ShapesDevSingleShapeResult compoundHead, ShapesDevSingleShapeResult compoundHead2, ShapesDevSingleShapeResult headings, ShapesDevSingleShapeResult headings2)
    {
        var rows = new[] { compoundHead.Overall, compoundHead2.Overall, headings.Overall, headings2.Overall };
        var decisions = rows.Sum(r => r.NounVerbDecisions);
        var harmful = rows.Sum(r => r.HarmfulErrors);
        var targets = rows.Sum(r => r.TargetItems);
        var correct = rows.Sum(r => r.TargetCorrect);
        json.Open("pooledPrimary", '{');
        json.Number("targetItems", targets);
        json.Number("targetCorrect", correct);
        json.Number("targetAccuracy", targets == 0 ? 0 : 100.0 * correct / targets);
        json.Number("decisions", decisions);
        json.Number("harmful", harmful);
        json.Number("harmfulPer1k", decisions == 0 ? 0 : 1000.0 * harmful / decisions);
        json.Number("confidentWrongTotal", rows.Sum(r => r.ConfidentWrong));
        json.Close('}');
    }

    /// <summary>
    ///     Writes the pooled result of the imperative headings: the items of kind <c>imperative</c> of the headings and the headings-2 tranche.
    /// </summary>
    /// <param name="json">The writer.</param>
    /// <param name="headings">The result of the headings tranche.</param>
    /// <param name="headings2">The result of the headings-2 tranche.</param>
    private static void WriteImperativeHeadings(JsonText json, ShapesDevSingleShapeResult headings, ShapesDevSingleShapeResult headings2)
    {
        var rows = new[] { KindRow(headings, "imperative"), KindRow(headings2, "imperative") };
        var decisions = rows.Sum(r => r.NounVerbDecisions);
        var harmful = rows.Sum(r => r.HarmfulErrors);
        var targets = rows.Sum(r => r.TargetItems);
        var correct = rows.Sum(r => r.TargetCorrect);
        json.Open("imperativeHeadings", '{');
        json.Number("targetItems", targets);
        json.Number("targetCorrect", correct);
        json.Number("targetAccuracy", targets == 0 ? 0 : 100.0 * correct / targets);
        json.Number("decisions", decisions);
        json.Number("harmful", harmful);
        json.Number("harmfulPer1k", decisions == 0 ? 0 : 1000.0 * harmful / decisions);
        json.Number("confidentWrong", rows.Sum(r => r.ConfidentWrong));
        json.Close('}');
    }

    /// <summary>
    ///     Writes the pooled control items of the two compound-head tranches (which should all stay correct).
    /// </summary>
    /// <param name="json">The writer.</param>
    /// <param name="compoundHead">The row of the compound-head tranche.</param>
    /// <param name="compoundHead2">The row of the compound-head-2 tranche.</param>
    private static void WriteCompoundControls(JsonText json, ShapesDevShapeResult compoundHead, ShapesDevShapeResult compoundHead2)
    {
        var controls = compoundHead.ControlItems + compoundHead2.ControlItems;
        var correct = compoundHead.ControlCorrect + compoundHead2.ControlCorrect;
        json.Open("compoundControls", '{');
        json.Number("controlItems", controls);
        json.Number("controlCorrect", correct);
        json.Number("controlAccuracy", controls == 0 ? 0 : 100.0 * correct / controls);
        json.Close('}');
    }

    /// <summary>
    ///     Gets the row of one kind of a headings tranche.
    /// </summary>
    /// <param name="result">The result of a headings tranche.</param>
    /// <param name="kind">The name of the kind.</param>
    /// <returns>The row of the kind.</returns>
    private static ShapesDevShapeResult KindRow(ShapesDevSingleShapeResult result, string kind)
    {
        for (var i = 0; i < result.Spec.Kinds.Count; i++)
        {
            if (result.Spec.Kinds[i].Name == kind)
            {
                return result.Kinds[i];
            }
        }

        throw new InvalidOperationException($"The tranche has no kind '{kind}'.");
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
