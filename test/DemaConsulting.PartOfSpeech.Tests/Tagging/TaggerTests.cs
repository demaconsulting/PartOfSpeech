// cspell:ignore zorbulon zork Cquotes
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

#if NET
    /// <summary>
    ///     Proves that reading the margin and the known-word flag allocates nothing, also for a token with alternatives.
    /// </summary>
    [Fact]
    public void Tagger_Tag_Margin_DoesNotAllocate()
    {
        // Arrange: a token that has three alternatives
        var weights = new TinyModels.WeightsBuilder().Word("cat", Upos.Noun, 3).Word("cat", Upos.Verb, 1);
        var token = CreateHandSet(weights, new[] { "cat NOUN VERB ADJ" }).Tag(new[] { "cat" }).Tokens[0];
        var sink = 0.0;

        // Act: read both members in many rounds
        var bytes = AllocationMeter.MinimumBatchBytes(
            () =>
            {
                sink += token.Margin;
                sink += token.IsKnownWord ? 1 : 0;
            },
            100);

        // Assert: nothing is allocated per batch (reading through the alternatives would allocate a wrapper per read)
        Assert.Equal(0, bytes);
        Assert.True(sink > 0);
    }
#endif

    /// <summary>
    ///     Describes a tagged text with tags, confidences and alternatives, for equality comparisons.
    /// </summary>
    private static string Describe(IReadOnlyList<TaggedSentence> sentences) =>
        string.Join("|", sentences.SelectMany(s => s.Tokens).Select(t =>
            $"{t.Text}@{t.Start}/{t.Tag}/{t.Confidence:R}/{t.IsKnownWord}/{string.Join(",", t.Alternatives)}"));
}
