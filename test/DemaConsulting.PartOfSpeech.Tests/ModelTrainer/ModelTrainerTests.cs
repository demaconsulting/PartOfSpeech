// cspell:ignore DCDAFUL
using DemaConsulting.PartOfSpeech.Trainer;

namespace DemaConsulting.PartOfSpeech.Tests.ModelTrainer;

/// <summary>
///     Unit tests of the model trainer: the averaged-perceptron engine, its corpus reader, the quantization, the
///     check logic and the guard that keeps held-out data away. The engine source files of the trainer tool are
///     compiled into this test assembly, so these tests run the real trainer code on every target framework.
/// </summary>
public class ModelTrainerTests
{
    /// <summary>
    ///     The bucket space of the subset runs (the smallest the format allows).
    /// </summary>
    private const int HashBits = 18;

    /// <summary>
    ///     The pinned SHA-256 of the uncompressed image of the small unrestricted run (cross-platform golden).
    /// </summary>
    private const string GoldenUnrestricted = "ef72b1b04b54b3bc50d9ee56a56361d58ebebeccb456cad90af5037eaf335ef3";

    /// <summary>
    ///     The pinned SHA-256 of the uncompressed image of the small fully restricted run (cross-platform golden).
    /// </summary>
    private const string GoldenRestricted = "a7f8f36462151c7052b9d37db5d6c2b0bedeeeaa52861f867562bb6c479a4616";

    /// <summary>
    ///     Creates the configuration of the small runs: three epochs over the sentences of one source at 18 bits.
    /// </summary>
    private static TrainingConfig SmallConfig(RestrictionMode mode = RestrictionMode.None, long seed = 7)
    {
        var config = new TrainingConfig
        {
            Epochs = 3,
            HashBits = HashBits,
            Seed = seed,
            Restriction = mode,
            Temperature = 2.0,
            Sources = { new SourceConfig { Name = "open", Path = "data/open/train.tsv", MaxSentences = 300 } },
        };
        config.Validate();
        return config;
    }

    /// <summary>
    ///     Reads the first 300 sentences of the open training corpus.
    /// </summary>
    private static List<TrainingSentence> SmallCorpus() =>
        CorpusReader.LoadSources(DataFiles.RepositoryRoot, SmallConfig());

    /// <summary>
    ///     Trains the small configuration.
    /// </summary>
    private static AveragedPerceptron TrainSmall(TrainingConfig config) =>
        PerceptronTrainer.Train(config, EmbeddedModel.Lexicon, SmallCorpus());

    /// <summary>
    ///     Trains the small run and returns the uncompressed image.
    /// </summary>
    private static byte[] TrainImage(TrainingConfig config) => ModelBuilder.Build(TrainSmall(config), config, false);

    /// <summary>
    ///     Parses a corpus fixture given as lines.
    /// </summary>
    private static List<TrainingSentence> Parse(SourceConfig source, params string[] lines) =>
        CorpusReader.Parse(lines, source, "fixture");

    /// <summary>
    ///     Validates that two runs with the same corpus, configuration and seed give a byte-identical image.
    /// </summary>
    [Fact]
    public void ModelTrainer_Train_SameInputs_ByteIdenticalImage()
    {
        // Arrange: the small configuration
        var config = SmallConfig();

        // Act: train twice
        var first = TrainImage(config);
        var second = TrainImage(config);

        // Assert: identical bytes
        Assert.Equal(first, second);
    }

    /// <summary>
    ///     Validates that another seed gives another image.
    /// </summary>
    [Fact]
    public void ModelTrainer_Train_DifferentSeed_DifferentImage()
    {
        // Arrange / Act: train with two seeds
        var first = TrainImage(SmallConfig(seed: 7));
        var second = TrainImage(SmallConfig(seed: 8));

        // Assert: the shuffle order changes the averaged weights
        Assert.NotEqual(first, second);
    }

    /// <summary>
    ///     Validates the pinned SHA-256 of the small unrestricted run: the same bytes on every operating system and framework.
    /// </summary>
    [Fact]
    public void ModelTrainer_Train_SmallUnrestrictedRun_MatchesGoldenHash()
    {
        // Arrange / Act
        var image = TrainImage(SmallConfig(RestrictionMode.None));

        // Assert: the integer pipeline and the own random generator make the hash platform independent
        var hash = ModelBuilder.Sha256Hex(image);
        Assert.True(hash == GoldenUnrestricted, "actual hash: " + hash);
    }

    /// <summary>
    ///     Validates the pinned SHA-256 of the small fully restricted run, which also exercises the lexicon and the shape rules.
    /// </summary>
    [Fact]
    public void ModelTrainer_Train_SmallRestrictedRun_MatchesGoldenHash()
    {
        // Arrange / Act
        var image = TrainImage(SmallConfig(RestrictionMode.Full));

        // Assert
        var hash = ModelBuilder.Sha256Hex(image);
        Assert.True(hash == GoldenRestricted, "actual hash: " + hash);
    }

    /// <summary>
    ///     Validates the known answers of the SplitMix64 generator for seed 0 (the reference sequence).
    /// </summary>
    [Fact]
    public void DeterministicRandom_NextUInt64_SeedZero_ReferenceSequence()
    {
        // Arrange
        var random = new DeterministicRandom(0);

        // Act / Assert: the first three outputs of SplitMix64 with state 0
        Assert.Equal(0xE220A8397B1DCDAFUL, random.NextUInt64());
        Assert.Equal(0x6E789E6AA1B965F4UL, random.NextUInt64());
        Assert.Equal(0x06C45D188009454FUL, random.NextUInt64());
    }

