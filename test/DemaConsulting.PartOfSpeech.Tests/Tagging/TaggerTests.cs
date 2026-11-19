// cspell:ignore zorbulon zork Cquotes zzqxv Zzqxv zzqx ZZQXV zqxjvkwbplm Zqxjvkwbplm Qzvxkjbwmp Qzvxkjbwmping Xjqzkvwbpl Wkvbzqxjpl
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
    ///     Builds a sentence text with an unpaired surrogate (which is not valid UTF-16) at the given place.
    /// </summary>
    /// <param name="kind"><c>High</c> (lone high surrogate at the end of a word), <c>Low</c> (lone low surrogate at the start of a word) or <c>Mid</c> (a reversed pair inside a word).</param>
    private static string MalformedText(string kind) => kind switch
    {
        "High" => "The cat\uD83D sat on the mat.",
        "Low" => "The \uDE00cat sat on the mat.",
        _ => "The ca\uDE00\uD83Dt sat on the mat."
    };

    /// <summary>
    ///     Proves that text with an unpaired surrogate is tagged without an exception and the tokens still index the text.
    /// </summary>
    [Theory]
    [InlineData("High")]
    [InlineData("Low")]
    [InlineData("Mid")]
    public void Tagger_Tag_Text_UnpairedSurrogate_DoesNotThrow(string kind)
    {
        // Arrange: a tagger and a text that is not well-formed UTF-16
        var tagger = Create();
        var text = MalformedText(kind);

        // Act: tag it twice
        var sentences = tagger.Tag(text);
        var again = tagger.Tag(text);

        // Assert: tokens exist, keep the surrogate and index the original text, and the result is deterministic
        var tokens = sentences.SelectMany(s => s.Tokens).ToList();
        Assert.NotEmpty(tokens);
        Assert.Contains(tokens, t => t.Text.Any(char.IsSurrogate));
        Assert.All(tokens, t => Assert.Equal(t.Text, text.Substring(t.Start, t.Length)));
        Assert.Equal(
            tokens.Select(t => (t.Text, t.Start, t.Tag, t.Confidence)),
            again.SelectMany(s => s.Tokens).Select(t => (t.Text, t.Start, t.Tag, t.Confidence)));
    }

    /// <summary>
    ///     Proves that tokens with an unpaired surrogate are tagged without an exception and keep their text.
    /// </summary>
    [Theory]
    [InlineData("High")]
    [InlineData("Low")]
    [InlineData("Mid")]
    public void Tagger_Tag_Tokens_UnpairedSurrogate_DoesNotThrow(string kind)
    {
        // Arrange: the whitespace-separated pieces of the malformed text as tokens
        var tagger = Create();
        var tokens = MalformedText(kind).Split(' ');

        // Act: tag them twice
        var sentence = tagger.Tag(tokens);
        var again = tagger.Tag(tokens);

        // Assert: the tokens are unchanged, index the joined text, and the result is deterministic
        Assert.Equal(tokens, sentence.Tokens.Select(t => t.Text));
        Assert.Equal(string.Join(" ", tokens), sentence.Text);
        Assert.All(sentence.Tokens, t => Assert.Equal(t.Text, sentence.Text.Substring(t.Start, t.Length)));
        Assert.Equal(
            sentence.Tokens.Select(t => (t.Tag, t.Confidence)),
            again.Tokens.Select(t => (t.Tag, t.Confidence)));
    }

    /// <summary>
    ///     Proves that a valid surrogate pair (an emoji) is still tagged as one piece of text and indexed correctly.
    /// </summary>
    [Fact]
    public void Tagger_Tag_Text_ValidSurrogatePair_RoundTrips()
    {
        // Arrange: emoji as tokens of their own and inside a word
        var tagger = Create();
        const string text = "The cat \U0001F600 sat on the \U0001F431mat.";

        // Act: tag it as text and as tokens
        var sentences = tagger.Tag(text);
        var sentence = tagger.Tag(new[] { "The", "cat", "\U0001F600", "sat" });

        // Assert: the emoji survives whole and offsets index the text
        var tokens = sentences.SelectMany(s => s.Tokens).ToList();
        Assert.Contains(tokens, t => t.Text == "\U0001F600");
        Assert.All(tokens, t => Assert.Equal(t.Text, text.Substring(t.Start, t.Length)));
        Assert.Equal("\U0001F600", sentence.Tokens[2].Text);
        Assert.Equal(2, sentence.Tokens[2].Length);
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
    ///     Proves that a weights file with feature groups loads through the public loader and tags its training sentences (the
    ///     preview group decodes in two passes), and that the tagger reproduces what the same model decodes in a plain loop.
    /// </summary>
    [Fact]
    public void Tagger_Load_ModelWithFeatureGroups_TagsWithTheGroups()
    {
        // Arrange: a toy model trained with all four groups
        var all = FeatureGroups.PositionCues | FeatureGroups.ClauseCues | FeatureGroups.RightPreview | FeatureGroups.Inflection;
        var weights = TinyTrainer.Train(groups: all);
        var tagger = TinyModels.CreateTagger(weights, FixtureLexicon);
        var sentences = TinyTrainer.Sentences();

        // Act: tag every fixture sentence as pre-tokenized text
        var correct = 0;
        var total = 0;
        foreach (var (tokens, gold) in sentences)
        {
            var tagged = tagger.Tag(tokens).Tokens;
            for (var i = 0; i < tokens.Length; i++)
            {
                total++;
                correct += tagged[i].Tag == gold[i] ? 1 : 0;
            }
        }

        // Assert: the header names all groups, and the model has learned the fixtures
        Assert.Equal(all, PerceptronModel.Load(weights).FeatureGroups);
        Assert.True(correct >= total * 0.9, $"{correct} of {total} tokens correct");
    }

    /// <summary>
    ///     Proves that the default tagger is the embedded model and that every read returns the same instance.
    /// </summary>
    [Fact]
    public void Tagger_Default_EmbeddedModel_ReturnsSameWorkingTagger()
    {
        // Arrange / Act: read the property twice and tag with it
        var first = Tagger.Default;
        var second = Tagger.Default;
        var token = first.Tag("The cat sat on the mat.").Single().Tokens[1];

        // Assert: one instance with the default options that tags with the embedded lexicon and weights
        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(3, first.Options.MaxAlternatives);
        Assert.Equal("cat", token.Text);
        Assert.Equal(Upos.Noun, token.Tag);
    }

    /// <summary>
    ///     Proves that many threads reading the default tagger at once all receive one instance.
    /// </summary>
    [Fact]
    public async Task Tagger_Default_ParallelAccess_ReturnsSingleInstance()
    {
        // Arrange: 16 tasks that start together
        using var barrier = new Barrier(16);
        var tasks = Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
        {
            barrier.SignalAndWait();
            return Tagger.Default;
        }, TestContext.Current.CancellationToken)).ToArray();

        // Act: wait for all of them
        var results = await Task.WhenAll(tasks);

        // Assert: every task saw the same instance
        Assert.All(results, r => Assert.Same(results[0], r));
    }

    /// <summary>
    ///     Proves that the default tagger tags a few hand-written sentences with unmistakable tags correctly. The
    ///     sentences are deliberately simple and were not tuned; accuracy is measured in the evaluation, not here.
    /// </summary>
    [Theory]
    [InlineData("The cat sat on the mat .", "DET NOUN VERB ADP DET NOUN PUNCT")]
    [InlineData("She bought two red books .", "PRON VERB NUM ADJ NOUN PUNCT")]
    [InlineData("The dog and the cat sleep quietly .", "DET NOUN CCONJ DET NOUN VERB ADV PUNCT")]
    [InlineData("They walked to the old house .", "PRON VERB ADP DET ADJ NOUN PUNCT")]
    public void Tagger_Default_ObviousSentences_AreTaggedCorrectly(string text, string expected)
    {
        // Arrange: the sentence as pre-tokenized text (the tokens are separated by single spaces)
        var tokens = text.Split(' ');

        // Act: tag it with the embedded model
        var sentence = Tagger.Default.Tag(tokens);

        // Assert: every tag is the unmistakable one
        Assert.Equal(expected, string.Join(" ", sentence.Tokens.Select(t => t.Tag.ToTagString())));
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
    ///     Proves that the overloads that take a glossary refuse a null glossary, and the null arguments that the other overloads refuse.
    /// </summary>
    [Fact]
    public void Tagger_Tag_Glossary_NullArguments_Throw()
    {
        // Arrange: a tagger and a glossary
        var tagger = Create();
        var glossary = Glossary.Empty;
        var sentence = Tokenizer.CreateSentence(new[] { "The", "cat" });

        // Act / Assert: a null glossary, text, sentence or token list is refused by every overload with a glossary
        Assert.Throws<ArgumentNullException>(() => tagger.Tag("The cat sat.", null!));
        Assert.Throws<ArgumentNullException>(() => tagger.Tag(sentence, (Glossary)null!));
        Assert.Throws<ArgumentNullException>(() => tagger.Tag(new[] { "The", "cat" }, (Glossary)null!));
        Assert.Throws<ArgumentNullException>(() => tagger.Tag((string)null!, glossary));
        Assert.Throws<ArgumentNullException>(() => tagger.Tag((Sentence)null!, glossary));
        Assert.Throws<ArgumentNullException>(() => tagger.Tag((IReadOnlyList<string>)null!, glossary));
    }

    /// <summary>
    ///     Proves that the constraint list of an overload with a glossary must have one element per token, like the list of the other overloads.
    /// </summary>
    [Fact]
    public void Tagger_Tag_Glossary_ConstraintLengthMismatch_Throws()
    {
        // Arrange: a tagger and a glossary that matches one of the two tokens
        var tagger = Create();
        var glossary = Glossary.Create(new[] { new GlossaryEntry("cat", Upos.Noun) });

        // Act / Assert: one constraint for two tokens is rejected, with and without a match; a list of the right length is accepted
        Assert.Throws<ArgumentException>(() => tagger.Tag(new[] { "The", "cat" }, glossary, new[] { TokenConstraint.None }));
        Assert.Throws<ArgumentException>(() => tagger.Tag(Tokenizer.CreateSentence(new[] { "The", "dog" }), glossary, new TokenConstraint[3]));
        Assert.Equal(2, tagger.Tag(new[] { "The", "cat" }, glossary, new[] { TokenConstraint.None, TokenConstraint.None }).Tokens.Count);
    }

    /// <summary>
    ///     Proves that an empty glossary gives exactly the result of tagging without a glossary, for all three overloads.
    /// </summary>
    [Fact]
    public void Tagger_Tag_Glossary_EmptyGlossary_GivesTheResultWithoutGlossary()
    {
        // Arrange: a tagger, a text and the glossary without entries
        var tagger = Create();
        const string text = "The cat sat on the mat. She reads a book.";
        var tokens = new[] { "She", "reads", "a", "book", "." };

        // Act: tag with and without the empty glossary
        var plainText = tagger.Tag(text);
        var glossaryText = tagger.Tag(text, Glossary.Empty);
        var plainTokens = tagger.Tag(tokens);
        var glossaryTokens = tagger.Tag(tokens, Glossary.Empty);
        var glossarySentence = tagger.Tag(Tokenizer.CreateSentence(tokens), Glossary.Empty);

        // Assert: the same tags, confidences and alternatives, and no match anywhere
        Assert.Equal(Describe(plainText), Describe(glossaryText));
        Assert.Equal(Describe(new[] { plainTokens }), Describe(new[] { glossaryTokens }));
        Assert.Equal(Describe(new[] { plainTokens }), Describe(new[] { glossarySentence }));
        Assert.All(glossaryText, s => Assert.Empty(s.GlossaryMatches));
        Assert.All(glossaryTokens.Tokens, t => Assert.Null(t.GlossaryMatch));
    }

    /// <summary>
    ///     Proves that a match of the glossary is applied to the tags and reported on the tokens and on the sentence, for text input.
    /// </summary>
    [Fact]
    public void Tagger_Tag_Glossary_Match_IsAppliedAndReported()
    {
        // Arrange: a tagger and a glossary that says that "mat" is a verb in this domain
        var tagger = Create();
        var glossary = Glossary.Create(new[] { new GlossaryEntry("mat", Upos.Verb, "To cover a floor.") });
        const string text = "The cat sat on the mat. She reads a book.";

        // Act: tag the text
        var sentences = tagger.Tag(text, glossary);

        // Assert: the first sentence has one match on "mat", certain and reported at the token, the second has none
        var match = Assert.Single(sentences[0].GlossaryMatches);
        var token = sentences[0].Tokens[5];
        Assert.Equal("mat", token.Text);
        Assert.Equal(Upos.Verb, token.Tag);
        Assert.Equal(1.0, token.Confidence);
        Assert.True(token.IsConstrained);
        Assert.True(token.IsGlossaryHead);
        Assert.Same(match, token.GlossaryMatch);
        Assert.Equal("To cover a floor.", match.Entry.Definition);
        Assert.Equal(text.IndexOf("mat", StringComparison.Ordinal), match.Start);
        Assert.Empty(sentences[1].GlossaryMatches);
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
    ///     Creates a tagger from hand-set weights and a lexicon of the given rows.
    /// </summary>
    private static Tagger CreateHandSet(TinyModels.WeightsBuilder weights, string[] lexicon, int alternatives = 3) =>
        TinyModels.CreateTagger(weights.Build(), TinyModels.CreateLexicon(lexicon), new TaggerOptions { MaxAlternatives = alternatives });

    /// <summary>
    ///     Proves that a spelling of the lexicon is a known word and an unlisted spelling is not, for text input.
    /// </summary>
    [Fact]
    public void Tagger_Tag_KnownWord_LexiconSpelling_IsTrueAndUnlistedSpellingIsFalse()
    {
        // Arrange: a tagger whose lexicon lists cat and sat
        var tagger = CreateHandSet(new TinyModels.WeightsBuilder().Word("cat", Upos.Noun, 3), new[] { "cat NOUN", "sat VERB" });

        // Act: tag a text with two listed words, an unlisted word and a period
        var tokens = tagger.Tag("cat sat zorbulon.").Single().Tokens;

        // Assert: the flag follows the lexicon, also for the period that the lexicon does not list
        Assert.Equal(new[] { "cat", "sat", "zorbulon", "." }, tokens.Select(t => t.Text));
        Assert.Equal(new[] { true, true, false, false }, tokens.Select(t => t.IsKnownWord));
    }

    /// <summary>
    ///     Proves that the known-word flag uses the lower-cased lookup of the tagger for every letter case.
    /// </summary>
    /// <param name="spelling">The token text.</param>
    /// <param name="expected">The expected flag.</param>
    [Theory]
    [InlineData("cat", true)]
    [InlineData("Cat", true)]
    [InlineData("CAT", true)]
    [InlineData("Zorbulon", false)]
    public void Tagger_Tag_KnownWord_CaseVariant_UsesTheLexiconLookup(string spelling, bool expected)
    {
        // Arrange: a tagger whose lexicon lists cat in lower case
        var tagger = CreateHandSet(new TinyModels.WeightsBuilder().Word("cat", Upos.Noun, 3), new[] { "cat NOUN" });

        // Act: tag the spelling as a pre-tokenized token
        var token = tagger.Tag(new[] { spelling }).Tokens[0];

        // Assert: the flag follows the lookup, not the letter case
        Assert.Equal(expected, token.IsKnownWord);
    }

    /// <summary>
    ///     Proves that the known-word flag uses the full lookup form of the tagger: the spelling is normalized (NFC), typographic
    ///     apostrophes, dashes and the ellipsis are mapped to ASCII, and the result is lower-cased.
    /// </summary>
    /// <param name="spelling">The token text.</param>
    /// <param name="expected">The expected flag.</param>
    [Theory]
    [InlineData("n't", true)]
    [InlineData("n\u2019t", true)]
    [InlineData("N\u2019T", true)]
    [InlineData("\u2014", true)]
    [InlineData("\u2026", true)]
    [InlineData("caf\u00E9", true)]
    [InlineData("CAFE\u0301", true)]
    [InlineData("cafe", false)]
    public void Tagger_Tag_KnownWord_FoldedSpelling_UsesTheLexiconLookup(string spelling, bool expected)
    {
        // Arrange: a tagger whose lexicon lists the folded forms of the apostrophe, the dash, the ellipsis and an accented word in NFC
        var tagger = CreateHandSet(new TinyModels.WeightsBuilder(), new[] { "n't PART", "-- PUNCT", "... PUNCT", "caf\u00E9 NOUN" });

        // Act: tag the spelling as a pre-tokenized token
        var token = tagger.Tag(new[] { spelling }).Tokens[0];

        // Assert: the flag follows the folded, lower-cased lookup
        Assert.Equal(expected, token.IsKnownWord);
    }

    /// <summary>
    ///     Proves that the head of a hard glossary entry that the lexicon does not list is an unknown word that is constrained.
    /// </summary>
    [Fact]
    public void Tagger_Tag_KnownWord_GlossaryHeadNotInLexicon_IsUnknownButConstrained()
    {
        // Arrange: the lexicon lists manual but not override, and a hard entry makes override a noun
        var tagger = CreateHandSet(new TinyModels.WeightsBuilder(), new[] { "the DET", "manual ADJ NOUN", "is AUX VERB" });
        var glossary = Glossary.Create(new[] { new GlossaryEntry("Manual Override", Upos.Noun, "A mode of the unit.") });

        // Act: tag the sentence with the glossary
        var tokens = tagger.Tag(new[] { "the", "manual", "override", "is" }, glossary).Tokens;

        // Assert: the head is a certain, constrained noun that the lexicon does not know; its neighbor keeps its own flag
        var head = tokens[2];
        Assert.Equal("override", head.Text);
        Assert.False(head.IsKnownWord);
        Assert.True(head.IsConstrained);
        Assert.True(head.IsGlossaryHead);
        Assert.Equal(Upos.Noun, head.Tag);
        Assert.Equal(1.0, head.Confidence);
        Assert.True(tokens[1].IsKnownWord);
        Assert.False(tokens[1].IsGlossaryHead);
    }

    /// <summary>
    ///     Proves that the head of a hard glossary entry that the lexicon lists is a known word, and that a glossary changes no flag.
    /// </summary>
    [Fact]
    public void Tagger_Tag_KnownWord_GlossaryHeadInLexicon_IsKnownAndConstrained()
    {
        // Arrange: the lexicon lists schematic, and a hard entry makes it a noun
        var tagger = CreateHandSet(new TinyModels.WeightsBuilder(), new[] { "read VERB", "the DET", "wiring NOUN", "schematic ADJ NOUN" });
        var glossary = Glossary.Create(new[] { new GlossaryEntry("Wiring Schematic", Upos.Noun) });
        var words = new[] { "read", "the", "wiring", "schematic", "now" };

        // Act: tag with and without the glossary
        var plain = tagger.Tag(words).Tokens;
        var withGlossary = tagger.Tag(words, glossary).Tokens;

        // Assert: the head is known, constrained and a head; every flag equals that of the text tagged without the glossary
        var head = withGlossary[3];
        Assert.True(head.IsKnownWord);
        Assert.True(head.IsConstrained);
        Assert.True(head.IsGlossaryHead);
        Assert.Equal(plain.Select(t => t.IsKnownWord), withGlossary.Select(t => t.IsKnownWord));
        Assert.Equal(new[] { true, true, true, true, false }, withGlossary.Select(t => t.IsKnownWord));
    }

    /// <summary>
    ///     Proves that the margin of a token with several alternatives is the confidence minus the probability of the runner-up.
    /// </summary>
    /// <param name="alternatives">The number of requested alternatives.</param>
    [Theory]
    [InlineData(3)]
    [InlineData(16)]
    public void Tagger_Tag_Margin_SeveralAlternatives_IsConfidenceMinusRunnerUp(int alternatives)
    {
        // Arrange: cat has three allowed tags with the raw scores 3, 1 and 0 at the temperature 1
        var weights = new TinyModels.WeightsBuilder().Word("cat", Upos.Noun, 3).Word("cat", Upos.Verb, 1);
        var tagger = CreateHandSet(weights, new[] { "cat NOUN VERB ADJ" }, alternatives);

        // Act: tag the word
        var token = tagger.Tag(new[] { "cat" }).Tokens[0];

        // Assert: the margin is (1 - exp(-2)) / (1 + exp(-2) + exp(-3)) and equals the difference to the first alternative
        var sum = 1 + Math.Exp(-2) + Math.Exp(-3);
        Assert.Equal(Upos.Noun, token.Tag);
        Assert.Equal((1 - Math.Exp(-2)) / sum, token.Margin, 9);
        Assert.Equal(token.Confidence - token.Alternatives[0].Probability, token.Margin);
        Assert.Equal(Upos.Verb, token.Alternatives[0].Tag);
    }

    /// <summary>
    ///     Proves that one requested alternative is enough for the exact margin.
    /// </summary>
    [Fact]
    public void Tagger_Tag_Margin_OneAlternative_IsConfidenceMinusRunnerUp()
    {
        // Arrange: cat has three allowed tags, and two taggers report one and all alternatives
        var weights = new TinyModels.WeightsBuilder().Word("cat", Upos.Noun, 3).Word("cat", Upos.Verb, 1);
        var one = CreateHandSet(weights, new[] { "cat NOUN VERB ADJ" }, 1);
        var all = CreateHandSet(weights, new[] { "cat NOUN VERB ADJ" }, 16);

        // Act: tag the word with both
        var oneToken = one.Tag(new[] { "cat" }).Tokens[0];
        var allToken = all.Tag(new[] { "cat" }).Tokens[0];

        // Assert: one alternative is reported and the margin equals the one with all alternatives
        Assert.Single(oneToken.Alternatives);
        Assert.Equal(oneToken.Confidence - oneToken.Alternatives[0].Probability, oneToken.Margin);
        Assert.Equal(allToken.Margin, oneToken.Margin, 12);
    }

    /// <summary>
    ///     Proves that without alternatives the margin is the smallest value that the confidence allows.
    /// </summary>
    [Fact]
    public void Tagger_Tag_Margin_NoAlternativesRequested_IsTheSmallestMarginTheConfidenceAllows()
    {
        // Arrange: cat is clearly a noun (three allowed tags), tri is a three-way tie, and both taggers report no alternatives or all
        var weights = new TinyModels.WeightsBuilder()
            .Word("cat", Upos.Noun, 3)
            .Word("cat", Upos.Verb, 1)
            .Word("tri", Upos.Noun, 2)
            .Word("tri", Upos.Verb, 2)
            .Word("tri", Upos.Adj, 2);
        var lexicon = new[] { "cat NOUN VERB ADJ", "tri NOUN VERB ADJ" };
        var bare = CreateHandSet(weights, lexicon, 0);
        var full = CreateHandSet(weights, lexicon, 16);

        // Act: tag both words with both taggers
        var bareTokens = bare.Tag(new[] { "cat", "tri" }).Tokens;
        var fullTokens = full.Tag(new[] { "cat", "tri" }).Tokens;

        // Assert: the margin is 2c - 1 for a confidence above one half, 0 below it, and never above the exact margin
        Assert.Empty(bareTokens[0].Alternatives);
        Assert.Equal((2 * bareTokens[0].Confidence) - 1, bareTokens[0].Margin, 12);
        Assert.True(bareTokens[0].Margin <= fullTokens[0].Margin);
        Assert.True(bareTokens[0].Margin < fullTokens[0].Margin);
        Assert.True(bareTokens[1].Confidence < 0.5);
        Assert.Equal(0.0, bareTokens[1].Margin);
        Assert.Equal(0.0, fullTokens[1].Margin, 12);
    }

    /// <summary>
    ///     Proves that a token with one allowed tag has the margin 1 whatever the number of requested alternatives.
    /// </summary>
    /// <param name="alternatives">The number of requested alternatives.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void Tagger_Tag_Margin_SingleAllowedTag_IsOne(int alternatives)
    {
        // Arrange: the lexicon lists the as DET only and cat with two tags, and cat gets a hard constraint
        var tagger = CreateHandSet(new TinyModels.WeightsBuilder().Word("cat", Upos.Noun, 3).Word("cat", Upos.Verb, 1), new[] { "the DET", "cat NOUN VERB" }, alternatives);
        var constraints = new[] { TokenConstraint.None, TokenConstraint.Only(Upos.Verb), TokenConstraint.Only(Upos.Noun) };

        // Act: tag a listed single-tag word, a constrained word and a constrained unlisted word
        var tokens = tagger.Tag(new[] { "the", "cat", "zork" }, constraints).Tokens;

        // Assert: all three have one allowed tag, the confidence 1 and the margin 1
        Assert.All(tokens, t => Assert.Equal(1.0, t.Confidence));
        Assert.All(tokens, t => Assert.Equal(1.0, t.Margin));
    }

    /// <summary>
    ///     Proves that two tied tags give the margin 0, with and without alternatives.
    /// </summary>
    [Fact]
    public void Tagger_Tag_Margin_TiedTags_IsZero()
    {
        // Arrange: cat has two allowed tags with the same score
        var weights = new TinyModels.WeightsBuilder().Word("cat", Upos.Noun, 2).Word("cat", Upos.Verb, 2);
        var withAlternatives = CreateHandSet(weights, new[] { "cat NOUN VERB" }, 3);
        var without = CreateHandSet(weights, new[] { "cat NOUN VERB" }, 0);

        // Act: tag the word with both
        var first = withAlternatives.Tag(new[] { "cat" }).Tokens[0];
        var second = without.Tag(new[] { "cat" }).Tokens[0];

        // Assert: the margin is 0
        Assert.Equal(0.0, first.Margin, 12);
        Assert.Equal(0.5, first.Confidence, 12);
        Assert.Equal(0.0, second.Margin, 12);
    }

    /// <summary>
    ///     Proves that the default token has the margin 0 and is not a known word.
    /// </summary>
    [Fact]
    public void Tagger_Tag_DefaultToken_HasNoMarginAndIsNotKnown()
    {
        // Arrange: the default value of the struct
        TaggedToken token = default;

        // Act: read the new members
        var margin = token.Margin;
        var known = token.IsKnownWord;

        // Assert: zero and false
        Assert.Equal(0.0, margin);
        Assert.False(known);
    }

    /// <summary>
    ///     The analyses of a lookup result as text, one entry per analysis, for comparisons.
    /// </summary>
    private static string[] AnalysesOf(WordTags word) => word.GetAnalyses().Select(a => a.ToString()).ToArray();

    /// <summary>
    ///     Creates a tagger whose lexicon has analyses that cover some tags but not all, and non-ASCII and apostrophe base words.
    /// </summary>
    private static Tagger CreateWithAnalyses()
    {
        static KeyValuePair<string, UposSet> Entry(string spelling, params Upos[] tags) => new(spelling, UposSet.Of(tags));
        static KeyValuePair<string, IReadOnlyList<DemaConsulting.PartOfSpeech.Lexicon.Analysis>> Readings(string spelling, params DemaConsulting.PartOfSpeech.Lexicon.Analysis[] analyses) => new(spelling, analyses);

        var lexicon = TinyModels.CreateLexicon(
            new[]
            {
                Entry("cats", Upos.Noun),
                Entry("dogs", Upos.Noun, Upos.Verb),
                Entry("caf\u00E9s", Upos.Noun),
                Entry("o'clock", Upos.Noun),
                Entry("birds", Upos.Noun),
            },
            analyses: new[]
            {
                Readings("cats", new DemaConsulting.PartOfSpeech.Lexicon.Analysis("cat", Upos.Noun, "Number=Plur")),
                Readings("dogs", new DemaConsulting.PartOfSpeech.Lexicon.Analysis("dog", Upos.Noun, "Number=Plur")),
                Readings("caf\u00E9s", new DemaConsulting.PartOfSpeech.Lexicon.Analysis("caf\u00E9", Upos.Noun, "Number=Plur")),
                Readings("o'clock", new DemaConsulting.PartOfSpeech.Lexicon.Analysis("o'clock", Upos.Noun, "Number=Sing")),
            });
        return TinyModels.CreateTagger(Weights, lexicon);
    }

    /// <summary>
    ///     Proves that the lookup of a spelling with two forms returns both analyses in the documented order, with the features in
    ///     Universal Dependencies notation, using real data of the embedded lexicon.
    /// </summary>
    [Fact]
    public void Tagger_GetPossibleTags_Tests_ReturnsBothAnalysesInDocumentedOrder()
    {
        // Arrange: the embedded lexicon lists tests as the plural of a noun and the third person of a verb
        var tagger = Tagger.Default;

        // Act: look up the word and read its analyses
        var word = tagger.GetPossibleTags("tests");
        var analyses = word.GetAnalyses();

        // Assert: both tags, a spelling match, every tag explained, and the analyses in tag order with readable features
        Assert.Equal(WordMatch.Spelling, word.Match);
        Assert.True(word.IsKnownWord);
        Assert.Equal(UposSet.Of(Upos.Noun, Upos.Verb), word.Tags);
        Assert.True(word.TagsWithoutAnalysis.IsEmpty);
        Assert.Equal(new[] { "test NOUN Number=Plur", "test VERB Person=3|Tense=Pres" }, analyses.Select(a => a.ToString()));
        Assert.Equal("Plur", analyses[0].GetFeature("Number"));
        Assert.Equal("3", analyses[1].GetFeature("Person"));
        Assert.Equal("Pres", analyses[1].GetFeature("Tense"));
        Assert.Null(analyses[1].GetFeature("Number"));
        Assert.Null(analyses[0].GetFeature("number"));
    }

    /// <summary>
    ///     Proves that the letter case of the word never changes the result.
    /// </summary>
    /// <param name="spelling">The word in another case.</param>
    [Theory]
    [InlineData("TESTS")]
    [InlineData("Tests")]
    [InlineData("tESTS")]
    public void Tagger_GetPossibleTags_Case_IgnoresLetterCase(string spelling)
    {
        // Arrange: the lower-case result as the reference
        var tagger = Tagger.Default;
        var reference = tagger.GetPossibleTags("tests");

        // Act: look up the other case
        var word = tagger.GetPossibleTags(spelling);

        // Assert: the same tags, match and analyses (the result is equal in every respect)
        Assert.Equal(reference, word);
        Assert.True(reference == word);
        Assert.Equal(AnalysesOf(reference), AnalysesOf(word));
    }

    /// <summary>
    ///     Proves that an unknown word is reported as not recognized, which differs from a word without tags, in both modes.
    /// </summary>
    [Fact]
    public void Tagger_GetPossibleTags_UnknownWord_IsNotRecognizedNotEmptyKnown()
    {
        // Arrange: an invented word
        var tagger = Tagger.Default;

        // Act: look it up in both modes
        var raw = tagger.GetPossibleTags("zzqxv");
        var shape = tagger.GetPossibleTags("zzqxv", PossibleTagsMode.WithShapeRules);

        // Assert: raw mode has no tags and shape mode has all 17, but both say plainly that the word is not recognized
        Assert.Equal(WordMatch.NotRecognized, raw.Match);
        Assert.False(raw.IsKnownWord);
        Assert.True(raw.Tags.IsEmpty);
        Assert.Empty(raw.GetAnalyses());
        Assert.Equal(WordMatch.NotRecognized, shape.Match);
        Assert.False(shape.IsKnownWord);
        Assert.Equal(UposSet.All, shape.Tags);
        Assert.Equal(UposSet.All, shape.TagsWithoutAnalysis);
        Assert.Empty(shape.GetAnalyses());
        Assert.Equal(default, raw);
    }

    /// <summary>
    ///     Proves that the default mode is the raw lexicon lookup.
    /// </summary>
    [Fact]
    public void Tagger_GetPossibleTags_DefaultMode_IsLexiconOnly()
    {
        // Arrange: a capitalized word, whose shape adds tags in the other mode
        var tagger = Tagger.Default;

        // Act: look it up without a mode, with each mode
        var byDefault = tagger.GetPossibleTags("Valve");
        var raw = tagger.GetPossibleTags("Valve", PossibleTagsMode.LexiconOnly);
        var shape = tagger.GetPossibleTags("Valve", PossibleTagsMode.WithShapeRules);

        // Assert: the default equals the raw lookup and differs from the shape-rule lookup
        Assert.Equal(raw, byDefault);
        Assert.NotEqual(shape, byDefault);
        Assert.Equal(default, default(PossibleTagsMode));
        Assert.Equal(PossibleTagsMode.LexiconOnly, default);
    }

    /// <summary>
    ///     Proves that the shape rules add the tags of the decoder in the shape-rule mode and do not touch the raw mode.
    /// </summary>
    /// <param name="spelling">The word.</param>
    /// <param name="raw">The lexicon tags, as a tag string.</param>
    /// <param name="shape">The tags that the tagger allows, as a tag string.</param>
    [Theory]
    [InlineData("valve", "NOUN", "NOUN")]
    [InlineData("Valve", "NOUN", "NOUN|PROPN|X")]
    [InlineData("VALVE", "NOUN", "NOUN|PROPN|X")]
    [InlineData("vALVE", "NOUN", "NOUN")]
    [InlineData("1st", "ADJ", "ADJ|NOUN|NUM|PROPN|SYM|X")]
    [InlineData("--", "PUNCT", "PUNCT|SYM|X")]
    [InlineData("\u2014", "PUNCT", "PUNCT|SYM|X")]
    [InlineData("n\u2019t", "PART", "PART")]
    [InlineData("N\u2019T", "PART", "PART|PROPN|X")]
    public void Tagger_GetPossibleTags_ShapeRules_ExtendTheLexiconTags(string spelling, string raw, string shape)
    {
        // Arrange: the embedded lexicon, which lists these spellings
        var tagger = Tagger.Default;

        // Act: look the word up in both modes
        var lexiconOnly = tagger.GetPossibleTags(spelling, PossibleTagsMode.LexiconOnly);
        var withShape = tagger.GetPossibleTags(spelling, PossibleTagsMode.WithShapeRules);

        // Assert: the raw mode is the lexicon fact, the other mode adds what the shape of the token allows; both know the word
        Assert.Equal(raw, lexiconOnly.Tags.ToString());
        Assert.Equal(shape, withShape.Tags.ToString());
        Assert.True(lexiconOnly.IsKnownWord);
        Assert.True(withShape.IsKnownWord);
    }

    /// <summary>
    ///     Proves that typographic quotes, dashes, the ellipsis and decomposed accents are folded like a token of the tagger.
    /// </summary>
    /// <param name="spelling">The word as written.</param>
    /// <param name="expected">The lexicon tags, or an empty string for a spelling that is not listed.</param>
    [Theory]
    [InlineData("n't", "PART")]
    [InlineData("n\u2019t", "PART")]
    [InlineData("N\u2019T", "PART")]
    [InlineData("\u2014", "PUNCT")]
    [InlineData("\u2013", "PUNCT")]
    [InlineData("\u2026", "PUNCT")]
    [InlineData("caf\u00E9", "NOUN")]
    [InlineData("CAFE\u0301", "NOUN")]
    [InlineData("cafe", "NOUN|PROPN")]
    [InlineData("zzqx\u00E9", "")]
    public void Tagger_GetPossibleTags_FoldedSpelling_UsesTheTaggerLookup(string spelling, string expected)
    {
        // Arrange: the embedded lexicon lists n't, --, ..., café and cafe
        var tagger = Tagger.Default;

        // Act: look the spelling up
        var word = tagger.GetPossibleTags(spelling);

        // Assert: the folded, normalized, lower-cased spelling decides
        Assert.Equal(expected, word.Tags.ToString());
        Assert.Equal(expected.Length > 0, word.IsKnownWord);
    }

    /// <summary>
    ///     Proves that tags that no analysis names are reported as attested without an analysis: closed-class noise, rule-added tags
    ///     and proper nouns, using real data.
    /// </summary>
    [Fact]
    public void Tagger_GetPossibleTags_TagsWithoutAnalysis_ReportsRuleAndAttestedTags()
    {
        // Arrange: the embedded lexicon lists the as DET (an analysis) next to ADV, PRON and PROPN (none), testing as noun and verb forms
        // next to the adjective that a rule adds, ran as a verb form next to an adjective, and paris as a proper noun without any analysis
        var tagger = Tagger.Default;

        // Act: look the words up
        var the = tagger.GetPossibleTags("the");
        var testing = tagger.GetPossibleTags("testing");
        var ran = tagger.GetPossibleTags("ran");
        var paris = tagger.GetPossibleTags("paris");

        // Assert: the tags without analysis are exactly the tags that no analysis names
        Assert.Equal(UposSet.Of(Upos.Adv, Upos.Pron, Upos.Propn), the.TagsWithoutAnalysis);
        Assert.Equal(new[] { "the DET" }, AnalysesOf(the));
        Assert.Equal(UposSet.Of(Upos.Adj), testing.TagsWithoutAnalysis);
        Assert.Equal(new[] { "testing NOUN Number=Sing", "test VERB VerbForm=Ger" }, AnalysesOf(testing));
        Assert.Equal(UposSet.Of(Upos.Adj), ran.TagsWithoutAnalysis);
        Assert.Equal(new[] { "run VERB Tense=Past" }, AnalysesOf(ran));
        Assert.Equal(UposSet.Of(Upos.Propn), paris.TagsWithoutAnalysis);
        Assert.Empty(paris.GetAnalyses());
    }

    /// <summary>
    ///     Proves that the analyses carry the base word of closed-class and irregular forms from the data.
    /// </summary>
    [Fact]
    public void Tagger_GetPossibleTags_ClosedClassAndIrregularForms_HaveTheBaseWord()
    {
        // Arrange: the embedded lexicon
        var tagger = Tagger.Default;

        // Act: look up closed-class words and irregular forms
        var of = tagger.GetPossibleTags("of");
        var be = tagger.GetPossibleTags("be");
        var went = tagger.GetPossibleTags("went");
        var @is = tagger.GetPossibleTags("is");

        // Assert: a closed-class word is its own base word, the irregular forms point to their base word, and features may be empty
        Assert.Equal(new[] { "of ADP" }, AnalysesOf(of));
        Assert.Equal(new[] { "go VERB Tense=Past" }, AnalysesOf(went));
        Assert.Equal(
            new[] { "be AUX Person=3|Tense=Pres", "i NOUN Number=Plur", "be VERB Person=3|Tense=Pres" },
            AnalysesOf(@is));
        var aux = be.GetAnalyses().First(a => a.Tag == Upos.Aux);
        Assert.Equal(string.Empty, aux.Features);
        Assert.Null(aux.GetFeature("Number"));
        Assert.Equal("be", aux.Lemma);
    }

    /// <summary>
    ///     Proves that a lexicon built without an analyses table gives tags and matches but no analyses.
    /// </summary>
    [Fact]
    public void Tagger_GetPossibleTags_CustomLexiconWithoutAnalyses_HasNoAnalyses()
    {
        // Arrange: a lexicon that lists cat and has no analyses, and the embedded lexicon that lists cat differently
        var tagger = TinyModels.CreateTagger(Weights, TinyModels.CreateLexicon("cat ADJ VERB"));

        // Act: look the word up in the two modes and ask for a form
        var word = tagger.GetPossibleTags("cat");
        var shape = tagger.GetPossibleTags("Cat", PossibleTagsMode.WithShapeRules);

        // Assert: the custom lexicon decides, nothing is analyzed, and no tag is a form of anything
        Assert.Equal(UposSet.Of(Upos.Adj, Upos.Verb), word.Tags);
        Assert.Equal(WordMatch.Spelling, word.Match);
        Assert.Empty(word.GetAnalyses());
        Assert.Equal(word.Tags, word.TagsWithoutAnalysis);
        Assert.Equal(shape.Tags, shape.TagsWithoutAnalysis);
        Assert.False(tagger.IsFormOf("cat", "cat", Upos.Verb));
    }

    /// <summary>
    ///     Proves that a custom lexicon with analyses reports its own analyses and the tags that they do not cover.
    /// </summary>
    [Fact]
    public void Tagger_GetPossibleTags_CustomLexiconWithAnalyses_ReportsItsAnalyses()
    {
        // Arrange: a lexicon whose analysis of dogs covers the noun but not the verb
        var tagger = CreateWithAnalyses();

        // Act: look up cats and dogs
        var cats = tagger.GetPossibleTags("cats");
        var dogs = tagger.GetPossibleTags("Dogs");
        var birds = tagger.GetPossibleTags("birds");

        // Assert: the analyses of the custom lexicon, the verb attested without one, and a spelling without any analysis
        Assert.Equal(new[] { "cat NOUN Number=Plur" }, AnalysesOf(cats));
        Assert.True(cats.TagsWithoutAnalysis.IsEmpty);
        Assert.Equal(new[] { "dog NOUN Number=Plur" }, AnalysesOf(dogs));
        Assert.Equal(UposSet.Of(Upos.Verb), dogs.TagsWithoutAnalysis);
        Assert.Empty(birds.GetAnalyses());
        Assert.Equal(UposSet.Of(Upos.Noun), birds.TagsWithoutAnalysis);
        Assert.True(tagger.IsFormOf("DOGS", "Dog", Upos.Noun));
        Assert.False(tagger.IsFormOf("dogs", "dog", Upos.Verb));
    }

    /// <summary>
    ///     Proves that the lookup of the embedded lexicon and the lookup of another tagger do not mix their lexicons.
    /// </summary>
    [Fact]
    public void Tagger_GetPossibleTags_CustomLexicon_UsesItsOwnLexicon()
    {
        // Arrange: a lexicon that allows only ADJ for valve, next to the embedded lexicon that allows NOUN
        var custom = TinyModels.CreateTagger(Weights, TinyModels.CreateLexicon("valve ADJ"));

        // Act: look the word up with each tagger, alternating on this thread
        var withCustom = custom.GetPossibleTags("valve");
        var withDefault = Tagger.Default.GetPossibleTags("valve");
        var withCustomAgain = custom.GetPossibleTags("valve");

        // Assert: each tagger answers from its own lexicon
        Assert.Equal(UposSet.Of(Upos.Adj), withCustom.Tags);
        Assert.Equal(UposSet.Of(Upos.Noun), withDefault.Tags);
        Assert.Equal(withCustom, withCustomAgain);
        Assert.NotEqual(withCustom, withDefault);
    }

    /// <summary>
    ///     Proves that a glossary changes the tags of a token in a sentence but never the lookup of the word.
    /// </summary>
    [Fact]
    public void Tagger_GetPossibleTags_Glossary_DoesNotChangeTheResult()
    {
        // Arrange: a glossary that forces valve to be a verb, and the lookup before tagging
        var tagger = Tagger.Default;
        var glossary = Glossary.Create(new[] { new GlossaryEntry("valve", Upos.Verb) });
        var before = tagger.GetPossibleTags("valve", PossibleTagsMode.WithShapeRules);

        // Act: tag a sentence with the glossary, then look the word up again
        var token = tagger.Tag("Close the valve.", glossary).Single().Tokens[2];
        var after = tagger.GetPossibleTags("valve", PossibleTagsMode.WithShapeRules);

        // Assert: the glossary decided the token, and the lookup is still the lexicon fact
        Assert.Equal(Upos.Verb, token.Tag);
        Assert.True(token.IsConstrained);
        Assert.Equal(UposSet.Of(Upos.Noun), after.Tags);
        Assert.Equal(before, after);
    }

    /// <summary>
    ///     Proves the compound rule for a hyphenated word that is not a spelling but whose last part is: the head's analyses, and the
    ///     tags of the whole token.
    /// </summary>
    [Fact]
    public void Tagger_GetPossibleTags_CompoundHead_UsesLastPartAndTagsStayTheWholeToken()
    {
        // Arrange: well-tested is not a spelling of the embedded lexicon, tested is
        var tagger = Tagger.Default;

        // Act: look it up in both modes
        var raw = tagger.GetPossibleTags("well-tested");
        var shape = tagger.GetPossibleTags("well-tested", PossibleTagsMode.WithShapeRules);

        // Assert: the head adds analyses, but the tags are those of the whole token (as for an unknown word of the tagger)
        Assert.Equal(WordMatch.CompoundHead, raw.Match);
        Assert.False(raw.IsKnownWord);
        Assert.True(raw.Tags.IsEmpty);
        Assert.Equal(new[] { "tested ADJ Degree=Pos", "test VERB Tense=Past|VerbForm=Part" }, AnalysesOf(raw));
        Assert.True(raw.TagsWithoutAnalysis.IsEmpty);
        Assert.Equal(WordMatch.CompoundHead, shape.Match);
        Assert.False(shape.IsKnownWord);
        Assert.Equal(UposSet.All, shape.Tags);
        Assert.Equal(15, shape.TagsWithoutAnalysis.Count);
        Assert.False(shape.TagsWithoutAnalysis.Contains(Upos.Adj));
        Assert.False(shape.TagsWithoutAnalysis.Contains(Upos.Verb));
        Assert.Equal(AnalysesOf(raw), AnalysesOf(shape));
    }

    /// <summary>
    ///     Proves which hyphenated words have the last part as head and which analyses it gives.
    /// </summary>
    /// <param name="word">The word as written.</param>
    /// <param name="expected">The analyses of the head, joined by a semicolon.</param>
    [Theory]
    [InlineData("pre-flight", "flight NOUN Number=Sing; flight VERB VerbForm=Inf")]
    [InlineData("WELL-Tested", "tested ADJ Degree=Pos; test VERB Tense=Past|VerbForm=Part")]
    [InlineData("very-state-of-the-art", "art NOUN Number=Sing; art VERB VerbForm=Inf")]
    [InlineData("zzqx-flight", "flight NOUN Number=Sing; flight VERB VerbForm=Inf")]
    [InlineData("state-of-the-art-style", "style NOUN Number=Sing; style VERB VerbForm=Inf")]
    public void Tagger_GetPossibleTags_CompoundHead_HeadIsTheLastPartOnly(string word, string expected)
    {
        // Arrange: the embedded lexicon, which lists the last part of each word but not the word itself
        var tagger = Tagger.Default;

        // Act: look the word up
        var result = tagger.GetPossibleTags(word);

        // Assert: a compound head with the analyses of the last part only (nothing is split further or looked up recursively)
        Assert.Equal(WordMatch.CompoundHead, result.Match);
        Assert.Equal(expected, string.Join("; ", AnalysesOf(result)));
    }

    /// <summary>
    ///     Proves that a hyphenated word whose whole spelling is listed is a spelling with its own analyses, never the head's.
    /// </summary>
    [Fact]
    public void Tagger_GetPossibleTags_CompoundWhole_PrefersTheSpelling()
    {
        // Arrange: built-in and user-friendly are spellings, and so are their last parts in and friendly
        var tagger = Tagger.Default;

        // Act: look the words up in both modes
        var builtIn = tagger.GetPossibleTags("built-in");
        var friendly = tagger.GetPossibleTags("User-Friendly", PossibleTagsMode.WithShapeRules);

        // Assert: the whole spelling wins: its tags, its match, its analyses
        Assert.Equal(WordMatch.Spelling, builtIn.Match);
        Assert.True(builtIn.IsKnownWord);
        Assert.Equal(UposSet.Of(Upos.Adj), builtIn.Tags);
        Assert.Equal(new[] { "built-in ADJ Degree=Pos" }, AnalysesOf(builtIn));
        Assert.Equal(WordMatch.Spelling, friendly.Match);
        Assert.Equal("ADJ|PROPN|X", friendly.Tags.ToString());
    }

    /// <summary>
    ///     Proves that words that are not hyphenated compounds, or whose last part is unknown, are not recognized.
    /// </summary>
    /// <param name="word">The word as written.</param>
    [Theory]
    [InlineData("-ed")]
    [InlineData("well-")]
    [InlineData("a--b")]
    [InlineData("well--tested")]
    [InlineData("-well-tested")]
    [InlineData("well-tested-")]
    [InlineData("well-zzqx")]
    [InlineData("zzqx-well-zzqx")]
    [InlineData("well\u2014tested")]
    [InlineData("well\u2013tested")]
    [InlineData("well\u2010tested")]
    [InlineData("well\u2011tested")]
    public void Tagger_GetPossibleTags_CompoundHead_Malformed_IsNotRecognized(string word)
    {
        // Arrange: the embedded lexicon lists ed, well and tested but not these words
        var tagger = Tagger.Default;

        // Act: look the word up in both modes
        var raw = tagger.GetPossibleTags(word);
        var shape = tagger.GetPossibleTags(word, PossibleTagsMode.WithShapeRules);

        // Assert: not recognized, no analyses (a dash character is folded to two hyphens, and a typographic hyphen is not a hyphen-minus)
        Assert.Equal(WordMatch.NotRecognized, raw.Match);
        Assert.Equal(WordMatch.NotRecognized, shape.Match);
        Assert.Empty(raw.GetAnalyses());
        Assert.Empty(shape.GetAnalyses());
    }

    /// <summary>
    ///     Proves that a word longer than the longest spelling is recognized only through the compound rule: a hyphenated word whose last
    ///     part is a spelling has that head, however long the whole word is, and the limit is the length of the longest spelling.
    /// </summary>
    [Fact]
    public void Tagger_GetPossibleTags_CompoundHead_TooLongWhole_UsesTheLastPart()
    {
        // Arrange: the longest spelling of the embedded lexicon, words that fit and words that are one character and a million characters too long
        var tagger = Tagger.Default;
        var longest = EmbeddedModel.Lexicon.MaxSpellingBytes;
        var fits = new string('a', longest - "-tested".Length) + "-tested";
        var tooLong = new string('a', longest - "-tested".Length + 1) + "-tested";
        var huge = new string('a', 1_000_000) + "-tested";
        var head = tagger.GetPossibleTags("tested");

        // Act: look them up in both modes and ask for a form
        var results = new[] { fits, tooLong, huge }
            .Select(w => (Raw: tagger.GetPossibleTags(w), Shape: tagger.GetPossibleTags(w, PossibleTagsMode.WithShapeRules), Form: tagger.IsFormOf(w, "test", Upos.Verb)))
            .ToList();

        // Assert: the limit is 122 bytes; whether or not the whole word fits, the head is the last part and the tags are those of the whole word
        Assert.Equal(122, longest);
        Assert.All(
            results,
            r =>
            {
                Assert.Equal(WordMatch.CompoundHead, r.Raw.Match);
                Assert.Equal(WordMatch.CompoundHead, r.Shape.Match);
                Assert.False(r.Raw.IsKnownWord);
                Assert.True(r.Raw.Tags.IsEmpty);
                Assert.Equal(UposSet.All, r.Shape.Tags);
                Assert.Equal(AnalysesOf(head), AnalysesOf(r.Raw));
                Assert.NotEmpty(r.Raw.GetAnalyses());
                Assert.True(r.Form);
            });
    }

    /// <summary>
    ///     Proves that a word longer than the longest spelling is not recognized when it is not a hyphenated compound with a known last part
    ///     of a length that fits: no hyphen, an empty part, a dash character, an unknown last part, or a last part that is itself too long.
    /// </summary>
    [Fact]
    public void Tagger_GetPossibleTags_CompoundHead_TooLongWhole_Otherwise_IsNotRecognized()
    {
        // Arrange: words that are longer than the longest spelling and fail the compound rule in different ways
        var tagger = Tagger.Default;
        var longest = EmbeddedModel.Lexicon.MaxSpellingBytes;
        var prefix = new string('a', longest + 1);
        var words = new[]
        {
            prefix,
            prefix + "tested",
            prefix + "-zzqxv",
            prefix + "-" + new string('a', longest + 1),
            prefix + "-" + new string('a', longest - 1) + "-",
            "-" + prefix + "-tested",
            prefix + "--tested",
            prefix + "-",
            prefix + "\u2013tested",
            prefix + "-\u2014tested",
            prefix + "\u2010tested",
        };

        // Act: look each up in both modes and ask for a form
        var results = words
            .Select(w => (Raw: tagger.GetPossibleTags(w), Shape: tagger.GetPossibleTags(w, PossibleTagsMode.WithShapeRules), Form: tagger.IsFormOf(w, "test", Upos.Verb)))
            .ToList();

        // Assert: nothing is recognized, nothing is analyzed, and the tags are those of any word that is not a spelling
        Assert.All(
            results,
            r =>
            {
                Assert.Equal(WordMatch.NotRecognized, r.Raw.Match);
                Assert.Equal(WordMatch.NotRecognized, r.Shape.Match);
                Assert.True(r.Raw.Tags.IsEmpty);
                Assert.Equal(UposSet.All, r.Shape.Tags);
                Assert.Empty(r.Raw.GetAnalyses());
                Assert.False(r.Form);
            });
    }

    /// <summary>
    ///     Proves that the answer for a word that is longer than the longest spelling is the answer that the same word gets from a lexicon
    ///     whose longest spelling is long enough to look the whole word up: the over-long path applies the compound rule exactly as the
    ///     normal path does, for words made of every kind of piece (hyphens, dash characters, ellipsis, case, accents).
    /// </summary>
    [Fact]
    public void Tagger_GetPossibleTags_TooLongWhole_EqualsTheAnswerOfTheNormalPath()
    {
        // Arrange: two lexicons with the same spellings; the second also lists a spelling of 200 characters, so no word below is too long for it
        static KeyValuePair<string, UposSet> Entry(string spelling, params Upos[] tags) => new(spelling, UposSet.Of(tags));
        static KeyValuePair<string, IReadOnlyList<DemaConsulting.PartOfSpeech.Lexicon.Analysis>> Readings(string spelling, params DemaConsulting.PartOfSpeech.Lexicon.Analysis[] analyses) => new(spelling, analyses);

        var entries = new List<KeyValuePair<string, UposSet>>
        {
            Entry("ab", Upos.Noun),
            Entry("well", Upos.Adv, Upos.Noun),
            Entry("tested", Upos.Adj, Upos.Verb),
            Entry("a-b", Upos.Noun),
        };
        var analyses = new[]
        {
            Readings("tested", new DemaConsulting.PartOfSpeech.Lexicon.Analysis("tested", Upos.Adj, "Degree=Pos"), new DemaConsulting.PartOfSpeech.Lexicon.Analysis("test", Upos.Verb, "Tense=Past|VerbForm=Part")),
            Readings("well", new DemaConsulting.PartOfSpeech.Lexicon.Analysis("well", Upos.Noun, "Number=Sing")),
        };
        var shortLexicon = TinyModels.CreateTagger(Weights, TinyModels.CreateLexicon(entries, analyses: analyses));
        var withLong = new List<KeyValuePair<string, UposSet>>(entries) { Entry(new string('q', 200), Upos.Noun) };
        var longLexicon = TinyModels.CreateTagger(Weights, TinyModels.CreateLexicon(withLong, analyses: analyses));
        var pieces = new[] { "ab", "well", "tested", "zz", "-", "--", "\u2013", "\u2026", "AB", "Well", "\u00E9", "\u2010" };
        var words = new List<string>();
        void Add(string prefix, int depth)
        {
            if (prefix.Length > 0)
            {
                words.Add(prefix);
            }

            if (depth == 0)
            {
                return;
            }

            foreach (var piece in pieces)
            {
                Add(prefix + piece, depth - 1);
            }
        }

        Add(string.Empty, 4);
        var plain = words.Where(w => w.Length <= 12).ToList();
        words.AddRange(plain.SelectMany(w => new[] { "aaaaaaaa-" + w, "aaaaaaaa" + w, "aaaaaaaa-well-" + w }));

        // Act / Assert: every word gets the same result and the same answers from both lexicons, in both modes
        var overlong = 0;
        foreach (var word in words)
        {
            // The longest spelling of the short lexicon is tested, six bytes
            if (word.Length > 6)
            {
                overlong++;
            }

            foreach (var mode in new[] { PossibleTagsMode.LexiconOnly, PossibleTagsMode.WithShapeRules })
            {
                var expected = longLexicon.GetPossibleTags(word, mode);
                var actual = shortLexicon.GetPossibleTags(word, mode);
                Assert.True(expected.Match == actual.Match, $"{word}: {expected.Match} / {actual.Match}");
                Assert.True(expected.Tags == actual.Tags, $"{word}: tags");
                Assert.True(expected.TagsWithoutAnalysis == actual.TagsWithoutAnalysis, $"{word}: tags without analysis");
                Assert.True(AnalysesOf(expected).SequenceEqual(AnalysesOf(actual)), $"{word}: analyses");
            }

            Assert.True(longLexicon.IsFormOf(word, "test", Upos.Verb) == shortLexicon.IsFormOf(word, "test", Upos.Verb), $"{word}: IsFormOf");
            Assert.True(longLexicon.IsFormOf(word, "well", Upos.Noun) == shortLexicon.IsFormOf(word, "well", Upos.Noun), $"{word}: IsFormOf well");
        }

        Assert.True(overlong > 10_000);
    }

    /// <summary>
    ///     Proves that a very long word is not recognized without an error, for text with and without non-ASCII characters, and that
    ///     the tagger still works afterward.
    /// </summary>
    [Fact]
    public void Tagger_GetPossibleTags_VeryLongWord_IsNotRecognizedWithoutError()
    {
        // Arrange: a million ASCII characters, a million non-ASCII characters, and a hyphenated word of a million characters
        var tagger = Tagger.Default;
        var ascii = new string('a', 1_000_000);
        var accented = new string('\u00E9', 1_000_000);
        var hyphenated = new string('a', 500_000) + "-" + new string('b', 499_999);

        // Act: look them up in both modes, ask for a form, and look up an ordinary word afterward
        var results = new[] { ascii, accented, hyphenated }
            .SelectMany(w => new[] { tagger.GetPossibleTags(w), tagger.GetPossibleTags(w, PossibleTagsMode.WithShapeRules) })
            .ToList();
        var form = tagger.IsFormOf(ascii, "a", Upos.Noun) || tagger.IsFormOf("tests", ascii, Upos.Noun);
        var after = tagger.GetPossibleTags("valve");

        // Assert: nothing is recognized, nothing throws, and the next call is not affected
        Assert.All(results, r => Assert.Equal(WordMatch.NotRecognized, r.Match));
        Assert.False(form);
        Assert.Equal(UposSet.Of(Upos.Noun), after.Tags);
    }

    /// <summary>
    ///     Proves that the memory the word lookup uses is bounded by the longest spelling and not by the word: once a word with as
    ///     many characters as the longest spelling has bytes has been folded (the most that is ever folded, whatever its characters are:
    ///     they can take two UTF-8 bytes each, so that the word cannot be a spelling, and an ellipsis folds to three characters), no word of
    ///     any length makes the character buffer of the thread's workspace grow further.
    /// </summary>
    [Fact]
    public void Tagger_GetPossibleTags_WordsWithManyCharacters_NeverGrowTheWorkspaceBeyondTheLongestSpelling()
    {
        // Arrange: words that are not spellings; the first has 100 characters of 2 bytes each (200 bytes), the next ones have as many characters as the
        // longest spelling has bytes (the ellipsis and the em dash fold to three and two characters), one more, and a million characters
        var tagger = Tagger.Default;
        var longest = EmbeddedModel.Lexicon.MaxSpellingBytes;
        var words = new[]
        {
            new string('\u00E9', 100), new string('\u00E9', longest), new string('\u2014', longest), new string('\u2026', longest),
            new string('\u00E9', longest + 1), new string('\u2026', longest + 1), new string('\u00E9', 1_000_000), new string('\u2026', 1_000_000),
            new string('\u2014', 1_000_000), new string('a', 1_000_000)
        };
        var matches = new List<WordMatch>();
        var forms = new List<bool>();
        var capacities = new List<int>();

        // Act: on a new thread (so the workspace is new), fold the words that need the most (an ellipsis for every byte of the longest spelling, as a
        // word and as the word of IsFormOf, which folds a second token), then look up every word in both modes and as IsFormOf and read the buffer size
        var thread = new Thread(() =>
        {
            static int Capacity() => TagDecoder.CurrentThreadWorkspace!.Chars.Length;
            _ = tagger.GetPossibleTags(new string('\u2026', longest), PossibleTagsMode.WithShapeRules);
            _ = tagger.IsFormOf(new string('\u2026', longest), "test", Upos.Verb);
            capacities.Add(Capacity());
            foreach (var word in words)
            {
                matches.Add(tagger.GetPossibleTags(word).Match);
                matches.Add(tagger.GetPossibleTags(word, PossibleTagsMode.WithShapeRules).Match);
                forms.Add(tagger.IsFormOf(word, "test", Upos.Verb));
                capacities.Add(Capacity());
            }
        });
        thread.Start();
        thread.Join();

        // Assert: nothing is recognized; the buffer holds what the folded word needs (6 characters of room per character, two tokens) and never grew again
        Assert.Equal(words.Length * 2, matches.Count);
        Assert.All(matches, m => Assert.Equal(WordMatch.NotRecognized, m));
        Assert.All(forms, Assert.False);
        Assert.InRange(capacities[0], 6 * longest, 16 * longest);
        Assert.All(capacities, capacity => Assert.Equal(capacities[0], capacity));
    }
    /// <summary>
    ///     Proves that no public tagging entry point leaves text of its request in the thread's decoder or glossary workspace, including a
    ///     shorter request after a longer one.
    /// </summary>
    [Fact]
    public void Tagger_EveryEntryPoint_LeavesNoRequestTextInTheWorkspaces()
    {
        // Arrange: a tagger, a glossary with a term of the request, a long distinctive text and a short one
        var tagger = Tagger.Default;
        var glossary = Glossary.Create(new[] { new GlossaryEntry("zqxjvkwbplm valve", Upos.Noun) });
        const string longText = "Close the Zqxjvkwbplm valve and the Qzvxkjbwmping Xjqzkvwbpl now. Then wait for Wkvbzqxjpl.";
        var sentence = Tokenizer.Tokenize(longText)[0];
        var tokens = new[] { "Close", "the", "Zqxjvkwbplm", "valve", "Qzvxkjbwmping", "." };
        var constraints = tokens.Select(_ => TokenConstraint.None).ToArray();

        // Act / Assert: after every call the workspaces hold nothing
        void AssertErased()
        {
            var decoder = TagDecoder.CurrentThreadWorkspace;
            Assert.NotNull(decoder);
            Assert.Equal(0, decoder.Chars.Count(c => c != '\0'));
            Assert.Equal(0, decoder.LowerHash.Count(h => h != 0));
            Assert.Equal(0, decoder.LexiconScratch.Count(b => b != 0));
            Assert.Equal(0, decoder.LemmaBytes.Count(b => b != 0));
            var glossaryWorkspace = GlossaryIndex.GetThreadWorkspace();
            Assert.Equal(0, glossaryWorkspace.Chars.Count(c => c != '\0'));
            Assert.Equal(0, glossaryWorkspace.Hash.Count(h => h != 0));
        }

        Assert.NotEmpty(tagger.Tag(longText));
        AssertErased();
        Assert.NotEmpty(tagger.Tag(longText, glossary));
        AssertErased();
        tagger.Tag(sentence);
        AssertErased();
        tagger.Tag(sentence, glossary);
        AssertErased();
        tagger.Tag(tokens, constraints);
        AssertErased();
        tagger.Tag(tokens, glossary, constraints);
        AssertErased();
        tagger.GetPossibleTags("Zqxjvkwbplm");
        AssertErased();
        tagger.IsFormOf("Zqxjvkwbplm", "Qzvxkjbwmp", Upos.Noun);
        AssertErased();
        tagger.Tag("Hi.");
        AssertErased();
    }

    /// <summary>
    ///     Proves that an unpaired surrogate does not throw and is looked up as U+FFFD, like a token of the tagger.
    /// </summary>
    [Fact]
    public void Tagger_GetPossibleTags_UnpairedSurrogate_DoesNotThrow()
    {
        // Arrange: a word with a lone surrogate and its replacement twin
        var tagger = Tagger.Default;

        // Act: look both up in both modes
        var lone = tagger.GetPossibleTags("a\uD800b", PossibleTagsMode.WithShapeRules);
        var twin = tagger.GetPossibleTags("a\uFFFDb", PossibleTagsMode.WithShapeRules);

        // Assert: the same result
        Assert.Equal(twin, lone);
    }

    /// <summary>
    ///     Proves that a null word is rejected by every overload that takes a string.
    /// </summary>
    [Fact]
    public void Tagger_GetPossibleTags_NullWord_Throws()
    {
        // Arrange / Act / Assert: null for the word and for the base word
        var tagger = Tagger.Default;
        Assert.Throws<ArgumentNullException>(() => tagger.GetPossibleTags((string)null!));
        Assert.Throws<ArgumentNullException>(() => tagger.IsFormOf(null!, "test", Upos.Verb));
        Assert.Throws<ArgumentNullException>(() => tagger.IsFormOf("tests", null!, Upos.Verb));
    }

    /// <summary>
    ///     Proves that an empty word, a word with white space and a phrase are rejected, not answered as unknown.
    /// </summary>
    /// <param name="word">The invalid word.</param>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\u00A0")]
    [InlineData("ice cream")]
    [InlineData("a\nb")]
    [InlineData("a\u2003b")]
    public void Tagger_GetPossibleTags_EmptyOrWhiteSpace_Throws(string word)
    {
        // Arrange / Act / Assert: every lookup member refuses it, in both modes
        var tagger = Tagger.Default;
        Assert.Throws<ArgumentException>(() => tagger.GetPossibleTags(word));
        Assert.Throws<ArgumentException>(() => tagger.GetPossibleTags(word, PossibleTagsMode.WithShapeRules));
        Assert.Throws<ArgumentException>(() => tagger.IsFormOf(word, "test", Upos.Verb));
        Assert.Throws<ArgumentException>(() => tagger.IsFormOf("tests", word, Upos.Verb));
    }

    /// <summary>
    ///     Proves that the lookup refuses exactly the words that the tokenizer refuses as a token, for every UTF-16 code unit.
    /// </summary>
    [Fact]
    public void Tagger_GetPossibleTags_WhiteSpaceRule_EqualsTokenizerRule()
    {
        // Arrange: a tagger and a word for every code unit
        var tagger = Create();
        var mismatches = new List<int>();

        // Act: compare the two rules
        for (var code = 0; code <= 0xFFFF; code++)
        {
            var word = "a" + (char)code;
            var tokenizerRefuses = false;
            try
            {
                Tokenizer.CreateSentence(new[] { word });
            }
            catch (ArgumentException)
            {
                tokenizerRefuses = true;
            }

            var lookupRefuses = false;
            try
            {
                tagger.GetPossibleTags(word);
            }
            catch (ArgumentException)
            {
                lookupRefuses = true;
            }

            if (tokenizerRefuses != lookupRefuses)
            {
                mismatches.Add(code);
            }
        }

        // Assert: no code unit is treated differently
        Assert.Empty(mismatches);
    }

    /// <summary>
    ///     Proves that an undefined mode or tag is rejected.
    /// </summary>
    [Fact]
    public void Tagger_GetPossibleTags_UndefinedModeOrTag_Throws()
    {
        // Arrange / Act / Assert: values that were cast from a number
        var tagger = Tagger.Default;
        Assert.Throws<ArgumentOutOfRangeException>(() => tagger.GetPossibleTags("valve", (PossibleTagsMode)2));
        Assert.Throws<ArgumentOutOfRangeException>(() => tagger.GetPossibleTags("valve", unchecked((PossibleTagsMode)(-1))));
        Assert.Throws<ArgumentOutOfRangeException>(() => tagger.IsFormOf("tests", "test", (Upos)17));
        Assert.Throws<ArgumentOutOfRangeException>(() => tagger.IsFormOf("tests", "test", unchecked((Upos)(-1))));
    }

    /// <summary>
    ///     Proves that the form question is answered from the analyses, with the folding of the tagger, for true and false cases.
    /// </summary>
    /// <param name="word">The word.</param>
    /// <param name="lemma">The base word.</param>
    /// <param name="tag">The tag.</param>
    /// <param name="expected">The expected answer.</param>
    [Theory]
    [InlineData("tests", "test", Upos.Verb, true)]
    [InlineData("tests", "test", Upos.Noun, true)]
    [InlineData("TESTS", "Test", Upos.Noun, true)]
    [InlineData("went", "go", Upos.Verb, true)]
    [InlineData("is", "be", Upos.Aux, true)]
    [InlineData("tested", "tested", Upos.Adj, true)]
    [InlineData("tested", "test", Upos.Verb, true)]
    [InlineData("\u2019hood", "'hood", Upos.Noun, true)]
    [InlineData("'HOOD", "\u2019Hood", Upos.Noun, true)]
    [InlineData("well-tested", "test", Upos.Verb, true)]
    [InlineData("WELL-Tested", "TEST", Upos.Verb, true)]
    [InlineData("tests", "test", Upos.Adj, false)]
    [InlineData("tests", "tests", Upos.Noun, false)]
    [InlineData("tests", "testing", Upos.Verb, false)]
    [InlineData("tests", "tes", Upos.Verb, false)]
    [InlineData("went", "go", Upos.Noun, false)]
    [InlineData("zzqxv", "zzqxv", Upos.Noun, false)]
    [InlineData("paris", "paris", Upos.Propn, false)]
    [InlineData("the", "the", Upos.Adv, false)]
    [InlineData("the", "the", Upos.Det, true)]
    [InlineData("built-in", "in", Upos.Adp, false)]
    [InlineData("built-in", "built-in", Upos.Adj, true)]
    [InlineData("well-zzqx", "well", Upos.Adv, false)]
    [InlineData("a--b", "b", Upos.Noun, false)]
    public void Tagger_IsFormOf_Analyses_AnswerTheQuestion(string word, string lemma, Upos tag, bool expected)
    {
        // Arrange: the embedded lexicon
        var tagger = Tagger.Default;

        // Act: ask
        var result = tagger.IsFormOf(word, lemma, tag);

        // Assert: true exactly when an analysis of the word (or of its compound head) has this base word and tag
        Assert.Equal(expected, result);
    }

    /// <summary>
    ///     Proves that the form question agrees with the analyses for every analysis of a sample of words, and that a decomposed
    ///     non-ASCII word and base word are normalized.
    /// </summary>
    [Fact]
    public void Tagger_IsFormOf_EveryAnalysisOfSampleWords_IsAForm()
    {
        // Arrange: words with analyses from the embedded lexicon, and a custom lexicon with a non-ASCII base word
        var tagger = Tagger.Default;
        var words = new[] { "tests", "is", "went", "testing", "tested", "flights", "valves", "well", "known", "WELL-Tested", "pre-flight" };
        var custom = CreateWithAnalyses();

        // Act / Assert: every analysis is a form of its own base word and tag, and of no other tag of the same word
        foreach (var word in words)
        {
            foreach (var analysis in tagger.GetPossibleTags(word).GetAnalyses())
            {
                Assert.True(tagger.IsFormOf(word, analysis.Lemma, analysis.Tag), $"{word} {analysis}");
            }
        }

        Assert.True(custom.IsFormOf("CAFE\u0301S", "cafe\u0301", Upos.Noun));
        Assert.True(custom.IsFormOf("caf\u00E9s", "CAF\u00C9", Upos.Noun));
        Assert.True(custom.IsFormOf("O\u2019Clock", "o'clock", Upos.Noun));
        Assert.False(custom.IsFormOf("caf\u00E9s", "cafe", Upos.Noun));
    }

    /// <summary>
    ///     Proves that a base word that a lower-case custom lexicon stores is returned as it is and is matched by the form question
    ///     whatever the case of the question, while a base word with a capital cannot be compiled into such a lexicon.
    /// </summary>
    [Fact]
    public void Tagger_IsFormOf_LowerCaseCustomBaseWord_MatchesAndUpperCaseCannotBeStored()
    {
        // Arrange: a custom lexicon with the base word run for running, and an attempt to store Run
        static KeyValuePair<string, IReadOnlyList<DemaConsulting.PartOfSpeech.Lexicon.Analysis>> Readings(string spelling, params DemaConsulting.PartOfSpeech.Lexicon.Analysis[] analyses) => new(spelling, analyses);
        var entries = new[] { new KeyValuePair<string, UposSet>("running", UposSet.Of(Upos.Verb)) };
        var lexicon = TinyModels.CreateLexicon(entries, analyses: new[] { Readings("running", new DemaConsulting.PartOfSpeech.Lexicon.Analysis("run", Upos.Verb, "VerbForm=Ger")) });
        var tagger = TinyModels.CreateTagger(Weights, lexicon);

        // Act: ask for the analyses and for the form in two cases
        var analyses = tagger.GetPossibleTags("Running").GetAnalyses();
        var refused = Assert.Throws<ArgumentException>(
            () => TinyModels.CreateLexicon(entries, analyses: new[] { Readings("running", new DemaConsulting.PartOfSpeech.Lexicon.Analysis("Run", Upos.Verb, "VerbForm=Ger")) }));

        // Assert: the base word is the stored lower-case text and every case of the question matches; the capital one is refused
        Assert.Equal("run", Assert.Single(analyses).Lemma);
        Assert.True(tagger.IsFormOf("running", "run", Upos.Verb));
        Assert.True(tagger.IsFormOf("RUNNING", "Run", Upos.Verb));
        Assert.True(tagger.IsFormOf("Running", "RUN", Upos.Verb));
        Assert.Contains("lower case", refused.Message);
    }

    /// <summary>
    ///     Proves the equality, hash code and text of the result and analysis types, and the value of their defaults.
    /// </summary>
    [Fact]
    public void WordTags_Equality_FollowsTheLookupResult()
    {
        // Arrange: results of two words and the default value
        var tagger = Tagger.Default;
        var valve = tagger.GetPossibleTags("valve");
        var again = tagger.GetPossibleTags("VALVE");
        var tests = tagger.GetPossibleTags("tests");

        // Act / Assert: equal for the same entry, different for another, and the default is the empty not recognized result
        Assert.True(valve.Equals(again));
        Assert.True(valve.Equals((object)again));
        Assert.Equal(valve.GetHashCode(), again.GetHashCode());
        Assert.True(valve != tests);
        Assert.False(valve.Equals("valve"));
        Assert.Equal("Spelling NOUN", valve.ToString());
        Assert.Equal("Spelling NOUN|VERB", tests.ToString());
        WordTags none = default;
        Assert.Equal(WordMatch.NotRecognized, none.Match);
        Assert.False(none.IsKnownWord);
        Assert.True(none.Tags.IsEmpty);
        Assert.True(none.TagsWithoutAnalysis.IsEmpty);
        Assert.Empty(none.GetAnalyses());
        Assert.Equal("NotRecognized", none.ToString());
        Assert.Equal(none, tagger.GetPossibleTags("zzqxv"));
        Assert.NotEqual(none, tagger.GetPossibleTags("zzqxv", PossibleTagsMode.WithShapeRules));
    }

    /// <summary>
    ///     Proves the members of a word analysis: equality, text, feature access and the default value.
    /// </summary>
    [Fact]
    public void WordAnalysis_Members_WorkOnRealAndDefaultValues()
    {
        // Arrange: analyses of the embedded lexicon (one with features, one without) and the default value
        var tagger = Tagger.Default;
        var verb = tagger.GetPossibleTags("tests").GetAnalyses()[1];
        var same = tagger.GetPossibleTags("TESTS").GetAnalyses()[1];
        var interjection = tagger.GetPossibleTags("well").GetAnalyses().Single(a => a.Tag == Upos.Intj);
        var gerund = tagger.GetPossibleTags("testing").GetAnalyses()[0];
        WordAnalysis none = default;

        // Act / Assert: value equality, UD text, feature lookup by exact name, and an empty default
        Assert.True(verb == same);
        Assert.True(verb.Equals((object)same));
        Assert.Equal(verb.GetHashCode(), same.GetHashCode());
        Assert.True(verb != gerund);
        Assert.False(verb.Equals("test"));
        Assert.Equal("test VERB Person=3|Tense=Pres", verb.ToString());
        Assert.Equal("well INTJ", interjection.ToString());
        Assert.Equal("Sing", gerund.GetFeature("Number"));
        Assert.Null(gerund.GetFeature("Num"));
        Assert.Null(gerund.GetFeature(string.Empty));
        Assert.Throws<ArgumentNullException>(() => gerund.GetFeature(null!));
        Assert.Equal(string.Empty, none.Lemma);
        Assert.Equal(string.Empty, none.Features);
        Assert.Null(none.GetFeature("Number"));
        Assert.Equal(none, default);
    }

    /// <summary>
    ///     Proves that many threads looking words up (and tagging text in between) all get the answers of one thread.
    /// </summary>
    [Fact]
    public void Tagger_GetPossibleTags_ConcurrentCalls_GiveTheSameResultsAsOneThread()
    {
        // Arrange: words that cover spellings, compounds, unknown words and shapes, and the single-threaded answers
        var tagger = Tagger.Default;
        var words = new[] { "tests", "Valve", "well-tested", "zzqxv", "1st", "--", "went", "CAFE\u0301", "is" };
        string Render(string word) => string.Join(
            "|",
            new[] { PossibleTagsMode.LexiconOnly, PossibleTagsMode.WithShapeRules }.Select(mode =>
            {
                var result = tagger.GetPossibleTags(word, mode);
                return $"{result}/{result.TagsWithoutAnalysis}/{string.Join(";", AnalysesOf(result))}";
            })) + "|" + tagger.IsFormOf(word, "test", Upos.Verb);
        const string sentence = "The old valve was tested well . Close the valve .";
        var expectedWords = words.Select(Render).ToArray();
        var expectedTags = Describe(tagger.Tag(sentence));
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();

        // Act: run the same work from several threads at once, interleaved with tagging
        Parallel.For(0, 400, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
        {
            var index = i % words.Length;
            if (Render(words[index]) != expectedWords[index])
            {
                failures.Add($"lookup of {words[index]}");
            }

            if (i % 5 == 0 && Describe(tagger.Tag(sentence)) != expectedTags)
            {
                failures.Add("tagging");
            }
        });

        // Assert: every answer equals the single-threaded one
        Assert.Empty(failures);
    }

#if NET
    /// <summary>
    ///     Proves that the span overload gives the same result as the string overload, also for a slice of a longer text.
    /// </summary>
    [Fact]
    public void Tagger_GetPossibleTags_Span_EqualsStringOverload()
    {
        // Arrange: words of every kind, and a text that holds a word between other characters
        var tagger = Tagger.Default;
        var words = new[] { "tests", "Valve", "well-tested", "zzqxv", "1st", "--", "CAFE\u0301", "n\u2019t", "a\uD800b" };
        const string text = "xx tests yy";

        // Act / Assert: the same result in both modes
        foreach (var word in words)
        {
            foreach (var mode in new[] { PossibleTagsMode.LexiconOnly, PossibleTagsMode.WithShapeRules })
            {
                Assert.Equal(tagger.GetPossibleTags(word, mode), tagger.GetPossibleTags(word.AsSpan(), mode));
            }
        }

        Assert.Equal(tagger.GetPossibleTags("tests"), tagger.GetPossibleTags(text.AsSpan(3, 5)));
        Assert.Equal(tagger.GetPossibleTags("tests"), tagger.GetPossibleTags("tests".AsSpan()));
    }

    /// <summary>
    ///     Proves that the span overload rejects an empty span, white space and an undefined mode.
    /// </summary>
    [Fact]
    public void Tagger_GetPossibleTags_Span_InvalidInput_Throws()
    {
        // Arrange / Act / Assert: the same rules as for the string overload
        var tagger = Tagger.Default;
        Assert.Throws<ArgumentException>(() => tagger.GetPossibleTags(ReadOnlySpan<char>.Empty));
        Assert.Throws<ArgumentException>(() => tagger.GetPossibleTags("ice cream".AsSpan()));
        Assert.Throws<ArgumentException>(() => tagger.GetPossibleTags("a\u00A0b".AsSpan()));
        Assert.Throws<ArgumentOutOfRangeException>(() => tagger.GetPossibleTags("valve".AsSpan(), (PossibleTagsMode)7));
    }
#endif

    /// <summary>
    ///     Proves that the form question answers from the analyses for an ASCII word in every letter case, for a hyphenated
    ///     compound and for a compound that is longer than the longest spelling of the lexicon.
    /// </summary>
    [Fact]
    public void Tagger_IsFormOf_CaseVariantsAndCompounds_AnswerFromTheAnalyses()
    {
        // Arrange: the embedded lexicon and a compound whose whole is longer than any spelling and whose last part is "tested"
        var tagger = Tagger.Default;
        var longCompound = new string('a', 300) + "-tested";

        // Act / Assert: true for the analyses that the words have, false for a tag without analysis and an unknown word
        Assert.True(tagger.IsFormOf("tests", "test", Upos.Verb));
        Assert.True(tagger.IsFormOf("TESTS", "Test", Upos.Noun));
        Assert.True(tagger.IsFormOf("well-tested", "test", Upos.Verb));
        Assert.True(tagger.IsFormOf(longCompound, "test", Upos.Verb));
        Assert.False(tagger.IsFormOf(longCompound, "test", Upos.Noun));
        Assert.False(tagger.IsFormOf("tests", "test", Upos.Adj));
        Assert.False(tagger.IsFormOf("zzqxv", "zzqxv", Upos.Noun));
    }

    /// <summary>
    ///     Proves that a known word lists its analyses, and that an unknown word and words of a million characters (ASCII and
    ///     non-ASCII) are answered without error, are not recognized and have no analyses.
    /// </summary>
    [Fact]
    public void Tagger_GetPossibleTags_Analyses_KnownWordHasThemAndUnknownOrHugeWordsHaveNone()
    {
        // Arrange: the embedded lexicon, a known word, an unknown word and two words of a million characters
        var tagger = Tagger.Default;
        var huge = new string('a', 1_000_000);
        var hugeAccented = new string('\u00E9', 1_000_000);

        // Act
        var known = tagger.GetPossibleTags("tests");
        var unknown = tagger.GetPossibleTags("zzqxv");
        var hugeResult = tagger.GetPossibleTags(huge);
        var hugeAccentedResult = tagger.GetPossibleTags(hugeAccented);

        // Assert: "tests" has the analyses of the base word "test"; the others have none and are not recognized
        Assert.NotEmpty(known.GetAnalyses());
        Assert.All(known.GetAnalyses(), analysis => Assert.Equal("test", analysis.Lemma));
        Assert.Empty(unknown.GetAnalyses());
        Assert.Equal(WordMatch.NotRecognized, hugeResult.Match);
        Assert.Empty(hugeResult.GetAnalyses());
        Assert.Equal(WordMatch.NotRecognized, hugeAccentedResult.Match);
        Assert.Empty(hugeAccentedResult.GetAnalyses());
    }

    /// <summary>
    ///     Describes a tagged text with tags, confidences and alternatives, for equality comparisons.
    /// </summary>
    private static string Describe(IReadOnlyList<TaggedSentence> sentences) =>
        string.Join("|", sentences.SelectMany(s => s.Tokens).Select(t =>
            $"{t.Text}@{t.Start}/{t.Tag}/{t.Confidence:R}/{t.IsKnownWord}/{string.Join(",", t.Alternatives)}"));
}
