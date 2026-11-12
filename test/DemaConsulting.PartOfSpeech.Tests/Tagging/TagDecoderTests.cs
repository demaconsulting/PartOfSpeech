// cspell:ignore zorbulon zork unknownword
namespace DemaConsulting.PartOfSpeech.Tests.Tagging;

/// <summary>
///     Unit tests of <see cref="TagDecoder"/>.
/// </summary>
public class TagDecoderTests
{
    /// <summary>
    ///     The tolerance for probabilities (the integer scores are exact, only the exponential differs in the last digits).
    /// </summary>
    private const double Tolerance = 1e-9;

    /// <summary>
    ///     Creates tokens with offsets for words separated by single spaces.
    /// </summary>
    private static Token[] Tokens(params string[] words)
    {
        var tokens = new Token[words.Length];
        var start = 0;
        for (var i = 0; i < words.Length; i++)
        {
            tokens[i] = new Token(words[i], start);
            start += words[i].Length + 1;
        }

        return tokens;
    }

    /// <summary>
    ///     Decodes words with a model built from hand-set weights and a lexicon.
    /// </summary>
    private static TaggedToken[] Decode(
        TinyModels.WeightsBuilder weights,
        byte[] lexicon,
        string[] words,
        TokenConstraint[]? constraints = null,
        int alternatives = 3,
        float temperature = 1f,
        float scale = 1f,
        FeatureGroups groups = FeatureGroups.None) =>
        TinyModels.CreateDecoder(weights.Build(temperature, scale, groups: groups), lexicon).Decode(Tokens(words), constraints, alternatives);

    /// <summary>
    ///     Builds weights where the word cat scores NOUN 3 and VERB 1.
    /// </summary>
    private static TinyModels.WeightsBuilder CatWeights() =>
        new TinyModels.WeightsBuilder().Word("cat", Upos.Noun, 3).Word("cat", Upos.Verb, 1);

    /// <summary>
    ///     Proves that the tags chosen for earlier tokens are the history of later tokens.
    /// </summary>
    [Fact]
    public void TagDecoder_Decode_UsesPreviousTagsAsHistory()
    {
        // Arrange: dog prefers VERB (2 against 1), but after a determiner NOUN gets +5
        var weights = new TinyModels.WeightsBuilder()
            .Word("a", Upos.Det, 10)
            .Word("dog", Upos.Verb, 2)
            .Word("dog", Upos.Noun, 1)
            .AfterTag("DET", Upos.Noun, 5);
        var lexicon = TinyModels.CreateLexicon("a DET", "dog NOUN VERB");

        // Act: tag dog alone and after a
        var alone = Decode(weights, lexicon, new[] { "dog" });
        var afterDeterminer = Decode(weights, lexicon, new[] { "a", "dog" });

        // Assert: the history changes the decision
        Assert.Equal(Upos.Verb, alone[0].Tag);
        Assert.Equal(new[] { Upos.Det, Upos.Noun }, afterDeterminer.Select(t => t.Tag));
    }

    /// <summary>
    ///     Proves that equal scores resolve to the lowest tag index on every platform.
    /// </summary>
    [Fact]
    public void TagDecoder_Decode_TieBreaksToLowestTag()
    {
        // Arrange: no weights at all, one unknown word and one word with NOUN and VERB
        var weights = new TinyModels.WeightsBuilder().Bias(Upos.Adj, 0);
        var lexicon = TinyModels.CreateLexicon("both NOUN VERB");

        // Act: decode
        var result = Decode(weights, lexicon, new[] { "unknownword", "both" });

        // Assert: ADJ (0) for the unknown word with 17 equal candidates, NOUN (7) before VERB (15) with probability one half each
        Assert.Equal(Upos.Adj, result[0].Tag);
        Assert.Equal(1.0 / 17, result[0].Confidence, Tolerance);
        Assert.Equal(Upos.Noun, result[1].Tag);
        Assert.Equal(0.5, result[1].Confidence, Tolerance);
        Assert.Equal(Upos.Verb, Assert.Single(result[1].Alternatives).Tag);
    }

    /// <summary>
    ///     Proves that a known word can only receive its lexicon tags even if the weights prefer another tag.
    /// </summary>
    [Fact]
    public void TagDecoder_Decode_Restricts_ToLexiconTags()
    {
        // Arrange: the weights say VERB with 100, the lexicon says NOUN only
        var weights = new TinyModels.WeightsBuilder().Word("cat", Upos.Verb, 100);
        var lexicon = TinyModels.CreateLexicon("cat NOUN");

        // Act: decode
        var result = Decode(weights, lexicon, new[] { "cat" });

        // Assert: NOUN
        Assert.Equal(Upos.Noun, result[0].Tag);
    }

    /// <summary>
    ///     Proves that an unknown word may receive any tag, also one that no lexicon entry has.
    /// </summary>
    [Fact]
    public void TagDecoder_Decode_UnknownWord_ConsidersAllTags()
    {
        // Arrange: the weights prefer INTJ for a word that the lexicon does not list
        var weights = new TinyModels.WeightsBuilder().Word("zork", Upos.Intj, 4);
        var lexicon = TinyModels.CreateLexicon("cat NOUN");

        // Act: decode with all alternatives
        var result = Decode(weights, lexicon, new[] { "zork" }, alternatives: 16);

        // Assert: INTJ wins and all 16 other tags are alternatives
        Assert.Equal(Upos.Intj, result[0].Tag);
        Assert.Equal(16, result[0].Alternatives.Count);
    }