    /// <summary>
    ///     Validates that the shuffle is a permutation, is reproducible and depends on the seed.
    /// </summary>
    [Fact]
    public void DeterministicRandom_Shuffle_Permutation_ReproducibleAndSeedDependent()
    {
        // Arrange
        int[] Shuffled(ulong seed)
        {
            var values = Enumerable.Range(0, 50).ToArray();
            new DeterministicRandom(seed).Shuffle(values);
            return values;
        }

        // Act
        var a = Shuffled(1);
        var b = Shuffled(1);
        var c = Shuffled(2);

        // Assert
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.Equal(Enumerable.Range(0, 50), a.OrderBy(v => v));
    }

    /// <summary>
    ///     Validates that a non-positive bound is rejected.
    /// </summary>
    [Fact]
    public void DeterministicRandom_NextInt_NonPositiveBound_Throws()
    {
        // Arrange / Act / Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new DeterministicRandom(1).NextInt(0));
    }

    /// <summary>
    ///     Validates that the trainer's predictor gives the same tags as the runtime decoder when the candidates are restricted by the embedded lexicon.
    /// </summary>
    [Fact]
    public void ModelTrainer_Predict_RestrictedWithLexicon_MatchesTagDecoder()
    {
        // Arrange: train the small restricted run and load the same weights into the runtime decoder
        var config = SmallConfig(RestrictionMode.Full);
        var corpus = SmallCorpus();
        var rows = ModelBuilder.Rows(PerceptronTrainer.Train(config, EmbeddedModel.Lexicon, corpus), config);
        var predictor = new QuantizedPredictor(HashBits, rows);
        var decoder = new TagDecoder(PerceptronModel.Load(ModelBuilder.Encode(rows, config, false), FeatureExtractor.FeatureSetVersion), EmbeddedModel.Lexicon);

        // Act / Assert: every token of every sentence gets the same tag
        var compared = 0;
        foreach (var sentence in corpus.Distinct())
        {
            var expected = predictor.Predict(sentence.Tokens, EmbeddedModel.Lexicon);
            var actual = decoder.Decode(Tokenizer.CreateSentence(sentence.Tokens).TokenArray, null, 0);
            Assert.Equal(expected, actual.Select(t => (int)t.Tag));
            compared += expected.Length;
        }

        Assert.True(compared > 2000);
    }

    /// <summary>
    ///     Validates the same parity without a lexicon (all tags allowed): the decoder with a lexicon that knows none of the tokens must agree.
    /// </summary>
    [Fact]
    public void ModelTrainer_Predict_Unrestricted_MatchesTagDecoderWithUnknownWords()
    {
        // Arrange: weights of the unrestricted run and a lexicon that contains none of the corpus words
        var config = SmallConfig(RestrictionMode.None);
        var corpus = SmallCorpus();
        var rows = ModelBuilder.Rows(PerceptronTrainer.Train(config, EmbeddedModel.Lexicon, corpus), config);
        var predictor = new QuantizedPredictor(HashBits, rows);
        var model = PerceptronModel.Load(ModelBuilder.Encode(rows, config, false), FeatureExtractor.FeatureSetVersion);
        var decoder = new TagDecoder(model, DemaConsulting.PartOfSpeech.Lexicon.Load(TinyModels.CreateLexicon("xylophone NOUN")));

        // Act / Assert: tags agree and the integer scores equal the sums that the runtime model computes
        foreach (var sentence in corpus.Distinct().Take(100))
        {
            var scores = new List<int[]>();
            var expected = predictor.Predict(sentence.Tokens, null, scores);
            var actual = decoder.Decode(Tokenizer.CreateSentence(sentence.Tokens).TokenArray, null, 0);
            Assert.Equal(expected, actual.Select(t => (int)t.Tag));
            AssertScoresMatchModel(model, sentence.Tokens, expected, scores);
        }
    }

    /// <summary>
    ///     Asserts that the integer scores of a predictor equal the sums that <see cref="PerceptronModel.AddScores"/> gives for the same history.
    /// </summary>
    private static void AssertScoresMatchModel(PerceptronModel model, string[] tokens, int[] tags, List<int[]> scores)
    {
        var workspace = new DecoderWorkspace();
        FeatureExtractor.Prepare(workspace, tokens, HashBits);
        var buckets = new int[FeatureExtractor.MaxFeatures];
        var previous1 = FeatureExtractor.StartTag1;
        var previous2 = FeatureExtractor.StartTag2;
        for (var i = 0; i < tokens.Length; i++)
        {
            var count = FeatureExtractor.Extract(workspace, i, previous1, previous2, HashBits, buckets);
            var sums = new int[UposExtensions.Count];
            for (var k = 0; k < count; k++)
            {
                model.AddScores(buckets[k], sums);
            }

            Assert.Equal(sums, scores[i]);
            previous2 = previous1;
            previous1 = tags[i];
        }
    }

    /// <summary>
    ///     Validates the exact quantization against a decimal oracle over a pseudo-random update sequence.
    /// </summary>
    [Fact]
    public void AveragedPerceptron_Quantize_RandomUpdates_MatchesDecimalOracle()
    {
        // Arrange: apply 3000 updates to a few buckets and record them for the oracle
        var perceptron = new AveragedPerceptron(HashBits);
        var weights = new Dictionary<(int, int), decimal>();
        var totals = new Dictionary<(int, int), decimal>();
        var random = new DeterministicRandom(99);
        decimal Get(Dictionary<(int, int), decimal> table, (int, int) key) => table.TryGetValue(key, out var value) ? value : 0m;
        for (var n = 0; n < 3000; n++)
        {
            perceptron.BeginStep();
            if (random.NextInt(3) == 0)
            {
                continue;
            }

            var bucket = random.NextInt(40);
            var gold = random.NextInt(17);
            var guess = (gold + 1 + random.NextInt(16)) % 17;
            perceptron.Update(new[] { bucket }, 1, gold, guess);
            weights[(bucket, gold)] = Get(weights, (bucket, gold)) + 1;
            weights[(bucket, guess)] = Get(weights, (bucket, guess)) - 1;
            totals[(bucket, gold)] = Get(totals, (bucket, gold)) + perceptron.Steps;
            totals[(bucket, guess)] = Get(totals, (bucket, guess)) - perceptron.Steps;
        }

        // Act: quantize with scale 16
        var rows = perceptron.Quantize(16, 0).ToDictionary(r => r.Key, r => r.Value);

        // Assert: every weight is round-half-away-from-zero of 16 * (W - U / T), zero weights are absent
        foreach (var key in weights.Keys)
        {
            var expected = (int)Math.Round(16m * (weights[key] - (totals[key] / perceptron.Steps)), MidpointRounding.AwayFromZero);
            var actual = rows.TryGetValue(key.Item1, out var row) ? row[key.Item2] : 0;
            Assert.Equal(expected, actual);
        }
    }

