// cspell:ignore zorbulon
namespace DemaConsulting.PartOfSpeech.Tests.Tagging;

/// <summary>
///     Subsystem tests of Tagging: the tagger, decoder, extractor, constraints and glossary working together on tiny models.
/// </summary>
public class TaggingTests
{
    /// <summary>
    ///     The weights of the tiny trained model.
    /// </summary>
    private static readonly byte[] Weights = TinyTrainer.Train();

    /// <summary>
    ///     The lexicon of the fixture words.
    /// </summary>
    private static readonly byte[] FixtureLexicon = TinyTrainer.FixtureLexicon();

    /// <summary>
    ///     Proves that the tiny model, trained with the shared feature extractor and the weights writer, tags its own training sentences correctly.
    /// </summary>
    [Fact]
    public void Tagging_TinyTrainedModel_TagsTrainingSentences()
    {
        // Arrange: the tagger and the 40 fixture sentences
        var tagger = TinyModels.CreateTagger(Weights, FixtureLexicon);
        var wrong = new List<string>();
        var total = 0;

        // Act: tag every sentence from its tokens and compare with the gold tags
        foreach (var (tokens, gold) in TinyTrainer.Sentences())
        {
            var tagged = tagger.Tag(tokens);
            for (var i = 0; i < tokens.Length; i++)
            {
                total++;
                if (tagged.Tokens[i].Tag != gold[i])
                {
                    wrong.Add($"{string.Join(" ", tokens)} [{tokens[i]}: {tagged.Tokens[i].Tag} instead of {gold[i]}]");
                }
            }
        }

        // Assert: all tokens are right (the fixtures are separable and the model trained for 15 epochs)
        Assert.True(total > 250);
        Assert.Empty(wrong);
    }

    /// <summary>
    ///     Proves that decoding stays inside the lexicon tags even when the sentence is nonsense that the model never saw.
    /// </summary>
    [Fact]
    public void Tagging_TinyTrainedModel_LexiconRestrictionHolds()
    {
        // Arrange: the lexicon tags of every fixture word, and the fixture sentences in reverse word order
        var lexicon = Lexicon.Load(FixtureLexicon);
        var tagger = TinyModels.CreateTagger(Weights, FixtureLexicon, new TaggerOptions { MaxAlternatives = 16 });
        var violations = new List<string>();

        // Act: tag the reversed sentences
        foreach (var (tokens, _) in TinyTrainer.Sentences())
        {
            var reversed = tokens.Reverse().ToArray();
            var tagged = tagger.Tag(reversed);
            foreach (var token in tagged.Tokens)
            {
                // The allowed set is the chosen tag together with the alternatives (all 16 others that were allowed)
                var allowed = UposSet.Of(token.Tag).Union(UposSet.Of(token.Alternatives.Select(a => a.Tag).ToArray()));
                if (!allowed.Equals(Lexicon.AllowedTags(token.Text, lexicon)))
                {
                    violations.Add(token.Text);
                }
            }
        }

        // Assert: the decoder considered exactly the lexicon (and shape) tags for every token
        Assert.Empty(violations);
    }

    /// <summary>
    ///     Proves that the same word gets different tags depending on the tags chosen before it.
    /// </summary>
    [Fact]
    public void Tagging_Context_PrecedingTags_ChangeTagOfAmbiguousWord()
    {
        // Arrange: the tiny model knows run as a noun after a determiner and as a verb after a pronoun and a modal
        var tagger = TinyModels.CreateTagger(Weights, FixtureLexicon);

        // Act: tag run in two contexts
        var noun = tagger.Tag(new[] { "We", "went", "for", "a", "run", "." }).Tokens[4];
        var verb = tagger.Tag(new[] { "She", "will", "run", "to", "school", "." }).Tokens[2];

        // Assert: NOUN and VERB, both with a real choice between several tags
        Assert.Equal(Upos.Noun, noun.Tag);
        Assert.Equal(Upos.Verb, verb.Tag);
        Assert.True(noun.Alternatives.Count > 0);
        Assert.True(verb.Alternatives.Count > 0);
    }

