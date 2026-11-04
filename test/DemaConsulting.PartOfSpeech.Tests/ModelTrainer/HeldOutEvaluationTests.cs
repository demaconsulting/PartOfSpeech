using DemaConsulting.PartOfSpeech.Trainer;

namespace DemaConsulting.PartOfSpeech.Tests.ModelTrainer;

/// <summary>
///     Tests of the held-out evaluation of the model trainer: the gate that keeps held-out files closed until a development
///     selection exists, the confidence curve and its pre-registered rule, the tallies and bootstraps, the parsers and the
///     report writers. Every test uses tiny fixtures; none reads a held-out file of the repository.
/// </summary>
public class HeldOutEvaluationTests
{
    /// <summary>
    ///     The hash that the fixture selection names for the frozen model.
    /// </summary>
    private const string ModelHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    /// <summary>
    ///     The hash that the fixture selection names for the open development file.
    /// </summary>
    private const string OpenDevHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    /// <summary>
    ///     The hash that the fixture selection names for the gold development file.
    /// </summary>
    private const string GoldDevHash = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    /// <summary>
    ///     Builds the expectation that matches <see cref="Data"/>.
    /// </summary>
    private static OperatingPointsExpectation Expectation()
    {
        var expectation = new OperatingPointsExpectation { ModelImageSha256 = ModelHash, ConfiguredImageSha256 = ModelHash };
        expectation.DevFileHashes["open/dev.tsv"] = OpenDevHash;
        expectation.DevFileHashes["eval/gold-v2/dev.tsv"] = GoldDevHash;
        return expectation;
    }

    /// <summary>
    ///     Builds a selection artifact with a strict point at 0.90 and a balanced point at 0.70.
    /// </summary>
    private static OperatingPointsData Data()
    {
        var data = new OperatingPointsData
        {
            RuleId = OperatingPointRule.Id,
            ModelImageSha256 = ModelHash,
            Temperature = 6.727,
            DevOpenAccuracyPercent = 96.0,
            OpenDevDecisions = 100,
            GoldDevDecisions = 20,
            DevFiles =
            {
                new DevFileRecord { Path = "open/dev.tsv", Sha256 = OpenDevHash },
                new DevFileRecord { Path = "eval/gold-v2/dev.tsv", Sha256 = GoldDevHash },
            },
            Points =
            {
                new PointRecord { Name = "strict", Threshold = 0.9, HarmfulPer1000Answered = 4.0, CoveragePercent = 70, Answered = 80, Harmful = 1, MaxHarmfulPer1000Answered = 5, MinCoveragePercent = 60 },
                new PointRecord { Name = "balanced", Threshold = 0.7, HarmfulPer1000Answered = 9.0, CoveragePercent = 90, Answered = 100, Harmful = 1, MaxHarmfulPer1000Answered = 10, MinCoveragePercent = 85 },
            },
        };
        data.Grid.AddRange(ConfidenceCurve.Grid);
        return data;
    }

    /// <summary>
    ///     Builds a valid selection.
    /// </summary>
    private static SelectedOperatingPoints Selection() => SelectedOperatingPoints.Validate(Data(), "dd", Expectation());

    /// <summary>
    ///     Builds a noun-verb decision.
    /// </summary>
    private static Outcome Decision(string document, double confidence, bool correct, bool harmful) =>
        new(document, correct, confidence, correct ? confidence : 0.1, true, true, harmful);

    /// <summary>
    ///     Builds the curve rows of a pooled set of 1,000 decisions where the answered and harmful counts follow a function of the threshold index.
    /// </summary>
    private static List<CurveRow> Rows(Func<int, (long Answered, long Harmful)> counts) =>
        ConfidenceCurve.Grid.Select((t, i) => new CurveRow(t, 1000, counts(i).Answered, counts(i).Harmful)).ToList();

    /// <summary>
    ///     Creates a tagger with all alternatives from the tiny trained model.
    /// </summary>
    private static Tagger CreateTagger() =>
        TinyModels.CreateTagger(TinyTrainer.Train(), null, new TaggerOptions { MaxAlternatives = TaggerOptions.MaxAlternativesLimit });

    /// <summary>
    ///     Validates that the held-out allow-list names exactly the four repository sets and that none of them is a development file.
    /// </summary>
    [Fact]
    public void DataGuard_HeldOutFiles_AreFourSetsAndNoDevelopmentFile()
    {
        // Arrange / Act
        var held = DataGuard.HeldOutFiles;

        // Assert: four sets in enumeration order, disjoint from the development allow-list
        Assert.Equal(4, held.Count);
        Assert.Equal(4, held.Distinct(StringComparer.Ordinal).Count());
        Assert.Empty(held.Intersect(DataGuard.DevelopmentFiles));
        foreach (var set in Enum.GetValues(typeof(HeldOutSet)).Cast<HeldOutSet>())
        {
            Assert.EndsWith(held[(int)set].Replace('/', Path.DirectorySeparatorChar), DataGuard.EnsureHeldOutFile(DataFiles.RepositoryRoot, set, Selection()));
        }
    }

    /// <summary>
    ///     Validates that the held-out files stay refused for development evaluation and for training.
    /// </summary>
    [Fact]
    public void DataGuard_HeldOutFiles_RefusedForDevelopmentEvaluationAndTraining()
    {
        foreach (var relative in DataGuard.HeldOutFiles)
        {
            var path = DataFiles.Repository(relative);
            Assert.Throws<InvalidOperationException>(() => DataGuard.EnsureDevelopmentFile(DataFiles.RepositoryRoot, path));
            Assert.Throws<InvalidOperationException>(() => DataGuard.EnsureTrainable(DataFiles.RepositoryRoot, path));
        }

        // The cache of the treebank is below the data refusal list only by name, never trainable either
        Assert.Throws<InvalidOperationException>(() => DataGuard.EnsureTrainable(DataFiles.RepositoryRoot, DataFiles.Repository("data/eval/ud-ewt/anything.tsv")));
    }

