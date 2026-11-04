namespace DemaConsulting.PartOfSpeech.Tests.Tokenization;

/// <summary>
///     Unit tests of <see cref="SentenceSplitter"/>.
/// </summary>
public class SentenceSplitterTests
{
    /// <summary>
    ///     Splits a text into the texts of its sentences.
    /// </summary>
    private static string[] Split(string text) =>
        SentenceSplitter.Split(text, Tokenizer.TokenizeWords(text)).Select(s => s.Text).ToArray();

    /// <summary>
    ///     Proves that period, question mark and exclamation mark end a sentence before a capitalized word.
    /// </summary>
    [Fact]
    public void SentenceSplitter_Split_PeriodQuestionExclamation_EndSentences()
    {
        // Arrange: four sentences with the three terminators
        const string text = "It works. Does it? Yes! Good.";

        // Act: split
        var sentences = Split(text);

        // Assert: four sentences
        Assert.Equal(new[] { "It works.", "Does it?", "Yes!", "Good." }, sentences);
    }

    /// <summary>
    ///     Proves that a closing quote or bracket stays with the sentence that it closes, and an opening quote starts the next.
    /// </summary>
    [Fact]
    public void SentenceSplitter_Split_ClosingQuoteStaysWithSentence()
    {
        // Arrange: a quoted sentence end, a bracketed one and an opening single quote after a sentence
        const string text = "He said \"Stop.\" Then he left (quickly.) She 'smiled.' 'Go,' he said.";

        // Act: split
        var sentences = Split(text);

        // Assert: the closers stay and the opening quote starts a sentence
        Assert.Equal(new[] { "He said \"Stop.\"", "Then he left (quickly.)", "She 'smiled.'", "'Go,' he said." }, sentences);
    }

    /// <summary>
    ///     Proves that titles and initials do not end a sentence.
    /// </summary>
    [Fact]
    public void SentenceSplitter_Split_AbbreviationDoesNotEndSentence()
    {
        // Arrange: a title, an initial and a dotted acronym before a name
        const string text = "Mr. Smith left. J. D. Salinger wrote. See Dr. No. now.";

        // Act: split
        var sentences = Split(text);

        // Assert: only the real full stops split
        Assert.Equal(new[] { "Mr. Smith left.", "J. D. Salinger wrote.", "See Dr. No. now." }, sentences);
    }

    /// <summary>
    ///     Proves that an abbreviation at the end of the text or before a capitalized word ends the sentence.
    /// </summary>
    [Fact]
    public void SentenceSplitter_Split_FinalAbbreviationEndsSentence()
    {
        // Arrange: etc. before a capitalized word, and at the end of the text
        const string text = "We sell tools, toys, etc. Prices vary. They sold etc.";

        // Act: split
        var sentences = Split(text);

        // Assert: three sentences
        Assert.Equal(new[] { "We sell tools, toys, etc.", "Prices vary.", "They sold etc." }, sentences);
    }

    /// <summary>
    ///     Proves that a terminator before a lower-case word does not end the sentence.
    /// </summary>
    [Fact]
    public void SentenceSplitter_Split_NoSplitBeforeLowerCase()
    {
        // Arrange: a period and a question mark followed by lower-case words
        const string text = "Wait. what is that? it is a cat.";

        // Act: split
        var sentences = Split(text);

        // Assert: one sentence
        Assert.Equal(new[] { "Wait. what is that? it is a cat." }, sentences);
    }

    /// <summary>
    ///     Proves that an ellipsis continues the sentence before a lower-case word and ends it before a capitalized one.
    /// </summary>
    [Fact]
    public void SentenceSplitter_Split_EllipsisBeforeLowerCaseContinues()
    {
        // Arrange / Act: the two cases
        var continued = Split("Well... maybe not.");
        var ended = Split("Well... Maybe not.");

        // Assert: one and two sentences
        Assert.Single(continued);
        Assert.Equal(new[] { "Well...", "Maybe not." }, ended);
    }

    /// <summary>
    ///     Proves that a blank line is always a boundary, also without terminal punctuation, for LF, CRLF and CR.
    /// </summary>
    [Fact]
    public void SentenceSplitter_Split_BlankLineEndsSentence()
    {
        // Arrange / Act: a title and a body separated by blank lines of three kinds
        var lf = Split("Title\n\nBody text");
        var crlf = Split("Title\r\n\r\nBody text");
        var cr = Split("Title\r\rBody text");
        var spaced = Split("Title\n  \t\nBody text");

        // Assert: always two sentences
        Assert.All(new[] { lf, crlf, cr, spaced }, s => Assert.Equal(new[] { "Title", "Body text" }, s));
    }

    /// <summary>
    ///     Proves that a single line break does not end a sentence.
    /// </summary>
    [Fact]
    public void SentenceSplitter_Split_SingleLineBreakDoesNotEndSentence()
    {
        // Arrange / Act: a hard-wrapped sentence
        var sentences = Split("one line\nsecond line\r\nthird");

        // Assert: one sentence
        Assert.Single(sentences);
    }

    /// <summary>
    ///     Proves that a text without a terminator is one sentence and an empty token list gives none.
    /// </summary>
    [Fact]
    public void SentenceSplitter_Split_TextWithoutTerminator_IsOneSentence()
    {
        // Arrange / Act: no terminator, and no tokens
        var one = Split("no terminator here");
        var none = Split("   ");

        // Assert: one sentence, and none
        Assert.Equal(new[] { "no terminator here" }, one);
        Assert.Empty(none);
    }
}
