// cspell:ignore bleeds resume sume ecole Ecole straße nai valx stra precomposed Brien Cquoted stanbul buse
namespace DemaConsulting.PartOfSpeech.Tests.Tagging;

/// <summary>
///     Tests of how a <see cref="Glossary"/> finds terms in sentences: the longest-first, left-to-right matching rules,
///     case handling, tokenization conventions, inflected forms and variants, the result objects, and the allocation
///     and thread-safety guarantees of matching.
/// </summary>
public class GlossaryMatchingTests
{
    /// <summary>
    ///     Tokenizes a text and returns its first sentence.
    /// </summary>
    private static Sentence S(string text) => Tokenizer.Tokenize(text)[0];

    /// <summary>
    ///     Finds the matches in the first sentence of a text and formats them as <c>text</c> joined by <c>|</c>.
    /// </summary>
    private static string Found(Glossary glossary, string text) =>
        string.Join("|", glossary.FindMatches(S(text)).Select(m => m.Text));

    /// <summary>
    ///     Creates a glossary of case-insensitive noun entries from terms.
    /// </summary>
    private static Glossary Nouns(params string[] terms) =>
        Glossary.Create(terms.Select(t => new GlossaryEntry(t, Upos.Noun)));

    /// <summary>
    ///     Proves that the longest term wins over a shorter term that starts at the same position.
    /// </summary>
    [Fact]
    public void Glossary_FindMatches_LongestFirst_PrefersLongerTerm()
    {
        // Arrange: a term and a longer term that contains it
        var glossary = Nouns("valve", "bleed valve", "main bleed valve");

        // Act: search texts that contain each of them
        var longest = Found(glossary, "Close the main bleed valve now.");
        var middle = Found(glossary, "Close the bleed valve now.");
        var shortest = Found(glossary, "Close the valve now.");

        // Assert: always the longest term at the position
        Assert.Equal("main bleed valve", longest);
        Assert.Equal("bleed valve", middle);
        Assert.Equal("valve", shortest);
    }

    /// <summary>
    ///     Proves that the leftmost match wins and that matches never overlap.
    /// </summary>
    [Fact]
    public void Glossary_FindMatches_LeftToRight_DoesNotOverlap()
    {
        // Arrange: two terms that overlap in the middle word
        var glossary = Nouns("alpha beta", "beta gamma");

        // Act: search the text where the overlap occurs
        var matches = glossary.FindMatches(S("see alpha beta gamma now"));

        // Assert: the leftmost term is taken and the overlapping one is not
        var match = Assert.Single(matches);
        Assert.Equal("alpha beta", match.Text);
        Assert.Equal(1, match.FirstToken);
        Assert.Equal(2, match.TokenCount);
    }

    /// <summary>
    ///     Proves that terms that follow each other directly are all found.
    /// </summary>
    [Fact]
    public void Glossary_FindMatches_AdjacentMatches_AreAllFound()
    {
        // Arrange: three terms
        var glossary = Nouns("alpha beta", "gamma", "delta epsilon");

        // Act: search a text in which they follow each other
        var matches = glossary.FindMatches(S("alpha beta gamma delta epsilon"));

        // Assert: three matches in order, covering tokens 0-1, 2 and 3-4
        Assert.Equal(new[] { "alpha beta", "gamma", "delta epsilon" }, matches.Select(m => m.Text));
        Assert.Equal(new[] { 0, 2, 3 }, matches.Select(m => m.FirstToken));
        Assert.Equal(new[] { 2, 1, 2 }, matches.Select(m => m.TokenCount));
    }

    /// <summary>
    ///     Proves that the beginning of a longer term is not a match when the term is not completed.
    /// </summary>
    [Fact]
    public void Glossary_FindMatches_PrefixOfLongerTerm_NoMatchWhenIncomplete()
    {
        // Arrange: only a three-word term
        var glossary = Nouns("main bleed valve");

        // Act / Assert: the first two words alone are not the term, and neither is a different third word
        Assert.Equal(string.Empty, Found(glossary, "the main bleed is open"));
        Assert.Equal(string.Empty, Found(glossary, "the main bleed valx"));
        Assert.Equal("main bleed valve", Found(glossary, "the main bleed valve is open"));
    }

    /// <summary>
    ///     Proves that a shorter term wins when a longer walk through the trie does not end in a term.
    /// </summary>
    [Fact]
    public void Glossary_FindMatches_LongerWalkWithoutTerm_FallsBackToShorterTerm()
    {
        // Arrange: a term and a longer term that shares its start
        var glossary = Nouns("bleed", "bleed valve assembly");

        // Act: search a text where the longer term is started but not completed
        var matches = glossary.FindMatches(S("the bleed valve is open"));

        // Assert: the shorter term is found, and the search continues after it
        var match = Assert.Single(matches);
        Assert.Equal("bleed", match.Text);
        Assert.Equal(1, match.TokenCount);
    }

    /// <summary>
    ///     Proves that entries are case-insensitive by default.
    /// </summary>
    [Fact]
    public void Glossary_FindMatches_CaseInsensitiveDefault_MatchesAnyCase()
    {
        // Arrange: a lower-case term
        var glossary = Nouns("bleed valve");

        // Act / Assert: lower case, capitalized, and upper case all match, and the text keeps its own case
        Assert.Equal("bleed valve", Found(glossary, "the bleed valve"));
        Assert.Equal("Bleed valve", Found(glossary, "Bleed valve is open."));
        Assert.Equal("BLEED VALVE", Found(glossary, "the BLEED VALVE"));
    }

    /// <summary>
    ///     Proves that a case-sensitive entry matches only its exact letters.
    /// </summary>
    [Fact]
    public void Glossary_FindMatches_CaseSensitiveEntry_ExactCaseOnly()
    {
        // Arrange: a case-sensitive acronym
        var glossary = Glossary.Create(new[] { new GlossaryEntry("IT", Upos.Noun).WithCaseSensitive(true) });

        // Act / Assert: the acronym matches, its other spellings do not
        Assert.Equal("IT", Found(glossary, "call IT now"));
        Assert.Equal(string.Empty, Found(glossary, "call it now"));
        Assert.Equal(string.Empty, Found(glossary, "call It now"));
    }

    /// <summary>
    ///     Proves that a case-sensitive entry wins over a case-insensitive entry of the same term where its letters match.
    /// </summary>
    [Fact]
    public void Glossary_FindMatches_CaseSensitiveWinsOverInsensitiveAtSameLength()
    {
        // Arrange: an insensitive pronoun entry and a sensitive acronym entry for the same term
        var glossary = Glossary.Create(new[]
        {
            new GlossaryEntry("it", Upos.Pron),
            new GlossaryEntry("IT", Upos.Noun).WithCaseSensitive(true),
        });

        // Act: search both spellings
        var acronym = glossary.FindMatches(S("call IT now")).Single();
        var pronoun = glossary.FindMatches(S("call it now")).Single();
        var capitalized = glossary.FindMatches(S("It rains")).Single();

        // Assert: the exact-case entry wins for IT, and the insensitive one applies elsewhere
        Assert.Equal(UposSet.Of(Upos.Noun), acronym.Entry.Tags);
        Assert.Equal(UposSet.Of(Upos.Pron), pronoun.Entry.Tags);
        Assert.Equal(UposSet.Of(Upos.Pron), capitalized.Entry.Tags);
    }

    /// <summary>
    ///     Proves that the glossary-wide case rule applies to entries without their own rule, and that an entry can override it.
    /// </summary>
    [Fact]
    public void Glossary_FindMatches_GlossaryWideCaseSensitive_CanBeOverriddenPerEntry()
    {
        // Arrange: a glossary that is case-sensitive by default, with one insensitive entry
        var glossary = Glossary.Create(
            new[]
            {
                new GlossaryEntry("Valve", Upos.Noun),
                new GlossaryEntry("wrench", Upos.Noun).WithCaseSensitive(false),
            },
            new GlossaryOptions { CaseSensitive = true });

        // Act / Assert: "Valve" needs its capital, "wrench" does not care
        Assert.Equal("Valve", Found(glossary, "the Valve is"));
        Assert.Equal(string.Empty, Found(glossary, "the valve is"));
        Assert.Equal("WRENCH", Found(glossary, "the WRENCH is"));
    }

    /// <summary>
    ///     Proves that a hyphenated term is one token and does not match the same words separated by a space, and the reverse.
    /// </summary>
    [Fact]
    public void Glossary_FindMatches_HyphenatedTermIsOneToken_DoesNotMatchSpaced()
    {
        // Arrange: a hyphenated term and a spaced term
        var hyphenated = Nouns("bleed-valve");
        var spaced = Nouns("bleed valve");

        // Act / Assert: each matches only its own spelling
        Assert.Equal("bleed-valve", Found(hyphenated, "the bleed-valve is open"));
        Assert.Equal(string.Empty, Found(hyphenated, "the bleed valve is open"));
        Assert.Equal("bleed valve", Found(spaced, "the bleed valve is open"));
        Assert.Equal(string.Empty, Found(spaced, "the bleed-valve is open"));
    }

    /// <summary>
    ///     Proves that a contraction in a term is split like in running text, so that the term matches the text.
    /// </summary>
    [Fact]
    public void Glossary_FindMatches_ContractionInTerm_SplitsLikeTokenizer()
    {
        // Arrange: a term with a contraction; the head is the last token (n't) by default
        var glossary = Nouns("don't");

        // Act: search a text with the contraction
        var match = glossary.FindMatches(S("I don't know")).Single();

        // Assert: two tokens covered, head on the second
        Assert.Equal("don't", match.Text);
        Assert.Equal(2, match.TokenCount);
        Assert.Equal(1, match.FirstToken);
        Assert.Equal(2, match.HeadToken);
    }

    /// <summary>
    ///     Proves that a term with a kept period matches in the middle of a text and at the end of a text.
    /// </summary>
    [Fact]
    public void Glossary_FindMatches_AbbreviationPeriod_MidTextAndEndOfText()
    {
        // Arrange: an abbreviation that keeps its period in the middle of a text but not at the end, and a dotted acronym
        var glossary = Nouns("etc.", "U.S.");

        // Act: search the abbreviation in the middle and at the end, and the acronym at the end
        var middle = glossary.FindMatches(S("pens, etc. and more")).Single();
        var end = glossary.FindMatches(S("pens, etc.")).Single();
        var acronym = glossary.FindMatches(S("in the U.S.")).Single();

        // Assert: one token in the middle, the word and the period at the end, and the acronym stays one token
        Assert.Equal("etc.", middle.Text);
        Assert.Equal(1, middle.TokenCount);
        Assert.Equal("etc.", end.Text);
        Assert.Equal(2, end.TokenCount);
        Assert.Equal(2, end.HeadToken);
        Assert.Equal("U.S.", acronym.Text);
        Assert.Equal(1, acronym.TokenCount);
    }

