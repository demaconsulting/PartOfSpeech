// cspell:ignore FFFD zzyzx
namespace DemaConsulting.PartOfSpeech.Tests.Tagging;

/// <summary>
///     Unit tests of <see cref="FeatureExtractor"/>.
/// </summary>
public class FeatureExtractorTests
{
    /// <summary>
    ///     The bucket space exponent used by most tests.
    /// </summary>
    private const int Bits = 21;

    /// <summary>
    ///     Prepares a workspace for the tokens and extracts the buckets of one token with sentence-start history.
    /// </summary>
    private static int[] Extract(string[] tokens, int index, int previous1 = FeatureExtractor.StartTag1, int previous2 = FeatureExtractor.StartTag2, int bits = Bits)
    {
        var workspace = new DecoderWorkspace();
        FeatureExtractor.Prepare(workspace, tokens, bits);
        var buckets = new int[FeatureExtractor.MaxFeatures];
        var count = FeatureExtractor.Extract(workspace, index, previous1, previous2, bits, buckets);
        return buckets.Take(count).ToArray();
    }

    /// <summary>
    ///     Gets the spelling of a history code for the reference extractor.
    /// </summary>
    private static string Spell(int code)
    {
        if (code == FeatureExtractor.StartTag1)
        {
            return "-S-";
        }

        return code == FeatureExtractor.StartTag2 ? "-S2-" : ((Upos)code).ToTagString();
    }

    /// <summary>
    ///     Proves that a long first word has all 22 templates and a later word 21 (the initial flag is only on the first word).
    /// </summary>
    [Fact]
    public void FeatureExtractor_Extract_EmitsTwentyTwoTemplates()
    {
        // Arrange: a sentence whose words have at least four characters
        var tokens = new[] { "Hello", "wonderful", "world" };

        // Act: extract the first and the second token
        var first = Extract(tokens, 0);
        var second = Extract(tokens, 1);

        // Assert: 22 buckets for the initial word, 21 otherwise, equal to the string-based reference
        Assert.Equal(FeatureExtractor.MaxFeatures, first.Length);
        Assert.Equal(FeatureExtractor.MaxFeatures - 1, second.Length);
        Assert.Equal(FeatureExtractor.ReferenceBuckets(tokens, 0, "-S-", "-S2-", Bits), first);
        Assert.Equal(FeatureExtractor.ReferenceBuckets(tokens, 1, "-S-", "-S2-", Bits), second);
    }

    /// <summary>
    ///     Proves that affixes longer than the token are omitted.
    /// </summary>
    [Fact]
    public void FeatureExtractor_Extract_ShortToken_OmitsLongPrefixesAndSuffixes()
    {
        // Arrange: tokens of one and two characters as first words
        var one = new[] { "a", "cat" };
        var two = new[] { "to", "go" };

        // Act: extract
        var oneBuckets = Extract(one, 0);
        var twoBuckets = Extract(two, 0);

        // Assert: one affix pair (16 buckets) and two pairs (18 buckets); both agree with the reference
        Assert.Equal(16, oneBuckets.Length);
        Assert.Equal(18, twoBuckets.Length);
        Assert.Equal(FeatureExtractor.ReferenceBuckets(one, 0, "-S-", "-S2-", Bits), oneBuckets);
        Assert.Equal(FeatureExtractor.ReferenceBuckets(two, 0, "-S-", "-S2-", Bits), twoBuckets);
    }

    /// <summary>
    ///     Proves the shape letters and shape bits of the spike for typical tokens.
    /// </summary>
    /// <param name="token">The token.</param>
    /// <param name="letters">The expected shape letters.</param>
    /// <param name="bits">The expected shape bits.</param>
    [Theory]
    [InlineData("cat", "l", 0)]
    [InlineData("Cat", "U", 1)]
    [InlineData("CAT", "UA", 3)]
    [InlineData("A", "U", 1)]
    [InlineData("3d", "D", 4)]
    [InlineData("Covid-19", "UDH", 13)]
    [InlineData("don't", "Q", 16)]
    [InlineData("...", "P", 32)]
    [InlineData("U.S.", "UA", 3)]
    [InlineData("well-known", "H", 8)]
    [InlineData("1,000", "D", 4)]
    public void FeatureExtractor_Shape_FollowsSpikeBits(string token, string letters, int bits)
    {
        // Arrange: a workspace holding the token
        var workspace = new DecoderWorkspace();
        FeatureExtractor.Prepare(workspace, new[] { token }, Bits);

        // Act: read the string form and the bits of the fast path
        var spelled = FeatureExtractor.ShapeLetters(token);
        var actualBits = workspace.Shape[0];

        // Assert: both agree with the expectation
        Assert.Equal(letters, spelled);
        Assert.Equal(bits, (int)actualBits);
    }

    /// <summary>
    ///     Proves that the initial flag goes to the first token with a letter or digit, not to a leading quote.
    /// </summary>
    [Fact]
    public void FeatureExtractor_Extract_InitialFlag_SkipsLeadingPunctuation()
    {
        // Arrange: a leading quote, then two words
        var tokens = new[] { "\"", "Hello", "world" };
        var initial = FeatureExtractor.ReferenceBucket(FeatureKind.Initial, null, null, Bits);

        // Act: extract all three
        var quote = Extract(tokens, 0);
        var hello = Extract(tokens, 1);
        var world = Extract(tokens, 2);

        // Assert: only the first word carries the flag
        Assert.DoesNotContain(initial, quote);
        Assert.Contains(initial, hello);
        Assert.DoesNotContain(initial, world);
    }