    /// <summary>
    ///     Validates the rounding at an exact tie, the sign symmetry and the clamp of the quantization.
    /// </summary>
    [Fact]
    public void AveragedPerceptron_Quantize_TiesAndClamp_RoundAwayFromZeroAndClamp()
    {
        // Arrange / Act / Assert: -0.5 and +0.5 round away from zero
        Assert.Equal(-1, AveragedPerceptron.Quantize(0, 1, 2, 1));
        Assert.Equal(1, AveragedPerceptron.Quantize(0, -1, 2, 1));

        // 1.4995 stays 1, an exact 1.5 becomes 2
        Assert.Equal(1, AveragedPerceptron.Quantize(0, -2999, 2000, 1));
        Assert.Equal(2, AveragedPerceptron.Quantize(0, -3, 2, 1));

        // Large weights clamp to 32767 (never -32768)
        Assert.Equal(32767, AveragedPerceptron.Quantize(5000, 0, 1, 16));
        Assert.Equal(-32767, AveragedPerceptron.Quantize(-5000, 0, 1, 16));
    }

    /// <summary>
    ///     Validates that a quantized model loads with exactly the weights that were quantized, none of them zero or -32768.
    /// </summary>
    [Fact]
    public void ModelTrainer_Build_SmallRun_LoadsWithTheQuantizedWeights()
    {
        // Arrange: train and quantize
        var config = SmallConfig();
        var rows = ModelBuilder.Rows(TrainSmall(config), config);

        // Act: encode and load
        var model = PerceptronModel.Load(ModelBuilder.Encode(rows, config, true), FeatureExtractor.FeatureSetVersion);

        // Assert: counts, header values and every row agree
        Assert.Equal(rows.Count, model.RowCount);
        Assert.Equal(rows.Sum(r => r.Value.Count(w => w != 0)), model.WeightCount);
        Assert.Equal(HashBits, model.HashBits);
        Assert.Equal(2f, model.Temperature);
        Assert.Equal(1f / 16f, model.Scale);
        Assert.All(rows, r => Assert.DoesNotContain(r.Value, w => w == short.MinValue));
        foreach (var row in rows.Take(500))
        {
            var scores = new int[UposExtensions.Count];
            model.AddScores(row.Key, scores);
            Assert.Equal(row.Value.Select(w => (int)w), scores);
        }
    }

    /// <summary>
    ///     Validates that pruning drops the small weights and keeps the large ones.
    /// </summary>
    [Fact]
    public void AveragedPerceptron_Quantize_PruneThreshold_DropsSmallWeights()
    {
        // Arrange: train the small run
        var perceptron = TrainSmall(SmallConfig());

        // Act
        var all = perceptron.Quantize(16, 0);
        var pruned = perceptron.Quantize(16, 3);

        // Assert: fewer weights, none of magnitude 3 or below
        Assert.True(pruned.Sum(r => r.Value.Count(w => w != 0)) < all.Sum(r => r.Value.Count(w => w != 0)));
        Assert.All(pruned, r => Assert.All(r.Value, w => Assert.True(w == 0 || Math.Abs(w) > 3)));
    }

    /// <summary>
    ///     Validates that masked and disputed tokens give no update but still count as steps and enter the history.
    /// </summary>
    [Fact]
    public void ModelTrainer_Train_MaskedToken_NoUpdateButStepAndHistory()
    {
        // Arrange: a sentence whose first token is masked and whose second token is a noun
        var sentences = Parse(
            new SourceConfig { Name = "t" },
            "# sent_id = s1",
            "# source_id = d1",
            "x\t_\tmasked",
            "y\tNOUN\tunanimous");
        var config = SmallConfig();
        config.Epochs = 1;

        // Act
        var perceptron = PerceptronTrainer.Train(config, EmbeddedModel.Lexicon, sentences);

        // Assert: two steps, one update; the history of the second token is the guess for the masked one (ADJ, the lowest tag)
        Assert.Equal(2, perceptron.Steps);
        Assert.Equal(1, perceptron.Mistakes);
        Assert.Equal(1, WeightOf(perceptron, FeatureKind.PrevTag1, "ADJ", Upos.Noun));
        Assert.Equal(0, WeightOf(perceptron, FeatureKind.PrevTag1, "-S-", Upos.Noun));
    }

    /// <summary>
    ///     Reads the current weight of a tag in the bucket of a feature.
    /// </summary>
    private static int WeightOf(AveragedPerceptron perceptron, FeatureKind kind, string value, Upos tag)
    {
        var scores = new int[UposExtensions.Count];
        perceptron.AddScores(FeatureExtractor.ReferenceBucket(kind, value, null, perceptron.HashBits), scores);
        return scores[(int)tag];
    }

