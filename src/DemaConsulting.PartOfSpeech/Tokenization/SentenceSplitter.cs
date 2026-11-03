namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     Groups the tokens of a text into sentences.
/// </summary>
/// <remarks>
///     <para>
///         The rules are deliberately simple and deterministic: a sentence ends after terminal punctuation (and the
///         quotes and brackets that close with it) when the next token starts like a sentence (an upper-case letter, a
///         digit or an opening quote or bracket) or the text ends; abbreviations, initials and ellipses followed by
///         lower-case text do not end a sentence; a blank line always does. They are tuned to the open corpus, where
///         the benchmark joins consecutive sentences of a document and measures how many boundaries are recovered.
///     </para>
///     <para>
///         All members are stateless and thread-safe.
///     </para>
/// </remarks>
internal static class SentenceSplitter
{
    /// <summary>
    ///     Abbreviations that never end a sentence because they are always followed by a name or a number.
    /// </summary>
    private static readonly HashSet<string> NeverFinal = new(StringComparer.OrdinalIgnoreCase)
    {
        "mr.", "mrs.", "ms.", "dr.", "st.", "jr.", "sr.", "vs.", "v.", "prof.", "capt.", "gen.", "col.", "sgt.", "lt.", "rev.",
        "hon.", "mt.", "no.", "e.g.", "i.e.", "gov.", "sen.", "rep.", "pres.", "cmdr.", "adm.", "maj.", "cpl.", "pvt.", "supt.",
        "messrs.", "ft."
    };

    /// <summary>
    ///     Words that start a sentence often enough that a dotted acronym before them (<c>U.S. The</c>) ends the sentence.
    /// </summary>
    private static readonly HashSet<string> SentenceStarters = new(StringComparer.Ordinal)
    {
        "The", "A", "An", "He", "She", "It", "They", "We", "I", "You", "In", "On", "At", "But", "And", "This", "That", "There",
        "If", "When", "As", "For", "After", "Before", "Then", "So", "My", "His", "Her", "Our", "Their", "Its", "What", "How",
        "Who", "Where", "Why", "These", "Those", "Some", "Many", "Most", "No", "Yes", "Not", "Now", "Today", "However",
        "Together", "Along", "Also", "Other", "More", "Over", "From", "With", "To", "Each", "Every", "One", "Two", "All",
        "Both", "Several", "Another", "While", "Although", "Since", "During", "Because", "Please", "Let", "Do", "Is", "Are",
        "Can", "Will", "Would", "Should", "Could", "Have", "Has", "Results", "Here", "Of", "By", "Or", "Once", "Still"
    };

    /// <summary>
    ///     Splits tokens into sentences.
    /// </summary>
    /// <param name="source">The source string the tokens index.</param>
    /// <param name="tokens">The tokens of the whole text in source order.</param>
    /// <returns>The sentences in text order; empty when there are no tokens.</returns>
    public static IReadOnlyList<Sentence> Split(string source, IReadOnlyList<Token> tokens)
    {
        var sentences = new List<Sentence>();
        var sentenceStart = 0;
        var i = 0;
        while (i < tokens.Count)
        {
            // A blank line is a boundary whatever the tokens are
            var last = i == tokens.Count - 1;
            if (!last && HasBlankLine(source, tokens[i], tokens[i + 1]))
            {
                AddSentence(source, tokens, sentenceStart, i + 1, sentences);
                sentenceStart = i + 1;
            }
            else if (IsBoundaryAfter(tokens, i, out var closers))
            {
                // Closing quotes and brackets belong to the sentence that they close
                i += closers;
                AddSentence(source, tokens, sentenceStart, i + 1, sentences);
                sentenceStart = i + 1;
            }

            i++;
        }

        if (sentenceStart < tokens.Count)
        {
            AddSentence(source, tokens, sentenceStart, tokens.Count, sentences);
        }

        return sentences;
    }

    /// <summary>
    ///     Adds the sentence made of a token range.
    /// </summary>
    /// <param name="source">The source string.</param>
    /// <param name="tokens">All tokens.</param>
    /// <param name="from">The first token.</param>
    /// <param name="to">The exclusive end.</param>
    /// <param name="sentences">The destination.</param>
    private static void AddSentence(string source, IReadOnlyList<Token> tokens, int from, int to, List<Sentence> sentences)
    {
        var range = new Token[to - from];
        for (var i = 0; i < range.Length; i++)
        {
            range[i] = tokens[from + i];
        }

        sentences.Add(new Sentence(source, range));
    }

    /// <summary>
    ///     Determines whether two consecutive tokens are separated by a blank line (two line breaks).
    /// </summary>
    /// <param name="source">The source string.</param>
    /// <param name="left">The first token.</param>
    /// <param name="right">The next token.</param>
    /// <returns><c>true</c> if the gap holds at least two line breaks.</returns>
    private static bool HasBlankLine(string source, Token left, Token right)
    {
        var breaks = 0;
        for (var i = left.Start + left.Length; i < right.Start; i++)
        {
            var c = source[i];
            if (c == '\u2029')
            {
                return true;
            }

            // A carriage return that is part of a CRLF pair is counted once, with its line feed
            if (c == '\n' || (c == '\r' && (i + 1 >= right.Start || source[i + 1] != '\n')))
            {
                breaks++;
            }
        }

        return breaks >= 2;
    }

