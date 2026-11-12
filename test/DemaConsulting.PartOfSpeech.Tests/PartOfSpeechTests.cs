// cspell:ignore zorbulon zzqxj Cquoted
using System.Xml.Linq;

namespace DemaConsulting.PartOfSpeech.Tests;

/// <summary>
///     System-level tests of the library: the public API from text to tagged tokens with the embedded lexicon, models loaded
///     from streams, constraints, glossaries, concurrency, throughput and packaging.
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
    ///     Proves that a hard glossary entry forces the tag of its head word, with certainty, in running text.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Glossary_HardEntry_ForcesHeadTag()
    {
        // Arrange: the word cat scores NOUN 3 and VERB 1 (the embedded lexicon allows both), and a glossary that says that cat is a verb
        var weights = new TinyModels.WeightsBuilder().Word("cat", Upos.Noun, 3).Word("cat", Upos.Verb, 1).Build();
        var tagger = TinyModels.CreateTagger(weights);
        var glossary = new GlossaryBuilder { { "cat", Upos.Verb } }.Build();

        // Act: tag a text without and with the glossary
        var free = tagger.Tag("The cat sat.")[0].Tokens[1];
        var forced = tagger.Tag("The Cat sat.", glossary)[0].Tokens[1];

        // Assert: NOUN without the glossary, VERB with it (also when capitalized), certain, constrained and reported as the head of the match
        Assert.Equal(Upos.Noun, free.Tag);
        Assert.Equal(Upos.Verb, forced.Tag);
        Assert.Equal(1.0, forced.Confidence);
        Assert.True(forced.IsConstrained);
        Assert.True(forced.IsGlossaryHead);
    }

    /// <summary>
    ///     Proves that a soft glossary entry adds a preference that a small strength does not and a large strength does turn into a different tag.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Glossary_SoftEntry_PreferenceShiftsCloseDecision()
    {
        // Arrange: the word cat scores NOUN 3 and VERB 1 at temperature 1, and soft glossaries of strength 0.5 and 5 that say VERB
        var weights = new TinyModels.WeightsBuilder().Word("cat", Upos.Noun, 3).Word("cat", Upos.Verb, 1).Build(1f);
        var tagger = TinyModels.CreateTagger(weights);
        Glossary Soft(double strength) =>
            new GlossaryBuilder(new GlossaryOptions { Mode = GlossaryMode.Soft, SoftStrength = strength }) { { "cat", Upos.Verb } }.Build();

        // Act: tag with each
        var free = tagger.Tag(new[] { "cat" }).Tokens[0];
        var small = tagger.Tag(new[] { "cat" }, Soft(0.5)).Tokens[0];
        var large = tagger.Tag(new[] { "cat" }, Soft(5)).Tokens[0];

        // Assert: the small preference keeps NOUN with a lower confidence, the large one flips it to VERB; both are constrained, and neither is certain
        Assert.Equal(Upos.Noun, free.Tag);
        Assert.Equal(Upos.Noun, small.Tag);
        Assert.True(small.Confidence < free.Confidence);
        Assert.Equal(Upos.Verb, large.Tag);
        Assert.True(small.IsConstrained && large.IsConstrained);
        Assert.True(large.Confidence < 1.0);
    }

    /// <summary>
    ///     Proves that the matches are reported on the tokens and on the sentences of a text, with offsets into the original string and the definitions.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Glossary_Results_ReportMatchesOnTokensAndSentences()
    {
        // Arrange: a tagger, a glossary with a definition and a text with two sentences, of which the second has two matches
        var tagger = EmbeddedLexiconTagger();
        var glossary = new GlossaryBuilder { { "cat", Upos.Noun, "A small animal." }, { "the mat", Upos.Noun } }.Build();
        const string text = "A dog barked. The cat sat on the mat.";

        // Act: tag the text
        var sentences = tagger.Tag(text, glossary);

        // Assert: no match in the first sentence, two in text order in the second, with their offsets in the whole text and their definitions
        Assert.Equal(2, sentences.Count);
        Assert.Empty(sentences[0].GlossaryMatches);
        Assert.Equal(new[] { "cat", "the mat" }, sentences[1].GlossaryMatches.Select(m => m.Text));
        Assert.All(sentences[1].GlossaryMatches, m => Assert.Equal(m.Text, text.Substring(m.Start, m.Length)));
        Assert.Equal("A small animal.", sentences[1].GlossaryMatches[0].Entry.Definition);
        Assert.Null(sentences[1].GlossaryMatches[1].Entry.Definition);
        var tokens = sentences[1].Tokens;
        Assert.Same(sentences[1].GlossaryMatches[0], tokens[1].GlossaryMatch);
        Assert.Same(sentences[1].GlossaryMatches[1], tokens[4].GlossaryMatch);
        Assert.Same(sentences[1].GlossaryMatches[1], tokens[5].GlossaryMatch);
        Assert.Equal(new[] { false, true, false, false, false, true, false }, tokens.Select(t => t.IsGlossaryHead));
        Assert.All(sentences[0].Tokens, t => Assert.Null(t.GlossaryMatch));
    }

    /// <summary>
    ///     Proves that the documented examples give the claimed results with the embedded default model: only what the constraints guarantee is asserted.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Glossary_DocumentedExamples_GiveTheClaimedResults()
    {
        // Arrange: the technical-name list of the documentation, the acronym example and the soft example
        var tagger = Tagger.Default;
        var names = new GlossaryBuilder { { "bleed valve", Upos.Noun }, { "torque wrench", Upos.Noun }, { "accumulator", Upos.Noun } }.Build();
        var acronym = new GlossaryBuilder { new GlossaryEntry("IT", Upos.Noun).WithCaseSensitive(true) }.Build();
        var soft = new GlossaryBuilder(new GlossaryOptions { Mode = GlossaryMode.Soft }) { { "torque wrench", Upos.Noun } }.Build();

        // Act: tag the sentences
        var technical = tagger.Tag("Bleed valves need a torque wrench.", names).Single();
        var instruction = tagger.Tag("Open the bleed valve. Use a torque wrench.", names);
        var upper = tagger.Tag("Call IT now.", acronym).Single();
        var lowerWithGlossary = tagger.Tag("Call it now.", acronym);
        var lowerWithout = tagger.Tag("Call it now.");
        var boosted = tagger.Tag(new[] { "Use", "a", "torque", "wrench", "." }, soft);

        // Assert: heads are nouns with certainty (also in the plural and at the start of the sentence), their first word is never a verb
        Assert.Equal(new[] { "Bleed valves", "torque wrench" }, technical.GlossaryMatches.Select(m => m.Text));
        Assert.Equal(Upos.Noun, technical.Tokens[1].Tag);
        Assert.Equal(1.0, technical.Tokens[1].Confidence);
        Assert.NotEqual(Upos.Verb, technical.Tokens[0].Tag);
        Assert.NotEqual(Upos.Aux, technical.Tokens[0].Tag);
        Assert.All(instruction.SelectMany(s => s.GlossaryMatches), m => Assert.Equal(2, m.TokenCount));

        Assert.Equal(2, instruction.Count);
        Assert.Equal(Upos.Noun, instruction[0].Tokens[3].Tag);
        Assert.NotEqual(Upos.Verb, instruction[0].Tokens[2].Tag);
        Assert.Equal(Upos.Noun, instruction[1].Tokens[3].Tag);
        Assert.NotEqual(Upos.Verb, instruction[1].Tokens[2].Tag);

        // The acronym is a noun; the lower-case pronoun is untouched
        Assert.Equal(Upos.Noun, upper.Tokens[1].Tag);
        Assert.True(upper.Tokens[1].IsGlossaryHead);
        Assert.Empty(lowerWithGlossary.SelectMany(s => s.GlossaryMatches));
        Assert.Equal(Describe(lowerWithout), Describe(lowerWithGlossary));

        // Soft mode: the head word is constrained, reported as head, with a confidence in (0, 1]
        var head = boosted.Tokens.Single(t => t.IsGlossaryHead);
        Assert.Equal("wrench", head.Text);
        Assert.True(head.IsConstrained);
        Assert.InRange(head.Confidence, double.Epsilon, 1.0);
    }

    /// <summary>
    ///     Proves through the public API with the embedded lexicon that a token reports whether the lexicon knows it and its margin:
    ///     a listed word is known, an invented word is not, and a close call has a small margin while a large gap has a large one.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Tagging_Evidence_UnknownWordAndCloseCall_AreReported()
    {
        // Arrange: a tagger with the embedded lexicon and all alternatives, and two hand-set models for cat (a close call and a large gap)
        const string invented = "zzqxj";
        var tagger = EmbeddedLexiconTagger(new TaggerOptions { MaxAlternatives = 16 });
        var close = TinyModels.CreateTagger(new TinyModels.WeightsBuilder().Word("cat", Upos.Noun, 3).Word("cat", Upos.Verb, 2).Build(4f));
        var gap = TinyModels.CreateTagger(new TinyModels.WeightsBuilder().Word("cat", Upos.Noun, 40).Word("cat", Upos.Verb, 0).Build(4f));

        // Act: tag a sentence with a listed and an invented word, and the close and the clear case
        var tokens = tagger.Tag(new[] { "The", "cat", invented }).Tokens;
        var closeCall = close.Tag(new[] { "cat" }).Tokens[0];
        var clearCase = gap.Tag(new[] { "cat" }).Tokens[0];

        // Assert: the flags follow the lexicon, the margin is the confidence minus the runner-up (1 without one), and the close call is small
        Assert.True(tokens[1].IsKnownWord);
        Assert.False(tokens[2].IsKnownWord);
        Assert.Equal(16, tokens[2].Alternatives.Count);
        foreach (var token in tokens)
        {
            var expected = token.Alternatives.Count == 0 ? 1.0 : token.Confidence - token.Alternatives[0].Probability;
            Assert.Equal(expected, token.Margin, 12);
        }

        Assert.True(closeCall.Margin < 0.2, $"close call margin {closeCall.Margin}");
        Assert.True(clearCase.Margin > 0.9, $"clear margin {clearCase.Margin}");
        Assert.True(closeCall.Margin < clearCase.Margin);
    }

    /// <summary>
    ///     Proves that the rule checker examples of the documentation give the claimed results with the embedded default model: only
    ///     what the constraints guarantee is asserted, and the known-word flag is compared with the embedded lexicon.
    /// </summary>
    [Fact]
    public void PartOfSpeech_RuleChecker_DocumentedExample_GivesTheClaimedResults()
    {
        // Arrange: the dictionary of the documentation, the spellings that the embedded lexicon lists, and the terms of the constraint variant
        var tagger = Tagger.Default;
        var dictionary = new GlossaryBuilder
        {
            { "manual override", Upos.Noun, "The mode in which an operator controls the unit by hand." },
            { "wiring schematic", Upos.Noun },
        }.Build();
        var listed = new HashSet<string>(EmbeddedModel.Lexicon.Enumerate().Select(e => e.Key), StringComparer.Ordinal);
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "override", "schematic" };

        // Act: tag the text with the glossary, and a sentence with the per-token constraints
        var tokens = tagger.Tag("Select the manual override, then read the wiring schematic.", dictionary).Single().Tokens;
        var sentence = Tokenizer.Tokenize("Read the wiring schematic.")[0];
        var constraints = sentence.Tokens
            .Select(token => terms.Contains(token.Text) ? TokenConstraint.Only(Upos.Noun) : TokenConstraint.None)
            .ToArray();
        var constrained = tagger.Tag(sentence, constraints).Tokens;

        // Assert: the heads are certain, constrained nouns and the flag equals the lexicon membership; the other tokens are not constrained
        foreach (var head in tokens.Where(t => t.IsGlossaryHead))
        {
            Assert.Equal(Upos.Noun, head.Tag);
            Assert.Equal(1.0, head.Confidence);
            Assert.True(head.IsConstrained);
            Assert.Equal(1.0, head.Margin);
        }

        Assert.Equal(new[] { "override", "schematic" }, tokens.Where(t => t.IsGlossaryHead).Select(t => t.Text));
        Assert.All(tokens, t => Assert.Equal(listed.Contains(t.Text.ToLowerInvariant()), t.IsKnownWord));
        var untouched = tokens.Where(t => t.Text == "then" || t.Text == ",").ToList();
        Assert.Equal(2, untouched.Count);
        Assert.All(untouched, t => Assert.False(t.IsConstrained));
        Assert.All(untouched, t => Assert.Null(t.GlossaryMatch));

        // The constraint variant: the listed tokens are certain nouns
        var schematic = constrained.Single(t => t.Text == "schematic");
        Assert.Equal(Upos.Noun, schematic.Tag);
        Assert.Equal(1.0, schematic.Confidence);
        Assert.True(schematic.IsConstrained);
        Assert.Equal(listed.Contains("schematic"), schematic.IsKnownWord);
        Assert.False(constrained.Single(t => t.Text == "Read").IsConstrained);
    }

    /// <summary>
    ///     Proves that a soft glossary entry and a Prefer constraint set IsConstrained without forcing the tag: the confidence stays
    ///     below 1.0, so the documented rule checker does not treat such a token as decided by the dictionary, while a hard entry
    ///     and a single-tag constraint report the confidence 1.0.
    /// </summary>
    [Fact]
    public void PartOfSpeech_RuleChecker_SoftAndPreferConstraints_AreConstrainedButNotForced()
    {
        // Arrange: a soft glossary and a hard glossary for an invented word, and a Prefer and a single-tag constraint
        var tagger = Tagger.Default;
        var soft = Glossary.Create(new[] { new GlossaryEntry("zorbulon", Upos.Noun) }, new GlossaryOptions { Mode = GlossaryMode.Soft });
        var hard = Glossary.Create(new[] { new GlossaryEntry("zorbulon", Upos.Noun) });
        var sentence = Tokenizer.Tokenize("Check the zorbulon now.")[0];
        var index = sentence.Tokens.ToList().FindIndex(t => t.Text == "zorbulon");
        TokenConstraint[] Constraints(TokenConstraint constraint)
        {
            var result = new TokenConstraint[sentence.Tokens.Count];
            result[index] = constraint;
            return result;
        }

        // Act: tag with each variant
        var viaSoft = tagger.Tag("Check the zorbulon now.", soft).Single().Tokens[index];
        var viaHard = tagger.Tag("Check the zorbulon now.", hard).Single().Tokens[index];
        var viaPrefer = tagger.Tag(sentence, Constraints(TokenConstraint.Prefer(Upos.Noun, 1.0))).Tokens[index];
        var viaOnly = tagger.Tag(sentence, Constraints(TokenConstraint.Only(Upos.Noun))).Tokens[index];

        // Assert: all four are constrained, but only the hard entry and the single-tag constraint are forced; the word is unknown in all four
        Assert.All(new[] { viaSoft, viaHard, viaPrefer, viaOnly }, t => Assert.True(t.IsConstrained));
        Assert.All(new[] { viaSoft, viaHard, viaPrefer, viaOnly }, t => Assert.False(t.IsKnownWord));
        Assert.Equal(1.0, viaHard.Confidence);
        Assert.Equal(1.0, viaOnly.Confidence);
        Assert.True(viaSoft.Confidence < 1.0, $"soft confidence {viaSoft.Confidence}");
        Assert.True(viaPrefer.Confidence < 1.0, $"prefer confidence {viaPrefer.Confidence}");
        Assert.True(viaSoft.Margin < 1.0 && viaPrefer.Margin < 1.0);
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
    ///     Proves that eight threads sharing one tagger and one glossary, hard and soft, get the same results as a sequential run.
    /// </summary>
    [Fact]
    public async Task PartOfSpeech_Concurrency_GlossaryTagging_MatchesSequential()
    {
        // Arrange: one tagger, a hard and a soft glossary and a set of texts; the sequential results are the reference
        var tagger = EmbeddedLexiconTagger();
        var hard = new GlossaryBuilder { { "cat", Upos.Verb }, { "the mat", Upos.Noun }, { "reads", Upos.Noun } }.Build();
        var soft = new GlossaryBuilder(new GlossaryOptions { Mode = GlossaryMode.Soft }) { { "cat", Upos.Verb }, { "book", Upos.Verb } }.Build();
        var texts = TinyTrainer.Sentences().Select(s => string.Join(" ", s.Tokens)).ToList();
        string Run(string text, Glossary glossary) =>
            Describe(tagger.Tag(text, glossary)) + "#" + string.Join(",", tagger.Tag(text, glossary).SelectMany(s => s.GlossaryMatches).Select(m => $"{m.Start}:{m.Text}"));
        var expected = texts.Select(t => (Run(t, hard), Run(t, soft))).ToList();
        var mismatches = 0;

        // Act: eight tasks, 100 iterations each, in different orders
        var tasks = Enumerable.Range(0, 8).Select(task => Task.Run(() =>
        {
            for (var iteration = 0; iteration < 100; iteration++)
            {
                var index = (iteration + (task * 5)) % texts.Count;
                if (Run(texts[index], hard) != expected[index].Item1 || Run(texts[index], soft) != expected[index].Item2)
                {
                    Interlocked.Increment(ref mismatches);
                }
            }
        }, TestContext.Current.CancellationToken)).ToArray();
        await Task.WhenAll(tasks);

        // Assert: no difference
        Assert.Equal(0, mismatches);
        Assert.Contains(expected, e => e.Item1.IndexOf(':') >= 0);
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