    /// <summary>
    ///     Validates the masking rules of the corpus reader: placeholders, masked status, disputed tokens and a silver probability limit.
    /// </summary>
    [Fact]
    public void CorpusReader_Parse_MaskingRules_MarkTokensWithoutSignal()
    {
        // Arrange: one sentence with every kind of token, including the token "#" (a row, not a comment)
        var lines = new[]
        {
            "# corpus = fixture",
            string.Empty,
            "# sent_id = s1",
            "# source_id = d1",
            "a\tDET\tunanimous\tD:99",
            "b\tNOUN\tdisputed\tN:99",
            "c\tVERB\tsilver\tV:60",
            "d\t_\tmasked\t?",
            "#\tSYM\tunchecked\t-",
            "e\tADJ\tsilver\tJ:90@s",
        };

        // Act: default masking, then with a silver limit of 80 and disputed tokens kept
        var plain = Parse(new SourceConfig { Name = "t" }, lines);
        var strict = Parse(new SourceConfig { Name = "t", MaskDisputed = false, MinSilverProbability = 80 }, lines);

        // Assert
        Assert.Single(plain);
        Assert.Equal(new[] { "a", "b", "c", "d", "#", "e" }, plain[0].Tokens);
        Assert.Equal(new sbyte[] { (sbyte)Upos.Det, -1, (sbyte)Upos.Verb, -1, (sbyte)Upos.Sym, (sbyte)Upos.Adj }, plain[0].Gold);
        Assert.Equal(new sbyte[] { (sbyte)Upos.Det, (sbyte)Upos.Noun, -1, -1, (sbyte)Upos.Sym, (sbyte)Upos.Adj }, strict[0].Gold);
        Assert.Equal("d1", plain[0].SourceId);
    }

    /// <summary>
    ///     Validates the sentence limit and the deterministic subsampling of the corpus reader.
    /// </summary>
    [Fact]
    public void CorpusReader_Parse_LimitAndSample_AreDeterministic()
    {
        // Arrange: twenty one-token sentences
        var lines = new List<string>();
        for (var i = 0; i < 20; i++)
        {
            lines.AddRange(new[] { "# sent_id = s" + i, "# source_id = d", "w\tNOUN\tunanimous", string.Empty });
        }

        // Act
        var limited = CorpusReader.Parse(lines, new SourceConfig { Name = "t", MaxSentences = 5 }, "fixture");
        var sampledA = CorpusReader.Parse(lines, new SourceConfig { Name = "t", SamplePermille = 500 }, "fixture");
        var sampledB = CorpusReader.Parse(lines, new SourceConfig { Name = "t", SamplePermille = 500 }, "fixture");

        // Assert
        Assert.Equal(5, limited.Count);
        Assert.Equal(sampledA.Select(s => s.SentenceId), sampledB.Select(s => s.SentenceId));
        Assert.InRange(sampledA.Count, 1, 19);
    }

    /// <summary>
    ///     Validates that an unknown tag is reported with its line number.
    /// </summary>
    [Fact]
    public void CorpusReader_Parse_UnknownTag_ThrowsWithLine()
    {
        // Arrange / Act
        var ex = Assert.Throws<InvalidDataException>(() => Parse(new SourceConfig { Name = "t" }, "# sent_id = s1", "a\tFOO\tunanimous"));

        // Assert
        Assert.Contains("line 2", ex.Message);
    }

    /// <summary>
    ///     Validates that the source weight repeats the sentences of the source within an epoch.
    /// </summary>
    [Fact]
    public void CorpusReader_LoadSources_Weight_RepeatsSentences()
    {
        // Arrange: one source with weight 2 and one with weight 0
        var config = SmallConfig();
        config.Sources[0].MaxSentences = 10;
        config.Sources[0].Weight = 2;
        config.Sources.Add(new SourceConfig { Name = "masc", Path = "data/masc/masc.tsv", Weight = 0 });

        // Act
        var sentences = CorpusReader.LoadSources(DataFiles.RepositoryRoot, config);

        // Assert: ten sentences twice, the same objects, and the weight-0 source was not read
        Assert.Equal(20, sentences.Count);
        Assert.Same(sentences[0], sentences[10]);
    }

    /// <summary>
    ///     Validates that the minimum count removes rare features from the training and so shrinks the model.
    /// </summary>
    [Fact]
    public void ModelTrainer_Train_MinCount_ShrinksTheModel()
    {
        // Arrange
        var all = SmallConfig();
        var cut = SmallConfig();
        cut.MinCount = 3;

        // Act
        var rowsAll = ModelBuilder.Rows(TrainSmall(all), all);
        var rowsCut = ModelBuilder.Rows(TrainSmall(cut), cut);

        // Assert
        Assert.True(rowsCut.Count < rowsAll.Count);
    }

    /// <summary>
    ///     Validates that the history mode and the restriction mode change the trained weights (the modes are not no-ops).
    /// </summary>
    [Fact]
    public void ModelTrainer_Train_RestrictionAndHistoryModes_ChangeTheImage()
    {
        // Arrange
        var baseline = TrainImage(SmallConfig(RestrictionMode.None));
        var history = TrainImage(SmallConfig(RestrictionMode.History));
        var full = TrainImage(SmallConfig(RestrictionMode.Full));
        var goldConfig = SmallConfig();
        goldConfig.History = HistoryMode.Gold;
        var gold = TrainImage(goldConfig);

        // Act / Assert: each differs from the baseline
        Assert.NotEqual(baseline, history);
        Assert.NotEqual(baseline, full);
        Assert.NotEqual(baseline, gold);
    }