    /// <summary>
    ///     Proves that missing neighbors are replaced by the sentinel words at both ends.
    /// </summary>
    [Fact]
    public void FeatureExtractor_Extract_Neighbors_UseSentinelsAtBoundaries()
    {
        // Arrange: a two-token sentence
        var tokens = new[] { "Alpha", "Beta" };

        // Act: extract both tokens
        var first = Extract(tokens, 0);
        var second = Extract(tokens, 1);

        // Assert: the neighbor buckets are those of the sentinels or the other word (lower-cased)
        Assert.Contains(FeatureExtractor.ReferenceBucket(FeatureKind.Prev1, "-S-", null, Bits), first);
        Assert.Contains(FeatureExtractor.ReferenceBucket(FeatureKind.Prev2, "-S2-", null, Bits), first);
        Assert.Contains(FeatureExtractor.ReferenceBucket(FeatureKind.Next1, "beta", null, Bits), first);
        Assert.Contains(FeatureExtractor.ReferenceBucket(FeatureKind.Next2, "-E2-", null, Bits), first);
        Assert.Contains(FeatureExtractor.ReferenceBucket(FeatureKind.PrevNext, "-S-", "beta", Bits), first);
        Assert.Contains(FeatureExtractor.ReferenceBucket(FeatureKind.Prev1, "alpha", null, Bits), second);
        Assert.Contains(FeatureExtractor.ReferenceBucket(FeatureKind.Prev2, "-S2-", null, Bits), second);
        Assert.Contains(FeatureExtractor.ReferenceBucket(FeatureKind.Next1, "-E-", null, Bits), second);
        Assert.Contains(FeatureExtractor.ReferenceBucket(FeatureKind.PrevNext, "alpha", "-E-", Bits), second);
    }

