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
    private static string TempRoot(IEnumerable<string>? lines)
    {
        var root = Path.Combine(Path.GetTempPath(), "shapes-dev-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "data", "eval", "shapes-dev"));
        if (lines != null)
        {
            System.IO.File.WriteAllText(Path.Combine(root, "data", "eval", "shapes-dev", "dev.tsv"), string.Join("\n", lines) + "\n");
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
    ///     Validates the guard theories of the third development file: only its own guard admits it, and the two-file allow-list is unchanged.
    /// </summary>
    [Theory]
    [InlineData("data/eval/shapes-dev/dev.tsv")]
    [InlineData("data/eval/shapes-dev/../shapes-dev/dev.tsv")]
    public void DataGuard_EnsureShapesDevelopmentFile_ShapesDevelopmentFile_Admitted(string relativePath)
    {
        // Arrange / Act
        var resolved = DataGuard.EnsureShapesDevelopmentFile(DataFiles.RepositoryRoot, DataFiles.Repository(relativePath));

        // Assert
        Assert.EndsWith(Path.Combine("data", "eval", "shapes-dev", "dev.tsv"), resolved, StringComparison.OrdinalIgnoreCase);
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
    [InlineData("data/eval/shapes-dev/README.md")]
    public void DataGuard_EnsureShapesDevelopmentFile_OtherFile_Refused(string relativePath)
    {
        // Arrange / Act / Assert
        Assert.Throws<InvalidOperationException>(() => DataGuard.EnsureShapesDevelopmentFile(DataFiles.RepositoryRoot, DataFiles.Repository(relativePath)));
    }

    /// <summary>
    ///     Validates that the shapes development file is a development file of its own: not in the two-file allow-list, not a held-out file and not trainable.
    /// </summary>
    [Fact]
    public void DataGuard_ShapesDevelopmentFile_OnlyItsOwnGuardAdmitsIt()
    {
        // Arrange
        var path = DataFiles.Repository(DataGuard.ShapesDevelopmentFile);

        // Act / Assert
        Assert.DoesNotContain(DataGuard.ShapesDevelopmentFile, DataGuard.DevelopmentFiles);
        Assert.DoesNotContain(DataGuard.ShapesDevelopmentFile, DataGuard.HeldOutFiles);
        Assert.Equal(new[] { "data/open/dev.tsv", "data/eval/gold-v2/dev.tsv" }, DataGuard.DevelopmentFiles);
        Assert.Equal(4, DataGuard.HeldOutFiles.Count);
        Assert.Throws<InvalidOperationException>(() => DataGuard.EnsureDevelopmentFile(DataFiles.RepositoryRoot, path));
        Assert.Throws<InvalidOperationException>(() => DataGuard.EnsureTrainable(DataFiles.RepositoryRoot, path));
        Assert.Throws<InvalidOperationException>(() => CorpusReader.ReadFile(DataFiles.RepositoryRoot, path, new SourceConfig { Name = "t" }));
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
    private static ShapesDevItem Item(string id, string shape, string role, string word, string goldClass, string origin = "real") =>
        new(id, origin == "real" ? "doc-a" : "shapes-dev-authored", new[] { word }, 0, goldClass, shape, role, word, origin);
}
