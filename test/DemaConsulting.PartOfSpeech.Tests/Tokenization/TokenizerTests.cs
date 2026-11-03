// cspell:ignore Artagnan Dowd Burdick
namespace DemaConsulting.PartOfSpeech.Tests.Tokenization;

/// <summary>
///     Unit tests of <see cref="Tokenizer"/> (rules W1 to W11 of <c>data/CONVENTIONS.md</c>).
/// </summary>
public class TokenizerTests
{
    /// <summary>
    ///     Tokenizes a text and returns the token texts of all sentences.
    /// </summary>
    private static string[] Words(string text) =>
        Tokenizer.Tokenize(text).SelectMany(s => s.Tokens).Select(t => t.Text).ToArray();

    /// <summary>
    ///     Asserts that a text gives the expected tokens (written with a vertical bar between them).
    /// </summary>
    private static void AssertTokens(string text, string expected) =>
        Assert.Equal(expected.Split('|'), Words(text));

    /// <summary>
    ///     Proves that every token text equals the source slice at its offset.
    /// </summary>
    [Fact]
    public void Tokenizer_Tokenize_Offsets_IndexOriginalText()
    {
        // Arrange: text with irregular spacing and punctuation
        const string text = "  It's  a well-known fact,   isn't it? \"Yes.\" ";

        // Act: tokenize
        var sentences = Tokenizer.Tokenize(text);

        // Assert: token and sentence offsets index the original string
        Assert.NotEmpty(sentences);
        foreach (var sentence in sentences)
        {
            Assert.Equal(text.Substring(sentence.Start, sentence.Length), sentence.Text);
            foreach (var token in sentence.Tokens)
            {
                Assert.Equal(text.Substring(token.Start, token.Length), token.Text);
                Assert.True(token.Start >= sentence.Start && token.Start + token.Length <= sentence.Start + sentence.Length);
            }
        }

        var all = sentences.SelectMany(s => s.Tokens).ToList();
        Assert.Equal(all.OrderBy(t => t.Start).ToList(), all);
        Assert.All(all.Zip(all.Skip(1), (a, b) => a.Start + a.Length <= b.Start), ok => Assert.True(ok));
    }

    /// <summary>
    ///     Proves that tabs, line breaks, non-breaking spaces and runs of spaces separate tokens and never end up in them.
    /// </summary>
    [Fact]
    public void Tokenizer_Tokenize_AdversarialWhiteSpace_KeepsOffsets()
    {
        // Arrange: tab, CRLF, NBSP, ideographic space, form feed and vertical tab between words
        const string text = "one\ttwo\r\nthree\u00A0four\u3000five\fsix\vseven   eight";

        // Act: tokenize
        var tokens = Tokenizer.Tokenize(text).SelectMany(s => s.Tokens).ToList();

        // Assert: eight words, no white space inside a token, offsets exact
        Assert.Equal(new[] { "one", "two", "three", "four", "five", "six", "seven", "eight" }, tokens.Select(t => t.Text));
        Assert.All(tokens, t =>
        {
            Assert.DoesNotContain(t.Text, c => char.IsWhiteSpace(c));
            Assert.Equal(t.Text, text.Substring(t.Start, t.Length));
        });
    }

    /// <summary>
    ///     Proves that characters outside the basic plane stay in one token.
    /// </summary>
    [Fact]
    public void Tokenizer_Tokenize_SurrogatePairs_StayWhole()
    {
        // Arrange: emoji and mathematical letters (surrogate pairs), also next to punctuation
        const string text = "I \uD83D\uDE00 love \uD835\uDC9C\uD835\uDCB7 you\uD83D\uDE00, \"\uD83D\uDE00\"";

        // Act: tokenize
        var words = Words(text);

        // Assert: no pair is split, and offsets still index the string
        Assert.Equal(new[] { "I", "\uD83D\uDE00", "love", "\uD835\uDC9C\uD835\uDCB7", "you\uD83D\uDE00", ",", "\"", "\uD83D\uDE00", "\"" }, words);
        Assert.All(words, w => Assert.False(w.Length > 0 && char.IsLowSurrogate(w[0])));
    }

    /// <summary>
    ///     Proves that sentence punctuation, brackets and quotes are separate tokens.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="expected">The expected tokens separated by a vertical bar.</param>
    [Theory]
    [InlineData("Hello, world!", "Hello|,|world|!")]
    [InlineData("\"Hi,\" she said.", "\"|Hi|,|\"|she|said|.")]
    [InlineData("(see below)", "(|see|below|)")]
    [InlineData("It's [here].", "It|'s|[|here|]|.")]
    [InlineData("He said 'hello'.", "He|said|'|hello|'|.")]
    [InlineData("Wait: what?!", "Wait|:|what|?|!")]
    [InlineData("a; b", "a|;|b")]
    public void Tokenizer_Tokenize_Punctuation_IsSeparated(string text, string expected) => AssertTokens(text, expected);

