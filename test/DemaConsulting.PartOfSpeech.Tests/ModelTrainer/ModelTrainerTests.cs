// cspell:ignore amongst thedogsrun DCDAFUL mklink XQZW
using System.Globalization;
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
    private const string GoldenUnrestricted = "faa097a1197b9527a42de10f4292db35e39b6eca088c5147449d261577b78f52";

    /// <summary>
    ///     The pinned SHA-256 of the uncompressed image of the small fully restricted run (cross-platform golden).
    /// </summary>
    private const string GoldenRestricted = "387e1eee31545ffc442a112a8b2684b1f21c81f4e5fc004da731adb97ca253b1";

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
    ///     Validates that status flags (<c>+mentioned</c>, <c>+foreign</c>) are read and kept, and that they change neither the tag nor the masking.
    /// </summary>
    [Fact]
    public void CorpusReader_Parse_StatusFlags_AreKeptAndDoNotChangeTagsOrMasking()
    {
        // Arrange: flagged rows of an open-style file (three cells) and of a silver-style file (a source tag)
        var lines = new[]
        {
            "# sent_id = s1",
            "# source_id = d1",
            "using\tVERB\tunanimous",
            "the\tDET\tunanimous",
            "verb\tNOUN\tunanimous+mentioned",
            "snowball\tVERB\tdisputed+mentioned",
            "Sierra\tNOUN\tunchecked+foreign+mentioned",
            "word\tNOUN\tsilver+foreign\tN:99",
            "x\t_\tmasked+mentioned\t?",
        };

        // Act: default masking (disputed tokens masked) and then keeping the disputed tokens
        var plain = Parse(new SourceConfig { Name = "t" }, lines);
        var kept = Parse(new SourceConfig { Name = "t", MaskDisputed = false }, lines);

        // Assert: flags are kept per token, the tags are the plain tags, and masking looks at the status only
        var flags = new[]
        {
            TokenMarks.None, TokenMarks.None, TokenMarks.Mentioned, TokenMarks.Mentioned,
            TokenMarks.Foreign | TokenMarks.Mentioned, TokenMarks.Foreign, TokenMarks.Mentioned,
        };
        Assert.Equal(flags, plain[0].Marks);
        Assert.Equal(flags, kept[0].Marks);
        Assert.Equal(
            new sbyte[] { (sbyte)Upos.Verb, (sbyte)Upos.Det, (sbyte)Upos.Noun, -1, (sbyte)Upos.Noun, (sbyte)Upos.Noun, -1 },
            plain[0].Gold);
        Assert.Equal((sbyte)Upos.Verb, kept[0].Gold[3]);
    }

    /// <summary>
    ///     Validates that a corpus row without flags has no flags on any token, so that the committed files stay valid.
    /// </summary>
    [Fact]
    public void CorpusReader_Parse_NoStatusFlags_GivesNoFlags()
    {
        // Arrange / Act
        var sentences = Parse(new SourceConfig { Name = "t" }, "# sent_id = s1", "a\tDET\tunanimous", "b\tNOUN\tdisputed");

        // Assert
        Assert.Equal(new[] { TokenMarks.None, TokenMarks.None }, sentences[0].Marks);
    }

    /// <summary>
    ///     Validates that an unknown status, or an unknown, repeated or out-of-order status flag, is reported with its line number.
    /// </summary>
    /// <param name="status">The status cell.</param>
    [Theory]
    [InlineData("unanimous+shouted")]
    [InlineData("unanimous+mentioned+mentioned")]
    [InlineData("unanimous+mentioned+foreign")]
    [InlineData("unanimous+")]
    [InlineData("")]
    [InlineData("bogus")]
    [InlineData("UNANIMOUS")]
    [InlineData(" unanimous")]
    [InlineData("+mentioned")]
    [InlineData("mentioned")]
    public void CorpusReader_Parse_InvalidStatusFlags_ThrowsWithLine(string status)
    {
        // Arrange / Act
        var ex = Assert.Throws<InvalidDataException>(() => Parse(new SourceConfig { Name = "t" }, "# sent_id = s1", "a\tNOUN\t" + status));

        // Assert
        Assert.Contains("line 2", ex.Message);
        Assert.Contains($"'{status}'", ex.Message);
    }

    /// <summary>
    ///     Validates that the trainer's reader gives the same verdicts, statuses, flags and error values as the Python reader
    ///     for the shared parity cases (<c>tools/data/tests/status-cases.tsv</c>, also read by the Python tests).
    /// </summary>
    [Fact]
    public void CorpusReader_Parse_SharedParityCases_AgreeWithPython()
    {
        // Arrange: the shared cases (tag, status cell, verdict, status, flags)
        var path = Path.Combine(DataFiles.RepositoryRoot, "tools", "data", "tests", "status-cases.tsv");
        var cases = File.ReadLines(path).Where(line => line.Length > 0 && !line.StartsWith("# ", StringComparison.Ordinal)).Select(line => line.Split('\t')).ToList();
        Assert.True(cases.Count > 20);

        foreach (var (tag, cell, verdict, status, flags) in cases.Select(c => (c[0], c[1], c[2], c[3], c[4])))
        {
            // Act
            var attempt = Record.Exception(() => Parse(new SourceConfig { Name = "t" }, "# sent_id = s1", "a\t" + tag + "\t" + cell));

            // Assert
            if (verdict == "accept")
            {
                Assert.Null(attempt);
                Assert.True(CorpusStatus.TryParse(cell, out var parsedStatus, out var marks), cell);
                Assert.Equal(status, parsedStatus);
                var expected = flags == "-" ? TokenMarks.None : flags.Split(',').Select(f => (TokenMarks)Enum.Parse(typeof(TokenMarks), f, true)).Aggregate((x, y) => x | y);
                Assert.Equal(expected, marks);
            }
            else
            {
                var ex = Assert.IsType<InvalidDataException>(attempt);
                Assert.Contains("line 2", ex.Message);
                Assert.Contains(verdict == "invalid-status" ? $"invalid status '{cell}'" : $"unknown tag '{tag}'", ex.Message);
            }
        }

        // A row with fewer than three cells is refused like the Python reader does
        var tooShort = Assert.Throws<InvalidDataException>(() => Parse(new SourceConfig { Name = "t" }, "# sent_id = s1", "a\tNOUN"));
        Assert.Contains("a token row needs at least form, tag and status", tooShort.Message);
    }

    /// <summary>
    ///     Validates the status cell helpers: the base status without its flags and the canonical flag order.
    /// </summary>
    [Fact]
    public void CorpusStatus_TryParse_SplitsStatusAndFlags()
    {
        // Arrange / Act
        var plain = CorpusStatus.TryParse("disputed", out var plainStatus, out var plainFlags);
        var both = CorpusStatus.TryParse("silver+foreign+mentioned", out var bothStatus, out var bothFlags);
        var swapped = CorpusStatus.TryParse("silver+mentioned+foreign", out _, out _);

        // Assert
        Assert.True(plain);
        Assert.Equal("disputed", plainStatus);
        Assert.Equal(TokenMarks.None, plainFlags);
        Assert.True(both);
        Assert.Equal("silver", bothStatus);
        Assert.Equal(TokenMarks.Foreign | TokenMarks.Mentioned, bothFlags);
        Assert.False(swapped);
        Assert.Equal("disputed", CorpusStatus.BaseStatus("disputed+mentioned"));
        Assert.Equal("masked", CorpusStatus.BaseStatus("masked"));
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
    [InlineData("data/eval/shapes-dev/dev.tsv")]
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
    [InlineData("data/eval/shapes-dev/dev.tsv")]
    [InlineData("cache/ud-ewt/en_ewt-ud-train.conllu")]
    public void DataGuard_EnsureTrainable_HeldOutOrDevelopmentFile_Refused(string path)
    {
        // Arrange / Act / Assert
        var full = Path.Combine(DataFiles.RepositoryRoot, path);
        Assert.Throws<InvalidOperationException>(() => DataGuard.EnsureTrainable(DataFiles.RepositoryRoot, full));
        Assert.Throws<InvalidOperationException>(() => CorpusReader.ReadFile(DataFiles.RepositoryRoot, full, new SourceConfig { Name = "t" }));
    }

    /// <summary>
    ///     Validates that the six training corpora are admitted as training sources.
    /// </summary>
    [Theory]
    [InlineData("data/open/train.tsv")]
    [InlineData("data/masc/masc.tsv")]
    [InlineData("data/silver/silver.tsv")]
    [InlineData("data/imperative/imperative.tsv")]
    [InlineData("data/compound/compound.tsv")]
    [InlineData("data/headings/headings.tsv")]
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
    ///     Validates that training with every feature group (the most buckets a token can have) and glossary hints on every eligible token fits the
    ///     bucket buffer: the buffer holds the group capacity plus the glossary buckets, and the run completes and is reproducible.
    /// </summary>
    [Fact]
    public void ModelTrainer_Train_AllGroupsWithGlossaryHints_FitsTheBucketBuffer()
    {
        // Arrange: all four groups and a glossary that hints every eligible token (a token of the second pass has the full group capacity)
        var config = GroupConfig(FeatureGroups.PositionCues | FeatureGroups.ClauseCues | FeatureGroups.RightPreview | FeatureGroups.Inflection);
        var corpus = SmallCorpus();

        // Act: before the buffer had room for the glossary buckets this overran it
        var first = PerceptronTrainer.Train(config, EmbeddedModel.Lexicon, corpus, null, new GlossaryTraining(EmbeddedModel.Lexicon, 100, 0, 5));
        var second = PerceptronTrainer.Train(config, EmbeddedModel.Lexicon, corpus, null, new GlossaryTraining(EmbeddedModel.Lexicon, 100, 0, 5));

        // Assert: the buffer is sized for both, and the run is deterministic and different from the run without hints
        Assert.Equal(FeatureExtractor.BucketCapacity + GlossaryFeatures.Count, GlossaryFeatures.BufferCapacity);
        Assert.True(first.Steps > 0);
        Assert.Equal(ModelBuilder.Build(first, config, false), ModelBuilder.Build(second, config, false));
        Assert.NotEqual(ModelBuilder.Build(first, config, false), ModelBuilder.Build(PerceptronTrainer.Train(config, EmbeddedModel.Lexicon, corpus), config, false));
    }

    /// <summary>
    ///     Validates that the experiment's decoder configures its workspace with the groups of the model: without hints it gives the tags of the
    ///     trainer's predictor (so the clause features are computed and advanced as in the decoder), while a decoder that ignores the groups does not.
    /// </summary>
    /// <param name="mask">The groups as the bits of the feature group flags: 1 position cues, 2 clause cues, 8 inflection.</param>
    [Theory]
    [InlineData(2u)]
    [InlineData(11u)]
    public void HintedPredictor_FeatureGroups_MatchesQuantizedPredictor(uint mask)
    {
        // Arrange: a grouped model, the trainer's predictor, the experiment's decoder with the groups and one that ignores them
        var groups = (FeatureGroups)mask;
        var config = GroupConfig(groups);
        var corpus = SmallCorpus();
        var rows = ModelBuilder.Rows(PerceptronTrainer.Train(config, EmbeddedModel.Lexicon, corpus), config);
        var reference = new QuantizedPredictor(HashBits, rows, groups);
        var hinted = new HintedPredictor(HashBits, rows, config.Scale, (float)config.Temperature, EmbeddedModel.Lexicon, groups);
        var ignoring = new HintedPredictor(HashBits, rows, config.Scale, (float)config.Temperature, EmbeddedModel.Lexicon);

        // Act / Assert: the same tags as the predictor, which the other test proves equal to the runtime decoder
        var differing = 0;
        foreach (var sentence in corpus.Distinct())
        {
            var expected = reference.Predict(sentence.Tokens, EmbeddedModel.Lexicon);
            Assert.Equal(groups, hinted.Groups);
            Assert.Equal(expected, hinted.Predict(sentence.Tokens, null, 0, false, false).Tags);
            differing += ignoring.Predict(sentence.Tokens, null, 0, false, false).Tags.SequenceEqual(expected) ? 0 : 1;
        }

        // Assert: leaving the groups out would have scored a different model
        Assert.True(differing > 0);
    }

    /// <summary>
    ///     Validates that the glossary features of a grouped model fit the buffer of the experiment's decoder for every token, and that a hint
    ///     still takes effect (a hard hint is followed) with the groups on.
    /// </summary>
    [Fact]
    public void HintedPredictor_FeatureGroupsWithHints_DecodeEveryTokenAndFollowHardHints()
    {
        // Arrange
        var groups = FeatureGroups.PositionCues | FeatureGroups.ClauseCues | FeatureGroups.Inflection;
        var config = GroupConfig(groups);
        var corpus = SmallCorpus();
        var rows = ModelBuilder.Rows(PerceptronTrainer.Train(config, EmbeddedModel.Lexicon, corpus, null, new GlossaryTraining(EmbeddedModel.Lexicon, 100, 0, 3)), config);
        var hinted = new HintedPredictor(HashBits, rows, config.Scale, (float)config.Temperature, EmbeddedModel.Lexicon, groups);
        var glossary = new GlossaryTraining(EmbeddedModel.Lexicon, 100, 0, 3);

        // Act / Assert: every token with a hint is decoded with the glossary features, and a hard hint is the tag
        var hintedTokens = 0;
        foreach (var sentence in corpus.Distinct().Take(80))
        {
            var hints = glossary.Draw(sentence, new DeterministicRandom(4));
            var feature = hinted.Predict(sentence.Tokens, hints, 0, false, true);
            var hard = hinted.Predict(sentence.Tokens, hints, 0, true, false);
            for (var i = 0; i < hints.Length; i++)
            {
                if (hints[i] >= 0)
                {
                    hintedTokens++;
                    Assert.Equal(hints[i], hard.Tags[i]);
                }
            }

            Assert.Equal(sentence.Tokens.Length, feature.Tags.Length);
        }

        Assert.True(hintedTokens > 0);
    }

    /// <summary>
    ///     Validates that the experiment's decoder refuses the right preview group (it decodes in a single pass), alone or combined.
    /// </summary>
    [Theory]
    [InlineData(4u)]
    [InlineData(5u)]
    [InlineData(15u)]
    public void HintedPredictor_RightPreviewGroup_IsRefused(uint mask)
    {
        // Arrange
        var groups = (FeatureGroups)mask;

        // Act / Assert
        var error = Assert.Throws<ArgumentException>(() => HintedPredictor.EnsureSupported(groups));
        Assert.Contains("right preview", error.Message);
        Assert.Throws<ArgumentException>(() => new HintedPredictor(HashBits, new List<KeyValuePair<int, short[]>>(), 1f, 1f, EmbeddedModel.Lexicon, groups));
        HintedPredictor.EnsureSupported(FeatureGroups.None);
        HintedPredictor.EnsureSupported(FeatureGroups.PositionCues | FeatureGroups.ClauseCues | FeatureGroups.Inflection);
    }

    /// <summary>
    ///     Validates that the probe and the evaluation, which tag through the real <see cref="Tagger"/>, accept a model with every feature group:
    ///     the tagger configures the groups itself, so the hints are applied to the model as it was trained.
    /// </summary>
    [Fact]
    public void GlossaryProbeAndEvaluation_GroupedModel_UseTheConfiguredTagger()
    {
        // Arrange: a small model with all groups
        var config = GroupConfig(FeatureGroups.PositionCues | FeatureGroups.ClauseCues | FeatureGroups.RightPreview | FeatureGroups.Inflection);
        var weights = ModelBuilder.Build(TrainSmall(config), config, false);
        Assert.Equal(FeatureGroups.PositionCues | FeatureGroups.ClauseCues | FeatureGroups.RightPreview | FeatureGroups.Inflection, PerceptronModel.Load(weights).FeatureGroups);
        var open = OpenFixture(
            "the/DET old/ADJ kitchen/NOUN table/NOUN is/AUX here/ADV ./PUNCT",
            "they/PRON open/VERB the/DET kitchen/NOUN table/NOUN now/ADV ./PUNCT",
            "i/PRON use/VERB it/PRON now/ADV ./PUNCT");
        var gold = GoldFixture("d-1|doc-a|1|valve|Noun|valve|The valve leaks .");

        // Act
        var probe = GlossaryProbe.Run(weights, open, EmbeddedModel.Lexicon, 1);
        var evaluation = string.Join("\n", GlossaryEvaluation.Run(weights, open, gold, EmbeddedModel.Lexicon, 1, 20));

        // Assert: both ran and the probe's baseline tags agree with the grouped tagger (the evaluation's parity check)
        Assert.True(probe.Count > 2);
        Assert.Contains("give 0 different tags", evaluation);
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
    ///     Validates the rule for the default soft strength on a synthetic table: only the strengths that pass both wrong-entry conditions count, and the largest of them wins.
    /// </summary>
    [Fact]
    public void GlossaryEvaluation_ChooseSoftStrength_LargestPassingStrengthWins()
    {
        // Arrange: 0.5 to 2 pass; 3 fails the swap condition and 4 fails the accuracy interval, although it has the best covered accuracy
        var candidates = new List<StrengthCandidate>
        {
            new() { Strength = 0.5, AccuracyUpperFull30 = 0.08, SwapDifferenceFull30 = -0.67, CoveredAccuracy0 = 98.0 },
            new() { Strength = 1, AccuracyUpperFull30 = 0.17, SwapDifferenceFull30 = -1.78, CoveredAccuracy0 = 98.1 },
            new() { Strength = 2, AccuracyUpperFull30 = 0.15, SwapDifferenceFull30 = -2.45, CoveredAccuracy0 = 98.2 },
            new() { Strength = 3, AccuracyUpperFull30 = 0.01, SwapDifferenceFull30 = 0.45, CoveredAccuracy0 = 98.6 },
            new() { Strength = 4, AccuracyUpperFull30 = -0.30, SwapDifferenceFull30 = 4.24, CoveredAccuracy0 = 99.5 },
        };

        // Act
        var chosen = GlossaryEvaluation.ChooseSoftStrength(candidates, out var passes);

        // Assert: the largest passing strength, not the best covered accuracy
        Assert.True(passes);
        Assert.Equal(2, chosen);
        candidates[3].SwapDifferenceFull30 = 0;
        Assert.Equal(3, GlossaryEvaluation.ChooseSoftStrength(candidates, out _));
    }

    /// <summary>
    ///     Validates each condition of the rule for the default soft strength at its boundary, and that the smallest strength of the grid is chosen when none passes.
    /// </summary>
    [Fact]
    public void GlossaryEvaluation_ChooseSoftStrength_ConditionsAndNonePasses()
    {
        // Arrange / Act / Assert: the boundaries (an upper bound of exactly 0 and a swap change of exactly 0 pass)
        Assert.True(GlossaryEvaluation.PassesWrongEntryRule(new StrengthCandidate { AccuracyUpperFull30 = 0, SwapDifferenceFull30 = 0 }));
        Assert.False(GlossaryEvaluation.PassesWrongEntryRule(new StrengthCandidate { AccuracyUpperFull30 = -0.01, SwapDifferenceFull30 = 0 }));
        Assert.False(GlossaryEvaluation.PassesWrongEntryRule(new StrengthCandidate { AccuracyUpperFull30 = 0, SwapDifferenceFull30 = 0.01 }));

        // None passes: the smallest strength, whatever its figures
        var candidates = new[]
        {
            new StrengthCandidate { Strength = 2, AccuracyUpperFull30 = -1, SwapDifferenceFull30 = 1 },
            new StrengthCandidate { Strength = 0.5, AccuracyUpperFull30 = -2, SwapDifferenceFull30 = 3 },
        };
        Assert.Equal(0.5, GlossaryEvaluation.ChooseSoftStrength(candidates, out var passes));
        Assert.False(passes);
        Assert.Throws<ArgumentException>(() => GlossaryEvaluation.ChooseSoftStrength(Array.Empty<StrengthCandidate>(), out _));
    }

    /// <summary>
    ///     Validates that the rule for the default soft strength, applied to the cells recorded in the run log (scenario AB at 100% coverage with 30% wrong entries), gives the shipped default.
    /// </summary>
    [Fact]
    public void GlossaryEvaluation_ChooseSoftStrength_RecordedCells_EqualsTheDefaultSoftStrength()
    {
        // Arrange: the upper end of the paired interval of the accuracy change and the swap change, as recorded for each strength of the grid
        var upper = new[] { 0.08, 0.17, 0.17, 0.15, 0.01, -0.30 };
        var swaps = new[] { -0.67, -1.78, -2.01, -2.45, 0.45, 4.24 };
        var candidates = GlossaryEvaluation.StrengthGrid
            .Select((strength, i) => new StrengthCandidate { Strength = strength, AccuracyUpperFull30 = upper[i], SwapDifferenceFull30 = swaps[i] })
            .ToList();

        // Act
        var chosen = GlossaryEvaluation.ChooseSoftStrength(candidates, out var passes);

        // Assert: the rule picks the strength that the library ships, and that strength is a member of the grid
        Assert.True(passes);
        Assert.Equal(GlossaryOptions.DefaultSoftStrength, chosen);
        Assert.Contains(chosen, GlossaryEvaluation.StrengthGrid);
    }

    /// <summary>
    ///     Validates the original pre-registered strength rule of the first run (kept as the historical decision) on a synthetic table: inadmissible strengths are out, strengths within 0.1 points of the best are tied, and the smallest tied strength wins.
    /// </summary>
    [Fact]
    public void GlossaryEvaluation_ChooseFirstRunSoftStrength_PicksSmallestWithinTolerance()
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
        var chosen = GlossaryEvaluation.ChooseFirstRunSoftStrength(candidates, out var admissible);

        // Assert
        Assert.True(admissible);
        Assert.Equal(1, chosen);
        candidates[3].CoveredAccuracy0 = 98.7;
        Assert.Equal(3, GlossaryEvaluation.ChooseFirstRunSoftStrength(candidates, out _));
    }

    /// <summary>
    ///     Validates each admissibility condition of the first-run strength rule, and that without an admissible strength the smallest of the grid is chosen.
    /// </summary>
    [Fact]
    public void GlossaryEvaluation_ChooseFirstRunSoftStrength_ConditionsAndNoneAdmissible()
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
        Assert.Equal(0.5, GlossaryEvaluation.ChooseFirstRunSoftStrength(candidates, out var admissible));
        Assert.False(admissible);
        Assert.Throws<ArgumentException>(() => GlossaryEvaluation.ChooseFirstRunSoftStrength(Array.Empty<StrengthCandidate>(), out _));
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
        Assert.Contains("**Historical decision (soft strength, rule of the first run)**", text);
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

    /// <summary>
    ///     Creates the sentences of a synthetic corpus: <paramref name="documents"/> documents of <paramref name="perDocument"/> sentences each (every
    ///     sentence ends in a token of its own, so that no two sentences share a sentence key).
    /// </summary>
    private static List<TrainingSentence> Documents(int documents, int perDocument)
    {
        var sentences = new List<TrainingSentence>();
        for (var d = 0; d < documents; d++)
        {
            for (var s = 0; s < perDocument; s++)
            {
                sentences.Add(new TrainingSentence($"doc{d}-s{s}", $"doc{d}", new[] { "The", "dog", "runs", $"mark{d}x{s}" }, new sbyte[] { (sbyte)Upos.Det, (sbyte)Upos.Noun, (sbyte)Upos.Verb, (sbyte)Upos.Noun }));
            }
        }

        return sentences;
    }

    /// <summary>
    ///     Creates one training sentence of a document whose tags are all nouns.
    /// </summary>
    private static TrainingSentence NounSentence(string sentenceId, string documentId, params string[] tokens) =>
        new(sentenceId, documentId, tokens, tokens.Select(_ => (sbyte)Upos.Noun).ToArray());

    /// <summary>
    ///     Creates the configuration of a small run with feature groups (three epochs, three jackknife folds).
    /// </summary>
    private static TrainingConfig GroupConfig(FeatureGroups groups, long seed = 7)
    {
        var config = SmallConfig(RestrictionMode.History, seed);
        config.Features = TrainingConfig.NamesOf(groups);
        config.PreviewFolds = 3;
        config.Validate();
        return config;
    }

    /// <summary>
    ///     Validates the value of the <c>--features</c> option: <c>none</c>, short names and full names in any order and case, resolved to the canonical order.
    /// </summary>
    [Theory]
    [InlineData("none", "")]
    [InlineData("NONE", "")]
    [InlineData("F1", "positionCues")]
    [InlineData("F3,F1", "positionCues,rightPreview")]
    [InlineData("inflection, clauseCues", "clauseCues,inflection")]
    [InlineData("positionCues,clauseCues,rightPreview,inflection", "positionCues,clauseCues,rightPreview,inflection")]
    [InlineData("INFLECTION,f1,rightPreview", "positionCues,rightPreview,inflection")]
    public void TrainingConfig_ParseFeatureList_ValidValue_CanonicalOrder(string text, string expected)
    {
        // Arrange / Act
        var names = TrainingConfig.ParseFeatureList(text);

        // Assert
        Assert.Equal(expected, string.Join(",", names));
    }

    /// <summary>
    ///     Validates that an empty list, an unknown group (the skipped clusters group included), a group named twice by either spelling and
    ///     <c>none</c> next to a group are usage errors.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(",")]
    [InlineData("F5")]
    [InlineData("wordClusters")]
    [InlineData("F1,F1")]
    [InlineData("F1,positionCues")]
    [InlineData("none,F1")]
    [InlineData("F1,none")]
    public void TrainingConfig_ParseFeatureList_InvalidValue_Throws(string text)
    {
        // Arrange / Act / Assert
        Assert.Throws<ArgumentException>(() => TrainingConfig.ParseFeatureList(text));
    }

    /// <summary>
    ///     Validates the defaults (no group, five folds), the resolution of the names to the flags of the extractor, the validation of the
    ///     names and of the fold count, and that a clone does not share the list of groups.
    /// </summary>
    [Fact]
    public void TrainingConfig_Features_DefaultsResolveValidateAndClone()
    {
        // Arrange
        var config = SmallConfig();

        // Act / Assert: the defaults are the shipped feature set
        Assert.Empty(config.Features);
        Assert.Equal(5, config.PreviewFolds);
        Assert.Equal(FeatureGroups.None, config.ResolveGroups());

        // Assert: names resolve to the flags in the order of the group bits
        config.Features = new List<string> { "inflection", "F1", "rightPreview", "clauseCues" };
        Assert.Equal(FeatureGroups.PositionCues | FeatureGroups.ClauseCues | FeatureGroups.RightPreview | FeatureGroups.Inflection, config.ResolveGroups());
        Assert.Equal(new[] { "positionCues", "clauseCues", "rightPreview", "inflection" }, TrainingConfig.NamesOf(config.ResolveGroups()));

        // Assert: a clone has its own list
        var clone = config.Clone();
        clone.Features.Clear();
        Assert.Equal(4, config.Features.Count);

        // Assert: unknown and repeated names, and the fold count, are refused by Validate
        foreach (var bad in new[] { new List<string> { "bogus" }, new List<string> { "F2", "clauseCues" }, new List<string> { "wordClusters" } })
        {
            config.Features = bad;
            Assert.Throws<InvalidDataException>(() => config.Validate());
            Assert.Throws<InvalidDataException>(() => config.ResolveGroups());
        }

        config.Features = new List<string>();
        foreach (var folds in new[] { 1, 11, 0, -3 })
        {
            config.PreviewFolds = folds;
            Assert.Throws<InvalidDataException>(() => config.Validate());
        }

        foreach (var folds in new[] { 2, 5, 10 })
        {
            config.PreviewFolds = folds;
            config.Validate();
        }
    }

    /// <summary>
    ///     Validates that the encoded weights file carries the groups of the configuration: no group is the shipped format (feature set 1, mask 0),
    ///     any group is feature set 2 with the mask in the header, and the loader reads the groups back.
    /// </summary>
    [Fact]
    public void ModelBuilder_Encode_FeatureGroups_WritesMaskAndFeatureSetVersion()
    {
        // Arrange
        var rows = new List<KeyValuePair<int, short[]>>();

        // Act / Assert: no group
        var none = ModelBuilder.Encode(rows, SmallConfig(), false);
        Assert.Equal(1, BitConverter.ToUInt16(none, 8));
        Assert.Equal(0u, BitConverter.ToUInt32(none, 40));
        Assert.Equal(FeatureGroups.None, PerceptronModel.Load(none).FeatureGroups);

        // Act / Assert: groups
        foreach (var groups in new[] { FeatureGroups.PositionCues, FeatureGroups.RightPreview, FeatureGroups.PositionCues | FeatureGroups.ClauseCues | FeatureGroups.RightPreview | FeatureGroups.Inflection })
        {
            var file = ModelBuilder.Encode(rows, GroupConfig(groups), false);
            Assert.Equal(2, BitConverter.ToUInt16(file, 8));
            Assert.Equal((uint)groups, BitConverter.ToUInt32(file, 40));
            Assert.Equal(groups, PerceptronModel.Load(file).FeatureGroups);
            Assert.Throws<InvalidDataException>(() => PerceptronModel.Load(file, FeatureExtractor.FeatureSetVersion));
        }
    }

    /// <summary>
    ///     Validates for every group and for the combinations of the experiment that the trainer's predictor gives the same tags as the runtime decoder
    ///     for a model that the trainer made (including the two decoding passes of the right preview group).
    /// </summary>
    /// <param name="mask">The groups as the bits of the feature group flags: 1 position cues, 2 clause cues, 4 right preview, 8 inflection.</param>
    [Theory]
    [InlineData(1u)]
    [InlineData(2u)]
    [InlineData(4u)]
    [InlineData(8u)]
    [InlineData(15u)]
    [InlineData(11u)]
    public void ModelTrainer_Predict_FeatureGroups_MatchesTagDecoder(uint mask)
    {
        // Arrange: train the small run with the groups and load the same weights into the runtime decoder
        var groups = (FeatureGroups)mask;
        var config = GroupConfig(groups);
        var corpus = SmallCorpus();
        var rows = ModelBuilder.Rows(PerceptronTrainer.Train(config, EmbeddedModel.Lexicon, corpus), config);
        var predictor = new QuantizedPredictor(HashBits, rows, groups);
        var model = PerceptronModel.Load(ModelBuilder.Encode(rows, config, false));
        var decoder = new TagDecoder(model, EmbeddedModel.Lexicon);

        // Act / Assert: the model has the groups, and every token of every sentence gets the same tag
        Assert.Equal(groups, model.FeatureGroups);
        var compared = 0;
        var correct = 0;
        foreach (var sentence in corpus.Distinct())
        {
            var expected = predictor.Predict(sentence.Tokens, EmbeddedModel.Lexicon);
            var actual = decoder.Decode(Tokenizer.CreateSentence(sentence.Tokens).TokenArray, null, 0);
            Assert.Equal(expected, actual.Select(t => (int)t.Tag));
            compared += expected.Length;
            correct += expected.Where((tag, i) => tag == sentence.Gold[i]).Count();
        }

        Assert.True(compared > 2000);

        // Assert: what the trainer learned is what the decoder executes (the training sentences are tagged mostly right)
        Assert.True(correct > compared * 0.8, $"only {correct} of {compared} training tokens are right");
    }

    /// <summary>
    ///     Validates that the two decoding passes of the right preview group are part of the predictor: the second pass reads the first-pass tags,
    ///     so a model with a strong weight on the preview of the next token changes the tag of the token before it, as the runtime decoder does.
    /// </summary>
    [Fact]
    public void QuantizedPredictor_RightPreview_SecondPassReadsFirstPassTags()
    {
        // Arrange: "dog" is a verb on its own; the preview feature "next tag is NOUN" makes the word before it a DET
        var lexicon = DemaConsulting.PartOfSpeech.Lexicon.Load(TinyModels.CreateLexicon("the DET NOUN", "dog NOUN VERB"));
        var rows = new Dictionary<int, short[]>();
        void Add(int bucket, Upos tag, short weight)
        {
            if (!rows.TryGetValue(bucket, out var row))
            {
                row = new short[UposExtensions.Count];
                rows[bucket] = row;
            }

            row[(int)tag] += weight;
        }

        Add(FeatureExtractor.ReferenceBucket(FeatureKind.Lower, "dog", null, HashBits), Upos.Verb, 100);
        Add(FeatureExtractor.ReferenceBucket(FeatureKind.Lower, "the", null, HashBits), Upos.Noun, 30);
        Add(FeatureExtractor.ReferenceBucketOfCodes(FeatureKind.Rt1, (uint)Upos.Verb, 0, HashBits), Upos.Det, 200);
        var pairs = rows.OrderBy(r => r.Key).Select(r => new KeyValuePair<int, short[]>(r.Key, r.Value)).ToList();

        // Act: the same weights without and with the group
        var plain = new QuantizedPredictor(HashBits, pairs).Predict(new[] { "the", "dog" }, lexicon);
        var previewed = new QuantizedPredictor(HashBits, pairs, FeatureGroups.RightPreview).Predict(new[] { "the", "dog" }, lexicon);

        // Assert: without the group "the" stays a noun (its own weight); with it the first-pass tag of "dog" (a verb) turns "the" into a determiner
        Assert.Equal(new[] { (int)Upos.Noun, (int)Upos.Verb }, plain);
        Assert.Equal(new[] { (int)Upos.Det, (int)Upos.Verb }, previewed);
    }

    /// <summary>
    ///     Validates the folds of a corpus without duplicate sentences: they depend on the document id only (a document is never split, the order of
    ///     the sentences and the seed do not matter), every fold is used, and the pinned values of the hash do not move.
    /// </summary>
    [Fact]
    public void Jackknife_Partition_ByDocumentHash_NeverSplitsADocumentAndIsStable()
    {
        // Arrange: 60 documents of three sentences
        var sentences = Documents(60, 3);

        // Act
        var folds = Jackknife.Partition(sentences, 5);
        var reversed = Jackknife.Partition(Enumerable.Reverse(sentences).ToList(), 5);

        // Assert: one fold per document, the same in any order, all folds used, a fold is a hash of the document id
        Assert.Equal(sentences.Count, folds.Count);
        foreach (var group in sentences.GroupBy(s => s.SourceId))
        {
            Assert.Single(group.Select(s => folds[s]).Distinct());
            Assert.Equal(Jackknife.FoldOf(group.Key, 5), folds[group.First()]);
        }

        Assert.All(sentences, s => Assert.Equal(folds[s], reversed[s]));
        Assert.Equal(Enumerable.Range(0, 5), folds.Values.Distinct().OrderBy(f => f));

        // Assert: a sentence without a document id falls back to its sentence id; the fold count is at least two
        Assert.Equal(Jackknife.FoldOf("lonely", 5), Jackknife.Partition(new[] { new TrainingSentence("lonely", string.Empty, new[] { "a" }, new sbyte[] { 0 }) }, 5).Values.Single());
        Assert.Throws<ArgumentOutOfRangeException>(() => Jackknife.FoldOf("doc", 1));

        // Assert: pinned folds (FNV-1a of the UTF-8 bytes modulo the fold count)
        Assert.Equal(new[] { 3, 2, 0 }, new[] { Jackknife.FoldOf("doc-a", 5), Jackknife.FoldOf("doc-b", 5), Jackknife.FoldOf("tatoeba-10001852", 5) });
    }

    /// <summary>
    ///     Validates that an exact duplicate sentence under two document ids lands in one fold: the documents are one group (the sentence key is the
    ///     lower-case letters and numbers, so case, spacing and punctuation do not matter), the fold is the hash of the smallest document id, and the
    ///     same two documents without the duplicate keep the folds of their own ids.
    /// </summary>
    [Fact]
    public void Jackknife_Partition_DuplicateSentenceInTwoDocuments_LandsInOneFold()
    {
        // Arrange: "doc-a" and "doc-b" have different folds of their own (3 and 2); they share one sentence up to case and punctuation
        var shared = NounSentence("a1", "doc-a", "The", "dog", "runs", ".");
        var variant = NounSentence("b1", "doc-b", "the", "DOG", "runs");
        var alone = NounSentence("b2", "doc-b", "A", "cat", "sleeps");
        var other = NounSentence("a2", "doc-a", "Birds", "sing");

        // Act
        var joined = Jackknife.Partition(new[] { shared, other, variant, alone }, 5);
        var separate = Jackknife.Partition(new[] { shared, other, NounSentence("b1", "doc-b", "A", "bird", "flies"), alone }, 5);

        // Assert: one fold for both documents, the fold of the smallest id; no duplicate, no change
        Assert.NotEqual(Jackknife.FoldOf("doc-a", 5), Jackknife.FoldOf("doc-b", 5));
        Assert.All(new[] { shared, other, variant, alone }, s => Assert.Equal(Jackknife.FoldOf("doc-a", 5), joined[s]));
        Assert.Equal(Jackknife.FoldOf("doc-a", 5), separate[shared]);
        Assert.Equal(Jackknife.FoldOf("doc-b", 5), separate[alone]);
    }

    /// <summary>
    ///     Validates that the groups are transitive: documents A and B share a sentence, B and C share another one, and all three land in one fold
    ///     although their own folds differ; a sentence without a letter or digit joins nothing.
    /// </summary>
    [Fact]
    public void Jackknife_Partition_TransitiveChain_LandsInOneFold()
    {
        // Arrange: three documents with three different folds of their own (3, 2 and 0)
        var ids = new[] { "doc-a", "doc-b", "tatoeba-10001852" };
        var sentences = new List<TrainingSentence>
        {
            NounSentence("a1", ids[0], "Red", "apples"),
            NounSentence("b1", ids[1], "red", "apples", "!"),
            NounSentence("b2", ids[1], "Blue", "pears"),
            NounSentence("c1", ids[2], "BLUE", "pears"),
            NounSentence("c2", ids[2], "Green", "plums"),

            // Two sentences of two other documents without any letter or digit do not match each other
            NounSentence("x1", "doc-x", "...", "!!!"),
            NounSentence("y1", "doc-y", "?", "?"),
        };

        // Act
        var folds = Jackknife.Partition(sentences, 5);
        var groups = Jackknife.GroupDocuments(sentences);

        // Assert
        Assert.Equal(3, ids.Select(i => Jackknife.FoldOf(i, 5)).Distinct().Count());
        Assert.All(sentences.Take(5), s => Assert.Equal(Jackknife.FoldOf("doc-a", 5), folds[s]));
        Assert.All(ids, i => Assert.Equal("doc-a", groups[i]));
        Assert.Equal("doc-x", groups["doc-x"]);
        Assert.Equal("doc-y", groups["doc-y"]);
        Assert.Equal(string.Empty, Jackknife.SentenceKey(sentences[5]));
    }

    /// <summary>
    ///     Validates that the folds with duplicate sentences are deterministic: they do not depend on the order of the sentences, and the group
    ///     of a document is its smallest document id whatever the order; the pinned fold values do not move.
    /// </summary>
    [Fact]
    public void Jackknife_Partition_WithDuplicates_IsDeterministicAndIndependentOfOrderAndSeed()
    {
        // Arrange: 40 documents, every fourth one has a duplicate of its first sentence in the next document
        var sentences = Documents(40, 2);
        for (var d = 0; d < 40; d += 4)
        {
            sentences[(2 * d) + 3] = NounSentence($"copy{d}", $"doc{d + 1}", "THE", "dog", "runs", $"mark{d}x0");
        }

        var shuffled = sentences.OrderBy(s => Jackknife.FoldOf(s.SentenceId + "shuffle", 97)).ThenBy(s => s.SentenceId, StringComparer.Ordinal).ToList();

        // Act
        var folds = Jackknife.Partition(sentences, 5);
        var reordered = Jackknife.Partition(shuffled, 5);
        var reversed = Jackknife.Partition(Enumerable.Reverse(sentences).ToList(), 5);
        var groups = Jackknife.GroupDocuments(sentences);
        var reversedGroups = Jackknife.GroupDocuments(Enumerable.Reverse(sentences).ToList());

        // Assert: the same folds and groups in any order; the group of a joined pair is its smaller id, the others are their own groups
        Assert.All(sentences, s => Assert.Equal(folds[s], reordered[s]));
        Assert.All(sentences, s => Assert.Equal(folds[s], reversed[s]));
        Assert.Equal(groups.OrderBy(g => g.Key, StringComparer.Ordinal), reversedGroups.OrderBy(g => g.Key, StringComparer.Ordinal));
        for (var d = 0; d < 40; d += 4)
        {
            var smaller = string.CompareOrdinal($"doc{d}", $"doc{d + 1}") < 0 ? $"doc{d}" : $"doc{d + 1}";
            Assert.Equal(smaller, groups[$"doc{d}"]);
            Assert.Equal(smaller, groups[$"doc{d + 1}"]);
            Assert.Equal(Jackknife.FoldOf(smaller, 5), folds[sentences[2 * d]]);
            Assert.Equal(Jackknife.FoldOf(smaller, 5), folds[sentences[(2 * d) + 3]]);
        }

        Assert.Equal("doc2", groups["doc2"]);
        Assert.Equal(30, groups.Values.Distinct().Count());
    }

    /// <summary>
    ///     Validates the leakage boundary on a toy corpus with duplicates across documents: no sentence key of a held-out fold occurs in the training
    ///     set of its fold model, the tags equal a replay of that fold model, and the folds of the document ids alone would have leaked.
    /// </summary>
    [Fact]
    public void Jackknife_Compute_DuplicatesAcrossDocuments_FoldModelNeverSeesAHeldOutSentenceKey()
    {
        // Arrange: 30 documents of three sentences, and ten more documents that repeat the first sentence of a document up to case and punctuation
        var corpus = Documents(30, 3);
        for (var d = 0; d < 30; d += 3)
        {
            corpus.Add(NounSentence($"dup{d}", $"extra{d}", "the", "DOG", "runs", ".", $"mark{d}x0"));
        }

        var groups = GroupConfig(FeatureGroups.ClauseCues | FeatureGroups.RightPreview);

        // Act
        var foldOf = Jackknife.Partition(corpus, 3);
        var tags = Jackknife.Compute(groups, EmbeddedModel.Lexicon, corpus);

        // Assert: the document ids alone would have put a duplicate into another fold than its original
        Assert.Contains(corpus.Skip(90), s => Jackknife.FoldOf(Jackknife.DocumentOf(s), 3) != Jackknife.FoldOf($"doc{s.SentenceId.Substring(3)}", 3));

        // Assert: no key of a held-out sentence in the training set of its fold model
        var foldConfig = GroupConfig(FeatureGroups.ClauseCues);
        for (var fold = 0; fold < 3; fold++)
        {
            var training = corpus.Where(s => foldOf[s] != fold).ToList();
            var held = corpus.Where(s => foldOf[s] == fold).ToList();
            var trainingKeys = new HashSet<string>(training.Select(Jackknife.SentenceKey), StringComparer.Ordinal);
            Assert.NotEmpty(held);
            Assert.DoesNotContain(held, s => trainingKeys.Contains(Jackknife.SentenceKey(s)));

            // Assert: the preview tags of the fold are the tags of that fold model
            var rows = ModelBuilder.Rows(PerceptronTrainer.Train(foldConfig, EmbeddedModel.Lexicon, training), foldConfig);
            var predictor = new QuantizedPredictor(HashBits, rows, FeatureGroups.ClauseCues);
            Assert.All(held, s => Assert.Equal(predictor.Predict(s.Tokens, EmbeddedModel.Lexicon), tags[s]));
        }
    }

    /// <summary>
    ///     Validates that without duplicate sentences every document is its own group, so that the fold of a document is the hash of its own id, and
    ///     that the sentence key of two spellings of one text is the same.
    /// </summary>
    [Fact]
    public void Jackknife_GroupDocuments_NoDuplicates_EveryDocumentIsItsOwnGroup()
    {
        // Arrange
        var sentences = Documents(25, 3);
        var texts = new[] { "The dog's  RUN, 42 times!", "Caf\u00E9 au lait", "Cafe\u0301 au lait", "(...)" };

        // Act
        var groups = Jackknife.GroupDocuments(sentences);
        var folds = Jackknife.Partition(sentences, 5);

        // Assert: every document alone, the fold of its own id
        Assert.Equal(25, groups.Count);
        Assert.All(groups, g => Assert.Equal(g.Key, g.Value));
        Assert.All(sentences, s => Assert.Equal(Jackknife.FoldOf(s.SourceId, 5), folds[s]));

        // Assert: the precomposed and the decomposed spelling have one key, and a text without letters has the empty key
        Assert.Equal("thedogsrun42times", Jackknife.SentenceKey(texts[0]));
        Assert.Equal(Jackknife.SentenceKey(NounSentence("k1", "d", texts[1])), Jackknife.SentenceKey(NounSentence("k2", "d", texts[2])));
        Assert.Equal(string.Empty, Jackknife.SentenceKey(texts[3]));
    }

    /// <summary>
    ///     Validates the sentence key against the keys that the Python function of the leakage check wrote (the shared parity cases
    ///     <c>tools/data/tests/sentence-key-cases.tsv</c>, also read by the Python tests): supplementary-plane letters and numbers, combining
    ///     marks, the capital sigma and other case folding must give exactly the keys of the authoritative normalizer, because a
    ///     sentence key that differs from it could leave a duplicate sentence in two folds.
    /// </summary>
    [Fact]
    public void Jackknife_SentenceKey_SharedParityCases_AgreeWithPython()
    {
        // Arrange: the shared cases (id, input and key as hexadecimal code points, the newer Unicode version that the case needs, description);
        // the case tables of .NET Framework 4.8 are those of Unicode 8, so on it the cases of newer scripts are left out
        var path = Path.Combine(DataFiles.RepositoryRoot, "tools", "data", "tests", "sentence-key-cases.tsv");
        var cases = File.ReadLines(path).Where(line => line.Length > 0 && !line.StartsWith("# ", StringComparison.Ordinal)).Select(line => line.Split('\t')).ToList();
        Assert.True(cases.Count >= 40);
        Assert.Contains(cases, c => c[0] == "deseret-capital-astral" && c[2].Length > 0);
#if NETFRAMEWORK
        var checkedCases = cases.Where(c => c[3].Length == 0).ToList();
#else
        var checkedCases = cases;
#endif

        // Act: the key of every input
        var mismatches = checkedCases
            .Select(c => (Id: c[0], Expected: c[2], Actual: HexOf(Jackknife.SentenceKey(TextOf(c[1])))))
            .Where(c => c.Expected != c.Actual)
            .Select(c => $"{c.Id}: expected [{c.Expected}] but was [{c.Actual}]")
            .ToList();

        // Assert: every key is the one that Python wrote
        Assert.Empty(mismatches);
    }

    /// <summary>
    ///     Builds a text from hexadecimal code points separated by spaces.
    /// </summary>
    private static string TextOf(string hexadecimal) =>
        string.Concat(hexadecimal.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(h => char.ConvertFromUtf32(int.Parse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture))));

    /// <summary>
    ///     Writes the code points of a text as hexadecimal numbers separated by spaces.
    /// </summary>
    private static string HexOf(string text)
    {
        var codePoints = new List<string>();
        var index = 0;
        while (index < text.Length)
        {
            var codePoint = char.ConvertToUtf32(text, index);
            codePoints.Add(codePoint.ToString("X", CultureInfo.InvariantCulture));
            index += char.IsSurrogatePair(text, index) ? 2 : 1;
        }

        return string.Join(" ", codePoints);
    }

    /// <summary>
    ///     Validates the jackknife tags against an independent replay: every held-out sentence is tagged by the quantized weights of a model that was
    ///     trained on the other folds without the right preview group (with the other groups of the run, the seed and the settings of the run), the
    ///     folds do not depend on the seed, and a run is reproducible.
    /// </summary>
    [Fact]
    public void Jackknife_Compute_TagsEachFoldWithAModelThatDidNotSeeIt()
    {
        // Arrange: the clause group (kept in the fold models) with the right preview group
        var groups = FeatureGroups.ClauseCues | FeatureGroups.RightPreview;
        var config = GroupConfig(groups);
        var corpus = SmallCorpus();
        var messages = new List<string>();

        // Act
        var tags = Jackknife.Compute(config, EmbeddedModel.Lexicon, corpus, messages.Add);
        var again = Jackknife.Compute(config, EmbeddedModel.Lexicon, corpus);
        var otherSeed = Jackknife.Compute(GroupConfig(groups, seed: 8), EmbeddedModel.Lexicon, corpus, null);

        // Assert: every distinct sentence has one tag per token, one message per fold, and a run is reproducible
        var distinct = corpus.Distinct().ToList();
        Assert.Equal(distinct.Count, tags.Count);
        Assert.All(distinct, s => Assert.Equal(s.Tokens.Length, tags[s].Length));
        Assert.Equal(3, messages.Count);
        Assert.All(distinct, s => Assert.Equal(tags[s], again[s]));

        // Assert: the seed changes the models but not the folds (the sentences are the same, their tags may differ)
        Assert.Equal(tags.Keys.OrderBy(s => s.SentenceId), otherSeed.Keys.OrderBy(s => s.SentenceId));
        Assert.Contains(distinct, s => !tags[s].SequenceEqual(otherSeed[s]));

        // Act / Assert: replay of every fold by hand
        var foldOf = Jackknife.Partition(corpus, 3);
        var foldConfig = GroupConfig(FeatureGroups.ClauseCues);
        for (var fold = 0; fold < 3; fold++)
        {
            var training = corpus.Where(s => foldOf[s] != fold).ToList();
            var foldRows = ModelBuilder.Rows(PerceptronTrainer.Train(foldConfig, EmbeddedModel.Lexicon, training), foldConfig);
            var predictor = new QuantizedPredictor(HashBits, foldRows, FeatureGroups.ClauseCues);
            foreach (var sentence in distinct.Where(s => foldOf[s] == fold))
            {
                Assert.Equal(predictor.Predict(sentence.Tokens, EmbeddedModel.Lexicon), tags[sentence]);
            }
        }
    }

    /// <summary>
    ///     Validates that sentences of fewer than two folds cannot be jackknifed.
    /// </summary>
    [Fact]
    public void Jackknife_Compute_OneDocument_Throws()
    {
        // Arrange: one document is one fold
        var config = GroupConfig(FeatureGroups.RightPreview);

        // Act / Assert
        Assert.Throws<InvalidDataException>(() => Jackknife.Compute(config, EmbeddedModel.Lexicon, Documents(1, 4)));
    }

    /// <summary>
    ///     Validates the mixed training of the right preview group: every sentence is trained twice on one table (twice the steps of the arm without
    ///     the group), the result does not depend on whether the preview tags are passed or computed, other tags give another model, and a missing
    ///     sentence is refused.
    /// </summary>
    [Fact]
    public void PerceptronTrainer_Train_RightPreview_TrainsBothPassesOnOneTable()
    {
        // Arrange
        var corpus = SmallCorpus();
        var plain = GroupConfig(FeatureGroups.None);
        var preview = GroupConfig(FeatureGroups.RightPreview);
        var previews = Jackknife.Compute(preview, EmbeddedModel.Lexicon, corpus);

        // Act
        var baseline = PerceptronTrainer.Train(plain, EmbeddedModel.Lexicon, corpus);
        var computed = PerceptronTrainer.Train(preview, EmbeddedModel.Lexicon, corpus);
        var given = PerceptronTrainer.Train(preview, EmbeddedModel.Lexicon, corpus, previews: previews);
        var gold = corpus.Distinct().ToDictionary(s => s, s => s.Gold.Select(g => (int)g).ToArray());
        var other = PerceptronTrainer.Train(preview, EmbeddedModel.Lexicon, corpus, previews: gold);

        // Assert: two passes per sentence, the same model from the same tags, another model from other tags
        Assert.Equal(2 * baseline.Steps, computed.Steps);
        Assert.Equal(ModelBuilder.Build(computed, preview, false), ModelBuilder.Build(given, preview, false));
        Assert.NotEqual(ModelBuilder.Build(computed, preview, false), ModelBuilder.Build(other, preview, false));
        var incomplete = previews.Where(p => p.Key != corpus[0]).ToDictionary(p => p.Key, p => p.Value);
        Assert.Throws<ArgumentException>(() => PerceptronTrainer.Train(preview, EmbeddedModel.Lexicon, corpus, previews: incomplete));
    }

    /// <summary>
    ///     Validates that without a group the training is the shipped training: the explicit empty list, the default and a list of the preview
    ///     tags that is not used give the same bytes as before the groups existed (the golden hash of the small run).
    /// </summary>
    [Fact]
    public void PerceptronTrainer_Train_NoGroups_IsTheShippedTraining()
    {
        // Arrange
        var config = SmallConfig(RestrictionMode.None);
        config.Features = new List<string>();
        config.PreviewFolds = 9;

        // Act
        var image = ModelBuilder.Sha256Hex(ModelBuilder.Build(PerceptronTrainer.Train(config, EmbeddedModel.Lexicon, SmallCorpus(), previews: new Dictionary<TrainingSentence, int[]>()), config, false));

        // Assert
        Assert.Equal(GoldenUnrestricted, image);
    }

