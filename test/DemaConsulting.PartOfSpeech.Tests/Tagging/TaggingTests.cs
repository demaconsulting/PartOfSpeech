// cspell:ignore zorbulon
namespace DemaConsulting.PartOfSpeech.Tests.Tagging;

/// <summary>
///     Subsystem tests of Tagging: the tagger, decoder, extractor and constraints working together on a tiny trained model.
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
    ///     Describes tagged sentences with tags, confidences and alternatives.
    /// </summary>
    private static string Describe(IReadOnlyList<TaggedSentence> sentences) =>
        string.Join("|", sentences.SelectMany(s => s.Tokens).Select(t =>
            $"{t.Text}@{t.Start}/{t.Tag}/{t.Confidence:R}/{string.Join(",", t.Alternatives)}"));
}