    /// <summary>
    ///     Proves that a currency symbol in front of a number is a token of its own.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="expected">The expected tokens.</param>
    [Theory]
    [InlineData("It costs $700.", "It|costs|$|700|.")]
    [InlineData("Pay \u20AC5 now", "Pay|\u20AC|5|now")]
    [InlineData("\u00A39 each", "\u00A3|9|each")]
    public void Tokenizer_Tokenize_LeadingCurrencySymbol_IsSeparated(string text, string expected) => AssertTokens(text, expected);

    /// <summary>
    ///     Proves that the percent sign is a token of its own.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="expected">The expected tokens.</param>
    [Theory]
    [InlineData("73% of them", "73|%|of|them")]
    [InlineData("about 5%.", "about|5|%|.")]
    public void Tokenizer_Tokenize_Percent_IsSeparated(string text, string expected) => AssertTokens(text, expected);

    /// <summary>
    ///     Proves that n't is split off a stem of letters.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="expected">The expected tokens.</param>
    [Theory]
    [InlineData("don't", "do|n't")]
    [InlineData("isn't", "is|n't")]
    [InlineData("couldn't", "could|n't")]
    [InlineData("didn't.", "did|n't|.")]
    [InlineData("Don't go", "Do|n't|go")]
    [InlineData("haven't", "have|n't")]
    public void Tokenizer_Tokenize_NotContractions_AreSplit(string text, string expected) => AssertTokens(text, expected);

    /// <summary>
    ///     Proves the irregular stems of the convention (ca, wo, sha, ai and cannot).
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="expected">The expected tokens.</param>
    [Theory]
    [InlineData("can't", "ca|n't")]
    [InlineData("won't", "wo|n't")]
    [InlineData("shan't", "sha|n't")]
    [InlineData("ain't", "ai|n't")]
    [InlineData("cannot", "can|not")]
    public void Tokenizer_Tokenize_IrregularContractions_AreSplit(string text, string expected) => AssertTokens(text, expected);

    /// <summary>
    ///     Proves that 's, 'm, 're, 've, 'll and 'd are split off alphabetic stems.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="expected">The expected tokens.</param>
    [Theory]
    [InlineData("John's", "John|'s")]
    [InlineData("I've", "I|'ve")]
    [InlineData("we're", "we|'re")]
    [InlineData("they'll", "they|'ll")]
    [InlineData("he'd", "he|'d")]
    [InlineData("I'm", "I|'m")]
    [InlineData("could've", "could|'ve")]
    [InlineData("she'd've", "she'd|'ve")]
    public void Tokenizer_Tokenize_Clitics_AreSplit(string text, string expected) => AssertTokens(text, expected);

    /// <summary>
    ///     Proves that the case of the pieces is the case of the source.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="expected">The expected tokens.</param>
    [Theory]
    [InlineData("CAN'T", "CA|N'T")]
    [InlineData("DON'T", "DO|N'T")]
    [InlineData("JOHN'S", "JOHN|'S")]
    [InlineData("Won't", "Wo|n't")]
    public void Tokenizer_Tokenize_CaseIsPreserved(string text, string expected) => AssertTokens(text, expected);

    /// <summary>
    ///     Proves that typographic apostrophes are recognized but the token keeps the original character.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="expected">The expected tokens.</param>
    [Theory]
    [InlineData("don\u2019t", "do|n\u2019t")]
    [InlineData("John\u2019s car", "John|\u2019s|car")]
    [InlineData("can\u2019t", "ca|n\u2019t")]
    public void Tokenizer_Tokenize_TypographicApostrophe_IsSplitButKept(string text, string expected) => AssertTokens(text, expected);

    /// <summary>
    ///     Proves that words that only look like contractions stay whole.
    /// </summary>
    /// <param name="text">The text.</param>
    [Theory]
    [InlineData("o'clock")]
    [InlineData("gonna")]
    [InlineData("D'Artagnan")]
    [InlineData("O'Dowd")]
    [InlineData("1900's")]
    [InlineData("U.N.'s")]
    [InlineData("'Tis")]
    [InlineData("y'all")]
    [InlineData("'em")]
    public void Tokenizer_Tokenize_WholeWordExceptions_StayWhole(string text) => Assert.Equal(new[] { text }, Words(text));

