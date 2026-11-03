// cspell:ignore zorbulon Cquotes
namespace DemaConsulting.PartOfSpeech.Tests.Tagging;

/// <summary>
///     Unit tests of <see cref="Tagger"/> and its option and result types.
/// </summary>
public class TaggerTests
{
    /// <summary>
    ///     The weights of the tiny trained model (trained once for all tests of the class).
    /// </summary>
    private static readonly byte[] Weights = TinyTrainer.Train();

    /// <summary>
    ///     The lexicon of the fixture words.
    /// </summary>
    private static readonly byte[] FixtureLexicon = TinyTrainer.FixtureLexicon();

    /// <summary>
    ///     Creates a tagger from the tiny model.
    /// </summary>
    private static Tagger Create(TaggerOptions? options = null) => TinyModels.CreateTagger(Weights, FixtureLexicon, options);

    /// <summary>
    ///     Proves that a text is split into sentences whose tokens carry the learned tags.
    /// </summary>
    [Fact]
    public void Tagger_Tag_Text_ReturnsTaggedSentences()
    {
        // Arrange: a tagger and two fixture-like sentences
        var tagger = Create();

        // Act: tag the text
        var sentences = tagger.Tag("The cat sat on the mat. She reads a book.");

        // Assert: two sentences with the expected tags
        Assert.Equal(2, sentences.Count);
        Assert.Equal("The cat sat on the mat.", sentences[0].Text);
        Assert.Equal(
            new[] { Upos.Det, Upos.Noun, Upos.Verb, Upos.Adp, Upos.Det, Upos.Noun, Upos.Punct },
            sentences[0].Tokens.Select(t => t.Tag));
        Assert.Equal(new[] { Upos.Pron, Upos.Verb, Upos.Det, Upos.Noun, Upos.Punct }, sentences[1].Tokens.Select(t => t.Tag));
        Assert.Empty(tagger.Tag("   "));
    }

    /// <summary>
    ///     Proves that already tokenized input is tagged as given and the sentence text is the joined tokens.
    /// </summary>
    [Fact]
    public void Tagger_Tag_Tokens_ReturnsTags()
    {
        // Arrange: tokens with an irregular split that a tokenizer would not produce
        var tagger = Create();
        var tokens = new[] { "The", "cat", "sat", "on", "the", "mat", "." };

        // Act: tag them
        var sentence = tagger.Tag(tokens);

        // Assert: one tagged token per input token with the learned tags, joined text, offsets into it
        Assert.Equal(tokens, sentence.Tokens.Select(t => t.Text));
        Assert.Equal("The cat sat on the mat .", sentence.Text);
        Assert.Equal(new[] { Upos.Det, Upos.Noun, Upos.Verb, Upos.Adp, Upos.Det, Upos.Noun, Upos.Punct }, sentence.Tokens.Select(t => t.Tag));
        Assert.All(sentence.Tokens, t => Assert.Equal(t.Text, sentence.Text.Substring(t.Start, t.Length)));
    }

    /// <summary>
    ///     Proves that an empty token list gives an empty sentence.
    /// </summary>
    [Fact]
    public void Tagger_Tag_Tokens_EmptyTokens_ReturnsEmptySentence()
    {
        // Arrange / Act: tag no tokens
        var sentence = Create().Tag(new string[0]);

        // Assert: empty sentence
        Assert.Empty(sentence.Tokens);
        Assert.Equal(string.Empty, sentence.Text);
        Assert.Equal(0, sentence.Length);
    }

    /// <summary>
    ///     Proves that the offsets of the tagged tokens index the original text.
    /// </summary>
    [Fact]
    public void Tagger_Tag_Text_TokenTextMatchesOffsets()
    {
        // Arrange: a text with odd spacing and a contraction
        const string text = "  The   cat\tsat on the mat.\n\n  We don't  know \"why\".  ";

        // Act: tag it
        var sentences = Create().Tag(text);

        // Assert: sentence and token offsets index the text (a blank line separates the two sentences)
        Assert.Equal(2, sentences.Count);
        foreach (var sentence in sentences)
        {
            Assert.Equal(text.Substring(sentence.Start, sentence.Length), sentence.Text);
            Assert.All(sentence.Tokens, t => Assert.Equal(t.Text, text.Substring(t.Start, t.Length)));
        }
    }

    /// <summary>
    ///     Proves that null streams are rejected by both overloads.
    /// </summary>
    [Fact]
    public void Tagger_Load_NullStreams_Throw()
    {
        // Arrange: a valid stream
        using var weights = new MemoryStream(Weights);
        using var lexicon = new MemoryStream(FixtureLexicon);

        // Act / Assert: null weights or null lexicon throw
        Assert.Throws<ArgumentNullException>(() => Tagger.Load(null!));
        Assert.Throws<ArgumentNullException>(() => Tagger.Load(null!, lexicon));
        Assert.Throws<ArgumentNullException>(() => Tagger.Load(weights, (Stream)null!));
    }