    /// <summary>
    ///     Proves that the token-shape rules extend the lexicon tags (upper-case first letter, digit, no letter or digit).
    /// </summary>
    [Fact]
    public void TagDecoder_Decode_ShapeRules_ExtendAllowedSet()
    {
        // Arrange: weights that prefer tags which the lexicon does not list, and a lexicon with NOUN-like entries
        var weights = new TinyModels.WeightsBuilder()
            .Word("paris", Upos.Propn, 100)
            .Word("b2b", Upos.Num, 100)
            .Word("~", Upos.X, 100);
        var lexicon = TinyModels.CreateLexicon("paris NOUN", "b2b NOUN", "~ SYM");

        // Act: decode capitalized, lower-case, digit and symbol forms
        var result = Decode(weights, lexicon, new[] { "Paris", "paris", "b2b", "~" });

        // Assert: PROPN only with the capital, NUM through the digit rule, X through the no-letter rule
        Assert.Equal(new[] { Upos.Propn, Upos.Noun, Upos.Num, Upos.X }, result.Select(t => t.Tag));
    }

    /// <summary>
    ///     Proves for a sample of real lexicon spellings and shape variants that the decoder allows exactly the tags that the data rule allows.
    /// </summary>
    [Fact]
    public void TagDecoder_Decode_AllowedSet_MatchesDataFilesRules()
    {
        // Arrange: a model without weights (every allowed tag ties), the embedded lexicon and sample tokens
        var reference = DataFiles.ReadLexicon(DataFiles.Data("lexicon/lexicon.tsv"));
        var decoder = TinyModels.CreateDecoder(new TinyModels.WeightsBuilder().Build(), File.ReadAllBytes(DataFiles.Repository("src/DemaConsulting.PartOfSpeech/Resources/lexicon.bin")));
        var samples = reference.Keys.Where((_, i) => i % 97 == 0).ToList();
        var variants = samples.Concat(samples.Select(s => char.ToUpperInvariant(s[0]) + s.Substring(1))).Concat(samples.Select(s => s + "9")).ToList();
        variants.AddRange(new[] { "Zorbulon", "zorbulon", "!!", "A1", "3.5", "\u201CHello\u201D", "don\u2019t" });
        var mismatches = new List<string>();

        // Act: decode them as one long sentence and read the allowed set (best tag plus alternatives)
        var result = decoder.Decode(Tokens(variants.ToArray()), null, 16);

        // Assert: the allowed set equals the reference rule (the reference sees the folded token like the decoder does)
        for (var i = 0; i < variants.Count; i++)
        {
            var allowed = UposSet.Of(result[i].Tag).Union(UposSet.Of(result[i].Alternatives.Select(a => a.Tag).ToArray()));
            var token = FeatureExtractor.Fold(variants[i]);
            foreach (var tag in Enum.GetValues(typeof(Upos)).Cast<Upos>())
            {
                if (allowed.Contains(tag) != DataFiles.IsTagAllowed(token, tag.ToTagString(), reference))
                {
                    mismatches.Add($"{variants[i]} {tag}");
                }
            }
        }

        Assert.Empty(mismatches.Take(10));
    }

    /// <summary>
    ///     Proves that the confidence is the softmax of the real scores at the model temperature, with analytic values.
    /// </summary>
    [Fact]
    public void TagDecoder_Decode_Confidence_IsSoftmaxAtTemperature()
    {
        // Arrange: cat scores NOUN 3 and VERB 1; scale 0.5 halves them; temperature 2 halves them again
        var lexicon = TinyModels.CreateLexicon("cat NOUN VERB");

        // Act: decode with temperature 1, with temperature 2 and with scale 0.5 and temperature 0.5
        var plain = Decode(CatWeights(), lexicon, new[] { "cat" });
        var hot = Decode(CatWeights(), lexicon, new[] { "cat" }, temperature: 2f);
        var scaled = Decode(CatWeights(), lexicon, new[] { "cat" }, temperature: 0.5f, scale: 0.5f);

        // Assert: p(NOUN) = 1 / (1 + exp(-(3 - 1) * scale / temperature)), and the alternative gets the rest
        Assert.Equal(1 / (1 + Math.Exp(-2)), plain[0].Confidence, Tolerance);
        Assert.Equal(1 / (1 + Math.Exp(-1)), hot[0].Confidence, Tolerance);
        Assert.Equal(1 / (1 + Math.Exp(-2)), scaled[0].Confidence, Tolerance);
        Assert.Equal(1 - plain[0].Confidence, plain[0].Alternatives[0].Probability, Tolerance);
    }

    /// <summary>
    ///     Proves that a lower temperature sharpens and a higher temperature flattens the distribution without changing the tag.
    /// </summary>
    [Fact]
    public void TagDecoder_Decode_Temperature_SharpensAndFlattens()
    {
        // Arrange: the same weights at three temperatures
        var lexicon = TinyModels.CreateLexicon("cat NOUN VERB");

        // Act: decode
        var sharp = Decode(CatWeights(), lexicon, new[] { "cat" }, temperature: 0.5f)[0];
        var normal = Decode(CatWeights(), lexicon, new[] { "cat" }, temperature: 1f)[0];
        var flat = Decode(CatWeights(), lexicon, new[] { "cat" }, temperature: 4f)[0];

        // Assert: same tag, confidence decreasing with temperature, never below one half
        Assert.All(new[] { sharp, normal, flat }, t => Assert.Equal(Upos.Noun, t.Tag));
        Assert.True(sharp.Confidence > normal.Confidence);
        Assert.True(normal.Confidence > flat.Confidence);
        Assert.True(flat.Confidence > 0.5);
    }

