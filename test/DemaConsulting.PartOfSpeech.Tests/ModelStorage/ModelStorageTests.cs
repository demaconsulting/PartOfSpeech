namespace DemaConsulting.PartOfSpeech.Tests.ModelStorage;

/// <summary>
///     Subsystem tests of ModelStorage: both binary formats, the embedded lexicon and its agreement with the source table.
/// </summary>
public class ModelStorageTests
{
    /// <summary>
    ///     Reads the embedded lexicon resource.
    /// </summary>
    private static byte[] EmbeddedLexiconBytes()
    {
        using var stream = EmbeddedModel.OpenResource(EmbeddedModel.LexiconResourceName);
        Assert.NotNull(stream);
        return BinaryFormat.ReadAllBytes(stream);
    }

    /// <summary>
    ///     Proves that both formats start with their magic and a version field that readers check.
    /// </summary>
    [Fact]
    public void ModelStorage_Formats_StartWithMagicAndVersion()
    {
        // Arrange: one file of each format
        var lexicon = LexiconWriter.Compile(new[] { new KeyValuePair<string, UposSet>("a", UposSet.Of(Upos.Det)) }, true);
        var weights = new TinyModels.WeightsBuilder().Bias(Upos.Noun, 1).Build(compress: true);

        // Act: read the first eight bytes of each
        var lexiconStart = System.Text.Encoding.ASCII.GetString(lexicon, 0, 4);
        var weightsStart = System.Text.Encoding.ASCII.GetString(weights, 0, 4);

        // Assert: magic and version 2 (little endian), Deflate flag set; readers refuse any other version
        Assert.Equal("POSL", lexiconStart);
        Assert.Equal("POSW", weightsStart);
        Assert.Equal((ushort)2, BinaryFormat.ReadUInt16(lexicon, 4));
        Assert.Equal((ushort)2, BinaryFormat.ReadUInt16(weights, 4));
        Assert.Equal((ushort)1, BinaryFormat.ReadUInt16(lexicon, 6));
        Assert.Equal((ushort)1, BinaryFormat.ReadUInt16(weights, 6));
        BinaryFormat.WriteUInt16(lexicon, 4, 1);
        BinaryFormat.WriteUInt16(weights, 4, 3);
        Assert.Throws<InvalidDataException>(() => Lexicon.Load(lexicon));
        Assert.Throws<InvalidDataException>(() => PerceptronModel.Load(weights, FeatureExtractor.FeatureSetVersion));
    }

    /// <summary>
    ///     Proves that the embedded lexicon holds exactly the spellings and tags of the source table, and that the committed
    ///     binary is current (the same comparison the LexiconCompiler tool makes in its check mode).
    /// </summary>
    [Fact]
    public void ModelStorage_EmbeddedLexicon_EncodesLexiconTsv()
    {
        // Arrange: the source table parsed by the writer, and the table read independently by the test helper
        var entries = LexiconWriter.FromTsv(File.ReadLines(DataFiles.Data("lexicon/lexicon.tsv")));
        var reference = DataFiles.ReadLexicon(DataFiles.Data("lexicon/lexicon.tsv"));
        var embedded = EmbeddedModel.Lexicon;

        // Act: decode the embedded lexicon, compile the table afresh and read the committed binary file
        var decoded = embedded.Enumerate().ToList();
        var fresh = LexiconWriter.Compile(entries, false);
        var committed = File.ReadAllBytes(DataFiles.Repository("src/DemaConsulting.PartOfSpeech/Resources/lexicon.bin"));

        // Assert: same spellings in the same order with the same tags as the independent reading; the committed and the
        // embedded file are the same bytes; a fresh compilation gives the same uncompressed image
        Assert.Equal(reference.Count, decoded.Count);
        Assert.All(decoded, pair => Assert.Equal(reference[pair.Key], pair.Value.Select(t => t.ToTagString()).ToArray()));
        Assert.Equal(committed, EmbeddedLexiconBytes());
        Assert.Equal(fresh, Lexicon.ToUncompressedImage(committed));
    }

    /// <summary>
    ///     Proves that a lookup finds every one of the 262,635 spellings of the table with its tags and that a spelling that is
    ///     not in the table is not found.
    /// </summary>
    [Fact]
    public void ModelStorage_EmbeddedLexicon_EveryTsvSpellingRoundTrips()
    {
        // Arrange: the table and the embedded lexicon with one reusable scratch buffer
        var reference = DataFiles.ReadLexicon(DataFiles.Data("lexicon/lexicon.tsv"));
        var lexicon = EmbeddedModel.Lexicon;
        var scratch = new byte[lexicon.ScratchSize];
        var failures = new List<string>();

        // Act: look every spelling up (as ToLowerInvariant of itself, like the decoder) and probe absent neighbors
        foreach (var pair in reference)
        {
            var key = pair.Key.ToLowerInvariant().ToCharArray();
            if (!lexicon.TryGetTags(key, 0, key.Length, scratch, out var tags) || !tags.Select(t => t.ToTagString()).SequenceEqual(pair.Value))
            {
                failures.Add(pair.Key);
            }
        }

        var absent = new[] { "zzzzzz\u0001", "the\u0001", "\u0001", "qqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqq" };
        var absentFound = absent.Where(a =>
        {
            var chars = a.ToCharArray();
            return !reference.ContainsKey(a) && lexicon.TryGetTags(chars, 0, chars.Length, scratch, out _);
        }).ToList();

        // Assert: no failures and no false positives
        Assert.Empty(failures.Take(10));
        Assert.Empty(absentFound);
    }

