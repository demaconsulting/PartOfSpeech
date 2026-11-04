// cspell:ignore bleeded buzzes dyeing toeing seed's runned LRUs wishes churches stopped plugged permited Inflectable zzqxv bleeder
namespace DemaConsulting.PartOfSpeech.Tests.Tagging;

/// <summary>
///     Unit tests of <see cref="Glossary"/>, <see cref="GlossaryBuilder"/>, <see cref="GlossaryEntry"/>,
///     <see cref="GlossaryOptions"/> and the inflection rules of <see cref="GlossaryInflector"/>: validation,
///     immutability, duplicates, case handling at creation and the generated forms.
/// </summary>
public class GlossaryTests
{
    /// <summary>
    ///     Proves that a null term is rejected.
    /// </summary>
    [Fact]
    public void GlossaryEntry_Ctor_NullTerm_ThrowsArgumentNullException()
    {
        // Arrange / Act / Assert: null term
        Assert.Throws<ArgumentNullException>(() => new GlossaryEntry(null!, Upos.Noun));
    }

    /// <summary>
    ///     Proves that an empty or white-space term is rejected.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void GlossaryEntry_Ctor_EmptyOrWhiteSpaceTerm_ThrowsArgumentException(string term)
    {
        // Arrange / Act / Assert: no token at all
        var exception = Assert.Throws<ArgumentException>(() => new GlossaryEntry(term, Upos.Noun));
        Assert.Equal("term", exception.ParamName);
    }

    /// <summary>
    ///     Proves that a term whose tokens have no letter or digit is rejected.
    /// </summary>
    [Theory]
    [InlineData("--")]
    [InlineData("...")]
    [InlineData("( )")]
    [InlineData("$ %")]
    public void GlossaryEntry_Ctor_TermWithoutLetterOrDigit_ThrowsArgumentException(string term)
    {
        // Arrange / Act / Assert: only punctuation
        Assert.Throws<ArgumentException>(() => new GlossaryEntry(term, Upos.Noun));
    }

    /// <summary>
    ///     Proves that an empty tag set is rejected.
    /// </summary>
    [Fact]
    public void GlossaryEntry_Ctor_EmptyTags_ThrowsArgumentException()
    {
        // Arrange / Act / Assert: the empty set
        var exception = Assert.Throws<ArgumentException>(() => new GlossaryEntry("valve", UposSet.Empty));
        Assert.Equal("tags", exception.ParamName);
    }

    /// <summary>
    ///     Proves that an undefined tag value is rejected.
    /// </summary>
    [Fact]
    public void GlossaryEntry_Ctor_UndefinedTag_ThrowsArgumentOutOfRangeException()
    {
        // Arrange / Act / Assert: 99 is not a tag
        Assert.Throws<ArgumentOutOfRangeException>(() => new GlossaryEntry("valve", (Upos)99));
    }

    /// <summary>
    ///     Proves that the tokens of a term are the tokens that the tokenizer makes in running text.
    /// </summary>
    [Theory]
    [InlineData("bleed valve", "bleed|valve")]
    [InlineData("bleed   valve", "bleed|valve")]
    [InlineData("well-known", "well-known")]
    [InlineData("bleed-valve", "bleed-valve")]
    [InlineData("U.S.", "U.S.")]
    [InlineData("U.S. Army", "U.S.|Army")]
    [InlineData("don't", "do|n't")]
    [InlineData("3.5", "3.5")]
    [InlineData("R&D", "R&D")]
    [InlineData("and/or", "and/or")]
    [InlineData("etc.", "etc.")]
    [InlineData("Dr. Smith", "Dr.|Smith")]
    public void GlossaryEntry_Tokens_FollowTokenizer(string term, string expected)
    {
        // Arrange / Act: create the entry
        var entry = new GlossaryEntry(term, Upos.Noun);

        // Assert: the tokens equal the expected split, and the term is kept as supplied
        Assert.Equal(expected, string.Join("|", entry.Tokens));
        Assert.Equal(term, entry.Term);
    }

    /// <summary>
    ///     Proves that a term that ends in a period also gets the split that the tokenizer makes at the end of a text.
    /// </summary>
    [Fact]
    public void GlossaryEntry_EndTokens_TermEndingInKeptPeriod_DiffersAtEndOfText()
    {
        // Arrange / Act: a term whose final period is kept in the middle of a text but split at the end
        var abbreviation = new GlossaryEntry("etc.", Upos.X);
        var plain = new GlossaryEntry("valve", Upos.Noun);

        // Assert: the end form is the word and the period; a plain term has none
        Assert.Equal(new[] { "etc", "." }, abbreviation.EndTokens);
        Assert.Null(plain.EndTokens);
    }

    /// <summary>
    ///     Proves that the head of an entry is the last token unless it is changed.
    /// </summary>
    [Theory]
    [InlineData("valve", 0)]
    [InlineData("bleed valve", 1)]
    [InlineData("main bleed valve", 2)]
    public void GlossaryEntry_HeadIndex_DefaultsToLastToken(string term, int expected)
    {
        // Arrange / Act: create the entry
        var entry = new GlossaryEntry(term, Upos.Noun);

        // Assert: the last token
        Assert.Equal(expected, entry.HeadIndex);
    }

    /// <summary>
    ///     Proves that a verb term whose later words are all listed particle words that the lexicon confirms gets the first token as head, whatever the letter case, and with every extra tag that includes VERB.
    /// </summary>
    [Theory]
    [InlineData("shut down", 0)]
    [InlineData("Shut Down", 0)]
    [InlineData("carry out", 0)]
    [InlineData("set up", 0)]
    [InlineData("switch off", 0)]
    [InlineData("go across", 0)]
    [InlineData("walk forth across", 0)]
    public void GlossaryEntry_HeadIndex_PhrasalVerbWithParticleWords_IsFirstToken(string term, int expected)
    {
        // Arrange / Act: verb entries, and a noun-or-verb entry for the same term
        var verb = new GlossaryEntry(term, Upos.Verb);
        var nounOrVerb = new GlossaryEntry(term, UposSet.Of(Upos.Noun, Upos.Verb));

        // Assert: every word after the first is a listed particle word that the lexicon confirms, so the head is the first word
        Assert.Equal(expected, verb.HeadIndex);
        Assert.Equal(expected, nounOrVerb.HeadIndex);
    }