    /// <summary>
    ///     Proves that alternatives are sorted by probability, limited to the requested number and that all probabilities add up to one.
    /// </summary>
    [Fact]
    public void TagDecoder_Decode_Alternatives_AreSortedAndLimited()
    {
        // Arrange: an unknown word with distinct scores for five tags
        var weights = new TinyModels.WeightsBuilder()
            .Word("w", Upos.Noun, 5).Word("w", Upos.Verb, 4).Word("w", Upos.Adj, 3).Word("w", Upos.Adv, 2).Word("w", Upos.Det, 1);
        var lexicon = TinyModels.CreateLexicon("cat NOUN");

        // Act: decode with three, sixteen and zero alternatives
        var three = Decode(weights, lexicon, new[] { "w" }, alternatives: 3)[0];
        var all = Decode(weights, lexicon, new[] { "w" }, alternatives: 16)[0];
        var none = Decode(weights, lexicon, new[] { "w" }, alternatives: 0)[0];

        // Assert: the order VERB, ADJ, ADV; sorted descending; total probability one; no alternatives when none were requested
        Assert.Equal(new[] { Upos.Verb, Upos.Adj, Upos.Adv }, three.Alternatives.Select(a => a.Tag));
        Assert.Equal(16, all.Alternatives.Count);
        Assert.Equal(all.Alternatives.OrderByDescending(a => a.Probability).Select(a => a.Tag), all.Alternatives.Select(a => a.Tag));
        Assert.Equal(1.0, all.Confidence + all.Alternatives.Sum(a => a.Probability), Tolerance);
        Assert.DoesNotContain(all.Alternatives, a => a.Tag == all.Tag);
        Assert.Empty(none.Alternatives);
    }

    /// <summary>
    ///     Proves that a token with exactly one allowed tag has confidence one and no alternatives.
    /// </summary>
    [Fact]
    public void TagDecoder_Decode_SingleAllowedTag_HasConfidenceOne()
    {
        // Arrange: the lexicon lists the only DET
        var lexicon = TinyModels.CreateLexicon("the DET");

        // Act: decode
        var result = Decode(new TinyModels.WeightsBuilder().Word("the", Upos.Noun, 50), lexicon, new[] { "the" });

        // Assert: DET with probability exactly one
        Assert.Equal(Upos.Det, result[0].Tag);
        Assert.Equal(1.0, result[0].Confidence);
        Assert.Empty(result[0].Alternatives);
        Assert.False(result[0].IsConstrained);
    }

    /// <summary>
    ///     Proves that an allowed-set constraint intersects with the lexicon tags.
    /// </summary>
    [Fact]
    public void TagDecoder_Decode_Constraint_Only_Intersects()
    {
        // Arrange: cat prefers NOUN, lexicon NOUN and VERB
        var lexicon = TinyModels.CreateLexicon("cat NOUN VERB");

        // Act: restrict to VERB, and to a set that contains NOUN, VERB and ADJ
        var verb = Decode(CatWeights(), lexicon, new[] { "cat" }, new[] { TokenConstraint.Only(Upos.Verb) })[0];
        var wide = Decode(CatWeights(), lexicon, new[] { "cat" }, new[] { TokenConstraint.Only(Upos.Noun, Upos.Verb, Upos.Adj) })[0];

        // Assert: VERB with probability one; for the wide set the lexicon still excludes ADJ
        Assert.Equal(Upos.Verb, verb.Tag);
        Assert.Equal(1.0, verb.Confidence);
        Assert.True(verb.IsConstrained);
        Assert.Equal(Upos.Noun, wide.Tag);
        Assert.Equal(Upos.Verb, Assert.Single(wide.Alternatives).Tag);
        Assert.True(wide.IsConstrained);
    }

    /// <summary>
    ///     Proves that a constraint that shares no tag with the lexicon wins over the lexicon.
    /// </summary>
    [Fact]
    public void TagDecoder_Decode_Constraint_DisjointFromLexicon_ConstraintWins()
    {
        // Arrange: the lexicon lists NOUN only
        var lexicon = TinyModels.CreateLexicon("cat NOUN");

        // Act: constrain to ADJ
        var result = Decode(CatWeights(), lexicon, new[] { "cat" }, new[] { TokenConstraint.Only(Upos.Adj) })[0];

        // Assert: ADJ, certain
        Assert.Equal(Upos.Adj, result.Tag);
        Assert.Equal(1.0, result.Confidence);
    }

    /// <summary>
    ///     Proves that soft constraints shift scores in raw units: a boost flips a close decision and a penalty does too.
    /// </summary>
    [Fact]
    public void TagDecoder_Decode_Constraint_Boost_And_Penalty()
    {
        // Arrange: cat scores NOUN 3, VERB 1
        var lexicon = TinyModels.CreateLexicon("cat NOUN VERB");

        // Act: boost VERB by 5, penalize NOUN by 5, and boost VERB by 1 (not enough to flip)
        var boosted = Decode(CatWeights(), lexicon, new[] { "cat" }, new[] { TokenConstraint.Prefer(Upos.Verb, 5) })[0];
        var penalized = Decode(CatWeights(), lexicon, new[] { "cat" }, new[] { TokenConstraint.Penalize(Upos.Noun, 5) })[0];
        var small = Decode(CatWeights(), lexicon, new[] { "cat" }, new[] { TokenConstraint.Prefer(Upos.Verb, 1) })[0];

        // Assert: flipped twice, and the small boost changes the probability analytically (scores 3 and 2)
        Assert.Equal(Upos.Verb, boosted.Tag);
        Assert.Equal(1 / (1 + Math.Exp(-(6 - 3))), boosted.Confidence, Tolerance);
        Assert.Equal(Upos.Verb, penalized.Tag);
        Assert.Equal(Upos.Noun, small.Tag);
        Assert.Equal(1 / (1 + Math.Exp(-(3 - 2))), small.Confidence, Tolerance);
        Assert.True(small.IsConstrained);
    }

    /// <summary>
    ///     Proves that the tag chosen under a constraint is the history of the next token.
    /// </summary>
    [Fact]
    public void TagDecoder_Decode_Constraint_PropagatesToNextToken()
    {
        // Arrange: a is DET (2) or ADJ (1); after DET the next word is NOUN, after ADJ it is VERB
        var weights = new TinyModels.WeightsBuilder()
            .Word("a", Upos.Det, 2).Word("a", Upos.Adj, 1)
            .AfterTag("DET", Upos.Noun, 5).AfterTag("ADJ", Upos.Verb, 5);
        var lexicon = TinyModels.CreateLexicon("a DET ADJ", "dog NOUN VERB");

        // Act: tag without and with a constraint on the first token
        var free = Decode(weights, lexicon, new[] { "a", "dog" });
        var constrained = Decode(weights, lexicon, new[] { "a", "dog" }, new[] { TokenConstraint.Only(Upos.Adj), TokenConstraint.None });

        // Assert: the constraint changes the second tag
        Assert.Equal(new[] { Upos.Det, Upos.Noun }, free.Select(t => t.Tag));
        Assert.Equal(new[] { Upos.Adj, Upos.Verb }, constrained.Select(t => t.Tag));
        Assert.False(constrained[1].IsConstrained);
    }

