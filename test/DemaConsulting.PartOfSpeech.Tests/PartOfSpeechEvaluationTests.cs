using System.Globalization;
using System.Text.RegularExpressions;

namespace DemaConsulting.PartOfSpeech.Tests;

/// <summary>
///     System tests of the evaluation record of the embedded model (<c>docs/evaluation</c>) and of the copies of its
///     figures in the documentation: the results name the shipped model, the committed selection and the data that they
///     used, the thresholds and figures of the packed reference and the user guide equal the generated
///     files, and no text of the external treebank is committed. The tests read the files of the working copy, parse
///     the JSON with regular expressions (the JSON API is not part of .NET Framework) and open no held-out data file.
/// </summary>
public class PartOfSpeechEvaluationTests
{
    /// <summary>
    ///     The training configuration of the trainer, relative to the repository root.
    /// </summary>
    private const string TrainingConfigPath = "tools/DemaConsulting.PartOfSpeech.Trainer/training.json";

    /// <summary>
    ///     The committed selection of the confidence thresholds.
    /// </summary>
    private const string OperatingPointsPath = "docs/evaluation/operating-points.json";

    /// <summary>
    ///     The committed results of the held-out run, as data.
    /// </summary>
    private const string ResultsPath = "docs/evaluation/test-results.json";

    /// <summary>
    ///     The committed results of the held-out run, as a report.
    /// </summary>
    private const string ReportPath = "docs/evaluation/test-results.md";

    /// <summary>
    ///     The committed report of the development selection.
    /// </summary>
    private const string SelectionReportPath = "docs/evaluation/confidence-thresholds.md";

    /// <summary>
    ///     The technical operating point (rule <c>nv-technical-dev-v1</c>), as data.
    /// </summary>
    private const string TechnicalPointsPath = "docs/evaluation/technical-operating-points.json";

    /// <summary>
    ///     The weights file that the library embeds.
    /// </summary>
    private const string WeightsPath = "src/DemaConsulting.PartOfSpeech/Resources/weights.bin";

    /// <summary>
    ///     The explanation of the evaluation record.
    /// </summary>
    private const string EvaluationReadmePath = "docs/evaluation/README.md";

    /// <summary>
    ///     The disclosure paragraph that the generated reports carry.
    /// </summary>
    private const string DisclosurePath = "docs/evaluation/disclosure.md";

    /// <summary>
    ///     The source of the namespace reference that is packed with the library.
    /// </summary>
    private const string NamespaceDocPath = "src/DemaConsulting.PartOfSpeech/NamespaceDoc.cs";

    /// <summary>
    ///     The source of the type that documents the confidence of a token.
    /// </summary>
    private const string TaggedTokenPath = "src/DemaConsulting.PartOfSpeech/Tagging/TaggedToken.cs";

    /// <summary>
    ///     The user guide.
    /// </summary>
    private const string UserGuidePath = "docs/user_guide/introduction.md";

    /// <summary>
    ///     The root README, which carries the tables of the held-out results and of the thresholds.
    /// </summary>
    private const string ReadmePath = "README.md";

    /// <summary>
    ///     The FileAssert configuration.
    /// </summary>
    private const string FileAssertPath = ".fileassert.yaml";

    /// <summary>
    ///     The absolute address of the evaluation record that the packed reference links to.
    /// </summary>
    private const string EvaluationUrl = "https://github.com/demaconsulting/PartOfSpeech/blob/main/docs/evaluation/";

    /// <summary>
    ///     The evaluation inputs that are committed data files: the name in the results and the path in the manifest.
    /// </summary>
    private static readonly (string Name, string ManifestPath)[] RepositoryInputs =
    {
        ("open-test", "open/test.tsv"),
        ("gold-v2-test", "eval/gold-v2/test.tsv"),
        ("shape-probe", "eval/shapes/probe.tsv"),
        ("challenge-set", "eval/ste-challenge/test.tsv")
    };

    /// <summary>
    ///     The files of the evaluation record.
    /// </summary>
    private static readonly string[] RecordFiles =
    {
        EvaluationReadmePath, DisclosurePath, SelectionReportPath, OperatingPointsPath, TechnicalPointsPath, ReportPath, ResultsPath
    };

