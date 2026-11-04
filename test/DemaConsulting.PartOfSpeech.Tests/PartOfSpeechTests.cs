// cspell:ignore zorbulon zzqxj Cquoted
using System.Xml.Linq;

namespace DemaConsulting.PartOfSpeech.Tests;

/// <summary>
///     System-level tests of the library: the public API from text to tagged tokens with the embedded lexicon, models loaded
///     from streams, constraints, concurrency, throughput and packaging.
/// </summary>
public class PartOfSpeechTests
{
    /// <summary>
    ///     The weights of the tiny trained model that most system tests load (trained once).
    /// </summary>
    private static readonly byte[] TinyWeights = TinyTrainer.Train(compress: true);

    /// <summary>
    ///     Creates a tagger from the tiny trained model with the lexicon that is embedded in the assembly.
    /// </summary>
    private static Tagger EmbeddedLexiconTagger(TaggerOptions? options = null)
    {
        using var stream = new MemoryStream(TinyWeights);
        return Tagger.Load(stream, options);
    }

    /// <summary>
    ///     Proves the whole pipeline on every platform: the embedded lexicon (an embedded resource that is inflated with Deflate)
    ///     and a weights file loaded through the public API tag every token of a text with one of the 17 tags.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Tagging_Text_TagsEveryTokenWithEmbeddedLexicon()
    {
        // Arrange: a tagger with the embedded lexicon and a sentence
        var tagger = EmbeddedLexiconTagger();

        // Act: tag a text
        var sentences = tagger.Tag("The cat sat on the mat.");

        // Assert: one sentence, seven tokens, each with a defined tag, a confidence in (0, 1] and an allowed tag
        var sentence = Assert.Single(sentences);
        Assert.Equal(new[] { "The", "cat", "sat", "on", "the", "mat", "." }, sentence.Tokens.Select(t => t.Text));
        Assert.All(sentence.Tokens, token =>
        {
            Assert.True(Enum.IsDefined(typeof(Upos), token.Tag));
            Assert.InRange(token.Confidence, 0.0, 1.0);
            Assert.True(token.Confidence > 0);
            Assert.True(Lexicon.AllowedTags(token.Text, EmbeddedModel.Lexicon).Contains(token.Tag));
        });
        Assert.Equal(new[] { Upos.Det, Upos.Noun, Upos.Verb, Upos.Adp, Upos.Det, Upos.Noun, Upos.Punct }, sentence.Tokens.Select(t => t.Tag));
    }

    /// <summary>
    ///     Proves that a text with several sentences is split and every sentence is tagged.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Tagging_Text_MultipleSentences_AreSplitAndTagged()
    {
        // Arrange: three sentences with different terminators
        var tagger = EmbeddedLexiconTagger();

        // Act: tag them
        var sentences = tagger.Tag("He writes a letter. They play games in the garden! Who is that man?");

        // Assert: three sentences with the texts and token counts of the input
        Assert.Equal(new[] { "He writes a letter.", "They play games in the garden!", "Who is that man?" }, sentences.Select(s => s.Text));
        Assert.Equal(new[] { 5, 7, 5 }, sentences.Select(s => s.Tokens.Count));
        Assert.Equal(Upos.Punct, sentences[2].Tokens[4].Tag);
    }

    /// <summary>
    ///     Proves that sentences and tokens carry offsets into the original string, whatever the white space.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Tagging_Text_OffsetsIndexTheOriginalString()
    {
        // Arrange: a text with tabs, line breaks, extra spaces and a contraction
        const string text = "  The cat\tsat   on\nthe mat.\r\n\r\n  We don't know. ";
        var tagger = EmbeddedLexiconTagger();

        // Act: tag it
        var sentences = tagger.Tag(text);

        // Assert: every sentence and token is the slice of the original text at its offset (a blank line separates the sentences)
        Assert.Equal(2, sentences.Count);
        foreach (var sentence in sentences)
        {
            Assert.Equal(text.Substring(sentence.Start, sentence.Length), sentence.Text);
            Assert.All(sentence.Tokens, token => Assert.Equal(token.Text, text.Substring(token.Start, token.Length)));
        }
    }