    /// <summary>
    ///     Proves that typographic apostrophes and quotes match their ASCII forms in both directions.
    /// </summary>
    [Fact]
    public void Glossary_FindMatches_CurlyApostropheEqualsStraight()
    {
        // Arrange: a term with a straight apostrophe and a term with a curly one
        var straight = Nouns("o'clock");
        var curly = Nouns("o\u2019clock");

        // Act / Assert: each matches both spellings in the text
        Assert.Equal("o\u2019clock", Found(straight, "at five o\u2019clock sharp"));
        Assert.Equal("o'clock", Found(straight, "at five o'clock sharp"));
        Assert.Equal("o\u2019clock", Found(curly, "at five o\u2019clock sharp"));
        Assert.Equal("o'clock", Found(curly, "at five o'clock sharp"));
    }

    /// <summary>
    ///     Proves that a term never matches across two sentences.
    /// </summary>
    [Fact]
    public void Glossary_FindMatches_TermSplitAcrossSentences_NoMatch()
    {
        // Arrange: a two-word term and a text whose sentence break falls between the words
        var glossary = Nouns("bleed valve");
        var sentences = Tokenizer.Tokenize("Close the bleed. Valve open.");

        // Act: search each sentence
        var matches = sentences.SelectMany(glossary.FindMatches).ToList();

        // Assert: two sentences and no match
        Assert.Equal(2, sentences.Count);
        Assert.Empty(matches);
    }

    /// <summary>
    ///     Proves that a token with punctuation stuck to it (pre-tokenized input) does not match the bare term.
    /// </summary>
    [Fact]
    public void Glossary_FindMatches_PreTokenizedPunctuationStuckToWord_DoesNotMatch()
    {
        // Arrange: a one-word term and two pre-split token lists
        var glossary = Nouns("valve");
        var stuck = Tokenizer.CreateSentence(new[] { "close", "the", "valve." });
        var split = Tokenizer.CreateSentence(new[] { "close", "the", "valve", "." });

        // Act / Assert: only the properly split tokens match
        Assert.Empty(glossary.FindMatches(stuck));
        Assert.Single(glossary.FindMatches(split));
    }

    /// <summary>
    ///     Proves that tokens with non-ASCII characters are normalized and matched, also in another letter case and normalization form.
    /// </summary>
    [Fact]
    public void Glossary_FindMatches_NonAsciiToken_SlowPathMatches()
    {
        // Arrange: a term with accented letters (precomposed)
        var glossary = Nouns("r\u00E9sum\u00E9", "stra\u00DFe");

        // Act / Assert: the same text precomposed, decomposed, in upper case, and with a sharp s
        Assert.Equal("r\u00E9sum\u00E9", Found(glossary, "my r\u00E9sum\u00E9 is"));
        Assert.Equal("re\u0301sume\u0301", Found(glossary, "my re\u0301sume\u0301 is"));
        Assert.Equal("R\u00C9SUM\u00C9", Found(glossary, "my R\u00C9SUM\u00C9 is"));
        Assert.Equal("stra\u00DFe", Found(glossary, "the stra\u00DFe is"));
    }

    /// <summary>
    ///     Proves that a hash collision does not produce a wrong match: the characters decide.
    /// </summary>
    [Fact]
    public void Glossary_FindMatches_HashCollision_ResolvedByComparison()
    {
        // Arrange: many terms in a glossary whose index hashes every token to the same value
        var entries = Enumerable.Range(0, 60).Select(i => new GlossaryEntry($"alpha{i} beta{i}", Upos.Noun)).ToList();
        var glossary = Glossary.Create(entries, null, 0UL);

        // Act: search a text with a few of the terms, a mismatched pair and unrelated words
        var matches = glossary.FindMatches(S("alpha7 beta7 and alpha8 beta9 and alpha59 beta59 and gamma1")).Select(m => m.Text).ToList();

        // Assert: exactly the three true pairs
        Assert.Equal(new[] { "alpha7 beta7", "alpha59 beta59" }, matches);
    }

    /// <summary>
    ///     Proves that the fast folding of sentence tokens equals the string folding that terms use, for all kinds of characters.
    /// </summary>
    [Theory]
    [InlineData("Hello")]
    [InlineData("O\u2019Brien")]
    [InlineData("well\u2014known")]
    [InlineData("wait\u2026")]
    [InlineData("\u201Cquoted\u201D")]
    [InlineData("\u00DCn\u00EF")]
    [InlineData("e\u0301")]
    [InlineData("\u00C9COLE")]
    [InlineData("\u0130stanbul")]
    [InlineData("\uD835\uDCB3y")]
    [InlineData("\uD83D")]
    [InlineData("\uFF21\uFF42\uFF43")]
    [InlineData("a\u2013b\u2015c")]
    public void GlossaryIndex_FoldWithWorkspace_AgreesWithFoldToString(string token)
    {
        // Arrange: one workspace
        var workspace = new GlossaryWorkspace();

        // Act: fold with the in-place path and with the string path
        var exact = GlossaryIndex.FoldWithWorkspace(token, workspace, false);
        var lower = GlossaryIndex.FoldWithWorkspace(token, workspace, true);

        // Assert: both forms agree with the string folding
        Assert.Equal(GlossaryIndex.FoldToString(token, false), exact);
        Assert.Equal(GlossaryIndex.FoldToString(token, true), lower);
    }

    /// <summary>
    ///     Proves that an empty sentence and an empty glossary give no matches.
    /// </summary>
    [Fact]
    public void Glossary_FindMatches_EmptySentenceOrEmptyGlossary_NoMatches()
    {
        // Arrange: an empty sentence, and a glossary with a term
        var empty = Tokenizer.CreateSentence(new string[0]);
        var glossary = Nouns("valve");

        // Act / Assert: nothing to find in an empty sentence, and the empty glossary finds nothing
        Assert.Empty(glossary.FindMatches(empty));
        Assert.Empty(Glossary.Empty.FindMatches(S("the valve")));
    }

    /// <summary>
    ///     Proves that a null sentence is rejected.
    /// </summary>
    [Fact]
    public void Glossary_FindMatches_NullSentence_ThrowsArgumentNullException()
    {
        // Arrange / Act / Assert: null sentence
        Assert.Throws<ArgumentNullException>(() => Glossary.Empty.FindMatches(null!));
    }

    /// <summary>
    ///     Proves that inflected forms of the head word are found and reported as variants, while the term as written is not.
    /// </summary>
    [Fact]
    public void Glossary_FindMatches_InflectedForms_AreFoundAndReportedAsVariants()
    {
        // Arrange: a noun compound and a verb with inflection on
        var glossary = Glossary.Create(new[]
        {
            new GlossaryEntry("bleed valve", Upos.Noun),
            new GlossaryEntry("isolate", Upos.Verb),
        });

        // Act: search the term, a plural, and the verb forms
        var term = glossary.FindMatches(S("the bleed valve")).Single();
        var plural = glossary.FindMatches(S("the bleed valves")).Single();
        var past = glossary.FindMatches(S("it isolated")).Single();
        var participle = glossary.FindMatches(S("by isolating it")).Single();

        // Assert: the term is not a variant, the generated forms are
        Assert.False(term.IsVariant);
        Assert.True(plural.IsVariant);
        Assert.True(past.IsVariant);
        Assert.True(participle.IsVariant);
        Assert.Equal("isolated", past.Text);
    }

    /// <summary>
    ///     Proves that the other words of a term are not inflected.
    /// </summary>
    [Fact]
    public void Glossary_FindMatches_OnlyHeadWordIsInflected()
    {
        // Arrange: a two-word term
        var glossary = Nouns("bleed valve");

        // Act / Assert: the plural of the head matches, the plural of the first word does not
        Assert.Equal("bleed valves", Found(glossary, "the bleed valves"));
        Assert.Equal(string.Empty, Found(glossary, "the bleeds valve"));
    }

    /// <summary>
    ///     Proves that the inflection of the head follows WithHead.
    /// </summary>
    [Fact]
    public void Glossary_FindMatches_HeadWordMovedByWithHead_IsTheOneInflected()
    {
        // Arrange: a verb term whose head is moved to the first word, although the last word is the default
        var glossary = Glossary.Create(new[] { new GlossaryEntry("take care", Upos.Verb).WithHead(0) });

        // Act / Assert: "takes care" matches, "take cares" does not
        Assert.Equal("takes care", Found(glossary, "it takes care now"));
        Assert.Equal(string.Empty, Found(glossary, "it take cares now"));
    }

    /// <summary>
    ///     Proves that an inferred head is the head that matches report and inflect, and that an explicit head overrides it.
    /// </summary>
    [Fact]
    public void Glossary_FindMatches_InferredPhrasalHead_IsReportedAndInflected()
    {
        // Arrange: a phrasal verb with a listed particle word without WithHead, and the same entry with the head set to the last word
        var inferred = Glossary.Create(new[] { new GlossaryEntry("shut down", Upos.Verb) });
        var explicitLast = Glossary.Create(new[] { new GlossaryEntry("shut down", Upos.Verb).WithHead(1) });

        // Act: find "shuts down" in "it shuts down now" (the match starts at token 1)
        var inferredMatch = Assert.Single(inferred.FindMatches(S("it shuts down now")));
        var explicitMatch = Assert.Single(explicitLast.FindMatches(S("it shut down now")));

        // Assert: the first word is the head and is the inflected one; with the explicit last head the match reports the last word
        Assert.Equal("shuts down", inferredMatch.Text);
        Assert.Equal(1, inferredMatch.HeadToken);
        Assert.True(inferredMatch.IsVariant);
        Assert.Equal("shut down", explicitMatch.Text);
        Assert.Equal(2, explicitMatch.HeadToken);
        Assert.Equal("shut down", Found(inferred, "it shut down now"));
        Assert.Equal("shutting down", Found(inferred, "it is shutting down now"));
    }

    /// <summary>
    ///     Proves that inflection can be switched off for the glossary and for one entry, and on again for one entry.
    /// </summary>
    [Fact]
    public void Glossary_FindMatches_InflectionSwitches_AreHonored()
    {
        // Arrange: inflection off glossary-wide with one entry that switches it on, and the reverse
        var offWithException = Glossary.Create(
            new[] { new GlossaryEntry("valve", Upos.Noun), new GlossaryEntry("wrench", Upos.Noun).WithInflection(true) },
            new GlossaryOptions { Inflect = false });
        var onWithException = Glossary.Create(new[] { new GlossaryEntry("valve", Upos.Noun).WithInflection(false), new GlossaryEntry("wrench", Upos.Noun) });

        // Act / Assert: the entry setting wins in both glossaries
        Assert.Equal("wrenches", Found(offWithException, "the wrenches and valves"));
        Assert.Equal("wrenches", Found(onWithException, "the wrenches and valves"));
        Assert.Equal("valves|wrenches", Found(Nouns("valve", "wrench"), "the valves and wrenches"));
    }

    /// <summary>
    ///     Proves that explicit variants are matched with the head tags and reported as variants, also those that rules cannot produce.
    /// </summary>
    [Fact]
    public void Glossary_FindMatches_ExplicitVariants_AreMatched()
    {
        // Arrange: an irregular verb with its past tense as a variant
        var glossary = Glossary.Create(new[] { new GlossaryEntry("bleed", UposSet.Of(Upos.Verb, Upos.Noun)).WithVariants("bled") });

        // Act: search the irregular form and the term
        var variant = glossary.FindMatches(S("it bled")).Single();
        var term = glossary.FindMatches(S("it bleed")).Single();

        // Assert: the variant is reported as such
        Assert.True(variant.IsVariant);
        Assert.False(term.IsVariant);
        Assert.Equal("bled", variant.Text);
    }