    /// <summary>
    ///     Validates that the check reports a current, a missing, a stale and an invalid committed file.
    /// </summary>
    [Fact]
    public void ModelBuilder_Check_CommittedFile_DetectsStaleMissingAndInvalid()
    {
        // Arrange: the fresh image, its compressed committed form and perturbed variants
        var config = SmallConfig();
        var perceptron = TrainSmall(config);
        var image = ModelBuilder.Build(perceptron, config, false);
        var committed = ModelBuilder.Build(perceptron, config, true);
        var otherTemperature = ModelBuilder.WithTemperature(committed, 3f);
        var otherSeed = TrainImage(SmallConfig(seed: 11));

        // Act / Assert: the compressed committed file is current, everything else is not
        Assert.Equal(image, PerceptronModel.ToUncompressedImage(committed));
        Assert.Equal(CheckStatus.Current, ModelBuilder.Check(image, committed));
        Assert.Equal(CheckStatus.Current, ModelBuilder.Check(image, image));
        Assert.Equal(CheckStatus.Stale, ModelBuilder.Check(image, otherSeed));
        Assert.Equal(CheckStatus.Stale, ModelBuilder.Check(image, otherTemperature));
        Assert.Equal(CheckStatus.Missing, ModelBuilder.Check(image, null));
        Assert.Equal(CheckStatus.Invalid, ModelBuilder.Check(image, new byte[] { 1, 2, 3 }));
    }

    /// <summary>
    ///     Validates that the temperature of a weights file can be replaced without touching the weights.
    /// </summary>
    [Fact]
    public void ModelBuilder_WithTemperature_ReplacesOnlyTheTemperature()
    {
        // Arrange
        var config = SmallConfig();
        var file = ModelBuilder.Build(TrainSmall(config), config, true);

        // Act
        var patched = ModelBuilder.WithTemperature(file, 5.5f);

        // Assert: loads, new temperature, same weights
        var before = PerceptronModel.Load(file, FeatureExtractor.FeatureSetVersion);
        var after = PerceptronModel.Load(patched, FeatureExtractor.FeatureSetVersion);
        Assert.Equal(5.5f, after.Temperature);
        Assert.Equal(before.WeightCount, after.WeightCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => ModelBuilder.WithTemperature(file, 0f));
    }