    /// <summary>
    ///     Proves through the public API that a constraint on one token changes the tag of the next token via the tag history.
    /// </summary>
    [Fact]
    public void Tagging_Constraints_FirstTokenOnly_ChangesNextTokenTag()
    {
        // Arrange: a is DET (2) or ADJ (1); the word after DET is a NOUN, after ADJ it is a VERB
        var weights = new TinyModels.WeightsBuilder()
            .Word("a", Upos.Det, 2).Word("a", Upos.Adj, 1)
            .AfterTag("DET", Upos.Noun, 5).AfterTag("ADJ", Upos.Verb, 5)
            .Build();
        var tagger = TinyModels.CreateTagger(weights, TinyModels.CreateLexicon("a DET ADJ", "dog NOUN VERB"));

        // Act: tag free and with the first token restricted to ADJ
        var free = tagger.Tag(new[] { "a", "dog" });
        var constrained = tagger.Tag(new[] { "a", "dog" }, new[] { TokenConstraint.Only(Upos.Adj), TokenConstraint.None });

        // Assert: the neighbor follows the constraint
        Assert.Equal(new[] { Upos.Det, Upos.Noun }, free.Tokens.Select(t => t.Tag));
        Assert.Equal(new[] { Upos.Adj, Upos.Verb }, constrained.Tokens.Select(t => t.Tag));
        Assert.True(constrained.Tokens[0].IsConstrained);
        Assert.False(constrained.Tokens[1].IsConstrained);
    }

    /// <summary>
    ///     Proves through the public API that a glossary entry changes the tag of its head word and, through the tag history, the tag of the next token.
    /// </summary>
    [Fact]
    public void Tagging_Glossary_HardEntry_ChangesHeadAndNextTokenThroughHistory()
    {
        // Arrange: a is DET (2) or ADJ (1); the word after DET is a NOUN, after ADJ it is a VERB; the glossary says a is ADJ
        var weights = new TinyModels.WeightsBuilder()
            .Word("a", Upos.Det, 2).Word("a", Upos.Adj, 1)
            .AfterTag("DET", Upos.Noun, 5).AfterTag("ADJ", Upos.Verb, 5)
            .Build();
        var tagger = TinyModels.CreateTagger(weights, TinyModels.CreateLexicon("a DET ADJ", "dog NOUN VERB"));
        var glossary = Glossary.Create(new[] { new GlossaryEntry("a", Upos.Adj) });

        // Act: tag free and with the glossary
        var free = tagger.Tag(new[] { "a", "dog" });
        var withGlossary = tagger.Tag(new[] { "a", "dog" }, glossary);

        // Assert: the head word follows the entry with certainty, the neighbor follows through the history
        Assert.Equal(new[] { Upos.Det, Upos.Noun }, free.Tokens.Select(t => t.Tag));
        Assert.Equal(new[] { Upos.Adj, Upos.Verb }, withGlossary.Tokens.Select(t => t.Tag));
        Assert.Equal(1.0, withGlossary.Tokens[0].Confidence);
        Assert.True(withGlossary.Tokens[0].IsConstrained);
        Assert.False(withGlossary.Tokens[1].IsConstrained);
    }