    /// <summary>
    ///     Proves that a term as written beats a generated form of another entry with the same spelling.
    /// </summary>
    [Fact]
    public void Glossary_FindMatches_TermBeatsVariantBeatsGeneratedForm()
    {
        // Arrange: "valves" is the term of one entry, a listed variant of another, and a generated form of "valve"
        var glossary = Glossary.Create(new[]
        {
            new GlossaryEntry("valve", Upos.Noun),
            new GlossaryEntry("tap", Upos.Noun).WithVariants("valves"),
            new GlossaryEntry("valves", Upos.Verb),
        });

        // Act: search the contested spelling
        var match = glossary.FindMatches(S("the valves")).Single();

        // Assert: the entry that lists it as a term wins
        Assert.Equal("valves", match.Entry.Term);
        Assert.False(match.IsVariant);
    }

    /// <summary>
    ///     Proves that a listed variant beats a generated form, and that the earlier of two generated forms wins.
    /// </summary>
    [Fact]
    public void Glossary_FindMatches_VariantBeatsGeneratedForm_EarlierEntryWinsOtherwise()
    {
        // Arrange: "valves" is a listed variant of "tap" and a generated form of "valve" (listed first), and "buses" is generated from two entries
        var glossary = Glossary.Create(new[]
        {
            new GlossaryEntry("valve", Upos.Noun),
            new GlossaryEntry("tap", Upos.Noun).WithVariants("valves"),
            new GlossaryEntry("bus", Upos.Noun),
            new GlossaryEntry("buse", Upos.Noun),
        });

        // Act: search both
        var valves = glossary.FindMatches(S("the valves")).Single();
        var buses = glossary.FindMatches(S("the buses")).Single();

        // Assert: the variant wins over the generated form; for two generated forms the earlier entry wins
        Assert.Equal("tap", valves.Entry.Term);
        Assert.Equal("bus", buses.Entry.Term);
    }

    /// <summary>
    ///     Proves that the match reports its tokens, head, offsets and text, also across odd white space and line breaks.
    /// </summary>
    [Fact]
    public void GlossaryMatch_Properties_DescribeTheMatchedSpan()
    {
        // Arrange: a text with a line break and extra spaces inside the term, and a second sentence
        const string text = "Open  the\r\nbleed   valve now. Close the valve.";
        var glossary = Glossary.Create(new[] { new GlossaryEntry("bleed valve", Upos.Noun, "Releases air.") });

        // Act: find the matches of every sentence
        var sentences = Tokenizer.Tokenize(text);
        var matches = sentences.SelectMany(glossary.FindMatches).ToList();

        // Assert: one match, covering two tokens with the head on the second, and offsets that cut the source text
        var match = Assert.Single(matches);
        Assert.Equal("bleed   valve", match.Text);
        Assert.Equal(text.Substring(match.Start, match.Length), match.Text);
        Assert.Equal(2, match.FirstToken);
        Assert.Equal(2, match.TokenCount);
        Assert.Equal(3, match.HeadToken);
        Assert.Equal("Releases air.", match.Entry.Definition);
        Assert.Equal(GlossaryMode.Hard, match.Mode);
        Assert.False(match.IsVariant);
        Assert.Equal($"bleed   valve@{match.Start} [bleed valve]", match.ToString());
    }

    /// <summary>
    ///     Proves that the offsets of a match in a later sentence index the whole source text.
    /// </summary>
    [Fact]
    public void GlossaryMatch_Start_InLaterSentence_IndexesWholeText()
    {
        // Arrange: a text with the term in the second sentence
        const string text = "Do it now. Close the bleed valve.";
        var glossary = Nouns("bleed valve");

        // Act: search the second sentence
        var match = glossary.FindMatches(Tokenizer.Tokenize(text)[1]).Single();

        // Assert: the offset indexes the whole text, the token indexes the sentence
        Assert.Equal(text.IndexOf("bleed valve", StringComparison.Ordinal), match.Start);
        Assert.Equal(2, match.FirstToken);
        Assert.Equal("bleed valve", text.Substring(match.Start, match.Length));
    }

    /// <summary>
    ///     Proves that the effective mode of a match is the entry's own mode or the mode of the glossary.
    /// </summary>
    [Fact]
    public void GlossaryMatch_Mode_IsEntryModeOrGlossaryMode()
    {
        // Arrange: a soft glossary with one hard entry
        var glossary = Glossary.Create(
            new[] { new GlossaryEntry("valve", Upos.Noun), new GlossaryEntry("wrench", Upos.Noun).WithMode(GlossaryMode.Hard) },
            new GlossaryOptions { Mode = GlossaryMode.Soft });

        // Act: find both
        var matches = glossary.FindMatches(S("the valve and wrench"));

        // Assert: the glossary mode and the entry mode
        Assert.Equal(new[] { GlossaryMode.Soft, GlossaryMode.Hard }, matches.Select(m => m.Mode));
    }

    /// <summary>
    ///     Proves that the list of matches of a sentence cannot be modified by the caller.
    /// </summary>
    [Fact]
    public void Glossary_FindMatches_ResultList_IsReadOnly()
    {
        // Arrange: a glossary and a sentence with one match
        var matches = Nouns("valve").FindMatches(S("the valve"));

        // Act: try to reach a writable collection behind the view
        var asList = matches as IList<GlossaryMatch>;

        // Assert: the view is read-only
        Assert.IsNotType<GlossaryMatch[]>(matches);
        Assert.True(asList == null || asList.IsReadOnly);
    }

#if NET
    /// <summary>
    ///     Builds a glossary of 10,000 entries (single and two-word terms) and a 200-token sentence of which about 30 percent of the tokens are in matches.
    /// </summary>
    private static (Glossary Glossary, Token[] Tokens) BuildLargeScenario()
    {
        // Arrange: 6,000 single-word and 4,000 two-word terms whose words end in a digit (so no inflected forms are generated)
        var entries = new List<GlossaryEntry>();
        for (var i = 0; i < 6000; i++)
        {
            entries.Add(new GlossaryEntry($"alpha{i}", Upos.Noun));
        }

        for (var i = 0; i < 4000; i++)
        {
            entries.Add(new GlossaryEntry($"beta{i} gamma{i}", Upos.Noun));
        }

        var glossary = Glossary.Create(entries);

        // A sentence of ordinary words with a single-word term at every tenth position and a two-word term after it
        var random = new Random(3);
        var words = new List<string>();
        var ordinary = new[] { "the", "valve", "open", "close", "pressure", "is", "high", "." };
        while (words.Count < 200)
        {
            for (var k = 0; k < 7; k++)
            {
                words.Add(ordinary[random.Next(ordinary.Length)]);
            }

            words.Add($"alpha{random.Next(6000)}");
            var pair = random.Next(4000);
            words.Add($"beta{pair}");
            words.Add($"gamma{pair}");
        }

        return (glossary, Tokenizer.CreateSentence(words).TokenArray);
    }

    /// <summary>
    ///     Proves that matching a 200-token sentence against a glossary of 10,000 entries allocates nothing.
    /// </summary>
    [Fact]
    public void GlossaryIndex_Find_DoesNotAllocate()
    {
        // Arrange: the large scenario and a workspace of this thread
        var (glossary, tokens) = BuildLargeScenario();
        var workspace = new GlossaryWorkspace();
        var found = glossary.Index.Find(tokens, workspace);

        // Act: warm up thoroughly, then take the minimum over several batches of the bytes allocated for 100 searches
        var allocated = AllocationMeter.MinimumBatchBytes(() => glossary.Index.Find(tokens, workspace), 100);

        // Assert: the sentence really has matches (about 30 percent of its tokens), and nothing was allocated
        Assert.True(found >= 40, $"{found} matches");
        Assert.Equal(0, allocated);
    }

    /// <summary>
    ///     Proves that finding matches in a sentence without any match allocates nothing, also through the public result path.
    /// </summary>
    [Fact]
    public void Glossary_Match_SentenceWithoutMatch_DoesNotAllocate()
    {
        // Arrange: the large glossary and a sentence of unrelated words
        var (glossary, _) = BuildLargeScenario();
        var sentence = Tokenizer.CreateSentence(Enumerable.Range(0, 200).Select(i => i % 5 == 0 ? "." : "word" + (i % 7)).ToArray());

        // Act: warm up, then measure
        var allocated = AllocationMeter.MinimumBatchBytes(() => glossary.Match(sentence), 100);

        // Assert: no match and no allocation
        Assert.Empty(glossary.Match(sentence));
        Assert.Equal(0, allocated);
    }
#endif

    /// <summary>
    ///     Proves that many threads can use one glossary at the same time and find exactly what a single thread finds.
    /// </summary>
    [Fact]
    public void Glossary_FindMatches_ParallelUse_MatchesSequentialResults()
    {
        // Arrange: a glossary and sentences with different matches
        var glossary = Glossary.Create(new[]
        {
            new GlossaryEntry("bleed valve", Upos.Noun),
            new GlossaryEntry("torque wrench", Upos.Noun),
            new GlossaryEntry("isolate", Upos.Verb),
            new GlossaryEntry("IT", Upos.Noun).WithCaseSensitive(true),
        });
        var sentences = Enumerable.Range(0, 400)
            .Select(i => S($"Step {i}: isolate the bleed valve {i % 7} and use a torque wrench, then call IT or it {i}."))
            .ToArray();
        string Summarize(Sentence s) => string.Join("|", glossary.FindMatches(s).Select(m => $"{m.FirstToken}:{m.TokenCount}:{m.HeadToken}:{m.Text}"));
        var expected = sentences.Select(Summarize).ToArray();
        var actual = new string[sentences.Length];

        // Act: find the matches from eight threads, several rounds each
        Parallel.For(
            0,
            sentences.Length * 5,
            new ParallelOptions { MaxDegreeOfParallelism = 8 },
            n => actual[n % sentences.Length] = Summarize(sentences[n % sentences.Length]));

        // Assert: the same as sequentially
        Assert.Equal(expected, actual);
        Assert.Contains("torque wrench", expected[0]);
    }