    /// <summary>
    ///     Validates that evaluation inputs are limited to the two development files.
    /// </summary>
    [Theory]
    [InlineData("data/open/test.tsv")]
    [InlineData("data/eval/gold-v2/test.tsv")]
    [InlineData("data/eval/shapes/probe.tsv")]
    [InlineData("data/eval/ste-challenge/test.tsv")]
    [InlineData("data/open/train.tsv")]
    [InlineData("data/open/../open/test.tsv")]
    public void DataGuard_EnsureDevelopmentFile_HeldOutOrOtherFile_Refused(string relativePath)
    {
        // Arrange / Act / Assert
        var path = Path.Combine(DataFiles.RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.Throws<InvalidOperationException>(() => DataGuard.EnsureDevelopmentFile(DataFiles.RepositoryRoot, path));
    }

    /// <summary>
    ///     Validates that the two development files are admitted, also through a dotted detour.
    /// </summary>
    [Theory]
    [InlineData("data/open/dev.tsv")]
    [InlineData("data/eval/gold-v2/dev.tsv")]
    [InlineData("data/open/../open/dev.tsv")]
    public void DataGuard_EnsureDevelopmentFile_DevelopmentFile_Admitted(string relativePath)
    {
        // Arrange / Act
        var path = Path.Combine(DataFiles.RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var resolved = DataGuard.EnsureDevelopmentFile(DataFiles.RepositoryRoot, path);

        // Assert
        Assert.True(File.Exists(resolved));
    }

    /// <summary>
    ///     Validates that training sources may not be held-out sets or development files, whichever way the path is spelled.
    /// </summary>
    [Theory]
    [InlineData("data/open/test.tsv")]
    [InlineData("data/open/dev.tsv")]
    [InlineData("data\\open\\TEST.tsv")]
    [InlineData("data/eval/gold-v2/dev.tsv")]
    [InlineData("data/eval/shapes/probe.tsv")]
    [InlineData("data/eval/ste-challenge/anything.tsv")]
    [InlineData("cache/ud-ewt/en_ewt-ud-train.conllu")]
    public void DataGuard_EnsureTrainable_HeldOutOrDevelopmentFile_Refused(string path)
    {
        // Arrange / Act / Assert
        Assert.Throws<InvalidOperationException>(() => DataGuard.EnsureTrainable(path));
        Assert.Throws<InvalidOperationException>(() => CorpusReader.ReadFile(path, new SourceConfig { Name = "t" }));
    }

    /// <summary>
    ///     Validates that the three training corpora are admitted as training sources.
    /// </summary>
    [Theory]
    [InlineData("data/open/train.tsv")]
    [InlineData("data/masc/masc.tsv")]
    [InlineData("data/silver/silver.tsv")]
    public void DataGuard_EnsureTrainable_TrainingCorpus_Admitted(string path)
    {
        // Arrange / Act
        var exception = Record.Exception(() => DataGuard.EnsureTrainable(path));

        // Assert: no exception
        Assert.Null(exception);
    }

    /// <summary>
    ///     Validates that a file that does not announce a development split is refused even at a development path.
    /// </summary>
    [Fact]
    public void DataGuard_EnsureDevelopmentHeader_NoDevSplitHeader_Throws()
    {
        // Arrange / Act / Assert
        Assert.Throws<InvalidDataException>(() => DataGuard.EnsureDevelopmentHeader(new[] { "# corpus = x, test split", "# columns = a" }, "fixture"));
        DataGuard.EnsureDevelopmentHeader(new[] { "# corpus = x, dev split (y)", "# columns = a" }, "fixture");
    }

    /// <summary>
    ///     Validates that no trainer source other than the guard names a held-out data set, so there is no code path that could read one.
    /// </summary>
    [Fact]
    public void ModelTrainer_Sources_NoHeldOutPathLiterals()
    {
        // Arrange: every C# source of the trainer except the guard itself
        var folder = DataFiles.Repository("tools/DemaConsulting.PartOfSpeech.Trainer");
        var separator = Path.DirectorySeparatorChar;
        var files = Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(separator + "obj" + separator) && !f.Contains(separator + "bin" + separator))
            .Where(f => Path.GetFileName(f) != "DevSets.cs")
            .ToList();
        var forbidden = new[] { "test.tsv", "probe.tsv", "ste-challenge", "ud-ewt", "en_ewt", "gold-v2/test", "open/test" };

        // Act / Assert
        Assert.True(files.Count >= 10);
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var name in forbidden)
            {
                Assert.False(text.Contains(name), $"{Path.GetFileName(file)} mentions '{name}'");
            }
        }
    }

    /// <summary>
    ///     Validates the gold v2 parser: the sentence is split into tokens, the target index is read and the class table maps UPOS tags.
    /// </summary>
    [Fact]
    public void DevSets_ParseGoldDev_Fixture_ReadsTargetsAndMapsClasses()
    {
        // Arrange
        var lines = new[]
        {
            "# corpus = fixture, dev split",
            "# columns = id doc_id source terms_url sent_id target form strata pos lemma features tense sense status agreement sentence",
            "d-1\tdoc-a\ts\tu\tx\t2\tdog\tC\tNoun\tdog\t-\t-\t-\tagreed\tfull\tThe big dog barks",
            "d-2\tdoc-b\ts\tu\ty\t1\tnot\tC\tAdverb\tnot\t-\t-\t-\tagreed\tfull\tI not go",
        };

        // Act
        var targets = DevSets.ParseGoldDev(lines, "fixture");

        // Assert
        Assert.Equal(2, targets.Count);
        Assert.Equal("dog", targets[0].Form);
        Assert.False(targets[0].IsConventionWord);
        Assert.True(targets[1].IsConventionWord);
        Assert.Equal("Noun", DevSets.GoldClassOf(Upos.Propn));
        Assert.Equal("Conjunction", DevSets.GoldClassOf(Upos.Sconj));
        Assert.Equal("Other", DevSets.GoldClassOf(Upos.Part));
    }

    /// <summary>
    ///     Validates the metrics: accuracy, harmful errors per 1,000 noun-verb decisions and the log-likelihood.
    /// </summary>
    [Fact]
    public void Metrics_Compute_KnownOutcomes_ExpectedNumbers()
    {
        // Arrange: four tokens; one harmful error, one low-confidence error, two correct
        var outcomes = new List<Outcome>
        {
            new("d1", true, 0.9, 0.9, true, true, false),
            new("d1", false, 0.8, 0.2, true, true, true),
            new("d2", false, 0.4, 0.5, false, true, false),
            new("d2", true, 1.0, 1.0, false, false, false),
        };

        // Act
        var metrics = Metrics.Compute(outcomes);

        // Assert
        Assert.Equal(50.0, metrics.Accuracy);
        Assert.Equal(2, metrics.AmbiguousTokens);
        Assert.Equal(50.0, metrics.AmbiguousAccuracy);
        Assert.Equal(3, metrics.NounVerbDecisions);
        Assert.Equal(1, metrics.HarmfulErrors);
        Assert.Equal(1000.0 / 3, metrics.HarmfulPer1000, 6);
        Assert.Equal(-(Math.Log(0.9) + Math.Log(0.2) + Math.Log(0.5) + Math.Log(1.0)) / 4, metrics.Nll, 9);
        Assert.InRange(metrics.Ece, 0, 100);
    }

    /// <summary>
    ///     Validates the judgment of a token on a real tagger: a confident noun-verb error is harmful, a correct token is not.
    /// </summary>
    [Fact]
    public void Metrics_Judge_TinyTagger_CorrectAndNounVerbError()
    {
        // Arrange: weights that make "dog" a verb and "cat" a noun, over a lexicon where both words may be either
        var weights = new TinyModels.WeightsBuilder()
            .Word("dog", Upos.Verb, 40)
            .Word("cat", Upos.Noun, 40)
            .Build(temperature: 1f, scale: 0.1f);
        var lexicon = TinyModels.CreateLexicon("cat NOUN VERB", "dog NOUN VERB");
        var tagger = TinyModels.CreateTagger(weights, lexicon, new TaggerOptions { MaxAlternatives = 16 });

        // Act
        var tagged = tagger.Tag(new[] { "dog", "cat" });
        var dog = Metrics.Judge("d", "NOUN", tagged.Tokens[0], t => t.ToTagString(), "NOUN", "VERB", true);
        var cat = Metrics.Judge("d", "NOUN", tagged.Tokens[1], t => t.ToTagString(), "NOUN", "VERB", true);

        // Assert: the dog is a confident noun-verb error, the cat is correct
        Assert.False(dog.Correct);
        Assert.True(dog.NounVerb);
        Assert.True(dog.Harmful);
        Assert.True(dog.GoldProbability > 0 && dog.GoldProbability < 0.5);
        Assert.True(cat.Correct);
        Assert.False(cat.Harmful);
    }

    /// <summary>
    ///     Validates that the bootstrap of identical outcome lists has a zero difference and a tied interval.
    /// </summary>
    [Fact]
    public void Bootstrap_Compare_IdenticalLists_ZeroDifferenceTied()
    {
        // Arrange: outcomes over five documents
        var list = Enumerable.Range(0, 100)
            .Select(i => new Outcome("d" + (i % 5), i % 4 != 0, 0.9, 0.9, true, true, i % 10 == 0))
            .ToList();

        // Act
        Bootstrap.Compare(list, list, 200, 1, out var accuracy, out var harmful);

        // Assert
        Assert.Equal(0, accuracy.Difference);
        Assert.True(accuracy.ContainsZero);
        Assert.Equal(0, harmful.Difference);
        Assert.Throws<ArgumentException>(() => Bootstrap.Compare(list, list.Take(10).ToList(), 10, 1, out _, out _));
    }

    /// <summary>
    ///     Validates that a clearly better list has a positive accuracy difference whose interval excludes zero.
    /// </summary>
    [Fact]
    public void Bootstrap_Compare_BetterList_PositiveDifferenceExcludesZero()
    {
        // Arrange: the first list is wrong on about 8 percent of 300 tokens, the second on a third; errors are spread over the ten documents
        var better = Enumerable.Range(0, 300).Select(i => new Outcome("d" + (i % 10), i % 13 != 0, 0.9, 0.9, true, false, false)).ToList();
        var worse = Enumerable.Range(0, 300).Select(i => new Outcome("d" + (i % 10), i % 3 != 0, 0.9, 0.9, true, false, false)).ToList();

        // Act
        Bootstrap.Compare(better, worse, 500, 3, out var accuracy, out _);

        // Assert
        Assert.True(accuracy.Difference > 15);
        Assert.False(accuracy.ContainsZero);
    }

    /// <summary>
    ///     Validates that a tiny trained model can be evaluated through the real decoder on a parsed development fixture.
    /// </summary>
    [Fact]
    public void DevEvaluator_EvaluateOpen_FixtureSentences_OneOutcomePerToken()
    {
        // Arrange: a trained small model, loaded as a tagger, and a two-sentence development fixture
        var config = SmallConfig();
        var tagger = DevEvaluator.CreateTagger(ModelBuilder.Build(TrainSmall(config), config, false));
        var dev = DevSets.ParseOpenDev(
            new[]
            {
                "# sent_id = s1", "# source_id = doc1", "The\tDET\tunanimous", "dog\tNOUN\tunanimous", "barks\tVERB\tdisputed", string.Empty,
                "# sent_id = s2", "# source_id = doc2", "Run\tVERB\tunchecked", ".\tPUNCT\tunanimous",
            },
            "fixture");

        // Act
        var outcomes = DevEvaluator.EvaluateOpen(tagger, dev);

        // Assert: disputed tokens are judged too
        Assert.Equal(5, outcomes.Count);
        Assert.Equal(2, outcomes.Select(o => o.DocumentId).Distinct().Count());
    }

    /// <summary>
    ///     Validates that a run-log row has one cell per column of the header.
    /// </summary>
    [Fact]
    public void RunLog_Row_Summary_HasOneCellPerHeaderColumn()
    {
        // Arrange
        var run = new RunSummary { Label = "x", Settings = "s", Rows = 10, Weights = 20, RawBytes = 2048, TrainSeconds = 1.5 };
        run.Result.Open.Add(new Outcome("d", true, 0.9, 0.9, true, true, false));
        var columns = RunLog.Header.Split('\n')[0].Split('|').Length;

        // Act
        var row = RunLog.Row(run);

        // Assert
        Assert.Equal(columns, row.Split('|').Length);
        Assert.StartsWith("| x | s |", row);
    }

    /// <summary>
    ///     Validates that a glossary hint is the gold tag when no hint is wrong, that only tokens with a gold tag and a lexicon ambiguity get one, and that the draw is reproducible.
    /// </summary>
    [Fact]
    public void GlossaryTraining_Draw_NoWrongHints_HintsAreGoldAndReproducible()
    {
        // Arrange: every eligible token gets a hint, none is wrong
        var lexicon = EmbeddedModel.Lexicon;
        var training = new GlossaryTraining(lexicon, 100, 0, 5);
        var corpus = SmallCorpus().Distinct().Take(60).ToList();

        // Act
        var hintCount = 0;
        foreach (var sentence in corpus)
        {
            var hints = training.Draw(sentence, new DeterministicRandom(11));
            var again = training.Draw(sentence, new DeterministicRandom(11));

            // Assert: reproducible, gold hints only, ambiguous tokens only
            Assert.Equal(hints, again);
            for (var i = 0; i < hints.Length; i++)
            {
                if (hints[i] < 0)
                {
                    continue;
                }

                hintCount++;
                Assert.Equal((int)sentence.Gold[i], hints[i]);
                Assert.True(DemaConsulting.PartOfSpeech.Lexicon.AllowedTags(sentence.Tokens[i].ToLowerInvariant(), lexicon).Count >= 2);
            }
        }

        Assert.True(hintCount > 100);
    }

    /// <summary>
    ///     Validates that with every hint wrong the hint is another tag that the lexicon allows for the token.
    /// </summary>
    [Fact]
    public void GlossaryTraining_Draw_AllHintsWrong_HintIsAnotherAllowedTag()
    {
        // Arrange
        var lexicon = EmbeddedModel.Lexicon;
        var training = new GlossaryTraining(lexicon, 100, 100, 5);

        // Act / Assert
        var wrong = 0;
        foreach (var sentence in SmallCorpus().Distinct().Take(60))
        {
            var hints = training.Draw(sentence, new DeterministicRandom(3));
            for (var i = 0; i < hints.Length; i++)
            {
                if (hints[i] >= 0)
                {
                    wrong++;
                    Assert.NotEqual((int)sentence.Gold[i], hints[i]);
                    Assert.True(DemaConsulting.PartOfSpeech.Lexicon.AllowedTags(sentence.Tokens[i].ToLowerInvariant(), lexicon).Contains((Upos)hints[i]));
                }
            }
        }

        Assert.True(wrong > 100);
    }

    /// <summary>
    ///     Validates that training with glossary hints changes the weights (the extra features are learned) and that omitting the process keeps the pinned image.
    /// </summary>
    [Fact]
    public void ModelTrainer_Train_GlossaryHints_ChangeTheImage()
    {
        // Arrange
        var config = SmallConfig();
        var glossary = new GlossaryTraining(EmbeddedModel.Lexicon, 30, 10, 9);

        // Act
        var withHints = ModelBuilder.Build(PerceptronTrainer.Train(config, EmbeddedModel.Lexicon, SmallCorpus(), null, glossary), config, false);
        var plain = TrainImage(config);

        // Assert: different bytes with hints, and the plain run is still the pinned one
        Assert.NotEqual(plain, withHints);
        Assert.Equal(GoldenUnrestricted, ModelBuilder.Sha256Hex(plain));
    }

    /// <summary>
    ///     Validates that the glossary features land in the bucket space and depend on the hint and on the previous tag.
    /// </summary>
    [Fact]
    public void GlossaryFeatures_Append_InBucketSpaceAndDependOnHintAndHistory()
    {
        // Arrange
        var buckets = new int[FeatureExtractor.MaxFeatures + GlossaryFeatures.Count];
        var other = new int[buckets.Length];

        // Act
        var count = GlossaryFeatures.Append(buckets, 3, (int)Upos.Noun, (int)Upos.Det, HashBits);
        GlossaryFeatures.Append(other, 3, (int)Upos.Verb, (int)Upos.Det, HashBits);
        var history = new int[buckets.Length];
        GlossaryFeatures.Append(history, 3, (int)Upos.Noun, (int)Upos.Adj, HashBits);

        // Assert: two features appended, both in range; the hint changes both, the history only the second
        Assert.Equal(3 + GlossaryFeatures.Count, count);
        Assert.All(new[] { buckets[3], buckets[4], other[3], other[4] }, b => Assert.InRange(b, 0, (1 << HashBits) - 1));
        Assert.NotEqual(buckets[3], other[3]);
        Assert.NotEqual(buckets[4], other[4]);
        Assert.Equal(buckets[3], history[3]);
        Assert.NotEqual(buckets[4], history[4]);
    }

    /// <summary>
    ///     Validates that the experiment's decoder without hints gives the same tags as the trainer's predictor (and so as the runtime decoder).
    /// </summary>
    [Fact]
    public void HintedPredictor_NoHints_MatchesQuantizedPredictor()
    {
        // Arrange
        var config = SmallConfig(RestrictionMode.History);
        var corpus = SmallCorpus();
        var rows = ModelBuilder.Rows(PerceptronTrainer.Train(config, EmbeddedModel.Lexicon, corpus), config);
        var reference = new QuantizedPredictor(HashBits, rows);
        var hinted = new HintedPredictor(HashBits, rows, config.Scale, (float)config.Temperature, EmbeddedModel.Lexicon);

        // Act / Assert
        foreach (var sentence in corpus.Distinct().Take(100))
        {
            var tagging = hinted.Predict(sentence.Tokens, null, 0, false, false);
            Assert.Equal(reference.Predict(sentence.Tokens, EmbeddedModel.Lexicon), tagging.Tags);
            Assert.All(tagging.Confidences, c => Assert.InRange(c, 0.0, 1.0));
        }
    }

    /// <summary>
    ///     Validates that a hard hint makes the hinted tag the only candidate and that a huge boost follows the hint.
    /// </summary>
    [Fact]
    public void HintedPredictor_HardHintAndHugeBoost_FollowTheHint()
    {
        // Arrange: a small model and a word that is both a noun and a verb in the lexicon
        var config = SmallConfig(RestrictionMode.History);
        var rows = ModelBuilder.Rows(TrainSmall(config), config);
        var hinted = new HintedPredictor(HashBits, rows, config.Scale, (float)config.Temperature, EmbeddedModel.Lexicon);
        var tokens = new[] { "They", "run" };
        var allowed = DemaConsulting.PartOfSpeech.Lexicon.AllowedTags("run", EmbeddedModel.Lexicon);
        Assert.True(allowed.Contains(Upos.Noun) && allowed.Contains(Upos.Verb));

        // Act / Assert
        foreach (var tag in new[] { Upos.Noun, Upos.Verb })
        {
            var hints = new[] { -1, (int)tag };
            var hard = hinted.Predict(tokens, hints, 0, true, false);
            var boosted = hinted.Predict(tokens, hints, 1e6, false, false);
            Assert.Equal((int)tag, hard.Tags[1]);
            Assert.Equal(1.0, hard.Confidences[1]);
            Assert.Equal(1 << (int)tag, hard.Candidates[1]);
            Assert.Equal((int)tag, boosted.Tags[1]);
        }
    }