    /// <summary>
    ///     The id of a sentence of the external treebank (genre, name and number), which must not be published.
    /// </summary>
    private static readonly Regex TreebankSentenceId = new(
        @"\b(?:weblog|reviews|email|answers|newsgroup)-[A-Za-z0-9._@-]+-\d{4}\b",
        RegexOptions.CultureInvariant);

    /// <summary>
    ///     Statements of the earlier releases that said that no threshold is recommended.
    /// </summary>
    private static readonly string[] StaleStatements =
    {
        "recommends no", "does not recommend", "recommend a confidence threshold until", "gives no threshold",
        "will be added after", "measured guidance will be added"
    };

    /// <summary>
    ///     Proves that the results of the held-out run name the model that is shipped, the committed selection of the
    ///     thresholds (by the hash of its bytes) and the hash of every input that is a committed data file or the pinned
    ///     treebank file, and that the selection names the committed development files. A retrained model, a changed
    ///     selection or a changed data file fails here until the evaluation is redone.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Evaluation_Results_RecordTheShippedModelAndInputs()
    {
        // Arrange: the record, the configuration of the shipped model, the manifest and the pins of the treebank
        var missingFiles = RecordFiles.Where(path => !File.Exists(DataFiles.Repository(path))).ToList();
        Assert.Empty(missingFiles);
        var config = ReadText(TrainingConfigPath);
        var selection = ReadText(OperatingPointsPath);
        var results = ReadText(ResultsPath);
        var manifest = ReadManifest();
        var pins = ReadText("tools/data/pins/ud-ewt.tsv");
        var modelHash = StringMember(config, "imageSha256");
        var selectionHash = DataFiles.Sha256Hex(DataFiles.Repository(OperatingPointsPath));

        // Act: collect what differs
        var problems = new List<string>();
        Check(problems, "selection model", modelHash, StringMember(selection, "modelImageSha256"));
        Check(problems, "results model", modelHash, StringMember(results, "modelImageSha256"));
        Check(problems, "results selection", selectionHash, StringMember(results, "selectionSha256"));
        Check(problems, "results rule", StringMember(selection, "ruleId"), StringMember(results, "ruleId"));
        foreach (var (name, manifestPath) in RepositoryInputs)
        {
            var recorded = StringMember(results, "sha256", NamedObject(results, name, 0));
            Check(problems, "input " + name, manifest[manifestPath], recorded);
        }

        foreach (var path in new[] { "open/dev.tsv", "eval/gold-v2/dev.tsv" })
        {
            var recorded = StringMember(selection, "sha256", NamedObject(selection, path, 0, "path"));
            Check(problems, "development file " + path, manifest[path], recorded);
        }

        var treebankHash = StringMember(results, "sha256", NamedObject(results, "ud-test-source", 0));
        if (pins.IndexOf("en_ewt-ud-test.conllu\t" + treebankHash, StringComparison.Ordinal) < 0)
        {
            problems.Add("the treebank source hash of the results is not the pinned one");
        }

        // Assert: the reports quote the same hashes as the data, and nothing differs
        Assert.Empty(problems);
        Assert.Contains(modelHash, ReadText(ReportPath), StringComparison.Ordinal);
        Assert.Contains(selectionHash, ReadText(ReportPath), StringComparison.Ordinal);
        Assert.Contains(modelHash, ReadText(SelectionReportPath), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that the thresholds and the figures that the packed reference, the user guide and the guidance
    ///     assertions of the build state equal the committed selection and results: the two thresholds, their coverage
    ///     and harmful-error rate on the development data and on the open test split, the rate without a threshold and
    ///     the links to the evaluation record; that the type of the confidence links to the guidance without a number;
    ///     and that no document still says that no threshold is recommended.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Evaluation_ConfidenceGuidance_MatchesTheCommittedFigures()
    {
        // Arrange: the development and test figures of both points, the rate without a threshold, and the documents
        var selection = ReadText(OperatingPointsPath);
        var results = ReadText(ResultsPath);
        var strict = new GuidancePoint(selection, results, "strict");
        var balanced = new GuidancePoint(selection, results, "balanced");
        var openTest = NamedObject(results, "open-test", Math.Max(0, results.IndexOf("\"sets\"", StringComparison.Ordinal)));
        var reference = One(NumberMember(results, "harmfulPer1000", openTest));
        var namespaceDoc = DocumentationText(NamespaceDocPath);
        var userGuide = ReadText(UserGuidePath);
        var problems = new List<string>();

        // Act: the packed reference states each point in its own bullet, with the figures of the selection and the run
        var guidance = Between(namespaceDoc, "## Confidence thresholds", "## Tokenization conventions");
        var strictBullet = Between(guidance, "**strict**", "**balanced**");
        var balancedBullet = Between(guidance, "**balanced**", "</list>");
        problems.AddRange(Missing("namespace strict", strictBullet, strict.NamespaceFragments()));
        problems.AddRange(Missing("namespace balanced", balancedBullet, balanced.NamespaceFragments()));
        problems.AddRange(Missing("namespace", guidance, new[]
        {
            reference + " of 1,000", "must use a threshold", EvaluationUrl + "confidence-thresholds.md",
            EvaluationUrl + "test-results.md", EvaluationUrl + "README.md"
        }));

        // The user guide has one row for each point and the rate without a threshold
        var rows = userGuide.Split('\n').ToList();
        problems.AddRange(Missing("user guide strict", rows.FirstOrDefault(row => row.StartsWith("| strict ", StringComparison.Ordinal)) ?? string.Empty, strict.TableFragments()));
        problems.AddRange(Missing("user guide balanced", rows.FirstOrDefault(row => row.StartsWith("| balanced ", StringComparison.Ordinal)) ?? string.Empty, balanced.TableFragments()));
        problems.AddRange(Missing("user guide", userGuide, new[]
        {
            reference + " of 1,000", "Confidence >= " + strict.Threshold, EvaluationUrl + "README.md"
        }));

        // The generated reference is asserted by the build with the same thresholds
        var fileAssert = ReadText(FileAssertPath);
        problems.AddRange(Missing(".fileassert.yaml", fileAssert, new[]
        {
            "ApiMark_ConfidenceGuidanceDocumented", "\"## Confidence thresholds\"", "\"" + strict.Threshold + "\"",
            "\"" + balanced.Threshold + "\"", EvaluationUrl.Substring("https://".Length) + "confidence-thresholds.md",
            "DemaConsulting.PartOfSpeech.md#confidence-thresholds"
        }));

        // The documentation of the confidence of a token links to the guidance and states no number of it
        var taggedToken = ReadText(TaggedTokenPath);
        problems.AddRange(Missing("TaggedToken", taggedToken, new[] { "DemaConsulting.PartOfSpeech.md#confidence-thresholds" }));
        problems.AddRange(new[] { strict.Threshold, balanced.Threshold }
            .Where(threshold => taggedToken.IndexOf(threshold, StringComparison.Ordinal) >= 0)
            .Select(threshold => "TaggedToken states the threshold " + threshold));

        // No document says any longer that no threshold is recommended
        foreach (var path in new[] { "README.md", UserGuidePath, NamespaceDocPath, TaggedTokenPath, "docs/design/part-of-speech.md" })
        {
            var text = Collapse(ReadText(path)).ToLowerInvariant();
            problems.AddRange(StaleStatements
                .Where(statement => text.IndexOf(statement, StringComparison.Ordinal) >= 0)
                .Select(statement => $"{path}: stale statement '{statement}'"));
        }

        // Assert: the copies equal the committed figures
        Assert.Empty(problems);
    }

    /// <summary>
    ///     Proves that the figures that the explanation of the evaluation record states equal the committed results:
    ///     the harmful-error rates without a threshold, the two thresholds with their rate and coverage, and the
    ///     limitation of the shape probe.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Evaluation_Record_StatesTheFiguresOfTheReports()
    {
        // Arrange: the figures of the open test split, gold v2 and the shape probe
        var selection = ReadText(OperatingPointsPath);
        var results = ReadText(ResultsPath);
        var sets = Math.Max(0, results.IndexOf("\"sets\"", StringComparison.Ordinal));
        var open = NamedObject(results, "open-test", sets);
        var gold = NamedObject(results, "gold-v2-test", sets);
        var probe = NamedObject(results, "shape-probe", sets);
        var strict = new GuidancePoint(selection, results, "strict");
        var balanced = new GuidancePoint(selection, results, "balanced");
        var goldStrict = ResultPoint(results, "gold-v2-test", "strict");
        var goldBalanced = ResultPoint(results, "gold-v2-test", "balanced");
        var probeReference = ResultPoint(results, "shape-probe", "reference");
        var probeStrict = ResultPoint(results, "shape-probe", "strict");
        var probeBalanced = ResultPoint(results, "shape-probe", "balanced");
        var problems = new List<string>();

        // Act: the explanation states the same figures in prose, and the limitation of the shape probe with its counts
        var explanation = Collapse(ReadText(EvaluationReadmePath));
        problems.AddRange(Missing("evaluation README", explanation, new[]
        {
            One(NumberMember(results, "harmfulPer1000", open)) + " noun-verb decisions in 1,000",
            "gold v2 " + One(NumberMember(results, "harmfulPer1000", gold)) + " in 1,000",
            "shape probe " + One(NumberMember(results, "harmfulPer1000", probe)) + " in 1,000",
            strict.Threshold + " cuts the open test split to " + One(strict.TestHarmful) + " harmful errors in 1,000 answered decisions and answers " + Whole(strict.TestCoverage) + "%",
            balanced.Threshold + " gives " + One(balanced.TestHarmful) + " in 1,000 and answers " + Whole(balanced.TestCoverage) + "%",
            "gold v2 the figures are " + One(goldStrict.Harmful) + " (" + Whole(goldStrict.Coverage) + "% coverage) and " + One(goldBalanced.Harmful) + " (" + Whole(goldBalanced.Coverage) + "% coverage)",
            $"{probeReference.HarmfulCount} of its {probeReference.Answered} answered",
            $"{probeStrict.HarmfulCount} harmful errors of {probeStrict.Answered} answered",
            $"{probeBalanced.HarmfulCount} of {probeBalanced.Answered}"
        }));

        // Assert: the stated figures equal the committed ones
        Assert.Empty(problems);
    }

    /// <summary>
    ///     Proves that the two tables of the root README equal the committed record: every row of the table of held-out
    ///     results states the accuracy and the harmful-error rate of its set of <c>test-results.json</c>, and every row
    ///     of the table of thresholds states the threshold of <c>operating-points.json</c> with the coverage and the
    ///     harmful-error rate of its point on the open test split, formatted as the generated reports format them. The
    ///     README cannot drift from the record without this test failing.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Evaluation_ReadmeTables_MatchTheCommittedFigures()
    {
        // Arrange: the committed selection and results, and the rows of the two tables of the README
        var selection = ReadText(OperatingPointsPath);
        var results = ReadText(ResultsPath);
        var readme = ReadText(ReadmePath);
        var sets = Math.Max(0, results.IndexOf("\"sets\"", StringComparison.Ordinal));
        var resultRows = ReadTable(readme, "| Set |");
        var thresholdRows = ReadTable(readme, "| Threshold |");
        var problems = new List<string>();

        // Act: compare every cell of a row with the committed figure, formatted as the reports format it
        foreach (var row in resultRows)
        {
            var set = NamedObject(results, row[0].Trim('`'), sets);
            problems.AddRange(CompareRow(row, new[]
            {
                "`" + row[0].Trim('`') + "`",
                NumberMember(results, "accuracy", set).ToString("F2", CultureInfo.InvariantCulture),
                One(NumberMember(results, "harmfulPer1000", set))
            }));
        }

        foreach (var row in thresholdRows)
        {
            var name = row[0].Trim('`');
            var point = new GuidancePoint(selection, results, name);
            problems.AddRange(CompareRow(row, new[]
            {
                "`" + name + "`", point.Threshold, One(point.TestCoverage), One(point.TestHarmful)
            }));
        }

        // Assert: both tables exist, they list exactly the sets and the points that the guidance relies on, and nothing differs
        Assert.Equal(
            new[] { "`open-test`", "`gold-v2-test`", "`shape-probe`", "`challenge-set`", "`ud-test-mapped`" },
            resultRows.Select(row => row[0]).ToArray());
        Assert.Equal(new[] { "`strict`", "`balanced`" }, thresholdRows.Select(row => row[0]).ToArray());
        Assert.Empty(problems);
    }

    /// <summary>
    ///     Proves that the technical operating point is the one of the model that ships and of the rule that was registered for it:
    ///     the artifact names the SHA-256 of the embedded weights file and the temperature of the configuration, the hashes of the four
    ///     technical tranche files equal the manifest, the first grid threshold with at most 10 wrong answers per 1,000 answers and at
    ///     least 25% of the judged words answered (integer comparisons) is the recorded point (or there is none), and the table of the
    ///     section "Technical text" of the selection report equals the curve of the artifact row by row. A retrained model or a changed
    ///     tranche fails here until the point is computed again.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Evaluation_TechnicalOperatingPoint_MatchesTheShippedModelAndTheRule()
    {
        // Arrange: the artifact, the configuration, the manifest, the embedded weights file and the selection report
        var json = ReadText(TechnicalPointsPath);
        var config = ReadText(TrainingConfigPath);
        var manifest = ReadManifest();
        var report = ReadText(SelectionReportPath);
        var curveStart = json.IndexOf("\"curve\"", StringComparison.Ordinal);
        var curveEnd = json.IndexOf("\"nounVerbDecisions\"", StringComparison.Ordinal);
        Assert.True(curveStart >= 0 && curveEnd > curveStart, "The artifact has no curve");
        var curve = Regex.Matches(
                json.Substring(curveStart, curveEnd - curveStart),
                "\"threshold\":\\s*([0-9.]+),\\s*\"answers\":\\s*([0-9]+),\\s*\"coveragePercent\":\\s*([0-9.eE+-]+),\\s*\"wrong\":\\s*([0-9]+),\\s*\"errorPer1k\":\\s*([0-9.eE+-]+)",
                RegexOptions.CultureInvariant)
            .Cast<Match>()
            .Select(match => (
                Threshold: double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                Answers: int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                Coverage: double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture),
                Wrong: int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture),
                Error: double.Parse(match.Groups[5].Value, CultureInfo.InvariantCulture)))
            .ToList();
        var judged = (int)NumberMember(json, "judged");

        // Act: collect what differs
        var problems = new List<string>();
        Check(problems, "rule", "nv-technical-dev-v1", StringMember(json, "rule"));
        Check(problems, "weights", DataFiles.Sha256Hex(DataFiles.Repository(WeightsPath)), StringMember(json, "weightsSha256"));
        Check(problems, "temperature", One(NumberMember(config, "temperature")), One(NumberMember(json, "temperature")));
        Check(problems, "target", "10", Whole(NumberMember(json, "targetErrorsPer1000")));
        Check(problems, "coverage floor", "25", Whole(NumberMember(json, "coverageFloorPercent")));
        foreach (var (member, manifestPath) in new[]
                 {
                     ("deverbalHeadsSha256", "eval/shapes-dev/deverbal-heads.tsv"),
                     ("definedTermsSha256", "eval/shapes-dev/defined-terms.tsv"),
                     ("stepLinesSha256", "eval/shapes-dev/step-lines.tsv"),
                     ("clauseVerbsSha256", "eval/shapes-dev/clause-verbs.tsv")
                 })
        {
            Check(problems, member, manifest[manifestPath], StringMember(json, member));
        }

        Check(problems, "grid", "0.45 0.50 0.55 0.60 0.65 0.70 0.75 0.80 0.85 0.90 0.95 0.96 0.97 0.98 0.99",
            string.Join(" ", curve.Select(row => row.Threshold.ToString("F2", CultureInfo.InvariantCulture))));
        var firstQualifying = curve.FindIndex(row => row.Wrong * 1000 <= 10 * row.Answers && row.Answers * 100 >= 25 * judged);
        if (firstQualifying < 0)
        {
            Check(problems, "status", "none", StringMember(json, "status"));
            if (report.IndexOf("**Technical point: none.**", StringComparison.Ordinal) < 0)
            {
                problems.Add("the section of the report does not say that there is no technical point");
            }
        }
        else
        {
            var threshold = curve[firstQualifying].Threshold.ToString("F2", CultureInfo.InvariantCulture);
            Check(problems, "status", "found", StringMember(json, "status"));
            Check(problems, "point", threshold,
                NumberMember(json, "threshold", json.IndexOf("\"point\"", StringComparison.Ordinal)).ToString("F2", CultureInfo.InvariantCulture));
            Check(problems, "section point", "**Technical point: " + threshold + "**",
                Regex.Match(report, @"\*\*Technical point: [0-9.]+\*\*").Value);
            var point = curve[firstQualifying];
            problems.AddRange(Missing("user guide", Collapse(ReadText(UserGuidePath)), new[]
            {
                $"**{threshold}**", $"{point.Wrong} wrong answer", $"in {point.Answers} ({One(point.Error)} in 1,000", $"coverage of {One(point.Coverage)}%",
                "not a recommended gate"
            }));
            problems.AddRange(Missing("namespace reference", DocumentationText(NamespaceDocPath), new[]
            {
                "not a recommended gate"
            }));
            problems.AddRange(Missing("selection report", report, new[] { "not as a recommended gate" }));
        }

        var section = Between(report, "## Technical text (rule `nv-technical-dev-v1`", "\n- The figures are development figures");
        foreach (var row in curve)
        {
            var prefix = $"| {row.Threshold.ToString("F2", CultureInfo.InvariantCulture)} | {row.Answers} | {One(row.Coverage)}% | {row.Wrong} | {One(row.Error)} | ";
            if (section.IndexOf("\n" + prefix, StringComparison.Ordinal) < 0)
            {
                problems.Add("the section of the report has no row that starts with: " + prefix);
            }
        }

        // Assert: nothing differs, the grid has 15 rows, and the report states the judged words of the artifact
        Assert.Empty(problems);
        Assert.Equal(15, curve.Count);
        Assert.Contains($"Judged words: {judged} (", section, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that the evaluation record contains no text of the external treebank (share-alike licensed, local use
    ///     only): no sentence id in the form of its identifiers, and, when the local cache of the pinned treebank is
    ///     present, none of its sentence ids and none of its sentences (of 25 or more characters).
    /// </summary>
    [Fact]
    public void PartOfSpeech_Evaluation_Record_ContainsNoTreebankText()
    {
        // Arrange: every file of the evaluation record, and the sentences of the local treebank cache when it exists
        var files = DataFiles.EnumerateFiles(DataFiles.Repository("docs/evaluation"), _ => false).ToList();
        var texts = files.ToDictionary(DataFiles.RelativePath, file => File.ReadAllText(file));
        var cache = DataFiles.Repository("tools/data/cache/ud-ewt/en_ewt-ud-test.conllu");
        var treebank = File.Exists(cache) ? TreebankFragments(cache) : new List<string>();

        // Act: find the identifiers in the form of the treebank and the cached ids and sentences
        var problems = new List<string>();
        foreach (var entry in texts)
        {
            var path = entry.Key;
            var text = entry.Value;
            problems.AddRange(TreebankSentenceId.Matches(text).Cast<Match>().Select(match => $"{path}: looks like a treebank sentence id: {match.Value}"));
            problems.AddRange(treebank
                .Where(fragment => text.IndexOf(fragment, StringComparison.Ordinal) >= 0)
                .Take(3)
                .Select(_ => $"{path}: contains text of the treebank cache"));
        }

        // Assert: the record was found, and none of it is treebank text
        Assert.Equal(RecordFiles.Length, files.Count);
        Assert.Empty(problems);
    }

    /// <summary>
    ///     Reads a text file of the repository with LF line endings.
    /// </summary>
    private static string ReadText(string relativePath) =>
        File.ReadAllText(DataFiles.Repository(relativePath)).Replace("\r\n", "\n");

    /// <summary>
    ///     Collapses every run of white space to one space.
    /// </summary>
    private static string Collapse(string text) => Regex.Replace(text, @"\s+", " ").Trim();

    /// <summary>
    ///     Reads the text of the documentation comments of a C# file: the comment lines without their slashes, collapsed.
    /// </summary>
    private static string DocumentationText(string relativePath) =>
        Collapse(string.Join(" ", ReadText(relativePath).Split('\n').Select(line => line.TrimStart().TrimStart('/'))));

    /// <summary>
    ///     Reads the checksum manifest of the data folder: the SHA-256 of every file by its path below <c>data</c>.
    /// </summary>
    private static Dictionary<string, string> ReadManifest() =>
        File.ReadLines(DataFiles.Data("MANIFEST.tsv"))
            .Where(line => line.Length > 0 && !DataFiles.IsComment(line))
            .Select(line => line.Split('\t'))
            .ToDictionary(cells => cells[0], cells => cells[2], StringComparer.Ordinal);

    /// <summary>
    ///     Lists the ids and the sentences (of 25 or more characters) of a treebank file.
    /// </summary>
    private static List<string> TreebankFragments(string path)
    {
        var fragments = new List<string>();
        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith("# sent_id = ", StringComparison.Ordinal))
            {
                fragments.Add(line.Substring("# sent_id = ".Length));
            }
            else if (line.StartsWith("# text = ", StringComparison.Ordinal) && line.Length >= "# text = ".Length + 25)
            {
                fragments.Add(line.Substring("# text = ".Length));
            }
        }

        return fragments;
    }

    /// <summary>
    ///     Gets the text value of a member of a canonical JSON file, searching from an index.
    /// </summary>
    private static string StringMember(string json, string name, int from = 0)
    {
        var match = Regex.Match(json.Substring(from), "\"" + name + "\"\\s*:\\s*\"([^\"]*)\"");
        Assert.True(match.Success, "The JSON file has no text member " + name);
        return match.Groups[1].Value;
    }

    /// <summary>
    ///     Gets the number value of a member of a canonical JSON file, searching from an index.
    /// </summary>
    private static double NumberMember(string json, string name, int from = 0)
    {
        var match = Regex.Match(json.Substring(from), "\"" + name + "\"\\s*:\\s*(-?[0-9][0-9.eE+-]*)");
        Assert.True(match.Success, "The JSON file has no number member " + name);
        return double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    /// <summary>
    ///     Finds the index of an object of a canonical JSON file by the value of its name member.
    /// </summary>
    private static int NamedObject(string json, string name, int from, string member = "name")
    {
        var index = json.IndexOf($"\"{member}\": \"{name}\"", from, StringComparison.Ordinal);
        Assert.True(index >= 0, $"The JSON file has no object with {member} {name}");
        return index;
    }

    /// <summary>
    ///     Gets the figures of one operating point of one set of the results.
    /// </summary>
    private static ResultFigures ResultPoint(string results, string set, string point)
    {
        var setStart = NamedObject(results, set, Math.Max(0, results.IndexOf("\"sets\"", StringComparison.Ordinal)));
        var start = NamedObject(results, point, setStart);
        return new ResultFigures(
            NumberMember(results, "coveragePercent", start),
            NumberMember(results, "harmfulPer1000Answered", start),
            (int)NumberMember(results, "answered", start),
            (int)NumberMember(results, "harmful", start));
    }

    /// <summary>
    ///     Formats a figure with one decimal place as the documents do.
    /// </summary>
    private static string One(double value) => value.ToString("F1", CultureInfo.InvariantCulture);

    /// <summary>
    ///     Formats a figure without decimal places.
    /// </summary>
    private static string Whole(double value) => value.ToString("F0", CultureInfo.InvariantCulture);

    /// <summary>
    ///     Reads the rows of a Markdown table: the cells of each row below the header that starts with a prefix, until
    ///     the first line that is not a table row.
    /// </summary>
    private static List<string[]> ReadTable(string markdown, string headerPrefix)
    {
        var lines = markdown.Split('\n').ToList();
        var header = lines.FindIndex(line => line.StartsWith(headerPrefix, StringComparison.Ordinal));
        Assert.True(header >= 0, "The README has no table with the header " + headerPrefix);
        return lines
            .Skip(header + 2)
            .TakeWhile(line => line.StartsWith("|", StringComparison.Ordinal))
            .Select(line => line.Trim().Trim('|').Split('|').Select(cell => cell.Trim()).ToArray())
            .ToList();
    }

    /// <summary>
    ///     Lists the cells of a table row that differ from the expected ones.
    /// </summary>
    private static IEnumerable<string> CompareRow(string[] row, string[] expected)
    {
        if (row.Length != expected.Length)
        {
            return new[] { $"row {row[0]}: expected {expected.Length} cells, found {row.Length}" };
        }

        return row
            .Select((cell, index) => (cell, index))
            .Where(item => !string.Equals(item.cell, expected[item.index], StringComparison.Ordinal))
            .Select(item => $"row {row[0]}, column {item.index + 1}: expected {expected[item.index]}, found {item.cell}");
    }

    /// <summary>
    ///     Gets the part of a text from one marker up to the next marker after it.
    /// </summary>
    private static string Between(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, "The text has no " + start);
        var to = text.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, "The text has no " + end + " after " + start);
        return text.Substring(from, to - from);
    }