    /// <summary>
    ///     Proves that the rules for non-head tokens produce the documented constraints in both modes.
    /// </summary>
    [Fact]
    public void GlossaryConstraints_NonHead_Rules_ProduceDocumentedConstraints()
    {
        // Arrange: a boost of 3 raw score units
        const double boost = 3.0;

        // Act / Assert: no rule leaves the token alone
        Assert.True(GlossaryConstraints.NonHead(GlossaryNonHeadRule.None, GlossaryMode.Hard, boost).IsNone);
        Assert.True(GlossaryConstraints.NonHead(GlossaryNonHeadRule.None, GlossaryMode.Soft, boost).IsNone);

        // Forbidding verbs and auxiliaries: all other tags (hard) or a penalty for these two (soft)
        Assert.Equal(
            TokenConstraint.Only(UposSet.All.Remove(Upos.Verb).Remove(Upos.Aux)),
            GlossaryConstraints.NonHead(GlossaryNonHeadRule.ForbidVerbAndAux, GlossaryMode.Hard, boost));
        Assert.Equal(
            TokenConstraint.Penalize(Upos.Verb, boost).And(TokenConstraint.Penalize(Upos.Aux, boost)),
            GlossaryConstraints.NonHead(GlossaryNonHeadRule.ForbidVerbAndAux, GlossaryMode.Soft, boost));

        // Particles only: adposition, adverb and particle, limited (hard) or favored (soft)
        Assert.Equal(
            TokenConstraint.Only(Upos.Adp, Upos.Adv, Upos.Part),
            GlossaryConstraints.NonHead(GlossaryNonHeadRule.ParticlesOnly, GlossaryMode.Hard, boost));
        Assert.Equal(
            TokenConstraint.Prefer(Upos.Adp, boost).And(TokenConstraint.Prefer(Upos.Adv, boost)).And(TokenConstraint.Prefer(Upos.Part, boost)),
            GlossaryConstraints.NonHead(GlossaryNonHeadRule.ParticlesOnly, GlossaryMode.Soft, boost));
    }

    /// <summary>
    ///     Proves that the default rule for the second token of a two-token verb term with the head first is the one that the development evaluation chose: particles only.
    /// </summary>
    [Fact]
    public void GlossaryConstraints_NonNominalRule_Default_IsParticlesOnly()
    {
        // Arrange / Act / Assert: the single switch, set by the pre-registered bar of scenario G (section E9 of the trainer's run log)
        Assert.Equal(GlossaryNonHeadRule.ParticlesOnly, GlossaryConstraints.NonNominalRule);
    }

    /// <summary>
    ///     Proves that the verb-forbidding rule applies to entries of noun and proper noun tags only, and that a word in front of the head of any other entry is left to the model.
    /// </summary>
    [Fact]
    public void Tagger_TagWithGlossary_NonHeadRule_NounRuleAppliesToNounAndProperNounEntriesOnly()
    {
        // Arrange: "can" is an auxiliary for the model; the same term as noun, proper noun, noun or verb, and verb entries (the head is the last word)
        var tagger = CreateTagger();
        var tokens = Words("use the can opener .");
        Upos CanTag(UposSet tags) => tagger.Tag(tokens, Glossary.Create(new[] { new GlossaryEntry("can opener", tags) })).Tokens[2].Tag;

        // Act / Assert: nouns and proper nouns forbid the auxiliary; an entry that may be a verb leaves the word in front of the head to the model
        Assert.Equal(Upos.Noun, CanTag(UposSet.Of(Upos.Noun)));
        Assert.Equal(Upos.Noun, CanTag(UposSet.Of(Upos.Noun, Upos.Propn)));
        Assert.NotEqual(Upos.Aux, CanTag(UposSet.Of(Upos.Propn)));
        Assert.Equal(Upos.Aux, CanTag(UposSet.Of(Upos.Noun, Upos.Verb)));
        Assert.Equal(Upos.Aux, CanTag(UposSet.Of(Upos.Verb)));
        Assert.Equal(Upos.Aux, CanTag(UposSet.Of(Upos.Adj)));
    }

    /// <summary>
    ///     Proves that the particle rule applies only to a two-token entry with the tag VERB whose head is the first token, and that every other non-head token is left to the model.
    /// </summary>
    [Fact]
    public void GlossaryConstraints_Compile_ParticleRule_AppliesToTwoTokenVerbTermsWithHeadFirstOnly()
    {
        // Arrange: the particle constraint and a two-token term as verb, adjective, noun and verb-or-noun entry
        var particles = TokenConstraint.Only(Upos.Adp, Upos.Adv, Upos.Part);

        // Act: compile the non-head constraints through a glossary (one entry per case, hard mode)
        TokenConstraint[] Compile(GlossaryEntry entry)
        {
            var glossary = Glossary.Create(new[] { entry });
            var compiled = GlossaryConstraints.Compile(glossary, 1.0);
            var target = new TokenConstraint[entry.Tokens.Count];
            compiled.Apply(glossary.FindMatches(S(entry.Term))[0], target);
            return target;
        }

        var verbHeadFirst = Compile(new GlossaryEntry("shut down", Upos.Verb));
        var verbHeadLast = Compile(new GlossaryEntry("shut down", Upos.Verb).WithHead(1));
        var verbOrNoun = Compile(new GlossaryEntry("shut down", UposSet.Of(Upos.Verb, Upos.Noun)));
        var adjectiveHeadLast = Compile(new GlossaryEntry("shut down", Upos.Adj));
        var adjectiveHeadFirst = Compile(new GlossaryEntry("shut down", Upos.Adj).WithHead(0));
        var explicitTags = Compile(new GlossaryEntry("shut down", Upos.Verb).WithTokenTags(1, UposSet.Of(Upos.Adv)));

        // Assert: only the second token of a two-token verb term with the head first gets the rule; an explicit tag set still wins
        Assert.Equal(particles, verbHeadFirst[1]);
        Assert.True(verbHeadLast[0].IsNone);
        Assert.Equal(particles, verbOrNoun[1]);
        Assert.True(adjectiveHeadLast[0].IsNone);
        Assert.True(adjectiveHeadFirst[1].IsNone);
        Assert.Equal(TokenConstraint.Only(Upos.Adv), explicitTags[1]);
    }

    /// <summary>
    ///     Proves that the particle rule applies to a two-token verb term whose head was inferred, and not when an explicit head says the last token.
    /// </summary>
    [Fact]
    public void GlossaryConstraints_Compile_InferredPhrasalHead_GetsParticleRule()
    {
        // Arrange: the particle constraint; compile the non-head constraints through a glossary (hard mode)
        var particles = TokenConstraint.Only(Upos.Adp, Upos.Adv, Upos.Part);
        TokenConstraint[] Compile(GlossaryEntry entry)
        {
            var glossary = Glossary.Create(new[] { entry });
            var compiled = GlossaryConstraints.Compile(glossary, 1.0);
            var target = new TokenConstraint[entry.Tokens.Count];
            compiled.Apply(glossary.FindMatches(S(entry.Term))[0], target);
            return target;
        }

        // Act: the inferred head, the explicit last head, the explicit first head on a term that is not inferred, and a noun compound
        var inferred = Compile(new GlossaryEntry("shut down", Upos.Verb));
        var explicitLast = Compile(new GlossaryEntry("shut down", Upos.Verb).WithHead(1));
        var explicitFirst = Compile(new GlossaryEntry("take care", Upos.Verb).WithHead(0));
        var compound = Compile(new GlossaryEntry("bleed valve", Upos.Noun));

        // Assert: the second token is limited to particles when the head is the first token, by inference or by WithHead; a last head leaves the first token alone
        Assert.Equal(particles, inferred[1]);
        Assert.True(explicitLast[0].IsNone);
        Assert.Equal(particles, explicitFirst[1]);
        Assert.NotEqual(particles, compound[0]);
    }

    /// <summary>
    ///     Proves that no non-head token of a verb term of three tokens gets the particle rule, wherever the head is, and that explicit tags still win.
    /// </summary>
    [Fact]
    public void GlossaryConstraints_Compile_ThreeTokenVerbTerm_NonHeadTokensAreUnconstrained()
    {
        // Arrange: a three-token verb term with the head first, in the middle and last, and one with explicit tags for the last token
        TokenConstraint[] Compile(GlossaryEntry entry)
        {
            var glossary = Glossary.Create(new[] { entry });
            var compiled = GlossaryConstraints.Compile(glossary, 1.0);
            var target = new TokenConstraint[entry.Tokens.Count];
            compiled.Apply(glossary.FindMatches(S(entry.Term))[0], target);
            return target;
        }

        // Act
        var headFirst = Compile(new GlossaryEntry("shut it down", Upos.Verb).WithHead(0));
        var headMiddle = Compile(new GlossaryEntry("shut it down", Upos.Verb).WithHead(1));
        var headLast = Compile(new GlossaryEntry("shut it down", Upos.Verb));
        var explicitTags = Compile(new GlossaryEntry("shut it down", Upos.Verb).WithHead(0).WithTokenTags(2, UposSet.Of(Upos.Adv)));

        // Assert: the middle and last tokens stay with the model; only the explicit tags constrain a non-head token
        Assert.True(headFirst[1].IsNone);
        Assert.True(headFirst[2].IsNone);
        Assert.True(headMiddle[0].IsNone);
        Assert.True(headMiddle[2].IsNone);
        Assert.True(headLast[0].IsNone);
        Assert.True(headLast[1].IsNone);
        Assert.True(explicitTags[1].IsNone);
        Assert.Equal(TokenConstraint.Only(Upos.Adv), explicitTags[2]);
    }

    /// <summary>
    ///     Proves that a word in front of the head of a verb term, and every word of a term of another kind, stays with the model, so an adjective term is not forced to adverbs.
    /// </summary>
    [Fact]
    public void Tagger_TagWithGlossary_NonVerbTermOrWordBeforeTheHead_IsLeftToTheModel()
    {
        // Arrange: "shut" is an adjective and "down" an adposition for the model
        var tagger = CreateTagger();
        var tokens = Words("shut down .");

        // Act: an adjective term with the head last, and a verb term with an explicit last head
        var adjectiveTerm = tagger.Tag(tokens, Glossary.Create(new[] { new GlossaryEntry("shut down", Upos.Adj) }));
        var verbTermHeadLast = tagger.Tag(tokens, Glossary.Create(new[] { new GlossaryEntry("shut down", Upos.Verb).WithHead(1) }));

        // Assert: the first word is not constrained and keeps the model's tag; only the head is constrained
        Assert.Equal(Upos.Adj, adjectiveTerm.Tokens[0].Tag);
        Assert.False(adjectiveTerm.Tokens[0].IsConstrained);
        Assert.Equal(Upos.Adj, adjectiveTerm.Tokens[1].Tag);
        Assert.Equal(Upos.Adj, verbTermHeadLast.Tokens[0].Tag);
        Assert.False(verbTermHeadLast.Tokens[0].IsConstrained);
        Assert.Equal(Upos.Verb, verbTermHeadLast.Tokens[1].Tag);
    }

    /// <summary>
    ///     Proves how a caller's constraint and a glossary constraint are merged for one token.
    /// </summary>
    [Fact]
    public void GlossaryConstraints_Merge_NoneIntersectionAndContradiction()
    {
        // Arrange: constraints
        var none = TokenConstraint.None;
        var nouns = TokenConstraint.Only(Upos.Noun);
        var nounsAndVerbs = TokenConstraint.Only(Upos.Noun, Upos.Verb);
        var verbs = TokenConstraint.Only(Upos.Verb);

        // Act / Assert: None gives way, an overlap is the intersection, a contradiction keeps the caller's constraint
        Assert.Equal(nouns, GlossaryConstraints.Merge(none, nouns));
        Assert.Equal(nouns, GlossaryConstraints.Merge(nouns, none));
        Assert.Equal(nouns, GlossaryConstraints.Merge(nounsAndVerbs, nouns));
        Assert.Equal(verbs, GlossaryConstraints.Merge(verbs, nouns));
    }