    /// <summary>
    ///     Proves that already tokenized input is tagged exactly as given, without re-tokenizing.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Tagging_Tokens_AreTaggedAsGiven()
    {
        // Arrange: tokens that a tokenizer would never produce (the contraction is not split)
        var tagger = EmbeddedLexiconTagger();
        var tokens = new[] { "She", "sings", "don't", "." };

        // Act: tag the tokens
        var sentence = tagger.Tag(tokens);

        // Assert: four tagged tokens with the given texts
        Assert.Equal(tokens, sentence.Tokens.Select(t => t.Text));
        Assert.Equal("She sings don't .", sentence.Text);
        Assert.Equal(Upos.Pron, sentence.Tokens[0].Tag);
        Assert.Equal(Upos.Punct, sentence.Tokens[3].Tag);
    }

    /// <summary>
    ///     Proves that the confidence and the alternatives form a probability distribution over the allowed tags, sorted by probability.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Tagging_Confidence_ProbabilitiesSumToOneAndAreOrdered()
    {
        // Arrange: a tagger that reports all alternatives, and a sentence with an unknown word
        var tagger = EmbeddedLexiconTagger(new TaggerOptions { MaxAlternatives = 16 });

        // Act: tag it
        var sentence = tagger.Tag("The cat saw a zorbulon.").Single();

        // Assert: the probabilities of the best tag and the alternatives add up to one and descend; the best tag has the highest
        foreach (var token in sentence.Tokens)
        {
            var all = new[] { token.Confidence }.Concat(token.Alternatives.Select(a => a.Probability)).ToList();
            Assert.Equal(1.0, all.Sum(), 9);
            Assert.Equal(all.OrderByDescending(p => p), all);
        }

        Assert.True(sentence.Tokens.Single(t => t.Text == "zorbulon").Alternatives.Count == 16);
    }

    /// <summary>
    ///     Proves that a known word only receives tags that the lexicon and the token-shape rules allow, even when the model prefers another tag.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Tagging_KnownWord_StaysWithinAllowedTags()
    {
        // Arrange: weights that push the word "the" and the word "Paris" to tags that are not theirs
        var weights = new TinyModels.WeightsBuilder().Word("the", Upos.Intj, 100).Word("cat", Upos.Adv, 100).Build();
        var tagger = TinyModels.CreateTagger(weights);

        // Act: tag known words
        var tagged = tagger.Tag(new[] { "the", "cat" });

        // Assert: neither preferred tag was chosen unless the lexicon allows it (the lexicon lists ADV for "the", so only INTJ is excluded there)
        foreach (var token in tagged.Tokens)
        {
            Assert.True(Lexicon.AllowedTags(token.Text, EmbeddedModel.Lexicon).Contains(token.Tag), $"{token.Text}: {token.Tag}");
        }

        Assert.NotEqual(Upos.Intj, tagged.Tokens[0].Tag);
        Assert.NotEqual(Upos.Adv, tagged.Tokens[1].Tag);
    }

    /// <summary>
    ///     Proves that a word that is not in the lexicon may take any of the 17 tags.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Tagging_UnknownWord_MayTakeAnyTag()
    {
        // Arrange: weights that make an invented word an interjection
        const string invented = "zzqxj";
        Assert.Equal(17, Lexicon.AllowedTags(invented, EmbeddedModel.Lexicon).Count);
        var weights = new TinyModels.WeightsBuilder().Word(invented, Upos.Intj, 50).Build();
        var tagger = TinyModels.CreateTagger(weights, null, new TaggerOptions { MaxAlternatives = 16 });

        // Act: tag it
        var token = tagger.Tag(new[] { invented }).Tokens[0];

        // Assert: INTJ was chosen from all 17 tags
        Assert.Equal(Upos.Intj, token.Tag);
        Assert.Equal(16, token.Alternatives.Count);
    }

    /// <summary>
    ///     Proves that an allowed-set constraint forces a tag that the model would not choose.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Tagging_Constraint_AllowedSetForcesTag()
    {
        // Arrange: a tagger and the word cat, which the tiny model tags as NOUN
        var tagger = EmbeddedLexiconTagger();
        var free = tagger.Tag(new[] { "The", "cat" }).Tokens[1];

        // Act: tag again with the word restricted to VERB
        var forced = tagger.Tag(new[] { "The", "cat" }, new[] { TokenConstraint.None, TokenConstraint.Only(Upos.Verb) }).Tokens[1];

        // Assert: NOUN without and VERB with the constraint, certain and flagged
        Assert.Equal(Upos.Noun, free.Tag);
        Assert.Equal(Upos.Verb, forced.Tag);
        Assert.Equal(1.0, forced.Confidence);
        Assert.True(forced.IsConstrained);
    }