    /// <summary>
    ///     Proves that an empty sentence gives an empty result and that invalid arguments are rejected.
    /// </summary>
    [Fact]
    public void TagDecoder_Decode_EmptySentence_ReturnsEmpty()
    {
        // Arrange: a decoder
        var decoder = TinyModels.CreateDecoder(CatWeights().Build(), TinyModels.CreateLexicon("cat NOUN"));

        // Act / Assert: empty input, and invalid arguments
        Assert.Empty(decoder.Decode(new Token[0], null, 3));
        Assert.Throws<ArgumentNullException>(() => decoder.Decode(null!, null, 3));
        Assert.Throws<ArgumentException>(() => decoder.Decode(Tokens("cat"), new TokenConstraint[2], 3));
    }

    /// <summary>
    ///     Proves that a caller-supplied constraint list is read before the decoder takes its per-thread workspace, so a list
    ///     that tags other text from its indexer (a nested call on the same thread) cannot corrupt the outer result.
    /// </summary>
    [Fact]
    public void TagDecoder_Decode_ReentrantConstraintList_DoesNotCorruptResult()
    {
        // Arrange: a is DET (2) or ADJ (1); after DET the next word is NOUN, after ADJ it is VERB. No token is constrained, so
        // the outer tags depend only on the words in the workspace, which a nested decode would overwrite if it ran too early.
        var weights = new TinyModels.WeightsBuilder()
            .Word("a", Upos.Det, 2).Word("a", Upos.Adj, 1)
            .AfterTag("DET", Upos.Noun, 5).AfterTag("ADJ", Upos.Verb, 5);
        var decoder = TinyModels.CreateDecoder(weights.Build(), TinyModels.CreateLexicon("a DET ADJ", "dog NOUN VERB"));
        var tokens = Tokens("a", "dog");
        var constraints = new[] { TokenConstraint.None, TokenConstraint.None };
        var expected = decoder.Decode(tokens, constraints, 3);
        var nestedTokens = Tokens("dog", "dog", "dog", "a", "dog", "a");
        var nestedExpected = decoder.Decode(nestedTokens, null, 3);

        // Act: decode with a list whose indexer decodes a longer, different sentence on the same thread
        var list = new ReentrantConstraintList(constraints, () => decoder.Decode(nestedTokens, null, 3));
        var actual = decoder.Decode(tokens, list, 3);

        // Assert: the nested call ran, produced its own correct result, and the outer result is that of a plain array
        Assert.True(list.NestedResults.Count > 0);
        Assert.All(list.NestedResults, nested => Assert.Equal(nestedExpected.Select(t => t.Tag), nested.Select(t => t.Tag)));
        Assert.Equal(new[] { Upos.Det, Upos.Noun }, actual.Select(t => t.Tag));
        Assert.Equal(expected.Select(t => t.Tag), actual.Select(t => t.Tag));
        Assert.Equal(expected.Select(t => t.Confidence), actual.Select(t => t.Confidence));
        Assert.Equal(expected.Select(t => t.IsConstrained), actual.Select(t => t.IsConstrained));
    }

    /// <summary>
    ///     A constraint list whose indexer runs caller code that uses the decoder again on the same thread.
    /// </summary>
    private sealed class ReentrantConstraintList : IReadOnlyList<TokenConstraint>
    {
        private readonly TokenConstraint[] _items;
        private readonly Func<TaggedToken[]> _nested;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ReentrantConstraintList"/> class.
        /// </summary>
        /// <param name="items">The constraints returned by the indexer.</param>
        /// <param name="nested">The caller code that runs on every read of the indexer.</param>
        public ReentrantConstraintList(TokenConstraint[] items, Func<TaggedToken[]> nested)
        {
            _items = items;
            _nested = nested;
        }

        /// <summary>
        ///     Gets the results of the nested calls.
        /// </summary>
        public List<TaggedToken[]> NestedResults { get; } = new();

        /// <inheritdoc/>
        public int Count => _items.Length;

        /// <inheritdoc/>
        public TokenConstraint this[int index]
        {
            get
            {
                NestedResults.Add(_nested());
                return _items[index];
            }
        }

        /// <inheritdoc/>
        public IEnumerator<TokenConstraint> GetEnumerator() => ((IEnumerable<TokenConstraint>)_items).GetEnumerator();

        /// <inheritdoc/>
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    ///     Proves that a token reports whether the lexicon has an entry for its spelling.
    /// </summary>
    [Fact]
    public void TagDecoder_Decode_KnownWord_ReportsLexiconMembership()
    {
        // Arrange: a lexicon with a word of one tag and a word of two tags
        var lexicon = TinyModels.CreateLexicon("cat NOUN", "run NOUN VERB");

        // Act: decode a single-tag word, a multi-tag word and a word that the lexicon does not list
        var result = Decode(CatWeights(), lexicon, new[] { "cat", "run", "zork" });

        // Assert: the listed words are known, the absent one is not
        Assert.True(result[0].IsKnownWord);
        Assert.True(result[1].IsKnownWord);
        Assert.False(result[2].IsKnownWord);
    }