    /// <summary>
    ///     The words of the tiny model, each with the tags that the lexicon lists for it.
    /// </summary>
    private static readonly byte[] TinyLexicon = TinyModels.CreateLexicon(
        "the DET", "a DET", "open VERB ADJ", "close VERB ADJ", "bleed NOUN VERB", "bleeds NOUN VERB", "torque NOUN VERB",
        "valve NOUN", "valves NOUN VERB", "wrench NOUN", "accumulator NOUN", "can NOUN AUX", "opener NOUN", "shut VERB ADJ",
        "down ADV ADP NOUN", "need VERB", "call VERB NOUN", "now ADV", "it PRON NOUN", "use VERB NOUN", "is AUX VERB",
        "and CCONJ", ". PUNCT", ", PUNCT");

    /// <summary>
    ///     Creates the tiny tagger whose every score is known: <c>bleed</c> prefers VERB by 4, <c>torque</c> prefers VERB by
    ///     2, <c>can</c> prefers AUX by 4, <c>shut</c> prefers ADJ by 4 and <c>down</c> prefers ADP by 4 (the rest is decided by the lexicon).
    /// </summary>
    /// <param name="temperature">The softmax temperature of the model.</param>
    /// <param name="alternatives">The maximum number of alternatives per token.</param>
    private static Tagger CreateTagger(float temperature = 1f, int alternatives = 3) =>
        TinyModels.CreateTagger(
            new TinyModels.WeightsBuilder()
                .Word("the", Upos.Det, 5).Word("a", Upos.Det, 5).Word("and", Upos.Cconj, 5).Word(".", Upos.Punct, 5)
                .Word("open", Upos.Verb, 3).Word("open", Upos.Adj, 1).Word("close", Upos.Verb, 3)
                .Word("bleed", Upos.Verb, 5).Word("bleed", Upos.Noun, 1).Word("bleeds", Upos.Verb, 5)
                .Word("torque", Upos.Verb, 3).Word("torque", Upos.Noun, 1)
                .Word("valve", Upos.Noun, 3).Word("valves", Upos.Noun, 3).Word("wrench", Upos.Noun, 3)
                .Word("accumulator", Upos.Noun, 3).Word("opener", Upos.Noun, 3)
                .Word("can", Upos.Aux, 5).Word("can", Upos.Noun, 1)
                .Word("shut", Upos.Adj, 5).Word("shut", Upos.Verb, 1)
                .Word("down", Upos.Adp, 5).Word("down", Upos.Adv, 1)
                .Word("need", Upos.Verb, 3).Word("call", Upos.Verb, 3).Word("call", Upos.Noun, 1)
                .Word("use", Upos.Verb, 3).Word("use", Upos.Noun, 1).Word("is", Upos.Aux, 5).Word("it", Upos.Pron, 5)
                .Build(temperature),
            TinyLexicon,
            new TaggerOptions { MaxAlternatives = alternatives });

    /// <summary>
    ///     Splits a text at spaces into pre-tokenized input.
    /// </summary>
    private static string[] Words(string text) => text.Split(' ');

    /// <summary>
    ///     Formats the tag, confidence and alternatives of every token for exact comparisons.
    /// </summary>
    private static string[] Describe(TaggedSentence sentence) =>
        sentence.Tokens
            .Select(t => $"{t.Text}/{t.Tag}/{t.Confidence:R}/" + string.Join(",", t.Alternatives.Select(a => $"{a.Tag}:{a.Probability:R}")))
            .ToArray();

    /// <summary>
    ///     Proves that a hard noun term gives its head word the entry tag with the confidence exactly 1.0 and no alternatives, and its other words a noun.
    /// </summary>
    [Fact]
    public void Tagger_TagWithGlossary_HardNounTerm_HeadIsNounWithConfidenceOne()
    {
        // Arrange: a tagger that reads "bleed" as a verb, and a glossary with the compound
        var tagger = CreateTagger();
        var glossary = Glossary.Create(new[] { new GlossaryEntry("bleed valve", Upos.Noun) });
        var tokens = Words("open the bleed valve .");

        // Act: tag with and without the glossary
        var baseline = tagger.Tag(tokens);
        var tagged = tagger.Tag(tokens, glossary);

        // Assert: the model alone makes the costly error; with the glossary both words are nouns, the head with certainty
        Assert.Equal(Upos.Verb, baseline.Tokens[2].Tag);
        Assert.Equal(Upos.Noun, tagged.Tokens[2].Tag);
        Assert.Equal(Upos.Noun, tagged.Tokens[3].Tag);
        Assert.Equal(1.0, tagged.Tokens[3].Confidence);
        Assert.Empty(tagged.Tokens[3].Alternatives);
        Assert.Equal(new[] { false, false, true, true, false }, tagged.Tokens.Select(t => t.IsConstrained));
    }

    /// <summary>
    ///     Proves that the other words of a noun term can no longer be an auxiliary even when the model prefers it.
    /// </summary>
    [Fact]
    public void Tagger_TagWithGlossary_NonHeadOfNounTerm_IsNeverVerbOrAuxiliary()
    {
        // Arrange: "can" is an auxiliary for the model; the term is "can opener"
        var tagger = CreateTagger();
        var glossary = Glossary.Create(new[] { new GlossaryEntry("can opener", Upos.Noun) });
        var tokens = Words("use the can opener .");

        // Act: tag with and without the glossary
        var baseline = tagger.Tag(tokens);
        var tagged = tagger.Tag(tokens, glossary);

        // Assert: the model alone says AUX, the glossary forbids it
        Assert.Equal(Upos.Aux, baseline.Tokens[2].Tag);
        Assert.Equal(Upos.Noun, tagged.Tokens[2].Tag);
        Assert.Equal(Upos.Noun, tagged.Tokens[3].Tag);
    }

    /// <summary>
    ///     Proves that the second word of a two-token verb term with the head first is limited to particles (the rule for non-nominal terms), and the head word is forced to the entry tag.
    /// </summary>
    [Fact]
    public void Tagger_TagWithGlossary_VerbTermWithHeadFirst_NonHeadIsLimitedToParticles()
    {
        // Arrange: "shut" is an adjective and "down" an adposition for the model; the entry says VERB with the head on "shut"
        var tagger = CreateTagger();
        var glossary = Glossary.Create(new[] { new GlossaryEntry("shut down", Upos.Verb) });
        var tokens = Words("shut down .");

        // Act: tag with and without the glossary
        var baseline = tagger.Tag(tokens);
        var tagged = tagger.Tag(tokens, glossary);

        // Assert: the head is a verb with certainty; "down" keeps the model's particle tag but is now constrained to the particle tags that the lexicon allows
        Assert.Equal(Upos.Adj, baseline.Tokens[0].Tag);
        Assert.Equal(Upos.Verb, tagged.Tokens[0].Tag);
        Assert.Equal(1.0, tagged.Tokens[0].Confidence);
        Assert.True(tagged.Tokens[0].IsGlossaryHead);
        Assert.Equal(Upos.Adp, tagged.Tokens[1].Tag);
        Assert.True(tagged.Tokens[1].IsConstrained);
        Assert.Equal(Upos.Adv, Assert.Single(tagged.Tokens[1].Alternatives).Tag);
        Assert.False(tagged.Tokens[1].IsGlossaryHead);
        Assert.NotNull(tagged.Tokens[1].GlossaryMatch);
    }

    /// <summary>
    ///     Proves that the inferred phrasal verbs <c>shut down</c> and <c>carry out</c> need no WithHead: the head word is the first token, a verb with certainty, also when inflected (<c>shuts down</c>, <c>shutting down</c>), and the particle is limited to particle tags; an explicit last head keeps the last token, and a verb term with a noun as last word (<c>take care</c>) keeps the last token without WithHead.
    /// </summary>
    [Fact]
    public void Tagger_TagWithGlossary_InferredPhrasalHead_VerbHeadAndParticleLimited()
    {
        // Arrange: the entries have no WithHead; "shut" and "down" are in the tiny lexicon as ADJ/VERB and ADV/ADP/NOUN, "carry" and "out" are unknown there
        var tagger = CreateTagger();
        var particles = UposSet.Of(Upos.Adp, Upos.Adv, Upos.Part);
        var shutDown = Glossary.Create(new[] { new GlossaryEntry("shut down", Upos.Verb) });

        // Act: tag the base form, two inflected forms, a second phrasal verb, an explicit last head and a verb term with a noun
        var inferred = tagger.Tag(Words("shut down ."), shutDown);
        var shuts = tagger.Tag(Words("it shuts down ."), shutDown);
        var shutting = tagger.Tag(Words("it is shutting down ."), shutDown);
        var carryOut = tagger.Tag(Words("carry out ."), Glossary.Create(new[] { new GlossaryEntry("carry out", Upos.Verb) }));
        var explicitLast = tagger.Tag(Words("shut down ."), Glossary.Create(new[] { new GlossaryEntry("shut down", Upos.Verb).WithHead(1) }));
        var takeCare = tagger.Tag(Words("take care ."), Glossary.Create(new[] { new GlossaryEntry("take care", Upos.Verb) }));

        // Assert: the first token is the head, a verb with confidence exactly 1.0; the second token is limited to particle tags and is not the head
        Assert.Equal(0, Assert.Single(inferred.GlossaryMatches).HeadToken);
        Assert.Equal(Upos.Verb, inferred.Tokens[0].Tag);
        Assert.Equal(1.0, inferred.Tokens[0].Confidence);
        Assert.Empty(inferred.Tokens[0].Alternatives);
        Assert.True(inferred.Tokens[0].IsGlossaryHead);
        Assert.True(inferred.Tokens[1].IsConstrained);
        Assert.False(inferred.Tokens[1].IsGlossaryHead);
        Assert.True(particles.Contains(inferred.Tokens[1].Tag));
        Assert.All(inferred.Tokens[1].Alternatives, alternative => Assert.True(particles.Contains(alternative.Tag)));

        // Assert: the inflected forms have the first token as head ("shuts" is a verb with certainty, "shutting" a participle that may also be an adjective or noun), and "down" stays limited
        Assert.Equal(1, Assert.Single(shuts.GlossaryMatches).HeadToken);
        Assert.Equal(Upos.Verb, shuts.Tokens[1].Tag);
        Assert.Equal(1.0, shuts.Tokens[1].Confidence);
        Assert.True(shuts.Tokens[1].IsGlossaryHead);
        Assert.True(particles.Contains(shuts.Tokens[2].Tag));
        Assert.Equal(2, Assert.Single(shutting.GlossaryMatches).HeadToken);
        Assert.True(UposSet.Of(Upos.Verb, Upos.Adj, Upos.Noun).Contains(shutting.Tokens[2].Tag));
        Assert.True(shutting.Tokens[2].IsGlossaryHead);
        Assert.True(particles.Contains(shutting.Tokens[3].Tag));

        // Assert: "carry out" has the head first and its particle limited even though the tiny lexicon does not know the words
        Assert.Equal(0, Assert.Single(carryOut.GlossaryMatches).HeadToken);
        Assert.Equal(Upos.Verb, carryOut.Tokens[0].Tag);
        Assert.True(particles.Contains(carryOut.Tokens[1].Tag));

        // Assert: the explicit last head makes the second token the verb and leaves the first one to the model
        Assert.Equal(1, Assert.Single(explicitLast.GlossaryMatches).HeadToken);
        Assert.Equal(Upos.Verb, explicitLast.Tokens[1].Tag);
        Assert.True(explicitLast.Tokens[1].IsGlossaryHead);
        Assert.False(explicitLast.Tokens[0].IsConstrained);

        // Assert: "care" is not a particle word, so the last token is the head
        Assert.Equal(1, Assert.Single(takeCare.GlossaryMatches).HeadToken);
        Assert.Equal(Upos.Verb, takeCare.Tokens[1].Tag);
    }

