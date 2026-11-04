// cspell:ignore resync resynced Contoso
namespace DemaConsulting.PartOfSpeech.Tests.Tagging;

/// <summary>
///     Tests that the statements and examples of the glossary documentation (the quick start, the edge cases and the
///     examples of the entry methods) hold for the real library and the embedded model, so that a reader who copies an
///     example gets the documented result.
/// </summary>
public class GlossaryDocumentationTests
{
    /// <summary>
    ///     Tokenizes a text and returns its first sentence.
    /// </summary>
    private static Sentence S(string text) => Tokenizer.Tokenize(text)[0];

    /// <summary>
    ///     Builds the glossary of the quick start of the namespace documentation.
    /// </summary>
    private static Glossary QuickStartGlossary() =>
        new GlossaryBuilder
        {
            new GlossaryEntry("Contoso Hub", Upos.Propn, "The Contoso device manager.")
                .WithTokenTags(0, UposSet.Of(Upos.Propn)),
            new GlossaryEntry("IT", Upos.Noun, "Information technology.").WithCaseSensitive(true),
            new GlossaryEntry("re-sync", Upos.Verb).WithVariants("resync"),
            { "shut down", Upos.Verb },
        }.Build();

    /// <summary>
    ///     Proves that the quick start finds the product name, the acronym, the hyphenated verb (also as its listed variant) and the phrasal verb, reports the head of each as documented, and leaves the pronoun <c>it</c> alone.
    /// </summary>
    [Fact]
    public void Glossary_QuickStart_FindsTheFourKindsOfTermAndReportsTheirHeads()
    {
        // Arrange: the glossary and the text of the quick start (the second sentence has the pronoun and the variant)
        var glossary = QuickStartGlossary();
        var text = "Ask IT to re-sync the Contoso Hub, then shut down the unit. Ask it to resync.";

        // Act
        var sentences = Tagger.Default.Tag(text, glossary);
        var first = sentences[0];
        var second = sentences[1];

        // Assert: the four terms of the first sentence in text order, with the span, the head and the certain tags of the heads
        Assert.Equal(new[] { "IT", "re-sync", "Contoso Hub", "shut down" }, first.GlossaryMatches.Select(m => m.Text));
        Assert.Equal(new[] { 1, 1, 2, 2 }, first.GlossaryMatches.Select(m => m.TokenCount));
        Assert.Equal(new[] { Upos.Noun, Upos.Verb, Upos.Propn, Upos.Verb }, first.GlossaryMatches.Select(m => first.Tokens[m.HeadToken].Tag));
        Assert.All(first.GlossaryMatches, m => Assert.Equal(1.0, first.Tokens[m.HeadToken].Confidence));
        Assert.Equal(new[] { "IT", "re-sync", "Hub", "shut" }, first.GlossaryMatches.Select(m => first.Tokens[m.HeadToken].Text));

        // Assert: the first word of the product name is a proper noun because of WithTokenTags, and the particle is limited to particle tags
        var contoso = first.Tokens[first.GlossaryMatches[2].FirstToken];
        Assert.Equal("Contoso", contoso.Text);
        Assert.Equal(Upos.Propn, contoso.Tag);
        Assert.False(contoso.IsGlossaryHead);
        var down = first.Tokens[first.GlossaryMatches[3].FirstToken + 1];
        Assert.Equal("down", down.Text);
        Assert.True(UposSet.Of(Upos.Adp, Upos.Adv, Upos.Part).Contains(down.Tag));

        // Assert: IsGlossaryHead marks exactly the head of each match, and the tokens of a match know it
        Assert.Equal(4, first.Tokens.Count(token => token.IsGlossaryHead));
        Assert.All(first.GlossaryMatches, m => Assert.Same(m, first.Tokens[m.HeadToken].GlossaryMatch));

        // Assert: the lower-case pronoun is no match, the variant resync is, and it is reported for the entry re-sync
        var match = Assert.Single(second.GlossaryMatches);
        Assert.Equal("resync", match.Text);
        Assert.Equal("re-sync", match.Entry.Term);
        Assert.True(match.IsVariant);
        Assert.Equal(Upos.Pron, second.Tokens[1].Tag);
        Assert.Equal("it", second.Tokens[1].Text);
        Assert.Null(second.Tokens[1].GlossaryMatch);
    }

