// cspell:ignore mklink
using DemaConsulting.PartOfSpeech.Trainer;

namespace DemaConsulting.PartOfSpeech.Tests.ModelTrainer;

/// <summary>
///     Unit tests of the hard-shape development evaluation: the parser of the development set, the per-shape evaluator,
///     the Markdown report and the guard that keeps the file a development-only input. The tests use small synthetic
///     fixtures and a tiny tagger; none of them reads the committed development file.
/// </summary>
public class ShapesDevEvaluationTests
{
    /// <summary>
    ///     The header of a valid development file.
    /// </summary>
    private static readonly string[] Header =
    {
        "# corpus = fixture hard-shape development set, dev split",
        "# columns = form, upos, status",
        string.Empty,
    };

    /// <summary>
    ///     Builds the lines of one sentence block.
    /// </summary>
    private static string[] Sentence(
        string id,
        string shape,
        string role,
        int target,
        string[] words,
        string[] tags,
        string source = "doc-a",
        string origin = "real",
        string targetStatus = "unanimous",
        string lemma = "word")
    {
        var lines = new List<string>
        {
            "# sent_id = " + id,
            "# source_id = " + source,
            "# text = " + string.Join(" ", words),
        };
        if (shape.Length > 0)
        {
            lines.Add("# shape = " + shape);
        }

        lines.Add("# role = " + role);
        if (target >= 0)
        {
            lines.Add("# target = " + target);
        }

        lines.Add("# lemma = " + lemma);
        lines.Add("# origin = " + origin);
        for (var i = 0; i < words.Length; i++)
        {
            lines.Add(words[i] + "\t" + tags[i] + "\t" + (i == target ? targetStatus : "unanimous"));
        }

        return lines.ToArray();
    }