    /// <summary>
    ///     Proves that the middle and last tokens of a three-token verb term whose head is the first token are not constrained, so a pronoun stays a pronoun.
    /// </summary>
    [Fact]
    public void Tagger_TagWithGlossary_ThreeTokenVerbTerm_NonHeadTokensAreLeftToTheModel()
    {
        // Arrange: "it" is a pronoun and "down" an adposition for the model; the entry says VERB with the head on "shut"
        var tagger = CreateTagger();
        var glossary = Glossary.Create(new[] { new GlossaryEntry("shut it down", Upos.Verb).WithHead(0) });

        // Act: tag
        var tagged = tagger.Tag(Words("shut it down ."), glossary);

        // Assert: only the head is constrained; the other two words keep the model's tags and report no constraint
        Assert.Equal(Upos.Verb, tagged.Tokens[0].Tag);
        Assert.True(tagged.Tokens[0].IsConstrained);
        Assert.Equal(Upos.Pron, tagged.Tokens[1].Tag);
        Assert.False(tagged.Tokens[1].IsConstrained);
        Assert.Equal(Upos.Adp, tagged.Tokens[2].Tag);
        Assert.False(tagged.Tokens[2].IsConstrained);
        Assert.NotNull(tagged.Tokens[1].GlossaryMatch);
        Assert.NotNull(tagged.Tokens[2].GlossaryMatch);
    }

    /// <summary>
    ///     Proves that explicit tags for a non-head token override the rules, in hard mode.
    /// </summary>
    [Fact]
    public void Tagger_TagWithGlossary_WithTokenTags_ForcesTheTokenTags()
    {
        // Arrange: the model reads "down" as an adposition; the entry says it is an adverb
        var tagger = CreateTagger();
        var glossary = Glossary.Create(new[] { new GlossaryEntry("shut down", Upos.Verb).WithHead(0).WithTokenTags(1, UposSet.Of(Upos.Adv)) });

        // Act: tag
        var tagged = tagger.Tag(Words("shut down ."), glossary);

        // Assert: ADV with certainty
        Assert.Equal(Upos.Adv, tagged.Tokens[1].Tag);
        Assert.Equal(1.0, tagged.Tokens[1].Confidence);
        Assert.True(tagged.Tokens[1].IsConstrained);
    }

    /// <summary>
    ///     Proves that an entry with several tags leaves the choice among them to the model with a softmax over the allowed tags.
    /// </summary>
    [Fact]
    public void Tagger_TagWithGlossary_HardHeadWithSeveralTags_SoftmaxOverRemainingTags()
    {
        // Arrange: "bleed" may be NOUN or VERB; the model prefers VERB by 4 at temperature 1
        var tagger = CreateTagger();
        var glossary = Glossary.Create(new[] { new GlossaryEntry("bleed", UposSet.Of(Upos.Noun, Upos.Verb)) });

        // Act: tag
        var token = tagger.Tag(Words("they bleed ."), glossary).Tokens[1];

        // Assert: the model's choice with the softmax probability, and the other tag as the one alternative; they add up to 1
        var expected = 1.0 / (1.0 + Math.Exp(-4.0));
        Assert.Equal(Upos.Verb, token.Tag);
        Assert.Equal(expected, token.Confidence, 1e-9);
        var alternative = Assert.Single(token.Alternatives);
        Assert.Equal(Upos.Noun, alternative.Tag);
        Assert.Equal(1.0, token.Confidence + alternative.Probability, 1e-12);
        Assert.True(token.IsConstrained);
    }

    /// <summary>
    ///     Proves that the glossary wins when its tags and the tags of the lexicon have nothing in common.
    /// </summary>
    [Fact]
    public void Tagger_TagWithGlossary_EntryDisjointFromLexicon_GlossaryWins()
    {
        // Arrange: the lexicon lists "valve" as a noun only; the glossary says adjective
        var tagger = CreateTagger();
        var glossary = Glossary.Create(new[] { new GlossaryEntry("valve", Upos.Adj) });

        // Act: tag
        var token = tagger.Tag(Words("the valve ."), glossary).Tokens[1];

        // Assert: the glossary is authoritative over the dictionary
        Assert.Equal(Upos.Adj, token.Tag);
        Assert.Equal(1.0, token.Confidence);
    }

    /// <summary>
    ///     Proves that a word that the lexicon does not know takes the tag of its entry.
    /// </summary>
    [Fact]
    public void Tagger_TagWithGlossary_UnknownWord_TakesEntryTag()
    {
        // Arrange: "LRU" is not in the lexicon, so any tag is possible without the glossary
        var tagger = CreateTagger();
        var glossary = Glossary.Create(new[] { new GlossaryEntry("LRU", Upos.Noun).WithCaseSensitive(true) });

        // Act: tag
        var tagged = tagger.Tag(Words("open the LRU ."), glossary);

        // Assert: NOUN with certainty
        Assert.Equal(Upos.Noun, tagged.Tokens[2].Tag);
        Assert.Equal(1.0, tagged.Tokens[2].Confidence);
        Assert.True(tagged.Tokens[2].IsGlossaryHead);
    }

    /// <summary>
    ///     Proves that soft mode flips a close decision but not a decisive one.
    /// </summary>
    [Fact]
    public void Tagger_TagWithGlossary_SoftMode_FlipsCloseDecisionButNotDecisiveOne()
    {
        // Arrange: temperature 2 and strength 1.5 give a boost of 3 raw score units; "torque" prefers VERB by 2, "bleed" by 4
        var tagger = CreateTagger(2f);
        var glossary = Glossary.Create(
            new[] { new GlossaryEntry("torque wrench", Upos.Noun), new GlossaryEntry("bleed valve", Upos.Noun) },
            new GlossaryOptions { Mode = GlossaryMode.Soft, SoftStrength = 1.5 });

        // Act: tag one sentence with each term
        var close = tagger.Tag(Words("use a torque wrench ."), glossary);
        var decisive = tagger.Tag(Words("need a bleed valve ."), glossary);

        // Assert: the close decision flips to NOUN, the decisive one stays VERB; the head of each term is a noun
        Assert.Equal(Upos.Noun, close.Tokens[2].Tag);
        Assert.Equal(Upos.Verb, decisive.Tokens[2].Tag);
        Assert.Equal(Upos.Noun, close.Tokens[3].Tag);
        Assert.Equal(Upos.Noun, decisive.Tokens[3].Tag);
    }

    /// <summary>
    ///     Proves that a soft entry gives exactly the result of hand-built constraints whose boost is strength times temperature.
    /// </summary>
    [Fact]
    public void Tagger_TagWithGlossary_SoftMode_AdjustmentEqualsStrengthTimesTemperature()
    {
        // Arrange: temperature 2 and strength 1.5, so the boost is 3.0 raw units
        var tagger = CreateTagger(2f);
        var options = new GlossaryOptions { Mode = GlossaryMode.Soft, SoftStrength = 1.5 };
        var single = Glossary.Create(new[] { new GlossaryEntry("torque", Upos.Noun) }, options);
        var compound = Glossary.Create(new[] { new GlossaryEntry("bleed valve", Upos.Noun) }, options);

        // Act: tag with the glossaries, and with the same constraints built by hand
        var viaSingle = tagger.Tag(Words("use a torque ."), single);
        var byHandSingle = tagger.Tag(
            Words("use a torque ."),
            new[] { TokenConstraint.None, TokenConstraint.None, TokenConstraint.Prefer(Upos.Noun, 3.0), TokenConstraint.None });
        var viaCompound = tagger.Tag(Words("need a bleed valve ."), compound);
        var byHandCompound = tagger.Tag(
            Words("need a bleed valve ."),
            new[]
            {
                TokenConstraint.None,
                TokenConstraint.None,
                TokenConstraint.Penalize(Upos.Verb, 3.0).And(TokenConstraint.Penalize(Upos.Aux, 3.0)),
                TokenConstraint.Prefer(Upos.Noun, 3.0),
                TokenConstraint.None,
            });

        // Assert: identical tags, confidences and alternatives, down to the last digit
        Assert.Equal(Describe(byHandSingle), Describe(viaSingle));
        Assert.Equal(Describe(byHandCompound), Describe(viaCompound));
    }

    /// <summary>
    ///     Proves that the soft boost raises the confidence of the preferred tag.
    /// </summary>
    [Fact]
    public void Tagger_TagWithGlossary_SoftMode_RaisesConfidenceOfPreferredTag()
    {
        // Arrange: a head word that may be NOUN or VERB for the lexicon, and a soft entry for NOUN
        var tagger = CreateTagger();
        var glossary = Glossary.Create(new[] { new GlossaryEntry("torque", Upos.Noun) }, new GlossaryOptions { Mode = GlossaryMode.Soft, SoftStrength = 3.0 });

        // Act: tag with and without
        var baseline = tagger.Tag(Words("use a torque ."), Glossary.Empty).Tokens[2];
        var boosted = tagger.Tag(Words("use a torque ."), glossary).Tokens[2];

        // Assert: the model prefers VERB by 2; with a boost of 3 the noun wins, and its probability is the softmax of the adjusted scores
        Assert.Equal(Upos.Verb, baseline.Tag);
        Assert.Equal(Upos.Noun, boosted.Tag);
        Assert.Equal(1.0 / (1.0 + Math.Exp(-(1.0 + 3.0 - 3.0))), boosted.Confidence, 1e-9);
    }

    /// <summary>
    ///     Proves that the mode of an entry overrides the mode of the glossary, in both directions.
    /// </summary>
    [Fact]
    public void Tagger_TagWithGlossary_PerEntryMode_OverridesGlossaryMode()
    {
        // Arrange: "torque" prefers VERB by 2; a weak soft boost of 0.5 cannot flip it, the hard constraint always does
        var tagger = CreateTagger();
        var weak = new GlossaryOptions { SoftStrength = 0.5 };
        var hardEntryInSoftGlossary = Glossary.Create(
            new[] { new GlossaryEntry("torque", Upos.Noun).WithMode(GlossaryMode.Hard) },
            new GlossaryOptions { Mode = GlossaryMode.Soft, SoftStrength = 0.5 });
        var softEntryInHardGlossary = Glossary.Create(new[] { new GlossaryEntry("torque", Upos.Noun).WithMode(GlossaryMode.Soft) }, weak);

        // Act: tag with both
        var hard = tagger.Tag(Words("use a torque ."), hardEntryInSoftGlossary);
        var soft = tagger.Tag(Words("use a torque ."), softEntryInHardGlossary);

        // Assert: the entry mode decides, and the match reports it
        Assert.Equal(Upos.Noun, hard.Tokens[2].Tag);
        Assert.Equal(GlossaryMode.Hard, hard.GlossaryMatches[0].Mode);
        Assert.Equal(Upos.Verb, soft.Tokens[2].Tag);
        Assert.Equal(GlossaryMode.Soft, soft.GlossaryMatches[0].Mode);
    }