    /// <summary>
    ///     Proves that the lookup of the known-word flag ignores letter case and folds typographic characters.
    /// </summary>
    /// <param name="spelling">The token text.</param>
    /// <param name="expected">The expected flag.</param>
    [Theory]
    [InlineData("cat", true)]
    [InlineData("Cat", true)]
    [InlineData("CAT", true)]
    [InlineData("cAt", true)]
    [InlineData("don't", true)]
    [InlineData("don\u2019t", true)]
    [InlineData("zork", false)]
    [InlineData("Zork", false)]
    public void TagDecoder_Decode_KnownWord_IgnoresLetterCase(string spelling, bool expected)
    {
        // Arrange: a lexicon that lists cat and don't in lower case
        var lexicon = TinyModels.CreateLexicon("cat NOUN", "don't AUX");

        // Act: decode the spelling alone
        var result = Decode(CatWeights(), lexicon, new[] { spelling });

        // Assert: the flag follows the lower-cased, folded lookup
        Assert.Equal(expected, result[0].IsKnownWord);
    }

    /// <summary>
    ///     Proves that the token-shape rules never make a word known: an unlisted capitalized word, number or punctuation
    ///     mark is unknown and keeps all 17 candidates.
    /// </summary>
    [Fact]
    public void TagDecoder_Decode_KnownWord_ShapeRulesDoNotMakeAWordKnown()
    {
        // Arrange: a lexicon that lists paris, b2b and ~ but not the other tokens
        var lexicon = TinyModels.CreateLexicon("paris NOUN", "b2b NOUN", "~ SYM");

        // Act: decode unlisted tokens of each shape and the listed ones, with all alternatives
        var result = Decode(CatWeights(), lexicon, new[] { "Zorbulon", "42", "!!", "Paris", "b2b", "~" }, alternatives: 16);

        // Assert: the unlisted tokens are unknown with 17 candidates (the best tag and 16 alternatives), the listed ones known
        for (var i = 0; i < 3; i++)
        {
            Assert.False(result[i].IsKnownWord, result[i].Text);
            Assert.Equal(16, result[i].Alternatives.Count);
        }

        for (var i = 3; i < 6; i++)
        {
            Assert.True(result[i].IsKnownWord, result[i].Text);
        }
    }

    /// <summary>
    ///     Proves that the known-word flag is a lexicon fact that a constraint does not change.
    /// </summary>
    [Fact]
    public void TagDecoder_Decode_KnownWord_IsIndependentOfConstraints()
    {
        // Arrange: a lexicon that lists cat as a noun only
        var lexicon = TinyModels.CreateLexicon("cat NOUN");
        var constraints = new[]
        {
            TokenConstraint.Only(Upos.Noun),
            TokenConstraint.Only(Upos.Adj),
            TokenConstraint.Prefer(Upos.Noun, 2),
            TokenConstraint.None
        };

        // Act: decode an unlisted word with a hard constraint, a listed word with a disjoint hard constraint, an unlisted word with a soft one and a plain unlisted word
        var result = Decode(CatWeights(), lexicon, new[] { "zork", "cat", "zork", "zork" }, constraints);

        // Assert: the flag follows the lexicon in every case
        Assert.False(result[0].IsKnownWord);
        Assert.True(result[0].IsConstrained);
        Assert.Equal(1.0, result[0].Confidence);
        Assert.True(result[1].IsKnownWord);
        Assert.Equal(Upos.Adj, result[1].Tag);
        Assert.False(result[2].IsKnownWord);
        Assert.False(result[3].IsKnownWord);
        Assert.False(result[3].IsConstrained);
    }

    /// <summary>
    ///     Proves that a model with all feature groups (cached lexicon facts, two passes) reports the same flags as a model without groups.
    /// </summary>
    [Fact]
    public void TagDecoder_Decode_KnownWord_AllGroups_EqualsPlainDecode()
    {
        // Arrange: a lexicon and a sentence with listed, differently cased and unlisted words
        var lexicon = TinyModels.CreateLexicon("cat NOUN VERB", "the DET", "sat VERB");
        var words = new[] { "The", "cat", "Cat", "zork", "sat", "." };
        const FeatureGroups groups = FeatureGroups.PositionCues | FeatureGroups.ClauseCues | FeatureGroups.RightPreview | FeatureGroups.Inflection | FeatureGroups.TitleCaseLine;

        // Act: decode with and without groups
        var plain = Decode(CatWeights(), lexicon, words);
        var grouped = Decode(CatWeights(), lexicon, words, groups: groups);

        // Assert: both report the lexicon facts of the spellings
        var expected = new[] { true, true, true, false, true, false };
        Assert.Equal(expected, plain.Select(t => t.IsKnownWord));
        Assert.Equal(expected, grouped.Select(t => t.IsKnownWord));
    }

#if NET
    /// <summary>
    ///     Proves that the result token keeps its size, which the allocation budget of the decoder counts on.
    /// </summary>
    [Fact]
    public void TagDecoder_Decode_ResultToken_KeepsItsSize()
    {
        // Arrange: the budget of the allocation tests counts 64 bytes per token, which holds for a struct of 48 bytes on 64-bit

        // Act: measure the real type
        var size = System.Runtime.CompilerServices.Unsafe.SizeOf<TaggedToken>();

        // Assert: it is at most 48 bytes on a 64-bit process
        if (IntPtr.Size == 8)
        {
            Assert.True(size <= 48, $"TaggedToken is {size} bytes");
        }
    }

    /// <summary>
    ///     Proves that decoding allocates only its result arrays: the bytes allocated per sentence are bounded by the size of the results.
    /// </summary>
    [Fact]
    public void TagDecoder_Decode_DoesNotAllocateInHotPath()
    {
        // Arrange: a decoder with the cat model and the real lexicon, and a 50-token ASCII sentence
        var decoder = TinyModels.CreateDecoder(CatWeights().Build(), File.ReadAllBytes(DataFiles.Repository("src/DemaConsulting.PartOfSpeech/Resources/lexicon.bin")));
        var tokens = Tokens(Enumerable.Range(0, 50).Select(i => i % 2 == 0 ? "cat" : "Running").ToArray());

        // Act: warm up thoroughly, then take the minimum over several batches of the bytes allocated by this thread for 100 decodes
        var perCall = AllocationMeter.MinimumBatchBytes(() => decoder.Decode(tokens, null, 3), 100) / 100;

        // Assert: a result array of 50 tokens (about 56 bytes each) and 150 alternatives (16 bytes each) plus headers, nothing per token otherwise
        var bound = (50 * 64) + (150 * 16) + 256;
        Assert.True(perCall <= bound, $"{perCall} bytes per call, bound {bound}");
    }