    /// <summary>
    ///     Proves that a hyphenated verb is one token, that its regular forms match, and that the spelling without the hyphen needs a variant.
    /// </summary>
    [Fact]
    public void Glossary_HyphenatedVerb_IsOneTokenAndMatchesItsFormsButNotOtherSpellings()
    {
        // Arrange: the entry without and with the variant
        var plain = Glossary.Create(new[] { new GlossaryEntry("re-sync", Upos.Verb) });
        var withVariant = Glossary.Create(new[] { new GlossaryEntry("re-sync", Upos.Verb).WithVariants("resync") });

        // Act / Assert: one token; the generated forms match; the unhyphenated spelling and the spaced spelling do not
        Assert.Equal(new[] { "re-sync" }, plain.Entries[0].Tokens);
        Assert.Equal(new[] { "re-syncs" }, plain.FindMatches(S("It re-syncs.")).Select(m => m.Text));
        Assert.Equal(new[] { "re-synced" }, plain.FindMatches(S("It re-synced.")).Select(m => m.Text));
        Assert.Equal(new[] { "re-syncing" }, plain.FindMatches(S("It is re-syncing.")).Select(m => m.Text));
        Assert.Empty(plain.FindMatches(S("It will resync.")));
        Assert.Empty(plain.FindMatches(S("It will re sync.")));
        Assert.Equal("resync", Assert.Single(withVariant.FindMatches(S("It will resync."))).Text);
    }

    /// <summary>
    ///     Proves the documented edge cases of matching: the longest of overlapping entries, punctuation around a term, a hyphen that joins the term to the next word, and a term that is not contiguous.
    /// </summary>
    [Fact]
    public void Glossary_FindMatches_DocumentedEdgeCases_BehaveAsDocumented()
    {
        // Arrange: the glossary of the edge-case example, plus a separate entry for the verb with the pronoun inside
        var glossary = Glossary.Create(new[]
        {
            new GlossaryEntry("bleed valve", Upos.Noun),
            new GlossaryEntry("bleed valve cover", Upos.Noun),
            new GlossaryEntry("Contoso Hub", Upos.Propn),
            new GlossaryEntry("shut down", Upos.Verb),
            new GlossaryEntry("shut it down", Upos.Verb).WithHead(0),
        });

        // Act
        var longest = glossary.FindMatches(S("Remove the bleed valve cover."));
        var shorter = glossary.FindMatches(S("Close the bleed valve."));
        var bracketed = glossary.FindMatches(S("Restart (Contoso Hub), then wait."));
        var final = glossary.FindMatches(S("Restart the Contoso Hub."));
        var hyphenated = glossary.FindMatches(S("Restart the Contoso Hub-based gear."));
        var shutIt = glossary.FindMatches(S("Shut it down now."));
        var shutDown = glossary.FindMatches(S("Shut down now."));

        // Assert: the longest term wins at its position, the shorter term matches when the longer one is not there
        Assert.Equal("bleed valve cover", Assert.Single(longest).Text);
        Assert.Equal(4, longest[0].HeadToken);
        Assert.Equal("bleed valve", Assert.Single(shorter).Text);

        // Assert: punctuation is separate tokens, so it neither prevents the match nor belongs to it
        Assert.Equal("Contoso Hub", Assert.Single(bracketed).Text);
        Assert.Equal(2, bracketed[0].FirstToken);
        Assert.Equal(2, bracketed[0].TokenCount);
        Assert.Equal("Contoso Hub", Assert.Single(final).Text);

        // Assert: a hyphen inside a word joins it, so the word is not the last word of the term
        Assert.Empty(hyphenated);

        // Assert: the term must be contiguous; the two-word entry does not match with a word inside, a separate entry does
        Assert.Equal(new[] { "shut it down" }, shutIt.Select(m => m.Entry.Term));
        Assert.Equal(new[] { "shut down" }, shutDown.Select(m => m.Entry.Term));
    }