    /// <summary>
    ///     Proves that the three Tag overloads with a glossary agree.
    /// </summary>
    [Fact]
    public void Tagger_TagWithGlossary_ThreeOverloads_Agree()
    {
        // Arrange: a glossary and a one-sentence text
        var tagger = CreateTagger();
        var glossary = Glossary.Create(new[] { new GlossaryEntry("bleed valve", Upos.Noun), new GlossaryEntry("torque wrench", Upos.Noun) });
        const string text = "open the bleed valve and use a torque wrench .";
        var sentence = Tokenizer.Tokenize(text)[0];

        // Act: tag through the text, the sentence and the token list
        var viaText = tagger.Tag(text, glossary).Single();
        var viaSentence = tagger.Tag(sentence, glossary);
        var viaTokens = tagger.Tag(sentence.Tokens.Select(t => t.Text).ToArray(), glossary);

        // Assert: the same tags, confidences and matches
        Assert.Equal(Describe(viaSentence), Describe(viaText));
        Assert.Equal(Describe(viaSentence), Describe(viaTokens));
        Assert.Equal(viaSentence.GlossaryMatches.Select(m => m.Text), viaText.GlossaryMatches.Select(m => m.Text));
        Assert.Equal(viaSentence.GlossaryMatches.Select(m => m.Text), viaTokens.GlossaryMatches.Select(m => m.Text));
        Assert.Equal(2, viaText.GlossaryMatches.Count);
    }

    /// <summary>
    ///     Proves that the empty glossary tags exactly like no glossary and reports no match.
    /// </summary>
    [Fact]
    public void Glossary_Empty_TagsLikeNoGlossary()
    {
        // Arrange: a tagger and a text
        var tagger = CreateTagger();
        const string text = "open the bleed valve . use a torque wrench .";

        // Act: tag without and with the empty glossary
        var without = tagger.Tag(text);
        var withEmpty = tagger.Tag(text, Glossary.Empty);

        // Assert: identical results without matches
        Assert.Equal(without.SelectMany(Describe), withEmpty.SelectMany(Describe));
        Assert.All(withEmpty, s => Assert.Empty(s.GlossaryMatches));
        Assert.All(withEmpty.SelectMany(s => s.Tokens), t => Assert.Null(t.GlossaryMatch));
    }

    /// <summary>
    ///     Proves that a call such as Tag(sentence, null) still compiles and chooses the overload with constraints.
    /// </summary>
    [Fact]
    public void Tagger_Tag_NullSecondArgument_StillBindsToConstraintsOverload()
    {
        // Arrange: a tagger, a sentence and tokens
        var tagger = CreateTagger();
        var sentence = Tokenizer.Tokenize("open the valve.")[0];
        var tokens = Words("open the valve .");

        // Act: the null literal must pick the constraints overload (the glossary overload would throw ArgumentNullException)
        var viaSentence = tagger.Tag(sentence, null);
        var viaTokens = tagger.Tag(tokens, null);

        // Assert: tagged without glossary and without exception
        Assert.Equal(4, viaSentence.Tokens.Count);
        Assert.Equal(4, viaTokens.Tokens.Count);
        Assert.Empty(viaSentence.GlossaryMatches);
    }

    /// <summary>
    ///     Proves that a null glossary, text, sentence or token list is rejected by the glossary overloads.
    /// </summary>
    [Fact]
    public void Tagger_TagWithGlossary_NullArguments_ThrowArgumentNullException()
    {
        // Arrange: a tagger and a sentence
        var tagger = CreateTagger();
        var sentence = Tokenizer.Tokenize("open the valve.")[0];

        // Act / Assert: every null argument of every overload
        Assert.Throws<ArgumentNullException>(() => tagger.Tag("open", (Glossary)null!));
        Assert.Throws<ArgumentNullException>(() => tagger.Tag((string)null!, Glossary.Empty));
        Assert.Throws<ArgumentNullException>(() => tagger.Tag(sentence, (Glossary)null!));
        Assert.Throws<ArgumentNullException>(() => tagger.Tag((Sentence)null!, Glossary.Empty));
        Assert.Throws<ArgumentNullException>(() => tagger.Tag(Words("open"), (Glossary)null!));
        Assert.Throws<ArgumentNullException>(() => tagger.Tag((IReadOnlyList<string>)null!, Glossary.Empty));
    }

    /// <summary>
    ///     Proves that the tokens before the first match are tagged exactly as without the glossary.
    /// </summary>
    [Fact]
    public void Tagger_TagWithGlossary_TokensBeforeFirstMatch_AreUnchanged()
    {
        // Arrange: a sentence whose match starts at token 3
        var tagger = CreateTagger();
        var glossary = Glossary.Create(new[] { new GlossaryEntry("bleed valve", Upos.Noun) });
        var tokens = Words("need to call the bleed valve now .");

        // Act: tag with and without
        var baseline = Describe(tagger.Tag(tokens));
        var tagged = tagger.Tag(tokens, glossary);

        // Assert: the tokens before the match are identical, down to confidence and alternatives
        Assert.Equal(baseline.Take(4), Describe(tagged).Take(4));
        Assert.Equal(4, tagged.GlossaryMatches[0].FirstToken);
    }

    /// <summary>
    ///     Proves that the constraints of the caller are combined with those of the glossary: nothing from the caller, an overlap, and a disjoint contradiction.
    /// </summary>
    [Fact]
    public void Tagger_TagWithGlossary_CallerConstraints_AreMergedAndCallerWinsWhenDisjoint()
    {
        // Arrange: the compound "bleed valve" (hard) and constraints of the caller for the same tokens
        var tagger = CreateTagger();
        var glossary = Glossary.Create(new[] { new GlossaryEntry("bleed valve", Upos.Noun) });
        var tokens = Words("open the bleed valve .");
        var none = new TokenConstraint[5];

        // The caller limits "bleed" to NOUN and VERB: the intersection with the glossary's rule is NOUN
        var overlapping = new[] { TokenConstraint.None, TokenConstraint.None, TokenConstraint.Only(Upos.Noun, Upos.Verb), TokenConstraint.None, TokenConstraint.None };

        // The caller demands VERB on "bleed" and ADJ on "valve": both contradict the glossary
        var disjoint = new[] { TokenConstraint.None, TokenConstraint.None, TokenConstraint.Only(Upos.Verb), TokenConstraint.Only(Upos.Adj), TokenConstraint.None };

        // The caller adds a constraint to a token outside the match
        var outside = new[] { TokenConstraint.Only(Upos.Adj), TokenConstraint.None, TokenConstraint.None, TokenConstraint.None, TokenConstraint.None };

        // Act: tag with each
        var withNone = tagger.Tag(tokens, glossary, none);
        var withOverlap = tagger.Tag(tokens, glossary, overlapping);
        var withDisjoint = tagger.Tag(tokens, glossary, disjoint);
        var withOutside = tagger.Tag(tokens, glossary, outside);

        // Assert: None changes nothing, an overlap is combined, a contradiction is decided by the caller, an outside token is constrained too
        Assert.Equal(Describe(tagger.Tag(tokens, glossary)), Describe(withNone));
        Assert.Equal(new[] { Upos.Noun, Upos.Noun }, withOverlap.Tokens.Skip(2).Take(2).Select(t => t.Tag));
        Assert.Equal(new[] { Upos.Verb, Upos.Adj }, withDisjoint.Tokens.Skip(2).Take(2).Select(t => t.Tag));
        Assert.Equal(Upos.Adj, withOutside.Tokens[0].Tag);
        Assert.Equal(new[] { Upos.Noun, Upos.Noun }, withOutside.Tokens.Skip(2).Take(2).Select(t => t.Tag));
    }

    /// <summary>
    ///     Proves that a soft constraint of the caller adds to the soft constraint of the glossary.
    /// </summary>
    [Fact]
    public void Tagger_TagWithGlossary_CallerSoftConstraint_AddsToGlossaryAdjustment()
    {
        // Arrange: a soft glossary for "torque" (boost 3 at temperature 1) and a caller who adds a penalty of 3 to NOUN
        var tagger = CreateTagger();
        var glossary = Glossary.Create(new[] { new GlossaryEntry("torque", Upos.Noun) }, new GlossaryOptions { Mode = GlossaryMode.Soft, SoftStrength = 3.0 });
        var tokens = Words("use a torque .");
        var caller = new[] { TokenConstraint.None, TokenConstraint.None, TokenConstraint.Penalize(Upos.Noun, 3.0), TokenConstraint.None };

        // Act: tag with the glossary alone and with the caller's constraint too
        var alone = tagger.Tag(tokens, glossary).Tokens[2];
        var combined = tagger.Tag(tokens, glossary, caller).Tokens[2];

        // Assert: the boost wins alone, and is canceled by the caller's penalty
        Assert.Equal(Upos.Noun, alone.Tag);
        Assert.Equal(Upos.Verb, combined.Tag);
    }

    /// <summary>
    ///     Proves that a list of constraints of the wrong length is rejected, with and without a match.
    /// </summary>
    [Fact]
    public void Tagger_TagWithGlossary_ConstraintListOfWrongLength_ThrowsArgumentException()
    {
        // Arrange: a sentence with a match and one without
        var tagger = CreateTagger();
        var glossary = Glossary.Create(new[] { new GlossaryEntry("bleed valve", Upos.Noun) });
        var wrong = new TokenConstraint[2];

        // Act / Assert: both are rejected, for an array and for a list
        Assert.Throws<ArgumentException>(() => tagger.Tag(Words("open the bleed valve ."), glossary, wrong));
        Assert.Throws<ArgumentException>(() => tagger.Tag(Words("open the bleed valve ."), glossary, wrong.ToList()));
        Assert.Throws<ArgumentException>(() => tagger.Tag(Words("open the valve ."), glossary, wrong));
    }

    /// <summary>
    ///     Proves that tokens and sentences report their match, the head word and their constraint state, and that matches are listed in text order.
    /// </summary>
    [Fact]
    public void Tagger_TagWithGlossary_Results_ReportMatchesOnTokensAndSentences()
    {
        // Arrange: two sentences, the second with a plural form; one entry has a definition
        var tagger = CreateTagger();
        var glossary = Glossary.Create(new[] { new GlossaryEntry("bleed valve", Upos.Noun, "Releases air."), new GlossaryEntry("torque wrench", Upos.Noun) });
        const string text = "open the bleed valve . use a torque wrench and the bleed valves .";

        // Act: tag the text
        var sentences = tagger.Tag(text, glossary);

        // Assert: one sentence with all matches in text order, with the details of each
        var sentence = Assert.Single(sentences);
        var matches = sentence.GlossaryMatches;
        Assert.Equal(new[] { "bleed valve", "torque wrench", "bleed valves" }, matches.Select(m => m.Text));
        Assert.Equal(new[] { 2, 7, 11 }, matches.Select(m => m.FirstToken));
        Assert.Equal(new[] { 3, 8, 12 }, matches.Select(m => m.HeadToken));
        Assert.Equal(new[] { false, false, true }, matches.Select(m => m.IsVariant));
        Assert.Equal("Releases air.", matches[2].Entry.Definition);
        Assert.All(matches, m => Assert.Equal(text.Substring(m.Start, m.Length), m.Text));

        // Every token of a match reports that match; only the head word is the head; the other tokens report none
        for (var i = 0; i < sentence.Tokens.Count; i++)
        {
            var token = sentence.Tokens[i];
            var owner = matches.SingleOrDefault(m => i >= m.FirstToken && i < m.FirstToken + m.TokenCount);
            Assert.Same(owner, token.GlossaryMatch);
            Assert.Equal(owner != null && owner.HeadToken == i, token.IsGlossaryHead);
            Assert.Equal(owner != null, token.IsConstrained);
        }
    }