    /// <summary>
    ///     Determines whether a sentence ends after a token (and the closing tokens that follow it).
    /// </summary>
    /// <param name="tokens">All tokens.</param>
    /// <param name="index">The token to test.</param>
    /// <param name="closers">Receives the number of closing quote or bracket tokens that follow the token and belong to the sentence.</param>
    /// <returns><c>true</c> if a boundary follows.</returns>
    private static bool IsBoundaryAfter(IReadOnlyList<Token> tokens, int index, out int closers)
    {
        closers = 0;
        var text = tokens[index].Text;
        var terminal = IsTerminal(text);
        var abbreviation = !terminal && text.Length > 1 && text[text.Length - 1] == '.';
        if (!terminal && !abbreviation)
        {
            return false;
        }

        // Absorb quotes and brackets that are written right after the punctuation (no white space between)
        while (index + closers + 1 < tokens.Count && IsCloser(tokens[index + closers], tokens[index + closers + 1]))
        {
            closers++;
        }

        var next = index + closers + 1;
        if (next >= tokens.Count)
        {
            return true;
        }

        var follower = tokens[next].Text;
        if (abbreviation)
        {
            return EndsAfterAbbreviation(text, follower, closers > 0);
        }

        // An ellipsis followed by lower-case text continues the sentence
        var ellipsis = text.Length > 1 && (text[0] == '.' || text[0] == '\u2026');
        if (ellipsis && char.IsLower(follower[0]))
        {
            return false;
        }

        return StartsSentence(follower);
    }

    /// <summary>
    ///     Decides whether an abbreviation token that carries its period ends the sentence.
    /// </summary>
    /// <param name="abbreviation">The token, for example <c>Mr.</c>, <c>U.S.</c>, <c>etc.</c> or <c>J.</c>.</param>
    /// <param name="follower">The next token after any closing quotes.</param>
    /// <param name="closed">Whether closing quotes or brackets follow the abbreviation directly.</param>
    /// <returns><c>true</c> if a boundary follows.</returns>
    private static bool EndsAfterAbbreviation(string abbreviation, string follower, bool closed)
    {
        // Titles and similar words are always followed by a name or number
        if (NeverFinal.Contains(abbreviation))
        {
            return false;
        }

        // A single initial (J.) is followed by a surname, but a letter that is quoted ("I.") ends its sentence
        if (abbreviation.Length == 2 && char.IsLetter(abbreviation[0]))
        {
            return closed && char.IsUpper(follower[0]);
        }

        // A dotted acronym ends a sentence only before a word that typically starts one
        if (abbreviation.IndexOf('.') < abbreviation.Length - 1)
        {
            return SentenceStarters.Contains(follower);
        }

        // Other abbreviations (etc., Inc.) end the sentence before a capitalized word
        return char.IsUpper(follower[0]);
    }

    /// <summary>
    ///     Determines whether a token can start a sentence.
    /// </summary>
    /// <param name="text">The token.</param>
    /// <returns><c>true</c> for an upper-case letter, a digit, or an opening quote or bracket.</returns>
    private static bool StartsSentence(string text)
    {
        var c = text[0];
        return char.IsUpper(c) || char.IsDigit(c) || c == '"' || c == '\'' || c == '(' || c == '[' || c == '{' || c == '`' ||
            c == '\u201C' || c == '\u2018' || c == '\u00AB' || c == '\u00BF' || c == '\u00A1';
    }

    /// <summary>
    ///     Determines whether a token consists only of sentence-ending punctuation.
    /// </summary>
    /// <param name="text">The token.</param>
    /// <returns><c>true</c> for <c>.</c>, <c>!</c>, <c>?</c>, <c>...</c> and combinations such as <c>?!</c>.</returns>
    private static bool IsTerminal(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c != '.' && c != '!' && c != '?' && c != '\u2026')
            {
                return false;
            }
        }

        return text.Length > 0;
    }

    /// <summary>
    ///     Determines whether a token closes a quotation or bracket together with the token before it.
    /// </summary>
    /// <remarks>
    ///     A quote that is separated from the previous token by white space is an opening quote (<c>. 'Go</c>), one
    ///     that touches it is a closing quote (<c>.'</c>). The same test works for every quote character, so no state
    ///     about open quotes needs to be kept.
    /// </remarks>
    /// <param name="previous">The token before.</param>
    /// <param name="token">The token to test.</param>
    /// <returns><c>true</c> if the token is a closer that touches the previous token.</returns>
    private static bool IsCloser(Token previous, Token token)
    {
        if (token.Text.Length != 1 || token.Start != previous.Start + previous.Length)
        {
            return false;
        }

        var c = token.Text[0];
        return c == ')' || c == ']' || c == '}' || c == '"' || c == '\'' || c == '\u201D' || c == '\u2019' || c == '\u00BB';
    }
}