    /// <summary>
    ///     Proves that a multiword entry keeps its other words from being verbs and that the match is reported on the tokens and on the sentence.
    /// </summary>
    [Fact]
    public void Tagging_Glossary_Results_ReportMatchesOnTokensAndSentence()
    {
        // Arrange: hand-set weights under which "run" is a verb for the model, a lexicon where it may be NOUN or VERB, and a noun term "run mat"
        var weights = new TinyModels.WeightsBuilder()
            .Word("a", Upos.Det, 5).Word("run", Upos.Verb, 5).Word("run", Upos.Noun, 1).Word("mat", Upos.Noun, 3)
            .Build();
        var tagger = TinyModels.CreateTagger(weights, TinyModels.CreateLexicon("a DET", "run NOUN VERB", "mat NOUN VERB"));
        var glossary = Glossary.Create(new[] { new GlossaryEntry("run mat", Upos.Noun, "A mat for exercise.") });
        const string text = "Use a run mat.";

        // Act: tag the text without and with the glossary
        var free = tagger.Tag(text)[0];
        var tagged = tagger.Tag(text, glossary)[0];

        // Assert: the model alone reads "run" as a verb; with the glossary both words are nouns, one match covers them and the head is the last word
        Assert.Equal(Upos.Verb, free.Tokens[2].Tag);
        Assert.Equal(Upos.Noun, tagged.Tokens[2].Tag);
        Assert.Equal(Upos.Noun, tagged.Tokens[3].Tag);
        var match = Assert.Single(tagged.GlossaryMatches);
        Assert.Equal(2, match.FirstToken);
        Assert.Equal(2, match.TokenCount);
        Assert.Equal(3, match.HeadToken);
        Assert.Equal("run mat", match.Text);
        Assert.Equal("A mat for exercise.", match.Entry.Definition);
        Assert.Equal(new[] { false, false, false, true, false }, tagged.Tokens.Select(t => t.IsGlossaryHead));
        Assert.Equal(new[] { false, false, true, true, false }, tagged.Tokens.Select(t => t.GlossaryMatch != null));
        Assert.Empty(free.GlossaryMatches);
    }

    /// <summary>
    ///     Proves that tagging is deterministic: the same input gives the same output, also from a second tagger loaded from the same files.
    /// </summary>
    [Fact]
    public void Tagging_Tag_SameInput_ProducesSameOutput()
    {
        // Arrange: two taggers from the same bytes and a text
        var first = TinyModels.CreateTagger(Weights, FixtureLexicon);
        var second = TinyModels.CreateTagger(Weights, FixtureLexicon);
        const string text = "She will run to school. An unknown zorbulon sat on the mat!";

        // Act: tag repeatedly
        var a = Describe(first.Tag(text));
        var b = Describe(first.Tag(text));
        var c = Describe(second.Tag(text));

        // Assert: identical
        Assert.Equal(a, b);
        Assert.Equal(a, c);
    }

    /// <summary>
    ///     Proves through the public API that the tokens report the evidence for a rule checker: whether the lexicon knows the
    ///     word and the margin to the runner-up.
    /// </summary>
    [Fact]
    public void Tagging_Evidence_UnknownWordAndCloseCall_AreReported()
    {
        // Arrange: the tiny trained model with its fixture lexicon, all alternatives, and the spellings that the lexicon lists
        var listed = new HashSet<string>(Lexicon.Load(FixtureLexicon).Enumerate().Select(e => e.Key), StringComparer.Ordinal);
        var tagger = TinyModels.CreateTagger(Weights, FixtureLexicon, new TaggerOptions { MaxAlternatives = 16 });

        // Act: tag a sentence with an invented word
        var tokens = tagger.Tag("The cat sat on a zorbulon.").Single().Tokens;

        // Assert: the flag equals the lexicon membership of the lower-cased spelling, the invented word is unknown, and the margin
        // is the confidence minus the first alternative (1 when there is none)
        Assert.All(tokens, t => Assert.Equal(listed.Contains(t.Text.ToLowerInvariant()), t.IsKnownWord));
        Assert.True(tokens[1].IsKnownWord);
        Assert.False(tokens[5].IsKnownWord);
        Assert.Equal("zorbulon", tokens[5].Text);
        foreach (var token in tokens)
        {
            var expected = token.Alternatives.Count == 0 ? 1.0 : token.Confidence - token.Alternatives[0].Probability;
            Assert.Equal(expected, token.Margin, 12);
            Assert.InRange(token.Margin, 0.0, 1.0);
        }
    }

    /// <summary>
    ///     Describes tagged sentences with tags, confidences and alternatives.
    /// </summary>
    private static string Describe(IReadOnlyList<TaggedSentence> sentences) =>
        string.Join("|", sentences.SelectMany(s => s.Tokens).Select(t =>
            $"{t.Text}@{t.Start}/{t.Tag}/{t.Confidence:R}/{string.Join(",", t.Alternatives)}"));
}