    /// <summary>
    ///     Proves that the match list of a tagged sentence cannot be modified by the caller.
    /// </summary>
    [Fact]
    public void Tagger_TagWithGlossary_MatchList_IsReadOnly()
    {
        // Arrange: a tagged sentence with a match
        var tagger = CreateTagger();
        var glossary = Glossary.Create(new[] { new GlossaryEntry("bleed valve", Upos.Noun) });
        var sentence = tagger.Tag(Words("open the bleed valve ."), glossary);

        // Act: try to reach a writable collection behind the view
        var asList = sentence.GlossaryMatches as IList<GlossaryMatch>;

        // Assert: it is no array and, as a list, read-only; the same for the empty case
        Assert.IsNotType<GlossaryMatch[]>(sentence.GlossaryMatches);
        Assert.True(asList == null || asList.IsReadOnly);
        Assert.Empty(tagger.Tag(Words("open the valve ."), glossary).GlossaryMatches);
    }

    /// <summary>
    ///     Proves that many threads can tag with one shared glossary and the shared default tagger and get what a single thread gets.
    /// </summary>
    [Fact]
    public void Glossary_Tag_ParallelUse_MatchesSequentialResults()
    {
        // Arrange: a hard and a soft glossary and sentences that differ in their matches
        var hard = new GlossaryBuilder { { "bleed valve", Upos.Noun }, { "torque wrench", Upos.Noun }, { "isolate", Upos.Verb } }.Build();
        var soft = new GlossaryBuilder(new GlossaryOptions { Mode = GlossaryMode.Soft }) { { "bleed valve", Upos.Noun }, { "torque wrench", Upos.Noun } }.Build();
        var texts = Enumerable.Range(0, 200)
            .Select(i => $"Step {i}: isolate the bleed valve {i % 7} and use a torque wrench, then close the bleed valves.")
            .ToArray();
        string[] Run(string text, Glossary glossary) =>
            Tagger.Default.Tag(text, glossary).SelectMany(s => Describe(s).Concat(s.GlossaryMatches.Select(m => $"{m.FirstToken}:{m.Text}"))).ToArray();
        var expected = texts.Select(t => string.Join(";", Run(t, hard).Concat(Run(t, soft)))).ToArray();
        var actual = new string[texts.Length];

        // Act: tag from eight threads, several rounds each
        Parallel.For(
            0,
            texts.Length * 3,
            new ParallelOptions { MaxDegreeOfParallelism = 8 },
            n => actual[n % texts.Length] = string.Join(";", Run(texts[n % texts.Length], hard).Concat(Run(texts[n % texts.Length], soft))));

        // Assert: the same as sequentially
        Assert.Equal(expected, actual);
    }

    /// <summary>
    ///     Proves that threads that use a soft glossary for the first time together compile its constraint tables correctly.
    /// </summary>
    [Fact]
    public void Glossary_Tag_SoftFirstUseRace_CompilesCorrectly()
    {
        // Arrange: a fresh tagger and glossary for each round, and the expected result from a sequential run
        const int threads = 8;
        var words = Words("use a torque wrench and need a bleed valve .");
        for (var round = 0; round < 20; round++)
        {
            var tagger = CreateTagger(2f);
            var options = new GlossaryOptions { Mode = GlossaryMode.Soft, SoftStrength = 1.5 };
            var entries = new[] { new GlossaryEntry("torque wrench", Upos.Noun), new GlossaryEntry("bleed valve", Upos.Noun) };
            var expected = Describe(CreateTagger(2f).Tag(words, Glossary.Create(entries, options)));
            var glossary = Glossary.Create(entries, options);
            var results = new string[threads][];

            // Act: eight threads start together on the first use of the glossary
            using var barrier = new Barrier(threads);
            var workers = Enumerable.Range(0, threads).Select(n => new Thread(() =>
            {
                barrier.SignalAndWait();
                results[n] = Describe(tagger.Tag(words, glossary));
            })).ToList();
            workers.ForEach(w => w.Start());
            workers.ForEach(w => w.Join());

            // Assert: every thread got the expected result
            Assert.All(results, r => Assert.Equal(expected, r));
        }
    }

#if NET
    /// <summary>
    ///     Builds a sentence of repeated groups of the words <c>open the bleed valve .</c> (or of <c>open the valve .</c> for no match).
    /// </summary>
    private static Sentence Repeated(int groups, bool withMatch) =>
        Tokenizer.CreateSentence(Enumerable.Range(0, groups).SelectMany(_ => Words(withMatch ? "open the bleed valve ." : "open the valve .")).ToArray());

    /// <summary>
    ///     Builds a sentence of the given number of groups of four tokens, of which the first <paramref name="matching"/> groups
    ///     read <c>open the valve .</c> (one match of the term <c>valve</c> each) and the others <c>open the bleed .</c> (none).
    /// </summary>
    private static Sentence Groups(int groups, int matching) =>
        Tokenizer.CreateSentence(Enumerable.Range(0, groups)
            .SelectMany(g => Words(g < matching ? "open the valve ." : "open the bleed ."))
            .ToArray());

    /// <summary>
    ///     The measurement noise of a per-call allocation comparison in bytes: a one-time runtime allocation (observed: 48 bytes)
    ///     can land on one side. Any allocation per token (at least 16 bytes each, about 3,000 bytes for the 200-token sentences
    ///     used here) is far above it.
    /// </summary>
    private const long AllocationNoiseBytes = 64;

    /// <summary>
    ///     Measures the bytes allocated by tagging a sentence, with and without a glossary.
    /// </summary>
    private static (long Plain, long WithGlossary) Measure(Tagger tagger, Glossary glossary, Sentence sentence)
    {
        // Two alternating rounds, each side keeping its minimum: a one-time runtime allocation (for example from a tiered
        // recompilation that finishes while one side is measured) shows in one round only and does not decide the comparison
        var plain = long.MaxValue;
        var withGlossary = long.MaxValue;
        for (var round = 0; round < 2; round++)
        {
            plain = Math.Min(plain, AllocationMeter.MinimumBatchBytes(() => tagger.Tag(sentence), 50) / 50);
            withGlossary = Math.Min(withGlossary, AllocationMeter.MinimumBatchBytes(() => tagger.Tag(sentence, glossary), 50) / 50);
        }

        return (plain, withGlossary);
    }

    /// <summary>
    ///     Proves that tagging a sentence without a match allocates no more with a glossary than without (within measurement noise).
    /// </summary>
    [Fact]
    public void Tagger_TagWithGlossary_SentenceWithoutMatch_AddsNoAllocation()
    {
        // Arrange: a glossary that does not occur in a 200-token sentence
        var tagger = CreateTagger();
        var glossary = Glossary.Create(new[] { new GlossaryEntry("bleed valve", Upos.Noun) });

        // Act: measure with and without the glossary
        var (plain, withGlossary) = Measure(tagger, glossary, Repeated(50, false));

        // Assert: nothing extra (one-sided: the glossary may not add allocation; being lower is measurement noise)
        Assert.True(withGlossary <= plain + AllocationNoiseBytes, $"plain {plain} bytes, with glossary {withGlossary} bytes");
    }

    /// <summary>
    ///     Proves that the allocation of tagging with a glossary grows with the number of matches when the number of tokens is fixed.
    /// </summary>
    [Fact]
    public void Tagger_TagWithGlossary_AllocationPerMatch_IsBoundedForFixedTokenCount()
    {
        // Arrange: sentences of the same 200 tokens with 10 and with 40 matches (so both have the constraint array)
        var tagger = CreateTagger();
        var glossary = Glossary.Create(new[] { new GlossaryEntry("valve", Upos.Noun) });

        // Act: measure the extra bytes of the glossary for both
        var (plain10, with10) = Measure(tagger, glossary, Groups(50, 10));
        var (plain40, with40) = Measure(tagger, glossary, Groups(50, 40));
        var perMatch = ((with40 - plain40) - (with10 - plain10)) / 30;

        // Assert: a match costs its result object, its text and a slot in the list (about 112 bytes measured) well below 170
        Assert.True(with10 > plain10, "a glossary with matches must allocate its results");
        Assert.True(perMatch > 0 && perMatch <= 170, $"{perMatch} bytes per match");
    }

    /// <summary>
    ///     Proves that, with the number of matches fixed, the allocation of tagging with a glossary grows by no more than one
    ///     constraint slot (16 bytes) per token: there is an array per sentence, and no object per token.
    /// </summary>
    [Fact]
    public void Tagger_TagWithGlossary_AllocationPerToken_IsOneConstraintSlot()
    {
        // Arrange: sentences with one match and 40 or 440 tokens
        var tagger = CreateTagger();
        var glossary = Glossary.Create(new[] { new GlossaryEntry("valve", Upos.Noun) });

        // Act: measure the extra bytes of the glossary for both
        var (plainShort, withShort) = Measure(tagger, glossary, Groups(10, 1));
        var (plainLong, withLong) = Measure(tagger, glossary, Groups(110, 1));
        var perToken = ((withLong - plainLong) - (withShort - plainShort)) / 400;

        // Assert: 16 bytes of the constraint array per token (as measured), nothing more (an object per token would add at least 24)
        Assert.True(perToken <= 20, $"{perToken} bytes per token");
    }

    /// <summary>
    ///     Proves that soft mode allocates no more than hard mode once the constraint tables are compiled.
    /// </summary>
    [Fact]
    public void Tagger_TagWithGlossary_SoftMode_AllocatesLikeHardModeAfterFirstUse()
    {
        // Arrange: the same entry in a hard and a soft glossary
        var tagger = CreateTagger();
        var entries = new[] { new GlossaryEntry("bleed valve", Upos.Noun) };
        var hard = Glossary.Create(entries);
        var soft = Glossary.Create(entries, new GlossaryOptions { Mode = GlossaryMode.Soft });
        var sentence = Repeated(20, true);

        // Act: measure both after a thorough warm-up
        var (_, hardBytes) = Measure(tagger, hard, sentence);
        var (_, softBytes) = Measure(tagger, soft, sentence);

        // Assert: soft mode adds no allocation to hard mode (one-sided, within measurement noise)
        Assert.True(softBytes <= hardBytes + AllocationNoiseBytes, $"hard {hardBytes} bytes, soft {softBytes} bytes");
    }
#endif
}