    /// <summary>
    ///     Validates that a held-out file cannot be resolved without a selection.
    /// </summary>
    [Fact]
    public void DataGuard_EnsureHeldOutFile_NoSelection_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => DataGuard.EnsureHeldOutFile(DataFiles.RepositoryRoot, HeldOutSet.OpenTest, null!));
        Assert.Throws<ArgumentNullException>(() => HeldOutReader.Read(DataFiles.RepositoryRoot, HeldOutSet.OpenTest, null!));
    }

    /// <summary>
    ///     Validates that a valid selection is accepted and keeps only the points that have a threshold.
    /// </summary>
    [Fact]
    public void SelectedOperatingPoints_Validate_ValidArtifact_KeepsSelectedPoints()
    {
        // Arrange: a balanced point that no threshold qualified for
        var data = Data();
        data.Points[1].Threshold = null;

        // Act
        var selection = SelectedOperatingPoints.Validate(data, "ee", Expectation());

        // Assert
        Assert.Equal("ee", selection.SelectionSha256);
        Assert.Equal(new[] { "strict" }, selection.SelectedPoints.Select(p => p.Name).ToArray());
    }

    /// <summary>
    ///     Validates that an artifact for another schema, rule, model, development file or an off-grid threshold is refused.
    /// </summary>
    [Fact]
    public void SelectedOperatingPoints_Validate_InconsistentArtifact_Throws()
    {
        // Another schema version, another rule, another model, a changed development file, a missing development file and an off-grid threshold
        var schema = Data();
        schema.SchemaVersion = 2;
        var rule = Data();
        rule.RuleId = "other";
        var model = Data();
        model.ModelImageSha256 = new string('d', 64);
        var file = Data();
        file.DevFiles[0].Sha256 = new string('d', 64);
        var missing = Data();
        missing.DevFiles.RemoveAt(1);
        var grid = Data();
        grid.Points[0].Threshold = 0.91;

        foreach (var broken in new[] { schema, rule, model, file, missing, grid })
        {
            Assert.Throws<InvalidDataException>(() => SelectedOperatingPoints.Validate(broken, "ee", Expectation()));
        }

        // The configuration must name the same model as the embedded one
        var expectation = Expectation();
        expectation.ConfiguredImageSha256 = new string('d', 64);
        Assert.Throws<InvalidDataException>(() => SelectedOperatingPoints.Validate(Data(), "ee", expectation));
    }

    /// <summary>
    ///     Validates that a tampered artifact is refused: no points, duplicate or extra development files, duplicate, extra,
    ///     missing or renamed points, another grid and thresholds that are not grid points.
    /// </summary>
    [Fact]
    public void SelectedOperatingPoints_Validate_TamperedArtifact_Throws()
    {
        var emptyPoints = Data();
        emptyPoints.Points.Clear();
        var noDevFiles = Data();
        noDevFiles.DevFiles.Clear();

        // The count of the development files is right, but one is listed twice and the other is missing
        var duplicateFile = Data();
        duplicateFile.DevFiles[1] = new DevFileRecord { Path = "open/dev.tsv", Sha256 = OpenDevHash };

        // Every expected file is there, and one more is listed twice or is not a development file
        var duplicateExtra = Data();
        duplicateExtra.DevFiles.Add(new DevFileRecord { Path = "open/dev.tsv", Sha256 = OpenDevHash });
        var extraFile = Data();
        extraFile.DevFiles.Add(new DevFileRecord { Path = "open/other.tsv", Sha256 = OpenDevHash });

        var duplicatePoint = Data();
        duplicatePoint.Points[1].Name = "strict";
        var missingBalanced = Data();
        missingBalanced.Points.RemoveAt(1);
        var extraPoint = Data();
        extraPoint.Points.Add(new PointRecord { Name = "strict", MaxHarmfulPer1000Answered = 5, MinCoveragePercent = 60 });
        var renamedPoint = Data();
        renamedPoint.Points[1].Name = "lenient";
        var otherTarget = Data();
        otherTarget.Points[0].MaxHarmfulPer1000Answered = 50;
        var otherFloor = Data();
        otherFloor.Points[1].MinCoveragePercent = 1;
        var otherGrid = Data();
        otherGrid.Grid.RemoveAt(otherGrid.Grid.Count - 1);
        var emptyGrid = Data();
        emptyGrid.Grid.Clear();
        var offGrid = Data();
        offGrid.Points[0].Threshold = 0.9001;

        foreach (var broken in new[] { emptyPoints, noDevFiles, duplicateFile, duplicateExtra, extraFile, duplicatePoint, missingBalanced, extraPoint, renamedPoint, otherTarget, otherFloor, otherGrid, emptyGrid, offGrid })
        {
            Assert.Throws<InvalidDataException>(() => SelectedOperatingPoints.Validate(broken, "ee", Expectation()));
        }

        // The untouched artifact stays valid
        Assert.NotNull(SelectedOperatingPoints.Validate(Data(), "ee", Expectation()));
    }

    /// <summary>
    ///     Validates that a threshold that is a grid point but not the one the rule selects from the development curve is refused,
    ///     and that a selection made by the rule is accepted.
    /// </summary>
    [Fact]
    public void SelectedOperatingPoints_Validate_ArbitraryGridThreshold_RefusedAgainstDerivation()
    {
        // Arrange: a pooled curve of 1,000 decisions (900 open, 100 gold) whose strict point is 0.60 and balanced point 0.50
        var rows = Rows(i => i switch
        {
            0 => (1000, 20),
            1 => (950, 7),
            2 => (590, 2),
            3 => (700, 3),
            _ => (400, 0),
        });
        var points = OperatingPointRule.Select(rows);
        Func<OperatingPointsData> artifact = () => OperatingPointsData.Build(
            ModelHash,
            6.727,
            96.0,
            900,
            100,
            new[] { new DevFileRecord { Path = "open/dev.tsv", Sha256 = OpenDevHash }, new DevFileRecord { Path = "eval/gold-v2/dev.tsv", Sha256 = GoldDevHash } },
            points);
        var expectation = Expectation();
        expectation.DerivePooledCurve = () => rows;

        // Act / Assert: the artifact the rule produces is valid
        var selection = SelectedOperatingPoints.Validate(artifact(), "ee", expectation);
        Assert.Equal(new[] { 0.60, 0.50 }, selection.SelectedPoints.Select(p => p.Threshold!.Value).ToArray());

        // An arbitrary grid threshold (valid on its own) differs from the derivation
        var arbitrary = artifact();
        arbitrary.Points[0].Threshold = 0.99;
        Assert.Throws<InvalidDataException>(() => SelectedOperatingPoints.Validate(arbitrary, "ee", expectation));
        Assert.NotNull(SelectedOperatingPoints.Validate(arbitrary, "ee", Expectation()));

        // A point turned into none, a changed count or a changed rate differs as well
        var none = artifact();
        none.Points[1].Threshold = null;
        var harmful = artifact();
        harmful.Points[0].Harmful += 1;
        var answered = artifact();
        answered.Points[1].Answered += 1;
        var coverage = artifact();
        coverage.Points[0].CoveragePercent += 0.5;
        var decisions = artifact();
        decisions.OpenDevDecisions += 1;
        foreach (var broken in new[] { none, harmful, answered, coverage, decisions })
        {
            Assert.Throws<InvalidDataException>(() => SelectedOperatingPoints.Validate(broken, "ee", expectation));
        }

        // The derivation is only consulted after the cheap checks, and a curve with no qualifying point yields none and none
        var nothing = Rows(_ => (1000, 50));
        var noneData = OperatingPointsData.Build(ModelHash, 6.727, 96.0, 900, 100, artifact().DevFiles, OperatingPointRule.Select(nothing));
        expectation.DerivePooledCurve = () => nothing;
        Assert.Empty(SelectedOperatingPoints.Validate(noneData, "ee", expectation).SelectedPoints);
    }

    /// <summary>
    ///     Validates that the pooled development curve equals the curve of the development selection of the same outcomes.
    /// </summary>
    [Fact]
    public void DevDerivation_PooledCurve_FixtureOutcomes_EqualsSelectionCurve()
    {
        // Arrange
        var open = Enumerable.Range(0, 200).Select(i => Decision("open:d" + (i % 20), 0.46 + ((i % 54) / 100.0), i % 7 != 0, i % 7 == 0)).ToList();
        var gold = Enumerable.Range(0, 40).Select(i => Decision("gold:d" + (i % 30), 0.50 + ((i % 49) / 100.0), i % 11 != 0, i % 11 == 0)).ToList();

        // Act
        var derived = DevDerivation.PooledCurve(open, gold);
        var selection = DevSelection.Create(ModelHash, 6.727, open, gold, 10, 1);
        var expected = ConfidenceCurve.Of(selection.Pooled);

        // Assert
        Assert.Equal(expected.Count, derived.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Threshold, derived[i].Threshold);
            Assert.Equal(expected[i].Decisions, derived[i].Decisions);
            Assert.Equal(expected[i].Answered, derived[i].Answered);
            Assert.Equal(expected[i].Harmful, derived[i].Harmful);
        }
    }

    /// <summary>
    ///     Validates that the header check accepts the announced set and refuses development splits, other sets and unannounced files.
    /// </summary>
    [Theory]
    [InlineData(0, "# corpus = open corpus, test split (UPOS projection)", true)]
    [InlineData(0, "# corpus = open corpus, dev split (UPOS projection)", false)]
    [InlineData(1, "# corpus = second all-words gold set (v2), test split: part of speech", true)]
    [InlineData(1, "# corpus = second all-words gold set (v2), dev split: part of speech", false)]
    [InlineData(3, "# corpus = DemaConsulting.PartOfSpeech simplified-technical-English challenge set (UPOS projection)", true)]
    [InlineData(3, "# corpus = open corpus, test split", false)]
    [InlineData(2, "id\tshape\tlemma\tgold\ttarget_index\tsentence", true)]
    [InlineData(2, "# corpus = open corpus, test split", false)]
    public void DataGuard_EnsureHeldOutHeader_AnnouncedSet_AcceptedOthersRefused(int setOrdinal, string header, bool accepted)
    {
        // Act
        var set = (HeldOutSet)setOrdinal;
        var exception = Record.Exception(() => DataGuard.EnsureHeldOutHeader(new[] { header, "# license = x" }, set, "fixture"));

        // Assert
        if (accepted)
        {
            Assert.Null(exception);
        }
        else
        {
            Assert.IsType<InvalidDataException>(exception);
        }
    }

    /// <summary>
    ///     Validates that the run-once check refuses an existing results file or lock file and accepts a missing one.
    /// </summary>
    [Fact]
    public void DataGuard_EnsureNotEvaluated_ExistingResultsFile_Throws()
    {
        // Arrange
        var folder = Path.Combine(Path.GetTempPath(), "pos-run-once-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "results.json");
        try
        {
            // Act / Assert: missing file passes, existing file is refused
            DataGuard.EnsureNotEvaluated(path);
            File.WriteAllText(path, "{}");
            Assert.Throws<InvalidOperationException>(() => DataGuard.EnsureNotEvaluated(path));
            File.Delete(path);

            // A lock file next to the results file is refused as well
            DataGuard.EnsureNotEvaluated(path);
            File.WriteAllText(DataGuard.RunLockPath(path), string.Empty);
            Assert.Throws<InvalidOperationException>(() => DataGuard.EnsureNotEvaluated(path));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    /// <summary>
    ///     Validates that the run lock is created exclusively before scoring, blocks a second run, and is replaced by the results file on completion.
    /// </summary>
    [Fact]
    public void DataGuard_AcquireRunLock_ExclusiveAndRemovedOnCompletion()
    {
        // Arrange
        var folder = Path.Combine(Path.GetTempPath(), "pos-run-lock-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "test-results.json");
        var lockPath = DataGuard.RunLockPath(path);
        try
        {
            // Act: the first run takes the lock, which is a file next to the results file
            var held = DataGuard.AcquireRunLock(path);
            Assert.Equal(lockPath, held.LockPath);
            Assert.EndsWith(".running", lockPath, StringComparison.Ordinal);
            Assert.True(File.Exists(lockPath));
            Assert.False(File.Exists(path));

            // Assert: a second run is refused while the lock is held, and the lock stays
            Assert.Throws<InvalidOperationException>(() => DataGuard.AcquireRunLock(path));
            Assert.True(File.Exists(lockPath));

            // Completing writes the results exclusively and removes the lock; afterwards the results file is the marker
            held.Complete("{\"done\": true}\n");
            Assert.False(File.Exists(lockPath));
            Assert.Equal("{\"done\": true}\n", File.ReadAllText(path));
            Assert.Throws<InvalidOperationException>(() => DataGuard.AcquireRunLock(path));
            Assert.False(File.Exists(lockPath));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    /// <summary>
    ///     Validates that a lock that was not completed (an aborted run) blocks every later run until it is deleted by hand.
    /// </summary>
    [Fact]
    public void DataGuard_AcquireRunLock_StaleLock_BlocksUntilDeletedByHand()
    {
        // Arrange
        var folder = Path.Combine(Path.GetTempPath(), "pos-run-stale-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "test-results.json");
        var lockPath = DataGuard.RunLockPath(path);
        try
        {
            // Act: a run takes the lock and is aborted (disposed without completing)
            using (DataGuard.AcquireRunLock(path))
            {
                Assert.True(File.Exists(lockPath));
            }

            // Assert: the lock stays, no results file exists, and a new run is refused with the manual-deletion message
            Assert.True(File.Exists(lockPath));
            Assert.False(File.Exists(path));
            var refused = Assert.Throws<InvalidOperationException>(() => DataGuard.AcquireRunLock(path));
            Assert.Contains("RUN-LOG", refused.Message, StringComparison.Ordinal);
            Assert.Throws<InvalidOperationException>(() => DataGuard.EnsureNotEvaluated(path));

            // Only the explicit deletion releases it
            File.Delete(lockPath);
            using var again = DataGuard.AcquireRunLock(path);
            Assert.True(File.Exists(lockPath));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    /// <summary>
    ///     Validates that a run whose results file appears while it is running does not overwrite it and keeps its lock.
    /// </summary>
    [Fact]
    public void RunLock_Complete_ExistingResultsFile_ThrowsAndKeepsLock()
    {
        // Arrange
        var folder = Path.Combine(Path.GetTempPath(), "pos-run-complete-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "test-results.json");
        try
        {
            var held = DataGuard.AcquireRunLock(path);
            File.WriteAllText(path, "first");

            // Act / Assert: the exclusive write fails, the first file is untouched and the lock remains
            Assert.ThrowsAny<IOException>(() => held.Complete("second"));
            Assert.Equal("first", File.ReadAllText(path));
            Assert.True(File.Exists(DataGuard.RunLockPath(path)));
            held.Dispose();
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    /// <summary>
    ///     Validates that of many concurrent attempts to start the run exactly one gets the lock.
    /// </summary>
    [Fact]
    public void DataGuard_AcquireRunLock_ConcurrentAttempts_ExactlyOneWins()
    {
        // Arrange
        var folder = Path.Combine(Path.GetTempPath(), "pos-run-race-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "test-results.json");
        var winners = new System.Collections.Concurrent.ConcurrentBag<RunLock>();
        var refused = 0;
        try
        {
            // Act
            Parallel.For(0, 16, new ParallelOptions { MaxDegreeOfParallelism = 8 }, _ =>
            {
                try
                {
                    winners.Add(DataGuard.AcquireRunLock(path));
                }
                catch (InvalidOperationException)
                {
                    Interlocked.Increment(ref refused);
                }
            });

            // Assert
            Assert.Single(winners);
            Assert.Equal(15, refused);
        }
        finally
        {
            foreach (var winner in winners)
            {
                winner.Dispose();
            }

            Directory.Delete(folder, true);
        }
    }

    /// <summary>
    ///     Validates that the dump folder is refused inside the repository (also through a detour) and accepted outside.
    /// </summary>
    [Fact]
    public void DataGuard_EnsureOutsideRepository_InsideRefused_OutsideAccepted()
    {
        // Arrange
        var root = DataFiles.RepositoryRoot;

        // Act / Assert
        Assert.Throws<InvalidOperationException>(() => DataGuard.EnsureOutsideRepository(root, root));
        Assert.Throws<InvalidOperationException>(() => DataGuard.EnsureOutsideRepository(root, Path.Combine(root, "docs", "evaluation")));
        Assert.Throws<InvalidOperationException>(() => DataGuard.EnsureOutsideRepository(root, Path.Combine(root, "docs", "..", "data", "dump")));
        var outside = Path.Combine(Path.GetTempPath(), "pos-dump-" + Guid.NewGuid().ToString("N"));
        Assert.Equal(Path.GetFullPath(outside), DataGuard.EnsureOutsideRepository(root, outside));
    }

    /// <summary>
    ///     Validates that the guarded reader checks the data manifest and the header of a file in a fixture repository.
    /// </summary>
    [Fact]
    public void HeldOutReader_Read_FixtureRepository_VerifiesHashAndHeader()
    {
        // Arrange: a fixture root with one corpus file and a manifest that records its hash
        var root = Path.Combine(Path.GetTempPath(), "pos-held-out-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "data", "open"));
        var content = "# corpus = fixture, test split\n\n# sent_id = s1\n# source_id = d1\nA\tDET\tunanimous\n";
        var file = Path.Combine(root, "data", "open", DataGuard.HeldOutFiles[0].Substring("data/open/".Length));
        File.WriteAllText(file, content);
        var manifest = Path.Combine(root, "data", "MANIFEST.tsv");
        File.WriteAllText(manifest, "# comment\n" + DataGuard.HeldOutFiles[0].Substring("data/".Length) + "\t10\t" + ModelBuilder.Sha256Hex(new System.Text.UTF8Encoding(false).GetBytes(content)) + "\t1\t-\n");
        try
        {
            // Act: the matching file is read
            var read = HeldOutReader.Read(root, HeldOutSet.OpenTest, Selection());

            // Assert: lines and hash; then a changed file is refused, and so is a wrong header with a matching hash
            Assert.Equal(5, read.Lines.Count);
            Assert.Equal(ModelBuilder.Sha256Hex(new System.Text.UTF8Encoding(false).GetBytes(content)), read.Sha256);
            File.WriteAllText(file, content + "B\tNOUN\tunanimous\n");
            Assert.Throws<InvalidDataException>(() => HeldOutReader.Read(root, HeldOutSet.OpenTest, Selection()));
            var wrong = content.Replace("test split", "dev split");
            File.WriteAllText(file, wrong);
            File.WriteAllText(manifest, DataGuard.HeldOutFiles[0].Substring("data/".Length) + "\t10\t" + ModelBuilder.Sha256Hex(new System.Text.UTF8Encoding(false).GetBytes(wrong)) + "\t1\t-\n");
            Assert.Throws<InvalidDataException>(() => HeldOutReader.Read(root, HeldOutSet.OpenTest, Selection()));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    ///     Validates that the treebank reader verifies the pinned source and reports a missing cache with the recipe.
    /// </summary>
    [Fact]
    public void HeldOutReader_ReadUd_FixtureCache_VerifiesPin()
    {
        // Arrange: a fixture root with a pin file, a cached source and a mapped table
        var root = Path.Combine(Path.GetTempPath(), "pos-ud-" + Guid.NewGuid().ToString("N"));
        var cache = Path.Combine(root, "cache");
        Directory.CreateDirectory(cache);
        Directory.CreateDirectory(Path.Combine(root, "tools", "data", "pins"));
        var source = new System.Text.UTF8Encoding(false).GetBytes("fixture source\n");
        File.WriteAllBytes(Path.Combine(cache, DataGuard.UdSourceFile), source);
        var mappedText = "# columns = form, mapped_upos, raw_upos, rule\n\n# sent_id = x-0001\nA\tDET\tDET\t_\n";
        var mappedPin = ModelBuilder.Sha256Hex(new System.Text.UTF8Encoding(false).GetBytes(mappedText));
        File.WriteAllText(Path.Combine(cache, DataGuard.UdMappedFile), mappedText);
        var pins = Path.Combine(root, DataGuard.UdPinsFile.Replace('/', Path.DirectorySeparatorChar));
        File.WriteAllText(pins, "# pins\n" + DataGuard.UdSourceFile + "\t" + ModelBuilder.Sha256Hex(source) + "\n");
        try
        {
            // Act / Assert: the pinned cache is read; a changed source and a missing table are refused with the recipe
            var read = HeldOutReader.ReadUd(root, "cache", Selection(), mappedPin);
            Assert.Equal(ModelBuilder.Sha256Hex(source), read.SourceSha256);
            Assert.Equal(mappedPin, read.Sha256);
            Assert.Equal(4, read.Lines.Count);

            // A mapped table that differs from the pin is refused, even though the pinned source is intact: the run pins the table of the held-out run
            Assert.Throws<InvalidDataException>(() => HeldOutReader.ReadUd(root, "cache", Selection()));
            File.WriteAllText(Path.Combine(cache, DataGuard.UdMappedFile), mappedText.Replace("DET\tDET", "NOUN\tDET"));
            Assert.Contains("pinned", Assert.Throws<InvalidDataException>(() => HeldOutReader.ReadUd(root, "cache", Selection(), mappedPin)).Message, StringComparison.Ordinal);
            File.WriteAllText(Path.Combine(cache, DataGuard.UdMappedFile), mappedText);

            File.WriteAllBytes(Path.Combine(cache, DataGuard.UdSourceFile), new System.Text.UTF8Encoding(false).GetBytes("changed\n"));
            Assert.Contains("fetch", Assert.Throws<InvalidDataException>(() => HeldOutReader.ReadUd(root, "cache", Selection(), mappedPin)).Message, StringComparison.Ordinal);
            File.Delete(Path.Combine(cache, DataGuard.UdMappedFile));
            Assert.Throws<InvalidDataException>(() => HeldOutReader.ReadUd(root, "cache", Selection(), mappedPin));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    /// <summary>
    ///     Validates that the probe parser removes the braces, keeps the shape as the group and refuses rows whose braces do not mark the target.
    /// </summary>
    [Fact]
    public void HeldOutSets_ParseProbe_Rows_BracesRemovedAndValidated()
    {
        // Arrange
        var header = "id\tshape\tlemma\tgold\ttarget_index\tsentence";
        var good = new[] { header, "p-1\tv-imperative-initial\tclose\tVerb\t0\t{Close} the valve .", "p-2\tn-measure\tfoot\tNoun\t3\tIt is 5 {foot} long" };

        // Act
        var rows = HeldOutSets.ParseProbe(good, "fixture");

        // Assert
        Assert.Equal(2, rows.Count);
        Assert.Equal("Close", rows[0].Form);
        Assert.Equal("v-imperative-initial", rows[0].Group);
        Assert.Equal("probe:p-2", rows[1].DocumentId);
        Assert.Equal(new[] { "It", "is", "5", "foot", "long" }, rows[1].Tokens);
        foreach (var bad in new[]
        {
            "p-1\ts\tclose\tVerb\t1\t{Close} the valve .",
            "p-1\ts\tclose\tVerb\t0\tClose the valve .",
            "p-1\ts\tclose\tVerb\t0\t{Close} {the} valve .",
            "p-1\ts\tclose\tAdverb\t0\t{Close} the valve .",
            "p-1\ts\tclose\tVerb\t9\t{Close} the valve .",
            "p-1\ts\tclose\tVerb",
        })
        {
            Assert.Throws<InvalidDataException>(() => HeldOutSets.ParseProbe(new[] { header, bad }, "fixture"));
        }

        Assert.Throws<InvalidDataException>(() => HeldOutSets.ParseProbe(new[] { "id\tshape\tsentence" }.Concat(new[] { "x" }), "fixture"));
    }

    /// <summary>
    ///     Validates that the treebank parser reads both tags, the rule and the document of a sentence, and refuses malformed rows.
    /// </summary>
    [Fact]
    public void HeldOutSets_ParseUd_MappedTable_ReadsTagsRulesAndDocuments()
    {
        // Arrange
        var lines = new[]
        {
            "# cache file, never commit", "# columns = form, mapped_upos, raw_upos, rule", string.Empty,
            "# sent_id = doc-a-0001", "# text = My dog #", "My\tDET\tPRON\tpossessive-det", "dog\tNOUN\tNOUN\t_", "#\tSYM\tSYM\t_", string.Empty,
            "# sent_id = doc-a-0002", "# text = Go", "Go\tVERB\tVERB\t_", string.Empty,
        };

        // Act
        var sentences = HeldOutSets.ParseUd(lines, "fixture");

        // Assert
        Assert.Equal(2, sentences.Count);
        Assert.Equal("doc-a", sentences[0].DocumentId);
        Assert.Equal("doc-a", sentences[1].DocumentId);
        Assert.Equal(new[] { "My", "dog", "#" }, sentences[0].Tokens);
        Assert.Equal(Upos.Det, sentences[0].Mapped[0]);
        Assert.Equal(Upos.Pron, sentences[0].Raw[0]);
        Assert.Equal("possessive-det", sentences[0].Rules[0]);
        Assert.Throws<InvalidDataException>(() => HeldOutSets.ParseUd(new[] { "# sent_id = x-1", "a\tNOUN\tNOUN" }, "fixture"));
        Assert.Throws<InvalidDataException>(() => HeldOutSets.ParseUd(new[] { "# sent_id = x-1", "a\tBOGUS\tNOUN\t_" }, "fixture"));
        Assert.Throws<InvalidDataException>(() => HeldOutSets.ParseUd(new[] { "a\tNOUN\tNOUN\t_" }, "fixture"));
    }

    /// <summary>
    ///     Validates that the gold parser carries the status and the agreement of a row for the cleaner subset.
    /// </summary>
    [Fact]
    public void DevSets_ParseGold_StatusAndAgreement_Carried()
    {
        // Arrange
        var lines = new[] { "# columns = id doc_id pos target status agreement sentence", "g-1\tdoc-a\tNoun\t1\tagreed\tfull\tThe dog barks", "g-2\tdoc-b\tVerb\t2\tadjudicated\tpos\tThe dog barks" };

        // Act
        var targets = DevSets.ParseGold(lines, "fixture");

        // Assert
        Assert.Equal("full", targets[0].Agreement);
        Assert.Equal("adjudicated", targets[1].Status);
        Assert.Equal(DevSets.ParseGoldDev(lines, "fixture").Count, targets.Count);
    }

    /// <summary>
    ///     Validates that the tally computes the same metrics as <see cref="Metrics.Compute"/> on the same outcomes.
    /// </summary>
    [Fact]
    public void EvalTally_Of_Outcomes_AgreesWithMetricsCompute()
    {
        // Arrange: varied outcomes over several documents
        var outcomes = Enumerable.Range(0, 200)
            .Select(i => new Outcome("d" + (i % 7), i % 9 != 0, 0.3 + ((i * 37 % 70) / 100.0), GoldProbability(i), i % 3 == 0, i % 2 == 0, i % 2 == 0 && i % 9 == 0 && i % 5 != 0))
            .ToList();

        static double GoldProbability(int i)
        {
            if (i % 9 != 0)
            {
                return 0.8;
            }

            return i % 4 == 0 ? 0.0 : 0.2;
        }

        // Act
        var expected = Metrics.Compute(outcomes);
        var tally = EvalTally.Of(outcomes);

        // Assert
        Assert.Equal(expected.Tokens, tally.Tokens);
        Assert.Equal(expected.Accuracy, tally.Accuracy, 9);
        Assert.Equal(expected.AmbiguousTokens, tally.Ambiguous);
        Assert.Equal(expected.AmbiguousAccuracy, tally.AmbiguousAccuracy, 9);
        Assert.Equal(expected.NounVerbDecisions, tally.Decisions);
        Assert.Equal(expected.HarmfulErrors, tally.Harmful);
        Assert.Equal(expected.HarmfulPer1000, tally.HarmfulPer1000, 9);
        Assert.Equal(expected.Nll, tally.Nll, 9);
        Assert.Equal(expected.Ece, tally.Ece, 9);
        Assert.Equal(expected.OutsideCandidates, tally.OutsideCandidates);

        // The sum of the per-document tallies is the tally
        var sum = new EvalTally();
        foreach (var part in EvalTally.ByDocument(outcomes))
        {
            sum.Add(part);
        }

        Assert.Equal(7, EvalTally.ByDocument(outcomes).Count);
        Assert.Equal(tally.Correct, sum.Correct);
        Assert.Equal(tally.Ece, sum.Ece, 9);
        Assert.Equal(tally.HarmfulAt, sum.HarmfulAt);
    }

    /// <summary>
    ///     Validates the curve, the coverage and the harmful rates on hand-computed outcomes, and that coverage never grows with the threshold.
    /// </summary>
    [Fact]
    public void ConfidenceCurve_Of_HandComputedOutcomes_MatchesExpectedRows()
    {
        // Arrange: seven decisions; the harmful ones are the wrong answers at 0.85 and 0.50, the wrong answer at 0.30 is below the harmful limit
        var outcomes = new[]
        {
            Decision("d1", 0.95, true, false),
            Decision("d1", 0.92, true, false),
            Decision("d2", 0.85, false, true),
            Decision("d2", 0.60, true, false),
            Decision("d3", 0.50, false, true),
            Decision("d3", 0.47, true, false),
            Decision("d4", 0.30, false, false),
        };

        // Act
        var rows = ConfidenceCurve.Of(EvalTally.Of(outcomes));

        // Assert: grid and counts
        Assert.Equal(15, rows.Count);
        Assert.Equal(0.45, rows[0].Threshold);
        Assert.Equal(0.99, rows[14].Threshold);
        Assert.All(rows, r => Assert.Equal(7, r.Decisions));
        var at = rows.ToDictionary(r => OperatingPointRule.Format(r.Threshold));
        Check("0.45", 6, 2);
        Check("0.50", 5, 2);
        Check("0.55", 4, 1);
        Check("0.85", 3, 1);
        Check("0.90", 2, 0);
        Check("0.99", 0, 0);

        void Check(string key, long answered, long harmful)
        {
            Assert.Equal(answered, at[key].Answered);
            Assert.Equal(harmful, at[key].Harmful);
        }
        Assert.Equal(1000.0 * 2 / 6, at["0.45"].HarmfulPer1000Answered, 9);
        Assert.Equal(1000.0 * 2 / 7, at["0.45"].HarmfulPer1000Decisions, 9);
        Assert.Equal(100.0 * 2 / 7, at["0.90"].CoveragePercent, 9);
        Assert.Equal(0, at["0.99"].HarmfulPer1000Answered);
        for (var i = 1; i < rows.Count; i++)
        {
            Assert.True(rows[i].Answered <= rows[i - 1].Answered);
        }

        // The harmful count of the 0.45 row is the harmful count of the metrics
        Assert.Equal(Metrics.Compute(outcomes).HarmfulErrors, at["0.45"].Harmful);
    }

    /// <summary>
    ///     Validates the reliability table: every decision lands in exactly one bin and the bins carry their means.
    /// </summary>
    [Fact]
    public void ConfidenceCurve_Reliability_Decisions_OneBinEach()
    {
        // Arrange
        var outcomes = new[] { Decision("d", 0.95, true, false), Decision("d", 0.97, false, true), Decision("d", 0.55, true, false), Decision("d", 0.995, true, false) };

        // Act
        var table = ConfidenceCurve.Reliability(EvalTally.Of(outcomes));

        // Assert
        Assert.Equal(4, table.Sum(r => r.Decisions));
        var high = table[6];
        Assert.Equal(2, high.Decisions);
        Assert.Equal(50.0, high.CorrectPercent, 9);
        Assert.Equal(1, high.Harmful);
        Assert.Equal(1, table[7].Decisions);
        Assert.Equal(1, table[1].Decisions);
        Assert.Equal(1.0, table[table.Count - 1].High);
    }

    /// <summary>
    ///     Validates that the rule picks the first grid threshold that meets both the target and the floor, separately for the strict and the balanced point.
    /// </summary>
    [Fact]
    public void OperatingPointRule_Select_FirstQualifyingThreshold_StrictAndBalanced()
    {
        // Arrange: harmful per 1,000 answered falls with the threshold while coverage falls too
        // index: 0 (0.45): 20.0 at 100%; 1 (0.50): 7.4 at 95%; 2 (0.55): 3.4 at 59%; 3 (0.60): 4.3 at 70%; then 0 at 40%
        var rows = Rows(i => i switch
        {
            0 => (1000, 20),
            1 => (950, 7),
            2 => (590, 2),
            3 => (700, 3),
            _ => (400, 0),
        });

        // Act
        var points = OperatingPointRule.Select(rows);

        // Assert: strict skips 0.55 (coverage 59%), balanced takes 0.50 (7.4 at 95%)
        Assert.Equal("strict", points[0].Name);
        Assert.Equal(0.60, points[0].Threshold);
        Assert.Equal("balanced", points[1].Name);
        Assert.Equal(0.50, points[1].Threshold);
        Assert.Equal(5.0, points[0].MaxHarmfulPer1000Answered);
        Assert.Equal(60.0, points[0].MinCoveragePercent);
        Assert.Equal(10.0, points[1].MaxHarmfulPer1000Answered);
        Assert.Equal(85.0, points[1].MinCoveragePercent);
    }

    /// <summary>
    ///     Validates the boundaries of the rule: a target and a floor met exactly qualify, a point that no threshold meets is none, and nothing answered never qualifies.
    /// </summary>
    [Fact]
    public void OperatingPointRule_Select_BoundariesAndNone_AreNotRelaxed()
    {
        // Arrange: exactly 5.0 per 1,000 answered (3 of 600) at exactly 60% coverage qualifies as strict
        var exact = OperatingPointRule.Select(Rows(i => i == 4 ? (600, 3) : (1000, 50)));
        Assert.Equal(0.65, exact[0].Threshold);
        Assert.Equal(5.0, exact[0].Row!.HarmfulPer1000Answered, 9);
        Assert.Equal(60.0, exact[0].Row!.CoveragePercent, 9);

        // A hair above the floor and the target does not qualify (3 of 599 is 5.008 per 1,000 at 59.9%)
        var near = OperatingPointRule.Select(Rows(i => i == 4 ? (599, 3) : (1000, 50)));
        Assert.Null(near[0].Threshold);

        // No threshold qualifies: both points are none and carry no row
        var none = OperatingPointRule.Select(Rows(_ => (1000, 50)));
        Assert.All(none, p => Assert.Null(p.Row));

        // Nothing answered has a rate of zero but must not qualify
        var empty = OperatingPointRule.Select(Rows(_ => (0, 0)));
        Assert.All(empty, p => Assert.Null(p.Threshold));
    }

    /// <summary>
    ///     Validates that the selection of a fixture development set is deterministic and consistent with its curve and its artifact.
    /// </summary>
    [Fact]
    public void DevSelection_Create_FixtureOutcomes_DeterministicAndConsistent()
    {
        // Arrange: 400 open decisions and 60 gold decisions; the harmful errors sit at the lowest confidences, so the higher thresholds are safe
        var open = Enumerable.Range(0, 400).Select(i => Decision("open:d" + (i % 20), 0.46 + ((i % 54) / 100.0), !(i % 54 <= 4 && i % 4 == 0), i % 54 <= 4 && i % 4 == 0)).ToList();
        var gold = Enumerable.Range(0, 60).Select(i => Decision("gold:d" + (i % 30), 0.50 + ((i % 49) / 100.0), i % 49 > 1, i % 49 <= 1)).ToList();

        // Act
        var first = DevSelection.Create(ModelHash, 6.727, open, gold, 100, 1);
        var second = DevSelection.Create(ModelHash, 6.727, open, gold, 100, 1);

        // Assert
        Assert.Equal(460, first.Pooled.Decisions);
        Assert.NotNull(first.Points[0].Row);
        Assert.Equal(first.Open.Harmful + first.Gold.Harmful, first.Pooled.Harmful);
        Assert.Equal(EvaluationReport.RenderDev(first), EvaluationReport.RenderDev(second));
        var artifact = first.ToArtifact(new[] { new DevFileRecord { Path = "open/dev.tsv", Sha256 = OpenDevHash }, new DevFileRecord { Path = "eval/gold-v2/dev.tsv", Sha256 = GoldDevHash } });
        Assert.Equal(OperatingPointRule.Id, artifact.RuleId);
        Assert.Equal(2, artifact.Points.Count);
        Assert.Equal(first.Points.Select(p => p.Threshold).ToArray(), artifact.Points.Select(p => p.Threshold).ToArray());
        Assert.Equal(ConfidenceCurve.Grid.Count, artifact.Grid.Count);
        foreach (var point in first.Points.Where(p => p.Row != null))
        {
            Assert.True(point.Row!.HarmfulPer1000Answered <= point.MaxHarmfulPer1000Answered);
            Assert.True(point.Row.CoveragePercent >= point.MinCoveragePercent);
        }

        var report = EvaluationReport.RenderDev(first);
        Assert.Contains(OperatingPointRule.Id, report, StringComparison.Ordinal);
        Assert.Contains("## Selected operating points", report, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", report, StringComparison.Ordinal);
        Assert.DoesNotContain("\n\n\n", report, StringComparison.Ordinal);
        Assert.EndsWith("|\n", report, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Validates that identical lists are tied, a clearly better list excludes zero, the interval is deterministic for a seed and single-item clusters work.
    /// </summary>
    [Fact]
    public void ClusterBootstrap_Intervals_TiedBetterDeterministicAndSingleItemClusters()
    {
        // Arrange: ten documents of thirty tokens; one list wrong on 1 in 13, the other on 1 in 3
        var better = Enumerable.Range(0, 300).Select(i => new Outcome("d" + (i % 10), i % 13 != 0, 0.9, 0.9, true, false, false)).ToList();
        var worse = Enumerable.Range(0, 300).Select(i => new Outcome("d" + (i % 10), i % 3 != 0, 0.9, 0.9, true, false, false)).ToList();
        var a = EvalTally.ByDocument(better);
        var b = EvalTally.ByDocument(worse);

        // Act: the paired difference is the same statistic evaluated on the same draw of documents
        var tied = ClusterBootstrap.Interval(a, t => t.Accuracy - (100.0 * t.Correct / t.Tokens), 200, 3);
        var single = ClusterBootstrap.Interval(a, t => t.Accuracy, 200, 3);
        var again = ClusterBootstrap.Interval(a, t => t.Accuracy, 200, 3);

        // Assert
        Assert.True(tied.ContainsZero);
        Assert.Equal(single.Low, again.Low);
        Assert.Equal(single.High, again.High);
        Assert.InRange(single.Difference, single.Low, single.High);
        Assert.True(EvalTally.Of(better).Accuracy - EvalTally.Of(worse).Accuracy > 15);

        // Single-item clusters (a set without documents): one cluster for every row; another seed gives another draw
        var items = better.Select((o, i) => new Outcome("row" + i, o.Correct, o.Confidence, o.GoldProbability, o.Ambiguous, o.NounVerb, o.Harmful)).ToList();
        Assert.Equal(300, EvalTally.ByDocument(items).Count);
        var item = ClusterBootstrap.Interval(EvalTally.ByDocument(items), t => t.Accuracy, 500, 3);
        var itemOther = ClusterBootstrap.Interval(EvalTally.ByDocument(items), t => t.Accuracy, 500, 4);
        Assert.True(Math.Abs(item.Low - itemOther.Low) + Math.Abs(item.High - itemOther.High) > 0);
        Assert.InRange(item.Low, 85, 100);
        Assert.Throws<ArgumentException>(() => ClusterBootstrap.Interval(new List<EvalTally>(), t => t.Accuracy, 10, 1));
        Assert.Equal(b.Count, a.Count);
    }

    /// <summary>
    ///     Validates that the paired difference of an operating point against 0.45 excludes zero when the point removes every harmful error.
    /// </summary>
    [Fact]
    public void EvaluationReport_Summarize_OperatingPoint_PairedDifferenceAgainstReference()
    {
        // Arrange: harmful errors exist only at confidences below 0.7, so the point 0.70 removes all of them; 40 documents
        var tokens = new List<EvalToken>();
        for (var i = 0; i < 400; i++)
        {
            var wrong = i % 8 == 0;
            var confidence = wrong ? 0.50 + ((i % 20) / 100.0) : 0.90;
            tokens.Add(Token("fixture-set", "doc" + (i / 10), "s" + i, wrong, confidence));
        }

        var points = new[] { new PointRecord { Name = "balanced", Threshold = 0.7 } };

        // Act
        var summary = EvaluationReport.Summarize("fixture-set", tokens, points, 200, 1);

        // Assert
        Assert.Equal(400, summary.Tokens);
        Assert.Equal(40, summary.Documents);
        Assert.Equal(2, summary.Points.Count);
        Assert.Equal("reference", summary.Points[0].Name);
        Assert.Equal(0.45, summary.Points[0].Threshold);
        Assert.Equal(50, summary.Harmful);
        Assert.Equal(0, summary.Points[1].Harmful);
        Assert.True(summary.Points[1].DeltaHarmfulPer1000Decisions < 0);
        Assert.True(summary.Points[1].DeltaHigh < 0);
        Assert.Equal(0, summary.Points[0].DeltaHarmfulPer1000Decisions);
        Assert.InRange(summary.Accuracy, summary.AccuracyLow, summary.AccuracyHigh);
    }

    /// <summary>
    ///     Validates the stop conditions of the protocol: each fires on its own and nothing fires on healthy results.
    /// </summary>
    [Fact]
    public void EvaluationReport_Tripwires_EachConditionFiresAlone()
    {
        // Arrange: healthy results first
        var selection = Selection();
        ResultsData Healthy()
        {
            var results = new ResultsData();
            results.Sets.Add(new SetRecord
            {
                Name = HeldOutEvaluator.Names.OpenTest,
                Tokens = 1000,
                Accuracy = 95.5,
                AccuracyLow = 95.0,
                AccuracyHigh = 96.0,
                Ece = 1.0,
                Decisions = 100,
                Points = { new PointResult { Name = "strict", HarmfulPer1000Answered = 5.0 }, new PointResult { Name = "balanced", HarmfulPer1000Answered = 12.0 } },
            });
            results.Sets.Add(new SetRecord { Name = HeldOutEvaluator.Names.Probe, Tokens = 560, Harmful = 20 });
            return results;
        }

        // Act / Assert: healthy
        Assert.Empty(EvaluationReport.Tripwires(Healthy(), selection));

        // Gold outside the candidates for more than 1 percent of a set
        var outside = Healthy();
        outside.Sets[0].OutsideCandidates = 11;
        Assert.Single(EvaluationReport.Tripwires(outside, selection));

        // Accuracy more than 2 points below development with an interval that excludes it (and not when the interval reaches it)
        var accuracy = Healthy();
        accuracy.Sets[0].Accuracy = 93.5;
        accuracy.Sets[0].AccuracyLow = 93.0;
        accuracy.Sets[0].AccuracyHigh = 94.0;
        Assert.Single(EvaluationReport.Tripwires(accuracy, selection));
        accuracy.Sets[0].AccuracyHigh = 96.5;
        Assert.Empty(EvaluationReport.Tripwires(accuracy, selection));

        // A point whose harmful rate is more than twice its development rate (strict 4.0 on dev: 8.1 fires, 8.0 does not)
        var doubled = Healthy();
        doubled.Sets[0].Points[0].HarmfulPer1000Answered = 8.1;
        Assert.Single(EvaluationReport.Tripwires(doubled, selection));
        doubled.Sets[0].Points[0].HarmfulPer1000Answered = 8.0;
        Assert.Empty(EvaluationReport.Tripwires(doubled, selection));

        // The probe: more than 5 percent of its rows are harmful errors (28 of 560 does not fire, 29 does)
        var probe = Healthy();
        probe.Sets[1].Harmful = 28;
        Assert.Empty(EvaluationReport.Tripwires(probe, selection));
        probe.Sets[1].Harmful = 29;
        Assert.Single(EvaluationReport.Tripwires(probe, selection));

        // The calibration error above 3 percent
        var ece = Healthy();
        ece.Sets[0].Ece = 3.1;
        Assert.Single(EvaluationReport.Tripwires(ece, selection));
    }

    /// <summary>
    ///     Validates the UPOS to class mapping that the gold views use, for every tag.
    /// </summary>
    [Fact]
    public void DevSets_GoldClassOf_EveryTag_CollapsesToTenClassesAndOther()
    {
        var classes = Enumerable.Range(0, UposExtensions.Count).Select(i => DevSets.GoldClassOf((Upos)i)).ToList();

        Assert.Equal("Noun", DevSets.GoldClassOf(Upos.Propn));
        Assert.Equal("Conjunction", DevSets.GoldClassOf(Upos.Sconj));
        Assert.Equal("Other", DevSets.GoldClassOf(Upos.Punct));
        Assert.Equal(11, classes.Distinct().Count());
        Assert.All(classes, c => Assert.Contains(c, EvalTally.Labels));
    }

    /// <summary>
    ///     Validates the documented sizes that the preflight compares with: they are the sizes of the pinned files, and a fixture
    ///     that has the documented sizes matches every one of them.
    /// </summary>
    [Fact]
    public void HeldOutData_Counts_DocumentedSizes()
    {
        // Arrange: a fixture with exactly the documented number of items of each set (the content is a placeholder)
        TrainingSentence Sentence(string document, int tokens) =>
            new("s", document, Enumerable.Range(0, tokens).Select(i => "w" + i).ToArray(), new sbyte[tokens]);

        GoldTarget Target(int i) => new("g" + i, "g" + (i % 856), new[] { "w" }, 0, "Noun");

        var data = new HeldOutData
        {
            OpenTest = Enumerable.Range(0, 1501).Select(i => Sentence("d" + (i % 517), i == 0 ? 26185 - 1500 : 1)).ToList(),
            Gold = Enumerable.Range(0, 3047).Select(Target).ToList(),
            Probe = Enumerable.Range(0, 560).Select(Target).ToList(),
            Challenge = Enumerable.Range(0, 32).Select(i => Sentence("c", i == 0 ? 272 - 31 : 1)).ToList(),
            Ud = Enumerable.Range(0, 2077).Select(i =>
            {
                var words = i == 0 ? 25094 - 2076 : 1;
                return new UdSentence("u" + i, "u", new string[words], new Upos[words], new Upos[words], new string[words]);
            }).ToList(),
        };

        // Act
        var counts = data.Counts();

        // Assert
        Assert.All(counts, c => Assert.True(c.Matches, $"{c.Name}: {c.Actual} against {c.Expected}"));
        Assert.Equal(25094, counts.Single(c => c.Name == "treebank words").Expected);
    }

    /// <summary>
    ///     Validates that the model is judged on every held-out view of fixtures, the views are consistent, the report is deterministic and the treebank never appears in it.
    /// </summary>
    [Fact]
    public void HeldOutEvaluator_Score_FixtureSets_ViewsAndReports()
    {
        // Arrange: fixtures for every set, with unmistakable treebank words
        var tagger = CreateTagger();
        var corpus = HeldOutSets.ParseCorpus(
            new[]
            {
                "# sent_id = s1", "# source_id = doc1", "The\tDET\tunanimous", "cat\tNOUN\tunanimous", "sat\tVERB\tunanimous", ".\tPUNCT\tunanimous", string.Empty,
                "# sent_id = s2", "# source_id = doc2", "She\tPRON\tunanimous", "reads\tVERB\tunanimous", ".\tPUNCT\tunanimous",
            },
            "fixture");
        var gold = DevSets.ParseGold(
            new[]
            {
                "# columns = id doc_id pos target status agreement sentence",
                "g-1\tdoc-a\tNoun\t1\tagreed\tfull\tThe cat sat .",
                "g-2\tdoc-b\tVerb\t2\tadjudicated\tpos\tThe cat sat .",
                "g-3\tdoc-c\tPreposition\t0\tagreed\tfull\tto go",
            },
            "fixture");
        var probe = HeldOutSets.ParseProbe(
            new[] { "id\tshape\tlemma\tgold\ttarget_index\tsentence", "p-1\tshape-a\tcat\tNoun\t1\tThe {cat} sat .", "p-2\tshape-b\tsit\tVerb\t2\tThe cat {sat} .", "p-3\tshape-a\tcat\tNoun\t1\tA {cat} ran ." },
            "fixture");
        var udSentences = HeldOutSets.ParseUd(
            new[] { "# sent_id = treebank-doc-0001", "# text = x", "My\tDET\tPRON\tpossessive-det", "xylophone\tNOUN\tNOUN\t_", "sat\tVERB\tVERB\t_", string.Empty },
            "fixture");
        var data = new HeldOutData { OpenTest = corpus, Gold = gold, Probe = probe, Challenge = corpus, Ud = udSentences };

        // Act
        var scores = HeldOutEvaluator.Score(tagger, EmbeddedModel.Lexicon, data);

        // Assert: counts of the views
        Assert.Equal(7, scores.OpenTest.Count);
        Assert.Equal(7, scores.Challenge.Count);
        Assert.Equal(2, scores.Gold.Count);
        Assert.Single(scores.GoldFullAgreement);
        Assert.Single(scores.GoldConvention);
        Assert.Equal(3, scores.Probe.Count);
        Assert.Equal(new[] { "shape-a", "shape-b" }, scores.ProbeShapes.Select(p => p.Key).ToArray());
        Assert.Equal(2, scores.ProbeShapes[0].Value.Count);
        Assert.Equal(3, scores.UdMapped.Count);
        Assert.Equal(1, scores.UdMappedWords);
        Assert.Equal("DET", scores.UdMapped[0].Gold);
        Assert.Equal("PRON", scores.UdRaw[0].Gold);
        Assert.Equal(scores.UdMapped[1].Chosen, scores.UdRaw[1].Chosen);
        Assert.All(scores.UdMapped, t => Assert.Equal("ud:treebank-doc", t.DocumentId));
        Assert.All(scores.OpenTest, t => Assert.Equal(UposExtensions.Count, t.Probabilities.Length));
        Assert.All(scores.OpenTest, t => Assert.Equal(t.Confidence, t.Probabilities[(int)ParseTag(t.Chosen)], 12));

        // The judged tokens agree with the development judgment of the same decoder output
        var viaDev = DevEvaluator.EvaluateOpen(tagger, corpus);
        Assert.Equal(viaDev.Select(o => o.Correct).ToArray(), scores.OpenTest.Select(t => t.Outcome.Correct).ToArray());
        Assert.Equal(viaDev.Select(o => o.Harmful).ToArray(), scores.OpenTest.Select(t => t.Outcome.Harmful).ToArray());

        // The report is deterministic, has one summary for each set and contains no treebank word, sentence id or document
        var selection = Selection();
        string Render()
        {
            var results = new ResultsData { ModelImageSha256 = ModelHash, SelectionSha256 = "dd", RuleId = OperatingPointRule.Id, Resamples = 50, Seed = 1, Runtime = "fixture runtime", TokensPerSecond = 12345 };
            results.Inputs.Add(new InputRecord { Name = "fixture", Sha256 = "ee" });
            foreach (var (name, tokens) in EvaluationReport.Views(scores))
            {
                results.Sets.Add(EvaluationReport.Summarize(name, tokens, selection.SelectedPoints, 50, 1));
            }

            return EvaluationReport.RenderResults(results, scores);
        }

        var report = Render();
        Assert.Equal(report, Render());
        Assert.DoesNotContain("xylophone", report, StringComparison.Ordinal);
        Assert.DoesNotContain("treebank-doc", report, StringComparison.Ordinal);
        Assert.DoesNotContain("\n\n\n", report, StringComparison.Ordinal);
        Assert.EndsWith("|\n", report, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", report, StringComparison.Ordinal);
        Assert.Contains("## Operating points on the held-out sets", report, StringComparison.Ordinal);
        Assert.Contains("12,345 tokens per second", report, StringComparison.Ordinal);
        Assert.Contains(HeldOutEvaluator.Names.UdMapped, report, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Validates that the columns of the noun-verb confusion table name the cells they hold: every cell has its own known count.
    /// </summary>
    [Fact]
    public void EvaluationReport_Confusion_KnownCells_LabelsMatchCounts()
    {
        // Arrange: gold noun answered other 1, noun 2, verb 3 times; gold verb answered other 4, noun 5, verb 6 times
        var scores = new HeldOutScores();
        void Add(int gold, int chosen, int count)
        {
            for (var i = 0; i < count; i++)
            {
                var outcome = new Outcome("doc", gold == chosen, 0.9, 0.5, true, true, false);
                scores.OpenTest.Add(new EvalToken("fixture-set", "doc", "s" + i, 0, "form", gold == 1 ? "NOUN" : "VERB", "X", 0.9, new double[UposExtensions.Count], outcome, gold, chosen));
            }
        }

        Add(1, 0, 1);
        Add(1, 1, 2);
        Add(1, 2, 3);
        Add(2, 0, 4);
        Add(2, 1, 5);
        Add(2, 2, 6);

        // Act
        var text = new System.Text.StringBuilder();
        EvaluationReport.Confusion(text, scores);

        // Assert: the header names the cells in the order of the counts
        var lines = text.ToString().Split('\n');
        Assert.Equal("## Noun-verb confusion", lines[0]);
        Assert.Equal("| Set | Noun as other | Noun as noun | Noun as verb | Verb as other | Verb as noun | Verb as verb |", lines[2]);
        Assert.Equal($"| {HeldOutEvaluator.Names.OpenTest} | 1 | 2 | 3 | 4 | 5 | 6 |", lines[4]);
    }

    /// <summary>
    ///     Validates the throughput measurement on a fixture: a positive rate.
    /// </summary>
    [Fact]
    public void HeldOutEvaluator_Throughput_Fixture_IsPositive()
    {
        var rate = HeldOutEvaluator.Throughput(CreateTagger(), new[] { new[] { "The", "cat", "sat", "." }, new[] { "She", "reads", "." } });

        Assert.True(rate > 0);
    }

    /// <summary>
    ///     Validates the per-token dump: one header and one row for each token, 17 probability columns, written to a folder outside the repository.
    /// </summary>
    [Fact]
    public void EvaluationReport_WriteDump_Tokens_HeaderAndRows()
    {
        // Arrange
        var folder = Path.Combine(Path.GetTempPath(), "pos-dump-" + Guid.NewGuid().ToString("N"));
        var tokens = new List<EvalToken> { Token("fixture-set", "doc", "s1", false, 0.9), Token("fixture-set", "doc", "s2", true, 0.6) };
        try
        {
            // Act
            DataGuard.EnsureOutsideRepository(DataFiles.RepositoryRoot, folder);
            EvaluationReport.WriteDump(folder, "fixture-set", tokens);

            // Assert
            var lines = File.ReadAllLines(Path.Combine(folder, "fixture-set.tsv"));
            Assert.Equal(3, lines.Length);
            Assert.Equal(8 + UposExtensions.Count, lines[0].Split('\t').Length);
            Assert.Equal(lines[0].Split('\t').Length, lines[1].Split('\t').Length);
            Assert.StartsWith("fixture-set\tdoc\ts1\t0\tform\t", lines[1]);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }

    /// <summary>
    ///     Validates that the held-out names stay out of every trainer source except the guard, including the new files.
    /// </summary>
    [Fact]
    public void HeldOutEvaluation_Sources_NewFilesDoNotSpellHeldOutNames()
    {
        // Arrange
        var folder = DataFiles.Repository("tools/DemaConsulting.PartOfSpeech.Trainer");
        var names = new[] { "ConfidenceCurve.cs", "ClusterBootstrap.cs", "EvalTally.cs", "EvaluationData.cs", "EvaluationReport.cs", "HeldOutEvaluator.cs", "HeldOutSets.cs", "OperatingPoints.cs", "OperatingPointsFile.cs", "Program.cs" };
        var forbidden = new[] { "test.tsv", "probe.tsv", "ste-challenge", "ud-ewt", "en_ewt", "gold-v2/test", "open/test" };

        // Act / Assert
        foreach (var name in names)
        {
            var file = Directory.GetFiles(folder, name, SearchOption.AllDirectories).Single(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar));
            var text = File.ReadAllText(file);
            foreach (var forbiddenName in forbidden)
            {
                Assert.False(text.Contains(forbiddenName), $"{name} mentions '{forbiddenName}'");
            }
        }
    }

#if NET8_0_OR_GREATER
    /// <summary>
    ///     Validates that the selection artifact round-trips in canonical form, refuses unknown members and loads only when it is valid.
    /// </summary>
    [Fact]
    public void OperatingPointsFile_FormatParseLoad_RoundTripAndValidation()
    {
        // Arrange
        var data = Data();
        var text = OperatingPointsFile.Format(data);
        var folder = Path.Combine(Path.GetTempPath(), "pos-points-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "operating-points.json");
        try
        {
            // Assert: canonical form and round trip
            Assert.EndsWith("}\n", text, StringComparison.Ordinal);
            Assert.DoesNotContain("\r", text, StringComparison.Ordinal);
            Assert.Contains("\"modelImageSha256\"", text, StringComparison.Ordinal);
            Assert.Equal(text, OperatingPointsFile.Format(OperatingPointsFile.Parse(System.Text.Encoding.UTF8.GetBytes(text))));
            Assert.Throws<InvalidDataException>(() => OperatingPointsFile.Parse(System.Text.Encoding.UTF8.GetBytes(text.Replace("\"schemaVersion\"", "\"surprise\": 1,\n  \"schemaVersion\""))));
            Assert.Throws<InvalidDataException>(() => OperatingPointsFile.Parse(System.Text.Encoding.UTF8.GetBytes("not json")));

            // Load: missing file, valid file (the selection hash is the hash of the exact bytes), another model
            Assert.Throws<InvalidDataException>(() => OperatingPointsFile.Load(path, Expectation()));
            File.WriteAllText(path, text);
            var loaded = OperatingPointsFile.Load(path, Expectation());
            Assert.Equal(ModelBuilder.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(text)), loaded.SelectionSha256);
            var other = Expectation();
            other.ModelImageSha256 = new string('d', 64);
            Assert.Throws<InvalidDataException>(() => OperatingPointsFile.Load(path, other));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    /// <summary>
    ///     Validates that the committed selection artifact passes the gate for the frozen embedded model and the committed
    ///     development files. Only the committed selection file and the embedded model are read; no held-out file is opened.
    /// </summary>
    [Fact]
    public void OperatingPointsFile_Load_CommittedSelection_PassesGateForFrozenModel()
    {
        // Arrange
        var root = DataFiles.RepositoryRoot;
        var modelSha = ModelBuilder.Sha256Hex(PerceptronModel.ToUncompressedImage(OperatingPointsFile.ReadEmbeddedWeights()));
        var config = ConfigFile.Load(DataFiles.Repository("tools/DemaConsulting.PartOfSpeech.Trainer/training.json"));
        var path = DataFiles.Repository("docs/evaluation/operating-points.json");

        // Act
        var selection = OperatingPointsFile.Load(path, OperatingPointsFile.Expect(root, modelSha, config.ImageSha256));

        // Assert: valid for the frozen model, and the selection hash is the hash of the committed bytes
        Assert.Equal(ModelBuilder.Sha256Hex(File.ReadAllBytes(path)), selection.SelectionSha256);
        Assert.Equal(modelSha, selection.Data.ModelImageSha256);
        Assert.Equal(OperatingPointRule.Id, selection.Data.RuleId);
    }

    /// <summary>
    ///     Validates that the committed selection is exactly what the registered rule selects from the two development files
    ///     for the frozen embedded model (the development-data derivation of the gate). Only development data is read.
    /// </summary>
    [Fact]
    public void OperatingPointsFile_Load_CommittedSelection_EqualsDevDerivation()
    {
        // Arrange
        var root = DataFiles.RepositoryRoot;
        var weights = OperatingPointsFile.ReadEmbeddedWeights();
        var modelSha = ModelBuilder.Sha256Hex(PerceptronModel.ToUncompressedImage(weights));
        var config = ConfigFile.Load(DataFiles.Repository("tools/DemaConsulting.PartOfSpeech.Trainer/training.json"));
        var path = DataFiles.Repository("docs/evaluation/operating-points.json");
        var expectation = OperatingPointsFile.Expect(root, modelSha, config.ImageSha256, weights);

        // Act / Assert: the derivation is wired in (the development files exist) and the committed artifact passes it
        Assert.NotNull(expectation.DerivePooledCurve);
        var selection = OperatingPointsFile.Load(path, expectation);
        Assert.Equal(2, selection.Data.Points.Count);

        // A tampered threshold of the committed artifact (another grid point) is refused by the same gate
        var tampered = OperatingPointsFile.Parse(File.ReadAllBytes(path));
        var strict = tampered.Points.Single(p => p.Name == "strict");
        strict.Threshold = strict.Threshold > 0.985 ? 0.98 : 0.99;
        Assert.Throws<InvalidDataException>(() => SelectedOperatingPoints.Validate(tampered, "ee", expectation));
    }

    /// <summary>
    ///     Validates that the pinned hash of the mapped treebank table is the hash that the committed results record for it.
    /// </summary>
    [Fact]
    public void DataGuard_UdMappedSha256_EqualsHashRecordedInCommittedResults()
    {
        // Arrange
        var results = OperatingPointsFile.ParseResults(File.ReadAllBytes(DataFiles.Repository("docs/evaluation/test-results.json")));

        // Act
        var recorded = results.Inputs.Single(i => i.Name == "ud-test-mapped").Sha256;

        // Assert
        Assert.Equal(DataGuard.UdMappedSha256, recorded);
    }

    /// <summary>
    ///     Validates that the results file round-trips in canonical form.
    /// </summary>
    [Fact]
    public void OperatingPointsFile_FormatResults_RoundTrip()
    {
        // Arrange
        var results = new ResultsData { ModelImageSha256 = ModelHash, SelectionSha256 = "dd", RuleId = OperatingPointRule.Id, Resamples = 10, Seed = 1, Runtime = "x", TokensPerSecond = 1.5 };
        results.Inputs.Add(new InputRecord { Name = "a", Sha256 = "b" });
        results.Sets.Add(new SetRecord { Name = "s", Tokens = 3, Points = { new PointResult { Name = "reference", Threshold = 0.45 } } });

        // Act
        var text = OperatingPointsFile.Format(results);
        var parsed = OperatingPointsFile.ParseResults(System.Text.Encoding.UTF8.GetBytes(text));

        // Assert
        Assert.Equal(text, OperatingPointsFile.Format(parsed));
        Assert.Equal("s", parsed.Sets[0].Name);
        Assert.Equal(0.45, parsed.Sets[0].Points[0].Threshold);
        Assert.Throws<InvalidDataException>(() => OperatingPointsFile.ParseResults(System.Text.Encoding.UTF8.GetBytes("{\"nope\": 1}")));
    }
#endif

    /// <summary>
    ///     Parses a tag name that the model produced.
    /// </summary>
    private static Upos ParseTag(string name)
    {
        Assert.True(UposExtensions.TryParse(name, out var tag));
        return tag;
    }

    /// <summary>
    ///     Builds a judged token of the fixture set: a verb that is right or wrong at a given confidence.
    /// </summary>
    private static EvalToken Token(string set, string document, string sentence, bool wrong, double confidence)
    {
        var outcome = new Outcome(document, !wrong, confidence, wrong ? 0.1 : confidence, true, true, wrong && confidence >= Metrics.HarmfulConfidence);
        return new EvalToken(set, document, sentence, 0, "form", "VERB", wrong ? "NOUN" : "VERB", confidence, new double[UposExtensions.Count], outcome, 2, wrong ? 1 : 2);
    }
}