    /// <summary>
    ///     Proves that hyphenated compounds stay whole.
    /// </summary>
    /// <param name="text">The text.</param>
    [Theory]
    [InlineData("well-known")]
    [InlineData("COVID-19")]
    [InlineData("-ing")]
    [InlineData("state-of-the-art")]
    public void Tokenizer_Tokenize_HyphenatedCompounds_StayWhole(string text) => Assert.Equal(new[] { text }, Words(text));

    /// <summary>
    ///     Proves that a double hyphen separates words and is a token of its own.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="expected">The expected tokens.</param>
    [Theory]
    [InlineData("forest--at", "forest|--|at")]
    [InlineData("wait---what", "wait|---|what")]
    [InlineData("end--", "end|--")]
    [InlineData("a -- b", "a|--|b")]
    public void Tokenizer_Tokenize_DoubleHyphen_IsSeparate(string text, string expected) => AssertTokens(text, expected);

    /// <summary>
    ///     Proves that slashes and ampersands inside words stay whole and standalone ones are tokens.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="expected">The expected tokens.</param>
    [Theory]
    [InlineData("and/or", "and/or")]
    [InlineData("km/h", "km/h")]
    [InlineData("C&O R&D", "C&O|R&D")]
    [InlineData("Tom & Jerry", "Tom|&|Jerry")]
    [InlineData("a / b + c", "a|/|b|+|c")]
    public void Tokenizer_Tokenize_SlashAndAmpersandWords_StayWhole(string text, string expected) => AssertTokens(text, expected);

    /// <summary>
    ///     Proves that numbers, times, ordinals and decades stay whole.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="expected">The expected tokens.</param>
    [Theory]
    [InlineData("1,000", "1,000")]
    [InlineData("3.5", "3.5")]
    [InlineData("7:00", "7:00")]
    [InlineData("at 6am on the 20th", "at|6am|on|the|20th")]
    [InlineData("the 1920s", "the|1920s")]
    [InlineData("open 24/7", "open|24/7")]
    [InlineData("The price is 1,000.", "The|price|is|1,000|.")]
    public void Tokenizer_Tokenize_NumbersAndTimes_StayWhole(string text, string expected) => AssertTokens(text, expected);

    /// <summary>
    ///     Proves that known abbreviations keep their period.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="expected">The expected tokens.</param>
    [Theory]
    [InlineData("Mr. Smith lives here", "Mr.|Smith|lives|here")]
    [InlineData("Dr. Who and Mrs. Hudson", "Dr.|Who|and|Mrs.|Hudson")]
    [InlineData("St. Louis", "St.|Louis")]
    [InlineData("Burdick v. United States", "Burdick|v.|United|States")]
    [InlineData("cats, dogs, etc. and more", "cats|,|dogs|,|etc.|and|more")]
    [InlineData("We like cats, dogs, etc.", "We|like|cats|,|dogs|,|etc|.")]
    public void Tokenizer_Tokenize_Abbreviations_KeepPeriod(string text, string expected) => AssertTokens(text, expected);

    /// <summary>
    ///     Proves that initials and dotted acronyms keep their periods.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="expected">The expected tokens.</param>
    [Theory]
    [InlineData("J. D. Salinger", "J.|D.|Salinger")]
    [InlineData("the U.S. army", "the|U.S.|army")]
    [InlineData("at 5 p.m. today", "at|5|p.m.|today")]
    [InlineData("J.D. Salinger", "J.D.|Salinger")]
    [InlineData("Plan B", "Plan|B")]
    public void Tokenizer_Tokenize_InitialsAndAcronyms_KeepPeriod(string text, string expected) => AssertTokens(text, expected);

    /// <summary>
    ///     Proves that the pronoun I and a final letter do not keep the period (a full stop), while a quoted letter does.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="expected">The expected tokens.</param>
    [Theory]
    [InlineData("So am I.", "So|am|I|.")]
    [InlineData("Plan B.", "Plan|B|.")]
    [InlineData("the letter \"a.\" Then", "the|letter|\"|a.|\"|Then")]
    [InlineData("\"Neither am I.\" Next", "\"|Neither|am|I.|\"|Next")]
    public void Tokenizer_Tokenize_SentenceFinalI_SplitsPeriod(string text, string expected) => AssertTokens(text, expected);