    /// <summary>
    ///     Joins header and blocks, separated by blank lines.
    /// </summary>
    private static List<string> File(params string[][] blocks)
    {
        var lines = new List<string>(Header);
        foreach (var block in blocks)
        {
            lines.AddRange(block);
            lines.Add(string.Empty);
        }

        lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    /// <summary>
    ///     A valid compound-head sentence: the judged word <c>cat</c> is a noun at index 1.
    /// </summary>
    private static string[] CompoundHead(string id = "s1", string source = "doc-a", string origin = "real") =>
        Sentence(id, "n-compound-head", "target", 1, new[] { "The", "cat", "sleeps" }, new[] { "DET", "NOUN", "VERB" }, source, origin, lemma: "cat");

    /// <summary>
    ///     Creates the temporary repository root of a reader test and writes the development file into it.
    /// </summary>
    private static string TempRoot(IEnumerable<string>? lines, string file = "dev.tsv")
    {
        var root = Path.Combine(Path.GetTempPath(), "shapes-dev-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "data", "eval", "shapes-dev"));
        if (lines != null)
        {
            System.IO.File.WriteAllText(Path.Combine(root, "data", "eval", "shapes-dev", file), string.Join("\n", lines) + "\n");
        }

        return root;
    }

    /// <summary>
    ///     Validates that a valid file is read into one item per sentence with shape, role, target, lemma, origin and gold class.
    /// </summary>
    [Fact]
    public void ShapesDevSets_Parse_ValidFile_ReturnsItems()
    {
        // Arrange: a target noun, a PROPN target and a control verb of a noun shape, one authored sentence
        var lines = File(
            CompoundHead(),
            Sentence("s2", "n-subject-initial", "target", 0, new[] { "Paris", "grows" }, new[] { "PROPN", "VERB" }, lemma: "paris"),
            Sentence("s3", "n-compound-head", "control", 2, new[] { "You", "must", "cat" }, new[] { "PRON", "AUX", "VERB" }, "shapes-dev-authored", "authored", lemma: "cat"));

        // Act
        var items = ShapesDevSets.Parse(lines, "fixture");

        // Assert
        Assert.Equal(3, items.Count);
        Assert.Equal(("s1", "doc-a", 1, "Noun", "n-compound-head", "target", "cat", "real"), (items[0].Id, items[0].SourceId, items[0].Target, items[0].GoldClass, items[0].Shape, items[0].Role, items[0].Lemma, items[0].Origin));
        Assert.Equal(new[] { "The", "cat", "sleeps" }, items[0].Tokens);
        Assert.True(items[0].IsTarget);
        Assert.Equal("Noun", items[1].GoldClass);
        Assert.False(items[2].IsTarget);
        Assert.Equal("Verb", items[2].GoldClass);
        Assert.Equal("authored", items[2].Origin);
        Assert.Equal("shapes-dev-authored", items[2].SourceId);
    }

    /// <summary>
    ///     Validates that a disputed non-target token and status flags are accepted (only the judged word must be undisputed).
    /// </summary>
    [Fact]
    public void ShapesDevSets_Parse_DisputedNonTargetAndFlags_Accepted()
    {
        // Arrange
        var block = CompoundHead();
        block[block.Length - 1] = "sleeps\tVERB\tdisputed+mentioned";
        block[block.Length - 3] = "The\tDET\tdisputed";

        // Act
        var items = ShapesDevSets.Parse(File(block), "fixture");

        // Assert
        Assert.Single(items);
    }

    /// <summary>
    ///     Validates that a sentence without the target comment is refused and named in the message.
    /// </summary>
    [Fact]
    public void ShapesDevSets_Parse_MissingTarget_Throws()
    {
        // Arrange
        var lines = File(Sentence("s9", "n-compound-head", "target", -1, new[] { "The", "cat" }, new[] { "DET", "NOUN" }));

        // Act
        var exception = Assert.Throws<InvalidDataException>(() => ShapesDevSets.Parse(lines, "fixture"));

        // Assert
        Assert.Contains("'target'", exception.Message);
        Assert.Contains("s9", exception.Message);
    }

    /// <summary>
    ///     Validates that a target of the wrong class for its shape and role is refused.
    /// </summary>
    [Theory]
    [InlineData("v-after-modal", "target", "NOUN", "must be a Verb")]
    [InlineData("n-measure", "target", "VERB", "must be a Noun")]
    [InlineData("n-measure", "control", "NOUN", "must be a Verb")]
    [InlineData("v-imperative-initial", "control", "VERB", "must be a Noun")]
    [InlineData("n-measure", "target", "ADJ", "NOUN, PROPN or VERB")]
    [InlineData("n-measure", "target", "AUX", "NOUN, PROPN or VERB")]
    public void ShapesDevSets_Parse_WrongClassForShapeAndRole_Throws(string shape, string role, string tag, string message)
    {
        // Arrange
        var lines = File(Sentence("s1", shape, role, 1, new[] { "The", "word" }, new[] { "DET", tag }));

        // Act
        var exception = Assert.Throws<InvalidDataException>(() => ShapesDevSets.Parse(lines, "fixture"));

        // Assert
        Assert.Contains(message, exception.Message);
    }

    /// <summary>
    ///     Validates that a disputed judged word is refused.
    /// </summary>
    [Fact]
    public void ShapesDevSets_Parse_DisputedTarget_Throws()
    {
        // Arrange
        var lines = File(Sentence("s1", "n-measure", "target", 1, new[] { "2", "pounds" }, new[] { "NUM", "NOUN" }, targetStatus: "disputed+mentioned"));

        // Act
        var exception = Assert.Throws<InvalidDataException>(() => ShapesDevSets.Parse(lines, "fixture"));

        // Assert
        Assert.Contains("must not be disputed", exception.Message);
    }

    /// <summary>
    ///     Validates that a target index outside the sentence, a negative one and a non-number are refused.
    /// </summary>
    [Theory]
    [InlineData("9")]
    [InlineData("2")]
    [InlineData("-1")]
    [InlineData("x")]
    public void ShapesDevSets_Parse_TargetNotATokenIndex_Throws(string target)
    {
        // Arrange
        var block = Sentence("s1", "n-measure", "target", 1, new[] { "2", "pounds" }, new[] { "NUM", "NOUN" });
        block[Array.FindIndex(block, l => l.StartsWith("# target", StringComparison.Ordinal))] = "# target = " + target;

        // Act
        var exception = Assert.Throws<InvalidDataException>(() => ShapesDevSets.Parse(File(block), "fixture"));

        // Assert
        Assert.Contains("not a token index", exception.Message);
    }

    /// <summary>
    ///     Validates that an unknown shape, role or origin and a duplicate sentence id are refused.
    /// </summary>
    [Fact]
    public void ShapesDevSets_Parse_UnknownShapeRoleOriginOrDuplicateId_Throws()
    {
        // Arrange / Act / Assert
        var words = new[] { "The", "cat" };
        var tags = new[] { "DET", "NOUN" };
        Assert.Contains("shape", Assert.Throws<InvalidDataException>(() => ShapesDevSets.Parse(File(Sentence("s1", "n-modifier", "target", 1, words, tags)), "fixture")).Message);
        Assert.Contains("role", Assert.Throws<InvalidDataException>(() => ShapesDevSets.Parse(File(Sentence("s1", "n-measure", "probe", 1, words, tags)), "fixture")).Message);
        Assert.Contains("origin", Assert.Throws<InvalidDataException>(() => ShapesDevSets.Parse(File(Sentence("s1", "n-measure", "target", 1, words, tags, origin: "found")), "fixture")).Message);
        Assert.Contains("twice", Assert.Throws<InvalidDataException>(() => ShapesDevSets.Parse(File(CompoundHead("s1"), CompoundHead("s1")), "fixture")).Message);
    }

    /// <summary>
    ///     Validates that the authored origin and the authored pseudo-document go together, in both directions.
    /// </summary>
    [Fact]
    public void ShapesDevSets_Parse_AuthoredOriginAndDocumentMismatch_Throws()
    {
        // Arrange / Act / Assert
        Assert.Throws<InvalidDataException>(() => ShapesDevSets.Parse(File(CompoundHead(origin: "authored")), "fixture"));
        Assert.Throws<InvalidDataException>(() => ShapesDevSets.Parse(File(CompoundHead(source: "shapes-dev-authored")), "fixture"));
        Assert.Single(ShapesDevSets.Parse(File(CompoundHead(source: "shapes-dev-authored", origin: "authored")), "fixture"));
    }

    /// <summary>
    ///     Validates that a malformed token row is refused by the shared corpus reader.
    /// </summary>
    [Fact]
    public void ShapesDevSets_Parse_UnknownTag_Throws()
    {
        // Arrange
        var lines = File(Sentence("s1", "n-measure", "target", 1, new[] { "2", "pounds" }, new[] { "NUM", "banana" }));

        // Act / Assert
        Assert.Throws<InvalidDataException>(() => ShapesDevSets.Parse(lines, "fixture"));
    }

    /// <summary>
    ///     Validates the five shapes and their classes.
    /// </summary>
    [Fact]
    public void ShapesDevSets_Shapes_AreTheFiveHardShapesWithTheirClasses()
    {
        // Arrange / Act / Assert
        Assert.Equal(new[] { "n-compound-head", "n-subject-initial", "v-imperative-initial", "n-measure", "v-after-modal" }, ShapesDevSets.Shapes);
        Assert.Equal(new[] { "Noun", "Noun", "Verb", "Noun", "Verb" }, ShapesDevSets.Shapes.Select(ShapesDevSets.ClassOfShape));
    }

    /// <summary>
    ///     Validates that a file whose header does not announce a development split is refused before any parsing.
    /// </summary>
    [Fact]
    public void DevSets_ReadShapesDev_HeaderWithoutDevSplit_Throws()
    {
        // Arrange
        var lines = File(CompoundHead());
        lines[0] = "# corpus = fixture hard-shape set, test split";
        var root = TempRoot(lines);
        try
        {
            // Act
            var exception = Assert.Throws<InvalidDataException>(() => DevSets.ReadShapesDev(root, out _));

            // Assert
            Assert.Contains("development split", exception.Message);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    ///     Validates that a missing file is reported as such, and that a valid file is read with its SHA-256.
    /// </summary>
    [Fact]
    public void DevSets_ReadShapesDev_MissingAndValidFile_ThrowsAndReads()
    {
        // Arrange
        var missing = TempRoot(null);
        var lines = File(CompoundHead());
        var valid = TempRoot(lines);
        try
        {
            // Act / Assert
            Assert.Throws<FileNotFoundException>(() => DevSets.ReadShapesDev(missing, out _));
            var items = DevSets.ReadShapesDev(valid, out var sha256);
            Assert.Single(items);
            Assert.Equal(ModelBuilder.Sha256Hex(System.IO.File.ReadAllBytes(Path.Combine(valid, "data", "eval", "shapes-dev", "dev.tsv"))), sha256);
        }
        finally
        {
            Directory.Delete(missing, true);
            Directory.Delete(valid, true);
        }
    }

    /// <summary>
    ///     Validates that the tranche of an item is the tranche of the file that was read, and that no comment in the file can set it.
    /// </summary>
    [Fact]
    public void ShapesDevSets_Parse_Tranche_IsGivenByTheCallerAndNotByTheContent()
    {
        // Arrange: a block that claims to be of the hard tranche in a comment
        var lines = File(CompoundHead("s1"));
        var claimed = File(Sentence("s2", "n-compound-head", "target", 1, new[] { "The", "cat", "sleeps" }, new[] { "DET", "NOUN", "VERB" }, lemma: "cat"));
        claimed.Insert(claimed.Count - 3, "# tranche = hard");

        // Act
        var byDefault = ShapesDevSets.Parse(lines, "fixture");
        var hard = ShapesDevSets.Parse(lines, "fixture", ShapesDevTranche.Hard);
        var comment = ShapesDevSets.Parse(claimed, "fixture");

        // Assert
        Assert.Equal(ShapesDevTranche.Standard, byDefault[0].Tranche);
        Assert.Equal(ShapesDevTranche.Hard, hard[0].Tranche);
        Assert.Equal(ShapesDevTranche.Standard, comment[0].Tranche);
    }

    /// <summary>
    ///     Validates that each tranche is read from its own file, with its own SHA-256, and marks its items.
    /// </summary>
    [Fact]
    public void DevSets_ReadShapesDev_EachTranche_ReadsItsOwnFileAndMarksItems()
    {
        // Arrange: a fake repository with a different number of sentences in the two files
        var standardLines = File(CompoundHead("s1"), CompoundHead("s2"));
        var hardLines = File(CompoundHead("h1"));
        var root = TempRoot(standardLines);
        System.IO.File.WriteAllText(Path.Combine(root, "data", "eval", "shapes-dev", "hard.tsv"), string.Join("\n", hardLines) + "\n");
        try
        {
            // Act
            var standard = DevSets.ReadShapesDev(root, ShapesDevTranche.Standard, out var standardSha);
            var hard = DevSets.ReadShapesDev(root, ShapesDevTranche.Hard, out var hardSha);
            var unchanged = DevSets.ReadShapesDev(root, out var defaultSha);

            // Assert
            Assert.Equal(new[] { "s1", "s2" }, standard.Select(i => i.Id));
            Assert.All(standard, i => Assert.Equal(ShapesDevTranche.Standard, i.Tranche));
            Assert.Equal(new[] { "h1" }, hard.Select(i => i.Id));
            Assert.All(hard, i => Assert.Equal(ShapesDevTranche.Hard, i.Tranche));
            Assert.NotEqual(standardSha, hardSha);
            Assert.Equal(ModelBuilder.Sha256Hex(System.IO.File.ReadAllBytes(Path.Combine(root, "data", "eval", "shapes-dev", "hard.tsv"))), hardSha);
            Assert.Equal(standardSha, defaultSha);
            Assert.Equal(2, unchanged.Count);
            Assert.True(DevSets.ShapesDevExists(root, ShapesDevTranche.Hard));
            Assert.Equal("data/eval/shapes-dev/hard.tsv", DevSets.ShapesDevFileOf(ShapesDevTranche.Hard));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    ///     Validates that a missing hard file is reported as such (and known to be missing) while the standard file is still read, and that
    ///     a hard file whose header does not announce a development split is refused.
    /// </summary>
    [Fact]
    public void DevSets_ReadShapesDev_HardMissingOrWrongHeader_ThrowsAndStandardStillReads()
    {
        // Arrange
        var root = TempRoot(File(CompoundHead()));
        try
        {
            // Act / Assert: missing hard file
            Assert.False(DevSets.ShapesDevExists(root, ShapesDevTranche.Hard));
            Assert.Throws<FileNotFoundException>(() => DevSets.ReadShapesDev(root, ShapesDevTranche.Hard, out _));
            Assert.Single(DevSets.ReadShapesDev(root, ShapesDevTranche.Standard, out _));

            // Assert: a hard file with a held-out looking header
            var wrong = File(CompoundHead());
            wrong[0] = "# corpus = fixture hard-shape set, test split";
            System.IO.File.WriteAllText(Path.Combine(root, "data", "eval", "shapes-dev", "hard.tsv"), string.Join("\n", wrong) + "\n");
            Assert.Contains("development split", Assert.Throws<InvalidDataException>(() => DevSets.ReadShapesDev(root, ShapesDevTranche.Hard, out _)).Message);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    ///     Validates that the evaluator summarizes per tranche and combined, gives the hard tranche zero counts when it has no items, and
    ///     keeps the plain summary of all items unchanged.
    /// </summary>
    [Fact]
    public void ShapesDevEvaluator_SummarizeTranches_PerTrancheAndCombined()
    {
        // Arrange
        var items = new List<ShapesDevItem>
        {
            Item("a", "n-compound-head", "target", "cat", "Noun"),
            Item("b", "n-compound-head", "control", "cat", "Verb"),
            Item("c", "n-compound-head", "target", "cat", "Noun", tranche: ShapesDevTranche.Hard),
            Item("d", "v-after-modal", "target", "dog", "Verb", tranche: ShapesDevTranche.Hard),
        };
        var outcomes = new List<Outcome>
        {
            new("d", true, 0.9, 0.9, true, true, false),
            new("d", true, 0.9, 0.9, true, true, false),
            new("d", false, 0.9, 0.1, true, true, true),
            new("d", true, 0.9, 0.9, true, true, false),
        };

        // Act
        var results = ShapesDevEvaluator.SummarizeTranches(items, outcomes);
        var standardOnly = ShapesDevEvaluator.SummarizeTranches(items.Take(2).ToList(), outcomes.Take(2).ToList());

        // Assert
        Assert.Equal((1, 1, 0, 1, 1), (results.Standard.Pooled.TargetItems, results.Standard.Pooled.TargetCorrect, results.Standard.Pooled.HarmfulErrors, results.Standard.Pooled.ControlItems, results.Standard.Pooled.ControlCorrect));
        Assert.Equal((2, 1, 1, 0), (results.Hard.Pooled.TargetItems, results.Hard.Pooled.TargetCorrect, results.Hard.Pooled.HarmfulErrors, results.Hard.Pooled.ControlItems));
        Assert.Equal(1, results.Hard.Shapes[0].TargetItems);
        Assert.Equal(0, results.Hard.Shapes[3].TargetItems);
        Assert.Equal((3, 2, 1, 1), (results.Combined.Pooled.TargetItems, results.Combined.Pooled.TargetCorrect, results.Combined.Pooled.HarmfulErrors, results.Combined.Pooled.ControlItems));
        Assert.Equal(results.Combined.Pooled.TargetItems, results.Standard.Pooled.TargetItems + results.Hard.Pooled.TargetItems);
        Assert.Equal(0, standardOnly.Hard.Pooled.TargetItems);
        Assert.Equal(standardOnly.Standard.Pooled.TargetItems, standardOnly.Combined.Pooled.TargetItems);
        Assert.Throws<ArgumentException>(() => ShapesDevEvaluator.SummarizeTranches(items, outcomes.Take(3).ToList()));
        Assert.Equal(3, ShapesDevEvaluator.Summarize(items, outcomes).Pooled.TargetItems);
    }

    /// <summary>
    ///     Validates that a tiny tagger is scored once per item and per tranche through the real decoder.
    /// </summary>
    [Fact]
    public void ShapesDevEvaluator_EvaluateTranches_TinyTagger_ScoresEachTrancheAndCombined()
    {
        // Arrange: "cat" is called a noun and "dog" a verb, both words possible as either
        var weights = new TinyModels.WeightsBuilder().Word("cat", Upos.Noun, 40).Word("dog", Upos.Verb, 40).Build(temperature: 1f, scale: 0.1f);
        var tagger = TinyModels.CreateTagger(weights, TinyModels.CreateLexicon("cat NOUN VERB", "dog NOUN VERB"), new TaggerOptions { MaxAlternatives = 16 });
        var items = new List<ShapesDevItem>
        {
            Item("a", "n-compound-head", "target", "cat", "Noun"),
            Item("b", "n-compound-head", "target", "dog", "Noun", tranche: ShapesDevTranche.Hard),
            Item("c", "v-after-modal", "target", "dog", "Verb", tranche: ShapesDevTranche.Hard),
        };

        // Act
        var results = ShapesDevEvaluator.EvaluateTranches(tagger, items);

        // Assert
        Assert.Equal((1, 1, 0), (results.Standard.Pooled.TargetItems, results.Standard.Pooled.TargetCorrect, results.Standard.Pooled.HarmfulErrors));
        Assert.Equal((2, 1, 1), (results.Hard.Pooled.TargetItems, results.Hard.Pooled.TargetCorrect, results.Hard.Pooled.HarmfulErrors));
        Assert.Equal((3, 2, 1), (results.Combined.Pooled.TargetItems, results.Combined.Pooled.TargetCorrect, results.Combined.Pooled.HarmfulErrors));
        Assert.Equal(ShapesDevEvaluator.Evaluate(tagger, items).Pooled.TargetCorrect, results.Combined.Pooled.TargetCorrect);
    }

    /// <summary>
    ///     Validates the three sections of the tranche report: the standard section is exactly what the plain report writes, the hard
    ///     section and the combined table follow, an empty shape shows dashes, and two renderings give the same bytes.
    /// </summary>
    [Fact]
    public void ShapesDevReport_RenderTranches_StandardUnchangedThenHardThenCombined()
    {
        // Arrange
        var items = new List<ShapesDevItem>
        {
            Item("a", "n-measure", "target", "pound", "Noun"),
            Item("b", "n-measure", "control", "pound", "Verb"),
            Item("c", "v-after-modal", "target", "dog", "Verb", tranche: ShapesDevTranche.Hard),
            Item("d", "v-after-modal", "control", "dog", "Noun", tranche: ShapesDevTranche.Hard),
        };
        var outcomes = new List<Outcome>
        {
            new("d", true, 0.9, 0.9, true, true, false),
            new("d", true, 0.9, 0.9, true, true, false),
            new("d", true, 0.9, 0.9, true, true, false),
            new("d", false, 0.9, 0.1, true, true, true),
        };
        var results = ShapesDevEvaluator.SummarizeTranches(items, outcomes);
        var (standardSha, hardSha, weightsSha) = (new string('a', 64), new string('c', 64), new string('b', 64));

        // Act
        var report = ShapesDevReport.RenderTranches("model.bin", standardSha, hardSha, results, weightsSha);
        var again = ShapesDevReport.RenderTranches("model.bin", standardSha, hardSha, ShapesDevEvaluator.SummarizeTranches(items, outcomes), weightsSha);
        var plain = ShapesDevReport.Render("model.bin", standardSha, results.Standard, weightsSha);
        var hardOnly = ShapesDevReport.RenderHard("model.bin", hardSha, results.Hard, weightsSha);

        // Assert: deterministic, the standard section is the plain report byte for byte, then the two other sections
        Assert.Equal(report, again);
        Assert.Equal(plain, report.Take(plain.Count));
        Assert.Equal(string.Empty, report[plain.Count]);
        var headings = report.Where(l => l.StartsWith("## ", StringComparison.Ordinal)).ToList();
        Assert.Equal(
            new[]
            {
                "## Shapes development set: model.bin",
                "## Shapes development set, hard tranche: model.bin",
                "## Shapes development set, standard and hard tranche combined: model.bin",
            },
            headings);
        Assert.Contains($"- Development file SHA-256: `{hardSha}`.", report);
        Assert.Contains($"- Development files SHA-256: standard `{standardSha}`, hard `{hardSha}`.", report);
        Assert.Equal(3, report.Count(l => l.StartsWith("| all shapes", StringComparison.Ordinal)));
        Assert.Equal(1, report.Count(l => l.StartsWith("| n-measure | 0 | - | - | 0 | 0 | - | 0 | - |", StringComparison.Ordinal)));          // the hard tranche has no n-measure items
        Assert.Equal(1, report.Count(l => l.StartsWith("| v-after-modal | 0 | - |", StringComparison.Ordinal)));                            // the standard tranche has no modal items
        Assert.Contains("| v-after-modal | 1 | 100.0% | [20.7%, 100.0%] | 1 | 0 | 0.0 | 1 | 0.0% |", report);
        Assert.Contains(report, l => l.StartsWith("| all shapes | 2 | 100.0% |", StringComparison.Ordinal));
        Assert.Equal("## Shapes development set, hard tranche: model.bin", hardOnly[0]);
        Assert.Equal(hardOnly, report.Skip(plain.Count + 1).Take(hardOnly.Count));
    }

    /// <summary>
    ///     Validates that the plain report (the standard section) has no tranche in its heading and keeps its dashes for an empty set.
    /// </summary>
    [Fact]
    public void ShapesDevReport_Render_StandardSection_HasNoTrancheNameInItsHeading()
    {
        // Arrange
        var results = ShapesDevEvaluator.SummarizeTranches(new List<ShapesDevItem>(), new List<Outcome>());

        // Act
        var plain = ShapesDevReport.Render("m", string.Empty, results.Standard);

        // Assert: the plain report has no tranche in its heading, whatever the other sections say
        Assert.Equal("## Shapes development set: m", plain[0]);
        Assert.DoesNotContain(plain, l => l.Contains("tranche", StringComparison.Ordinal));
        Assert.Contains("| all shapes | 0 | - | - | 0 | 0 | - | 0 | - |", plain);
    }

    /// <summary>
    ///     Validates that the evaluator reports the accuracy, the harmful errors and the control accuracy of a tiny tagger by shape and pooled.
    /// </summary>
    [Fact]
    public void ShapesDevEvaluator_Evaluate_TinyTagger_ReportsPerShapeCounts()
    {
        // Arrange: a tagger that calls "cat" a noun and "dog" a verb, both words possible as either
        var weights = new TinyModels.WeightsBuilder().Word("cat", Upos.Noun, 40).Word("dog", Upos.Verb, 40).Build(temperature: 1f, scale: 0.1f);
        var tagger = TinyModels.CreateTagger(weights, TinyModels.CreateLexicon("cat NOUN VERB", "dog NOUN VERB"), new TaggerOptions { MaxAlternatives = 16 });
        var items = new List<ShapesDevItem>
        {
            Item("a", "n-compound-head", "target", "cat", "Noun"),
            Item("b", "n-compound-head", "target", "dog", "Noun"),
            Item("c", "n-compound-head", "control", "dog", "Verb"),
            Item("d", "v-after-modal", "target", "dog", "Verb", "authored"),
            Item("e", "v-after-modal", "control", "cat", "Verb"),
        };

        // Act
        var result = ShapesDevEvaluator.Evaluate(tagger, items);

        // Assert: compound head: 1 of 2 targets correct, one harmful error, 2 decisions; its control is right
        var head = result.Shapes[0];
        Assert.Equal(("n-compound-head", 2, 1, 2, 1, 1, 1), (head.Shape, head.TargetItems, head.TargetCorrect, head.NounVerbDecisions, head.HarmfulErrors, head.ControlItems, head.ControlCorrect));
        Assert.Equal(50.0, head.TargetAccuracy);
        Assert.Equal(500.0, head.HarmfulPer1000);
        Assert.Equal(100.0, head.ControlAccuracy);
        var modal = result.Shapes[4];
        Assert.Equal((1, 1, 0, 1, 0, 1, 0), (modal.TargetItems, modal.TargetCorrect, modal.HarmfulErrors, modal.AuthoredTargets, modal.AuthoredControls, modal.ControlItems, modal.ControlCorrect));
        Assert.Equal(0.0, modal.ControlAccuracy);
        Assert.Equal((3, 2, 1, 2, 1), (result.Pooled.TargetItems, result.Pooled.TargetCorrect, result.Pooled.HarmfulErrors, result.Pooled.ControlItems, result.Pooled.ControlCorrect));
    }

    /// <summary>
    ///     Validates that the summary groups by shape and role, gives a shape without items zero counts and refuses inconsistent input.
    /// </summary>
    [Fact]
    public void ShapesDevEvaluator_Summarize_GroupsByShapeAndRoleAndRefusesBadInput()
    {
        // Arrange
        var items = new List<ShapesDevItem>
        {
            Item("a", "n-measure", "target", "pound", "Noun"),
            Item("b", "n-measure", "target", "cup", "Noun", "authored"),
            Item("c", "n-measure", "control", "pound", "Verb"),
        };
        var outcomes = new List<Outcome>
        {
            new("d", true, 0.9, 0.9, true, true, false),
            new("d", false, 0.8, 0.2, true, true, true),
            new("d", false, 0.6, 0.4, true, true, true),
        };

        // Act
        var result = ShapesDevEvaluator.Summarize(items, outcomes);

        // Assert: the harmful error of the control does not count as a target decision
        var measure = result.Shapes[3];
        Assert.Equal((2, 1, 2, 1, 1, 0, 1), (measure.TargetItems, measure.TargetCorrect, measure.NounVerbDecisions, measure.HarmfulErrors, measure.ControlItems, measure.ControlCorrect, measure.AuthoredTargets));
        Assert.Equal(0, result.Shapes[0].TargetItems);
        Assert.Equal(0.0, result.Shapes[0].TargetAccuracy);
        Assert.Equal(0.0, result.Shapes[0].HarmfulPer1000);
        Assert.Equal(0.0, result.Shapes[0].ControlAccuracy);
        Assert.Equal(5, result.Shapes.Count);
        Assert.Throws<ArgumentException>(() => ShapesDevEvaluator.Summarize(items, outcomes.Take(2).ToList()));
        Assert.Throws<ArgumentException>(() => ShapesDevEvaluator.Summarize(new[] { Item("x", "n-modifier", "target", "a", "Noun") }, new[] { outcomes[0] }));
    }

    /// <summary>
    ///     Validates that the report is deterministic and carries the table, the empty-shape dashes and the dataset hash.
    /// </summary>
    [Fact]
    public void ShapesDevReport_Render_SameResult_IsDeterministicMarkdown()
    {
        // Arrange
        var items = new List<ShapesDevItem> { Item("a", "n-measure", "target", "pound", "Noun"), Item("c", "n-measure", "control", "pound", "Verb") };
        var outcomes = new List<Outcome> { new("d", true, 0.9, 0.9, true, true, false), new("d", true, 0.9, 0.9, true, true, false) };
        var result = ShapesDevEvaluator.Summarize(items, outcomes);

        // Act
        var first = ShapesDevReport.Render("model.bin", new string('a', 64), result, new string('b', 64));
        var second = ShapesDevReport.Render("model.bin", new string('a', 64), ShapesDevEvaluator.Summarize(items, outcomes), new string('b', 64));

        // Assert
        Assert.Equal(first, second);
        Assert.Equal("## Shapes development set: model.bin", first[0]);
        Assert.Contains(first, l => l.Contains(new string('a', 64)));
        Assert.Contains(first, l => l.Contains(new string('b', 64)));
        Assert.Contains("| n-compound-head | 0 | - | - | 0 | 0 | - | 0 | - |", first);
        Assert.Contains("| n-measure | 1 | 100.0% | [20.7%, 100.0%] | 1 | 0 | 0.0 | 1 | 100.0% |", first);
        Assert.Contains(first, l => l.StartsWith("| all shapes | 1 | 100.0% |", StringComparison.Ordinal));
        Assert.Equal(first.Count(l => l.StartsWith("| ", StringComparison.Ordinal)), ShapesDevSets.Shapes.Count + 3);
    }

    /// <summary>
    ///     Validates the Wilson interval against hand-computed values.
    /// </summary>
    [Fact]
    public void ShapesDevReport_Interval_KnownCounts_MatchesWilsonScore()
    {
        // Arrange / Act / Assert
        Assert.Equal("[49.0%, 94.3%]", ShapesDevReport.Interval(8, 10));
        Assert.Equal("[0.0%, 27.8%]", ShapesDevReport.Interval(0, 10));
        Assert.Equal("[72.2%, 100.0%]", ShapesDevReport.Interval(10, 10));
    }

    /// <summary>
    ///     Validates the guard theories of the files of the hard-shape development set: only its own guard admits them, and the two-file allow-list is unchanged.
    /// </summary>
    [Theory]
    [InlineData("data/eval/shapes-dev/dev.tsv")]
    [InlineData("data/eval/shapes-dev/../shapes-dev/dev.tsv")]
    [InlineData("data/eval/shapes-dev/hard.tsv")]
    [InlineData("data/eval/shapes-dev/../shapes-dev/hard.tsv")]
    public void DataGuard_EnsureShapesDevelopmentFile_ShapesDevelopmentFile_Admitted(string relativePath)
    {
        // Arrange / Act
        var resolved = DataGuard.EnsureShapesDevelopmentFile(DataFiles.RepositoryRoot, DataFiles.Repository(relativePath));

        // Assert
        Assert.EndsWith(Path.Combine("data", "eval", "shapes-dev", relativePath.EndsWith("hard.tsv", StringComparison.Ordinal) ? "hard.tsv" : "dev.tsv"), resolved, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Validates that the shapes development guard refuses every other file, the two development files and the held-out files included.
    /// </summary>
    [Theory]
    [InlineData("data/open/dev.tsv")]
    [InlineData("data/eval/gold-v2/dev.tsv")]
    [InlineData("data/open/test.tsv")]
    [InlineData("data/eval/shapes/probe.tsv")]
    [InlineData("data/eval/shapes-dev/sources.tsv")]
    [InlineData("data/eval/shapes-dev/hard-sources.tsv")]
    [InlineData("data/eval/shapes-dev/hard-leakage-report.tsv")]
    [InlineData("data/eval/shapes-dev/hard.tsv.bak")]
    [InlineData("data/eval/shapes-dev/README.md")]
    public void DataGuard_EnsureShapesDevelopmentFile_OtherFile_Refused(string relativePath)
    {
        // Arrange / Act / Assert
        Assert.Throws<InvalidOperationException>(() => DataGuard.EnsureShapesDevelopmentFile(DataFiles.RepositoryRoot, DataFiles.Repository(relativePath)));
    }

    /// <summary>
    ///     Validates that a symbolic link or junction replacing a file of the shapes development set (either tranche), or the folder that holds it, is
    ///     refused, while the real file in the same fake repository is admitted.
    /// </summary>
    /// <param name="allowedFile">The allowed file, relative to the repository root with forward slashes.</param>
    [Theory]
    [InlineData("data/eval/shapes-dev/dev.tsv")]
    [InlineData("data/eval/shapes-dev/hard.tsv")]
    public void DataGuard_EnsureShapesDevelopmentFile_LinkInPlaceOfFileOrFolder_Refused(string allowedFile)
    {
#if NET6_0_OR_GREATER
        // Arrange: two fake repositories, one with a link as the file and one with a link as the folder
        var temp = Path.Combine(Path.GetTempPath(), "pos-shapes-dev-" + Guid.NewGuid().ToString("N"));
        var fileRepo = Path.Combine(temp, "file-repo");
        var folderRepo = Path.Combine(temp, "folder-repo");
        var realRepo = Path.Combine(temp, "real-repo");
        var relative = allowedFile.Replace('/', Path.DirectorySeparatorChar);
        try
        {
            var other = Path.Combine(temp, "other");
            Directory.CreateDirectory(other);
            System.IO.File.WriteAllText(Path.Combine(other, "dev.tsv"), "# fixture");
            foreach (var repo in new[] { fileRepo, realRepo })
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(repo, relative))!);
            }

            System.IO.File.WriteAllText(Path.Combine(realRepo, relative), "# fixture");
            Directory.CreateDirectory(Path.Combine(folderRepo, "data", "eval"));
            var fileLinked = TryCreateFileLink(Path.Combine(fileRepo, relative), Path.Combine(other, "dev.tsv"));
            var folderLinked = TryCreateDirectoryLink(Path.Combine(folderRepo, "data", "eval", "shapes-dev"), other);
            if (!fileLinked && !folderLinked)
            {
                Assert.Skip("Neither symbolic links nor junctions can be created here.");
            }

            // Act / Assert: the written path is the allowed one, but a link lies on its way
            if (fileLinked)
            {
                Assert.Throws<InvalidOperationException>(() => DataGuard.EnsureShapesDevelopmentFile(fileRepo, Path.Combine(fileRepo, relative)));
            }

            if (folderLinked)
            {
                Assert.Throws<InvalidOperationException>(() => DataGuard.EnsureShapesDevelopmentFile(folderRepo, Path.Combine(folderRepo, relative)));
            }

            Assert.Equal(Path.Combine(realRepo, relative), DataGuard.EnsureShapesDevelopmentFile(realRepo, Path.Combine(realRepo, relative)));
        }
        finally
        {
            // Remove the links first: a recursive delete must not follow them into the targets
            var folderLink = Path.Combine(folderRepo, "data", "eval", "shapes-dev");
            if (Directory.Exists(folderLink))
            {
                Directory.Delete(folderLink, false);
            }

            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, true);
            }
        }
#else
        // Links are resolved only on .NET 6 and later, which is where the trainer runs
        Assert.Skip("Link resolution needs .NET 6 or later: " + allowedFile);
#endif
    }

    /// <summary>
    ///     Validates that a symbolic link or junction replacing a standard development file, the folder that holds it, or the data
    ///     folder is refused, while the real file in a fake repository is admitted.
    /// </summary>
    [Fact]
    public void DataGuard_EnsureDevelopmentFile_LinkInPlaceOfFileOrFolder_Refused()
    {
#if NET6_0_OR_GREATER
        // Arrange
        var temp = Path.Combine(Path.GetTempPath(), "pos-dev-links-" + Guid.NewGuid().ToString("N"));
        var links = new List<string>();
        try
        {
            var other = Path.Combine(temp, "other");
            Directory.CreateDirectory(other);
            System.IO.File.WriteAllText(Path.Combine(other, "dev.tsv"), "# fixture");
            var created = 0;
            var number = 0;
            foreach (var allowed in DataGuard.DevelopmentFiles)
            {
                var relative = allowed.Replace('/', Path.DirectorySeparatorChar);
                var parent = Path.GetDirectoryName(relative)!;

                // A real repository admits the file
                var realRepo = Path.Combine(temp, "real-" + number);
                Directory.CreateDirectory(Path.Combine(realRepo, parent));
                System.IO.File.WriteAllText(Path.Combine(realRepo, relative), "# fixture");
                Assert.Equal(Path.Combine(realRepo, relative), DataGuard.EnsureDevelopmentFile(realRepo, Path.Combine(realRepo, relative)));

                // A link as the file
                var fileRepo = Path.Combine(temp, "file-" + number);
                Directory.CreateDirectory(Path.Combine(fileRepo, parent));
                if (TryCreateFileLink(Path.Combine(fileRepo, relative), Path.Combine(other, "dev.tsv")))
                {
                    created++;
                    links.Add(Path.Combine(fileRepo, relative));
                    Assert.Throws<InvalidOperationException>(() => DataGuard.EnsureDevelopmentFile(fileRepo, Path.Combine(fileRepo, relative)));
                }

                // A link as the folder that holds the file
                var folderRepo = Path.Combine(temp, "folder-" + number);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(folderRepo, parent))!);
                if (TryCreateDirectoryLink(Path.Combine(folderRepo, parent), other))
                {
                    created++;
                    links.Add(Path.Combine(folderRepo, parent));
                    Assert.Throws<InvalidOperationException>(() => DataGuard.EnsureDevelopmentFile(folderRepo, Path.Combine(folderRepo, relative)));
                }

                // A link as the data folder
                var dataRepo = Path.Combine(temp, "data-" + number);
                Directory.CreateDirectory(dataRepo);
                if (TryCreateDirectoryLink(Path.Combine(dataRepo, "data"), other))
                {
                    created++;
                    links.Add(Path.Combine(dataRepo, "data"));
                    Assert.Throws<InvalidOperationException>(() => DataGuard.EnsureDevelopmentFile(dataRepo, Path.Combine(dataRepo, relative)));
                }

                number++;
            }

            if (created == 0)
            {
                Assert.Skip("Neither symbolic links nor junctions can be created here.");
            }
        }
        finally
        {
            // Remove the links first: a recursive delete must not follow them into the targets
            foreach (var link in links)
            {
                if (Directory.Exists(link))
                {
                    Directory.Delete(link, false);
                }
                else if (System.IO.File.Exists(link))
                {
                    System.IO.File.Delete(link);
                }
            }

            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, true);
            }
        }
#else
        // Links are resolved only on .NET 6 and later, which is where the trainer runs
        Assert.Skip("Link resolution needs .NET 6 or later.");
#endif
    }

#if NET6_0_OR_GREATER
    /// <summary>
    ///     Creates a symbolic link to a file when the system allows it.
    /// </summary>
    /// <param name="link">The path of the link.</param>
    /// <param name="target">The file it points to.</param>
    /// <returns><c>true</c> when the link was created.</returns>
    private static bool TryCreateFileLink(string link, string target)
    {
        try
        {
            System.IO.File.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    ///     Creates a symbolic link to a directory, or on Windows a junction (which needs no privilege) when that fails.
    /// </summary>
    /// <param name="link">The path of the link.</param>
    /// <param name="target">The directory it points to.</param>
    /// <returns><c>true</c> when the link was created.</returns>
    private static bool TryCreateDirectoryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return OperatingSystem.IsWindows() && TryCreateJunction(link, target);
        }
    }

    /// <summary>
    ///     Creates a directory junction with the command shell.
    /// </summary>
    /// <param name="link">The path of the junction.</param>
    /// <param name="target">The directory it points to.</param>
    /// <returns><c>true</c> when the junction was created.</returns>
    private static bool TryCreateJunction(string link, string target)
    {
        var start = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        start.ArgumentList.Add("/c");
        start.ArgumentList.Add("mklink");
        start.ArgumentList.Add("/J");
        start.ArgumentList.Add(link);
        start.ArgumentList.Add(target);
        using var process = System.Diagnostics.Process.Start(start);
        if (process == null)
        {
            return false;
        }

        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0;
    }
#endif

    /// <summary>
    ///     Validates that each file of the shapes development set is a development file of its own: not in the two-file allow-list, not a held-out file and not trainable.
    /// </summary>
    [Fact]
    public void DataGuard_ShapesDevelopmentFile_OnlyItsOwnGuardAdmitsIt()
    {
        // Arrange / Act / Assert: the list of the set is exactly the two tranches, the other allow-lists did not grow
        Assert.Equal(new[] { "data/eval/shapes-dev/dev.tsv", "data/eval/shapes-dev/hard.tsv" }, DataGuard.ShapesDevelopmentFiles);
        Assert.Equal(DataGuard.ShapesDevelopmentFile, DataGuard.ShapesDevelopmentFiles[(int)ShapesDevTranche.Standard]);
        Assert.Equal(DataGuard.ShapesDevelopmentHardFile, DataGuard.ShapesDevelopmentFiles[(int)ShapesDevTranche.Hard]);
        Assert.Equal(new[] { "data/open/dev.tsv", "data/eval/gold-v2/dev.tsv" }, DataGuard.DevelopmentFiles);
        Assert.Equal(4, DataGuard.HeldOutFiles.Count);
        foreach (var file in DataGuard.ShapesDevelopmentFiles)
        {
            var path = DataFiles.Repository(file);
            Assert.DoesNotContain(file, DataGuard.DevelopmentFiles);
            Assert.DoesNotContain(file, DataGuard.HeldOutFiles);
            Assert.Throws<InvalidOperationException>(() => DataGuard.EnsureDevelopmentFile(DataFiles.RepositoryRoot, path));
            Assert.Throws<InvalidOperationException>(() => DataGuard.EnsureTrainable(DataFiles.RepositoryRoot, path));
            Assert.Throws<InvalidOperationException>(() => CorpusReader.ReadFile(DataFiles.RepositoryRoot, path, new SourceConfig { Name = "t" }));
        }
    }

    /// <summary>
    ///     Validates that the library tokenizer reproduces the stored tokens of sentences in the corpus convention (what the
    ///     <c>shapes-tokenize</c> command writes and the development file stores).
    /// </summary>
    [Theory]
    [InlineData("Check the pressure valve.", "Check|the|pressure|valve|.")]
    [InlineData("Don't add 2 pounds of oil.", "Do|n't|add|2|pounds|of|oil|.")]
    [InlineData("Mix \"A\" and B, then stop.", "Mix|\"|A|\"|and|B|,|then|stop|.")]
    public void Tokenizer_Tokenize_ShapesSentences_ReproducesStoredTokens(string text, string tokens)
    {
        // Arrange / Act
        var sentences = Tokenizer.Tokenize(text);

        // Assert: one sentence, the stored tokens, which rejoin to the text without spaces
        Assert.Single(sentences);
        var actual = sentences[0].Tokens.Select(t => t.Text).ToArray();
        Assert.Equal(tokens.Split('|'), actual);
        Assert.Equal(text.Replace(" ", string.Empty), string.Concat(actual));
    }

    /// <summary>
    ///     Validates that the path guard of <c>shapes-tokenize</c> refuses every path inside the data folder (also through a dotted
    ///     detour and for the folder itself) and admits a path outside the repository.
    /// </summary>
    [Fact]
    public void DataGuard_EnsureOutsideData_InsideDataFolder_RefusedAndOutsideAdmitted()
    {
        // Arrange
        var root = DataFiles.RepositoryRoot;
        var dataFolder = Path.Combine(root, "data");
        var detour = Path.Combine(root, "tools", "..", "data", "x.txt");
        var temporary = Path.Combine(Path.GetTempPath(), "shapes-tokenize-guard", "in.txt");

        // Act / Assert: refused inside the data folder
        Assert.Throws<InvalidOperationException>(() => DataGuard.EnsureOutsideData(root, DataFiles.Repository(DataGuard.ShapesDevelopmentFile)));
        Assert.Throws<InvalidOperationException>(() => DataGuard.EnsureOutsideData(root, DataFiles.Repository(DataGuard.ShapesDevelopmentHardFile)));
        Assert.Throws<InvalidOperationException>(() => DataGuard.EnsureOutsideData(root, Path.Combine(dataFolder, "new-output.txt")));
        Assert.Throws<InvalidOperationException>(() => DataGuard.EnsureOutsideData(root, dataFolder));
        Assert.Throws<InvalidOperationException>(() => DataGuard.EnsureOutsideData(root, detour));

        // Assert: admitted outside the repository, and next to the data folder
        Assert.Equal(Path.GetFullPath(temporary), DataGuard.EnsureOutsideData(root, temporary));
        Assert.Equal(Path.GetFullPath(Path.Combine(root, "database.txt")), DataGuard.EnsureOutsideData(root, Path.Combine(root, "database.txt")));
    }

    /// <summary>
    ///     Creates an item with a one-word sentence for the evaluator tests.
    /// </summary>
    private static ShapesDevItem Item(string id, string shape, string role, string word, string goldClass, string origin = "real", ShapesDevTranche tranche = ShapesDevTranche.Standard) =>
        new(id, origin == "real" ? "doc-a" : "shapes-dev-authored", new[] { word }, 0, goldClass, shape, role, word, origin, tranche);
}