    /// <summary>
    ///     Proves that typographic quotes, dashes and the ellipsis are folded to ASCII and that features see the folded form.
    /// </summary>
    [Fact]
    public void FeatureExtractor_Extract_Fold_MapsTypographicQuotesAndDashes()
    {
        // Arrange: typographic input and its ASCII twin
        var typographic = new[] { "don\u2019t", "\u201Chi\u201D", "a\u2014b", "wait\u2026", "x\u2013y" };
        var ascii = new[] { "don't", "\"hi\"", "a--b", "wait...", "x--y" };

        // Act: fold and extract
        var folded = FeatureExtractor.Fold(string.Concat(typographic));
        var typographicBuckets = Enumerable.Range(0, 5).Select(i => Extract(typographic, i)).ToList();
        var asciiBuckets = Enumerable.Range(0, 5).Select(i => Extract(ascii, i)).ToList();

        // Assert: the folded string is the ASCII form, and the features are identical
        Assert.Equal(string.Concat(ascii), folded);
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(asciiBuckets[i], typographicBuckets[i]);
        }
    }

    /// <summary>
    ///     Proves that decomposed and composed spellings of the same text give the same features.
    /// </summary>
    [Fact]
    public void FeatureExtractor_Extract_Fold_NfcNormalizesNonAscii()
    {
        // Arrange: caf + e + combining acute versus the single composed character
        var decomposed = new[] { "cafe\u0301", "x" };
        var composed = new[] { "caf\u00E9", "x" };

        // Act: fold and extract
        var folded = FeatureExtractor.Fold(decomposed[0]);
        var a = Extract(decomposed, 0);
        var b = Extract(composed, 0);

        // Assert: NFC form and equal buckets
        Assert.Equal("caf\u00E9", folded);
        Assert.Equal(b, a);
        Assert.Equal(FeatureExtractor.ReferenceBuckets(decomposed, 0, "-S-", "-S2-", Bits), a);
    }

    /// <summary>
    ///     Proves that an unpaired surrogate counts as U+FFFD for the features without failing, that a valid pair is kept and that the rest of the token is still normalized.
    /// </summary>
    [Fact]
    public void FeatureExtractor_Extract_Fold_UnpairedSurrogate_UsesReplacementCharacter()
    {
        // Arrange: lone high, lone low and reversed-pair tokens, their replacement twins, and a valid pair
        var malformed = new[] { "a\uD800b", "\uDC00x", "ca\uDE00\uD83Dt", "e\u0301\uDC00", "\U0001F600" };
        var replaced = new[] { "a\uFFFDb", "\uFFFDx", "ca\uFFFD\uFFFDt", "e\u0301\uFFFD", "\U0001F600" };

        // Act: fold and extract without an exception
        var folded = malformed.Select(FeatureExtractor.Fold).ToList();
        var foldedBuckets = Enumerable.Range(0, malformed.Length).Select(i => Extract(malformed, i)).ToList();
        var replacedBuckets = Enumerable.Range(0, replaced.Length).Select(i => Extract(replaced, i)).ToList();

        // Assert: folded as the replacement twins (NFC still applied), equal features, equal to the reference
        Assert.Equal(new[] { "a\uFFFDb", "\uFFFDx", "ca\uFFFD\uFFFDt", "\u00E9\uFFFD", "\U0001F600" }, folded);
        for (var i = 0; i < malformed.Length; i++)
        {
            Assert.Equal(replacedBuckets[i], foldedBuckets[i]);
            Assert.Equal(FeatureExtractor.ReferenceBuckets(malformed, i, "-S-", "-S2-", Bits), foldedBuckets[i]);
        }
    }

    /// <summary>
    ///     Proves that the allocation-free fast path equals the string-based reference for thousands of corpus tokens with varying histories and both extreme bucket space sizes.
    /// </summary>
    [Fact]
    public void FeatureExtractor_Extract_MatchesReferenceStringHashing()
    {
        // Arrange: 400 development sentences
        var sentences = DataFiles.ReadCorpus(DataFiles.Data("open/dev.tsv")).Take(400).ToList();
        var workspace = new DecoderWorkspace();
        var buckets = new int[FeatureExtractor.MaxFeatures];
        var compared = 0;

        // Act / Assert: compare every token for two bucket spaces, with the gold tags of the corpus as history
        foreach (var sentence in sentences)
        {
            var tokens = sentence.Tokens.Select(t => t[0]).ToArray();
            var gold = sentence.Tokens.Select(t => (int)ParseTag(t[1])).ToArray();
            foreach (var bits in new[] { 18, 24 })
            {
                FeatureExtractor.Prepare(workspace, tokens, bits);
                for (var i = 0; i < tokens.Length; i++)
                {
                    var p1 = i >= 1 ? gold[i - 1] : FeatureExtractor.StartTag1;
                    var p2 = History2(gold, i);
                    var count = FeatureExtractor.Extract(workspace, i, p1, p2, bits, buckets);
                    var expected = FeatureExtractor.ReferenceBuckets(tokens, i, Spell(p1), Spell(p2), bits);
                    Assert.Equal(expected, buckets.Take(count).ToArray());
                    compared++;
                }
            }
        }

        Assert.True(compared > 10000);
    }

    /// <summary>
    ///     Proves that buckets are always inside the bucket space and do not depend on what the workspace held before.
    /// </summary>
    [Fact]
    public void FeatureExtractor_Buckets_AreInRangeAndStable()
    {
        // Arrange: one workspace reused for a long and then a short sentence
        var workspace = new DecoderWorkspace();
        var buckets = new int[FeatureExtractor.MaxFeatures];
        var longSentence = Enumerable.Range(0, 80).Select(i => "word" + i).ToArray();
        var shortSentence = new[] { "Hello", "world" };
        FeatureExtractor.Prepare(workspace, longSentence, 18);

        // Act: prepare the short sentence twice and extract
        FeatureExtractor.Prepare(workspace, shortSentence, 18);
        var count = FeatureExtractor.Extract(workspace, 0, 3, 4, 18, buckets);
        var first = buckets.Take(count).ToArray();
        FeatureExtractor.Prepare(workspace, shortSentence, 18);
        count = FeatureExtractor.Extract(workspace, 0, 3, 4, 18, buckets);
        var second = buckets.Take(count).ToArray();

        // Assert: stable, in range, and pinned hash vectors (FNV-1a over UTF-16 code units equals the byte vectors for ASCII)
        Assert.Equal(first, second);
        Assert.All(first, b => Assert.InRange(b, 0, (1 << 18) - 1));
        Assert.Equal(0xE40C292Cu, FeatureExtractor.HashString("a"));
        Assert.Equal(0xBF9CF968u, FeatureExtractor.HashString("foobar"));
        Assert.Equal(0x811C9DC5u, FeatureExtractor.HashString(string.Empty));
    }

    /// <summary>
    ///     Proves that the cased word feature distinguishes capitalization and the lower-cased feature does not.
    /// </summary>
    [Fact]
    public void FeatureExtractor_Hash_IsCaseSensitiveWhereTemplatesAre()
    {
        // Arrange: the same word with two capitalizations
        var upper = new[] { "Cat" };
        var lower = new[] { "cat" };

        // Act: extract both
        var a = Extract(upper, 0);
        var b = Extract(lower, 0);

        // Assert: the cased word differs, the lower-cased word is shared
        Assert.Contains(FeatureExtractor.ReferenceBucket(FeatureKind.Word, "Cat", null, Bits), a);
        Assert.DoesNotContain(FeatureExtractor.ReferenceBucket(FeatureKind.Word, "Cat", null, Bits), b);
        Assert.Contains(FeatureExtractor.ReferenceBucket(FeatureKind.Lower, "cat", null, Bits), a);
        Assert.Contains(FeatureExtractor.ReferenceBucket(FeatureKind.Lower, "cat", null, Bits), b);
        Assert.NotEqual(
            FeatureExtractor.ReferenceBucket(FeatureKind.Word, "cat", null, Bits),
            FeatureExtractor.ReferenceBucket(FeatureKind.Lower, "cat", null, Bits));
    }

    /// <summary>
    ///     Proves that the tag features use the previous tags and that the extractor accepts any history.
    /// </summary>
    [Fact]
    public void FeatureExtractor_Extract_TagHistory_ChangesOnlyTagFeatures()
    {
        // Arrange: one token (long enough for all affixes) with two histories
        var tokens = new[] { "running" };

        // Act: extract with different previous tags
        var a = Extract(tokens, 0, (int)Upos.Pron, (int)Upos.Det);
        var b = Extract(tokens, 0, (int)Upos.Noun, (int)Upos.Det);

        // Assert: the first 18 (tag independent) buckets agree, the PrevTag1, PrevTagPair and PrevTagLower buckets differ, PrevTag2 does not
        Assert.Equal(a.Take(18), b.Take(18));
        Assert.NotEqual(a[18], b[18]);
        Assert.Equal(a[19], b[19]);
        Assert.NotEqual(a[20], b[20]);
        Assert.NotEqual(a[21], b[21]);
    }

#if NET
    /// <summary>
    ///     Proves that extracting buckets does not allocate once the workspace has grown.
    /// </summary>
    [Fact]
    public void FeatureExtractor_Extract_DoesNotAllocateAfterWarmUp()
    {
        // Arrange: a sentence, a workspace and a bucket buffer, warmed up once
        var tokens = new[] { "The", "quick", "brown", "fox", "jumps", "over", "the", "lazy", "dog", "." };
        var workspace = new DecoderWorkspace();
        var buckets = new int[FeatureExtractor.MaxFeatures];
        FeatureExtractor.Prepare(workspace, tokens, Bits);
        for (var i = 0; i < tokens.Length; i++)
        {
            FeatureExtractor.Extract(workspace, i, 3, 4, Bits, buckets);
        }

        // Act: prepare and extract again; warm up thoroughly, then take the minimum over several batches of the bytes
        // allocated by this thread for 100 rounds
        var allocated = AllocationMeter.MinimumBatchBytes(
            () =>
            {
                workspace.Begin(tokens.Length);
                for (var i = 0; i < tokens.Length; i++)
                {
                    FeatureExtractor.SetToken(workspace, i, tokens[i]);
                }

                FeatureExtractor.Finish(workspace, Bits);
                for (var i = 0; i < tokens.Length; i++)
                {
                    FeatureExtractor.Extract(workspace, i, 3, 4, Bits, buckets);
                }
            },
            100);

        // Assert: nothing was allocated
        Assert.Equal(0, allocated);
    }