    /// <summary>
    ///     Proves that a trailing ellipsis is a token and that dots between spaces are one token each.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="expected">The expected tokens.</param>
    [Theory]
    [InlineData("Well...", "Well|...")]
    [InlineData("Well... maybe", "Well|...|maybe")]
    [InlineData("Hmm ... ok", "Hmm|.|.|.|ok")]
    [InlineData("the men....", "the|men|.|...")]
    public void Tokenizer_Tokenize_TrailingEllipsis_IsSeparate(string text, string expected) => AssertTokens(text, expected);

    /// <summary>
    ///     Proves that an ellipsis inside a word, or in front of it, stays attached.
    /// </summary>
    /// <param name="text">The text.</param>
    [Theory]
    [InlineData("as...as")]
    [InlineData("Oh...Okay")]
    [InlineData("...and")]
    public void Tokenizer_Tokenize_InnerEllipsis_StaysWhole(string text) => Assert.Equal(new[] { text }, Words(text));

    /// <summary>
    ///     Proves that URLs and e-mail addresses are single tokens without trailing punctuation.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="expected">The expected tokens.</param>
    [Theory]
    [InlineData("See https://example.com/a?b=1, or www.example.org.", "See|https://example.com/a?b=1|,|or|www.example.org|.")]
    [InlineData("Mail bob@example.com.", "Mail|bob@example.com|.")]
    [InlineData("(http://x.org/y)", "(|http://x.org/y|)")]
    public void Tokenizer_Tokenize_UrlsAndEmail_StayWhole(string text, string expected) => AssertTokens(text, expected);

    /// <summary>
    ///     Proves that typographic quotes, dashes and the ellipsis character are separate tokens that keep their characters.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="expected">The expected tokens.</param>
    [Theory]
    [InlineData("\u201CHi,\u201D he said \u2014 then left\u2026", "\u201C|Hi|,|\u201D|he|said|\u2014|then|left|\u2026")]
    [InlineData("word\u2014word", "word|\u2014|word")]
    [InlineData("1990\u20132000", "1990|\u2013|2000")]
    public void Tokenizer_Tokenize_TypographicQuotesAndDashes_AreSeparate(string text, string expected) => AssertTokens(text, expected);

    /// <summary>
    ///     Proves that pre-tokenized input is joined with single spaces and offsets index the joined text.
    /// </summary>
    [Fact]
    public void Tokenizer_CreateSentence_JoinsTokensWithSpaces()
    {
        // Arrange: three tokens
        var tokens = new[] { "Hello", ",", "world" };

        // Act: create the sentence
        var sentence = Tokenizer.CreateSentence(tokens);

        // Assert: joined text, start 0, exact offsets
        Assert.Equal("Hello , world", sentence.Text);
        Assert.Equal(0, sentence.Start);
        Assert.Equal(13, sentence.Length);
        Assert.Equal(new[] { 0, 6, 8 }, sentence.Tokens.Select(t => t.Start));
        Assert.Equal(tokens, sentence.Tokens.Select(t => t.Text));
        Assert.Empty(Tokenizer.CreateSentence(new string[0]).Tokens);
    }

    /// <summary>
    ///     Proves that null, empty and white-space tokens are rejected.
    /// </summary>
    [Fact]
    public void Tokenizer_CreateSentence_InvalidTokens_Throw()
    {
        // Arrange / Act / Assert: each invalid token and a null list throw the documented exception
        Assert.Throws<ArgumentException>(() => Tokenizer.CreateSentence(new string[] { "a", null! }));
        Assert.Throws<ArgumentException>(() => Tokenizer.CreateSentence(new[] { "a", string.Empty }));
        Assert.Throws<ArgumentException>(() => Tokenizer.CreateSentence(new[] { "a b" }));
        Assert.Throws<ArgumentException>(() => Tokenizer.CreateSentence(new[] { "a\tb" }));
        Assert.Throws<ArgumentNullException>(() => Tokenizer.CreateSentence(null!));
    }

    /// <summary>
    ///     Proves that a null text is rejected.
    /// </summary>
    [Fact]
    public void Tokenizer_Tokenize_NullText_ThrowsArgumentNullException()
    {
        // Arrange / Act / Assert: null is an error
        Assert.Throws<ArgumentNullException>(() => Tokenizer.Tokenize(null!));
    }

    /// <summary>
    ///     Proves that an empty or white-space text has no sentences.
    /// </summary>
    /// <param name="text">The text.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" \t\r\n \u00A0")]
    public void Tokenizer_Tokenize_EmptyOrWhiteSpace_ReturnsNoSentences(string text) => Assert.Empty(Tokenizer.Tokenize(text));
}