    /// <summary>
    ///     Proves that a boost in score units flips a close decision and a small one does not.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Tagging_Constraint_BoostShiftsCloseDecision()
    {
        // Arrange: the word cat scores NOUN 3 and VERB 1 (the embedded lexicon allows both)
        var weights = new TinyModels.WeightsBuilder().Word("cat", Upos.Noun, 3).Word("cat", Upos.Verb, 1).Build();
        var tagger = TinyModels.CreateTagger(weights);

        // Act: tag free, with a boost of 1 and with a boost of 5 for VERB
        var free = tagger.Tag(new[] { "cat" }).Tokens[0];
        var small = tagger.Tag(new[] { "cat" }, new[] { TokenConstraint.Prefer(Upos.Verb, 1) }).Tokens[0];
        var large = tagger.Tag(new[] { "cat" }, new[] { TokenConstraint.Prefer(Upos.Verb, 5) }).Tokens[0];

        // Assert: NOUN, NOUN with a lower confidence, VERB
        Assert.Equal(Upos.Noun, free.Tag);
        Assert.Equal(Upos.Noun, small.Tag);
        Assert.True(small.Confidence < free.Confidence);
        Assert.Equal(Upos.Verb, large.Tag);
    }

    /// <summary>
    ///     Proves that a weights file can be loaded from a file stream and tags text.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Model_LoadFromStream_TagsText()
    {
        // Arrange: write the weights to a temporary file
        var path = Path.Combine(Path.GetTempPath(), "pos-weights-" + Guid.NewGuid().ToString("N") + ".bin");
        File.WriteAllBytes(path, TinyWeights);
        try
        {
            // Act: load from a FileStream and tag
            using var stream = File.OpenRead(path);
            var tagger = Tagger.Load(stream);
            var sentences = tagger.Tag("She reads a book.");

            // Assert: the model works
            Assert.Equal(new[] { Upos.Pron, Upos.Verb, Upos.Det, Upos.Noun, Upos.Punct }, sentences.Single().Tokens.Select(t => t.Tag));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    ///     Proves that files that are not valid weights are rejected with an InvalidDataException that says why.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Model_InvalidWeights_ThrowInvalidDataException()
    {
        // Arrange: not a weights file, a truncated one, one of another version and one with a changed byte
        var garbage = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var truncated = TinyWeights.Take(TinyWeights.Length / 2).ToArray();
        var version = (byte[])TinyWeights.Clone();
        BinaryFormat.WriteUInt16(version, 4, 7);
        var corrupt = (byte[])TinyWeights.Clone();
        corrupt[corrupt.Length / 2] ^= 0xFF;

        // Act / Assert: each is an InvalidDataException with a distinct message
        Assert.Contains("POSW", Assert.Throws<InvalidDataException>(() => Tagger.Load(new MemoryStream(garbage))).Message);
        Assert.Contains("truncated", Assert.Throws<InvalidDataException>(() => Tagger.Load(new MemoryStream(truncated))).Message);
        Assert.Contains("version 7", Assert.Throws<InvalidDataException>(() => Tagger.Load(new MemoryStream(version))).Message);
        Assert.Throws<InvalidDataException>(() => Tagger.Load(new MemoryStream(corrupt)));
        Assert.Throws<InvalidDataException>(() => Tagger.Load(new MemoryStream(new byte[0])));
    }

    /// <summary>
    ///     Proves that the default tagger needs no file: it tags running text with the model that is embedded in the
    ///     package, gives the unmistakable tags of a few hand-written sentences, and many threads share one instance
    ///     with sequential results.
    /// </summary>
    [Fact]
    public async Task PartOfSpeech_Default_EmbeddedModel_TagsTextWithoutFile()
    {
        // Arrange: the default tagger, two simple sentences as text and a sequential reference
        var tagger = Tagger.Default;
        const string text = "The cat sat on the mat. She bought two red books.";
        var expected = Describe(tagger.Tag(text));
        var mismatches = 0;

        // Act: tag the text and tag it again from eight threads that read the property themselves
        var sentences = tagger.Tag(text);
        var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (var iteration = 0; iteration < 20; iteration++)
            {
                if (Describe(Tagger.Default.Tag(text)) != expected)
                {
                    Interlocked.Increment(ref mismatches);
                }
            }
        }, TestContext.Current.CancellationToken)).ToArray();
        await Task.WhenAll(tasks);