    /// <summary>
    ///     The mask of all groups.
    /// </summary>
    private const FeatureGroups AllGroups = FeatureGroups.PositionCues | FeatureGroups.ClauseCues | FeatureGroups.RightPreview | FeatureGroups.Inflection | FeatureGroups.TitleCaseLine;

    /// <summary>
    ///     Proves that decoding with all groups on (both passes, stem lookups) allocates only its result arrays.
    /// </summary>
    [Fact]
    public void TagDecoder_Decode_AllGroups_DoesNotAllocateInHotPath()
    {
        // Arrange: a decoder whose weights say all groups, the real lexicon and a 50-token ASCII sentence
        var weights = CatWeights().Build(groups: AllGroups);
        var decoder = TinyModels.CreateDecoder(weights, File.ReadAllBytes(DataFiles.Repository("src/DemaConsulting.PartOfSpeech/Resources/lexicon.bin")));
        var tokens = Tokens(Enumerable.Range(0, 50).Select(i => i % 2 == 0 ? "cat" : "Running").ToArray());

        // Act: warm up thoroughly, then take the minimum over several batches of the bytes allocated by this thread for 100 decodes
        var perCall = AllocationMeter.MinimumBatchBytes(() => decoder.Decode(tokens, null, 3), 100) / 100;

        // Assert: the same bound as without groups
        var bound = (50 * 64) + (150 * 16) + 256;
        Assert.True(perCall <= bound, $"{perCall} bytes per call, bound {bound}");
    }
#endif

    /// <summary>
    ///     Proves that a model with the clause cues turns an imperative around: the ambiguous first word before a determiner becomes a
    ///     verb, and the same weights without the group in the header do nothing.
    /// </summary>
    [Fact]
    public void TagDecoder_Decode_ClauseCues_ImperativeBeforeDeterminer()
    {
        // Arrange: check prefers NOUN, but a determiner as the next token adds 5 to VERB
        var lexicon = TinyModels.CreateLexicon("check NOUN VERB", "the DET", "valve NOUN");
        TinyModels.WeightsBuilder Weights() => new TinyModels.WeightsBuilder()
            .Word("check", Upos.Noun, 3)
            .Word("check", Upos.Verb, 1)
            .AddCodes(FeatureKind.NextClass1, (uint)TokenClass.Det, 0, Upos.Verb, 5);

        // Act: decode with and without the group in the header, and the word alone
        var withGroup = Decode(Weights(), lexicon, new[] { "check", "the", "valve" }, groups: FeatureGroups.ClauseCues);
        var withoutGroup = Decode(Weights(), lexicon, new[] { "check", "the", "valve" });
        var alone = Decode(Weights(), lexicon, new[] { "check" }, groups: FeatureGroups.ClauseCues);

        // Assert: the next-class feature acts only when the header names the group
        Assert.Equal(Upos.Verb, withGroup[0].Tag);
        Assert.Equal(Upos.Noun, withoutGroup[0].Tag);
        Assert.Equal(Upos.Noun, alone[0].Tag);
    }

    /// <summary>
    ///     Proves that the clause state of the decoder follows the tags that it chose itself: a verb chosen for the first
    ///     token changes the decision of the second.
    /// </summary>
    [Fact]
    public void TagDecoder_Decode_ClauseCues_ClauseStateFollowsChosenTags()
    {
        // Arrange: run is NOUN or VERB; a clause that already has a verb (state 2) pushes the next ambiguous word to NOUN
        var lexicon = TinyModels.CreateLexicon("run NOUN VERB", "dog NOUN VERB");
        var weights = new TinyModels.WeightsBuilder()
            .Word("run", Upos.Verb, 4)
            .Word("dog", Upos.Verb, 2)
            .Word("dog", Upos.Noun, 1)
            .AddCodes(FeatureKind.ClauseState, 2, 0, Upos.Noun, 5);

        // Act: after run (VERB) the state is 2; alone dog has no verb before it
        var afterVerb = Decode(weights, lexicon, new[] { "run", "dog" }, groups: FeatureGroups.ClauseCues);
        var alone = Decode(weights, lexicon, new[] { "dog" }, groups: FeatureGroups.ClauseCues);

        // Assert: the history feature acted
        Assert.Equal(new[] { Upos.Verb, Upos.Noun }, afterVerb.Select(t => t.Tag));
        Assert.Equal(Upos.Verb, alone[0].Tag);
    }

    /// <summary>
    ///     Proves that the position cues reach the decoder: the first position favors a verb, the second does not.
    /// </summary>
    [Fact]
    public void TagDecoder_Decode_PositionCues_UsePositionInSentence()
    {
        // Arrange: dog prefers NOUN, the first position adds 5 to VERB
        var lexicon = TinyModels.CreateLexicon("dog NOUN VERB");
        var weights = new TinyModels.WeightsBuilder()
            .Word("dog", Upos.Noun, 2)
            .Word("dog", Upos.Verb, 1)
            .AddCodes(FeatureKind.PosIdx, 0, 0, Upos.Verb, 5);

        // Act: decode two dogs
        var result = Decode(weights, lexicon, new[] { "dog", "dog" }, groups: FeatureGroups.PositionCues);

        // Assert: VERB at position 0, NOUN at position 1
        Assert.Equal(new[] { Upos.Verb, Upos.Noun }, result.Select(t => t.Tag));
    }