    /// <summary>
    ///     Proves that the number of alternatives must be 0 to 16.
    /// </summary>
    [Fact]
    public void Tagger_Load_InvalidOptions_Throw()
    {
        // Arrange / Act / Assert: -1 and 17 are rejected, 0 and 16 accepted
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(new TaggerOptions { MaxAlternatives = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(new TaggerOptions { MaxAlternatives = 17 }));
        Assert.Equal(0, Create(new TaggerOptions { MaxAlternatives = 0 }).Options.MaxAlternatives);
        Assert.Equal(16, Create(new TaggerOptions { MaxAlternatives = 16 }).Options.MaxAlternatives);
    }

    /// <summary>
    ///     Proves that a lexicon passed to <see cref="Tagger.Load(Stream, Stream, TaggerOptions?)"/> replaces the embedded one.
    /// </summary>
    [Fact]
    public void Tagger_Load_CustomLexicon_IsUsed()
    {
        // Arrange: a lexicon that allows only ADJ for cat, next to the fixture lexicon and the embedded lexicon
        var custom = TinyModels.CreateLexicon("cat ADJ");

        // Act: tag cat with each lexicon
        var withCustom = TinyModels.CreateTagger(Weights, custom).Tag(new[] { "cat" }).Tokens[0];
        var withFixture = Create().Tag(new[] { "cat" }).Tokens[0];
        var withEmbedded = TinyModels.CreateTagger(Weights).Tag(new[] { "cat" }).Tokens[0];

        // Assert: the custom lexicon restricts; the embedded lexicon allows NOUN and VERB for cat and the model prefers NOUN
        Assert.Equal(Upos.Adj, withCustom.Tag);
        Assert.Equal(1.0, withCustom.Confidence);
        Assert.Equal(Upos.Noun, withFixture.Tag);
        Assert.Equal(Upos.Noun, withEmbedded.Tag);
    }

    /// <summary>
    ///     Proves that the default tagger explains why it is unavailable and keeps doing so.
    /// </summary>
    [Fact]
    public void Tagger_Default_NoEmbeddedWeights_ThrowsWithClearMessage()
    {
        // Arrange / Act: ask twice
        var first = Assert.Throws<InvalidOperationException>(() => Tagger.Default);
        var second = Assert.Throws<InvalidOperationException>(() => Tagger.Default);

        // Assert: the message names the problem and the remedy
        Assert.Contains("no trained model", first.Message);
        Assert.Contains("Tagger.Load(Stream)", first.Message);
        Assert.Equal(first.Message, second.Message);
    }

    /// <summary>
    ///     Proves that one tagger used by eight threads gives the same results as a sequential run.
    /// </summary>
    [Fact]
    public async Task Tagger_Tag_Parallel_MatchesSequential()
    {
        // Arrange: one tagger, a mixed set of texts (including non-ASCII, which uses the allocating path) and a sequential reference
        var tagger = Create();
        var texts = TinyTrainer.Sentences().Select(s => string.Join(" ", s.Tokens)).Concat(new[] { "Caf\u00E9 \u201Cquotes\u201D don\u2019t stop.", "An unknown zorbulon sat." }).ToList();
        var expected = texts.Select(t => Describe(tagger.Tag(t))).ToList();
        var mismatches = 0;

        // Act: eight tasks tag everything repeatedly and compare
        var tasks = Enumerable.Range(0, 8).Select(task => Task.Run(() =>
        {
            for (var round = 0; round < 25; round++)
            {
                for (var i = 0; i < texts.Count; i++)
                {
                    var index = (i + task) % texts.Count;
                    if (Describe(tagger.Tag(texts[index])) != expected[index])
                    {
                        Interlocked.Increment(ref mismatches);
                    }
                }
            }
        }, TestContext.Current.CancellationToken)).ToArray();
        await Task.WhenAll(tasks);

        // Assert: identical results
        Assert.Equal(0, mismatches);
    }

    /// <summary>
    ///     Proves that results and options cannot be modified through the interfaces they expose.
    /// </summary>
    [Fact]
    public void Tagger_Tag_ResultsAreImmutable()
    {
        // Arrange: a tagger created from options that are changed afterwards, and a tagged sentence
        var options = new TaggerOptions { MaxAlternatives = 2 };
        var tagger = Create(options);
        var sentence = tagger.Tag(new[] { "The", "cat" });

        // Act: try to write through the list interfaces and read the options again
        var tokens = (IList<TaggedToken>)sentence.Tokens;
        var alternatives = sentence.Tokens[0].Alternatives;
        var again = tagger.Options;

        // Assert: the token list is read-only, the alternatives expose no writable interface at all, and the options are a copy
        Assert.True(tokens.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => tokens[0] = default);
        Assert.False(alternatives is IList<TagScore>);
        Assert.False(alternatives is TagScore[]);
        Assert.Throws<ArgumentOutOfRangeException>(() => alternatives[alternatives.Count]);
        Assert.Equal(2, again.MaxAlternatives);
        Assert.NotSame(again, tagger.Options);
        Assert.True(alternatives.Count <= 2);
    }

    /// <summary>
    ///     Proves that null arguments are rejected by every overload.
    /// </summary>
    [Fact]
    public void Tagger_Tag_NullArguments_Throw()
    {
        // Arrange: a tagger
        var tagger = Create();

        // Act / Assert: each overload rejects null
        Assert.Throws<ArgumentNullException>(() => tagger.Tag((string)null!));
        Assert.Throws<ArgumentNullException>(() => tagger.Tag((Sentence)null!));
        Assert.Throws<ArgumentNullException>(() => tagger.Tag((IReadOnlyList<string>)null!));
        Assert.Throws<ArgumentException>(() => tagger.Tag(new[] { "a", string.Empty }));
    }

    /// <summary>
    ///     Proves that the constraint list must have one element per token.
    /// </summary>
    [Fact]
    public void Tagger_Tag_ConstraintLengthMismatch_Throws()
    {
        // Arrange: a tagger and two tokens
        var tagger = Create();

        // Act / Assert: one constraint for two tokens is rejected, for both overloads
        Assert.Throws<ArgumentException>(() => tagger.Tag(new[] { "The", "cat" }, new[] { TokenConstraint.None }));
        Assert.Throws<ArgumentException>(() => tagger.Tag(Tokenizer.CreateSentence(new[] { "The", "cat" }), new TokenConstraint[3]));
        Assert.Equal(2, tagger.Tag(new[] { "The", "cat" }, new[] { TokenConstraint.None, TokenConstraint.None }).Tokens.Count);
    }

    /// <summary>
    ///     Proves that the option limits the number of alternatives of every token.
    /// </summary>
    [Fact]
    public void Tagger_Options_MaxAlternativesLimitsAlternatives()
    {
        // Arrange: three taggers with 0, 2 and 16 alternatives
        var tokens = new[] { "An", "unknown", "zorbulon", "sat", "." };

        // Act: tag with each
        var none = Create(new TaggerOptions { MaxAlternatives = 0 }).Tag(tokens);
        var two = Create(new TaggerOptions { MaxAlternatives = 2 }).Tag(tokens);
        var all = Create(new TaggerOptions { MaxAlternatives = 16 }).Tag(tokens);

        // Assert: limits hold; unknown words have the full number of alternatives available
        Assert.All(none.Tokens, t => Assert.Empty(t.Alternatives));
        Assert.All(two.Tokens, t => Assert.True(t.Alternatives.Count <= 2));
        Assert.Equal(2, two.Tokens[2].Alternatives.Count);
        Assert.Equal(16, all.Tokens[2].Alternatives.Count);
        Assert.Equal(3, new TaggerOptions().MaxAlternatives);
    }

    /// <summary>
    ///     Proves that the alternatives exclude the chosen tag, are sorted and add up to the confidence of the chosen tag at most to one.
    /// </summary>
    [Fact]
    public void Tagger_Tag_Alternatives_ExcludeBestAndSumWithConfidence()
    {
        // Arrange: a tagger that reports all alternatives
        var tagger = Create(new TaggerOptions { MaxAlternatives = 16 });

        // Act: tag a sentence with known and unknown words
        var sentence = tagger.Tag("The cat sat on a zorbulon.").Single();

        // Assert: for every token the best tag is not an alternative, the alternatives are sorted, and everything adds up to one
        foreach (var token in sentence.Tokens)
        {
            Assert.DoesNotContain(token.Alternatives, a => a.Tag == token.Tag);
            Assert.Equal(token.Alternatives.OrderByDescending(a => a.Probability).Select(a => a.Probability), token.Alternatives.Select(a => a.Probability));
            Assert.Equal(1.0, token.Confidence + token.Alternatives.Sum(a => a.Probability), 9);
            Assert.All(token.Alternatives, a => Assert.True(a.Probability <= token.Confidence + 1e-12));
        }
    }

    /// <summary>
    ///     Describes a tagged text with tags, confidences and alternatives, for equality comparisons.
    /// </summary>
    private static string Describe(IReadOnlyList<TaggedSentence> sentences) =>
        string.Join("|", sentences.SelectMany(s => s.Tokens).Select(t =>
            $"{t.Text}@{t.Start}/{t.Tag}/{t.Confidence:R}/{string.Join(",", t.Alternatives)}"));
}