        // Assert: two sentences with the obvious tags, the shared instance and no difference between threads
        Assert.Same(tagger, Tagger.Default);
        Assert.Equal(2, sentences.Count);
        Assert.Equal(
            new[] { Upos.Det, Upos.Noun, Upos.Verb, Upos.Adp, Upos.Det, Upos.Noun, Upos.Punct },
            sentences[0].Tokens.Select(t => t.Tag));
        Assert.Equal(
            new[] { Upos.Pron, Upos.Verb, Upos.Num, Upos.Adj, Upos.Noun, Upos.Punct },
            sentences[1].Tokens.Select(t => t.Tag));
        Assert.Equal(0, mismatches);
    }

    /// <summary>
    ///     Proves that eight threads sharing one tagger get the same results as a sequential run.
    /// </summary>
    [Fact]
    public async Task PartOfSpeech_Concurrency_ParallelTagging_MatchesSequential()
    {
        // Arrange: one tagger and a set of texts; the sequential results are the reference
        var tagger = EmbeddedLexiconTagger();
        var texts = TinyTrainer.Sentences().Select(s => string.Join(" ", s.Tokens)).Concat(new[] { "Caf\u00E9 \u201Cquoted\u201D text isn\u2019t hard." }).ToList();
        var expected = texts.Select(t => Describe(tagger.Tag(t))).ToList();
        var mismatches = 0;

        // Act: eight tasks, 200 iterations each, in different orders
        var tasks = Enumerable.Range(0, 8).Select(task => Task.Run(() =>
        {
            for (var iteration = 0; iteration < 200; iteration++)
            {
                var index = (iteration + (task * 5)) % texts.Count;
                if (Describe(tagger.Tag(texts[index])) != expected[index])
                {
                    Interlocked.Increment(ref mismatches);
                }
            }
        }, TestContext.Current.CancellationToken)).ToArray();
        await Task.WhenAll(tasks);

        // Assert: no difference
        Assert.Equal(0, mismatches);
    }

    /// <summary>
    ///     Proves that the package has no runtime dependency: every package reference of the library project is a private build-time asset.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Packaging_ProjectFile_HasNoRuntimePackageReferences()
    {
        // Arrange: load the project file of the library
        var project = XDocument.Load(DataFiles.Repository("src/DemaConsulting.PartOfSpeech/DemaConsulting.PartOfSpeech.csproj"));

        // Act: collect the package references that are not private assets
        var references = project.Descendants("PackageReference").ToList();
        var exposed = references
            .Where(r => !string.Equals(((string?)r.Attribute("PrivateAssets")) ?? (string?)r.Element("PrivateAssets"), "all", StringComparison.OrdinalIgnoreCase))
            .Select(r => (string?)r.Attribute("Include"))
            .ToList();

        // Assert: none is exposed, and the lexicon and the weights are embedded as resources
        Assert.NotEmpty(references);
        Assert.Empty(exposed);
        Assert.Contains(project.Descendants("EmbeddedResource"), r => ((string?)r.Attribute("Include") ?? string.Empty).Replace('\\', '/').EndsWith("Resources/lexicon.bin", StringComparison.Ordinal));
        Assert.Contains(project.Descendants("EmbeddedResource"), r => ((string?)r.Attribute("Include") ?? string.Empty).Replace('\\', '/').EndsWith("Resources/weights.bin", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Describes tagged sentences with tags, confidences and alternatives, for equality comparisons.
    /// </summary>
    private static string Describe(IReadOnlyList<TaggedSentence> sentences) =>
        string.Join("|", sentences.SelectMany(s => s.Tokens).Select(t =>
            $"{t.Text}@{t.Start}/{t.Tag}/{t.Confidence:R}/{string.Join(",", t.Alternatives)}"));
}