    /// <summary>
    ///     Proves that every word of the documented closed particle list is confirmed by the embedded lexicon, so that each one makes a verb term a phrasal verb.
    /// </summary>
    [Fact]
    public void GlossaryEntry_HeadIndex_EveryListedParticleWord_IsConfirmedByTheLexicon()
    {
        // Arrange: the documented list
        var particles = new[]
        {
            "about", "across", "ahead", "along", "apart", "around", "aside", "away", "back", "by", "down", "forth",
            "forward", "in", "off", "on", "onto", "out", "over", "through", "together", "up", "upon", "within",
        };

        // Act / Assert: a verb term that ends in the word has the first token as head
        foreach (var particle in particles)
        {
            Assert.True(new GlossaryEntry("go " + particle, Upos.Verb).HeadIndex == 0, particle);
        }
    }

    /// <summary>
    ///     Proves that a verb term keeps the last token as head when one later word is not in the closed particle list (such as a noun) or is not in the lexicon, and that an explicit head still moves it.
    /// </summary>
    [Theory]
    [InlineData("take care")]
    [InlineData("make sense")]
    [InlineData("go home")]
    [InlineData("take it apart")]
    [InlineData("shut down valve")]
    [InlineData("turn around valve")]
    [InlineData("go around zzqxv")]
    public void GlossaryEntry_HeadIndex_VerbTermWithAnotherLaterWord_IsLastToken(string term)
    {
        // Arrange / Act: a verb entry whose later words are not all listed particle words
        var entry = new GlossaryEntry(term, Upos.Verb);

        // Assert: the last token; WithHead(0) still moves it
        Assert.Equal(entry.Tokens.Count - 1, entry.HeadIndex);
        Assert.Equal(0, entry.WithHead(0).HeadIndex);
    }

    /// <summary>
    ///     Proves that the inference needs the tag VERB and at least two tokens: noun compounds, terms without a verb tag and single words keep the last token.
    /// </summary>
    [Theory]
    [InlineData("bleed valve", Upos.Noun)]
    [InlineData("torque wrench", Upos.Noun)]
    [InlineData("shut down", Upos.Noun)]
    [InlineData("shut down", Upos.Adj)]
    [InlineData("shut down", Upos.Adv)]
    [InlineData("shut", Upos.Verb)]
    [InlineData("down", Upos.Verb)]
    [InlineData("around", Upos.Noun)]
    public void GlossaryEntry_HeadIndex_NoVerbTagOrSingleToken_IsLastToken(string term, Upos tag)
    {
        // Arrange / Act: create the entry
        var entry = new GlossaryEntry(term, tag);

        // Assert: the last token
        Assert.Equal(entry.Tokens.Count - 1, entry.HeadIndex);
    }

    /// <summary>
    ///     Proves that a noun-or-verb entry follows the same particle rule: the compound <c>bleed valve</c> keeps the last token because <c>valve</c> is not a particle word, while <c>shut down</c> takes the first.
    /// </summary>
    [Fact]
    public void GlossaryEntry_HeadIndex_NounOrVerbTags_FollowTheParticleRule()
    {
        // Arrange / Act: the same tags for a compound and a phrasal verb
        var tags = UposSet.Of(Upos.Noun, Upos.Verb);
        var compound = new GlossaryEntry("bleed valve", tags);
        var phrasal = new GlossaryEntry("shut down", tags);

        // Assert: only the particle word moves the head
        Assert.Equal(1, compound.HeadIndex);
        Assert.Equal(0, phrasal.HeadIndex);
    }

    /// <summary>
    ///     Proves that an explicit head overrides the inferred head in both directions, also when it names the last token, and that later settings keep it.
    /// </summary>
    [Fact]
    public void GlossaryEntry_WithHead_OverridesInferredHeadBothWays()
    {
        // Arrange: an entry whose head is inferred as the first token, and one whose head is the last token
        var phrasal = new GlossaryEntry("shut down", Upos.Verb);
        var compound = new GlossaryEntry("bleed valve", Upos.Noun);

        // Act: override both ways, then apply other settings
        var phrasalLast = phrasal.WithHead(1);
        var phrasalFirst = phrasal.WithHead(0);
        var compoundFirst = compound.WithHead(0);
        var compoundLast = compound.WithHead(1);

        // Assert: the explicit head wins, also WithHead(last) against the inference; other settings keep whichever head the entry has
        Assert.Equal(0, phrasal.HeadIndex);
        Assert.Equal(1, phrasalLast.HeadIndex);
        Assert.Equal(0, phrasalFirst.HeadIndex);
        Assert.Equal(0, compoundFirst.HeadIndex);
        Assert.Equal(1, compoundLast.HeadIndex);
        Assert.Equal(1, phrasalLast.WithMode(GlossaryMode.Soft).WithInflection(false).WithDefinition("d").WithCaseSensitive(true).HeadIndex);
        Assert.Equal(0, phrasal.WithMode(GlossaryMode.Soft).WithVariants("shuts down").WithDefinition("d").HeadIndex);
    }