    /// <summary>
    ///     Proves that the title-case line group reaches the decoder: the first word of a Title Case line is tagged by the line feature,
    ///     the same words as an ordinary sentence and the same weights without the group in the header are not.
    /// </summary>
    [Fact]
    public void TagDecoder_Decode_TitleCaseLine_TitleLineChangesTheDecision()
    {
        // Arrange: check prefers NOUN, but on a Title Case line the first token adds 5 to VERB
        var lexicon = TinyModels.CreateLexicon("check NOUN VERB", "fluid NOUN", "level NOUN");
        TinyModels.WeightsBuilder Weights() => new TinyModels.WeightsBuilder()
            .Word("check", Upos.Noun, 3)
            .Word("check", Upos.Verb, 1)
            .AddCodes(FeatureKind.TitleLineFirst, 2, 0, Upos.Verb, 5);

        // Act: a Title Case line, the same words in lower case (an ordinary line), a line that ends as a sentence, and the line without the group
        var title = Decode(Weights(), lexicon, new[] { "Check", "Fluid", "Level" }, groups: FeatureGroups.TitleCaseLine);
        var lower = Decode(Weights(), lexicon, new[] { "Check", "fluid", "level" }, groups: FeatureGroups.TitleCaseLine);
        var sentence = Decode(Weights(), lexicon, new[] { "Check", "Fluid", "Level", "." }, groups: FeatureGroups.TitleCaseLine);
        var withoutGroup = Decode(Weights(), lexicon, new[] { "Check", "Fluid", "Level" });

        // Assert: only the Title Case line with the group in the header turns the first word into a verb
        Assert.Equal(Upos.Verb, title[0].Tag);
        Assert.Equal(Upos.Noun, lower[0].Tag);
        Assert.Equal(Upos.Noun, sentence[0].Tag);
        Assert.Equal(Upos.Noun, withoutGroup[0].Tag);
    }

    /// <summary>
    ///     Proves that the inflection group tells a word with a verb stem from a word that only ends in <c>ing</c>.
    /// </summary>
    [Fact]
    public void TagDecoder_Decode_Inflection_SeparatesVerbStemFromLookalike()
    {
        // Arrange: both words prefer NOUN; a verb stem with the ing suffix (value 5) adds 5 to VERB
        var lexicon = TinyModels.CreateLexicon("avoiding NOUN VERB", "avoid VERB", "thing NOUN VERB");
        var weights = new TinyModels.WeightsBuilder()
            .Word("avoiding", Upos.Noun, 2)
            .Word("avoiding", Upos.Verb, 1)
            .Word("thing", Upos.Noun, 2)
            .Word("thing", Upos.Verb, 1)
            .AddCodes(FeatureKind.StemKind, 5, 0, Upos.Verb, 5);

        // Act: decode both words
        var result = Decode(weights, lexicon, new[] { "avoiding", "thing" }, groups: FeatureGroups.Inflection);

        // Assert: VERB for the word with the verb stem, NOUN for the lookalike
        Assert.Equal(new[] { Upos.Verb, Upos.Noun }, result.Select(t => t.Tag));
    }

    /// <summary>
    ///     Proves the two-pass decode of the right preview group: the second pass sees the first-pass tags of the next tokens,
    ///     a token at the end of the sentence sees the end marker, and constraints act in both passes.
    /// </summary>
    [Fact]
    public void TagDecoder_Decode_RightPreview_SecondPassSeesFirstPassTags()
    {
        // Arrange: in the first pass dog is VERB (2 against 1); a NOUN ahead (first pass) adds 5 to NOUN in the second pass
        var lexicon = TinyModels.CreateLexicon("dog NOUN VERB", "bone NOUN VERB");
        TinyModels.WeightsBuilder Weights() => new TinyModels.WeightsBuilder()
            .Word("dog", Upos.Verb, 2)
            .Word("dog", Upos.Noun, 1)
            .Word("bone", Upos.Noun, 5)
            .AddCodes(FeatureKind.Rt1, (uint)Upos.Noun, 0, Upos.Noun, 5);

        // Act: two tokens, one token (end marker), the second token forced to VERB (first pass sees the constraint), and no group
        var pair = Decode(Weights(), lexicon, new[] { "dog", "bone" }, groups: FeatureGroups.RightPreview);
        var single = Decode(Weights(), lexicon, new[] { "dog" }, groups: FeatureGroups.RightPreview);
        var forced = Decode(Weights(), lexicon, new[] { "dog", "bone" }, new[] { TokenConstraint.None, TokenConstraint.Only(Upos.Verb) }, groups: FeatureGroups.RightPreview);
        var noGroup = Decode(Weights(), lexicon, new[] { "dog", "bone" });

        // Assert
        Assert.Equal(new[] { Upos.Noun, Upos.Noun }, pair.Select(t => t.Tag));
        Assert.Equal(Upos.Verb, single[0].Tag);
        Assert.Equal(new[] { Upos.Verb, Upos.Verb }, forced.Select(t => t.Tag));
        Assert.Equal(new[] { Upos.Verb, Upos.Noun }, noGroup.Select(t => t.Tag));
    }