#endif

    /// <summary>
    ///     The lexicon of the group tests: function words, ambiguous words and the stems that the inflection examples need.
    /// </summary>
    private static readonly DemaConsulting.PartOfSpeech.Lexicon GroupLexicon = DemaConsulting.PartOfSpeech.Lexicon.Load(TinyModels.CreateLexicon(
        "the DET",
        "a DET",
        "of ADP",
        "in ADP",
        "she PRON",
        "you PRON",
        "and CCONJ",
        "because SCONJ",
        ", PUNCT",
        "check INTJ NOUN VERB",
        "valve NOUN",
        "dog NOUN",
        "cat NOUN",
        "avoid VERB",
        "make NOUN VERB",
        "run NOUN VERB",
        "stop NOUN VERB",
        "try NOUN VERB",
        "box NOUN VERB",
        "walk NOUN VERB",
        "red ADJ",
        "quickly ADV",
        "sat VERB"));

    /// <summary>
    ///     Gets every set of groups (as masks, since the groups are internal) for the theories.
    /// </summary>
    public static IEnumerable<object[]> GroupMasks() => Enumerable.Range(0, 16).Select(mask => new object[] { (uint)mask });

    /// <summary>
    ///     Prepares a workspace with groups and the group lexicon for the tokens.
    /// </summary>
    private static DecoderWorkspace Prepared(string[] tokens, FeatureGroups groups)
    {
        var workspace = new DecoderWorkspace();
        workspace.Configure(groups, GroupLexicon);
        FeatureExtractor.Prepare(workspace, tokens, Bits);
        return workspace;
    }

    /// <summary>
    ///     Extracts the buckets of one token of a prepared workspace with sentence-start history.
    /// </summary>
    private static int[] ExtractFrom(DecoderWorkspace workspace, int index, int previous1 = FeatureExtractor.StartTag1, int previous2 = FeatureExtractor.StartTag2)
    {
        var buckets = new int[FeatureExtractor.BucketCapacity];
        var count = FeatureExtractor.Extract(workspace, index, previous1, previous2, Bits, buckets);
        return buckets.Take(count).ToArray();
    }

    /// <summary>
    ///     Computes the bucket of a group feature from its codes.
    /// </summary>
    private static int Code(FeatureKind kind, uint a, uint b = 0) => FeatureExtractor.ReferenceBucketOfCodes(kind, a, b, Bits);

    /// <summary>
    ///     Proves that with no group the buckets equal the plain definition, also after the workspace served a model with all groups,
    ///     so that no state of a group leaks into the 22 base templates.
    /// </summary>
    [Fact]
    public void FeatureExtractor_Extract_NoGroups_ProducesOnlyBaseTemplates()
    {
        // Arrange: a workspace that first served all groups, then is configured for none
        var tokens = new[] { "Check", "the", "running", "valve", "." };
        var workspace = new DecoderWorkspace();
        workspace.Configure(FeatureGroups.PositionCues | FeatureGroups.ClauseCues | FeatureGroups.RightPreview | FeatureGroups.Inflection, GroupLexicon);
        FeatureExtractor.Prepare(workspace, tokens, Bits);
        workspace.Configure(FeatureGroups.None, null);

        // Act: prepare again and extract every token, with a stale clause state and preview left behind
        FeatureExtractor.Prepare(workspace, tokens, Bits);
        var fresh = new DecoderWorkspace();
        FeatureExtractor.Prepare(fresh, tokens, Bits);

        // Assert: the base stride, at most 22 buckets, equal to a fresh workspace and to the string-based reference
        Assert.Equal(FeatureExtractor.StaticFeatureCount, workspace.StaticStride);
        for (var i = 0; i < tokens.Length; i++)
        {
            var buckets = ExtractFrom(workspace, i);
            Assert.True(buckets.Length <= FeatureExtractor.MaxFeatures);
            Assert.Equal(ExtractFrom(fresh, i), buckets);
            Assert.Equal(FeatureExtractor.ReferenceBuckets(tokens, i, "-S-", "-S2-", Bits), buckets);
        }

        Assert.Equal(FeatureExtractor.FeatureSetVersion, FeatureExtractor.FeatureSetVersionFor(FeatureGroups.None));
        Assert.Equal(FeatureExtractor.GroupsFeatureSetVersion, FeatureExtractor.FeatureSetVersionFor(FeatureGroups.Inflection));
    }

    /// <summary>
    ///     Proves that for every set of groups the fast path equals the string-based reference on corpus sentences, with the
    ///     real lexicon, gold tags as history and (for the preview group) gold tags as preview.
    /// </summary>
    /// <param name="mask">The groups as a mask.</param>
    [Theory]
    [MemberData(nameof(GroupMasks))]
    public void FeatureExtractor_Extract_GroupsMatchReference(uint mask)
    {
        // Arrange: 60 development sentences and the embedded lexicon
        var groups = (FeatureGroups)mask;
        var lexicon = EmbeddedModel.Lexicon;
        var sentences = DataFiles.ReadCorpus(DataFiles.Data("open/dev.tsv")).Take(60).ToList();
        var workspace = new DecoderWorkspace();
        var buckets = new int[FeatureExtractor.BucketCapacity];
        var compared = 0;

        // Act / Assert: compare every token, advancing the clause state with the gold tag after each token
        foreach (var sentence in sentences)
        {
            var tokens = sentence.Tokens.Select(t => t[0]).ToArray();
            var gold = sentence.Tokens.Select(t => (int)ParseTag(t[1])).ToArray();
            var spelled = gold.Select(Spell).ToList();
            workspace.Configure(groups, lexicon);
            FeatureExtractor.Prepare(workspace, tokens, Bits);
            if ((groups & FeatureGroups.RightPreview) != 0)
            {
                workspace.SetPreview(gold);
                FeatureExtractor.StartPass(workspace, true);
            }

            for (var i = 0; i < tokens.Length; i++)
            {
                var p1 = i >= 1 ? gold[i - 1] : FeatureExtractor.StartTag1;
                var p2 = History2(gold, i);
                var count = FeatureExtractor.Extract(workspace, i, p1, p2, Bits, buckets);
                var expected = FeatureExtractor.ReferenceBuckets(
                    tokens,
                    i,
                    Spell(p1),
                    Spell(p2),
                    Bits,
                    groups,
                    lexicon,
                    spelled,
                    (groups & FeatureGroups.RightPreview) != 0 ? spelled : null);
                Assert.Equal(expected, buckets.Take(count).ToArray());
                Assert.InRange(count, 1, FeatureExtractor.BucketCapacity);
                FeatureExtractor.Advance(workspace, i, gold[i]);
                compared++;
            }
        }

        Assert.True(compared > 300);
    }

    /// <summary>
    ///     Proves the token classes of the clause cues: the first matching rule wins and the fast path equals the readable rules.
    /// </summary>
    /// <param name="word">The token.</param>
    /// <param name="expected">The expected class.</param>
    [Theory]
    [InlineData(",", (int)TokenClass.Punct)]
    [InlineData("will", (int)TokenClass.Modal)]
    [InlineData("Cannot", (int)TokenClass.Modal)]
    [InlineData("to", (int)TokenClass.To)]
    [InlineData("42", (int)TokenClass.Num)]
    [InlineData("the", (int)TokenClass.Det)]
    [InlineData("she", (int)TokenClass.Pron)]
    [InlineData("of", (int)TokenClass.Adp)]
    [InlineData("and", (int)TokenClass.Conj)]
    [InlineData("because", (int)TokenClass.Conj)]
    [InlineData("check", (int)TokenClass.NounVerb)]
    [InlineData("valve", (int)TokenClass.NounOnly)]
    [InlineData("avoid", (int)TokenClass.VerbOnly)]
    [InlineData("red", (int)TokenClass.AdjAdv)]
    [InlineData("quickly", (int)TokenClass.AdjAdv)]
    [InlineData("zzyzx", (int)TokenClass.Other)]
    [InlineData("1,000", (int)TokenClass.Other)]
    public void FeatureExtractor_Extract_ClauseCues_ClassifiesTokens(string word, int expected)
    {
        // Arrange / Act: prepare the token with the clause cues
        var workspace = Prepared(new[] { word }, FeatureGroups.ClauseCues);
        var lower = FeatureExtractor.Fold(word).ToLowerInvariant();
        var tags = GroupLexicon.TryGetTags(lower.ToCharArray(), 0, lower.Length, new byte[GroupLexicon.ScratchSize], out var set) ? set : UposSet.Empty;

        // Assert: both implementations give the class, and the class goes into the neighbor features
        Assert.Equal((TokenClass)expected, (TokenClass)workspace.Classes[0]);
        Assert.Equal((TokenClass)expected, FeatureExtractor.ReferenceTokenClass(FeatureExtractor.Fold(word), lower, tags));
    }

    /// <summary>
    ///     Proves the clause cues of an imperative: the verb before a determiner sees the determiner as next class, and the noun
    ///     after it sees the determiner at distance one; a modal or <c>to</c> before the verb is seen at distance one.
    /// </summary>
    [Fact]
    public void FeatureExtractor_Extract_ClauseCues_ImperativeFollowedByDeterminer()
    {
        // Arrange: an imperative, a clause with a modal and an infinitive
        var imperative = Prepared(new[] { "Check", "the", "valve" }, FeatureGroups.ClauseCues);
        var modal = Prepared(new[] { "You", "will", "check", "the", "valve" }, FeatureGroups.ClauseCues);
        var infinitive = Prepared(new[] { "to", "check" }, FeatureGroups.ClauseCues);
        var boundary = (uint)FeatureExtractor.ClassBoundary;

        // Act: extract
        var check = ExtractFrom(imperative, 0);
        var valve = ExtractFrom(imperative, 2);
        var afterModal = ExtractFrom(modal, 2);
        var afterTo = ExtractFrom(infinitive, 1);

        // Assert: the cues of the written definition
        Assert.Contains(Code(FeatureKind.PrevClass1, boundary), check);
        Assert.Contains(Code(FeatureKind.NextClass1, (uint)TokenClass.Det), check);
        Assert.Contains(Code(FeatureKind.PrevNextClass, boundary, (uint)TokenClass.Det), check);
        Assert.Contains(Code(FeatureKind.DetDist, 0), check);
        Assert.Contains(Code(FeatureKind.ModalDist, 0), check);
        Assert.Contains(Code(FeatureKind.PrevClass1, (uint)TokenClass.Det), valve);
        Assert.Contains(Code(FeatureKind.NextClass1, boundary), valve);
        Assert.Contains(Code(FeatureKind.DetDist, 1), valve);
        Assert.Contains(Code(FeatureKind.ModalDist, 1), afterModal);
        Assert.Contains(Code(FeatureKind.PrevClass1, (uint)TokenClass.Modal), afterModal);
        Assert.Contains(Code(FeatureKind.NextClass1, (uint)TokenClass.Det), afterModal);
        Assert.Contains(Code(FeatureKind.DetDist, 0), afterModal);
        Assert.Contains(Code(FeatureKind.ModalDist, 1), afterTo);
        Assert.Contains(Code(FeatureKind.DetDist, 2), ExtractFrom(Prepared(new[] { "the", "red", "valve" }, FeatureGroups.ClauseCues), 2));
        Assert.Contains(Code(FeatureKind.DetDist, 0), ExtractFrom(Prepared(new[] { "the", "red", "red", "red", "valve" }, FeatureGroups.ClauseCues), 4));
    }

    /// <summary>
    ///     Proves that the clause state records a subject-like and a verb tag, and that a boundary token or a conjunction resets it.
    /// </summary>
    [Fact]
    public void FeatureExtractor_Advance_ClauseState_TracksSubjectVerbAndBoundaries()
    {
        // Arrange: a sentence with a comma and a conjunction
        var tokens = new[] { "the", "cat", ",", "sat", "and", "dog" };
        var workspace = Prepared(tokens, FeatureGroups.ClauseCues);
        var tags = new[] { Upos.Det, Upos.Noun, Upos.Punct, Upos.Verb, Upos.Cconj, Upos.Noun };
        var expectedBefore = new uint[] { 0, 1, 1, 0, 2, 0 };

        // Act / Assert: the state before each token is the one of the tags decided so far
        var previous1 = FeatureExtractor.StartTag1;
        for (var i = 0; i < tokens.Length; i++)
        {
            var buckets = ExtractFrom(workspace, i, previous1);
            Assert.Contains(Code(FeatureKind.ClauseState, expectedBefore[i]), buckets);
            Assert.Contains(Code(FeatureKind.ClauseStatePrev, expectedBefore[i], (uint)previous1), buckets);
            FeatureExtractor.Advance(workspace, i, (int)tags[i]);
            previous1 = (int)tags[i];
        }

        // Assert: starting a pass again clears the state
        FeatureExtractor.StartPass(workspace, false);
        Assert.Contains(Code(FeatureKind.ClauseState, 0), ExtractFrom(workspace, 0));
    }

    /// <summary>
    ///     Proves which tokens are clause boundaries: a comma, a semicolon, a colon, an opening bracket and a dash (also typographic) reset
    ///     the state, a hyphen or a period does not, and a predicted subordinating conjunction resets it too.
    /// </summary>
    /// <param name="token">The token after the noun.</param>
    /// <param name="tag">The tag predicted for it.</param>
    /// <param name="expectedState">The clause state before the next token.</param>
    [Theory]
    [InlineData(",", Upos.Punct, 0u)]
    [InlineData(";", Upos.Punct, 0u)]
    [InlineData(":", Upos.Punct, 0u)]
    [InlineData("(", Upos.Punct, 0u)]
    [InlineData("\u2014", Upos.Punct, 0u)]
    [InlineData("--", Upos.Punct, 0u)]
    [InlineData("-", Upos.Punct, 1u)]
    [InlineData(".", Upos.Punct, 1u)]
    [InlineData("because", Upos.Sconj, 0u)]
    [InlineData("because", Upos.Adp, 1u)]
    public void FeatureExtractor_Advance_ClauseState_BoundaryTokens(string token, Upos tag, uint expectedState)
    {
        // Arrange: a noun followed by the token
        var workspace = Prepared(new[] { "cat", token, "sat" }, FeatureGroups.ClauseCues);

        // Act: record the decisions of the first two tokens
        FeatureExtractor.Advance(workspace, 0, (int)Upos.Noun);
        FeatureExtractor.Advance(workspace, 1, (int)tag);

        // Assert: the state seen by the third token
        Assert.Contains(Code(FeatureKind.ClauseState, expectedState), ExtractFrom(workspace, 2));
    }

    /// <summary>
    ///     Proves the position cues: position capped at three, the first content token, the terminal of the sentence (a final closing
    ///     quote or bracket skipped) and the length bucket.
    /// </summary>
    [Fact]
    public void FeatureExtractor_Extract_PositionCues_FollowTheDefinition()
    {
        // Arrange: function words before the first content word, a question, a quoted statement and a sentence without a terminal
        var content = Prepared(new[] { "the", "of", "valve", "?" }, FeatureGroups.PositionCues);
        var quoted = Prepared(new[] { "she", "sat", "\"", "go", ".", "\"" }, FeatureGroups.PositionCues);
        var bare = Prepared(new[] { "Check", "valve" }, FeatureGroups.PositionCues);
        var onlyFunction = Prepared(new[] { "the", "of" }, FeatureGroups.PositionCues);
        var unknown = Prepared(new[] { "zzyzx", "dog" }, FeatureGroups.PositionCues);

        // Act / Assert: first content token (function-only tokens are skipped; unknown spellings and punctuation not in the lexicon count as content)
        Assert.Equal(2, content.FirstContent);
        Assert.Contains(Code(FeatureKind.FirstContent, 2, 1), ExtractFrom(content, 2));
        Assert.Contains(Code(FeatureKind.FirstContent, 2, 0), ExtractFrom(content, 0));
        Assert.Equal(2, onlyFunction.FirstContent);
        Assert.Contains(Code(FeatureKind.FirstContent, 2, 0), ExtractFrom(onlyFunction, 1));
        Assert.Equal(0, unknown.FirstContent);
        Assert.Contains(Code(FeatureKind.FirstContent, 0, 1), ExtractFrom(unknown, 0));
        Assert.Contains(Code(FeatureKind.FirstContent, 0, 0), ExtractFrom(unknown, 1));

        // Position and terminal: "?" is 2, "." behind a skipped closing quote is 0, none is 4
        Assert.Contains(Code(FeatureKind.PosIdx, 3), ExtractFrom(content, 3));
        Assert.Contains(Code(FeatureKind.PosIdx, 0), ExtractFrom(content, 0));
        Assert.Contains(Code(FeatureKind.EndPos, 2, 1), ExtractFrom(content, 1));
        Assert.Contains(Code(FeatureKind.EndPos, 0, 3), ExtractFrom(quoted, 5));
        Assert.Contains(Code(FeatureKind.EndPos, 4, 0), ExtractFrom(bare, 0));
        Assert.Contains(Code(FeatureKind.LenBucket, 1), ExtractFrom(quoted, 0));
        Assert.Contains(Code(FeatureKind.LenPos, 1, 3), ExtractFrom(quoted, 4));
    }

    /// <summary>
    ///     Proves the length buckets at both ends of every range.
    /// </summary>
    /// <param name="length">The number of tokens.</param>
    /// <param name="bucket">The expected bucket.</param>
    [Theory]
    [InlineData(1, 0)]
    [InlineData(3, 0)]
    [InlineData(4, 1)]
    [InlineData(6, 1)]
    [InlineData(7, 2)]
    [InlineData(10, 2)]
    [InlineData(11, 3)]
    [InlineData(15, 3)]
    [InlineData(16, 4)]
    [InlineData(25, 4)]
    [InlineData(26, 5)]
    [InlineData(60, 5)]
    public void FeatureExtractor_Extract_PositionCues_LengthBuckets(int length, int bucket)
    {
        // Arrange / Act: a sentence of the given length
        var workspace = Prepared(Enumerable.Repeat("dog", length).ToArray(), FeatureGroups.PositionCues);
        var buckets = ExtractFrom(workspace, 0);

        // Assert: the bucket, alone and with the position
        Assert.Contains(Code(FeatureKind.LenBucket, (uint)bucket), buckets);
        Assert.Contains(Code(FeatureKind.LenPos, (uint)bucket, 0), buckets);
    }

    /// <summary>
    ///     Proves the inflection stems, including <c>avoid + ing</c> against <c>br + ing</c> and <c>th + ing</c>, the restored <c>e</c> and the undoubled consonant.
    /// </summary>
    /// <param name="word">The token.</param>
    /// <param name="expected">The expected value: the suffix kind (1 ing, 2 ed, 3 s), plus 4 when a stem is a verb and 8 when a stem is a noun.</param>
    [Theory]
    [InlineData("avoiding", 5)]
    [InlineData("bring", 1)]
    [InlineData("thing", 1)]
    [InlineData("making", 13)]
    [InlineData("running", 13)]
    [InlineData("stopped", 14)]
    [InlineData("tried", 14)]
    [InlineData("tries", 15)]
    [InlineData("boxes", 15)]
    [InlineData("cats", 11)]
    [InlineData("Walks", 15)]
    [InlineData("pass", 3)]
    [InlineData("is", 3)]
    [InlineData("red", 2)]
    [InlineData("ing", 1)]
    [InlineData("cat", 0)]
    [InlineData("the", 0)]
    public void FeatureExtractor_Extract_Inflection_StemFacts(string word, int expected)
    {
        // Arrange / Act: prepare the token with the inflection group
        var workspace = Prepared(new[] { word }, FeatureGroups.Inflection);
        var buckets = ExtractFrom(workspace, 0, (int)Upos.Pron);
        var lower = FeatureExtractor.Fold(word).ToLowerInvariant();

        // Assert: fast path and reference agree; the two features exist exactly for tokens with a suffix
        Assert.Equal(expected, workspace.StemValue[0]);
        Assert.Equal(expected, FeatureExtractor.ReferenceStemValue(lower, GroupLexicon));
        Assert.Equal(expected != 0, buckets.Contains(Code(FeatureKind.StemKind, (uint)expected)));
        Assert.Equal(expected != 0, buckets.Contains(Code(FeatureKind.StemKindPrev, (uint)expected, (uint)Upos.Pron)));
    }

    /// <summary>
    ///     Proves that a word with a verb stem gets other inflection features than words that only look inflected.
    /// </summary>
    [Fact]
    public void FeatureExtractor_Extract_Inflection_SeparatesAvoidingFromBringAndThing()
    {
        // Arrange / Act: extract the three words
        var avoiding = ExtractFrom(Prepared(new[] { "avoiding" }, FeatureGroups.Inflection), 0);
        var bring = ExtractFrom(Prepared(new[] { "bring" }, FeatureGroups.Inflection), 0);
        var thing = ExtractFrom(Prepared(new[] { "thing" }, FeatureGroups.Inflection), 0);

        // Assert: bring and thing share the "inflected, no verb or noun stem" features, avoiding has its own
        Assert.Contains(Code(FeatureKind.StemKind, 5), avoiding);
        Assert.DoesNotContain(Code(FeatureKind.StemKind, 1), avoiding);
        Assert.Contains(Code(FeatureKind.StemKind, 1), bring);
        Assert.Contains(Code(FeatureKind.StemKind, 1), thing);
    }

    /// <summary>
    ///     Proves the preview features: absent in the first pass, present in the second with the first-pass tags of the next two
    ///     tokens and the end markers, and inactive without the group.
    /// </summary>
    [Fact]
    public void FeatureExtractor_Extract_RightPreview_UsesFirstPassTagsOfNextTwoTokens()
    {
        // Arrange: three tokens with first-pass tags
        var tokens = new[] { "the", "dog", "sat" };
        var workspace = Prepared(tokens, FeatureGroups.RightPreview);
        var firstPass = ExtractFrom(workspace, 0);
        workspace.SetPreview(new[] { (int)Upos.Det, (int)Upos.Noun, (int)Upos.Verb });

        // Act: second pass
        FeatureExtractor.StartPass(workspace, true);
        var second = Enumerable.Range(0, 3).Select(i => ExtractFrom(workspace, i, (int)Upos.Adj)).ToList();
        var withoutGroup = Prepared(tokens, FeatureGroups.PositionCues);
        withoutGroup.SetPreview(new[] { 0, 0, 0 });
        FeatureExtractor.StartPass(withoutGroup, true);

        // Assert: the first pass has the base buckets only, the second four more, with the end markers 17 (end) and 18 (beyond)
        Assert.Equal(firstPass, ExtractFrom(Prepared(tokens, FeatureGroups.None), 0));
        Assert.Equal(firstPass.Length + 4, second[0].Length);
        Assert.Contains(Code(FeatureKind.Rt1, (uint)Upos.Noun), second[0]);
        Assert.Contains(Code(FeatureKind.Rt2, (uint)Upos.Verb), second[0]);
        Assert.Contains(Code(FeatureKind.RtPair, (uint)Upos.Noun, (uint)Upos.Verb), second[0]);
        Assert.Contains(Code(FeatureKind.RtPrev, (uint)Upos.Adj, (uint)Upos.Noun), second[0]);
        Assert.Contains(Code(FeatureKind.Rt1, (uint)Upos.Verb), second[1]);
        Assert.Contains(Code(FeatureKind.Rt2, FeatureExtractor.StartTag1), second[1]);
        Assert.Contains(Code(FeatureKind.Rt1, FeatureExtractor.StartTag1), second[2]);
        Assert.Contains(Code(FeatureKind.RtPair, FeatureExtractor.StartTag1, FeatureExtractor.StartTag2), second[2]);
        Assert.Equal(ExtractFrom(Prepared(tokens, FeatureGroups.PositionCues), 0).Length, ExtractFrom(withoutGroup, 0).Length);
        Assert.Throws<ArgumentException>(() => workspace.SetPreview(new[] { 1 }));
    }

    /// <summary>
    ///     Proves that a token with every possible feature has exactly the capacity that the buffers are sized for.
    /// </summary>
    [Fact]
    public void FeatureExtractor_Extract_AllGroups_FillsBucketCapacity()
    {
        // Arrange: the first token has a suffix (inflection), is the initial token and has all four affix lengths
        var all = FeatureGroups.PositionCues | FeatureGroups.ClauseCues | FeatureGroups.RightPreview | FeatureGroups.Inflection;
        var workspace = Prepared(new[] { "running", "the", "valve" }, all);
        workspace.SetPreview(new[] { (int)Upos.Verb, (int)Upos.Det, (int)Upos.Noun });
        FeatureExtractor.StartPass(workspace, true);

        // Act
        var buckets = ExtractFrom(workspace, 0);

        // Assert: 40 buckets, and the base stride grows by the static group features only
        Assert.Equal(FeatureExtractor.BucketCapacity, buckets.Length);
        Assert.Equal(FeatureExtractor.StaticFeatureCount + 5 + 5 + 1, workspace.StaticStride);
        Assert.Equal(FeatureExtractor.StaticFeatureCount, FeatureExtractor.StaticStrideFor(FeatureGroups.None));
        Assert.Equal(FeatureExtractor.StaticFeatureCount, FeatureExtractor.StaticStrideFor(FeatureGroups.RightPreview));
    }