    /// <summary>
    ///     Proves the heads that the edge-case example of the Glossary page documents.
    /// </summary>
    [Fact]
    public void Glossary_EdgeCaseExample_ReportsTheDocumentedMatchesAndHeads()
    {
        // Arrange: the glossary of the example
        var glossary = Glossary.Create(new[]
        {
            new GlossaryEntry("bleed valve", Upos.Noun),
            new GlossaryEntry("bleed valve cover", Upos.Noun),
            new GlossaryEntry("Contoso Hub", Upos.Propn),
            new GlossaryEntry("look after", Upos.Verb).WithHead(0),
            new GlossaryEntry("look forward to", Upos.Verb).WithHead(0),
        });
        var texts = new[]
        {
            "Remove the bleed valve cover.",
            "Close the bleed valve.",
            "Restart (Contoso Hub), then wait.",
            "Restart the Contoso Hub.",
            "Please look after the pump.",
            "They look forward to the test.",
        };

        // Act
        var printed = texts.SelectMany(text => glossary.FindMatches(S(text))).Select(match => $"{match.Text} (head {match.HeadToken})");

        // Assert
        Assert.Equal(
            new[]
            {
                "bleed valve cover (head 4)",
                "bleed valve (head 3)",
                "Contoso Hub (head 3)",
                "Contoso Hub (head 3)",
                "look after (head 1)",
                "look forward to (head 1)",
            },
            printed);
    }

    /// <summary>
    ///     Proves how the head of a phrasal verb with a particle that is not in the list is chosen, and that WithHead fixes it: <c>look after</c> and <c>look forward to</c> have the last word as head by default, and <c>get back together</c> (all later words listed) has the first.
    /// </summary>
    [Fact]
    public void GlossaryEntry_HeadIndex_UnknownParticleAndLongerPhrasalVerbs_FollowTheDocumentedRule()
    {
        // Arrange / Act
        var lookAfter = new GlossaryEntry("look after", Upos.Verb);
        var lookForwardTo = new GlossaryEntry("look forward to", Upos.Verb);
        var getBackTogether = new GlossaryEntry("get back together", Upos.Verb);

        // Assert: after and to are not in the list of particle words, so the last word is the head until WithHead says otherwise
        Assert.Equal(1, lookAfter.HeadIndex);
        Assert.Equal(0, lookAfter.WithHead(0).HeadIndex);
        Assert.Equal(2, lookForwardTo.HeadIndex);
        Assert.Equal(0, lookForwardTo.WithHead(0).HeadIndex);

        // Assert: back and together are listed, so the first word is the head by default
        Assert.Equal(0, getBackTogether.HeadIndex);
    }

    /// <summary>
    ///     Proves with the embedded model that the default head of <c>look after</c> forces the preposition to be a verb in hard mode, that WithHead(0) makes the verb the head and limits the particle, and that the later words of a three-word phrasal verb are left to the model.
    /// </summary>
    [Fact]
    public void Tagger_TagWithGlossary_PhrasalVerbHeads_ForceOrLeaveTagsAsDocumented()
    {
        // Arrange
        var tagger = Tagger.Default;
        var byDefault = Glossary.Create(new[] { new GlossaryEntry("look after", Upos.Verb) });
        var fixedHead = Glossary.Create(new[] { new GlossaryEntry("look after", Upos.Verb).WithHead(0) });
        var longer = Glossary.Create(new[]
        {
            new GlossaryEntry("look forward to", Upos.Verb).WithHead(0),
            new GlossaryEntry("get back together", Upos.Verb),
        });

        // Act
        var wrongHead = tagger.Tag("Please look after the pump.", byDefault)[0];
        var rightHead = tagger.Tag("Please look after the pump.", fixedHead)[0];
        var threeWords = tagger.Tag("We look forward to the test. They get back together.", longer);

        // Assert: without WithHead the last word is the head and a hard entry makes it a verb
        Assert.Equal(2, Assert.Single(wrongHead.GlossaryMatches).HeadToken);
        Assert.Equal(Upos.Verb, wrongHead.Tokens[2].Tag);

        // Assert: with WithHead(0) the verb is the head with certainty and the particle is limited to particle tags
        Assert.Equal(1, Assert.Single(rightHead.GlossaryMatches).HeadToken);
        Assert.Equal(Upos.Verb, rightHead.Tokens[1].Tag);
        Assert.Equal(1.0, rightHead.Tokens[1].Confidence);
        Assert.True(UposSet.Of(Upos.Adp, Upos.Adv, Upos.Part).Contains(rightHead.Tokens[2].Tag));

        // Assert: in a term of three words only the head is constrained; the other words are left to the model
        var forward = Assert.Single(threeWords[0].GlossaryMatches);
        Assert.Equal(3, forward.TokenCount);
        Assert.Equal(Upos.Verb, threeWords[0].Tokens[forward.HeadToken].Tag);
        Assert.False(threeWords[0].Tokens[forward.FirstToken + 1].IsConstrained);
        Assert.False(threeWords[0].Tokens[forward.FirstToken + 2].IsConstrained);
        var together = Assert.Single(threeWords[1].GlossaryMatches);
        Assert.Equal(together.FirstToken, together.HeadToken);
        Assert.False(threeWords[1].Tokens[together.FirstToken + 1].IsConstrained);
        Assert.False(threeWords[1].Tokens[together.FirstToken + 2].IsConstrained);
    }