#if NET8_0_OR_GREATER
    /// <summary>
    ///     Validates that the committed configuration is valid, names the three corpora and is in the canonical form that the tool writes.
    /// </summary>
    [Fact]
    public void ConfigFile_Parse_CommittedConfiguration_ValidAndCanonical()
    {
        // Arrange
        var text = File.ReadAllText(DataFiles.Repository("tools/DemaConsulting.PartOfSpeech.Trainer/training.json"));

        // Act
        var config = ConfigFile.Parse(text);

        // Assert: valid, three known sources that exist, and rewriting gives the same text (so --update-config makes no noise)
        Assert.Equal(new[] { "open", "masc", "silver" }, config.Sources.Select(s => s.Name));
        Assert.All(config.Sources, s => Assert.True(File.Exists(DataFiles.Repository(s.Path))));
        Assert.Equal(text.Replace("\r\n", "\n"), ConfigFile.Format(config));
    }

    /// <summary>
    ///     Validates that a misspelled setting and an out-of-range value are errors.
    /// </summary>
    [Fact]
    public void ConfigFile_Parse_UnknownMemberOrBadRange_Throws()
    {
        // Arrange / Act / Assert
        Assert.Throws<InvalidDataException>(() => ConfigFile.Parse("{ \"epoch\": 3 }"));
        Assert.Throws<InvalidDataException>(() => ConfigFile.Parse("{ \"sources\": [ { \"name\": \"a\", \"path\": \"p\" } ], \"quantScale\": 3 }"));
        Assert.Throws<InvalidDataException>(() => ConfigFile.Parse("{ \"sources\": [ { \"name\": \"a\", \"path\": \"p\" } ], \"hashBits\": 30 }"));
    }
#endif
}