    /// <summary>
    ///     Lists the fragments that a text lacks.
    /// </summary>
    private static IEnumerable<string> Missing(string label, string text, IEnumerable<string> fragments) =>
        fragments.Where(fragment => text.IndexOf(fragment, StringComparison.Ordinal) < 0).Select(fragment => $"{label}: lacks '{fragment}'");

    /// <summary>
    ///     Adds a problem when two values differ.
    /// </summary>
    private static void Check(List<string> problems, string what, string expected, string actual)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            problems.Add($"{what}: expected {expected}, found {actual}");
        }
    }

    /// <summary>
    ///     The figures of one operating point of one set of the results.
    /// </summary>
    private sealed class ResultFigures
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="ResultFigures"/> class.
        /// </summary>
        /// <param name="coverage">The coverage in percent.</param>
        /// <param name="harmful">The harmful errors per 1,000 answered decisions.</param>
        /// <param name="answered">The number of answered decisions.</param>
        /// <param name="harmfulCount">The number of harmful errors.</param>
        public ResultFigures(double coverage, double harmful, int answered, int harmfulCount)
        {
            Coverage = coverage;
            Harmful = harmful;
            Answered = answered;
            HarmfulCount = harmfulCount;
        }

        /// <summary>
        ///     Gets the coverage in percent.
        /// </summary>
        public double Coverage { get; }

        /// <summary>
        ///     Gets the harmful errors per 1,000 answered decisions.
        /// </summary>
        public double Harmful { get; }

        /// <summary>
        ///     Gets the number of answered decisions.
        /// </summary>
        public int Answered { get; }

        /// <summary>
        ///     Gets the number of harmful errors.
        /// </summary>
        public int HarmfulCount { get; }
    }

    /// <summary>
    ///     The figures of one recommended threshold: the selection on development data and the run on the open test split.
    /// </summary>
    private sealed class GuidancePoint
    {
        /// <summary>
        ///     Reads the figures of a point from the selection and the results.
        /// </summary>
        /// <param name="selection">The text of <c>operating-points.json</c>.</param>
        /// <param name="results">The text of <c>test-results.json</c>.</param>
        /// <param name="name">The name of the point, <c>strict</c> or <c>balanced</c>.</param>
        public GuidancePoint(string selection, string results, string name)
        {
            var start = NamedObject(selection, name, 0);
            Threshold = NumberMember(selection, "threshold", start).ToString("F2", CultureInfo.InvariantCulture);
            DevCoverage = NumberMember(selection, "coveragePercent", start);
            DevHarmful = NumberMember(selection, "harmfulPer1000Answered", start);
            var test = ResultPoint(results, "open-test", name);
            TestCoverage = test.Coverage;
            TestHarmful = test.Harmful;
            var testThreshold = NumberMember(results, "threshold", NamedObject(results, name, NamedObject(results, "open-test", Math.Max(0, results.IndexOf("\"sets\"", StringComparison.Ordinal)))));
            Assert.Equal(Threshold, testThreshold.ToString("F2", CultureInfo.InvariantCulture));
        }

        /// <summary>
        ///     Gets the threshold with two decimal places.
        /// </summary>
        public string Threshold { get; }

        /// <summary>
        ///     Gets the coverage on the development data, in percent.
        /// </summary>
        public double DevCoverage { get; }

        /// <summary>
        ///     Gets the harmful errors per 1,000 answered decisions on the development data.
        /// </summary>
        public double DevHarmful { get; }

        /// <summary>
        ///     Gets the coverage on the open test split, in percent.
        /// </summary>
        public double TestCoverage { get; }

        /// <summary>
        ///     Gets the harmful errors per 1,000 answered decisions on the open test split.
        /// </summary>
        public double TestHarmful { get; }

        /// <summary>
        ///     Lists the figures that the bullet of the packed reference states.
        /// </summary>
        public string[] NamespaceFragments() => new[]
        {
            Threshold, "answered " + Percent(DevCoverage) + " of the decisions with " + One(DevHarmful) + " harmful errors in 1,000",
            "answered " + Percent(TestCoverage) + " with " + One(TestHarmful) + " in 1,000"
        };

        /// <summary>
        ///     Lists the figures that the row of the user guide states.
        /// </summary>
        public string[] TableFragments() => new[] { Threshold, Percent(TestCoverage), One(TestHarmful) };

        /// <summary>
        ///     Formats a percentage with one decimal place and a percent sign.
        /// </summary>
        private static string Percent(double value) => One(value) + "%";
    }
}