    /// <summary>
    ///     Proves that the confidence of a head word that a hard entry limits to one tag is 1.0 also when the entry is wrong, so that it cannot be read as evidence for the entry, and that soft mode lets the model overrule a wrong entry.
    /// </summary>
    [Fact]
    public void Tagger_TagWithGlossary_WrongHardEntry_HeadConfidenceIsStillOne()
    {
        // Arrange: a wrong entry (valve is a noun here) in hard mode and in soft mode
        var hard = Glossary.Create(new[] { new GlossaryEntry("valve", Upos.Verb) });
        var soft = Glossary.Create(new[] { new GlossaryEntry("valve", Upos.Verb) }, new GlossaryOptions { Mode = GlossaryMode.Soft });

        // Act
        var hardHead = Tagger.Default.Tag("Open the valve.", hard)[0].Tokens[2];
        var softHead = Tagger.Default.Tag("Open the valve.", soft)[0].Tokens[2];

        // Assert: hard mode forces the wrong tag with confidence 1.0 and no alternatives; soft mode lets the model overrule the wrong entry
        Assert.Equal(Upos.Verb, hardHead.Tag);
        Assert.Equal(1.0, hardHead.Confidence);
        Assert.Empty(hardHead.Alternatives);
        Assert.Equal(Upos.Noun, softHead.Tag);
    }

    /// <summary>
    ///     Proves the example of the sentence overload of Tag: a caller constraint that contradicts a hard entry wins for its token, and without a constraint the entry applies.
    /// </summary>
    [Fact]
    public void Tagger_TagWithGlossary_CallerConstraintContradictingHardEntry_CallerWins()
    {
        // Arrange: the entry says NOUN for valve; the caller allows only VERB for it
        var glossary = Glossary.Create(new[] { new GlossaryEntry("bleed valve", Upos.Noun) });
        var sentence = S("Close the bleed valve.");
        var constraints = sentence.Tokens
            .Select(token => token.Text == "valve" ? TokenConstraint.Only(Upos.Verb) : TokenConstraint.None)
            .ToArray();

        // Act
        var caller = Tagger.Default.Tag(sentence, glossary, constraints);
        var plain = Tagger.Default.Tag(sentence, glossary);

        // Assert: the caller wins for the contradicted token; without the constraint the glossary decides
        Assert.Equal(Upos.Verb, caller.Tokens[3].Tag);
        Assert.Equal(Upos.Noun, plain.Tokens[3].Tag);
        Assert.Equal(Upos.Noun, plain.Tokens[2].Tag);
    }

    /// <summary>
    ///     Proves the values that the examples of the entry methods print.
    /// </summary>
    [Fact]
    public void GlossaryEntry_MethodExamples_GiveTheDocumentedValues()
    {
        // Arrange / Act: WithHead, WithTokenTags, WithInflection and WithVariants as in their examples
        var lookAfter = new GlossaryEntry("look after", Upos.Verb);
        var hub = new GlossaryEntry("Contoso Hub", Upos.Propn).WithTokenTags(0, UposSet.Of(Upos.Propn));
        var tap = Glossary.Create(new[] { new GlossaryEntry("tap", Upos.Noun).WithInflection(false) });
        var bleed = new GlossaryEntry("bleed", Upos.Verb).WithVariants("bled");
        var again = bleed.WithVariants("bleed", "bled", "bled");
        var valve = new GlossaryEntry("release valve", Upos.Noun).WithVariants("relief valve");

        // Assert
        Assert.Equal(1, lookAfter.HeadIndex);
        Assert.Equal(0, lookAfter.WithHead(0).HeadIndex);
        Assert.Equal(1, hub.HeadIndex);
        Assert.True(hub.GetTokenTags(0)?.Contains(Upos.Propn));
        Assert.Null(hub.GetTokenTags(1));
        Assert.Single(tap.FindMatches(S("Open the tap.")));
        Assert.Empty(tap.FindMatches(S("Open the taps.")));
        Assert.Equal("bled", string.Join(", ", again.Variants));
        Assert.Single(valve.Variants);
        Assert.Equal("relief valve", Assert.Single(Glossary.Create(new[] { valve }).FindMatches(S("Open the relief valve."))).Text);
    }
}