#if NET8_0_OR_GREATER
    /// <summary>
    ///     Validates that the file form leaves the feature settings out while they are at their defaults (so the committed configuration keeps its
    ///     form), writes them when a group is selected, reads them back and refuses an unknown group.
    /// </summary>
    [Fact]
    public void ConfigFile_Features_OmittedByDefaultAndRoundTripped()
    {
        // Arrange
        var config = SmallConfig();

        // Act / Assert: the defaults are not written
        var plain = ConfigFile.Format(config);
        Assert.DoesNotContain("features", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("previewFolds", plain, StringComparison.Ordinal);

        // Act / Assert: a group is written, with the fold count when the preview group is on, and read back
        config.Features = new List<string> { "positionCues", "rightPreview" };
        var text = ConfigFile.Format(config);
        Assert.Contains("\"features\": [", text, StringComparison.Ordinal);
        Assert.Contains("\"rightPreview\"", text, StringComparison.Ordinal);
        Assert.Contains("\"previewFolds\": 5", text, StringComparison.Ordinal);
        var parsed = ConfigFile.Parse(text);
        Assert.Equal(new[] { "positionCues", "rightPreview" }, parsed.Features);
        Assert.Equal(text, ConfigFile.Format(parsed));
        config.Features = new List<string> { "inflection" };
        Assert.DoesNotContain("previewFolds", ConfigFile.Format(config), StringComparison.Ordinal);

        // Assert: a short or unknown name that the file form cannot carry is refused where the engine would meet it
        Assert.Throws<InvalidDataException>(() => ConfigFile.Parse(text.Replace("positionCues", "bogus")));
        Assert.Throws<InvalidDataException>(() => ConfigFile.Parse(text.Replace("\"previewFolds\": 5", "\"previewFolds\": 1")));
    }

    /// <summary>
    ///     Validates that the committed configuration is valid, names the six corpora (the imperative source with weight 1, so
    ///     it is in the mix of the shipped model; the compound and headings sources with weight 0, so they are not) and is in the
    ///     canonical form that the tool writes.
    /// </summary>
    [Fact]
    public void ConfigFile_Parse_CommittedConfiguration_ValidAndCanonical()
    {
        // Arrange
        var text = File.ReadAllText(DataFiles.Repository("tools/DemaConsulting.PartOfSpeech.Trainer/training.json"));

        // Act
        var config = ConfigFile.Parse(text);

        // Assert: valid, six known sources that exist, and rewriting gives the same text (so --update-config makes no noise)
        Assert.Equal(new[] { "open", "masc", "silver", "imperative", "compound", "headings" }, config.Sources.Select(s => s.Name));
        Assert.All(config.Sources, s => Assert.True(File.Exists(DataFiles.Repository(s.Path))));
        Assert.Equal(1, config.Sources.Single(s => s.Name == "imperative").Weight);
        Assert.Equal(new[] { 2, 1, 1, 1, 0, 0 }, config.Sources.Select(s => s.Weight));
        Assert.Equal(text.Replace("\r\n", "\n"), ConfigFile.Format(config));
    }

    /// <summary>
    ///     Validates that the committed imperative source is read by the corpus reader as 900 sentences with no masked token
    ///     when its weight is raised to one (the way a training run adds it).
    /// </summary>
    [Fact]
    public void CorpusReader_LoadSources_ImperativeSource_ReadsUnmaskedWhenWeighted()
    {
        // Arrange: the committed configuration with only the imperative source weighted
        var weighted = ConfigFile.Parse(File.ReadAllText(DataFiles.Repository("tools/DemaConsulting.PartOfSpeech.Trainer/training.json")));
        foreach (var source in weighted.Sources)
        {
            source.Weight = source.Name == "imperative" ? 1 : 0;
        }

        // Act
        var sentences = CorpusReader.LoadSources(DataFiles.RepositoryRoot, weighted);

        // Assert: 900 sentences from the imperative file, every token with a gold tag, the first token a verb
        Assert.Equal(900, sentences.Count);
        Assert.All(sentences, s => Assert.DoesNotContain((sbyte)-1, s.Gold));
        Assert.All(sentences, s => Assert.Equal((sbyte)Upos.Verb, s.Gold[0]));
    }

    /// <summary>
    ///     Validates that the committed compound and headings sources contribute nothing at the committed weight 0, and that the
    ///     corpus reader reads each of them with no masked token when its weight is raised to one (the way a training run adds it).
    /// </summary>
    [Theory]
    [InlineData("compound", 742)]
    [InlineData("headings", 375)]
    public void CorpusReader_LoadSources_TargetedSource_ReadsUnmaskedOnlyWhenWeighted(string name, int expected)
    {
        // Arrange: the committed configuration, as it is, with this source added, and with only this source weighted
        var committed = ConfigFile.Parse(File.ReadAllText(DataFiles.Repository("tools/DemaConsulting.PartOfSpeech.Trainer/training.json")));
        var added = ConfigFile.Parse(File.ReadAllText(DataFiles.Repository("tools/DemaConsulting.PartOfSpeech.Trainer/training.json")));
        added.Sources.Single(s => s.Name == name).Weight = 1;
        var weighted = ConfigFile.Parse(File.ReadAllText(DataFiles.Repository("tools/DemaConsulting.PartOfSpeech.Trainer/training.json")));
        foreach (var source in weighted.Sources)
        {
            source.Weight = source.Name == name ? 1 : 0;
        }

        // Act
        var sentences = CorpusReader.LoadSources(DataFiles.RepositoryRoot, weighted);
        var committedCount = CorpusReader.LoadSources(DataFiles.RepositoryRoot, committed).Count;
        var addedCount = CorpusReader.LoadSources(DataFiles.RepositoryRoot, added).Count;

        // Assert: the whole file with every token gold-tagged, nothing of it in the committed mix, and exactly its sentences more with weight 1
        Assert.Equal(expected, sentences.Count);
        Assert.All(sentences, s => Assert.DoesNotContain((sbyte)-1, s.Gold));
        Assert.Equal(0, committed.Sources.Single(s => s.Name == name).Weight);
        Assert.Equal(committedCount + expected, addedCount);
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