    /// <summary>
    ///     Proves that threads that access the lexicon at the same time share one loaded instance.
    /// </summary>
    [Fact]
    public async Task ModelStorage_EmbeddedLexicon_ParallelFirstAccess_LoadsOnce()
    {
        // Arrange: 16 threads released together
        using var barrier = new Barrier(16);
        var tasks = Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
        {
            barrier.SignalAndWait();
            return EmbeddedModel.Lexicon;
        }, TestContext.Current.CancellationToken)).ToArray();

        // Act: wait for them
        var results = await Task.WhenAll(tasks);

        // Assert: one instance for all
        Assert.All(results, r => Assert.Same(results[0], r));
    }

    /// <summary>
    ///     Proves that a tiny weights file and a tiny lexicon written by the writers load back through streams with the same content.
    /// </summary>
    [Fact]
    public void ModelStorage_WeightsWriterAndReader_RoundTrip()
    {
        // Arrange: hand-set weights (word cat favors NOUN by 5 and VERB by 2) in a Deflate file, and a tiny lexicon
        var weights = new TinyModels.WeightsBuilder().Word("cat", Upos.Noun, 5).Word("cat", Upos.Verb, 2).Build(2.5f, 0.5f, true);
        var lexicon = TinyModels.CreateLexicon("cat NOUN VERB", "dog NOUN");
        using var weightStream = new MemoryStream(weights);
        using var lexiconStream = new MemoryStream(lexicon);

        // Act: load both from streams and score the feature of the word cat
        var model = PerceptronModel.Load(weightStream, FeatureExtractor.FeatureSetVersion);
        var loaded = Lexicon.Load(lexiconStream);
        var scores = new int[17];
        model.AddScores(FeatureExtractor.ReferenceBucket(FeatureKind.Lower, "cat", null, TinyModels.HashBits), scores);

        // Assert: weights, header values and lexicon content survived
        Assert.Equal(5, scores[(int)Upos.Noun]);
        Assert.Equal(2, scores[(int)Upos.Verb]);
        Assert.Equal(2.5f, model.Temperature);
        Assert.Equal(0.5f, model.Scale);
        Assert.Equal(2, loaded.SpellingCount);
        Assert.Equal(3, loaded.EntryCount);
    }

    /// <summary>
    ///     Proves that the committed weights file is the shipped feature set with the clause-cue group and no other: feature set 2, the clause-cue mask at offset 40 and zero reserved bytes from offset 44 on.
    /// </summary>
    [Fact]
    public void ModelStorage_EmbeddedWeights_EnableTheClauseCueGroup()
    {
        // Arrange: the committed file
        var committed = File.ReadAllBytes(DataFiles.Repository("src/DemaConsulting.PartOfSpeech/Resources/weights.bin"));

        // Act: read the header fields and the loaded model
        var model = EmbeddedModel.Weights;

        // Assert: the layout of the format with the clause-cue group only
        Assert.Equal((ushort)2, BinaryFormat.ReadUInt16(committed, 8));
        Assert.Equal((uint)FeatureGroups.ClauseCues, BinaryFormat.ReadUInt32(committed, PerceptronModel.GroupMaskOffset));
        Assert.All(committed.Skip(44).Take(4), b => Assert.Equal(0, b));
        Assert.Equal(FeatureGroups.ClauseCues, model.FeatureGroups);
    }

    /// <summary>
    ///     Proves that a weights file with groups goes through the writer, a stream and the reader with its mask.
    /// </summary>
    [Fact]
    public void ModelStorage_WeightsWriterAndReader_FeatureGroupsRoundTrip()
    {
        // Arrange: hand-set weights written with the clause cues and the preview group in a Deflate file
        var groups = FeatureGroups.ClauseCues | FeatureGroups.RightPreview;
        var weights = new TinyModels.WeightsBuilder().Word("cat", Upos.Noun, 5).Build(2.5f, 0.5f, true, groups);
        using var stream = new MemoryStream(weights);

        // Act: load from the stream
        var model = PerceptronModel.Load(stream);

        // Assert: the mask and the feature set of the header
        Assert.Equal(groups, model.FeatureGroups);
        Assert.Equal(FeatureExtractor.GroupsFeatureSetVersion, BinaryFormat.ReadUInt16(weights, 8));
        Assert.Equal((uint)groups, BinaryFormat.ReadUInt32(weights, 40));
    }
}
