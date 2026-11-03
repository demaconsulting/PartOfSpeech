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