    /// <summary>
    ///     Proves that the inferred head is the same when entries are created on several threads at once.
    /// </summary>
    [Fact]
    public void GlossaryEntry_HeadIndex_Inference_IsDeterministicAcrossThreads()
    {
        // Arrange: the results of many creations on a few threads
        var heads = new int[256];

        // Act: create entries in parallel; the first use of the embedded lexicon may happen on any of the threads
        Parallel.For(0, heads.Length, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
        {
            heads[i] = new GlossaryEntry(i % 2 == 0 ? "shut down" : "take care", Upos.Verb).HeadIndex;
        });

        // Assert: the verb with the particle word has the first token, the other the last, every time
        for (var i = 0; i < heads.Length; i++)
        {
            Assert.Equal(i % 2 == 0 ? 0 : 1, heads[i]);
        }
    }

    /// <summary>
    ///     Proves that a head outside the tokens is rejected.
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(100)]
    public void GlossaryEntry_WithHead_OutOfRange_ThrowsArgumentOutOfRangeException(int head)
    {
        // Arrange: a two-token entry
        var entry = new GlossaryEntry("take care", Upos.Verb);

        // Act / Assert: the index must be 0 or 1
        Assert.Throws<ArgumentOutOfRangeException>(() => entry.WithHead(head));
    }

    /// <summary>
    ///     Proves that a valid head changes the head only and leaves the original entry untouched.
    /// </summary>
    [Fact]
    public void GlossaryEntry_WithHead_ValidIndex_ReturnsNewEntryAndLeavesOriginal()
    {
        // Arrange: a two-token entry
        var original = new GlossaryEntry("take care", Upos.Verb, "Be careful.");

        // Act: move the head to the first word
        var changed = original.WithHead(0);

        // Assert: the copy has the new head and keeps everything else; the original is unchanged
        Assert.Equal(0, changed.HeadIndex);
        Assert.Equal(1, original.HeadIndex);
        Assert.NotSame(original, changed);
        Assert.Equal(original.Term, changed.Term);
        Assert.Equal(original.Tags, changed.Tags);
        Assert.Equal(original.Definition, changed.Definition);
    }

    /// <summary>
    ///     Proves that every With method returns a new entry and leaves the original unchanged.
    /// </summary>
    [Fact]
    public void GlossaryEntry_With_ReturnsNewEntryAndLeavesOriginal()
    {
        // Arrange: a plain entry
        var original = new GlossaryEntry("bleed valve", Upos.Noun);

        // Act: apply every With method to the original
        var caseSensitive = original.WithCaseSensitive(true);
        var soft = original.WithMode(GlossaryMode.Soft);
        var noInflection = original.WithInflection(false);
        var defined = original.WithDefinition("A valve.");
        var variants = original.WithVariants("bleed valves");
        var tokenTags = original.WithTokenTags(0, UposSet.Of(Upos.Adj));

        // Assert: each copy carries only its own change, and the original has no overrides at all
        Assert.True(caseSensitive.CaseSensitive);
        Assert.Equal(GlossaryMode.Soft, soft.Mode);
        Assert.False(noInflection.Inflect);
        Assert.Equal("A valve.", defined.Definition);
        Assert.Equal(new[] { "bleed valves" }, variants.Variants);
        Assert.Equal(UposSet.Of(Upos.Adj), tokenTags.GetTokenTags(0));
        Assert.Null(original.CaseSensitive);
        Assert.Null(original.Mode);
        Assert.Null(original.Inflect);
        Assert.Null(original.Definition);
        Assert.Empty(original.Variants);
        Assert.Null(original.GetTokenTags(0));
        Assert.Null(caseSensitive.Mode);
        Assert.Null(soft.CaseSensitive);
    }

    /// <summary>
    ///     Proves that chained With calls accumulate and keep earlier settings.
    /// </summary>
    [Fact]
    public void GlossaryEntry_With_Chained_KeepsEarlierSettings()
    {
        // Arrange / Act: chain several changes
        var entry = new GlossaryEntry("shut down", Upos.Verb, "Stop.")
            .WithHead(0)
            .WithCaseSensitive(false)
            .WithMode(GlossaryMode.Soft)
            .WithInflection(false)
            .WithTokenTags(1, UposSet.Of(Upos.Adv))
            .WithVariants("shuts down");

        // Assert: all settings are present
        Assert.Equal(0, entry.HeadIndex);
        Assert.False(entry.CaseSensitive);
        Assert.Equal(GlossaryMode.Soft, entry.Mode);
        Assert.False(entry.Inflect);
        Assert.Equal(UposSet.Of(Upos.Adv), entry.GetTokenTags(1));
        Assert.Equal("Stop.", entry.Definition);
        Assert.Equal(new[] { "shuts down" }, entry.Variants);
    }

    /// <summary>
    ///     Proves that the definition is carried through unchanged and can be removed.
    /// </summary>
    [Fact]
    public void GlossaryEntry_Definition_IsCarriedUnchanged()
    {
        // Arrange: an entry with a definition that has odd spacing
        const string definition = "  A valve,\r\nwith  odd   spacing.  ";
        var entry = new GlossaryEntry("valve", Upos.Noun, definition);

        // Act: remove it again with a copy
        var without = entry.WithDefinition(null);

        // Assert: the text is exactly the supplied one; the copy has none
        Assert.Equal(definition, entry.Definition);
        Assert.Null(without.Definition);
    }

    /// <summary>
    ///     Proves that variants with another number of tokens are rejected.
    /// </summary>
    [Fact]
    public void GlossaryEntry_WithVariants_WrongTokenCount_ThrowsArgumentException()
    {
        // Arrange: a two-token term
        var entry = new GlossaryEntry("bleed valve", Upos.Noun);

        // Act / Assert: one token and three tokens are both wrong
        Assert.Throws<ArgumentException>(() => entry.WithVariants("bleed-valve"));
        Assert.Throws<ArgumentException>(() => entry.WithVariants("bleed valve assembly"));
    }

    /// <summary>
    ///     Proves that null, empty and token-less variants are rejected.
    /// </summary>
    [Fact]
    public void GlossaryEntry_WithVariants_NullEmptyOrWithoutWord_Throws()
    {
        // Arrange: a one-token term
        var entry = new GlossaryEntry("valve", Upos.Noun);

        // Act / Assert: null array, null item, empty item, item without a letter
        Assert.Throws<ArgumentNullException>(() => entry.WithVariants(null!));
        Assert.Throws<ArgumentNullException>(() => entry.WithVariants((string)null!));
        Assert.Throws<ArgumentException>(() => entry.WithVariants(string.Empty));
        Assert.Throws<ArgumentException>(() => entry.WithVariants("--"));
    }

    /// <summary>
    ///     Proves that later variants replace the earlier ones.
    /// </summary>
    [Fact]
    public void GlossaryEntry_WithVariants_Valid_ReplacesEarlierVariants()
    {
        // Arrange: an entry with one variant
        var entry = new GlossaryEntry("bleed", Upos.Verb).WithVariants("bled");

        // Act: replace the variants
        var replaced = entry.WithVariants("bleeds", "bleeding");

        // Assert: only the new variants remain; the empty call removes them all
        Assert.Equal(new[] { "bleeds", "bleeding" }, replaced.Variants);
        Assert.Equal(new[] { "bled" }, entry.Variants);
        Assert.Empty(entry.WithVariants().Variants);
    }

    /// <summary>
    ///     Proves that a variant that spells the term, and a variant that is listed twice, are ignored without an error, so that the variants list holds each extra spelling once in the order given.
    /// </summary>
    [Fact]
    public void GlossaryEntry_WithVariants_VariantEqualToTermOrDuplicate_IsIgnoredWithoutError()
    {
        // Arrange: a one-token verb and a two-token noun
        var bleed = new GlossaryEntry("bleed", Upos.Verb);
        var valve = new GlossaryEntry("bleed valve", Upos.Noun);

        // Act: the term itself, a duplicate, a different letter case and an invalid variant after a redundant one
        var itself = bleed.WithVariants("bleed");
        var duplicate = bleed.WithVariants("bled", "bled", "bleeds", "bled");
        var otherCase = bleed.WithVariants("Bleed");
        var spaced = valve.WithVariants("bleed  valve", "bleeder valve", "bleeder  valve");

        // Assert: nothing is thrown, the redundant spellings are gone, the order of the others is kept
        Assert.Empty(itself.Variants);
        Assert.Equal(new[] { "bled", "bleeds" }, duplicate.Variants);
        Assert.Equal(new[] { "Bleed" }, otherCase.Variants);
        Assert.Equal(new[] { "bleeder valve" }, spaced.Variants);
        Assert.Throws<ArgumentException>(() => valve.WithVariants("bleed valve", "bleed-valve"));
    }

    /// <summary>
    ///     Proves that a redundant variant changes nothing in matching: the term is reported as the term, a duplicate gives one match, a variant equal to a generated form wins and carries the tags of the entry, and an irregular past tense that equals the lemma (<c>set</c>) needs no variant.
    /// </summary>
    [Fact]
    public void Glossary_FindMatches_RedundantVariantsAndLemmaEqualForms_NeedNoVariant()
    {
        // Arrange: entries with redundant variants, a variant that equals a generated form, and set without any variant
        var glossary = Glossary.Create(new[]
        {
            new GlossaryEntry("bleed", UposSet.Of(Upos.Verb, Upos.Noun)).WithVariants("bleed", "bled", "bled", "bleeds"),
            new GlossaryEntry("set", Upos.Verb),
        });

        // Act
        var term = glossary.FindMatches(Tokenizer.Tokenize("They bleed.")[0]);
        var irregular = glossary.FindMatches(Tokenizer.Tokenize("They bled.")[0]);
        var generated = glossary.FindMatches(Tokenizer.Tokenize("It bleeds.")[0]);
        var lemmaForm = glossary.FindMatches(Tokenizer.Tokenize("They set it.")[0]);

        // Assert: the term is not a variant; the listed variant matches once; a variant beats the generated form and has the full tag set of the entry
        Assert.False(Assert.Single(term).IsVariant);
        Assert.True(Assert.Single(irregular).IsVariant);
        Assert.True(Assert.Single(generated).IsVariant);
        Assert.Equal(UposSet.Of(Upos.Verb, Upos.Noun), generated[0].Entry.Tags);

        // Assert: the past tense of set is set, which the term already matches, as the term and not as a variant
        Assert.False(Assert.Single(lemmaForm).IsVariant);
        Assert.Equal("set", lemmaForm[0].Text);
    }

    /// <summary>
    ///     Proves that token tags of the head, of an empty set or of a missing token are rejected.
    /// </summary>
    [Fact]
    public void GlossaryEntry_WithTokenTags_HeadEmptyOrOutOfRange_Throws()
    {
        // Arrange: a two-token entry whose head is the last token
        var entry = new GlossaryEntry("set up", Upos.Verb).WithHead(0);

        // Act / Assert: the head, an empty set and two bad indexes
        Assert.Throws<ArgumentException>(() => entry.WithTokenTags(0, UposSet.Of(Upos.Verb)));
        Assert.Throws<ArgumentException>(() => entry.WithTokenTags(1, UposSet.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => entry.WithTokenTags(-1, UposSet.Of(Upos.Adv)));
        Assert.Throws<ArgumentOutOfRangeException>(() => entry.WithTokenTags(2, UposSet.Of(Upos.Adv)));
    }

    /// <summary>
    ///     Proves that token tags are readable, that a later call replaces an earlier one, and that the head has none.
    /// </summary>
    [Fact]
    public void GlossaryEntry_GetTokenTags_ReturnsOverrideOrNull()
    {
        // Arrange: a three-token entry with an override for the first token, then another one
        var entry = new GlossaryEntry("main bleed valve", Upos.Noun).WithTokenTags(0, UposSet.Of(Upos.Adj));
        var replaced = entry.WithTokenTags(0, UposSet.Of(Upos.Adj, Upos.Noun));

        // Act / Assert: the override, the replacement, no override for the middle token and none for the head
        Assert.Equal(UposSet.Of(Upos.Adj), entry.GetTokenTags(0));
        Assert.Equal(UposSet.Of(Upos.Adj, Upos.Noun), replaced.GetTokenTags(0));
        Assert.Null(entry.GetTokenTags(1));
        Assert.Null(entry.GetTokenTags(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => entry.GetTokenTags(3));
    }

    /// <summary>
    ///     Proves that an override for a token that later becomes the head is ignored while it is the head.
    /// </summary>
    [Fact]
    public void GlossaryEntry_GetTokenTags_TokenBecomesHead_ReturnsNull()
    {
        // Arrange: an override for the first token, then the first token becomes the head
        var entry = new GlossaryEntry("take care", Upos.Verb).WithTokenTags(0, UposSet.Of(Upos.Adj));

        // Act: move the head to the first token
        var moved = entry.WithHead(0);

        // Assert: before the move the override is visible, after it the head has none
        Assert.NotNull(entry.GetTokenTags(0));
        Assert.Null(moved.GetTokenTags(0));
    }

    /// <summary>
    ///     Proves that an undefined mode is rejected.
    /// </summary>
    [Fact]
    public void GlossaryEntry_WithMode_UndefinedMode_ThrowsArgumentOutOfRangeException()
    {
        // Arrange: an entry
        var entry = new GlossaryEntry("valve", Upos.Noun);

        // Act / Assert: 7 is not a mode
        Assert.Throws<ArgumentOutOfRangeException>(() => entry.WithMode((GlossaryMode)7));
    }

    /// <summary>
    ///     Proves that a collection initializer builds the entries in the order written.
    /// </summary>
    [Fact]
    public void GlossaryBuilder_CollectionInitializer_BuildsEntriesInOrder()
    {
        // Arrange / Act: strings with a tag, strings with a tag set, and a full entry
        var glossary = new GlossaryBuilder
        {
            { "bleed valve", Upos.Noun, "A valve." },
            { "isolate", UposSet.Of(Upos.Verb, Upos.Noun) },
            new GlossaryEntry("LRU", Upos.Noun).WithCaseSensitive(true),
        }.Build();

        // Assert: three entries in order with the values given
        Assert.Equal(3, glossary.Count);
        Assert.Equal(new[] { "bleed valve", "isolate", "LRU" }, glossary.Entries.Select(e => e.Term));
        Assert.Equal("A valve.", glossary.Entries[0].Definition);
        Assert.Equal(UposSet.Of(Upos.Verb, Upos.Noun), glossary.Entries[1].Tags);
        Assert.True(glossary.Entries[2].CaseSensitive);
    }

    /// <summary>
    ///     Proves that Add returns the builder so that calls chain, and that Count and enumeration reflect the entries.
    /// </summary>
    [Fact]
    public void GlossaryBuilder_Add_Chained_CountsAndEnumerates()
    {
        // Arrange: an empty builder
        var builder = new GlossaryBuilder();

        // Act: chain three different Add overloads
        var returned = builder
            .Add("valve", Upos.Noun)
            .Add("isolate", UposSet.Of(Upos.Verb))
            .Add(new GlossaryEntry("wrench", Upos.Noun));

        // Assert: the same builder, three entries, enumerated in order
        Assert.Same(builder, returned);
        Assert.Equal(3, builder.Count);
        Assert.Equal(new[] { "valve", "isolate", "wrench" }, builder.Select(e => e.Term));
    }

    /// <summary>
    ///     Proves that a null entry is rejected.
    /// </summary>
    [Fact]
    public void GlossaryBuilder_Add_NullEntry_ThrowsArgumentNullException()
    {
        // Arrange / Act / Assert: null entry
        Assert.Throws<ArgumentNullException>(() => new GlossaryBuilder().Add(null!));
    }

    /// <summary>
    ///     Proves that the same term under the same case rule is rejected, also with another letter case.
    /// </summary>
    [Fact]
    public void GlossaryBuilder_Add_DuplicateSameCase_ThrowsArgumentException()
    {
        // Arrange: a builder with one case-insensitive entry
        var builder = new GlossaryBuilder { { "bleed valve", Upos.Noun } };

        // Act / Assert: the same term, another spelling of the case, and the same term with extra white space
        Assert.Throws<ArgumentException>(() => builder.Add("bleed valve", Upos.Noun));
        Assert.Throws<ArgumentException>(() => builder.Add("Bleed VALVE", Upos.Verb));
        Assert.Throws<ArgumentException>(() => builder.Add("bleed   valve", Upos.Noun));
        Assert.Equal(1, builder.Count);
    }

    /// <summary>
    ///     Proves that a case-sensitive and a case-insensitive entry for the same term may coexist.
    /// </summary>
    [Fact]
    public void GlossaryBuilder_Add_CaseSensitiveAndInsensitiveSameTerm_Allowed()
    {
        // Arrange / Act: an insensitive and a sensitive entry for the same term
        var builder = new GlossaryBuilder
        {
            { "it", Upos.Pron },
            new GlossaryEntry("IT", Upos.Noun).WithCaseSensitive(true),
            new GlossaryEntry("it", Upos.Noun).WithCaseSensitive(true),
        };

        // Assert: all three are accepted
        Assert.Equal(3, builder.Count);
    }

    /// <summary>
    ///     Proves that two case-sensitive entries that differ in letter case are not duplicates, but identical ones are.
    /// </summary>
    [Fact]
    public void GlossaryBuilder_Add_CaseSensitiveEntries_DuplicateOnlyWhenExactlyEqual()
    {
        // Arrange: a builder with a case-sensitive entry
        var builder = new GlossaryBuilder().Add(new GlossaryEntry("IT", Upos.Noun).WithCaseSensitive(true));

        // Act / Assert: a different case is fine, the same case is a duplicate
        builder.Add(new GlossaryEntry("It", Upos.Noun).WithCaseSensitive(true));
        Assert.Throws<ArgumentException>(() => builder.Add(new GlossaryEntry("IT", Upos.Verb).WithCaseSensitive(true)));
    }

    /// <summary>
    ///     Proves that the glossary-wide case rule decides what a duplicate is.
    /// </summary>
    [Fact]
    public void GlossaryBuilder_Add_GlossaryWideCaseSensitive_DecidesDuplicates()
    {
        // Arrange: a builder whose entries are case-sensitive unless they say otherwise
        var builder = new GlossaryBuilder(new GlossaryOptions { CaseSensitive = true }) { { "IT", Upos.Noun } };

        // Act / Assert: "it" differs under the glossary-wide rule, an explicit sensitive "IT" does not, and an insensitive "IT" differs
        builder.Add("it", Upos.Pron);
        Assert.Throws<ArgumentException>(() => builder.Add(new GlossaryEntry("IT", Upos.Noun).WithCaseSensitive(true)));
        builder.Add(new GlossaryEntry("IT", Upos.Noun).WithCaseSensitive(false));
        Assert.Equal(3, builder.Count);
    }

    /// <summary>
    ///     Proves that a built glossary is a snapshot that later additions do not change.
    /// </summary>
    [Fact]
    public void GlossaryBuilder_Build_IsSnapshot()
    {
        // Arrange: a builder with one entry, built once
        var builder = new GlossaryBuilder { { "valve", Upos.Noun } };
        var first = builder.Build();

        // Act: add another entry and build again
        builder.Add("wrench", Upos.Noun);
        var second = builder.Build();

        // Assert: the first glossary is unchanged and the builder is still usable
        Assert.Equal(1, first.Count);
        Assert.Equal(2, second.Count);
        Assert.Empty(first.FindMatches(Tokenizer.Tokenize("Use a wrench.")[0]));
        Assert.Single(second.FindMatches(Tokenizer.Tokenize("Use a wrench.")[0]));
    }

    /// <summary>
    ///     Proves that invalid options are rejected when the builder is created.
    /// </summary>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void GlossaryBuilder_Constructor_InvalidSoftStrength_ThrowsArgumentOutOfRangeException(double strength)
    {
        // Arrange / Act / Assert: the strength must be finite and greater than zero
        Assert.Throws<ArgumentOutOfRangeException>(() => new GlossaryBuilder(new GlossaryOptions { SoftStrength = strength }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Glossary.Create(new GlossaryEntry[0], new GlossaryOptions { SoftStrength = strength }));
    }

    /// <summary>
    ///     Proves that an undefined mode in the options is rejected.
    /// </summary>
    [Fact]
    public void GlossaryBuilder_Constructor_UndefinedMode_ThrowsArgumentOutOfRangeException()
    {
        // Arrange / Act / Assert: 5 is not a mode
        Assert.Throws<ArgumentOutOfRangeException>(() => new GlossaryBuilder(new GlossaryOptions { Mode = (GlossaryMode)5 }));
    }

    /// <summary>
    ///     Proves that the defaults of the options are the documented ones.
    /// </summary>
    [Fact]
    public void GlossaryOptions_Defaults_AreCaseInsensitiveHardInflecting()
    {
        // Arrange / Act: default options
        var options = new GlossaryOptions();

        // Assert: documented defaults
        Assert.False(options.CaseSensitive);
        Assert.Equal(GlossaryMode.Hard, options.Mode);
        Assert.True(options.Inflect);
        Assert.Equal(GlossaryOptions.DefaultSoftStrength, options.SoftStrength);
    }

    /// <summary>
    ///     Proves that the default soft strength is a usable number (the literal value is chosen by the development evaluation, not asserted here).
    /// </summary>
    [Fact]
    public void GlossaryOptions_DefaultSoftStrength_IsFiniteAndPositive()
    {
        // Arrange / Act / Assert: finite and positive, so that a glossary with the default options can be created
        Assert.False(double.IsNaN(GlossaryOptions.DefaultSoftStrength) || double.IsInfinity(GlossaryOptions.DefaultSoftStrength));
        Assert.True(GlossaryOptions.DefaultSoftStrength > 0);
        Assert.NotNull(new GlossaryBuilder(new GlossaryOptions()).Build());
    }

    /// <summary>
    ///     Proves that the options of a glossary are a copy of those it was created with, and a new copy on every read.
    /// </summary>
    [Fact]
    public void Glossary_Options_ReturnsCopy()
    {
        // Arrange: a glossary created with non-default options
        var source = new GlossaryOptions { CaseSensitive = true, Mode = GlossaryMode.Soft, SoftStrength = 2.5, Inflect = false };
        var glossary = Glossary.Create(new GlossaryEntry[0], source);

        // Act: read the options twice
        var first = glossary.Options;
        var second = glossary.Options;

        // Assert: equal values, but never the object that was passed in and never the same object twice
        Assert.NotSame(source, first);
        Assert.NotSame(first, second);
        Assert.True(first.CaseSensitive);
        Assert.Equal(GlossaryMode.Soft, first.Mode);
        Assert.Equal(2.5, first.SoftStrength);
        Assert.False(first.Inflect);
    }

    /// <summary>
    ///     Proves that Create rejects a null collection and a null entry.
    /// </summary>
    [Fact]
    public void Glossary_Create_NullEntriesOrNullItem_ThrowsArgumentNullException()
    {
        // Arrange / Act / Assert: null collection and a collection with a null item
        Assert.Throws<ArgumentNullException>(() => Glossary.Create(null!));
        Assert.Throws<ArgumentNullException>(() => Glossary.Create(new GlossaryEntry[] { new("valve", Upos.Noun), null! }));
    }

    /// <summary>
    ///     Proves that Create rejects duplicates.
    /// </summary>
    [Fact]
    public void Glossary_Create_Duplicate_ThrowsArgumentException()
    {
        // Arrange / Act / Assert: two entries for one term
        Assert.Throws<ArgumentException>(() => Glossary.Create(new[] { new GlossaryEntry("valve", Upos.Noun), new GlossaryEntry("Valve", Upos.Verb) }));
    }

    /// <summary>
    ///     Proves that the empty glossary has no entries and that Create of nothing gives an empty glossary.
    /// </summary>
    [Fact]
    public void Glossary_Empty_HasNoEntries()
    {
        // Arrange / Act: the shared empty glossary and one that was created from nothing
        var created = Glossary.Create(new GlossaryEntry[0]);

        // Assert: both are empty, and the shared one is one instance
        Assert.Equal(0, Glossary.Empty.Count);
        Assert.Empty(Glossary.Empty.Entries);
        Assert.Same(Glossary.Empty, Glossary.Empty);
        Assert.Equal(0, created.Count);
    }

    /// <summary>
    ///     Proves that changing the source collection after Create does not change the glossary.
    /// </summary>
    [Fact]
    public void Glossary_Create_SourceChangedAfterwards_DoesNotChangeGlossary()
    {
        // Arrange: a mutable list of entries
        var source = new List<GlossaryEntry> { new("valve", Upos.Noun) };
        var glossary = Glossary.Create(source);

        // Act: change the list
        source.Add(new GlossaryEntry("wrench", Upos.Noun));
        source.Clear();

        // Assert: the glossary still has its one entry
        Assert.Equal(1, glossary.Count);
        Assert.Equal("valve", glossary.Entries.Single().Term);
    }

    /// <summary>
    ///     Proves that the entries of a glossary cannot be modified through the returned list.
    /// </summary>
    [Fact]
    public void Glossary_Immutable_EntriesAreReadOnly()
    {
        // Arrange: a glossary with one entry
        var glossary = Glossary.Create(new[] { new GlossaryEntry("valve", Upos.Noun) });

        // Act: try to reach a writable collection behind the view
        var asList = glossary.Entries as IList<GlossaryEntry>;
        var asArray = glossary.Entries as GlossaryEntry[];

        // Assert: it is no array, and a list view is read-only
        Assert.Null(asArray);
        Assert.True(asList == null || asList.IsReadOnly);
        if (asList != null)
        {
            Assert.Throws<NotSupportedException>(() => asList.Add(new GlossaryEntry("x", Upos.Noun)));
        }
    }

    /// <summary>
    ///     Proves the regular plural rules.
    /// </summary>
    [Theory]
    [InlineData("valve", "valves")]
    [InlineData("box", "boxes")]
    [InlineData("bus", "buses")]
    [InlineData("buzz", "buzzes")]
    [InlineData("church", "churches")]
    [InlineData("wish", "wishes")]
    [InlineData("battery", "batteries")]
    [InlineData("key", "keys")]
    [InlineData("Valve", "Valves")]
    [InlineData("LRU", "LRUs")]
    [InlineData("x-ray", "x-rays")]
    public void GlossaryInflector_Plural_FollowsRegularRules(string word, string expected)
    {
        // Arrange / Act: inflect
        var plural = GlossaryInflector.Plural(word);

        // Assert: the regular plural
        Assert.Equal(expected, plural);
    }

    /// <summary>
    ///     Proves the regular past-tense rules, including consonant doubling for short words.
    /// </summary>
    [Theory]
    [InlineData("isolate", "isolated")]
    [InlineData("stop", "stopped")]
    [InlineData("plug", "plugged")]
    [InlineData("open", "opened")]
    [InlineData("bleed", "bleeded")]
    [InlineData("carry", "carried")]
    [InlineData("tie", "tied")]
    [InlineData("see", "seed")]
    [InlineData("fix", "fixed")]
    [InlineData("show", "showed")]
    [InlineData("play", "played")]
    [InlineData("permit", "permited")]
    public void GlossaryInflector_Past_FollowsRegularRules(string word, string expected)
    {
        // Arrange / Act: inflect
        var past = GlossaryInflector.Past(word);

        // Assert: the regular past form (irregular and multi-syllable doubling are not generated)
        Assert.Equal(expected, past);
    }

    /// <summary>
    ///     Proves the regular present-participle rules.
    /// </summary>
    [Theory]
    [InlineData("isolate", "isolating")]
    [InlineData("stop", "stopping")]
    [InlineData("plug", "plugging")]
    [InlineData("open", "opening")]
    [InlineData("bleed", "bleeding")]
    [InlineData("carry", "carrying")]
    [InlineData("tie", "tying")]
    [InlineData("see", "seeing")]
    [InlineData("dye", "dyeing")]
    [InlineData("toe", "toeing")]
    [InlineData("hide", "hiding")]
    [InlineData("fix", "fixing")]
    public void GlossaryInflector_PresentParticiple_FollowsRegularRules(string word, string expected)
    {
        // Arrange / Act: inflect
        var participle = GlossaryInflector.PresentParticiple(word);

        // Assert: the regular -ing form
        Assert.Equal(expected, participle);
    }

    /// <summary>
    ///     Proves the forms and the derived tags of a noun entry.
    /// </summary>
    [Fact]
    public void GlossaryInflector_Inflect_Noun_GivesPluralNoun()
    {
        // Arrange / Act: inflect a noun
        var forms = GlossaryInflector.Inflect("valve", UposSet.Of(Upos.Noun));

        // Assert: one form, a noun
        var form = Assert.Single(forms);
        Assert.Equal("valves", form.Text);
        Assert.Equal(UposSet.Of(Upos.Noun), form.Tags);
    }

    /// <summary>
    ///     Proves the forms and the derived tags of a verb entry.
    /// </summary>
    [Fact]
    public void GlossaryInflector_Inflect_Verb_GivesThreeFormsWithDerivedTags()
    {
        // Arrange / Act: inflect a verb
        var forms = GlossaryInflector.Inflect("isolate", UposSet.Of(Upos.Verb)).ToDictionary(f => f.Text, f => f.Tags);

        // Assert: third person is a verb, the past form a verb or adjective, the participle a verb, adjective or noun
        Assert.Equal(3, forms.Count);
        Assert.Equal(UposSet.Of(Upos.Verb), forms["isolates"]);
        Assert.Equal(UposSet.Of(Upos.Verb, Upos.Adj), forms["isolated"]);
        Assert.Equal(UposSet.Of(Upos.Verb, Upos.Adj, Upos.Noun), forms["isolating"]);
    }

    /// <summary>
    ///     Proves that two rules that give one spelling unite their tags.
    /// </summary>
    [Fact]
    public void GlossaryInflector_Inflect_NounAndVerb_UnitesTagsOfEqualSpelling()
    {
        // Arrange / Act: a word that is a noun and a verb, so "bleeds" is a plural and a third-person form
        var forms = GlossaryInflector.Inflect("bleed", UposSet.Of(Upos.Noun, Upos.Verb)).ToDictionary(f => f.Text, f => f.Tags);

        // Assert: one spelling with both tags; irregular "bled" is not generated
        Assert.Equal(UposSet.Of(Upos.Noun, Upos.Verb), forms["bleeds"]);
        Assert.DoesNotContain("bled", forms.Keys);
        Assert.Equal(3, forms.Count);
    }

    /// <summary>
    ///     Proves that a proper noun keeps its tag in the plural of an entry that has both noun tags.
    /// </summary>
    [Fact]
    public void GlossaryInflector_Inflect_NounAndProperNoun_PluralHasBothTags()
    {
        // Arrange / Act: inflect
        var form = Assert.Single(GlossaryInflector.Inflect("valve", UposSet.Of(Upos.Noun, Upos.Propn)));

        // Assert: both tags
        Assert.Equal(UposSet.Of(Upos.Noun, Upos.Propn), form.Tags);
    }

    /// <summary>
    ///     Proves that proper nouns, adjectives and other tags are not inflected.
    /// </summary>
    [Theory]
    [InlineData(Upos.Propn)]
    [InlineData(Upos.Adj)]
    [InlineData(Upos.Adv)]
    [InlineData(Upos.Num)]
    [InlineData(Upos.X)]
    public void GlossaryInflector_Inflect_OtherTags_GivesNothing(Upos tag)
    {
        // Arrange / Act: inflect with a tag that has no rule
        var forms = GlossaryInflector.Inflect("valve", UposSet.Of(tag));

        // Assert: nothing
        Assert.Empty(forms);
    }

    /// <summary>
    ///     Proves that words the English rules do not fit are not inflected.
    /// </summary>
    [Theory]
    [InlineData("x")]
    [InlineData("R&D")]
    [InlineData("3.5")]
    [InlineData("COVID-19")]
    [InlineData("don't")]
    [InlineData("U.S.")]
    [InlineData("naïve")]
    public void GlossaryInflector_Inflect_NotInflectableWord_GivesNothing(string word)
    {
        // Arrange / Act: inflect a noun and verb entry of such a word
        var forms = GlossaryInflector.Inflect(word, UposSet.Of(Upos.Noun, Upos.Verb));

        // Assert: nothing
        Assert.Empty(forms);
    }

    /// <summary>
    ///     Proves that only the head word of a multiword entry is inflected, and that its other words stay as written.
    /// </summary>
    [Fact]
    public void Glossary_Create_MultiwordEntry_InflectsOnlyHeadWord()
    {
        // Arrange: a two-word noun entry
        var glossary = Glossary.Create(new[] { new GlossaryEntry("bleed valve", Upos.Noun) });

        // Act: look for the plural of the head and the plural of the first word
        var head = glossary.FindMatches(Tokenizer.Tokenize("Close the bleed valves.")[0]);
        var first = glossary.FindMatches(Tokenizer.Tokenize("Close the bleeds valve.")[0]);

        // Assert: the head is inflected, the other word is not
        Assert.Single(head);
        Assert.Empty(first);
    }

    /// <summary>
    ///     Proves that the number of forms follows the inflection switches (glossary-wide and per entry).
    /// </summary>
    [Fact]
    public void Glossary_Create_InflectionSwitches_ControlGeneratedForms()
    {
        // Arrange: the same entries with inflection on, off glossary-wide, and off for one entry only
        var entry = new GlossaryEntry("valve", Upos.Noun);
        var on = Glossary.Create(new[] { entry });
        var offGlobally = Glossary.Create(new[] { entry }, new GlossaryOptions { Inflect = false });
        var offForEntry = Glossary.Create(new[] { entry.WithInflection(false) });
        var onForEntry = Glossary.Create(new[] { entry.WithInflection(true) }, new GlossaryOptions { Inflect = false });

        // Act: the number of spellings that each index holds
        var forms = new[] { on, offGlobally, offForEntry, onForEntry }.Select(g => g.Index.FormCount).ToArray();

        // Assert: the term and its plural, or the term only; the entry setting beats the glossary setting
        Assert.Equal(new[] { 2, 1, 1, 2 }, forms);
    }
}