#if NET
    /// <summary>
    ///     Proves that extracting the buckets of all groups, with a stem lookup and both passes, does not allocate once the workspace has grown.
    /// </summary>
    [Fact]
    public void FeatureExtractor_Extract_AllGroups_DoesNotAllocateAfterWarmUp()
    {
        // Arrange: a sentence with inflected words, a workspace configured for all groups and a bucket buffer, warmed up once
        var all = FeatureGroups.PositionCues | FeatureGroups.ClauseCues | FeatureGroups.RightPreview | FeatureGroups.Inflection;
        var tokens = new[] { "Check", "the", "running", "valves", "and", "avoiding", "boxes", "." };
        var preview = new[] { 7, 5, 15, 7, 4, 15, 7, 12 };
        var workspace = new DecoderWorkspace();
        var buckets = new int[FeatureExtractor.BucketCapacity];

        void Run()
        {
            workspace.Configure(all, GroupLexicon);
            workspace.Begin(tokens.Length);
            for (var i = 0; i < tokens.Length; i++)
            {
                FeatureExtractor.SetToken(workspace, i, tokens[i]);
            }

            FeatureExtractor.Finish(workspace, Bits);
            workspace.SetPreview(preview);
            for (var pass = 0; pass < 2; pass++)
            {
                FeatureExtractor.StartPass(workspace, pass == 1);
                for (var i = 0; i < tokens.Length; i++)
                {
                    FeatureExtractor.Extract(workspace, i, 3, 4, Bits, buckets);
                    FeatureExtractor.Advance(workspace, i, preview[i]);
                }
            }
        }

        Run();

        // Act: take the minimum over several batches of the bytes allocated by this thread for 100 rounds
        var allocated = AllocationMeter.MinimumBatchBytes(Run, 100);

        // Assert: nothing was allocated
        Assert.Equal(0, allocated);
    }
#endif

    /// <summary>
    ///     Parses a corpus tag.
    /// </summary>
    private static Upos ParseTag(string text)
    {
        Assert.True(UposExtensions.TryParse(text, out var tag), text);
        return tag;
    }

    /// <summary>
    ///     Gets the history code two positions before a token: the tag there, or a sentinel near the start.
    /// </summary>
    private static int History2(int[] gold, int index)
    {
        if (index >= 2)
        {
            return gold[index - 2];
        }

        return index == 1 ? FeatureExtractor.StartTag1 : FeatureExtractor.StartTag2;
    }
}
