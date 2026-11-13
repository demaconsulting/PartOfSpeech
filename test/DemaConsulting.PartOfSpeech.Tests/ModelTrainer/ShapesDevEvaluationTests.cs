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
    ///     Joins the header of the hard tranche (its corpus comment carries the hard tranche marker) and blocks.
    /// </summary>
    private static List<string> HardFile(params string[][] blocks)
    {
        var lines = File(blocks);
        lines[0] = "# corpus = fixture hard-shape development set, hard tranche (UPOS), dev split";
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
    ///     Validates that the kind comment belongs to the tranches that have kinds only: the standard and the hard tranche refuse it.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShapesDevSets_Parse_KindCommentInStandardOrHardTranche_Throws(bool hard)
    {
        var tranche = hard ? ShapesDevTranche.Hard : ShapesDevTranche.Standard;

        // Arrange: a valid block that carries a kind comment
        var lines = File(CompoundHead("s1"));
        lines.Insert(lines.Count - 3, "# kind = noun");

        // Act
        var exception = Assert.Throws<InvalidDataException>(() => ShapesDevSets.Parse(lines, "fixture", tranche));

        // Assert
        Assert.Contains("'kind'", exception.Message);
        Assert.Contains("tranches that have kinds", exception.Message);
    }

    /// <summary>
    ///     Validates that each tranche is read from its own file, with its own SHA-256, and marks its items.
    /// </summary>
    [Fact]
    public void DevSets_ReadShapesDev_EachTranche_ReadsItsOwnFileAndMarksItems()
    {
        // Arrange: a fake repository with a different number of sentences in the two files
        var standardLines = File(CompoundHead("s1"), CompoundHead("s2"));
        var hardLines = HardFile(CompoundHead("shapes-dev-hard-0001"));
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
            Assert.Equal(new[] { "shapes-dev-hard-0001" }, hard.Select(i => i.Id));
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
    ///     Validates that a file of one tranche cannot be read as the other: a standard-header file at the hard path, a hard-header file at
    ///     the standard path, and sentence ids of the wrong tranche are refused, while correct files are accepted.
    /// </summary>
    [Fact]
    public void DevSets_ReadShapesDev_FileOfTheOtherTranche_Throws()
    {
        // Arrange: the standard file at the hard path; the hard file at the standard path; right header but the other tranche's ids
        var standardLines = File(CompoundHead("shapes-dev-0001"));
        var hardLines = HardFile(CompoundHead("shapes-dev-hard-0001"));
        var root = TempRoot(hardLines);
        var hardPath = Path.Combine(root, "data", "eval", "shapes-dev", "hard.tsv");
        var standardPath = Path.Combine(root, "data", "eval", "shapes-dev", "dev.tsv");
        try
        {
            // Act / Assert: hard header at the standard path
            Assert.Contains("hard tranche", Assert.Throws<InvalidDataException>(() => DevSets.ReadShapesDev(root, ShapesDevTranche.Standard, out _)).Message);

            // Standard header at the hard path
            System.IO.File.WriteAllText(hardPath, string.Join("\n", standardLines) + "\n");
            Assert.Contains("does not announce itself as the hard tranche", Assert.Throws<InvalidDataException>(() => DevSets.ReadShapesDev(root, ShapesDevTranche.Hard, out _)).Message);

            // Ids of the other tranche behind the right header
            System.IO.File.WriteAllText(hardPath, string.Join("\n", HardFile(CompoundHead("shapes-dev-0001"))) + "\n");
            Assert.Contains("sentence id", Assert.Throws<InvalidDataException>(() => DevSets.ReadShapesDev(root, ShapesDevTranche.Hard, out _)).Message);
            System.IO.File.WriteAllText(standardPath, string.Join("\n", File(CompoundHead("shapes-dev-hard-0001"))) + "\n");
            Assert.Contains("sentence id", Assert.Throws<InvalidDataException>(() => DevSets.ReadShapesDev(root, ShapesDevTranche.Standard, out _)).Message);

            // Correct files are accepted
            System.IO.File.WriteAllText(hardPath, string.Join("\n", hardLines) + "\n");
            System.IO.File.WriteAllText(standardPath, string.Join("\n", standardLines) + "\n");
            Assert.Single(DevSets.ReadShapesDev(root, ShapesDevTranche.Hard, out _));
            Assert.Single(DevSets.ReadShapesDev(root, ShapesDevTranche.Standard, out _));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    ///     Validates that the committed files of both tranches are accepted by the tranche-aware reader.
    /// </summary>
    [Fact]
    public void DevSets_ReadShapesDev_CommittedFiles_AreAccepted()
    {
        // Arrange: the repository root is the folder above data/ (found from the test assembly folder)
        var root = AppContext.BaseDirectory;
        while (root != null && !System.IO.File.Exists(Path.Combine(root, "data", "eval", "shapes-dev", "hard.tsv")))
        {
            root = Path.GetDirectoryName(root);
        }

        Assert.NotNull(root);

        // Act
        var standard = DevSets.ReadShapesDev(root, ShapesDevTranche.Standard, out _);
        var hard = DevSets.ReadShapesDev(root, ShapesDevTranche.Hard, out _);

        // Assert
        Assert.NotEmpty(standard);
        Assert.NotEmpty(hard);
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
        // Arrange / Act / Assert: the list of the set is exactly the seven tranches, the other allow-lists did not grow
        Assert.Equal(
            new[]
            {
                "data/eval/shapes-dev/dev.tsv",
                "data/eval/shapes-dev/hard.tsv",
                "data/eval/shapes-dev/compound-head.tsv",
                "data/eval/shapes-dev/headings.tsv",
                "data/eval/shapes-dev/named-state.tsv",
                "data/eval/shapes-dev/compound-head-2.tsv",
                "data/eval/shapes-dev/headings-2.tsv",
                "data/eval/shapes-dev/deverbal-heads.tsv",
                "data/eval/shapes-dev/defined-terms.tsv",
                "data/eval/shapes-dev/step-lines.tsv",
                "data/eval/shapes-dev/clause-verbs.tsv",
            },
            DataGuard.ShapesDevelopmentFiles);
        Assert.Equal(DataGuard.ShapesDevelopmentFile, DataGuard.ShapesDevelopmentFiles[(int)ShapesDevTranche.Standard]);
        Assert.Equal(DataGuard.ShapesDevelopmentHardFile, DataGuard.ShapesDevelopmentFiles[(int)ShapesDevTranche.Hard]);
        Assert.Equal(DataGuard.ShapesDevelopmentCompoundHeadFile, DataGuard.ShapesDevelopmentFiles[(int)ShapesDevTranche.CompoundHead]);
        Assert.Equal(DataGuard.ShapesDevelopmentHeadingsFile, DataGuard.ShapesDevelopmentFiles[(int)ShapesDevTranche.Headings]);
        Assert.Equal(DataGuard.ShapesDevelopmentNamedStateFile, DataGuard.ShapesDevelopmentFiles[(int)ShapesDevTranche.NamedState]);
        Assert.Equal(DataGuard.ShapesDevelopmentCompoundHead2File, DataGuard.ShapesDevelopmentFiles[(int)ShapesDevTranche.CompoundHead2]);
        Assert.Equal(DataGuard.ShapesDevelopmentHeadings2File, DataGuard.ShapesDevelopmentFiles[(int)ShapesDevTranche.Headings2]);
        Assert.Equal(DataGuard.ShapesDevelopmentDeverbalHeadsFile, DataGuard.ShapesDevelopmentFiles[(int)ShapesDevTranche.DeverbalHeads]);
        Assert.Equal(DataGuard.ShapesDevelopmentDefinedTermsFile, DataGuard.ShapesDevelopmentFiles[(int)ShapesDevTranche.DefinedTerms]);
        Assert.Equal(DataGuard.ShapesDevelopmentStepLinesFile, DataGuard.ShapesDevelopmentFiles[(int)ShapesDevTranche.StepLines]);
        Assert.Equal(DataGuard.ShapesDevelopmentClauseVerbsFile, DataGuard.ShapesDevelopmentFiles[(int)ShapesDevTranche.ClauseVerbs]);
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

#if NET8_0_OR_GREATER
    /// <summary>
    ///     Builds the development data of the arm evaluation from small fixtures: two open sentences, two gold targets, one treebank sentence and four shapes items.
    /// </summary>
    private static ArmEvaluationData ArmData() =>
        new()
        {
            Open = DevSets.ParseOpenDev(
                new[]
                {
                    "# corpus = fixture, dev split",
                    string.Empty,
                    "# sent_id = o1",
                    "# source_id = d1",
                    "The\tDET\tunanimous",
                    "cat\tNOUN\tunanimous",
                    "runs\tVERB\tunanimous",
                    string.Empty,
                    "# sent_id = o2",
                    "# source_id = d2",
                    "Dogs\tNOUN\tunanimous",
                    "sleep\tVERB\tunanimous",
                },
                "fixture"),
            Gold = DevSets.ParseGoldDev(
                new[]
                {
                    "# columns = id doc_id pos target sentence",
                    "g1\tdoc-a\tNoun\t1\tThe cat sleeps",
                    "g2\tdoc-b\tVerb\t1\tWe dog them",
                },
                "fixture"),
            UdDev = HeldOutSets.ParseUd(
                new[]
                {
                    "# sent_id = weblog-0001-0001",
                    "The\tDET\tDET\t_",
                    "cat\tNOUN\tNOUN\t_",
                    "sleeps\tVERB\tVERB\t_",
                },
                "fixture"),
            Shapes = new List<ShapesDevItem>
            {
                Item("a", "n-compound-head", "target", "cat", "Noun"),
                Item("b", "n-compound-head", "control", "cat", "Verb"),
                Item("c", "n-compound-head", "target", "cat", "Noun", tranche: ShapesDevTranche.Hard),
                Item("d", "v-after-modal", "target", "dog", "Verb", tranche: ShapesDevTranche.Hard),
            },
            CompoundHead = new List<ShapesDevItem>
            {
                TrancheItem(ShapesDevTranche.CompoundHead, "ch1", "target", "cat", "NOUN"),
                TrancheItem(ShapesDevTranche.CompoundHead, "ch2", "control", "cat", "VERB"),
            },
            Headings = new List<ShapesDevItem>
            {
                TrancheItem(ShapesDevTranche.Headings, "hd1", "target", "cat", "NOUN", "noun"),
                TrancheItem(ShapesDevTranche.Headings, "hd2", "target", "dog", "VERB", "imperative"),
                TrancheItem(ShapesDevTranche.Headings, "hd3", "control", "cat", "PROPN", "proper"),
            },
            NamedState = new List<ShapesDevItem>
            {
                TrancheItem(ShapesDevTranche.NamedState, "ns1", "target", "cat", "NOUN"),
            },
            CompoundHead2 = new List<ShapesDevItem>
            {
                TrancheItem(ShapesDevTranche.CompoundHead2, "c21", "target", "cat", "NOUN"),
                TrancheItem(ShapesDevTranche.CompoundHead2, "c22", "target", "dog", "NOUN"),
                TrancheItem(ShapesDevTranche.CompoundHead2, "c23", "control", "dog", "VERB"),
            },
            Headings2 = new List<ShapesDevItem>
            {
                TrancheItem(ShapesDevTranche.Headings2, "h21", "target", "cat", "NOUN", "noun"),
                TrancheItem(ShapesDevTranche.Headings2, "h22", "target", "dog", "VERB", "imperative"),
                TrancheItem(ShapesDevTranche.Headings2, "h23", "target", "cat", "VERB", "imperative"),
                TrancheItem(ShapesDevTranche.Headings2, "h24", "control", "cat", "PROPN", "proper"),
            },
            DeverbalHeads = new List<ShapesDevItem>
            {
                TrancheItem(ShapesDevTranche.DeverbalHeads, "dv1", "target", "cat", "NOUN", "pred"),
                TrancheItem(ShapesDevTranche.DeverbalHeads, "dv2", "target", "dog", "NOUN", "ing"),
                TrancheItem(ShapesDevTranche.DeverbalHeads, "dv3", "target", "cat", "NOUN", "other"),
                TrancheItem(ShapesDevTranche.DeverbalHeads, "dv4", "control", "dog", "VERB", "verb"),
            },
            DefinedTerms = new List<ShapesDevItem>
            {
                TrancheItem(ShapesDevTranche.DefinedTerms, "dt1", "target", "cat", "NOUN", "noun"),
                TrancheItem(ShapesDevTranche.DefinedTerms, "dt2", "target", "dog", "ADJ", "adj"),
                TrancheItem(ShapesDevTranche.DefinedTerms, "dt3", "control", "cat", "PROPN", "proper"),
            },
            StepLines = new List<ShapesDevItem>
            {
                TrancheItem(ShapesDevTranche.StepLines, "st1", "target", "dog", "VERB", "imperative"),
                TrancheItem(ShapesDevTranche.StepLines, "st2", "target", "cat", "VERB", "imperative"),
                TrancheItem(ShapesDevTranche.StepLines, "st3", "control", "cat", "NOUN", "noun"),
            },
            ClauseVerbs = new List<ShapesDevItem>
            {
                TrancheItem(ShapesDevTranche.ClauseVerbs, "cv1", "target", "dog", "VERB", "main"),
                TrancheItem(ShapesDevTranche.ClauseVerbs, "cv2", "target", "cat", "VERB", "sub"),
                TrancheItem(ShapesDevTranche.ClauseVerbs, "cv3", "control", "cat", "NOUN", "noun"),
                TrancheItem(ShapesDevTranche.ClauseVerbs, "cv4", "control", "dog", "ADJ", "adj"),
            },
            Inputs = new ArmInputs
            {
                DevSha256 = "s1",
                HardSha256 = "s2",
                UdDevSha256 = "s3",
                OpenDevSha256 = "s4",
                GoldDevSha256 = "s5",
                CompoundHeadSha256 = "s6",
                HeadingsSha256 = "s7",
                NamedStateSha256 = "s8",
                CompoundHead2Sha256 = "s9",
                Headings2Sha256 = "s10",
                DeverbalHeadsSha256 = "s11",
                DefinedTermsSha256 = "s12",
                StepLinesSha256 = "s13",
                ClauseVerbsSha256 = "s14",
                Commit = "abc123",
                Dirty = true,
            },
        };

    /// <summary>
    ///     Validates the result of <c>eval-arm</c>: valid JSON in schema 1 with every member of the contract, the groups of the weights header,
    ///     counts that agree between the per-shape rows, the pooled rows and the per-item outcomes, the inputs that were given, and a label
    ///     that survives escaping.
    /// </summary>
    [Fact]
    public void ArmEvaluation_Evaluate_TinyWeights_WritesTheResultSchema()
    {
        // Arrange: a tiny model with two groups in its header, and the fixtures
        var groups = FeatureGroups.PositionCues | FeatureGroups.RightPreview;
        var weights = new TinyModels.WeightsBuilder().Word("cat", Upos.Noun, 40).Word("dog", Upos.Verb, 40).Build(temperature: 1f, scale: 0.125f, compress: true, groups: groups);
        var data = ArmData();
        var echo = new ArmConfigEcho { PreviewFolds = 5, Epochs = 6, Prune = 8 };
        const string Label = "arm \"x\"\\y\t1";

        // Act
        var text = ArmEvaluation.Evaluate(weights, Label, echo, data, timedPasses: 1);
        using var document = System.Text.Json.JsonDocument.Parse(text);
        var root = document.RootElement;

        // Assert: one line of JSON that ends with a line feed, schema 1, the label and the groups of the header
        Assert.EndsWith("}\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", text, StringComparison.Ordinal);
        Assert.Equal(1, root.GetProperty("schema").GetInt32());
        Assert.Equal(Label, root.GetProperty("label").GetString());
        Assert.Equal(new[] { "positionCues", "rightPreview" }, root.GetProperty("groups").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(1.0, root.GetProperty("temperature").GetDouble());

        // Assert: the echo of the configuration (groups, bucket space and scale from the header, the rest as given)
        var config = root.GetProperty("config");
        Assert.Equal(new[] { "positionCues", "rightPreview" }, config.GetProperty("features").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal((5, 6, TinyModels.HashBits, 8, 8), (config.GetProperty("previewFolds").GetInt32(), config.GetProperty("epochs").GetInt32(), config.GetProperty("hashBits").GetInt32(), config.GetProperty("quantScale").GetInt32(), config.GetProperty("prune").GetInt32()));

        // Assert: the size of the file
        var size = root.GetProperty("weights");
        Assert.Equal(ModelBuilder.Sha256Hex(weights), size.GetProperty("sha256").GetString());
        Assert.Equal(weights.Length, size.GetProperty("bytesDeflated").GetInt32());
        Assert.Equal(PerceptronModel.ToUncompressedImage(weights).Length, size.GetProperty("bytesRaw").GetInt32());
        Assert.True(size.GetProperty("rows").GetInt32() > 0);
        Assert.True(size.GetProperty("weights").GetInt32() >= size.GetProperty("rows").GetInt32());

        // Assert: the throughput counts the tokens of open dev (5) and treebank dev (3)
        var throughput = root.GetProperty("throughput");
        Assert.Equal(8, throughput.GetProperty("tokens").GetInt32());
        Assert.Equal(1, throughput.GetProperty("passes").GetInt32());
        Assert.True(throughput.GetProperty("tokensPerSecond").GetDouble() > 0);

        // Assert: the metric blocks of the contract
        var open = root.GetProperty("openDev");
        Assert.Equal(5, open.GetProperty("tokens").GetInt32());
        foreach (var name in new[] { "accuracy", "ambiguousAccuracy", "nounVerbDecisions", "harmful", "harmfulPer1k", "nll", "ece" })
        {
            Assert.True(open.TryGetProperty(name, out _), "openDev." + name);
        }

        var gold = root.GetProperty("goldDev");
        Assert.Equal(2, gold.GetProperty("tokens").GetInt32());
        Assert.False(gold.TryGetProperty("ambiguousAccuracy", out _));
        foreach (var name in new[] { "accuracy", "nounVerbDecisions", "harmful", "harmfulPer1k", "nll", "ece" })
        {
            Assert.True(gold.TryGetProperty(name, out _), "goldDev." + name);
        }

        var ud = root.GetProperty("udDev");
        Assert.Equal(3, ud.GetProperty("tokens").GetInt32());
        foreach (var name in new[] { "accuracy", "decisions", "harmful", "harmfulPer1k" })
        {
            Assert.True(ud.TryGetProperty(name, out _), "udDev." + name);
        }

        // Assert: the tranches have their shapes (only those with items), pooled rows that add up, and the per-item outcomes of the target items
        var shapes = root.GetProperty("shapes");
        var standard = shapes.GetProperty("standard");
        var hard = shapes.GetProperty("hard");
        Assert.Equal(new[] { "n-compound-head" }, standard.GetProperty("shapes").EnumerateObject().Select(p => p.Name));
        Assert.Equal(new[] { "n-compound-head", "v-after-modal" }, hard.GetProperty("shapes").EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal((1, 1), (standard.GetProperty("pooled").GetProperty("targetItems").GetInt32(), standard.GetProperty("pooled").GetProperty("controlItems").GetInt32()));
        var pooled = hard.GetProperty("pooled");
        Assert.Equal(2, pooled.GetProperty("targetItems").GetInt32());
        foreach (var name in new[] { "targetItems", "targetCorrect", "decisions", "harmful", "controlItems", "controlCorrect" })
        {
            Assert.Equal(
                hard.GetProperty("shapes").EnumerateObject().Sum(p => p.Value.GetProperty(name).GetInt32()),
                pooled.GetProperty(name).GetInt32());
        }

        foreach (var name in new[] { "targetAccuracy", "harmfulPer1k", "controlAccuracy" })
        {
            Assert.True(pooled.TryGetProperty(name, out _), "pooled." + name);
        }

        var items = root.GetProperty("items");
        Assert.Equal(new[] { "c", "d" }, items.GetProperty("hard").EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(new[] { "a" }, items.GetProperty("standard").EnumerateObject().Select(p => p.Name));
        Assert.Equal(pooled.GetProperty("decisions").GetInt32(), items.GetProperty("hard").EnumerateObject().Sum(p => p.Value.GetProperty("d").GetInt32()));
        Assert.Equal(pooled.GetProperty("harmful").GetInt32(), items.GetProperty("hard").EnumerateObject().Sum(p => p.Value.GetProperty("h").GetInt32()));
        Assert.Equal(pooled.GetProperty("targetCorrect").GetInt32(), items.GetProperty("hard").EnumerateObject().Sum(p => p.Value.GetProperty("c").GetInt32()));

        // Assert: the inputs are the ones that were given, the nine tranche hashes after the old members
        var inputs = root.GetProperty("inputs");
        Assert.Equal(new[] { "s1", "s2", "s3", "s4", "s5", "abc123" }, new[] { "devSha256", "hardSha256", "udDevSha256", "openDevSha256", "goldDevSha256", "commit" }.Select(n => inputs.GetProperty(n).GetString()));
        Assert.True(inputs.GetProperty("dirty").GetBoolean());
        Assert.Equal(new[] { "s6", "s7", "s8", "s9", "s10", "s11", "s12", "s13", "s14" }, new[] { "compoundHeadSha256", "headingsSha256", "namedStateSha256", "compoundHead2Sha256", "headings2Sha256", "deverbalHeadsSha256", "definedTermsSha256", "stepLinesSha256", "clauseVerbsSha256" }.Select(n => inputs.GetProperty(n).GetString()));
        Assert.Equal(
            new[] { "devSha256", "hardSha256", "udDevSha256", "openDevSha256", "goldDevSha256", "commit", "dirty", "compoundHeadSha256", "headingsSha256", "namedStateSha256", "compoundHead2Sha256", "headings2Sha256", "deverbalHeadsSha256", "definedTermsSha256", "stepLinesSha256", "clauseVerbsSha256" },
            inputs.EnumerateObject().Select(p => p.Name));

        // Assert: the new blocks are additive: the schema is still 1 and the members of before keep their order, the new blocks sit behind "items"
        Assert.Equal(
            new[] { "schema", "label", "groups", "temperature", "config", "weights", "throughput", "openDev", "goldDev", "udDev", "shapes", "items", "shapesNew", "shapesFollowup", "shapesTechnical", "inputs" },
            root.EnumerateObject().Select(p => p.Name));

        // Assert: the three new tranches have their shape row (with the confident wrong count) and the kinds of the headings only
        var shapesNew = root.GetProperty("shapesNew");
        Assert.Equal(new[] { "compoundHead", "headings", "namedState", "pooledPrimary" }, shapesNew.EnumerateObject().Select(p => p.Name));
        var members = new[] { "targetItems", "targetCorrect", "targetAccuracy", "decisions", "harmful", "harmfulPer1k", "confidentWrong", "controlItems", "controlCorrect", "controlAccuracy" };
        foreach (var name in new[] { "compoundHead", "headings", "namedState" })
        {
            Assert.Equal(new[] { "shape", "kinds" }, shapesNew.GetProperty(name).EnumerateObject().Select(p => p.Name));
            Assert.Equal(members, shapesNew.GetProperty(name).GetProperty("shape").EnumerateObject().Select(p => p.Name));
        }

        var compound = shapesNew.GetProperty("compoundHead").GetProperty("shape");
        var headings = shapesNew.GetProperty("headings").GetProperty("shape");
        var named = shapesNew.GetProperty("namedState").GetProperty("shape");
        Assert.Equal((1, 1), (compound.GetProperty("targetItems").GetInt32(), compound.GetProperty("controlItems").GetInt32()));
        Assert.Equal((2, 1), (headings.GetProperty("targetItems").GetInt32(), headings.GetProperty("controlItems").GetInt32()));
        Assert.Equal((1, 0), (named.GetProperty("targetItems").GetInt32(), named.GetProperty("controlItems").GetInt32()));
        Assert.Empty(shapesNew.GetProperty("compoundHead").GetProperty("kinds").EnumerateObject());
        Assert.Empty(shapesNew.GetProperty("namedState").GetProperty("kinds").EnumerateObject());
        var kinds = shapesNew.GetProperty("headings").GetProperty("kinds");
        Assert.Equal(new[] { "noun", "imperative", "proper" }, kinds.EnumerateObject().Select(p => p.Name));
        Assert.Equal((1, 1, 0), (kinds.GetProperty("noun").GetProperty("targetItems").GetInt32(), kinds.GetProperty("imperative").GetProperty("targetItems").GetInt32(), kinds.GetProperty("proper").GetProperty("targetItems").GetInt32()));
        Assert.Equal(1, kinds.GetProperty("proper").GetProperty("controlItems").GetInt32());
        foreach (var name in new[] { "targetItems", "targetCorrect", "decisions", "harmful", "confidentWrong", "controlItems", "controlCorrect" })
        {
            Assert.Equal(headings.GetProperty(name).GetInt32(), kinds.EnumerateObject().Sum(p => p.Value.GetProperty(name).GetInt32()));
        }

        // Assert: the pooled primary block adds the compound-head and the headings rows (not the named-state row), and its rate is read from the pooled counts
        var primary = shapesNew.GetProperty("pooledPrimary");
        Assert.Equal(new[] { "targetItems", "targetCorrect", "targetAccuracy", "decisions", "harmful", "harmfulPer1k", "confidentWrongTotal" }, primary.EnumerateObject().Select(p => p.Name));
        foreach (var (name, source) in new[] { ("targetItems", "targetItems"), ("targetCorrect", "targetCorrect"), ("decisions", "decisions"), ("harmful", "harmful"), ("confidentWrongTotal", "confidentWrong") })
        {
            Assert.Equal(compound.GetProperty(source).GetInt32() + headings.GetProperty(source).GetInt32(), primary.GetProperty(name).GetInt32());
        }

        Assert.Equal(3, primary.GetProperty("targetItems").GetInt32());
        var pooledDecisions = primary.GetProperty("decisions").GetInt32();
        Assert.True(pooledDecisions > 0);
        Assert.Equal(1000.0 * primary.GetProperty("harmful").GetInt32() / pooledDecisions, primary.GetProperty("harmfulPer1k").GetDouble(), 9);
        Assert.Equal(100.0 * primary.GetProperty("targetCorrect").GetInt32() / 3, primary.GetProperty("targetAccuracy").GetDouble(), 9);

        // Assert: the follow-up block has the four tranches in order, each as a shapesNew row, and the three pooled blocks
        var followup = root.GetProperty("shapesFollowup");
        Assert.Equal(new[] { "compoundHead", "compoundHead2", "headings", "headings2", "pooledPrimary", "imperativeHeadings", "compoundControls" }, followup.EnumerateObject().Select(p => p.Name));
        foreach (var name in new[] { "compoundHead", "compoundHead2", "headings", "headings2" })
        {
            Assert.Equal(new[] { "shape", "kinds" }, followup.GetProperty(name).EnumerateObject().Select(p => p.Name));
            Assert.Equal(members, followup.GetProperty(name).GetProperty("shape").EnumerateObject().Select(p => p.Name));
        }

        // Assert: the old rows in the follow-up block are the rows of shapesNew, and the new rows hold the items of the new tranches
        Assert.Equal(compound.GetRawText(), followup.GetProperty("compoundHead").GetProperty("shape").GetRawText());
        Assert.Equal(headings.GetRawText(), followup.GetProperty("headings").GetProperty("shape").GetRawText());
        var compound2 = followup.GetProperty("compoundHead2").GetProperty("shape");
        var headings2 = followup.GetProperty("headings2").GetProperty("shape");
        Assert.Equal((2, 1), (compound2.GetProperty("targetItems").GetInt32(), compound2.GetProperty("controlItems").GetInt32()));
        Assert.Equal((3, 1), (headings2.GetProperty("targetItems").GetInt32(), headings2.GetProperty("controlItems").GetInt32()));
        Assert.Empty(followup.GetProperty("compoundHead2").GetProperty("kinds").EnumerateObject());
        var kinds2 = followup.GetProperty("headings2").GetProperty("kinds");
        Assert.Equal(new[] { "noun", "imperative", "proper" }, kinds2.EnumerateObject().Select(p => p.Name));
        Assert.Equal((1, 2, 0), (kinds2.GetProperty("noun").GetProperty("targetItems").GetInt32(), kinds2.GetProperty("imperative").GetProperty("targetItems").GetInt32(), kinds2.GetProperty("proper").GetProperty("targetItems").GetInt32()));

        // Assert: the pooled primary of the follow-up adds the four tranche rows (all heading dev items, the enlarged compound dev) and its rate is read from the pooled counts
        var pool = followup.GetProperty("pooledPrimary");
        Assert.Equal(new[] { "targetItems", "targetCorrect", "targetAccuracy", "decisions", "harmful", "harmfulPer1k", "confidentWrongTotal" }, pool.EnumerateObject().Select(p => p.Name));
        var rows = new[] { compound, compound2, headings, headings2 };
        foreach (var (name, source) in new[] { ("targetItems", "targetItems"), ("targetCorrect", "targetCorrect"), ("decisions", "decisions"), ("harmful", "harmful"), ("confidentWrongTotal", "confidentWrong") })
        {
            Assert.Equal(rows.Sum(r => r.GetProperty(source).GetInt32()), pool.GetProperty(name).GetInt32());
        }

        Assert.Equal(1 + 2 + 2 + 3, pool.GetProperty("targetItems").GetInt32());
        var poolDecisions = pool.GetProperty("decisions").GetInt32();
        Assert.True(poolDecisions > 0);
        Assert.Equal(1000.0 * pool.GetProperty("harmful").GetInt32() / poolDecisions, pool.GetProperty("harmfulPer1k").GetDouble(), 9);
        Assert.Equal(100.0 * pool.GetProperty("targetCorrect").GetInt32() / 8, pool.GetProperty("targetAccuracy").GetDouble(), 9);

        // Assert: the pooled imperative headings are the imperative rows of both headings tranches, no more
        var imperative = followup.GetProperty("imperativeHeadings");
        Assert.Equal(new[] { "targetItems", "targetCorrect", "targetAccuracy", "decisions", "harmful", "harmfulPer1k", "confidentWrong" }, imperative.EnumerateObject().Select(p => p.Name));
        var imperativeRows = new[] { kinds.GetProperty("imperative"), kinds2.GetProperty("imperative") };
        foreach (var name in new[] { "targetItems", "targetCorrect", "decisions", "harmful", "confidentWrong" })
        {
            Assert.Equal(imperativeRows.Sum(r => r.GetProperty(name).GetInt32()), imperative.GetProperty(name).GetInt32());
        }

        Assert.Equal(3, imperative.GetProperty("targetItems").GetInt32());
        var imperativeDecisions = imperative.GetProperty("decisions").GetInt32();
        Assert.True(imperativeDecisions > 0);
        Assert.Equal(1000.0 * imperative.GetProperty("harmful").GetInt32() / imperativeDecisions, imperative.GetProperty("harmfulPer1k").GetDouble(), 9);
        Assert.Equal(100.0 * imperative.GetProperty("targetCorrect").GetInt32() / 3, imperative.GetProperty("targetAccuracy").GetDouble(), 9);

        // Assert: the compound controls pool the controls of both compound-head tranches
        var controls = followup.GetProperty("compoundControls");
        Assert.Equal(new[] { "controlItems", "controlCorrect", "controlAccuracy" }, controls.EnumerateObject().Select(p => p.Name));
        Assert.Equal(2, controls.GetProperty("controlItems").GetInt32());
        Assert.Equal(compound.GetProperty("controlCorrect").GetInt32() + compound2.GetProperty("controlCorrect").GetInt32(), controls.GetProperty("controlCorrect").GetInt32());
        Assert.Equal(100.0 * controls.GetProperty("controlCorrect").GetInt32() / 2, controls.GetProperty("controlAccuracy").GetDouble(), 9);

        // Assert: the same weights and fixtures give the same result up to the measured throughput
        var again = ArmEvaluation.Evaluate(weights, Label, echo, data, timedPasses: 1);
        Assert.Equal(System.Text.RegularExpressions.Regex.Replace(text, "\"tokensPerSecond\":[^,]+", string.Empty), System.Text.RegularExpressions.Regex.Replace(again, "\"tokensPerSecond\":[^,]+", string.Empty));
    }

    /// <summary>
    ///     Validates the <c>shapesTechnical</c> block of <c>eval-arm</c>: the four tranche rows with their kinds and the counts at 0.90 and 0.95,
    ///     the pooled counts of the three noun-verb tranches, the exact-tag error of the defined terms, the calibration scopes (pooled technical,
    ///     all nine tranches and one per technical tranche), and that every member of before is unchanged when the technical items are added.
    /// </summary>
    [Fact]
    public void ArmEvaluation_Evaluate_TechnicalBlock_RowsPooledCountsCalibrationAndOldKeysUnchanged()
    {
        // Arrange
        var weights = new TinyModels.WeightsBuilder().Word("cat", Upos.Noun, 40).Word("dog", Upos.Verb, 40).Build(temperature: 1f, scale: 0.125f, compress: true);
        var echo = new ArmConfigEcho { PreviewFolds = 5, Epochs = 6, Prune = 8 };
        var data = ArmData();

        // Act
        var text = ArmEvaluation.Evaluate(weights, "arm", echo, data, timedPasses: 1);
        using var document = System.Text.Json.JsonDocument.Parse(text);
        var technical = document.RootElement.GetProperty("shapesTechnical");

        // Assert: the members of the block, in order, and the members of a tranche row
        Assert.Equal(new[] { "deverbalHeads", "definedTerms", "stepLines", "clauseVerbs", "pooledTechnical", "termError", "calibration" }, technical.EnumerateObject().Select(p => p.Name));
        var members = new[] { "targetItems", "targetCorrect", "targetAccuracy", "decisions", "harmful", "harmfulPer1k", "confidentWrong", "wrong90", "controlItems", "controlCorrect", "controlAccuracy", "controlWrong90", "controlConfidentWrong" };
        var expectedKinds = new Dictionary<string, string[]>
        {
            ["deverbalHeads"] = new[] { "pred", "num", "ing", "other", "verb" },
            ["definedTerms"] = new[] { "noun", "adj", "proper" },
            ["stepLines"] = new[] { "imperative", "noun" },
            ["clauseVerbs"] = new[] { "main", "sub", "noun", "adj" },
        };
        var expectedItems = new Dictionary<string, (int Targets, int Controls)>
        {
            ["deverbalHeads"] = (3, 1),
            ["definedTerms"] = (2, 1),
            ["stepLines"] = (2, 1),
            ["clauseVerbs"] = (2, 2),
        };
        foreach (var (name, kinds) in expectedKinds)
        {
            var row = technical.GetProperty(name);
            Assert.Equal(new[] { "shape", "kinds" }, row.EnumerateObject().Select(p => p.Name));
            var shape = row.GetProperty("shape");
            Assert.Equal(members, shape.EnumerateObject().Select(p => p.Name));
            Assert.Equal(expectedItems[name], (shape.GetProperty("targetItems").GetInt32(), shape.GetProperty("controlItems").GetInt32()));
            Assert.Equal(kinds, row.GetProperty("kinds").EnumerateObject().Select(p => p.Name));
            foreach (var member in new[] { "targetItems", "targetCorrect", "decisions", "harmful", "confidentWrong", "wrong90", "controlItems", "controlCorrect", "controlWrong90", "controlConfidentWrong" })
            {
                Assert.Equal(shape.GetProperty(member).GetInt32(), row.GetProperty("kinds").EnumerateObject().Sum(p => p.Value.GetProperty(member).GetInt32()));
            }

            Assert.True(shape.GetProperty("wrong90").GetInt32() >= shape.GetProperty("confidentWrong").GetInt32());
            Assert.True(shape.GetProperty("controlWrong90").GetInt32() >= shape.GetProperty("controlConfidentWrong").GetInt32());
        }

        // Assert: the pooled block adds the deverbal-heads, step-lines and clause-verbs rows (not the defined terms), and its rate is derived from the counts
        var pooled = technical.GetProperty("pooledTechnical");
        Assert.Equal(new[] { "targetItems", "targetCorrect", "targetAccuracy", "decisions", "harmful", "harmfulPer1k", "confidentWrongTotal", "wrong90Total" }, pooled.EnumerateObject().Select(p => p.Name));
        var rows = new[] { "deverbalHeads", "stepLines", "clauseVerbs" }.Select(n => technical.GetProperty(n).GetProperty("shape")).ToList();
        foreach (var (name, source) in new[] { ("targetItems", "targetItems"), ("targetCorrect", "targetCorrect"), ("decisions", "decisions"), ("harmful", "harmful"), ("confidentWrongTotal", "confidentWrong"), ("wrong90Total", "wrong90") })
        {
            Assert.Equal(rows.Sum(r => r.GetProperty(source).GetInt32()), pooled.GetProperty(name).GetInt32());
        }

        Assert.Equal(7, pooled.GetProperty("targetItems").GetInt32());
        var decisions = pooled.GetProperty("decisions").GetInt32();
        Assert.True(decisions > 0);
        Assert.Equal(1000.0 * pooled.GetProperty("harmful").GetInt32() / decisions, pooled.GetProperty("harmfulPer1k").GetDouble(), 9);
        Assert.Equal(100.0 * pooled.GetProperty("targetCorrect").GetInt32() / 7, pooled.GetProperty("targetAccuracy").GetDouble(), 9);

        // Assert: the defined-terms error is the exact-tag error per 1,000 target items
        var terms = technical.GetProperty("definedTerms").GetProperty("shape");
        var termError = technical.GetProperty("termError");
        Assert.Equal(new[] { "targetItems", "targetCorrect", "errors", "errorPer1k", "confidentWrong", "wrong90" }, termError.EnumerateObject().Select(p => p.Name));
        Assert.Equal(2, termError.GetProperty("targetItems").GetInt32());
        Assert.Equal(terms.GetProperty("targetCorrect").GetInt32(), termError.GetProperty("targetCorrect").GetInt32());
        Assert.Equal(2 - terms.GetProperty("targetCorrect").GetInt32(), termError.GetProperty("errors").GetInt32());
        Assert.Equal(1000.0 * termError.GetProperty("errors").GetInt32() / 2, termError.GetProperty("errorPer1k").GetDouble(), 9);
        Assert.Equal((terms.GetProperty("confidentWrong").GetInt32(), terms.GetProperty("wrong90").GetInt32()), (termError.GetProperty("confidentWrong").GetInt32(), termError.GetProperty("wrong90").GetInt32()));

        // Assert: the calibration scopes, the thresholds 0.90 and 0.95 and seven bins that add up to the judged words
        var calibration = technical.GetProperty("calibration");
        Assert.Equal(new[] { "technical", "allSingle", "tranches" }, calibration.EnumerateObject().Select(p => p.Name));
        Assert.Equal(new[] { "deverbalHeads", "definedTerms", "stepLines", "clauseVerbs" }, calibration.GetProperty("tranches").EnumerateObject().Select(p => p.Name));
        Assert.Equal(14, calibration.GetProperty("technical").GetProperty("judged").GetInt32());
        Assert.Equal(14 + 2 + 3 + 1 + 3 + 4, calibration.GetProperty("allSingle").GetProperty("judged").GetInt32());
        Assert.Equal(new[] { 4, 3, 3, 4 }, calibration.GetProperty("tranches").EnumerateObject().Select(p => p.Value.GetProperty("judged").GetInt32()));
        foreach (var scope in new[] { calibration.GetProperty("technical"), calibration.GetProperty("allSingle") }.Concat(calibration.GetProperty("tranches").EnumerateObject().Select(p => p.Value)))
        {
            Assert.Equal(new[] { "judged", "thresholds", "bins" }, scope.EnumerateObject().Select(p => p.Name));
            var thresholds = scope.GetProperty("thresholds").EnumerateArray().ToList();
            Assert.Equal(new[] { 0.9, 0.95 }, thresholds.Select(t => t.GetProperty("threshold").GetDouble()));
            foreach (var row in thresholds)
            {
                Assert.Equal(new[] { "threshold", "answers", "wrong", "errorPer1k", "intervalLowPer1k", "intervalHighPer1k" }, row.EnumerateObject().Select(p => p.Name));
                var answers = row.GetProperty("answers").GetInt32();
                Assert.InRange(row.GetProperty("wrong").GetInt32(), 0, answers);
                Assert.Equal(answers == 0 ? 0.0 : 1000.0 * row.GetProperty("wrong").GetInt32() / answers, row.GetProperty("errorPer1k").GetDouble(), 9);
            }

            var bins = scope.GetProperty("bins").EnumerateArray().ToList();
            Assert.Equal(new[] { 0.0, 0.5, 0.6, 0.7, 0.8, 0.9, 0.95 }, bins.Select(b => b.GetProperty("low").GetDouble()));
            Assert.Equal(new[] { "low", "high", "count", "meanConfidence", "correctPercent", "harmful" }, bins[0].EnumerateObject().Select(p => p.Name));
            Assert.Equal(scope.GetProperty("judged").GetInt32(), bins.Sum(b => b.GetProperty("count").GetInt32()));
        }

        // Assert: the pooled technical scope is the sum of its four tranche scopes
        var perTranche = calibration.GetProperty("tranches").EnumerateObject().Select(p => p.Value).ToList();
        foreach (var index in new[] { 0, 1 })
        {
            Assert.Equal(
                perTranche.Sum(s => s.GetProperty("thresholds")[index].GetProperty("answers").GetInt32()),
                calibration.GetProperty("technical").GetProperty("thresholds")[index].GetProperty("answers").GetInt32());
            Assert.Equal(
                perTranche.Sum(s => s.GetProperty("thresholds")[index].GetProperty("wrong").GetInt32()),
                calibration.GetProperty("technical").GetProperty("thresholds")[index].GetProperty("wrong").GetInt32());
        }

        // Act: the same data without the technical items and hashes
        var without = ArmData();
        without.DeverbalHeads = Array.Empty<ShapesDevItem>();
        without.DefinedTerms = Array.Empty<ShapesDevItem>();
        without.StepLines = Array.Empty<ShapesDevItem>();
        without.ClauseVerbs = Array.Empty<ShapesDevItem>();
        using var before = System.Text.Json.JsonDocument.Parse(ArmEvaluation.Evaluate(weights, "arm", echo, without, timedPasses: 1));

        // Assert: every member of before is byte for byte the same (the throughput is a measurement), the block is present with zero counts, and the inputs differ by the new hashes only
        foreach (var name in new[] { "schema", "label", "groups", "temperature", "config", "weights", "openDev", "goldDev", "udDev", "shapes", "items", "shapesNew", "shapesFollowup" })
        {
            Assert.Equal(before.RootElement.GetProperty(name).GetRawText(), document.RootElement.GetProperty(name).GetRawText());
        }

        Assert.Equal(before.RootElement.GetProperty("throughput").GetProperty("tokens").GetRawText(), document.RootElement.GetProperty("throughput").GetProperty("tokens").GetRawText());
        Assert.Equal(0, before.RootElement.GetProperty("shapesTechnical").GetProperty("pooledTechnical").GetProperty("targetItems").GetInt32());
        Assert.Equal(0, before.RootElement.GetProperty("shapesTechnical").GetProperty("calibration").GetProperty("technical").GetProperty("judged").GetInt32());
        foreach (var name in new[] { "devSha256", "hardSha256", "udDevSha256", "openDevSha256", "goldDevSha256", "commit", "dirty", "compoundHeadSha256", "headingsSha256", "namedStateSha256", "compoundHead2Sha256", "headings2Sha256" })
        {
            Assert.Equal(before.RootElement.GetProperty("inputs").GetProperty(name).GetRawText(), document.RootElement.GetProperty("inputs").GetProperty(name).GetRawText());
        }
    }

    /// <summary>
    ///     Validates that an arm without groups echoes no preview fold count and that invalid weights and a missing timed pass are refused.
    /// </summary>
    [Fact]
    public void ArmEvaluation_Evaluate_NoGroupsAndBadInput_BehavesAsSpecified()
    {
        // Arrange
        var weights = new TinyModels.WeightsBuilder().Word("cat", Upos.Noun, 40).Build(temperature: 1f, scale: 0.125f);
        var echo = new ArmConfigEcho { PreviewFolds = 5, Epochs = 6, Prune = 8 };

        // Act
        using var document = System.Text.Json.JsonDocument.Parse(ArmEvaluation.Evaluate(weights, "base", echo, ArmData(), timedPasses: 1));

        // Assert: no group, so no fold count
        Assert.Empty(document.RootElement.GetProperty("groups").EnumerateArray());
        Assert.Equal(0, document.RootElement.GetProperty("config").GetProperty("previewFolds").GetInt32());
        Assert.Throws<InvalidDataException>(() => ArmEvaluation.Evaluate(new byte[16], "x", echo, ArmData(), timedPasses: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ArmEvaluation.Evaluate(weights, "x", echo, ArmData(), timedPasses: 0));
    }
#endif

    /// <summary>
    ///     Validates that the files of the arm evaluation (the weights it reads, the result it writes) must lie outside the data folder and the
    ///     data tooling folder (which holds the treebank cache), whatever the spelling, so that a held-out file can neither be read as weights
    ///     nor be overwritten by a result, while ordinary locations are admitted.
    /// </summary>
    /// <param name="relative">The path relative to the repository root.</param>
    [Theory]
    [InlineData("data/open/test.tsv")]
    [InlineData("data/eval/gold-v2/test.tsv")]
    [InlineData("data/eval/shapes/probe.tsv")]
    [InlineData("data/eval/ste-challenge/test.tsv")]
    [InlineData("data/open/dev.tsv")]
    [InlineData("data/eval/shapes-dev/hard.tsv")]
    [InlineData("data/new-result.json")]
    [InlineData("tools/data/cache/ud-ewt/ud_test.tsv")]
    [InlineData("tools/data/cache/ud-ewt/ud_dev.tsv")]
    [InlineData("tools/data/cache/ud-ewt/en_ewt-ud-test.conllu")]
    [InlineData("tools/data/new-result.json")]
    [InlineData("tools/src/../data/cache/ud-ewt/ud_test.tsv")]
    [InlineData("tools/../data/open/test.tsv")]
    public void DataGuard_EnsureArmFile_DataFolders_Refused(string relative)
    {
        // Arrange / Act / Assert
        Assert.Throws<InvalidOperationException>(() => DataGuard.EnsureArmFile(DataFiles.RepositoryRoot, DataFiles.Repository(relative)));
    }

    /// <summary>
    ///     Validates that ordinary locations are admitted for the files of the arm evaluation: the shipped weights, a result next to the harness
    ///     and a temporary file.
    /// </summary>
    [Fact]
    public void DataGuard_EnsureArmFile_OrdinaryLocations_Admitted()
    {
        // Arrange
        var root = DataFiles.RepositoryRoot;
        var temporary = Path.Combine(Path.GetTempPath(), "arm-guard", "result.json");

        // Act / Assert
        Assert.Equal(Path.GetFullPath(temporary), DataGuard.EnsureArmFile(root, temporary));
        Assert.Equal(Path.GetFullPath(DataFiles.Repository("src/DemaConsulting.PartOfSpeech/Resources/weights.bin")), DataGuard.EnsureArmFile(root, DataFiles.Repository("src/DemaConsulting.PartOfSpeech/Resources/weights.bin")));
        Assert.Equal(Path.GetFullPath(DataFiles.Repository("tools/database.json")), DataGuard.EnsureArmFile(root, DataFiles.Repository("tools/database.json")));
    }

    /// <summary>
    ///     Pins that <c>eval-arm</c> cannot reach a held-out set: every held-out file and the test table of the treebank are refused by the
    ///     development guards and by the arm guard, the development table of the treebank is another file with its own pin, and neither the
    ///     source of the evaluation nor the source of the command mentions the held-out readers, the selection that opens them or the held-out
    ///     file names; the command's only paths are its two guarded files and the loader of the development data takes the repository root only.
    /// </summary>
    [Fact]
    public void ArmEvaluation_HeldOutSets_AreUnreachable()
    {
        // Arrange: the five held-out sets
        var root = DataFiles.RepositoryRoot;
        var heldOut = DataGuard.HeldOutFiles.Concat(new[] { DataGuard.UdCacheFolder + "/" + DataGuard.UdMappedFile }).ToList();
        Assert.Equal(5, heldOut.Count);

        // Act / Assert: no guard of a development or arm input admits one
        foreach (var relative in heldOut)
        {
            var path = DataFiles.Repository(relative);
            Assert.Throws<InvalidOperationException>(() => DataGuard.EnsureDevelopmentFile(root, path));
            Assert.Throws<InvalidOperationException>(() => DataGuard.EnsureShapesDevelopmentFile(root, path));
            Assert.Throws<InvalidOperationException>(() => DataGuard.EnsureArmFile(root, path));
        }

        // Assert: the development table of the treebank is a different file and a different pin
        Assert.NotEqual(DataGuard.UdMappedFile, DataGuard.UdDevMappedFile);
        Assert.NotEqual(DataGuard.UdMappedSha256, DataGuard.UdDevMappedSha256);

        // Assert: the sources do not mention the machinery of the held-out run
        var forbidden = new[] { "HeldOutReader", "HeldOutFiles", "HeldOutData", "HeldOutSet.", "UdMappedFile", "UdMappedSha256", "SelectedOperatingPoints", "EnsureHeldOut", "ReadUd(" };
        var engine = System.IO.File.ReadAllText(DataFiles.Repository("tools/DemaConsulting.PartOfSpeech.Trainer/Evaluation/ArmEvaluation.cs"));
        var program = System.IO.File.ReadAllText(DataFiles.Repository("tools/DemaConsulting.PartOfSpeech.Trainer/Program.cs"));
        var start = program.IndexOf("private static int EvalArm(", StringComparison.Ordinal);
        var command = program.Substring(start, program.IndexOf("private static (string Commit, bool Dirty) GitState", StringComparison.Ordinal) - start);
        foreach (var name in forbidden)
        {
            Assert.DoesNotContain(name, engine, StringComparison.Ordinal);
            Assert.DoesNotContain(name, command, StringComparison.Ordinal);
        }

        // Assert: the command guards its two files, reads the development data through the one loader and has no data option
        Assert.Equal(2, command.Split(new[] { "DataGuard.EnsureArmFile(" }, StringSplitOptions.None).Length - 1);
        Assert.Contains("ArmEvaluationData.Load(root)", command, StringComparison.Ordinal);
        Assert.DoesNotContain("--ud-cache", command, StringComparison.Ordinal);
        var load = typeof(ArmEvaluationData).GetMethod(nameof(ArmEvaluationData.Load))!;
        Assert.Equal(new[] { typeof(string) }, load.GetParameters().Select(p => p.ParameterType));
        var evaluate = typeof(ArmEvaluation).GetMethod(nameof(ArmEvaluation.Evaluate))!;
        Assert.Equal(new[] { typeof(byte[]), typeof(string), typeof(ArmConfigEcho), typeof(ArmEvaluationData), typeof(int) }, evaluate.GetParameters().Select(p => p.ParameterType));
    }

    /// <summary>
    ///     Validates the reader of the development part of the treebank: a missing cache, a table with another hash and a cache that holds only the
    ///     test table are all refused with the recipe, and the cached table of this machine, when present, is the pinned one.
    /// </summary>
    [Fact]
    public void DevSets_ReadUdDev_MissingOrChangedTable_RefusedAndPinnedTableRead()
    {
        // Arrange: a fake repository root with an empty cache folder
        var root = Path.Combine(Path.GetTempPath(), "ud-dev-" + Guid.NewGuid().ToString("N"));
        var cache = Path.Combine(root, "tools", "data", "cache", "ud-ewt");
        Directory.CreateDirectory(cache);
        try
        {
            // Act / Assert: missing
            var missing = Assert.Throws<InvalidDataException>(() => DevSets.ReadUdDev(root, out _));
            Assert.Contains("fetch_ud_ewt.py", missing.Message, StringComparison.Ordinal);

            // Act / Assert: a cache that holds the test table only is still a missing development table
            System.IO.File.WriteAllText(Path.Combine(cache, DataGuard.UdMappedFile), "# cache file, never commit: test, convention-mapped\n");
            Assert.Throws<InvalidDataException>(() => DevSets.ReadUdDev(root, out _));

            // Act / Assert: another table in the place of the development table
            System.IO.File.WriteAllText(Path.Combine(cache, DataGuard.UdDevMappedFile), "# cache file, never commit: dev, convention-mapped\n");
            var changed = Assert.Throws<InvalidDataException>(() => DevSets.ReadUdDev(root, out _));
            Assert.Contains("pinned SHA-256", changed.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
        }

        // Assert: the table of this machine, when the (git-ignored) cache exists, is the pinned one and has the documented size
        if (System.IO.File.Exists(DataFiles.Repository(DataGuard.UdCacheFolder + "/" + DataGuard.UdDevMappedFile)))
        {
            var sentences = DevSets.ReadUdDev(DataFiles.RepositoryRoot, out var sha);
            Assert.Equal(DataGuard.UdDevMappedSha256, sha);
            Assert.Equal(25148, sentences.Sum(s => s.Tokens.Length));
        }
    }

    /// <summary>
    ///     Validates the throughput measurement: it needs at least one timed pass and reports a positive rate.
    /// </summary>
    [Fact]
    public void HeldOutEvaluator_Throughput_TimedPasses_AtLeastOneAndPositiveRate()
    {
        // Arrange
        var tagger = TinyModels.CreateTagger(new TinyModels.WeightsBuilder().Word("cat", Upos.Noun, 40).Build(temperature: 1f, scale: 0.1f), TinyModels.CreateLexicon("cat NOUN VERB"));
        var sentences = new List<string[]> { new[] { "The", "cat", "sleeps" } };

        // Act / Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => HeldOutEvaluator.Throughput(tagger, sentences, 0));
        Assert.True(HeldOutEvaluator.Throughput(tagger, sentences, 1) > 0);
        Assert.True(HeldOutEvaluator.Throughput(tagger, sentences, 4) > 0);
        Assert.True(HeldOutEvaluator.Throughput(tagger, sentences) > 0);
    }

    /// <summary>
    ///     Builds a block of a single-shape tranche: the judged word <c>cat</c> at index 1 of a three-word sentence.
    /// </summary>
    private static string[] NewBlock(string id, string shape, string role, string tag, string? kind = null, string status = "unanimous")
    {
        var block = Sentence(id, shape, role, 1, new[] { "The", "cat", "sleeps" }, new[] { "DET", tag, "VERB" }, targetStatus: status, lemma: "cat");
        if (kind == null)
        {
            return block;
        }

        var lines = block.ToList();
        lines.Insert(lines.FindIndex(l => l.StartsWith("# origin = ", StringComparison.Ordinal)) + 1, "# kind = " + kind);
        return lines.ToArray();
    }

    /// <summary>
    ///     Joins the header of a single-shape tranche (its corpus comment carries the tranche marker) and blocks.
    /// </summary>
    private static List<string> TrancheFile(string tranche, params string[][] blocks)
    {
        var lines = File(blocks);
        lines[0] = $"# corpus = fixture hard-shape development set, {tranche} tranche (UPOS), dev split";
        return lines;
    }

    /// <summary>
    ///     The sentence id of a single-shape tranche.
    /// </summary>
    private static string NewId(string tranche, int number) => $"shapes-dev-{tranche}-{number:0000}";

    /// <summary>
    ///     A valid file of the compound-head tranche: a NOUN target, a PROPN target and a VERB control.
    /// </summary>
    private static List<string> CompoundHeadFile() => TrancheFile(
        "compound-head",
        NewBlock(NewId("compound-head", 1), "n-compound-head-ambiguous", "target", "NOUN"),
        NewBlock(NewId("compound-head", 2), "n-compound-head-ambiguous", "target", "PROPN"),
        NewBlock(NewId("compound-head", 3), "n-compound-head-ambiguous", "control", "VERB"));

    /// <summary>
    ///     A valid file of the headings tranche: a noun, an imperative and a proper heading.
    /// </summary>
    private static List<string> HeadingsFile() => TrancheFile(
        "headings",
        NewBlock(NewId("headings", 1), "heading-titlecase", "target", "NOUN", "noun"),
        NewBlock(NewId("headings", 2), "heading-titlecase", "target", "VERB", "imperative"),
        NewBlock(NewId("headings", 3), "heading-titlecase", "control", "PROPN", "proper"));

    /// <summary>
    ///     A valid file of the named-state tranche: a PROPN target, a NOUN target and a VERB control.
    /// </summary>
    private static List<string> NamedStateFile() => TrancheFile(
        "named-state",
        NewBlock(NewId("named-state", 1), "n-named-state", "target", "PROPN"),
        NewBlock(NewId("named-state", 2), "n-named-state", "target", "NOUN"),
        NewBlock(NewId("named-state", 3), "n-named-state", "control", "VERB"));

    /// <summary>
    ///     Writes a file into the shapes development folder of a temporary repository root.
    /// </summary>
    private static void WriteShapes(string root, string file, IEnumerable<string> lines) =>
        System.IO.File.WriteAllText(Path.Combine(root, "data", "eval", "shapes-dev", file), string.Join("\n", lines) + "\n");

    /// <summary>
    ///     Creates an item of a single-shape tranche with a one-word sentence.
    /// </summary>
    private static ShapesDevItem TrancheItem(ShapesDevTranche tranche, string id, string role, string word, string goldTag, string kind = "") =>
        new(id, "doc-a", new[] { word }, 0, goldTag == "VERB" ? "Verb" : "Noun", ShapesDevSets.SpecOf(tranche)!.Shape, role, word, "real", tranche, kind, goldTag);

    /// <summary>
    ///     Creates a tagger that calls <c>cat</c> a PROPN (NOUN is the other candidate) and <c>dog</c> a VERB (NOUN is the other candidate).
    /// </summary>
    private static Tagger PropnCatVerbDogTagger() => TinyModels.CreateTagger(
        new TinyModels.WeightsBuilder().Word("cat", Upos.Propn, 40).Word("dog", Upos.Verb, 40).Build(temperature: 1f, scale: 0.1f),
        TinyModels.CreateLexicon("cat NOUN PROPN", "dog NOUN VERB"),
        new TaggerOptions { MaxAlternatives = 16 });

    /// <summary>
    ///     Creates a tagger for the sentences of the file fixtures (the embedded lexicon; <c>cat</c> is a noun).
    /// </summary>
    private static Tagger FixtureTagger() => TinyModels.CreateTagger(
        new TinyModels.WeightsBuilder().Word("cat", Upos.Noun, 40).Build(temperature: 1f, scale: 0.1f),
        null,
        new TaggerOptions { MaxAlternatives = 16 });

    /// <summary>
    ///     Validates that valid files of the three single-shape tranches are read into items with shape, role, kind, gold tag and gold class.
    /// </summary>
    [Fact]
    public void ShapesDevSets_Parse_NewTranches_ValidFiles_ReturnItemsWithKindAndGoldTag()
    {
        // Arrange / Act
        var compound = ShapesDevSets.Parse(CompoundHeadFile(), "fixture", ShapesDevTranche.CompoundHead);
        var headings = ShapesDevSets.Parse(HeadingsFile(), "fixture", ShapesDevTranche.Headings);
        var named = ShapesDevSets.Parse(NamedStateFile(), "fixture", ShapesDevTranche.NamedState);

        // Assert: class-scored tranches carry the tag and its class, no kind
        Assert.Equal(new[] { "NOUN", "PROPN", "VERB" }, compound.Select(i => i.GoldTag));
        Assert.Equal(new[] { "Noun", "Noun", "Verb" }, compound.Select(i => i.GoldClass));
        Assert.Equal(new[] { "target", "target", "control" }, compound.Select(i => i.Role));
        Assert.All(compound, i => Assert.Equal(("n-compound-head-ambiguous", ShapesDevTranche.CompoundHead, string.Empty), (i.Shape, i.Tranche, i.Kind)));
        Assert.Equal(new[] { "PROPN", "NOUN", "VERB" }, named.Select(i => i.GoldTag));
        Assert.All(named, i => Assert.Equal(("n-named-state", ShapesDevTranche.NamedState), (i.Shape, i.Tranche)));

        // Assert: headings carry the kind, the role it fixes and the exact gold tag
        Assert.Equal(new[] { "noun", "imperative", "proper" }, headings.Select(i => i.Kind));
        Assert.Equal(new[] { "NOUN", "VERB", "PROPN" }, headings.Select(i => i.GoldTag));
        Assert.Equal(new[] { true, true, false }, headings.Select(i => i.IsTarget));
        Assert.All(headings, i => Assert.Equal(("heading-titlecase", ShapesDevTranche.Headings), (i.Shape, i.Tranche)));
    }

    /// <summary>
    ///     Validates that each invalid item of a single-shape tranche is refused with a message that names the rule: a shape that is not the
    ///     shape of the tranche, a wrong gold tag, a missing, unknown or misplaced kind, a kind with the wrong role and a disputed judged word.
    /// </summary>
    [Fact]
    public void ShapesDevSets_Parse_NewTranches_InvalidItems_AreRefused()
    {
        // Arrange: one broken file per rule, with the text the message must contain
        var compound = (ShapesDevTranche.CompoundHead, "n-compound-head-ambiguous");
        var named = (ShapesDevTranche.NamedState, "n-named-state");
        var headings = (ShapesDevTranche.Headings, "heading-titlecase");
        var cases = new List<(ShapesDevTranche Tranche, string[] Block, string Expected)>
        {
            (compound.Item1, NewBlock(NewId("compound-head", 1), "n-compound-head", "target", "NOUN"), "the only shape of the compound-head tranche"),
            (named.Item1, NewBlock(NewId("named-state", 1), "heading-titlecase", "target", "NOUN"), "the only shape of the named-state tranche"),
            (headings.Item1, NewBlock(NewId("headings", 1), "n-named-state", "target", "NOUN", "noun"), "the only shape of the headings tranche"),
            (compound.Item1, NewBlock(NewId("compound-head", 1), compound.Item2, "target", "VERB"), "must be tagged NOUN or PROPN but is tagged VERB"),
            (compound.Item1, NewBlock(NewId("compound-head", 1), compound.Item2, "control", "NOUN"), "must be tagged VERB but is tagged NOUN"),
            (named.Item1, NewBlock(NewId("named-state", 1), named.Item2, "target", "ADJ"), "must be tagged NOUN or PROPN but is tagged ADJ"),
            (compound.Item1, NewBlock(NewId("compound-head", 1), compound.Item2, "target", "NOUN", "noun"), "belongs to the tranches that have kinds"),
            (compound.Item1, NewBlock(NewId("compound-head", 1), compound.Item2, "target", "NOUN", status: "disputed"), "must not be disputed"),
            (headings.Item1, NewBlock(NewId("headings", 1), headings.Item2, "target", "NOUN"), "the comment 'kind' is missing"),
            (headings.Item1, NewBlock(NewId("headings", 1), headings.Item2, "target", "NOUN", "adjective"), "the kind 'adjective' is not one of noun, imperative, proper"),
            (headings.Item1, NewBlock(NewId("headings", 1), headings.Item2, "control", "NOUN", "noun"), "does not match the kind 'noun'"),
            (headings.Item1, NewBlock(NewId("headings", 1), headings.Item2, "target", "PROPN", "proper"), "does not match the kind 'proper'"),
            (headings.Item1, NewBlock(NewId("headings", 1), headings.Item2, "target", "VERB", "noun"), "must be tagged NOUN but is tagged VERB"),
            (headings.Item1, NewBlock(NewId("headings", 1), headings.Item2, "target", "NOUN", "imperative"), "must be tagged VERB but is tagged NOUN"),
            (headings.Item1, NewBlock(NewId("headings", 1), headings.Item2, "control", "NOUN", "proper"), "must be tagged PROPN but is tagged NOUN"),
            (headings.Item1, NewBlock(NewId("headings", 1), headings.Item2, "target", "NOUN", "noun", "disputed"), "must not be disputed"),
        };

        // Act / Assert
        foreach (var (tranche, block, expected) in cases)
        {
            var exception = Assert.Throws<InvalidDataException>(() => ShapesDevSets.Parse(TrancheFile(DevSets.ShapesDevNameOf(tranche), block), "fixture", tranche));
            Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
        }

        // Assert: the shape of a single-shape tranche is not a shape of the standard or the hard tranche, and the other way round
        var wrongTranche = Assert.Throws<InvalidDataException>(() => ShapesDevSets.Parse(CompoundHeadFile(), "fixture"));
        Assert.Contains("is not one of n-compound-head", wrongTranche.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => ShapesDevSets.Parse(File(CompoundHead()), "fixture", ShapesDevTranche.CompoundHead));
    }

    /// <summary>
    ///     Validates that each single-shape tranche is read from its own file with its own SHA-256 and marks its items, that the standard
    ///     tranche is still read next to them and that an absent file is known to be absent and refused when it is read.
    /// </summary>
    [Fact]
    public void DevSets_ReadShapesDev_NewTranches_ReadOwnFileAndMarkItems()
    {
        // Arrange: the standard file and the three new files; the hard file is absent
        var root = TempRoot(File(CompoundHead("shapes-dev-0001")));
        WriteShapes(root, "compound-head.tsv", CompoundHeadFile());
        WriteShapes(root, "headings.tsv", HeadingsFile());
        WriteShapes(root, "named-state.tsv", NamedStateFile());
        try
        {
            // Act
            var compound = DevSets.ReadShapesDev(root, ShapesDevTranche.CompoundHead, out var compoundSha);
            var headings = DevSets.ReadShapesDev(root, ShapesDevTranche.Headings, out var headingsSha);
            var named = DevSets.ReadShapesDev(root, ShapesDevTranche.NamedState, out var namedSha);

            // Assert
            Assert.Equal(new[] { NewId("compound-head", 1), NewId("compound-head", 2), NewId("compound-head", 3) }, compound.Select(i => i.Id));
            Assert.All(compound, i => Assert.Equal(ShapesDevTranche.CompoundHead, i.Tranche));
            Assert.All(headings, i => Assert.Equal(ShapesDevTranche.Headings, i.Tranche));
            Assert.All(named, i => Assert.Equal(ShapesDevTranche.NamedState, i.Tranche));
            Assert.Equal(ModelBuilder.Sha256Hex(System.IO.File.ReadAllBytes(Path.Combine(root, "data", "eval", "shapes-dev", "headings.tsv"))), headingsSha);
            Assert.Equal(3, new[] { compoundSha, headingsSha, namedSha }.Distinct().Count());
            Assert.Equal("data/eval/shapes-dev/named-state.tsv", DevSets.ShapesDevFileOf(ShapesDevTranche.NamedState));
            Assert.Single(DevSets.ReadShapesDev(root, ShapesDevTranche.Standard, out _));
            Assert.False(DevSets.ShapesDevExists(root, ShapesDevTranche.Hard));
            Assert.True(DevSets.ShapesDevExists(root, ShapesDevTranche.Headings));

            // Assert: removing a file makes it absent and its read an error
            System.IO.File.Delete(Path.Combine(root, "data", "eval", "shapes-dev", "headings.tsv"));
            Assert.False(DevSets.ShapesDevExists(root, ShapesDevTranche.Headings));
            Assert.Throws<FileNotFoundException>(() => DevSets.ReadShapesDev(root, ShapesDevTranche.Headings, out _));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    ///     Validates that a file of one tranche cannot be read as another: a corpus comment without the marker of the tranche, with the marker
    ///     of another tranche or with two markers, a header that is not a development split, and sentence ids of another tranche are refused.
    /// </summary>
    [Fact]
    public void DevSets_ReadShapesDev_NewTranches_FileOfAnotherTranche_Throws()
    {
        // Arrange
        var root = TempRoot(null);
        try
        {
            // Act / Assert: the headings file at the compound-head path, and the standard file at the headings path
            WriteShapes(root, "compound-head.tsv", HeadingsFile());
            Assert.Contains("announces itself as the headings tranche", Assert.Throws<InvalidDataException>(() => DevSets.ReadShapesDev(root, ShapesDevTranche.CompoundHead, out _)).Message);
            WriteShapes(root, "headings.tsv", File(CompoundHead(NewId("headings", 1))));
            Assert.Contains("does not announce itself as the headings tranche", Assert.Throws<InvalidDataException>(() => DevSets.ReadShapesDev(root, ShapesDevTranche.Headings, out _)).Message);

            // Assert: a hard-tranche or two-tranche header is not a header of a new tranche
            WriteShapes(root, "named-state.tsv", HardFile(NewBlock(NewId("named-state", 1), "n-named-state", "target", "NOUN")));
            Assert.Contains("announces itself as the hard tranche", Assert.Throws<InvalidDataException>(() => DevSets.ReadShapesDev(root, ShapesDevTranche.NamedState, out _)).Message);
            var two = NamedStateFile();
            two[0] = "# corpus = fixture hard-shape development set, named-state tranche and headings tranche, dev split";
            WriteShapes(root, "named-state.tsv", two);
            Assert.Contains("announces itself as the headings tranche", Assert.Throws<InvalidDataException>(() => DevSets.ReadShapesDev(root, ShapesDevTranche.NamedState, out _)).Message);

            // Assert: a new tranche's marker at the standard or the hard path
            WriteShapes(root, "dev.tsv", HeadingsFile());
            Assert.Contains("announces itself as the headings tranche", Assert.Throws<InvalidDataException>(() => DevSets.ReadShapesDev(root, ShapesDevTranche.Standard, out _)).Message);
            WriteShapes(root, "hard.tsv", HeadingsFile());
            Assert.Contains("announces itself as the headings tranche", Assert.Throws<InvalidDataException>(() => DevSets.ReadShapesDev(root, ShapesDevTranche.Hard, out _)).Message);

            // Assert: not a development split
            var held = NamedStateFile();
            held[0] = "# corpus = fixture hard-shape set, named-state tranche, test split";
            WriteShapes(root, "named-state.tsv", held);
            Assert.Contains("development split", Assert.Throws<InvalidDataException>(() => DevSets.ReadShapesDev(root, ShapesDevTranche.NamedState, out _)).Message);

            // Assert: right header, ids of another tranche
            WriteShapes(root, "named-state.tsv", TrancheFile("named-state", NewBlock(NewId("headings", 1), "n-named-state", "target", "NOUN")));
            Assert.Contains("sentence id", Assert.Throws<InvalidDataException>(() => DevSets.ReadShapesDev(root, ShapesDevTranche.NamedState, out _)).Message);
            WriteShapes(root, "named-state.tsv", TrancheFile("named-state", NewBlock("shapes-dev-0001", "n-named-state", "target", "NOUN")));
            Assert.Contains("sentence id", Assert.Throws<InvalidDataException>(() => DevSets.ReadShapesDev(root, ShapesDevTranche.NamedState, out _)).Message);
            WriteShapes(root, "named-state.tsv", TrancheFile("named-state", NewBlock("shapes-dev-hard-0001", "n-named-state", "target", "NOUN")));
            Assert.Contains("sentence id", Assert.Throws<InvalidDataException>(() => DevSets.ReadShapesDev(root, ShapesDevTranche.NamedState, out _)).Message);

            // Assert: ids of a new tranche at the standard path
            WriteShapes(root, "dev.tsv", File(CompoundHead(NewId("named-state", 1))));
            Assert.Contains("sentence id", Assert.Throws<InvalidDataException>(() => DevSets.ReadShapesDev(root, ShapesDevTranche.Standard, out _)).Message);

            // Assert: the correct file is accepted
            WriteShapes(root, "named-state.tsv", NamedStateFile());
            Assert.Equal(3, DevSets.ReadShapesDev(root, ShapesDevTranche.NamedState, out _).Count);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    ///     Validates that the shapes guard admits the files of the three new tranches (also through a dotted detour) and returns the file under the repository root.
    /// </summary>
    /// <param name="relativePath">The path relative to the repository root.</param>
    [Theory]
    [InlineData("data/eval/shapes-dev/compound-head.tsv")]
    [InlineData("data/eval/shapes-dev/headings.tsv")]
    [InlineData("data/eval/shapes-dev/named-state.tsv")]
    [InlineData("data/eval/shapes-dev/compound-head-2.tsv")]
    [InlineData("data/eval/shapes-dev/headings-2.tsv")]
    [InlineData("data/eval/shapes-dev/deverbal-heads.tsv")]
    [InlineData("data/eval/shapes-dev/defined-terms.tsv")]
    [InlineData("data/eval/shapes-dev/step-lines.tsv")]
    [InlineData("data/eval/shapes-dev/clause-verbs.tsv")]
    [InlineData("data/eval/shapes-dev/../shapes-dev/headings.tsv")]
    [InlineData("data/eval/shapes-dev/../shapes-dev/step-lines.tsv")]
    public void DataGuard_EnsureShapesDevelopmentFile_NewTrancheFile_Admitted(string relativePath)
    {
        // Arrange / Act
        var resolved = DataGuard.EnsureShapesDevelopmentFile(DataFiles.RepositoryRoot, DataFiles.Repository(relativePath));

        // Assert
        Assert.EndsWith(Path.Combine("data", "eval", "shapes-dev", Path.GetFileName(relativePath)), resolved, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(relativePath, DataGuard.DevelopmentFiles);
        Assert.Throws<InvalidOperationException>(() => DataGuard.EnsureDevelopmentFile(DataFiles.RepositoryRoot, DataFiles.Repository(relativePath)));
        Assert.Throws<InvalidOperationException>(() => DataGuard.EnsureTrainable(DataFiles.RepositoryRoot, DataFiles.Repository(relativePath)));
    }

    /// <summary>
    ///     Validates that the shapes guard still refuses by name every other file of the folder, among them the sources files and the
    ///     leakage reports of the three new tranches.
    /// </summary>
    /// <param name="relativePath">The path relative to the repository root.</param>
    [Theory]
    [InlineData("data/eval/shapes-dev/compound-head-sources.tsv")]
    [InlineData("data/eval/shapes-dev/headings-sources.tsv")]
    [InlineData("data/eval/shapes-dev/named-state-sources.tsv")]
    [InlineData("data/eval/shapes-dev/compound-head-leakage-report.tsv")]
    [InlineData("data/eval/shapes-dev/headings-leakage-report.tsv")]
    [InlineData("data/eval/shapes-dev/named-state-leakage-report.tsv")]
    [InlineData("data/eval/shapes-dev/compound-head-2-sources.tsv")]
    [InlineData("data/eval/shapes-dev/headings-2-sources.tsv")]
    [InlineData("data/eval/shapes-dev/compound-head-2-leakage-report.tsv")]
    [InlineData("data/eval/shapes-dev/headings-2-leakage-report.tsv")]
    [InlineData("data/eval/shapes-dev/deverbal-heads-sources.tsv")]
    [InlineData("data/eval/shapes-dev/defined-terms-sources.tsv")]
    [InlineData("data/eval/shapes-dev/step-lines-sources.tsv")]
    [InlineData("data/eval/shapes-dev/clause-verbs-sources.tsv")]
    [InlineData("data/eval/shapes-dev/deverbal-heads-leakage-report.tsv")]
    [InlineData("data/eval/shapes-dev/defined-terms-leakage-report.tsv")]
    [InlineData("data/eval/shapes-dev/step-lines-leakage-report.tsv")]
    [InlineData("data/eval/shapes-dev/clause-verbs-leakage-report.tsv")]
    [InlineData("data/eval/shapes-dev/step-lines.tsv.bak")]
    [InlineData("data/eval/shapes-dev/headings-2.tsv.bak")]
    [InlineData("data/eval/shapes-dev/headings.tsv.bak")]
    [InlineData("data/eval/shapes-dev/new.tsv")]
    public void DataGuard_EnsureShapesDevelopmentFile_NewTrancheSourcesAndLeakage_Refused(string relativePath)
    {
        // Arrange / Act / Assert
        Assert.Throws<InvalidOperationException>(() => DataGuard.EnsureShapesDevelopmentFile(DataFiles.RepositoryRoot, DataFiles.Repository(relativePath)));
    }

    /// <summary>
    ///     Validates that a confident wrong answer is a wrong target item with a confidence of at least 0.95: 0.95 counts, 0.949 does not, a
    ///     correct answer and a wrong control do not, and the count does not change the harmful errors.
    /// </summary>
    [Fact]
    public void ShapesDevTrancheEvaluator_Summarize_ConfidentWrong_CountsFromTheLimit()
    {
        // Arrange
        var tranche = ShapesDevTranche.CompoundHead;
        var items = new List<ShapesDevItem>
        {
            TrancheItem(tranche, "a", "target", "cat", "NOUN"),
            TrancheItem(tranche, "b", "target", "cat", "NOUN"),
            TrancheItem(tranche, "c", "target", "cat", "NOUN"),
            TrancheItem(tranche, "d", "target", "cat", "NOUN"),
            TrancheItem(tranche, "e", "control", "cat", "VERB"),
        };
        var outcomes = new List<Outcome>
        {
            new("d", false, 0.95, 0.05, true, true, true),
            new("d", false, 0.949, 0.05, true, true, true),
            new("d", true, 0.99, 0.99, true, true, false),
            new("d", false, 1.0, 0.0, true, false, false),
            new("d", false, 0.99, 0.01, true, true, true),
        };

        // Act
        var result = ShapesDevTrancheEvaluator.Summarize(tranche, items, outcomes);

        // Assert
        Assert.Equal(0.95, Metrics.ConfidentWrongConfidence);
        Assert.Equal(2, result.Overall.ConfidentWrong);
        Assert.Equal((4, 1, 3, 2, 1, 0), (result.Overall.TargetItems, result.Overall.TargetCorrect, result.Overall.NounVerbDecisions, result.Overall.HarmfulErrors, result.Overall.ControlItems, result.Overall.ControlCorrect));
        Assert.Empty(result.Kinds);
        Assert.True(Metrics.IsConfidentlyWrong(outcomes[0]));
        Assert.False(Metrics.IsConfidentlyWrong(outcomes[1]));
        Assert.False(Metrics.IsConfidentlyWrong(outcomes[2]));
    }

    /// <summary>
    ///     Validates the exact-tag scoring of the headings through the real decoder: a capitalized common noun that is tagged PROPN is wrong
    ///     (and not harmful) although both are class Noun, the same answer is right under class scoring, a noun-verb swap is harmful, and a
    ///     PROPN control that is answered with another tag is wrong and never harmful.
    /// </summary>
    [Fact]
    public void ShapesDevTrancheEvaluator_Evaluate_Headings_ScoredByExactTag()
    {
        // Arrange: "cat" is answered PROPN (NOUN is the other candidate), "dog" is answered VERB (NOUN is the other candidate)
        var tagger = PropnCatVerbDogTagger();
        var items = new List<ShapesDevItem>
        {
            TrancheItem(ShapesDevTranche.Headings, "a", "target", "cat", "NOUN", "noun"),
            TrancheItem(ShapesDevTranche.Headings, "b", "target", "dog", "NOUN", "noun"),
            TrancheItem(ShapesDevTranche.Headings, "c", "target", "dog", "VERB", "imperative"),
            TrancheItem(ShapesDevTranche.Headings, "d", "control", "cat", "PROPN", "proper"),
            TrancheItem(ShapesDevTranche.Headings, "e", "control", "dog", "PROPN", "proper"),
        };
        var asClass = new List<ShapesDevItem> { TrancheItem(ShapesDevTranche.CompoundHead, "f", "target", "cat", "NOUN") };

        // Act
        var result = ShapesDevTrancheEvaluator.Evaluate(tagger, ShapesDevTranche.Headings, items);
        var classResult = ShapesDevTrancheEvaluator.Evaluate(tagger, ShapesDevTranche.CompoundHead, asClass);

        // Assert: the PROPN answer for the noun heading is wrong, and not a noun-verb decision, so not harmful
        Assert.Equal(("heading-titlecase", 3, 1, 2, 1, 2, 1), (result.Overall.Shape, result.Overall.TargetItems, result.Overall.TargetCorrect, result.Overall.NounVerbDecisions, result.Overall.HarmfulErrors, result.Overall.ControlItems, result.Overall.ControlCorrect));
        Assert.Equal(
            new[] { "heading-titlecase: noun targets", "heading-titlecase: imperative targets", "heading-titlecase: PROPN controls" },
            result.Kinds.Select(k => k.Shape));
        Assert.Equal((2, 0, 1, 1), (result.Kinds[0].TargetItems, result.Kinds[0].TargetCorrect, result.Kinds[0].NounVerbDecisions, result.Kinds[0].HarmfulErrors));
        Assert.Equal((1, 1, 1, 0), (result.Kinds[1].TargetItems, result.Kinds[1].TargetCorrect, result.Kinds[1].NounVerbDecisions, result.Kinds[1].HarmfulErrors));
        Assert.Equal((0, 2, 1, 0), (result.Kinds[2].TargetItems, result.Kinds[2].ControlItems, result.Kinds[2].ControlCorrect, result.Kinds[2].HarmfulErrors));

        // Assert: the same PROPN answer for a NOUN gold is right when the shape is scored by class
        Assert.Equal((1, 1, 0), (classResult.Overall.TargetItems, classResult.Overall.TargetCorrect, classResult.Overall.HarmfulErrors));
    }

    /// <summary>
    ///     Validates that the summary refuses a tranche that has no single shape, lists of different lengths and items of another tranche or shape.
    /// </summary>
    [Fact]
    public void ShapesDevTrancheEvaluator_Summarize_InconsistentInput_Throws()
    {
        // Arrange
        var item = TrancheItem(ShapesDevTranche.NamedState, "a", "target", "cat", "NOUN");
        var outcome = new Outcome("d", true, 0.9, 0.9, true, true, false);
        var otherShape = new ShapesDevItem("b", "doc-a", new[] { "cat" }, 0, "Noun", "n-compound-head", "target", "cat", "real", ShapesDevTranche.NamedState, string.Empty, "NOUN");

        // Act / Assert
        Assert.Throws<ArgumentException>(() => ShapesDevTrancheEvaluator.Summarize(ShapesDevTranche.Standard, new[] { item }, new[] { outcome }));
        Assert.Throws<ArgumentException>(() => ShapesDevTrancheEvaluator.Summarize(ShapesDevTranche.NamedState, new[] { item }, Array.Empty<Outcome>()));
        Assert.Throws<ArgumentException>(() => ShapesDevTrancheEvaluator.Summarize(ShapesDevTranche.Headings, new[] { item }, new[] { outcome }));
        Assert.Throws<ArgumentException>(() => ShapesDevTrancheEvaluator.Summarize(ShapesDevTranche.NamedState, new[] { otherShape }, new[] { outcome }));
        Assert.Throws<ArgumentException>(() => ShapesDevTrancheEvaluator.Judge(PropnCatVerbDogTagger(), new[] { Item("x", "n-measure", "target", "cat", "Noun") }));
        Assert.Equal(0, ShapesDevTrancheEvaluator.Summarize(ShapesDevTranche.NamedState, Array.Empty<ShapesDevItem>(), Array.Empty<Outcome>()).Overall.TargetItems);
    }

    /// <summary>
    ///     Validates the section of a class-scored tranche: heading, counts, hashes, scoring note and the table with the confident-wrong column,
    ///     rendered identically twice.
    /// </summary>
    [Fact]
    public void ShapesDevReport_RenderTranche_CompoundHead_PrintsSectionAndTable()
    {
        // Arrange: three targets (one correct, one harmful and confident, one wrong but not confident) and one control
        var tranche = ShapesDevTranche.CompoundHead;
        var items = new List<ShapesDevItem>
        {
            TrancheItem(tranche, "a", "target", "cat", "NOUN"),
            TrancheItem(tranche, "b", "target", "cat", "NOUN"),
            TrancheItem(tranche, "c", "target", "cat", "PROPN"),
            TrancheItem(tranche, "d", "control", "cat", "VERB"),
        };
        var outcomes = new List<Outcome>
        {
            new("d", true, 0.9, 0.9, true, true, false),
            new("d", false, 0.96, 0.04, true, true, true),
            new("d", false, 0.6, 0.4, true, true, true),
            new("d", true, 0.9, 0.9, true, true, false),
        };
        var result = ShapesDevTrancheEvaluator.Summarize(tranche, items, outcomes);

        // Act
        var section = ShapesDevReport.RenderTranche("model.bin", tranche, new string('a', 64), result, new string('b', 64));
        var again = ShapesDevReport.RenderTranche("model.bin", tranche, new string('a', 64), ShapesDevTrancheEvaluator.Summarize(tranche, items, outcomes), new string('b', 64));

        // Assert
        Assert.Equal(section, again);
        Assert.Equal(
            new[]
            {
                "## Shapes development set, compound-head tranche: model.bin",
                string.Empty,
                "- Items: 3 target items and 1 control items; authored: 0 target items (0.0%) and 0 control items (0.0%).",
                $"- Development file SHA-256: `{new string('a', 64)}`.",
                $"- Weights file SHA-256: `{new string('b', 64)}`.",
                "- Scoring: the judged word of every item through the real decoder with all alternatives, by class (Noun: NOUN and PROPN; Verb: VERB); a noun-verb decision is a target whose candidates include both classes; a harmful error is a swapped class with a confidence of at least 0.45; wrong at confidence >= 0.95 counts the target items answered wrongly with a confidence of at least 0.95.",
                "- The set is for development: it selects settings and is never a held-out figure.",
                string.Empty,
                "| shape | target items | target accuracy | 95% interval | noun-verb decisions | harmful errors | harmful per 1,000 decisions | wrong at confidence >= 0.95 | control items | control accuracy |",
                "| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |",
                "| n-compound-head-ambiguous | 3 | 33.3% | [6.1%, 79.2%] | 3 | 2 | 666.7 | 1 | 1 | 100.0% |",
            },
            section);
    }

    /// <summary>
    ///     Validates the section of the headings tranche: its scoring note names the exact tag, and the rows of the noun targets, the
    ///     imperative targets and the PROPN controls follow the row of the shape, with dashes where a column does not apply.
    /// </summary>
    [Fact]
    public void ShapesDevReport_RenderTranche_Headings_PrintsRowsOfTheKinds()
    {
        // Arrange
        var tranche = ShapesDevTranche.Headings;
        var items = new List<ShapesDevItem>
        {
            TrancheItem(tranche, "a", "target", "cat", "NOUN", "noun"),
            TrancheItem(tranche, "b", "target", "dog", "VERB", "imperative"),
            TrancheItem(tranche, "c", "control", "cat", "PROPN", "proper"),
        };
        var outcomes = new List<Outcome>
        {
            new("d", false, 0.97, 0.03, true, false, false),
            new("d", true, 0.9, 0.9, true, true, false),
            new("d", true, 0.9, 0.9, true, false, false),
        };

        // Act
        var section = ShapesDevReport.RenderTranche("m", tranche, string.Empty, ShapesDevTrancheEvaluator.Summarize(tranche, items, outcomes));

        // Assert
        Assert.Equal("## Shapes development set, headings tranche: m", section[0]);
        Assert.Contains(section, l => l.Contains("by exact tag (a NOUN or VERB heading answered PROPN is wrong, but it is not harmful", StringComparison.Ordinal));
        Assert.Equal(
            new[]
            {
                "| heading-titlecase | 2 | 50.0% | [9.5%, 90.5%] | 1 | 0 | 0.0 | 1 | 1 | 100.0% |",
                "| heading-titlecase: noun targets | 1 | 0.0% | [0.0%, 79.3%] | 0 | 0 | - | 1 | - | - |",
                "| heading-titlecase: imperative targets | 1 | 100.0% | [20.7%, 100.0%] | 1 | 0 | 0.0 | 0 | - | - |",
                "| heading-titlecase: PROPN controls | - | - | - | - | - | - | - | 1 | 100.0% |",
            },
            section.Skip(section.Count - 4));
    }

    /// <summary>
    ///     Validates that a tranche without items renders dashes in every column of its row.
    /// </summary>
    [Fact]
    public void ShapesDevReport_RenderTranche_NoItems_PrintsDashes()
    {
        // Arrange / Act
        var section = ShapesDevReport.RenderTranche("m", ShapesDevTranche.NamedState, string.Empty, ShapesDevTrancheEvaluator.Summarize(ShapesDevTranche.NamedState, Array.Empty<ShapesDevItem>(), Array.Empty<Outcome>()));

        // Assert
        Assert.Equal("| n-named-state | - | - | - | - | - | - | - | - | - |", section[^1]);
        Assert.Equal("- The named-state tranche data/eval/shapes-dev/named-state.tsv is not present; it is not scored.", ShapesDevReport.RenderTrancheMissing(ShapesDevTranche.NamedState));
    }

    /// <summary>
    ///     Validates the values of <c>--tranche</c>: the original three and the four new ones are accepted, anything else is refused.
    /// </summary>
    /// <param name="which">The value of the option.</param>
    /// <param name="valid">Whether it is accepted.</param>
    [Theory]
    [InlineData("standard", true)]
    [InlineData("hard", true)]
    [InlineData("both", true)]
    [InlineData("compound-head", true)]
    [InlineData("headings", true)]
    [InlineData("named-state", true)]
    [InlineData("new", true)]
    [InlineData("compound-head-2", true)]
    [InlineData("headings-2", true)]
    [InlineData("followup", true)]
    [InlineData("deverbal-heads", true)]
    [InlineData("defined-terms", true)]
    [InlineData("step-lines", true)]
    [InlineData("clause-verbs", true)]
    [InlineData("technical", true)]
    [InlineData("all-single", true)]
    [InlineData("Technical", false)]
    [InlineData("named-state-2", false)]
    [InlineData("all", false)]
    [InlineData("Headings", false)]
    [InlineData("", false)]
    public void ShapesDevCommand_EnsureSelection_Value_AcceptedOrRefused(string which, bool valid)
    {
        // Arrange / Act / Assert
        if (valid)
        {
            ShapesDevCommand.EnsureSelection(which);
        }
        else
        {
            Assert.Contains("--tranche must be", Assert.Throws<InvalidOperationException>(() => ShapesDevCommand.EnsureSelection(which)).Message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    ///     Validates that an explicit single-shape tranche is scored alone, and that its absent file is an error that names the file.
    /// </summary>
    [Fact]
    public void ShapesDevCommand_Run_ExplicitNewTranche_ScoresItAloneAndMissingFileIsAnError()
    {
        // Arrange: only the headings file exists
        var root = TempRoot(null);
        WriteShapes(root, "headings.tsv", HeadingsFile());
        var tagger = FixtureTagger();
        try
        {
            // Act
            var lines = ShapesDevCommand.Run(root, tagger, "m", "w", "headings");
            var items = DevSets.ReadShapesDev(root, ShapesDevTranche.Headings, out var sha);

            // Assert: exactly the section of the renderer, and no other tranche
            Assert.Equal(ShapesDevReport.RenderTranche("m", ShapesDevTranche.Headings, sha, ShapesDevTrancheEvaluator.Evaluate(tagger, ShapesDevTranche.Headings, items), "w"), lines);
            Assert.Single(lines, l => l.StartsWith("## ", StringComparison.Ordinal));

            // Assert: an absent file of an explicit tranche is an error that names the file
            foreach (var name in new[] { "compound-head", "named-state" })
            {
                var error = Assert.Throws<FileNotFoundException>(() => ShapesDevCommand.Run(root, tagger, "m", "w", name));
                Assert.Contains(name + ".tsv", error.Message, StringComparison.Ordinal);
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    ///     Validates that <c>new</c> scores the three tranches in the order compound-head, headings, named-state, one section each separated
    ///     by an empty line, and that an absent file is noted in its place instead of being an error.
    /// </summary>
    [Fact]
    public void ShapesDevCommand_Run_New_ScoresPresentTranchesAndNotesMissingFiles()
    {
        // Arrange: the compound-head file is absent
        var root = TempRoot(null);
        WriteShapes(root, "headings.tsv", HeadingsFile());
        WriteShapes(root, "named-state.tsv", NamedStateFile());
        var tagger = FixtureTagger();
        try
        {
            // Act
            var lines = ShapesDevCommand.Run(root, tagger, "m", "w", "new");

            // Assert
            var headings = DevSets.ReadShapesDev(root, ShapesDevTranche.Headings, out var headingsSha);
            var named = DevSets.ReadShapesDev(root, ShapesDevTranche.NamedState, out var namedSha);
            var expected = new List<string> { ShapesDevReport.RenderTrancheMissing(ShapesDevTranche.CompoundHead), string.Empty };
            expected.AddRange(ShapesDevReport.RenderTranche("m", ShapesDevTranche.Headings, headingsSha, ShapesDevTrancheEvaluator.Evaluate(tagger, ShapesDevTranche.Headings, headings), "w"));
            expected.Add(string.Empty);
            expected.AddRange(ShapesDevReport.RenderTranche("m", ShapesDevTranche.NamedState, namedSha, ShapesDevTrancheEvaluator.Evaluate(tagger, ShapesDevTranche.NamedState, named), "w"));
            Assert.Equal(expected, lines);

            // Assert: with no file at all every tranche is noted
            Directory.Delete(Path.Combine(root, "data", "eval", "shapes-dev"), true);
            Directory.CreateDirectory(Path.Combine(root, "data", "eval", "shapes-dev"));
            var none = ShapesDevCommand.Run(root, tagger, "m", "w", "new");
            Assert.Equal(5, none.Count);
            Assert.Equal(new[] { ShapesDevTranche.CompoundHead, ShapesDevTranche.Headings, ShapesDevTranche.NamedState }.Select(ShapesDevReport.RenderTrancheMissing), none.Where(l => l.Length > 0));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    ///     Validates that the original selections write the sections they always wrote and are not changed by the presence of the new
    ///     files: <c>both</c> is the three sections of the standard and hard tranche (with the note when the hard file is absent), and
    ///     <c>standard</c> and <c>hard</c> are the plain and the hard-only report.
    /// </summary>
    [Fact]
    public void ShapesDevCommand_Run_StandardHardBoth_OutputIsUnchangedByTheNewFiles()
    {
        // Arrange: the standard and the hard file, and the three new files next to them
        var root = TempRoot(File(CompoundHead("shapes-dev-0001")));
        var tagger = FixtureTagger();
        var items = DevSets.ReadShapesDev(root, ShapesDevTranche.Standard, out var standardSha);
        var standardOnly = ShapesDevEvaluator.EvaluateTranches(tagger, items);
        try
        {
            // Act / Assert: without the hard file, "both" is the plain section and the note
            var withoutHard = ShapesDevCommand.Run(root, tagger, "m", "w", "both");
            var expectedNote = ShapesDevReport.Render("m", standardSha, standardOnly.Standard, "w");
            expectedNote.Add(string.Empty);
            expectedNote.Add("- The hard tranche data/eval/shapes-dev/hard.tsv is not present; only the standard tranche is scored.");
            Assert.Equal(expectedNote, withoutHard);
            Assert.Equal(ShapesDevReport.Render("m", standardSha, standardOnly.Standard, "w"), ShapesDevCommand.Run(root, tagger, "m", "w", "standard"));
            Assert.Throws<FileNotFoundException>(() => ShapesDevCommand.Run(root, tagger, "m", "w", "hard"));

            // Act / Assert: with the hard file and the new files, "both" is the three sections of the renderer
            WriteShapes(root, "hard.tsv", HardFile(CompoundHead("shapes-dev-hard-0001")));
            WriteShapes(root, "compound-head.tsv", CompoundHeadFile());
            WriteShapes(root, "headings.tsv", HeadingsFile());
            WriteShapes(root, "named-state.tsv", NamedStateFile());
            var hardItems = DevSets.ReadShapesDev(root, ShapesDevTranche.Hard, out var hardSha);
            var both = ShapesDevEvaluator.EvaluateTranches(tagger, items.Concat(hardItems).ToList());
            Assert.Equal(ShapesDevReport.RenderTranches("m", standardSha, hardSha, both, "w"), ShapesDevCommand.Run(root, tagger, "m", "w", "both"));
            Assert.Equal(ShapesDevReport.RenderHard("m", hardSha, both.Hard, "w"), ShapesDevCommand.Run(root, tagger, "m", "w", "hard"));
            Assert.Equal(ShapesDevReport.Render("m", standardSha, both.Standard, "w"), ShapesDevCommand.Run(root, tagger, "m", "w", "standard"));
            Assert.DoesNotContain(ShapesDevCommand.Run(root, tagger, "m", "w", "both"), l => l.Contains("compound-head tranche", StringComparison.Ordinal) || l.Contains("headings tranche", StringComparison.Ordinal) || l.Contains("named-state tranche", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    ///     A valid file of the compound-head-2 tranche (the shape of the compound-head tranche, ids with the infix <c>compound-head-2</c>).
    /// </summary>
    private static List<string> CompoundHead2File() => TrancheFile(
        "compound-head-2",
        NewBlock(NewId("compound-head-2", 1), "n-compound-head-ambiguous", "target", "NOUN"),
        NewBlock(NewId("compound-head-2", 2), "n-compound-head-ambiguous", "target", "PROPN"),
        NewBlock(NewId("compound-head-2", 3), "n-compound-head-ambiguous", "control", "VERB"));

    /// <summary>
    ///     A valid file of the headings-2 tranche (the shape of the headings tranche, ids with the infix <c>headings-2</c>).
    /// </summary>
    private static List<string> Headings2File() => TrancheFile(
        "headings-2",
        NewBlock(NewId("headings-2", 1), "heading-titlecase", "target", "NOUN", "noun"),
        NewBlock(NewId("headings-2", 2), "heading-titlecase", "target", "VERB", "imperative"),
        NewBlock(NewId("headings-2", 3), "heading-titlecase", "control", "PROPN", "proper"));

    /// <summary>
    ///     Validates that valid files of the two follow-up tranches are read with the shape, roles, kinds and gold tags of their first tranches,
    ///     that each item carries its own tranche, and that the two tranches have a specification of their own with the shape of the first.
    /// </summary>
    [Fact]
    public void ShapesDevSets_Parse_FollowupTranches_ValidFiles_ReturnItemsOfTheirOwnTranche()
    {
        // Arrange / Act
        var compound = ShapesDevSets.Parse(CompoundHead2File(), "fixture", ShapesDevTranche.CompoundHead2);
        var headings = ShapesDevSets.Parse(Headings2File(), "fixture", ShapesDevTranche.Headings2);

        // Assert: same shapes, roles, tags and kinds as the first tranches, own tranche
        Assert.Equal(new[] { "NOUN", "PROPN", "VERB" }, compound.Select(i => i.GoldTag));
        Assert.Equal(new[] { "target", "target", "control" }, compound.Select(i => i.Role));
        Assert.All(compound, i => Assert.Equal(("n-compound-head-ambiguous", ShapesDevTranche.CompoundHead2, string.Empty), (i.Shape, i.Tranche, i.Kind)));
        Assert.Equal(new[] { "noun", "imperative", "proper" }, headings.Select(i => i.Kind));
        Assert.Equal(new[] { "NOUN", "VERB", "PROPN" }, headings.Select(i => i.GoldTag));
        Assert.Equal(new[] { true, true, false }, headings.Select(i => i.IsTarget));
        Assert.All(headings, i => Assert.Equal(("heading-titlecase", ShapesDevTranche.Headings2), (i.Shape, i.Tranche)));

        // Assert: the specifications of the follow-up tranches repeat those of the first tranches (shape, scoring, tags, kinds)
        foreach (var (first, second) in new[] { (ShapesDevTranche.CompoundHead, ShapesDevTranche.CompoundHead2), (ShapesDevTranche.Headings, ShapesDevTranche.Headings2) })
        {
            var a = ShapesDevSets.SpecOf(first)!;
            var b = ShapesDevSets.SpecOf(second)!;
            Assert.NotSame(a, b);
            Assert.Equal((a.Shape, a.Scoring), (b.Shape, b.Scoring));
            Assert.Equal(a.TargetTags, b.TargetTags);
            Assert.Equal(a.ControlTags, b.ControlTags);
            Assert.Equal(a.Kinds.Select(k => (k.Name, k.Role, k.Tag)), b.Kinds.Select(k => (k.Name, k.Role, k.Tag)));
            Assert.Equal(second, b.Tranche);
            Assert.Same(a, ShapesDevSets.SpecOf(a.Shape));
        }

        // Assert: the rules of the first tranches hold for the follow-up tranches
        var disputed = Assert.Throws<InvalidDataException>(() => ShapesDevSets.Parse(TrancheFile("compound-head-2", NewBlock(NewId("compound-head-2", 1), "n-compound-head-ambiguous", "target", "NOUN", status: "disputed")), "fixture", ShapesDevTranche.CompoundHead2));
        Assert.Contains("must not be disputed", disputed.Message, StringComparison.Ordinal);
        var noKind = Assert.Throws<InvalidDataException>(() => ShapesDevSets.Parse(TrancheFile("headings-2", NewBlock(NewId("headings-2", 1), "heading-titlecase", "target", "NOUN")), "fixture", ShapesDevTranche.Headings2));
        Assert.Contains("the comment 'kind' is missing", noKind.Message, StringComparison.Ordinal);
        var kindOnCompound = Assert.Throws<InvalidDataException>(() => ShapesDevSets.Parse(TrancheFile("compound-head-2", NewBlock(NewId("compound-head-2", 1), "n-compound-head-ambiguous", "target", "NOUN", "noun")), "fixture", ShapesDevTranche.CompoundHead2));
        Assert.Contains("belongs to the tranches that have kinds", kindOnCompound.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Validates that each follow-up tranche is read from its own file with its own SHA-256, that the first tranche of the same shape is read
    ///     from its file next to it, and that a file cannot be read as the other tranche of its shape: the header marker and the id prefix of the
    ///     second tranche (which starts with the prefix of the first) are told apart.
    /// </summary>
    [Fact]
    public void DevSets_ReadShapesDev_FollowupTranches_OwnFileOwnMarkerAndLongestIdPrefix()
    {
        // Arrange: the first and the second tranches of both shapes
        var root = TempRoot(null);
        WriteShapes(root, "compound-head.tsv", CompoundHeadFile());
        WriteShapes(root, "compound-head-2.tsv", CompoundHead2File());
        WriteShapes(root, "headings.tsv", HeadingsFile());
        WriteShapes(root, "headings-2.tsv", Headings2File());
        try
        {
            // Act
            var compound = DevSets.ReadShapesDev(root, ShapesDevTranche.CompoundHead, out var compoundSha);
            var compound2 = DevSets.ReadShapesDev(root, ShapesDevTranche.CompoundHead2, out var compound2Sha);
            var headings = DevSets.ReadShapesDev(root, ShapesDevTranche.Headings, out var headingsSha);
            var headings2 = DevSets.ReadShapesDev(root, ShapesDevTranche.Headings2, out var headings2Sha);

            // Assert: each file marks its items with its own tranche and holds its own ids
            Assert.All(compound, i => Assert.Equal(ShapesDevTranche.CompoundHead, i.Tranche));
            Assert.All(compound2, i => Assert.Equal(ShapesDevTranche.CompoundHead2, i.Tranche));
            Assert.All(headings, i => Assert.Equal(ShapesDevTranche.Headings, i.Tranche));
            Assert.All(headings2, i => Assert.Equal(ShapesDevTranche.Headings2, i.Tranche));
            Assert.Equal(new[] { NewId("compound-head-2", 1), NewId("compound-head-2", 2), NewId("compound-head-2", 3) }, compound2.Select(i => i.Id));
            Assert.Equal(new[] { NewId("headings-2", 1), NewId("headings-2", 2), NewId("headings-2", 3) }, headings2.Select(i => i.Id));
            Assert.Equal(4, new[] { compoundSha, compound2Sha, headingsSha, headings2Sha }.Distinct().Count());
            Assert.Equal("compound-head-2", DevSets.ShapesDevNameOf(ShapesDevTranche.CompoundHead2));
            Assert.Equal("headings-2", DevSets.ShapesDevNameOf(ShapesDevTranche.Headings2));
            Assert.Equal("data/eval/shapes-dev/compound-head-2.tsv", DevSets.ShapesDevFileOf(ShapesDevTranche.CompoundHead2));
            Assert.Equal("data/eval/shapes-dev/headings-2.tsv", DevSets.ShapesDevFileOf(ShapesDevTranche.Headings2));
            Assert.True(DevSets.ShapesDevExists(root, ShapesDevTranche.CompoundHead2));

            // Assert: a file of the second tranche at the path of the first (marker and ids), and the other way round, is refused
            WriteShapes(root, "compound-head.tsv", CompoundHead2File());
            Assert.Contains("announces itself as the compound-head-2 tranche", Assert.Throws<InvalidDataException>(() => DevSets.ReadShapesDev(root, ShapesDevTranche.CompoundHead, out _)).Message);
            WriteShapes(root, "compound-head-2.tsv", CompoundHeadFile());
            Assert.Contains("announces itself as the compound-head tranche", Assert.Throws<InvalidDataException>(() => DevSets.ReadShapesDev(root, ShapesDevTranche.CompoundHead2, out _)).Message);
            WriteShapes(root, "headings.tsv", Headings2File());
            Assert.Contains("announces itself as the headings-2 tranche", Assert.Throws<InvalidDataException>(() => DevSets.ReadShapesDev(root, ShapesDevTranche.Headings, out _)).Message);
            WriteShapes(root, "headings-2.tsv", HeadingsFile());
            Assert.Contains("announces itself as the headings tranche", Assert.Throws<InvalidDataException>(() => DevSets.ReadShapesDev(root, ShapesDevTranche.Headings2, out _)).Message);

            // Assert: the right marker with the ids of the other tranche of the shape is refused (the longer prefix decides)
            WriteShapes(root, "compound-head.tsv", TrancheFile("compound-head", NewBlock(NewId("compound-head-2", 1), "n-compound-head-ambiguous", "target", "NOUN")));
            Assert.Contains("sentence id", Assert.Throws<InvalidDataException>(() => DevSets.ReadShapesDev(root, ShapesDevTranche.CompoundHead, out _)).Message);
            WriteShapes(root, "compound-head-2.tsv", TrancheFile("compound-head-2", NewBlock(NewId("compound-head", 1), "n-compound-head-ambiguous", "target", "NOUN")));
            Assert.Contains("sentence id", Assert.Throws<InvalidDataException>(() => DevSets.ReadShapesDev(root, ShapesDevTranche.CompoundHead2, out _)).Message);
            WriteShapes(root, "headings.tsv", TrancheFile("headings", NewBlock(NewId("headings-2", 1), "heading-titlecase", "target", "NOUN", "noun")));
            Assert.Contains("sentence id", Assert.Throws<InvalidDataException>(() => DevSets.ReadShapesDev(root, ShapesDevTranche.Headings, out _)).Message);
            WriteShapes(root, "headings-2.tsv", TrancheFile("headings-2", NewBlock(NewId("headings", 1), "heading-titlecase", "target", "NOUN", "noun")));
            Assert.Contains("sentence id", Assert.Throws<InvalidDataException>(() => DevSets.ReadShapesDev(root, ShapesDevTranche.Headings2, out _)).Message);

            // Assert: removing a file makes it absent and its read an error
            System.IO.File.Delete(Path.Combine(root, "data", "eval", "shapes-dev", "headings-2.tsv"));
            Assert.False(DevSets.ShapesDevExists(root, ShapesDevTranche.Headings2));
            Assert.Throws<FileNotFoundException>(() => DevSets.ReadShapesDev(root, ShapesDevTranche.Headings2, out _));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    ///     Validates that <c>followup</c> scores the two follow-up tranches in the order compound-head-2, headings-2 (a section each, separated
    ///     by an empty line, an absent file noted in its place), that an explicit follow-up tranche is scored alone and needs its file, and that
    ///     <c>new</c> does not include them, so the output of <c>new</c> does not change.
    /// </summary>
    [Fact]
    public void ShapesDevCommand_Run_Followup_ScoresBothTranchesAndNewDoesNotIncludeThem()
    {
        // Arrange: only the headings-2 file exists of the follow-up, next to the three files of the first generation
        var root = TempRoot(null);
        WriteShapes(root, "compound-head.tsv", CompoundHeadFile());
        WriteShapes(root, "headings.tsv", HeadingsFile());
        WriteShapes(root, "named-state.tsv", NamedStateFile());
        WriteShapes(root, "headings-2.tsv", Headings2File());
        var tagger = FixtureTagger();
        try
        {
            // Act
            var lines = ShapesDevCommand.Run(root, tagger, "m", "w", "followup");
            var headings2 = DevSets.ReadShapesDev(root, ShapesDevTranche.Headings2, out var sha);

            // Assert: the missing file is noted, the present one scored with the renderer of the single-shape tranches
            var expected = new List<string> { ShapesDevReport.RenderTrancheMissing(ShapesDevTranche.CompoundHead2), string.Empty };
            expected.AddRange(ShapesDevReport.RenderTranche("m", ShapesDevTranche.Headings2, sha, ShapesDevTrancheEvaluator.Evaluate(tagger, ShapesDevTranche.Headings2, headings2), "w"));
            Assert.Equal(expected, lines);
            Assert.Contains("## Shapes development set, headings-2 tranche: m", lines);
            Assert.Equal("- The compound-head-2 tranche data/eval/shapes-dev/compound-head-2.tsv is not present; it is not scored.", lines[0]);

            // Assert: an explicit follow-up tranche is scored alone, and its absent file is an error that names the file
            var alone = ShapesDevCommand.Run(root, tagger, "m", "w", "headings-2");
            Assert.Equal(ShapesDevReport.RenderTranche("m", ShapesDevTranche.Headings2, sha, ShapesDevTrancheEvaluator.Evaluate(tagger, ShapesDevTranche.Headings2, headings2), "w"), alone);
            Assert.Contains("compound-head-2.tsv", Assert.Throws<FileNotFoundException>(() => ShapesDevCommand.Run(root, tagger, "m", "w", "compound-head-2")).Message, StringComparison.Ordinal);

            // Assert: "new" scores the three first-generation tranches only (the follow-up files are present but not selected)
            var firstGeneration = ShapesDevCommand.Run(root, tagger, "m", "w", "new");
            Assert.Equal(3, firstGeneration.Count(l => l.StartsWith("## ", StringComparison.Ordinal)));
            Assert.DoesNotContain(firstGeneration, l => l.Contains("headings-2", StringComparison.Ordinal) || l.Contains("compound-head-2", StringComparison.Ordinal));

            // Assert: with both files both sections come, in order
            WriteShapes(root, "compound-head-2.tsv", CompoundHead2File());
            var both = ShapesDevCommand.Run(root, tagger, "m", "w", "followup");
            Assert.Equal(new[] { "## Shapes development set, compound-head-2 tranche: m", "## Shapes development set, headings-2 tranche: m" }, both.Where(l => l.StartsWith("## ", StringComparison.Ordinal)));
            var compound2 = DevSets.ReadShapesDev(root, ShapesDevTranche.CompoundHead2, out var compound2Sha);
            var expectedBoth = ShapesDevReport.RenderTranche("m", ShapesDevTranche.CompoundHead2, compound2Sha, ShapesDevTrancheEvaluator.Evaluate(tagger, ShapesDevTranche.CompoundHead2, compound2), "w").ToList();
            expectedBoth.Add(string.Empty);
            expectedBoth.AddRange(ShapesDevReport.RenderTranche("m", ShapesDevTranche.Headings2, sha, ShapesDevTrancheEvaluator.Evaluate(tagger, ShapesDevTranche.Headings2, headings2), "w"));
            Assert.Equal(expectedBoth, both);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    ///     Adds comments (<c>key = value</c>) to a block, behind its kind or origin comment.
    /// </summary>
    private static string[] WithComments(string[] block, params string[] comments)
    {
        var lines = block.ToList();
        var at = lines.FindLastIndex(l => l.StartsWith("# ", StringComparison.Ordinal)) + 1;
        lines.InsertRange(at, comments.Select(c => "# " + c));
        return lines.ToArray();
    }

    /// <summary>
    ///     A valid file of the deverbal-heads tranche: the four target kinds (NOUN) and the verb control (VERB), with the comments of the committed file.
    /// </summary>
    private static List<string> DeverbalHeadsFile() => TrancheFile(
        "deverbal-heads",
        NewBlock(NewId("deverbal-heads", 1), "n-deverbal-head", "target", "NOUN", "pred"),
        NewBlock(NewId("deverbal-heads", 2), "n-deverbal-head", "target", "NOUN", "num"),
        NewBlock(NewId("deverbal-heads", 3), "n-deverbal-head", "target", "NOUN", "ing"),
        NewBlock(NewId("deverbal-heads", 4), "n-deverbal-head", "target", "NOUN", "other"),
        NewBlock(NewId("deverbal-heads", 5), "n-deverbal-head", "control", "VERB", "verb"));

    /// <summary>
    ///     A valid file of the defined-terms tranche: a noun and an adjective target and a proper-name control; every item has the <c>term</c> comment.
    /// </summary>
    private static List<string> DefinedTermsFile() => TrancheFile(
        "defined-terms",
        WithComments(NewBlock(NewId("defined-terms", 1), "term-capitalized", "target", "NOUN", "noun"), "term = cat"),
        WithComments(NewBlock(NewId("defined-terms", 2), "term-capitalized", "target", "ADJ", "adj"), "term = cat"),
        WithComments(NewBlock(NewId("defined-terms", 3), "term-capitalized", "control", "PROPN", "proper"), "term = cat"));

    /// <summary>
    ///     A valid file of the step-lines tranche: an imperative target and a noun control; every item has the <c>frame</c> and <c>unit</c> comments.
    /// </summary>
    private static List<string> StepLinesFile() => TrancheFile(
        "step-lines",
        WithComments(NewBlock(NewId("step-lines", 1), "step-line", "target", "VERB", "imperative"), "frame = bare", "unit = enumerated"),
        WithComments(NewBlock(NewId("step-lines", 2), "step-line", "control", "NOUN", "noun"), "frame = bare", "unit = bulleted"));

    /// <summary>
    ///     A valid file of the clause-verbs tranche: a main and a subordinate target (VERB), a noun and an adjective control.
    /// </summary>
    private static List<string> ClauseVerbsFile() => TrancheFile(
        "clause-verbs",
        NewBlock(NewId("clause-verbs", 1), "v-clause-ambiguous", "target", "VERB", "main"),
        NewBlock(NewId("clause-verbs", 2), "v-clause-ambiguous", "target", "VERB", "sub"),
        NewBlock(NewId("clause-verbs", 3), "v-clause-ambiguous", "control", "NOUN", "noun"),
        NewBlock(NewId("clause-verbs", 4), "v-clause-ambiguous", "control", "ADJ", "adj"));

    /// <summary>
    ///     Writes the four technical files into the shapes development folder of a temporary repository root.
    /// </summary>
    private static void WriteTechnical(string root)
    {
        WriteShapes(root, "deverbal-heads.tsv", DeverbalHeadsFile());
        WriteShapes(root, "defined-terms.tsv", DefinedTermsFile());
        WriteShapes(root, "step-lines.tsv", StepLinesFile());
        WriteShapes(root, "clause-verbs.tsv", ClauseVerbsFile());
    }

    /// <summary>
    ///     Writes the five files of the first generation and the follow-up into the shapes development folder of a temporary repository root.
    /// </summary>
    private static void WriteEarlier(string root)
    {
        WriteShapes(root, "compound-head.tsv", CompoundHeadFile());
        WriteShapes(root, "headings.tsv", HeadingsFile());
        WriteShapes(root, "named-state.tsv", NamedStateFile());
        WriteShapes(root, "compound-head-2.tsv", CompoundHead2File());
        WriteShapes(root, "headings-2.tsv", Headings2File());
    }

    /// <summary>
    ///     Validates the table of the four technical tranches: their values, names, files, shapes, scoring, kinds with their roles and tags,
    ///     and that no tranche name is the start of another name (the longest-prefix rule needs no extra case).
    /// </summary>
    [Fact]
    public void ShapesDevSets_TechnicalSpecs_AreTheTableOfTheDataFiles()
    {
        // Arrange
        var expected = new (ShapesDevTranche Tranche, int Value, string Name, string Shape, ShapesDevScoring Scoring, string[] Kinds)[]
        {
            (ShapesDevTranche.DeverbalHeads, 7, "deverbal-heads", "n-deverbal-head", ShapesDevScoring.Class, new[] { "pred:target:Noun", "num:target:Noun", "ing:target:Noun", "other:target:Noun", "verb:control:Verb" }),
            (ShapesDevTranche.DefinedTerms, 8, "defined-terms", "term-capitalized", ShapesDevScoring.Exact, new[] { "noun:target:Noun", "adj:target:Adj", "proper:control:Propn" }),
            (ShapesDevTranche.StepLines, 9, "step-lines", "step-line", ShapesDevScoring.Exact, new[] { "imperative:target:Verb", "noun:control:Noun" }),
            (ShapesDevTranche.ClauseVerbs, 10, "clause-verbs", "v-clause-ambiguous", ShapesDevScoring.Exact, new[] { "main:target:Verb", "sub:target:Verb", "noun:control:Noun", "adj:control:Adj" }),
        };

        // Act / Assert: value, name, file, specification
        foreach (var (tranche, value, name, shape, scoring, kinds) in expected)
        {
            var spec = ShapesDevSets.SpecOf(tranche)!;
            Assert.Equal(value, (int)tranche);
            Assert.Equal(name, DevSets.ShapesDevNameOf(tranche));
            Assert.Equal($"data/eval/shapes-dev/{name}.tsv", DevSets.ShapesDevFileOf(tranche));
            Assert.Equal((shape, scoring, tranche), (spec.Shape, spec.Scoring, spec.Tranche));
            Assert.Equal(kinds, spec.Kinds.Select(k => $"{k.Name}:{k.Role}:{k.Tag}"));
            Assert.Same(spec, ShapesDevSets.SpecOf(shape));
        }

        // Assert: nine single-shape tranches in all, the four technical ones last and in the order of the files
        Assert.Equal(9, ShapesDevSets.TrancheShapes.Count);
        Assert.Equal(new[] { ShapesDevTranche.DeverbalHeads, ShapesDevTranche.DefinedTerms, ShapesDevTranche.StepLines, ShapesDevTranche.ClauseVerbs }, ShapesDevCommand.TechnicalTranches);
        Assert.Equal(ShapesDevCommand.NewTranches.Concat(ShapesDevCommand.FollowupTranches).Concat(ShapesDevCommand.TechnicalTranches), ShapesDevCommand.AllSingleTranches);
        Assert.Equal(ShapesDevSets.TrancheShapes.Select(s => s.Tranche), ShapesDevCommand.AllSingleTranches.OrderBy(t => (int)t));

        // Assert: the id prefix of a tranche (shapes-dev-<name>-) is not the start of the prefix of another tranche
        var prefixes = Enum.GetValues(typeof(ShapesDevTranche)).Cast<ShapesDevTranche>().Where(t => t > ShapesDevTranche.Standard).Select(t => "shapes-dev-" + DevSets.ShapesDevNameOf(t) + "-").ToList();
        foreach (var technical in ShapesDevCommand.TechnicalTranches)
        {
            var own = "shapes-dev-" + DevSets.ShapesDevNameOf(technical) + "-";
            Assert.DoesNotContain(prefixes, p => p != own && (p.StartsWith(own, StringComparison.Ordinal) || own.StartsWith(p, StringComparison.Ordinal)));
        }
    }

    /// <summary>
    ///     Validates that valid files of the four technical tranches are read with their kinds, the role each kind fixes, the exact gold tag
    ///     and the class of the tag, and that the extra comments of the committed files (<c>term</c>, <c>frame</c>, <c>unit</c>) are accepted.
    /// </summary>
    [Fact]
    public void ShapesDevSets_Parse_TechnicalTranches_ValidFiles_ReturnItemsWithKindAndGoldTag()
    {
        // Arrange / Act
        var deverbal = ShapesDevSets.Parse(DeverbalHeadsFile(), "fixture", ShapesDevTranche.DeverbalHeads);
        var terms = ShapesDevSets.Parse(DefinedTermsFile(), "fixture", ShapesDevTranche.DefinedTerms);
        var steps = ShapesDevSets.Parse(StepLinesFile(), "fixture", ShapesDevTranche.StepLines);
        var clauses = ShapesDevSets.Parse(ClauseVerbsFile(), "fixture", ShapesDevTranche.ClauseVerbs);

        // Assert: kinds, roles, exact tags and classes
        Assert.Equal(new[] { "pred", "num", "ing", "other", "verb" }, deverbal.Select(i => i.Kind));
        Assert.Equal(new[] { "NOUN", "NOUN", "NOUN", "NOUN", "VERB" }, deverbal.Select(i => i.GoldTag));
        Assert.Equal(new[] { true, true, true, true, false }, deverbal.Select(i => i.IsTarget));
        Assert.Equal(new[] { "Noun", "Noun", "Noun", "Noun", "Verb" }, deverbal.Select(i => i.GoldClass));
        Assert.Equal(new[] { "noun", "adj", "proper" }, terms.Select(i => i.Kind));
        Assert.Equal(new[] { "NOUN", "ADJ", "PROPN" }, terms.Select(i => i.GoldTag));
        Assert.Equal(new[] { true, true, false }, terms.Select(i => i.IsTarget));
        Assert.Equal(new[] { "imperative", "noun" }, steps.Select(i => i.Kind));
        Assert.Equal(new[] { "VERB", "NOUN" }, steps.Select(i => i.GoldTag));
        Assert.Equal(new[] { true, false }, steps.Select(i => i.IsTarget));
        Assert.Equal(new[] { "main", "sub", "noun", "adj" }, clauses.Select(i => i.Kind));
        Assert.Equal(new[] { "VERB", "VERB", "NOUN", "ADJ" }, clauses.Select(i => i.GoldTag));
        Assert.Equal(new[] { true, true, false, false }, clauses.Select(i => i.IsTarget));

        // Assert: shape and tranche of every item
        Assert.All(deverbal, i => Assert.Equal(("n-deverbal-head", ShapesDevTranche.DeverbalHeads), (i.Shape, i.Tranche)));
        Assert.All(terms, i => Assert.Equal(("term-capitalized", ShapesDevTranche.DefinedTerms), (i.Shape, i.Tranche)));
        Assert.All(steps, i => Assert.Equal(("step-line", ShapesDevTranche.StepLines), (i.Shape, i.Tranche)));
        Assert.All(clauses, i => Assert.Equal(("v-clause-ambiguous", ShapesDevTranche.ClauseVerbs), (i.Shape, i.Tranche)));
    }

    /// <summary>
    ///     Validates that each invalid item of a technical tranche is refused with a message that names the rule: another shape, a missing,
    ///     unknown or misplaced kind, a kind with the wrong role, a tag that the kind does not allow and a disputed judged word.
    /// </summary>
    [Fact]
    public void ShapesDevSets_Parse_TechnicalTranches_InvalidItems_AreRefused()
    {
        // Arrange: one broken file per rule, with the text the message must contain
        const string Dv = "n-deverbal-head";
        const string Dt = "term-capitalized";
        const string St = "step-line";
        const string Cv = "v-clause-ambiguous";
        var cases = new List<(ShapesDevTranche Tranche, string[] Block, string Expected)>
        {
            (ShapesDevTranche.DeverbalHeads, NewBlock(NewId("deverbal-heads", 1), "n-compound-head-ambiguous", "target", "NOUN", "pred"), "the only shape of the deverbal-heads tranche"),
            (ShapesDevTranche.DefinedTerms, NewBlock(NewId("defined-terms", 1), Dv, "target", "NOUN", "noun"), "the only shape of the defined-terms tranche"),
            (ShapesDevTranche.DeverbalHeads, NewBlock(NewId("deverbal-heads", 1), Dv, "target", "NOUN"), "the comment 'kind' is missing"),
            (ShapesDevTranche.DeverbalHeads, NewBlock(NewId("deverbal-heads", 1), Dv, "target", "NOUN", "heading"), "the kind 'heading' is not one of pred, num, ing, other, verb"),
            (ShapesDevTranche.DeverbalHeads, NewBlock(NewId("deverbal-heads", 1), Dv, "target", "VERB", "pred"), "must be tagged NOUN but is tagged VERB"),
            (ShapesDevTranche.DeverbalHeads, NewBlock(NewId("deverbal-heads", 1), Dv, "target", "PROPN", "ing"), "must be tagged NOUN but is tagged PROPN"),
            (ShapesDevTranche.DeverbalHeads, NewBlock(NewId("deverbal-heads", 1), Dv, "control", "NOUN", "other"), "does not match the kind 'other'"),
            (ShapesDevTranche.DeverbalHeads, NewBlock(NewId("deverbal-heads", 1), Dv, "target", "VERB", "verb"), "does not match the kind 'verb'"),
            (ShapesDevTranche.DeverbalHeads, NewBlock(NewId("deverbal-heads", 1), Dv, "control", "NOUN", "verb"), "must be tagged VERB but is tagged NOUN"),
            (ShapesDevTranche.DeverbalHeads, NewBlock(NewId("deverbal-heads", 1), Dv, "target", "NOUN", "num", "disputed"), "must not be disputed"),
            (ShapesDevTranche.DefinedTerms, NewBlock(NewId("defined-terms", 1), Dt, "target", "NOUN", "adj"), "must be tagged ADJ but is tagged NOUN"),
            (ShapesDevTranche.DefinedTerms, NewBlock(NewId("defined-terms", 1), Dt, "target", "PROPN", "noun"), "must be tagged NOUN but is tagged PROPN"),
            (ShapesDevTranche.DefinedTerms, NewBlock(NewId("defined-terms", 1), Dt, "target", "PROPN", "proper"), "does not match the kind 'proper'"),
            (ShapesDevTranche.DefinedTerms, NewBlock(NewId("defined-terms", 1), Dt, "control", "NOUN", "proper"), "must be tagged PROPN but is tagged NOUN"),
            (ShapesDevTranche.DefinedTerms, NewBlock(NewId("defined-terms", 1), Dt, "target", "VERB", "imperative"), "the kind 'imperative' is not one of noun, adj, proper"),
            (ShapesDevTranche.StepLines, NewBlock(NewId("step-lines", 1), St, "target", "NOUN", "imperative"), "must be tagged VERB but is tagged NOUN"),
            (ShapesDevTranche.StepLines, NewBlock(NewId("step-lines", 1), St, "target", "NOUN", "noun"), "does not match the kind 'noun'"),
            (ShapesDevTranche.StepLines, NewBlock(NewId("step-lines", 1), St, "control", "VERB", "noun"), "must be tagged NOUN but is tagged VERB"),
            (ShapesDevTranche.StepLines, NewBlock(NewId("step-lines", 1), St, "control", "VERB", "imperative"), "does not match the kind 'imperative'"),
            (ShapesDevTranche.ClauseVerbs, NewBlock(NewId("clause-verbs", 1), Cv, "target", "NOUN", "main"), "must be tagged VERB but is tagged NOUN"),
            (ShapesDevTranche.ClauseVerbs, NewBlock(NewId("clause-verbs", 1), Cv, "target", "ADJ", "sub"), "must be tagged VERB but is tagged ADJ"),
            (ShapesDevTranche.ClauseVerbs, NewBlock(NewId("clause-verbs", 1), Cv, "control", "NOUN", "adj"), "must be tagged ADJ but is tagged NOUN"),
            (ShapesDevTranche.ClauseVerbs, NewBlock(NewId("clause-verbs", 1), Cv, "control", "VERB", "main"), "does not match the kind 'main'"),
            (ShapesDevTranche.ClauseVerbs, NewBlock(NewId("clause-verbs", 1), Cv, "target", "NOUN", "noun"), "does not match the kind 'noun'"),
            (ShapesDevTranche.ClauseVerbs, NewBlock(NewId("clause-verbs", 1), Cv, "target", "VERB", "main", "disputed"), "must not be disputed"),
        };

        // Act / Assert
        foreach (var (tranche, block, expected) in cases)
        {
            var exception = Assert.Throws<InvalidDataException>(() => ShapesDevSets.Parse(TrancheFile(DevSets.ShapesDevNameOf(tranche), block), "fixture", tranche));
            Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
        }

        // Assert: the shape of a technical tranche is not a shape of the standard tranche, and a file of the standard tranche is no technical file
        Assert.Throws<InvalidDataException>(() => ShapesDevSets.Parse(DeverbalHeadsFile(), "fixture"));
        Assert.Throws<InvalidDataException>(() => ShapesDevSets.Parse(File(CompoundHead()), "fixture", ShapesDevTranche.StepLines));
    }

    /// <summary>
    ///     Validates that each technical tranche is read from its own file with its own SHA-256 and marks its items, that the longest id prefix
    ///     decides, and that a file cannot be read as another tranche: the corpus marker and the sentence-id prefix are both checked.
    /// </summary>
    [Fact]
    public void DevSets_ReadShapesDev_TechnicalTranches_OwnFileOwnMarkerAndIdPrefix()
    {
        // Arrange: the four technical files next to a first-generation file
        var root = TempRoot(null);
        WriteTechnical(root);
        WriteShapes(root, "headings.tsv", HeadingsFile());
        var files = new (ShapesDevTranche Tranche, string Name, Func<List<string>> Make)[]
        {
            (ShapesDevTranche.DeverbalHeads, "deverbal-heads", DeverbalHeadsFile),
            (ShapesDevTranche.DefinedTerms, "defined-terms", DefinedTermsFile),
            (ShapesDevTranche.StepLines, "step-lines", StepLinesFile),
            (ShapesDevTranche.ClauseVerbs, "clause-verbs", ClauseVerbsFile),
        };
        try
        {
            // Act / Assert: each file is read as its own tranche, with a distinct hash and ids that carry the prefix of the tranche
            var hashes = new List<string>();
            foreach (var (tranche, name, _) in files)
            {
                var items = DevSets.ReadShapesDev(root, tranche, out var sha);
                hashes.Add(sha);
                Assert.All(items, i => Assert.Equal(tranche, i.Tranche));
                Assert.All(items, i => Assert.StartsWith($"shapes-dev-{name}-", i.Id, StringComparison.Ordinal));
                Assert.True(DevSets.ShapesDevExists(root, tranche));
            }

            Assert.Equal(4, hashes.Distinct().Count());

            // Assert: a file of another tranche at the path of a technical tranche is refused, by the marker first
            foreach (var (tranche, name, _) in files)
            {
                foreach (var (_, otherName, otherMake) in files.Where(f => f.Tranche != tranche))
                {
                    WriteShapes(root, name + ".tsv", otherMake());
                    Assert.Contains($"announces itself as the {otherName} tranche", Assert.Throws<InvalidDataException>(() => DevSets.ReadShapesDev(root, tranche, out _)).Message, StringComparison.Ordinal);
                }

                WriteShapes(root, name + ".tsv", HeadingsFile());
                Assert.Contains("announces itself as the headings tranche", Assert.Throws<InvalidDataException>(() => DevSets.ReadShapesDev(root, tranche, out _)).Message, StringComparison.Ordinal);
                WriteShapes(root, name + ".tsv", files.First(f => f.Tranche == tranche).Make());
            }

            // Assert: the right marker with a sentence id of another tranche, of the standard tranche or of the hard tranche is refused
            WriteShapes(root, "step-lines.tsv", TrancheFile("step-lines", WithComments(NewBlock(NewId("clause-verbs", 1), "step-line", "target", "VERB", "imperative"), "frame = bare", "unit = enumerated")));
            Assert.Contains("sentence id", Assert.Throws<InvalidDataException>(() => DevSets.ReadShapesDev(root, ShapesDevTranche.StepLines, out _)).Message, StringComparison.Ordinal);
            WriteShapes(root, "step-lines.tsv", TrancheFile("step-lines", NewBlock("shapes-dev-0001", "step-line", "target", "VERB", "imperative")));
            Assert.Contains("sentence id", Assert.Throws<InvalidDataException>(() => DevSets.ReadShapesDev(root, ShapesDevTranche.StepLines, out _)).Message, StringComparison.Ordinal);
            WriteShapes(root, "deverbal-heads.tsv", TrancheFile("deverbal-heads", NewBlock("shapes-dev-hard-0001", "n-deverbal-head", "target", "NOUN", "pred")));
            Assert.Contains("sentence id", Assert.Throws<InvalidDataException>(() => DevSets.ReadShapesDev(root, ShapesDevTranche.DeverbalHeads, out _)).Message, StringComparison.Ordinal);

            // Assert: a file that names no marker at all is refused, and a technical id at the standard path is refused
            WriteShapes(root, "defined-terms.tsv", File(CompoundHead(NewId("defined-terms", 1))));
            Assert.Contains("does not announce itself as the defined-terms tranche", Assert.Throws<InvalidDataException>(() => DevSets.ReadShapesDev(root, ShapesDevTranche.DefinedTerms, out _)).Message, StringComparison.Ordinal);
            WriteShapes(root, "dev.tsv", File(CompoundHead(NewId("clause-verbs", 1))));
            Assert.Contains("sentence id", Assert.Throws<InvalidDataException>(() => DevSets.ReadShapesDev(root, ShapesDevTranche.Standard, out _)).Message, StringComparison.Ordinal);

            // Assert: removing a file makes it absent and its read an error that names the file
            System.IO.File.Delete(Path.Combine(root, "data", "eval", "shapes-dev", "clause-verbs.tsv"));
            Assert.False(DevSets.ShapesDevExists(root, ShapesDevTranche.ClauseVerbs));
            Assert.Contains("clause-verbs.tsv", Assert.Throws<FileNotFoundException>(() => DevSets.ReadShapesDev(root, ShapesDevTranche.ClauseVerbs, out _)).Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    ///     Validates the counts at the confidence limits: a wrong target counts at 0.90 from 0.90 on and at 0.95 from 0.95 on (0.899 and 0.949 do
    ///     not), a correct answer does not, a wrong control is counted in its own two columns, and the counts do not change the harmful errors.
    /// </summary>
    [Fact]
    public void ShapesDevTrancheEvaluator_Summarize_Wrong90And95_CountTargetsAndControlsSeparately()
    {
        // Arrange
        var tranche = ShapesDevTranche.StepLines;
        var items = new List<ShapesDevItem>
        {
            TrancheItem(tranche, "a", "target", "cat", "VERB", "imperative"),
            TrancheItem(tranche, "b", "target", "cat", "VERB", "imperative"),
            TrancheItem(tranche, "c", "target", "cat", "VERB", "imperative"),
            TrancheItem(tranche, "d", "target", "cat", "VERB", "imperative"),
            TrancheItem(tranche, "e", "target", "cat", "VERB", "imperative"),
            TrancheItem(tranche, "f", "control", "cat", "NOUN", "noun"),
            TrancheItem(tranche, "g", "control", "cat", "NOUN", "noun"),
            TrancheItem(tranche, "h", "control", "cat", "NOUN", "noun"),
        };
        var outcomes = new List<Outcome>
        {
            new("d", false, 0.95, 0.05, true, true, true),
            new("d", false, 0.90, 0.10, true, true, true),
            new("d", false, 0.899, 0.10, true, true, true),
            new("d", true, 0.99, 0.99, true, true, false),
            new("d", false, 0.999, 0.0, true, false, false),
            new("d", false, 0.97, 0.03, true, false, false),
            new("d", false, 0.92, 0.08, true, false, false),
            new("d", true, 0.99, 0.99, true, false, false),
        };

        // Act
        var result = ShapesDevTrancheEvaluator.Summarize(tranche, items, outcomes);

        // Assert: the limits and the predicates
        Assert.Equal(0.90, Metrics.ConfidentWrong90);
        Assert.Equal(0.95, Metrics.ConfidentWrongConfidence);
        Assert.True(Metrics.IsConfidentlyWrong90(outcomes[1]));
        Assert.False(Metrics.IsConfidentlyWrong90(outcomes[2]));
        Assert.False(Metrics.IsConfidentlyWrong90(outcomes[3]));
        Assert.Throws<ArgumentNullException>(() => Metrics.IsConfidentlyWrong90(null!));

        // Assert: the targets (wrong at 0.90: a, b, e; wrong at 0.95: a, e) and the controls (f, g at 0.90; f at 0.95)
        Assert.Equal((5, 1, 4, 3), (result.Overall.TargetItems, result.Overall.TargetCorrect, result.Overall.NounVerbDecisions, result.Overall.HarmfulErrors));
        Assert.Equal((3, 2), (result.Overall.ConfidentWrong90, result.Overall.ConfidentWrong));
        Assert.Equal((3, 1, 2, 1), (result.Overall.ControlItems, result.Overall.ControlCorrect, result.Overall.ControlConfidentWrong90, result.Overall.ControlConfidentWrong));

        // Assert: the kinds split the same counts
        Assert.Equal(new[] { "step-line: imperative targets", "step-line: NOUN controls" }, result.Kinds.Select(k => k.Shape));
        Assert.Equal((5, 3, 2, 0, 0), (result.Kinds[0].TargetItems, result.Kinds[0].ConfidentWrong90, result.Kinds[0].ConfidentWrong, result.Kinds[0].ControlItems, result.Kinds[0].ControlConfidentWrong90));
        Assert.Equal((0, 3, 2, 1), (result.Kinds[1].TargetItems, result.Kinds[1].ControlItems, result.Kinds[1].ControlConfidentWrong90, result.Kinds[1].ControlConfidentWrong));
    }

    /// <summary>
    ///     Validates the exact-tag scoring of the technical shapes through the real decoder: a capitalized adjective answered PROPN or NOUN is
    ///     wrong and not harmful, a noun-verb swap on an imperative is harmful, an adjective control answered as a verb is wrong and never
    ///     harmful, and the deverbal-head shape is scored by class (a PROPN answer for a NOUN head is right).
    /// </summary>
    [Fact]
    public void ShapesDevTrancheEvaluator_Evaluate_TechnicalShapes_AreScoredUnderTheirScoring()
    {
        // Arrange: "cat" is answered PROPN (NOUN is the other candidate), "dog" is answered VERB (NOUN is the other candidate)
        var tagger = PropnCatVerbDogTagger();
        var terms = new List<ShapesDevItem>
        {
            TrancheItem(ShapesDevTranche.DefinedTerms, "a", "target", "cat", "ADJ", "adj"),
            TrancheItem(ShapesDevTranche.DefinedTerms, "b", "target", "cat", "NOUN", "noun"),
            TrancheItem(ShapesDevTranche.DefinedTerms, "c", "control", "cat", "PROPN", "proper"),
        };
        var clauses = new List<ShapesDevItem>
        {
            TrancheItem(ShapesDevTranche.ClauseVerbs, "d", "target", "dog", "VERB", "main"),
            TrancheItem(ShapesDevTranche.ClauseVerbs, "e", "control", "dog", "ADJ", "adj"),
            TrancheItem(ShapesDevTranche.ClauseVerbs, "f", "control", "dog", "NOUN", "noun"),
        };
        var steps = new List<ShapesDevItem> { TrancheItem(ShapesDevTranche.StepLines, "g", "target", "cat", "VERB", "imperative") };
        var deverbal = new List<ShapesDevItem>
        {
            TrancheItem(ShapesDevTranche.DeverbalHeads, "h", "target", "cat", "NOUN", "pred"),
            TrancheItem(ShapesDevTranche.DeverbalHeads, "i", "target", "dog", "NOUN", "ing"),
            TrancheItem(ShapesDevTranche.DeverbalHeads, "j", "control", "dog", "VERB", "verb"),
        };

        // Act
        var termResult = ShapesDevTrancheEvaluator.Evaluate(tagger, ShapesDevTranche.DefinedTerms, terms);
        var clauseResult = ShapesDevTrancheEvaluator.Evaluate(tagger, ShapesDevTranche.ClauseVerbs, clauses);
        var stepResult = ShapesDevTrancheEvaluator.Evaluate(tagger, ShapesDevTranche.StepLines, steps);
        var deverbalResult = ShapesDevTrancheEvaluator.Evaluate(tagger, ShapesDevTranche.DeverbalHeads, deverbal);

        // Assert: both defined-term targets are wrong (PROPN is not ADJ and not NOUN), neither is a noun-verb decision, the PROPN control is right
        Assert.Equal((2, 0, 0, 0, 1, 1), (termResult.Overall.TargetItems, termResult.Overall.TargetCorrect, termResult.Overall.NounVerbDecisions, termResult.Overall.HarmfulErrors, termResult.Overall.ControlItems, termResult.Overall.ControlCorrect));
        Assert.Equal(new[] { "term-capitalized: noun targets", "term-capitalized: adj targets", "term-capitalized: PROPN controls" }, termResult.Kinds.Select(k => k.Shape));

        // Assert: the main-clause verb answered VERB is right, the adjective and noun controls answered VERB are wrong and never harmful
        Assert.Equal((1, 1, 1, 0, 2, 0), (clauseResult.Overall.TargetItems, clauseResult.Overall.TargetCorrect, clauseResult.Overall.NounVerbDecisions, clauseResult.Overall.HarmfulErrors, clauseResult.Overall.ControlItems, clauseResult.Overall.ControlCorrect));

        // Assert: the imperative answered PROPN is wrong and not a noun-verb swap
        Assert.Equal((1, 0, 0), (stepResult.Overall.TargetItems, stepResult.Overall.TargetCorrect, stepResult.Overall.HarmfulErrors));

        // Assert: by class the PROPN answer for a NOUN head is right, the verb answer for the other head is a harmful swap, the verb control is right
        Assert.Equal((2, 1, 1, 1, 1, 1), (deverbalResult.Overall.TargetItems, deverbalResult.Overall.TargetCorrect, deverbalResult.Overall.NounVerbDecisions, deverbalResult.Overall.HarmfulErrors, deverbalResult.Overall.ControlItems, deverbalResult.Overall.ControlCorrect));
    }

    /// <summary>
    ///     Validates the counts table: heading, the explanation of the three counts, the note that no threshold is chosen, and one row per
    ///     tranche and one per kind with a dash for a column that does not apply.
    /// </summary>
    [Fact]
    public void ShapesDevReport_RenderCounts_PrintsOneRowPerTrancheAndKind()
    {
        // Arrange: a step-lines result (kinds: imperative targets, noun controls) and a compound-head result (no kinds)
        var stepItems = new List<ShapesDevItem>
        {
            TrancheItem(ShapesDevTranche.StepLines, "a", "target", "cat", "VERB", "imperative"),
            TrancheItem(ShapesDevTranche.StepLines, "b", "target", "cat", "VERB", "imperative"),
            TrancheItem(ShapesDevTranche.StepLines, "c", "control", "cat", "NOUN", "noun"),
        };
        var stepOutcomes = new List<Outcome>
        {
            new("d", false, 0.96, 0.04, true, true, true),
            new("d", true, 0.99, 0.99, true, true, false),
            new("d", false, 0.91, 0.09, true, false, false),
        };
        var steps = ShapesDevTrancheEvaluator.Summarize(ShapesDevTranche.StepLines, stepItems, stepOutcomes);
        var compound = ShapesDevTrancheEvaluator.Summarize(ShapesDevTranche.CompoundHead, new List<ShapesDevItem> { TrancheItem(ShapesDevTranche.CompoundHead, "x", "target", "cat", "NOUN") }, new List<Outcome> { new("d", true, 0.7, 0.7, true, true, false) });

        // Act
        var first = ShapesDevReport.RenderCounts("m", new[] { steps, compound });
        var second = ShapesDevReport.RenderCounts("m", new[] { steps, compound });

        // Assert
        Assert.Equal(first, second);
        Assert.Equal("## Shapes development set, counts at the confidence limits: m", first[0]);
        Assert.Contains(first, l => l.StartsWith("- Harmful: a swapped noun-verb decision of a target item with a confidence of at least 0.45. Wrong at confidence >= 0.90 and >= 0.95", StringComparison.Ordinal));
        Assert.Contains("- The counts are measurements at fixed limits; no threshold is chosen or changed here.", first);
        Assert.Equal("| tranche | row | target items | target accuracy | noun-verb decisions | harmful at >= 0.45 | wrong at >= 0.90 | wrong at >= 0.95 | control items | control wrong at >= 0.90 | control wrong at >= 0.95 |", first[5]);
        Assert.Equal("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |", first[6]);
        Assert.Equal(
            new[]
            {
                "| step-lines | all items | 2 | 50.0% | 2 | 1 | 1 | 1 | 1 | 1 | 0 |",
                "| step-lines | imperative (target) | 2 | 50.0% | 2 | 1 | 1 | 1 | - | - | - |",
                "| step-lines | noun (control) | - | - | - | - | - | - | 1 | 1 | 0 |",
                "| compound-head | all items | 1 | 100.0% | 1 | 0 | 0 | 0 | - | - | - |",
            },
            first.Skip(7));
    }

    /// <summary>
    ///     Validates that the scoring note of a technical shape (exact tag) does not speak of headings, that the note of the headings is the
    ///     one it always was, and that the deverbal-head shape (class) has the note of the class-scored shapes.
    /// </summary>
    [Fact]
    public void ShapesDevReport_RenderTranche_TechnicalShapes_HaveTheirOwnScoringNote()
    {
        // Arrange
        string Scoring(ShapesDevTranche tranche, string goldTag, string kind) =>
            ShapesDevReport.RenderTranche("m", tranche, string.Empty, ShapesDevTrancheEvaluator.Summarize(tranche, new List<ShapesDevItem> { TrancheItem(tranche, "a", "target", "cat", goldTag, kind) }, new List<Outcome> { new("d", true, 0.9, 0.9, true, true, false) })).Single(l => l.StartsWith("- Scoring:", StringComparison.Ordinal));

        // Act
        var steps = Scoring(ShapesDevTranche.StepLines, "VERB", "imperative");
        var headings = Scoring(ShapesDevTranche.Headings, "NOUN", "noun");
        var deverbal = Scoring(ShapesDevTranche.DeverbalHeads, "NOUN", "pred");

        // Assert
        Assert.Contains("by exact tag (the answer is right only when it is the gold tag;", steps, StringComparison.Ordinal);
        Assert.DoesNotContain("heading", steps, StringComparison.Ordinal);
        Assert.Contains("by exact tag (a NOUN or VERB heading answered PROPN is wrong", headings, StringComparison.Ordinal);
        Assert.Contains("by class (Noun: NOUN and PROPN; Verb: VERB)", deverbal, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Validates that the groups and the names select the tranches in report order: <c>technical</c> the four, <c>all-single</c> all nine,
    ///     <c>new</c> and <c>followup</c> their groups, a tranche name the one tranche, and that only the groups are groups.
    /// </summary>
    [Fact]
    public void ShapesDevCommand_TranchesOf_GroupsAndNames_SelectInReportOrder()
    {
        // Arrange / Act / Assert
        Assert.Equal(ShapesDevCommand.TechnicalTranches, ShapesDevCommand.TranchesOf("technical"));
        Assert.Equal(ShapesDevCommand.AllSingleTranches, ShapesDevCommand.TranchesOf("all-single"));
        Assert.Equal(ShapesDevCommand.NewTranches, ShapesDevCommand.TranchesOf("new"));
        Assert.Equal(ShapesDevCommand.FollowupTranches, ShapesDevCommand.TranchesOf("followup"));
        Assert.Equal(9, ShapesDevCommand.AllSingleTranches.Distinct().Count());
        foreach (var tranche in ShapesDevCommand.AllSingleTranches)
        {
            Assert.Equal(new[] { tranche }, ShapesDevCommand.TranchesOf(DevSets.ShapesDevNameOf(tranche)));
            Assert.False(ShapesDevCommand.IsGroup(DevSets.ShapesDevNameOf(tranche)));
        }

        Assert.All(new[] { "new", "followup", "technical", "all-single" }, which => Assert.True(ShapesDevCommand.IsGroup(which)));
        Assert.Empty(ShapesDevCommand.TranchesOf("standard"));
    }

    /// <summary>
    ///     Validates that <c>technical</c> scores the four tranches in order, each as a section, followed by the counts table, that an absent
    ///     file of the group is noted, that an explicit technical tranche is scored alone with its counts table and needs its file, and that
    ///     <c>all-single</c> adds the nine tranches in the order of the generations.
    /// </summary>
    [Fact]
    public void ShapesDevCommand_Run_Technical_ScoresTheFourTranchesWithTheCountsTable()
    {
        // Arrange: the step-lines file is absent
        var root = TempRoot(null);
        WriteShapes(root, "deverbal-heads.tsv", DeverbalHeadsFile());
        WriteShapes(root, "defined-terms.tsv", DefinedTermsFile());
        WriteShapes(root, "clause-verbs.tsv", ClauseVerbsFile());
        var tagger = FixtureTagger();
        try
        {
            // Act
            var lines = ShapesDevCommand.Run(root, tagger, "m", "w", "technical");

            // Assert: sections in order with the missing file noted in its place, then one counts table over the scored tranches
            var expected = new List<string>();
            var results = new List<ShapesDevSingleShapeResult>();
            foreach (var tranche in ShapesDevCommand.TechnicalTranches)
            {
                if (expected.Count > 0)
                {
                    expected.Add(string.Empty);
                }

                if (tranche == ShapesDevTranche.StepLines)
                {
                    expected.Add(ShapesDevReport.RenderTrancheMissing(tranche));
                    continue;
                }

                var items = DevSets.ReadShapesDev(root, tranche, out var sha);
                var result = ShapesDevTrancheEvaluator.Evaluate(tagger, tranche, items);
                results.Add(result);
                expected.AddRange(ShapesDevReport.RenderTranche("m", tranche, sha, result, "w"));
            }

            expected.Add(string.Empty);
            expected.AddRange(ShapesDevReport.RenderCounts("m", results));
            Assert.Equal(expected, lines);
            Assert.Equal(
                new[] { "## Shapes development set, deverbal-heads tranche: m", "## Shapes development set, defined-terms tranche: m", "## Shapes development set, clause-verbs tranche: m", "## Shapes development set, counts at the confidence limits: m" },
                lines.Where(l => l.StartsWith("## ", StringComparison.Ordinal)));
            Assert.DoesNotContain(lines, l => l.Contains("calibration", StringComparison.Ordinal));

            // Assert: an explicit technical tranche is scored alone, with its counts table, and needs its file
            var alone = ShapesDevCommand.Run(root, tagger, "m", "w", "defined-terms");
            Assert.Equal(new[] { "## Shapes development set, defined-terms tranche: m", "## Shapes development set, counts at the confidence limits: m" }, alone.Where(l => l.StartsWith("## ", StringComparison.Ordinal)));
            Assert.Contains("step-lines.tsv", Assert.Throws<FileNotFoundException>(() => ShapesDevCommand.Run(root, tagger, "m", "w", "step-lines")).Message, StringComparison.Ordinal);

            // Assert: all-single notes the five earlier files that are absent and scores the technical ones after them
            var all = ShapesDevCommand.Run(root, tagger, "m", "w", "all-single");
            Assert.Equal(6, all.Count(l => l.StartsWith("- The ", StringComparison.Ordinal) && l.EndsWith("is not present; it is not scored.", StringComparison.Ordinal)));
            Assert.Equal(
                new[] { "## Shapes development set, deverbal-heads tranche: m", "## Shapes development set, defined-terms tranche: m", "## Shapes development set, clause-verbs tranche: m", "## Shapes development set, counts at the confidence limits: m" },
                all.Where(l => l.StartsWith("## ", StringComparison.Ordinal)));
            var order = all.Where(l => l.StartsWith("- The ", StringComparison.Ordinal) && l.EndsWith("is not present; it is not scored.", StringComparison.Ordinal)).Select(l => l.Split(' ')[2]);
            Assert.Equal(new[] { "compound-head", "headings", "named-state", "compound-head-2", "headings-2", "step-lines" }, order);

            // Assert: with no file at all every tranche of a group is noted and there is no counts table
            Directory.Delete(Path.Combine(root, "data", "eval", "shapes-dev"), true);
            Directory.CreateDirectory(Path.Combine(root, "data", "eval", "shapes-dev"));
            var none = ShapesDevCommand.Run(root, tagger, "m", "w", "technical");
            Assert.Equal(7, none.Count);
            Assert.Equal(ShapesDevCommand.TechnicalTranches.Select(ShapesDevReport.RenderTrancheMissing), none.Where(l => l.Length > 0));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    ///     Validates that <c>new</c> and <c>followup</c> and the names of the first two generations print exactly what they printed before: the
    ///     same lines whether or not the four technical files are present, no counts table and no calibration section, and the sections of the
    ///     renderer of the single-shape tranches.
    /// </summary>
    [Fact]
    public void ShapesDevCommand_Run_NewAndFollowup_AreByteIdenticalWithTheTechnicalFilesPresent()
    {
        // Arrange: two roots, with and without the technical files
        var without = TempRoot(null);
        var with = TempRoot(null);
        WriteEarlier(without);
        WriteEarlier(with);
        WriteTechnical(with);
        var tagger = FixtureTagger();
        try
        {
            foreach (var which in new[] { "new", "followup", "compound-head", "headings", "named-state", "compound-head-2", "headings-2" })
            {
                // Act
                var before = ShapesDevCommand.Run(without, tagger, "m", "w", which);
                var after = ShapesDevCommand.Run(with, tagger, "m", "w", which);

                // Assert: identical lines, built only from the sections of the renderer
                Assert.Equal(string.Join("\n", before), string.Join("\n", after));
                Assert.DoesNotContain(after, l => l.Contains("counts at the confidence limits", StringComparison.Ordinal) || l.Contains("calibration", StringComparison.Ordinal));
                var expected = new List<string>();
                foreach (var tranche in ShapesDevCommand.TranchesOf(which))
                {
                    if (expected.Count > 0)
                    {
                        expected.Add(string.Empty);
                    }

                    var items = DevSets.ReadShapesDev(without, tranche, out var sha);
                    expected.AddRange(ShapesDevReport.RenderTranche("m", tranche, sha, ShapesDevTrancheEvaluator.Evaluate(tagger, tranche, items), "w"));
                }

                Assert.Equal(expected, after);
            }

            // Assert: the technical tranches and the groups that contain them add their own sections after the earlier ones, never inside new or followup
            var allSingle = ShapesDevCommand.Run(with, tagger, "m", "w", "all-single");
            var newLines = ShapesDevCommand.Run(with, tagger, "m", "w", "new");
            var followupLines = ShapesDevCommand.Run(with, tagger, "m", "w", "followup");
            Assert.Equal(newLines.Concat(new[] { string.Empty }).Concat(followupLines), allSingle.Take(newLines.Count + 1 + followupLines.Count));
            Assert.Equal(9 + 1, allSingle.Count(l => l.StartsWith("## ", StringComparison.Ordinal)));
        }
        finally
        {
            Directory.Delete(without, true);
            Directory.Delete(with, true);
        }
    }

    /// <summary>
    ///     Validates that the calibration section applies to the single-shape tranches only: it is refused with the original selections and
    ///     is appended after the tranche sections (and the counts table) otherwise, pooled and per tranche.
    /// </summary>
    [Fact]
    public void ShapesDevCommand_Run_Calibration_AppendsTheSectionForSingleShapeSelectionsOnly()
    {
        // Arrange
        var root = TempRoot(File(CompoundHead("shapes-dev-0001")));
        WriteTechnical(root);
        WriteEarlier(root);
        var tagger = FixtureTagger();
        try
        {
            // Act / Assert: refused for standard, hard and both
            foreach (var which in new[] { "standard", "hard", "both" })
            {
                Assert.Contains("--calibration applies to the single-shape tranches only", Assert.Throws<InvalidOperationException>(() => ShapesDevCommand.Run(root, tagger, "m", "w", which, true)).Message, StringComparison.Ordinal);
            }

            // Act: technical selection with the calibration
            var plain = ShapesDevCommand.Run(root, tagger, "m", "w", "technical");
            var calibrated = ShapesDevCommand.Run(root, tagger, "m", "w", "technical", true);

            // Assert: the plain output is the prefix, then an empty line and the section of the measurement of the four tranches
            Assert.Equal(plain, calibrated.Take(plain.Count));
            Assert.Equal(string.Empty, calibrated[plain.Count]);
            var judged = ShapesDevCommand.TechnicalTranches.Select(t =>
            {
                var items = DevSets.ReadShapesDev(root, t, out _);
                return new KeyValuePair<string, IReadOnlyList<Outcome>>(DevSets.ShapesDevNameOf(t), ShapesDevTrancheEvaluator.Judge(tagger, items));
            }).ToList();
            Assert.Equal(ShapesDevCalibration.Render("m", ShapesDevCalibration.Measure(judged)), calibrated.Skip(plain.Count + 1));
            Assert.Equal(14, judged.Sum(j => j.Value.Count));
            Assert.Contains("| all selected tranches | 14 |", calibrated.Skip(plain.Count + 1).Single(l => l.StartsWith("| all selected", StringComparison.Ordinal)), StringComparison.Ordinal);

            // Assert: for "new" the sections are the old ones followed by the calibration, and a group without any file has nothing to measure
            var newPlain = ShapesDevCommand.Run(root, tagger, "m", "w", "new");
            var newCalibrated = ShapesDevCommand.Run(root, tagger, "m", "w", "new", true);
            Assert.Equal(newPlain, newCalibrated.Take(newPlain.Count));
            Assert.DoesNotContain(newPlain, l => l.Contains("calibration", StringComparison.Ordinal));
            Assert.Contains(newCalibrated, l => l.StartsWith("## Shapes development set, calibration of the confidence: m", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    ///     Validates the Wilson score interval: the known values (agreeing with the interval of the report), the bounds for no or all successes,
    ///     and the empty case.
    /// </summary>
    [Fact]
    public void ShapesDevCalibration_Wilson_KnownCounts_MatchesTheReportInterval()
    {
        // Arrange / Act
        var none = ShapesDevCalibration.Wilson(0, 10);
        var most = ShapesDevCalibration.Wilson(8, 10);
        var all = ShapesDevCalibration.Wilson(10, 10);
        var empty = ShapesDevCalibration.Wilson(0, 0);

        // Assert: the values of the report test ([0.0%, 27.8%], [49.0%, 94.3%], [72.2%, 100.0%])
        Assert.Equal((0.0, 0.278), (Math.Round(none.Low, 3), Math.Round(none.High, 3)));
        Assert.Equal((0.490, 0.943), (Math.Round(most.Low, 3), Math.Round(most.High, 3)));
        Assert.Equal((0.722, 1.0), (Math.Round(all.Low, 3), Math.Round(all.High, 3)));
        Assert.Equal((0.0, 0.0), empty);
        Assert.Equal((0.0, 0.0), ShapesDevCalibration.Wilson(3, -1));

        // Assert: the interval of the report is the same one, formatted
        foreach (var (successes, total) in new[] { (0, 10), (8, 10), (10, 10), (1, 4), (37, 1000) })
        {
            var (low, high) = ShapesDevCalibration.Wilson(successes, total);
            Assert.Equal("[" + (100.0 * low).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "%, " + (100.0 * high).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "%]", ShapesDevReport.Interval(successes, total));
            Assert.InRange(low, 0.0, successes / (double)total + 1e-9);
            Assert.InRange(high, successes / (double)total - 1e-9, 1.0);
        }
    }

    /// <summary>
    ///     Builds ten synthetic outcomes for the calibration tests: confidences 0.99, 0.97, 0.95, 0.90, 0.899, 0.60, 0.50, 0.49, 1.0 and 0.70,
    ///     with the wrong ones at 0.97, 0.90, 0.60, 0.49 and 0.70 and harmful errors at 0.97, 0.90 and 0.60.
    /// </summary>
    private static List<Outcome> SyntheticOutcomes() => new()
    {
        new("d", true, 0.99, 0.99, true, true, false),
        new("d", false, 0.97, 0.03, true, true, true),
        new("d", true, 0.95, 0.95, true, true, false),
        new("d", false, 0.90, 0.10, true, true, true),
        new("d", true, 0.899, 0.899, true, true, false),
        new("d", false, 0.60, 0.40, true, true, true),
        new("d", true, 0.50, 0.50, true, true, false),
        new("d", false, 0.49, 0.10, true, false, false),
        new("d", true, 1.0, 1.0, true, true, false),
        new("d", false, 0.70, 0.30, true, false, false),
    };

    /// <summary>
    ///     Validates the arithmetic of the calibration on synthetic outcomes: the answers and wrong answers at 0.90 and 0.95 (the limit counts),
    ///     the errors per 1,000 answers, the Wilson interval per 1,000, and the reliability bins with their edges (the lower edge belongs to the
    ///     bin, 1.0 to the last bin), counts, mean confidence, correct share and harmful errors.
    /// </summary>
    [Fact]
    public void ShapesDevCalibration_MeasureScope_SyntheticOutcomes_ArithmeticAndBins()
    {
        // Arrange / Act
        var scope = ShapesDevCalibration.MeasureScope("synthetic", SyntheticOutcomes());

        // Assert: thresholds
        Assert.Equal(new[] { 0.90, 0.95 }, ShapesDevCalibration.Thresholds);
        Assert.Equal(("synthetic", 10), (scope.Name, scope.Judged));
        var at90 = scope.Thresholds[0];
        var at95 = scope.Thresholds[1];
        Assert.Equal((0.90, 5, 2), (at90.Threshold, at90.Answers, at90.Wrong));
        Assert.Equal(400.0, at90.ErrorPer1000, 9);
        Assert.Equal((0.95, 4, 1), (at95.Threshold, at95.Answers, at95.Wrong));
        Assert.Equal(250.0, at95.ErrorPer1000, 9);
        var wilson90 = ShapesDevCalibration.Wilson(2, 5);
        Assert.Equal(1000.0 * wilson90.Low, at90.IntervalLowPer1000, 9);
        Assert.Equal(1000.0 * wilson90.High, at90.IntervalHighPer1000, 9);
        Assert.True(at90.IntervalLowPer1000 < at90.ErrorPer1000 && at90.ErrorPer1000 < at90.IntervalHighPer1000);
        Assert.True(at95.IntervalLowPer1000 <= at95.ErrorPer1000 && at95.ErrorPer1000 <= at95.IntervalHighPer1000);

        // Assert: the bins, the lower edge inclusive: below 0.5, 0.5, 0.6, 0.7, 0.8, 0.9 and 0.95 to 1.0
        Assert.Equal(new[] { 0.0, 0.5, 0.6, 0.7, 0.8, 0.9, 0.95 }, scope.Bins.Select(b => b.Low));
        Assert.Equal(new[] { 0.5, 0.6, 0.7, 0.8, 0.9, 0.95, 1.0 }, scope.Bins.Select(b => b.High));
        Assert.Equal(new long[] { 1, 1, 1, 1, 1, 1, 4 }, scope.Bins.Select(b => b.Decisions));
        Assert.Equal(10L, scope.Bins.Sum(b => b.Decisions));
        Assert.Equal(new long[] { 0, 0, 1, 0, 0, 1, 1 }, scope.Bins.Select(b => b.Harmful));
        Assert.Equal(new[] { 0.0, 100.0, 0.0, 0.0, 100.0, 0.0, 75.0 }, scope.Bins.Select(b => b.CorrectPercent));
        Assert.Equal(0.49, scope.Bins[0].MeanConfidence, 9);
        Assert.Equal(0.899, scope.Bins[4].MeanConfidence, 9);
        Assert.Equal((0.99 + 0.97 + 0.95 + 1.0) / 4, scope.Bins[6].MeanConfidence, 9);

        // Assert: no outcome at all gives no answer, zero rates and empty bins
        var empty = ShapesDevCalibration.MeasureScope("none", new List<Outcome>());
        Assert.Equal(0, empty.Judged);
        Assert.All(empty.Thresholds, t => Assert.Equal((0, 0, 0.0, 0.0, 0.0), (t.Answers, t.Wrong, t.ErrorPer1000, t.IntervalLowPer1000, t.IntervalHighPer1000)));
        Assert.All(empty.Bins, b => Assert.Equal((0L, 0.0, 0.0, 0L), (b.Decisions, b.MeanConfidence, b.CorrectPercent, b.Harmful)));
    }

    /// <summary>
    ///     Validates that the measurement pools the outcomes of all tranches and keeps one scope per tranche in the order given, and that the
    ///     rendering is deterministic Markdown with the pooled table, one row per tranche and one reliability table per scope, with a dash where
    ///     there is no answer, and that it states that no threshold is chosen.
    /// </summary>
    [Fact]
    public void ShapesDevCalibration_MeasureAndRender_PooledAndPerTranche()
    {
        // Arrange: the synthetic outcomes split into two tranches (the second has no answer at 0.90)
        var all = SyntheticOutcomes();
        var tranches = new List<KeyValuePair<string, IReadOnlyList<Outcome>>>
        {
            new("one", new[] { all[0], all[1], all[2], all[3], all[4], all[8] }),
            new("two", new[] { all[5], all[6], all[7], all[9] }),
        };

        // Act
        var report = ShapesDevCalibration.Measure(tranches);
        var lines = ShapesDevCalibration.Render("m", report);

        // Assert: pooled has the counts of the scope of all outcomes; the scopes keep the order
        var pooled = ShapesDevCalibration.MeasureScope("all selected tranches", all);
        Assert.Equal(pooled.Name, report.Pooled.Name);
        Assert.Equal(pooled.Judged, report.Pooled.Judged);
        Assert.Equal(pooled.Thresholds.Select(t => (t.Answers, t.Wrong)), report.Pooled.Thresholds.Select(t => (t.Answers, t.Wrong)));
        Assert.Equal(pooled.Bins.Select(b => b.Decisions), report.Pooled.Bins.Select(b => b.Decisions));
        Assert.Equal(new[] { "one", "two" }, report.Tranches.Select(t => t.Name));
        Assert.Equal((6, 4), (report.Tranches[0].Judged, report.Tranches[1].Judged));
        Assert.Equal((5, 0), (report.Tranches[0].Thresholds[0].Answers, report.Tranches[1].Thresholds[0].Answers));
        Assert.Equal(report.Pooled.Thresholds[0].Answers, report.Tranches.Sum(t => t.Thresholds[0].Answers));
        Assert.Equal(report.Pooled.Thresholds[1].Wrong, report.Tranches.Sum(t => t.Thresholds[1].Wrong));

        // Assert: the text
        Assert.Equal(lines, ShapesDevCalibration.Render("m", ShapesDevCalibration.Measure(tranches)));
        Assert.Equal("## Shapes development set, calibration of the confidence: m", lines[0]);
        Assert.Contains("- The measurement reports what the model does; no threshold is chosen, changed or proposed here.", lines);
        Assert.Contains("| scope | judged words | answers at >= 0.90 | wrong at >= 0.90 | errors per 1,000 answers at >= 0.90 | 95% interval at >= 0.90 | answers at >= 0.95 | wrong at >= 0.95 | errors per 1,000 answers at >= 0.95 | 95% interval at >= 0.95 |", lines);
        var interval = pooled.Thresholds[0];
        Assert.Contains(
            "| all selected tranches | 10 | 5 | 2 | 400.0 | [" + interval.IntervalLowPer1000.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ", " + interval.IntervalHighPer1000.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "] | 4 | 1 | 250.0 | [",
            lines.Single(l => l.StartsWith("| all selected tranches |", StringComparison.Ordinal)),
            StringComparison.Ordinal);
        Assert.Equal("| two | 4 | 0 | 0 | - | - | 0 | 0 | - | - |", lines.Single(l => l.StartsWith("| two |", StringComparison.Ordinal)));
        Assert.Equal(new[] { "### Reliability: all selected tranches", "### Reliability: one", "### Reliability: two" }, lines.Where(l => l.StartsWith("### ", StringComparison.Ordinal)));
        Assert.Contains("| below 0.50 | 1 | 0.490 | 0.0% | 0 |", lines);
        Assert.Contains("| 0.90 to 0.95 | 1 | 0.900 | 0.0% | 1 |", lines);
        var last = lines.Where(l => l.StartsWith("| 0.95 to 1.00 | 4 | ", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, last.Count);
        Assert.All(last, line => Assert.EndsWith("| 75.0% | 1 |", line, StringComparison.Ordinal));
        Assert.Contains("| 0.80 to 0.90 | 0 | - | - | 0 |", lines.Skip(lines.IndexOf("### Reliability: two")));
    }

    /// <summary>
    ///     Creates an item with a one-word sentence for the evaluator tests.
    /// </summary>
    private static ShapesDevItem Item(string id, string shape, string role, string word, string goldClass, string origin = "real", ShapesDevTranche tranche = ShapesDevTranche.Standard) =>
        new(id, origin == "real" ? "doc-a" : "shapes-dev-authored", new[] { word }, 0, goldClass, shape, role, word, origin, tranche);
}