    /// <summary>
    ///     Proves that a right-preview model whose weights have no preview rows decodes exactly like the same weights without the group:
    ///     the first pass equals the one-pass decode, so the second pass changes nothing.
    /// </summary>
    [Fact]
    public void TagDecoder_Decode_RightPreview_WithoutPreviewWeights_EqualsOnePass()
    {
        // Arrange: weights with base features only, written once with and once without the group
        var lexicon = TinyModels.CreateLexicon("a DET", "dog NOUN VERB", "cat NOUN", "runs VERB");
        TinyModels.WeightsBuilder Weights() => new TinyModels.WeightsBuilder()
            .Word("a", Upos.Det, 10)
            .Word("dog", Upos.Verb, 2)
            .Word("dog", Upos.Noun, 1)
            .Word("cat", Upos.Noun, 3)
            .AfterTag("DET", Upos.Noun, 5);
        var sentences = new[] { new[] { "a", "dog" }, new[] { "dog", "runs" }, new[] { "a", "cat", "runs" }, new[] { "dog" } };

        // Act / Assert: tags, confidences and alternatives are equal
        foreach (var words in sentences)
        {
            var onePass = Decode(Weights(), lexicon, words, temperature: 3f);
            var twoPass = Decode(Weights(), lexicon, words, temperature: 3f, groups: FeatureGroups.RightPreview);
            Assert.Equal(onePass.Select(t => t.Tag), twoPass.Select(t => t.Tag));
            Assert.Equal(onePass.Select(t => t.Confidence), twoPass.Select(t => t.Confidence));
            Assert.Equal(
                onePass.Select(t => t.Alternatives.Select(a => (a.Tag, a.Probability)).ToList()),
                twoPass.Select(t => t.Alternatives.Select(a => (a.Tag, a.Probability)).ToList()));
        }
    }

    /// <summary>
    ///     Proves that for every set of groups the decoder chooses the tags that a plain reference decoder chooses with the string-based
    ///     reference features, on a toy model trained with that set of groups (the preview group in two passes).
    /// </summary>
    /// <param name="mask">The groups as a mask.</param>
    [Theory]
    [MemberData(nameof(FeatureExtractorTests.GroupMasks), MemberType = typeof(FeatureExtractorTests))]
    public void TagDecoder_Decode_MatchesReferenceDecoder(uint mask)
    {
        // Arrange: a toy model trained with the groups, its lexicon, and fixture sentences plus a few new ones
        var groups = (FeatureGroups)mask;
        var model = PerceptronModel.Load(TinyTrainer.Train(epochs: 5, groups: groups));
        var lexiconBytes = TinyTrainer.FixtureLexicon();
        var lexicon = DemaConsulting.PartOfSpeech.Lexicon.Load(lexiconBytes);
        var decoder = new TagDecoder(model, lexicon);
        var sentences = TinyTrainer.Sentences().Select(s => s.Tokens).Concat(new[]
        {
            new[] { "Open", "the", "door", "and", "run", "." },
            new[] { "She", "will", "run", "to", "the", "store", ",", "then", "walks", "home", "." },
            new[] { "Running", "dogs", "chased", "the", "boxes", "." },
        }).ToList();

        // Act / Assert: the same tags for every token of every sentence
        Assert.Equal(groups, model.FeatureGroups);
        foreach (var words in sentences)
        {
            var actual = decoder.Decode(Tokens(words), null, 0).Select(t => (int)t.Tag).ToArray();
            Assert.Equal(ReferenceDecode(model, lexicon, words, groups), actual);
        }
    }

    /// <summary>
    ///     Proves that the two-pass decode is deterministic: repeated and concurrent decodes of the same sentences give identical results.
    /// </summary>
    [Fact]
    public void TagDecoder_Decode_RightPreview_IsDeterministic()
    {
        // Arrange: a toy model with the preview group and a list of sentences
        var model = PerceptronModel.Load(TinyTrainer.Train(epochs: 5, groups: FeatureGroups.RightPreview | FeatureGroups.ClauseCues));
        var decoder = new TagDecoder(model, DemaConsulting.PartOfSpeech.Lexicon.Load(TinyTrainer.FixtureLexicon()));
        var sentences = TinyTrainer.Sentences().Select(s => Tokens(s.Tokens)).ToList();
        string Render(Token[] tokens) => string.Join(" ", decoder.Decode(tokens, null, 2).Select(t => t.Tag + ":" + t.Confidence.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
        var expected = sentences.Select(Render).ToArray();

        // Act: decode again sequentially and from several threads
        var again = sentences.Select(Render).ToArray();
        var concurrent = new string[sentences.Count * 4];
        Parallel.For(0, concurrent.Length, i => concurrent[i] = Render(sentences[i % sentences.Count]));

        // Assert: identical text every time
        Assert.Equal(expected, again);
        for (var i = 0; i < concurrent.Length; i++)
        {
            Assert.Equal(expected[i % sentences.Count], concurrent[i]);
        }
    }

    /// <summary>
    ///     Decodes with the string-based reference features and the plain rules of the decoder (lexicon restriction, lowest tag wins a tie),
    ///     in two passes when the model has the preview group.
    /// </summary>
    private static int[] ReferenceDecode(PerceptronModel model, DemaConsulting.PartOfSpeech.Lexicon lexicon, string[] words, FeatureGroups groups)
    {
        int[] Pass(IReadOnlyList<string>? preview)
        {
            var chosen = new List<string>();
            var result = new int[words.Length];
            for (var i = 0; i < words.Length; i++)
            {
                var previous1 = i >= 1 ? chosen[i - 1] : "-S-";
                var previous2 = "-S2-";
                if (i == 1)
                {
                    previous2 = "-S-";
                }
                else if (i >= 2)
                {
                    previous2 = chosen[i - 2];
                }
                var buckets = FeatureExtractor.ReferenceBuckets(words, i, previous1, previous2, model.HashBits, groups, lexicon, chosen, preview);
                var scores = new int[UposExtensions.Count];
                foreach (var bucket in buckets)
                {
                    model.AddScores(bucket, scores);
                }

                var allowed = DemaConsulting.PartOfSpeech.Lexicon.AllowedTags(words[i], lexicon);
                var best = -1;
                for (var tag = 0; tag < UposExtensions.Count; tag++)
                {
                    if (allowed.Contains((Upos)tag) && (best < 0 || scores[tag] > scores[best]))
                    {
                        best = tag;
                    }
                }

                result[i] = best;
                chosen.Add(((Upos)best).ToTagString());
            }

            return result;
        }

        var first = Pass(null);
        return (groups & FeatureGroups.RightPreview) != 0 ? Pass(first.Select(t => ((Upos)t).ToTagString()).ToList()) : first;
    }
}
