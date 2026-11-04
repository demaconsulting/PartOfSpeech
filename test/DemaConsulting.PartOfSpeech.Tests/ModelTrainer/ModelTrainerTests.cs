// cspell:ignore DCDAFUL mklink XQZW
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
        var full = Path.Combine(DataFiles.RepositoryRoot, path);
        Assert.Throws<InvalidOperationException>(() => DataGuard.EnsureTrainable(DataFiles.RepositoryRoot, full));
        Assert.Throws<InvalidOperationException>(() => CorpusReader.ReadFile(DataFiles.RepositoryRoot, full, new SourceConfig { Name = "t" }));
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
        var exception = Record.Exception(() => DataGuard.EnsureTrainable(DataFiles.RepositoryRoot, DataFiles.Repository(path)));

        // Assert: no exception
        Assert.Null(exception);
    }

    /// <summary>
    ///     Validates that a rooted path, a dotted detour out of the data folder and a path outside the data folder are refused.
    /// </summary>
    [Theory]
    [InlineData("data/../README.md")]
    [InlineData("data/open/../../src/anything.tsv")]
    [InlineData("data/../data/open/../eval/gold-v2/train.tsv")]
    [InlineData("data")]
    [InlineData("train.tsv")]
    public void DataGuard_EnsureTrainable_PathOutsideDataRoot_Refused(string relativePath)
    {
        // Arrange
        var full = DataFiles.Repository(relativePath);

        // Act / Assert
        Assert.Throws<InvalidOperationException>(() => DataGuard.EnsureTrainable(DataFiles.RepositoryRoot, full));
    }

    /// <summary>
    ///     Validates that a rooted source path, which <c>Path.Combine</c> lets replace the repository root, is refused when it leads outside the data folder.
    /// </summary>
    [Fact]
    public void CorpusReader_LoadSources_RootedPathOutsideData_Refused()
    {
        // Arrange: a rooted path that is not below the data folder of the repository
        var rooted = Path.Combine(Path.GetTempPath(), "other-corpus.tsv");
        var config = new TrainingConfig { Sources = { new SourceConfig { Name = "rooted", Path = rooted.Replace('\\', '/') } } };

        // Act / Assert
        Assert.Throws<InvalidOperationException>(() => CorpusReader.LoadSources(DataFiles.RepositoryRoot, config));
    }

    /// <summary>
    ///     Validates that a link inside the data folder cannot point a safe-looking path at held-out data or at a place
    ///     outside the data folder. Directory links are symbolic links, or junctions on Windows when symbolic links need a
    ///     privilege that is not held; the file links need symbolic links and are checked only where they can be created.
    /// </summary>
    [Fact]
    public void DataGuard_EnsureTrainable_LinkToHeldOutOrOutsideData_Refused()
    {
#if NET6_0_OR_GREATER
        // Arrange: a fake repository with a training file, a held-out folder and an outside folder
        var repo = Path.Combine(Path.GetTempPath(), "pos-guard-" + Guid.NewGuid().ToString("N"));
        var open = Path.Combine(repo, "data", "open");
        try
        {
            var heldOut = Path.Combine(repo, "data", "eval", "gold-v2");
            var outside = Path.Combine(repo, "elsewhere");
            Directory.CreateDirectory(open);
            Directory.CreateDirectory(heldOut);
            Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(open, "train.tsv"), "# fixture");
            File.WriteAllText(Path.Combine(heldOut, "secret.tsv"), "# fixture");
            File.WriteAllText(Path.Combine(outside, "other.tsv"), "# fixture");
            if (!TryCreateDirectoryLink(Path.Combine(open, "folder"), heldOut) || !TryCreateDirectoryLink(Path.Combine(open, "away"), outside))
            {
                Assert.Skip("Neither symbolic links nor junctions can be created here.");
            }

            // Act / Assert: the directory links lead to refused places although the written paths look like training files
            Assert.Throws<InvalidOperationException>(() => DataGuard.EnsureTrainable(repo, Path.Combine(open, "folder", "secret.tsv")));
            Assert.Throws<InvalidOperationException>(() => DataGuard.EnsureTrainable(repo, Path.Combine(open, "away", "other.tsv")));

            // File links, where the system allows them: held-out and outside targets are refused, a link to a training file is admitted
            if (TryCreateFileLink(Path.Combine(open, "innocent.tsv"), Path.Combine(heldOut, "secret.tsv"))
                && TryCreateFileLink(Path.Combine(open, "outside-file.tsv"), Path.Combine(outside, "other.tsv"))
                && TryCreateFileLink(Path.Combine(open, "same-folder.tsv"), Path.Combine(open, "train.tsv")))
            {
                Assert.Throws<InvalidOperationException>(() => DataGuard.EnsureTrainable(repo, Path.Combine(open, "innocent.tsv")));
                Assert.Throws<InvalidOperationException>(() => DataGuard.EnsureTrainable(repo, Path.Combine(open, "outside-file.tsv")));
                DataGuard.EnsureTrainable(repo, Path.Combine(open, "same-folder.tsv"));
            }

            DataGuard.EnsureTrainable(repo, Path.Combine(open, "train.tsv"));
        }
        finally
        {
            // Remove the directory links first: a recursive delete must not follow them into the targets
            foreach (var link in new[] { Path.Combine(open, "folder"), Path.Combine(open, "away") })
            {
                if (Directory.Exists(link))
                {
                    Directory.Delete(link, false);
                }
            }

            if (Directory.Exists(repo))
            {
                Directory.Delete(repo, true);
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
            File.CreateSymbolicLink(link, target);
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
    ///     Validates that an experiment without an output file is refused, so that it cannot overwrite the shipped weights.
    /// </summary>
    /// <param name="output">The value of <c>--output</c>: absent or empty.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void OutputGuard_EnsureExplicitOutput_ExperimentWithoutOutput_Throws(string? output)
    {
        // Arrange / Act / Assert
        Assert.Throws<ArgumentException>(() => OutputGuard.EnsureExplicitOutput(true, output));
    }

    /// <summary>
    ///     Validates that an experiment with an output file and a default run without one are admitted.
    /// </summary>
    [Fact]
    public void OutputGuard_EnsureExplicitOutput_ExperimentWithOutputOrDefaultRun_Admitted()
    {
        // Arrange / Act
        var experiment = Record.Exception(() => OutputGuard.EnsureExplicitOutput(true, "experiment.bin"));
        var defaultRun = Record.Exception(() => OutputGuard.EnsureExplicitOutput(false, null));

        // Assert: no exception
        Assert.Null(experiment);
        Assert.Null(defaultRun);
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
    ///     Validates that a negative, non-numeric or too large target index of a gold row is refused at parse time with a clear error.
    /// </summary>
    [Theory]
    [InlineData("-1", "outside its sentence")]
    [InlineData("-7", "outside its sentence")]
    [InlineData("4", "outside its sentence")]
    [InlineData("x", "not an integer")]
    public void DevSets_ParseGoldDev_InvalidTarget_Throws(string target, string message)
    {
        // Arrange
        var lines = new[]
        {
            "# columns = id doc_id pos target sentence",
            "d-1\tdoc-a\tNoun\t" + target + "\tThe big dog barks",
        };

        // Act
        var exception = Assert.Throws<InvalidDataException>(() => DevSets.ParseGoldDev(lines, "fixture"));

        // Assert
        Assert.Contains(message, exception.Message);
        Assert.Contains("d-1", exception.Message);
    }

    /// <summary>
    ///     Validates that the quantization reports an exhausted 64-bit range with a clear exception instead of wrapping around.
    /// </summary>
    [Fact]
    public void AveragedPerceptron_Quantize_RangeExceeded_ThrowsClearException()
    {
        // Arrange / Act / Assert: scale * (weight * steps - total) does not fit 64 bits
        Assert.Throws<InvalidOperationException>(() => AveragedPerceptron.Quantize(int.MaxValue, 0, long.MaxValue / 2, 16));

        // 2 * magnitude does not fit 64 bits although the numerator does
        var exception = Assert.Throws<InvalidOperationException>(() => AveragedPerceptron.Quantize(0, -(long.MaxValue / 2) - 1, 1, 1));
        Assert.Contains("epochs * repeats * tokens * scale", exception.Message);

        // Realistic sizes still work
        Assert.Equal(16, AveragedPerceptron.Quantize(1, 0, 1_000_000_000, 16));
    }

    /// <summary>
    ///     Validates that an update that would overflow a weight or an update sum is refused instead of wrapping around.
    /// </summary>
    [Fact]
    public void AveragedPerceptron_Update_SumsExceedRange_ThrowsClearException()
    {
        // Arrange: put the sum of one cell at the top of the range, and a weight at the top of its range
        var perceptron = new AveragedPerceptron(HashBits);
        var totals = (long[])typeof(AveragedPerceptron).GetField("_totals", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(perceptron)!;
        var weights = (int[])typeof(AveragedPerceptron).GetField("_weights", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(perceptron)!;
        perceptron.BeginStep();
        totals[(3 * 17) + 2] = long.MaxValue;
        weights[(4 * 17) + 5] = int.MaxValue;

        // Act / Assert: the sum of bucket 3 and the weight of bucket 4 overflow
        Assert.Throws<InvalidOperationException>(() => perceptron.Update(new[] { 3 }, 1, 2, 7));
        Assert.Throws<InvalidOperationException>(() => perceptron.Update(new[] { 4 }, 1, 5, 7));

        // A normal update still works and counts
        perceptron.Update(new[] { 9 }, 1, 1, 2);
        Assert.Equal(1, perceptron.Mistakes);
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
                Assert.True(DemaConsulting.PartOfSpeech.Lexicon.AllowedTags(sentence.Tokens[i], lexicon).Count >= 2);
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
                    Assert.True(DemaConsulting.PartOfSpeech.Lexicon.AllowedTags(sentence.Tokens[i], lexicon).Contains((Upos)hints[i]));
                }
            }
        }

        Assert.True(wrong > 100);
    }

    /// <summary>
    ///     Validates that the eligibility of a token uses the token as written, as the runtime decoder does: a capitalized
    ///     token also allows PROPN and X, so a single-tag word becomes ambiguous (and eligible) only when it is capitalized.
    /// </summary>
    [Fact]
    public void GlossaryTraining_Draw_CapitalizedToken_UsesShapeCandidatesOfTheOriginalToken()
    {
        // Arrange: pick a function word with exactly one lexicon tag; capitalized, it also allows PROPN and X
        var lexicon = EmbeddedModel.Lexicon;
        var word = new[] { "of", "because", "whereas", "although", "nevertheless", "moreover", "thereby", "hence", "albeit", "unto", "amongst" }
            .FirstOrDefault(w => DemaConsulting.PartOfSpeech.Lexicon.AllowedTags(w, lexicon).Count == 1);
        Assert.NotNull(word);
        var only = (sbyte)DemaConsulting.PartOfSpeech.Lexicon.AllowedTags(word, lexicon).Single();
        var capitalized = new TrainingSentence("s1", "x", new[] { char.ToUpperInvariant(word[0]) + word.Substring(1) }, new[] { (sbyte)Upos.Propn });
        var lower = new TrainingSentence("s2", "x", new[] { word }, new[] { only });
        var training = new GlossaryTraining(lexicon, 100, 0, 5);

        // Act
        var capitalizedHints = training.Draw(capitalized, new DeterministicRandom(1));
        var lowerHints = training.Draw(lower, new DeterministicRandom(1));

        // Assert: the capitalized PROPN token gets its gold hint; the unambiguous lower-case token gets none
        Assert.Equal((int)Upos.Propn, capitalizedHints[0]);
        Assert.Equal(-1, lowerHints[0]);
    }

    /// <summary>
    ///     Validates that the glossary eligibility agrees with the runtime candidate mask for every token of a corpus sample.
    /// </summary>
    [Fact]
    public void GlossaryTraining_Draw_CorpusSample_EligibilityMatchesRuntimeMask()
    {
        // Arrange
        var lexicon = EmbeddedModel.Lexicon;
        var training = new GlossaryTraining(lexicon, 100, 0, 5);
        var workspace = new DecoderWorkspace();

        // Act / Assert: a hint exists exactly where the runtime mask has two or more tags including the gold tag
        foreach (var sentence in SmallCorpus().Distinct().Take(60))
        {
            var hints = training.Draw(sentence, new DeterministicRandom(2));
            FeatureExtractor.Prepare(workspace, sentence.Tokens, HashBits);
            for (var i = 0; i < hints.Length; i++)
            {
                var mask = TagSelection.AllowedMask(workspace, i, lexicon);
                var gold = sentence.Gold[i];
                var eligible = gold >= 0 && Enumerable.Range(0, 17).Count(t => (mask & (1 << t)) != 0) >= 2 && (mask & (1 << gold)) != 0;
                Assert.Equal(eligible, hints[i] >= 0);
            }
        }
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

    /// <summary>
    ///     Creates a gold v2 table fixture from rows of (id, document, target index, form, class, lemma, sentence).
    /// </summary>
    private static List<GoldTarget> GoldFixture(params string[] rows)
    {
        var lines = new List<string>
        {
            "# corpus = fixture, dev split",
            "# columns = id doc_id source terms_url sent_id target form strata pos lemma features tense sense status agreement sentence",
        };
        lines.AddRange(rows.Select(r => r.Split('|')).Select(c => $"{c[0]}\t{c[1]}\ts\tu\tx\t{c[2]}\t{c[3]}\tC\t{c[4]}\t{c[5]}\t-\t-\t-\tagreed\tfull\t{c[6]}"));
        return DevSets.ParseGoldDev(lines, "fixture");
    }

    /// <summary>
    ///     Creates an open development fixture from sentences written as <c>word/TAG</c> separated by spaces.
    /// </summary>
    private static List<TrainingSentence> OpenFixture(params string[] sentences)
    {
        var lines = new List<string>();
        for (var i = 0; i < sentences.Length; i++)
        {
            lines.Add($"# sent_id = s{i}");
            lines.Add($"# source_id = d{i}");
            lines.AddRange(sentences[i].Split(' ').Select(w => w.Split('/')).Select(p => $"{p[0]}\t{p[1]}\tunanimous"));
            lines.Add(string.Empty);
        }

        return DevSets.ParseOpenDev(lines, "fixture");
    }

    /// <summary>
    ///     The lexicon of the tiny glossary evaluation model.
    /// </summary>
    private static readonly byte[] GlossaryEvalLexicon = TinyModels.CreateLexicon(
        "the DET", "open VERB ADJ", "bleed NOUN VERB", "valve NOUN", "now ADV", "and CCONJ", "it PRON", ". PUNCT");

    /// <summary>
    ///     Creates the tiny tagger of the glossary evaluation tests: <c>bleed</c> is a verb for the model (by 4 raw units at temperature 1).
    /// </summary>
    private static Tagger GlossaryEvalTagger() =>
        TinyModels.CreateTagger(
            new TinyModels.WeightsBuilder()
                .Word("the", Upos.Det, 5).Word("open", Upos.Verb, 3).Word("bleed", Upos.Verb, 5).Word("bleed", Upos.Noun, 1)
                .Word("valve", Upos.Noun, 3).Word("now", Upos.Adv, 3).Word("and", Upos.Cconj, 5).Word("it", Upos.Pron, 5).Word(".", Upos.Punct, 5)
                .Build(temperature: 1f),
            GlossaryEvalLexicon,
            new TaggerOptions { MaxAlternatives = 16 });

    /// <summary>
    ///     The two sentences of the tiny glossary evaluation: one with the compound, one without.
    /// </summary>
    private static List<TrainingSentence> GlossaryEvalSentences() =>
        OpenFixture(
            "open/VERB the/DET bleed/NOUN valve/NOUN now/ADV and/CCONJ it/PRON ./PUNCT",
            "open/VERB the/DET valve/NOUN ./PUNCT");

    /// <summary>
    ///     Loads the tiny evaluation lexicon.
    /// </summary>
    private static DemaConsulting.PartOfSpeech.Lexicon GlossaryEvalLexiconModel()
    {
        using var stream = new MemoryStream(GlossaryEvalLexicon);
        return DemaConsulting.PartOfSpeech.Lexicon.Load(stream);
    }

    /// <summary>
    ///     Validates that the gold v2 parser reads the lemma column when there is one and gives an empty lemma otherwise.
    /// </summary>
    [Fact]
    public void GoldTarget_Lemma_IsParsedAndOptional()
    {
        // Arrange: a row with a lemma, a row with the placeholder, and a table without a lemma column
        var withLemma = GoldFixture("d-1|doc-a|3|valves|Noun|valve|The big red valves leak .", "d-2|doc-a|1|it|Pronoun|-|It leaks .");
        var withoutColumn = DevSets.ParseGoldDev(
            new[]
            {
                "# columns = id doc_id sentence target pos",
                "d-1\tdoc-a\tThe dog barks\t1\tNoun",
            },
            "fixture");

        // Act / Assert
        Assert.Equal("valve", withLemma[0].Lemma);
        Assert.Equal(string.Empty, withLemma[1].Lemma);
        Assert.Equal(string.Empty, withoutColumn[0].Lemma);
        Assert.Equal(string.Empty, new GoldTarget("i", "d", new[] { "a" }, 0, "Noun").Lemma);
    }

    /// <summary>
    ///     Validates that the default soft strength is a member of the evaluated grid, so the constant and the evaluation cannot drift apart.
    /// </summary>
    [Fact]
    public void GlossaryEvaluation_StrengthGrid_ContainsTheDefaultSoftStrength()
    {
        // Arrange / Act / Assert
        Assert.Contains(GlossaryOptions.DefaultSoftStrength, GlossaryEvaluation.StrengthGrid);
        Assert.Equal(GlossaryEvaluation.StrengthGrid.OrderBy(s => s), GlossaryEvaluation.StrengthGrid);
    }

    /// <summary>
    ///     Validates the pre-registered strength rule on a synthetic table: inadmissible strengths are out, strengths within 0.1 points of the best are tied, and the smallest tied strength wins.
    /// </summary>
    [Fact]
    public void GlossaryEvaluation_ChooseSoftStrength_PicksSmallestWithinTolerance()
    {
        // Arrange: strength 4 has the best covered accuracy but loses accuracy at r = 10%; 1, 2 and 3 are within 0.1 of the best admissible one
        var candidates = new List<StrengthCandidate>
        {
            new() { Strength = 0.5, CoveredAccuracy0 = 98.0 },
            new() { Strength = 1, CoveredAccuracy0 = 98.5 },
            new() { Strength = 2, CoveredAccuracy0 = 98.55 },
            new() { Strength = 3, CoveredAccuracy0 = 98.6 },
            new() { Strength = 4, AccuracyDifference10 = -0.01, CoveredAccuracy0 = 99.5 },
        };

        // Act
        var chosen = GlossaryEvaluation.ChooseSoftStrength(candidates, out var admissible);

        // Assert
        Assert.True(admissible);
        Assert.Equal(1, chosen);
        candidates[3].CoveredAccuracy0 = 98.7;
        Assert.Equal(3, GlossaryEvaluation.ChooseSoftStrength(candidates, out _));
    }

    /// <summary>
    ///     Validates each admissibility condition of the strength rule, and that without an admissible strength the smallest of the grid is chosen.
    /// </summary>
    [Fact]
    public void GlossaryEvaluation_ChooseSoftStrength_ConditionsAndNoneAdmissible()
    {
        // Arrange / Act / Assert: the boundaries of the three conditions
        Assert.True(GlossaryEvaluation.IsAdmissible(new StrengthCandidate { AccuracyDifference30 = -0.5 }));
        Assert.False(GlossaryEvaluation.IsAdmissible(new StrengthCandidate { AccuracyDifference30 = -0.51 }));
        Assert.False(GlossaryEvaluation.IsAdmissible(new StrengthCandidate { SwapDifference10 = 0.01 }));
        Assert.False(GlossaryEvaluation.IsAdmissible(new StrengthCandidate { AccuracyDifference10 = -0.01 }));

        // None admissible: the smallest strength, whatever its figures
        var candidates = new[]
        {
            new StrengthCandidate { Strength = 2, SwapDifference10 = 1, CoveredAccuracy0 = 99 },
            new StrengthCandidate { Strength = 0.5, SwapDifference10 = 3, CoveredAccuracy0 = 90 },
        };
        Assert.Equal(0.5, GlossaryEvaluation.ChooseSoftStrength(candidates, out var admissible));
        Assert.False(admissible);
        Assert.Throws<ArgumentException>(() => GlossaryEvaluation.ChooseSoftStrength(Array.Empty<StrengthCandidate>(), out _));
    }

    /// <summary>
    ///     Validates the pre-registered bar of scenario G: a gain of at least 0.5 points whose interval excludes zero, and no loss at r = 10%.
    /// </summary>
    [Fact]
    public void GlossaryEvaluation_ChooseNonHeadRule_BarNeedsGainIntervalAndNoLoss()
    {
        // Arrange: one rule per way to miss the bar, and two that meet it
        var candidates = new List<RuleCandidate>
        {
            new() { Name = "interval touches zero", Gain0 = 1.0, Low0 = 0.0, Gain10 = 1.0 },
            new() { Name = "small gain", Gain0 = 0.49, Low0 = 0.1, Gain10 = 1.0 },
            new() { Name = "loss at r=10", Gain0 = 2.0, Low0 = 1.0, Gain10 = -0.1 },
        };

        // Act / Assert: none qualifies, so the default stays
        Assert.Null(GlossaryEvaluation.ChooseNonHeadRule(candidates));

        // With two that qualify the larger gain wins
        candidates.Add(new RuleCandidate { Name = "boundary", Gain0 = 0.5, Low0 = 0.01, Gain10 = 0.0 });
        Assert.Equal("boundary", GlossaryEvaluation.ChooseNonHeadRule(candidates));
        candidates.Add(new RuleCandidate { Name = "best", Gain0 = 0.9, Low0 = 0.4, Gain10 = 0.9 });
        Assert.Equal("best", GlossaryEvaluation.ChooseNonHeadRule(candidates));
    }

    /// <summary>
    ///     Validates the compound candidates of scenario A: the longest suffix of two or three tokens of a run of nominal tokens that ends in a noun, one term per distinct text.
    /// </summary>
    [Fact]
    public void GlossaryEvaluation_CompoundCandidates_SpansEndInNounAndAreDistinct()
    {
        // Arrange: a run ADJ NOUN NOUN (three tokens), a run ending in an adjective (no compound), a single noun (no compound), a repeated compound and a token with a digit
        var open = OpenFixture(
            "the/DET old/ADJ kitchen/NOUN table/NOUN is/AUX ./PUNCT",
            "a/DET big/ADJ dog/NOUN ./PUNCT",
            "the/DET kitchen/NOUN table/NOUN ./PUNCT",
            "it/PRON is/AUX the/DET table/NOUN old/ADJ ./PUNCT",
            "a/DET 3d/ADJ printer/NOUN ./PUNCT");

        // Act
        var candidates = GlossaryEvaluation.CompoundCandidates(open, EmbeddedModel.Lexicon);

        // Assert: ordinal order, the three-token suffix and the two-token compounds, no span with a digit
        Assert.Equal(new[] { "big dog", "kitchen table", "old kitchen table" }, candidates.Select(c => c.Text));
        Assert.All(candidates, c => Assert.Contains(c.Tag, new[] { Upos.Noun, Upos.Propn }));
        Assert.All(candidates, c => Assert.InRange(c.Tokens.Length, 2, 3));
    }

    /// <summary>
    ///     Validates the draw of a simulated glossary: deterministic for a seed, the coverage share, and a wrong entry never keeps the right tag.
    /// </summary>
    [Fact]
    public void GlossaryEvaluation_DrawTerms_DeterministicSeededAndWrongEntriesDiffer()
    {
        // Arrange: fifty candidates
        var candidates = Enumerable.Range(0, 50).Select(i => new SimTerm(new[] { "w" + i.ToString("00"), "x" }, Upos.Noun)).ToList();

        // Act
        var first = GlossaryEvaluation.DrawTerms(candidates, 300, 0, 7, GlossaryEvaluation.WrongCompoundTags);
        var again = GlossaryEvaluation.DrawTerms(candidates, 300, 0, 7, GlossaryEvaluation.WrongCompoundTags);
        var other = GlossaryEvaluation.DrawTerms(candidates, 300, 0, 8, GlossaryEvaluation.WrongCompoundTags);
        var allWrong = GlossaryEvaluation.DrawTerms(candidates, 1000, 100, 7, GlossaryEvaluation.WrongCompoundTags);
        var someWrong = GlossaryEvaluation.DrawTerms(candidates, 1000, 30, 7, GlossaryEvaluation.WrongCompoundTags);

        // Assert: 15 of 50, reproducible, seed dependent; with 100% wrong no entry keeps its tag, with 30% about a third do not
        Assert.Equal(15, first.Count);
        Assert.Equal(first.Select(t => t.Text), again.Select(t => t.Text));
        Assert.NotEqual(first.Select(t => t.Text), other.Select(t => t.Text));
        Assert.All(first, t => Assert.Equal(Upos.Noun, t.Tag));
        Assert.Equal(50, allWrong.Count);
        Assert.All(allWrong, t => Assert.NotEqual(Upos.Noun, t.Tag));
        Assert.InRange(someWrong.Count(t => t.Tag != Upos.Noun), 5, 25);
    }

    /// <summary>
    ///     Validates that terms the glossary cannot represent as simulated (punctuation, an abbreviation the tokenizer splits) and repeated terms are skipped and counted.
    /// </summary>
    [Fact]
    public void GlossaryEvaluation_ToEntries_UnrepresentableTermsAreSkipped()
    {
        // Arrange
        var terms = new[]
        {
            new SimTerm(new[] { "valve" }, Upos.Noun),
            new SimTerm(new[] { "." }, Upos.Punct),
            new SimTerm(new[] { "dr." }, Upos.Propn),
            new SimTerm(new[] { "valve" }, Upos.Verb),
            new SimTerm(new[] { "bleed", "valve" }, Upos.Noun),
        };
        var skippedTexts = new SortedSet<string>(StringComparer.Ordinal);

        // Act
        var entries = GlossaryEvaluation.ToEntries(terms, out var skipped, skippedTexts);

        // Assert: the two entries that the glossary can express, three skipped
        Assert.Equal(new[] { "valve", "bleed valve" }, entries.Select(e => e.Term));
        Assert.Equal(3, skipped);
        Assert.Contains(".", skippedTexts);
    }

    /// <summary>
    ///     Validates that the categories partition every scored token of a run: before the match, the non-head and head of the match, and the tokens at distances +1, +2 and later; and that the at-risk flag marks the first token of a compound that the lexicon lists as a verb.
    /// </summary>
    [Fact]
    public void GlossaryEvaluation_RunSystem_CategoriesPartitionEveryScoredToken()
    {
        // Arrange: the compound "bleed valve" is a right entry in the first sentence; the second sentence has no match
        var tagger = GlossaryEvalTagger();
        var lexicon = GlossaryEvalLexiconModel();
        var set = EvalSet.FromOpen(GlossaryEvalSentences());
        var entries = new[] { new GlossaryEntry("bleed valve", Upos.Noun) };

        // Act
        var run = GlossaryEvaluation.RunSystem(tagger, set, GlossaryEvaluation.MakeGlossary(entries, GlossaryMode.Hard, 1), lexicon, "hard");

        // Assert: twelve scored tokens, each in exactly one category
        Assert.Equal(12, set.ScoredCount);
        Assert.Equal(
            new[]
            {
                GlossaryCategory.Before, GlossaryCategory.Before, GlossaryCategory.NonHead, GlossaryCategory.HeadRight,
                GlossaryCategory.Plus1, GlossaryCategory.Plus2, GlossaryCategory.Later, GlossaryCategory.Later,
                GlossaryCategory.NoMatch, GlossaryCategory.NoMatch, GlossaryCategory.NoMatch, GlossaryCategory.NoMatch,
            },
            run.Categories);
        Assert.Equal(new[] { 2 }, Enumerable.Range(0, 12).Where(i => run.AtRisk[i]));
        Assert.Equal(1, run.Categories.Count(GlossaryEvaluation.IsCovered) - run.Categories.Count(c => c == GlossaryCategory.NonHead));
        Assert.Equal(6, run.Categories.Count(GlossaryEvaluation.IsNeighbor));

        // A wrong entry makes the head wrong
        var wrong = GlossaryEvaluation.RunSystem(tagger, set, GlossaryEvaluation.MakeGlossary(new[] { new GlossaryEntry("bleed valve", Upos.Verb) }, GlossaryMode.Hard, 1), lexicon, "wrong");
        Assert.Equal(GlossaryCategory.HeadWrong, wrong.Categories[3]);
    }

    /// <summary>
    ///     Validates the swap count, the flips and the baseline denominator: a hard constraint that fixes a confident noun-verb error removes the swap and the harmful error, and a hard wrong entry is a swap although it leaves one candidate (so no harmful error).
    /// </summary>
    [Fact]
    public void GlossaryEvaluation_RunScenario_ConfidentSwapsFlipsAndBaselineDenominator()
    {
        // Arrange: the model reads "bleed" as a verb with certainty; the gold says noun
        var tagger = GlossaryEvalTagger();
        var lexicon = GlossaryEvalLexiconModel();
        var set = EvalSet.FromOpen(GlossaryEvalSentences());
        var none = GlossaryEvaluation.RunSystem(tagger, set, null, lexicon, "no glossary");

        // Act: a right entry, then a wrong one
        var right = GlossaryEvaluation.RunScenario(tagger, set, new[] { new GlossaryEntry("bleed valve", Upos.Noun) }, lexicon, none);
        GlossaryEvaluation.Pair(right, 20, 1);
        var wrong = GlossaryEvaluation.RunScenario(tagger, set, new[] { new GlossaryEntry("bleed valve", Upos.Verb) }, lexicon, none);

        // Assert: the baseline has one confident swap and one harmful error ("bleed"), the right entry removes both
        Assert.Equal(new[] { 2 }, Enumerable.Range(0, 12).Where(i => none.Swaps[i]));
        Assert.Equal(1, none.Outcomes.Count(o => o.Harmful));
        Assert.Equal(1, none.Outcomes.Count(o => o.NounVerb));
        Assert.DoesNotContain(true, right.Systems[0].Swaps);
        Assert.Equal(-1000.0, right.Cells[0].Harmful.Difference);
        Assert.Equal(-1000.0, right.Cells[0].Swaps.Difference);
        Assert.Equal(100.0 / 12, right.Cells[0].Accuracy.Difference, 9);
        GlossaryEvaluation.CountFlips(none, right.Systems[0], right.Categories, GlossaryCategory.NonHead, out var rightToWrong, out var wrongToRight);
        Assert.Equal((0, 1), (rightToWrong, wrongToRight));

        // The wrong entry forces the head to VERB with one candidate: a confident swap that Judge does not count as a noun-verb decision
        Assert.True(wrong.Systems[0].Swaps[3]);
        Assert.False(wrong.Systems[0].Outcomes[3].NounVerb);
        Assert.False(wrong.Systems[0].Outcomes[3].Harmful);
        Assert.Equal(1.0, wrong.Systems[0].Outcomes[3].Confidence);

        // The invariant: nothing before the first match changes
        Assert.Equal(0, GlossaryEvaluation.CountBeforeDifferences(none, right.Systems[0], right.Categories, out var before));
        Assert.Equal(2, before);
    }

    /// <summary>
    ///     Validates the acronym types of scenario D: all-capital tokens of two to five letters whose lower case is a lexicon word and whose majority gold is a noun.
    /// </summary>
    [Fact]
    public void GlossaryEvaluation_AcronymEntries_AllCapitalNounsWithLexiconHomograph()
    {
        // Arrange: "IT" is a noun twice, "US" is a pronoun, "XQZW" is not a lexicon word, "It" is not an acronym shape
        var set = EvalSet.FromOpen(OpenFixture(
            "the/DET IT/NOUN team/NOUN ./PUNCT",
            "our/PRON IT/NOUN department/NOUN ./PUNCT",
            "give/VERB US/PRON it/PRON ./PUNCT",
            "the/DET XQZW/NOUN team/NOUN ./PUNCT",
            "It/PRON works/VERB ./PUNCT"));

        // Act
        var entries = GlossaryEvaluation.AcronymEntries(EmbeddedModel.Lexicon, new[] { set });

        // Assert
        Assert.Equal(new[] { "IT" }, entries.Select(e => e.Term));
        Assert.Equal(Upos.Noun, entries[0].Tags.Single());
    }

    /// <summary>
    ///     Validates that the whole evaluation runs on tiny dev-like data, reports every part, is deterministic and finds no change before a match.
    /// </summary>
    [Fact]
    public void GlossaryEvaluation_Run_TinyData_ReportsEveryScenarioDeterministically()
    {
        // Arrange: a small trained model and a few sentences with a compound, an acronym and its homograph; gold targets with lemmas
        var config = SmallConfig(RestrictionMode.History);
        var weights = ModelBuilder.Build(TrainSmall(config), config, false);
        var open = OpenFixture(
            "the/DET old/ADJ kitchen/NOUN table/NOUN is/AUX here/ADV ./PUNCT",
            "they/PRON open/VERB the/DET kitchen/NOUN table/NOUN now/ADV ./PUNCT",
            "the/DET IT/NOUN team/NOUN works/VERB ./PUNCT",
            "i/PRON use/VERB it/PRON now/ADV ./PUNCT",
            "she/PRON gives/VERB up/ADP ./PUNCT",
            "he/PRON gave/VERB up/ADP ./PUNCT");
        var gold = GoldFixture(
            "d-1|doc-a|3|valves|Noun|valve|The big red valves leak .",
            "d-2|doc-b|1|valve|Noun|valve|The valve leaks .",
            "d-3|doc-c|1|opened|Verb|open|They opened the valve .",
            "d-4|doc-d|1|bought|Verb|buy|They bought a valve .");

        // Act
        var first = GlossaryEvaluation.Run(weights, open, gold, EmbeddedModel.Lexicon, 1, 20);
        var second = GlossaryEvaluation.Run(weights, open, gold, EmbeddedModel.Lexicon, 1, 20);
        var text = string.Join("\n", first);

        // Assert: reproducible, every part is reported, the parity and the invariant hold
        Assert.Equal(first, second);
        Assert.Contains("**Parity (B), tags**", text);
        Assert.Contains("give 0 different tags", text);
        Assert.Contains("**Scenario sizes", text);
        Assert.Contains("**A, coverage 30%", text);
        Assert.Contains("**AB, coverage 100%", text);
        Assert.Contains("**A, at-risk subset", text);
        Assert.Contains("**F, history effect, scenario AB", text);
        Assert.Contains("differ from the no-glossary tags in 0 of", text);
        Assert.Contains("**E, coverage 100%", text);
        Assert.Contains("**C, inflection from gold lemmas", text);
        Assert.Contains("| Verb, inflected form | 2 |", text);
        Assert.Contains("**D, case sensitivity", text);
        Assert.Contains("**G, phrasal verbs", text);
        Assert.Contains("**Decision (soft strength)**", text);
        Assert.Contains("**Decision (non-head rule)**", text);
        Assert.Throws<ArgumentOutOfRangeException>(() => GlossaryEvaluation.Run(weights, open, gold, EmbeddedModel.Lexicon, 1, 0));
    }

    /// <summary>
    ///     Validates that the glossary evaluation is structurally unable to read any file: its entry point takes no path, and its sources use no file API and name no data file.
    /// </summary>
    [Fact]
    public void GlossaryEvaluation_UsesOnlyDevData_NoPathParameterAndNoFileAccess()
    {
        // Arrange: the entry point and the two source files of the evaluation
        var run = typeof(GlossaryEvaluation).GetMethod(nameof(GlossaryEvaluation.Run))!;
        var folder = DataFiles.Repository("tools/DemaConsulting.PartOfSpeech.Trainer/Evaluation");
        var sources = new[] { "GlossaryEvaluation.cs", "GlossaryEvaluationReport.cs" }.Select(f => File.ReadAllText(Path.Combine(folder, f))).ToList();

        // Act / Assert: the parameters are data and settings only (weights, sentences, targets, lexicon, seed, resamples)
        Assert.DoesNotContain(run.GetParameters(), p => p.ParameterType == typeof(string));
        Assert.Equal(
            new[] { typeof(byte[]), typeof(IReadOnlyList<TrainingSentence>), typeof(IReadOnlyList<GoldTarget>), typeof(DemaConsulting.PartOfSpeech.Lexicon), typeof(ulong), typeof(int) },
            run.GetParameters().Select(p => p.ParameterType));
        foreach (var text in sources)
        {
            foreach (var forbidden in new[] { "File.", "Directory.", "FileStream", "StreamReader", "DevSets.Read", "Path." })
            {
                Assert.DoesNotContain(forbidden, text);
            }
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
