namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     Splits English text into sentences and tokens following the tokenization convention of the training data.
/// </summary>
/// <remarks>
///     <para>
///         The tagger was trained on text tokenized in a UD-like convention: contractions are split
///         (<c>does</c> + <c>n't</c>, <c>John</c> + <c>'s</c>), punctuation is separate, hyphenated compounds stay
///         whole. A tagger fed with another tokenization would see unseen tokens, so this tokenizer reproduces that
///         convention; the rules are verified against all open-corpus sentences by the test suite. The complete
///         normative rules are in https://github.com/demaconsulting/PartOfSpeech/blob/main/data/CONVENTIONS.md in the
///         repository; this page is the compact version.
///     </para>
///     <para>
///         ## Conventions
///     </para>
///     <para>
///         The text is first cut at white space: every Unicode white-space character (space, tab, no-break space, CR
///         and LF, so also CRLF line ends) separates chunks, and a token never contains white space. Each chunk is then
///         split by these rules; every example is the real output of the tokenizer, with the tokens separated by
///         spaces.
///     </para>
///     <list type="table">
///     <listheader>
///     <term>Rule</term>
///     <description>Text becomes tokens</description>
///     </listheader>
///     <item>
///     <term>Negation contractions: <c>n't</c> is split off; <c>ca</c>, <c>wo</c>, <c>sha</c>, <c>ai</c> are the stems of the irregular forms; <c>cannot</c> is two tokens</term>
///     <description><c>doesn't</c> gives <c>does n't</c>; <c>can't</c> gives <c>ca n't</c>; <c>won't</c> gives <c>wo n't</c>; <c>cannot</c> gives <c>can not</c></description>
///     </item>
///     <item>
///     <term>Clitics <c>'s 're 've 'll 'd 'm</c> are split off a stem of letters. The possessive <c>'s</c> and the contracted <c>is</c> look the same and both are split</term>
///     <description><c>I've</c> gives <c>I 've</c>; <c>John's</c> gives <c>John 's</c>; <c>We're</c> gives <c>We 're</c></description>
///     </item>
///     <item>
///     <term>A plural possessive apostrophe and other trailing quote characters are tokens of their own</term>
///     <description><c>dogs'</c> gives <c>dogs '</c></description>
///     </item>
///     <item>
///     <term>Hyphens inside a word stay (compounds, numbered names); a double hyphen and the dash characters en dash, em dash and horizontal bar are tokens of their own</term>
///     <description><c>well-known</c>, <c>COVID-19</c>, <c>co-op</c> stay whole; <c>forest--at</c> gives <c>forest -- at</c>; an em dash (U+2014) between two words without spaces gives the word, the dash and the word as three tokens</description>
///     </item>
///     <item>
///     <term>Punctuation at the start or end of a chunk is split off, one token per character: <c>( [ { &lt;</c> and quotes and currency signs in front; <c>, ; : ! ? ) ] } &gt; %</c> and quotes behind. Punctuation inside a chunk is not split</term>
///     <description><c>"Hi,"</c> gives <c>" Hi , "</c>; <c>(quietly).</c> gives <c>( quietly ) .</c>; <c>$700</c> gives <c>$ 700</c>; <c>73%</c> gives <c>73 %</c>; <c>Hello,world</c> stays one token</description>
///     </item>
///     <item>
///     <term>Numbers with inner punctuation, times, and words with a slash or an ampersand stay whole</term>
///     <description><c>3.5</c>, <c>1,000</c>, <c>7:00</c>, <c>and/or</c>, <c>R&amp;D</c>, <c>o'clock</c></description>
///     </item>
///     <item>
///     <term>A single final period stays attached to a dotted acronym (letters separated by periods), to a known abbreviation (titles such as <c>Dr.</c> only with a capital letter; <c>etc.</c>, <c>Inc.</c> and the like in any case) and to a single-letter initial; any other final period is split off. Abbreviations and initials lose the period when their chunk is the last one of the text, and the pronoun <c>I</c> never keeps it (unless a closing quote follows the period)</term>
///     <description><c>Dr.</c>, <c>Mr.</c>, <c>U.S.</c>, <c>p.m.</c>, <c>etc.</c> and the initial in <c>J. Smith</c> stay whole; <c>home.</c> gives <c>home .</c>; at the end of the text <c>pens, etc.</c> gives <c>pens , etc .</c> and <c>by J.</c> gives <c>by J .</c>, but the dotted acronym in <c>in the U.S.</c> stays whole; <c>He said I.</c> gives <c>He said I .</c></description>
///     </item>
///     <item>
///     <term>Ellipsis: three periods after a word are one token, and so is the ellipsis character</term>
///     <description><c>Wait...</c> gives <c>Wait ...</c></description>
///     </item>
///     <item>
///     <term>Web and e-mail addresses are one token each, without the punctuation that follows them</term>
///     <description><c>www.example.com.</c> gives <c>www.example.com .</c>; <c>me@x.org</c> stays whole</description>
///     </item>
///     <item>
///     <term>Typographic characters: the curly apostrophes and quotes (U+2018, U+2019, U+201B, U+02BC, U+201C, U+201D) count like the ASCII ones, and the token keeps the character that it had</term>
///     <description>The text <c>don</c>, U+2019, <c>t</c> gives the tokens <c>do</c> and <c>n’t</c> (the second one keeps the curly apostrophe U+2019, it is not changed to <c>n't</c>); a quotation in U+201C and U+201D gives the two quote marks as separate tokens</description>
///     </item>
///     </list>
///     <para>
///         ## Sentences, offsets and normalization
///     </para>
///     <para>
///         A sentence ends after <c>.</c>, <c>!</c>, <c>?</c>, <c>...</c> or the ellipsis character (and the quotes and
///         brackets that touch it) when the next token starts with an upper-case letter, a digit or an opening quote or
///         bracket, or when the text ends. An ellipsis before a lower-case word does not end a sentence, a single line
///         break (LF or CRLF) does not, and a blank line (two line breaks) always does. For example
///         <c>He left home. She stayed.</c> is two sentences, and <c>Title</c>, a blank line and <c>Body text.</c> are
///         two sentences even though the title has no period.
///     </para>
///     <para>
///         A token that keeps its period (see the table) is handled by its own rules. Titles and similar words
///         (<c>Mr.</c>, <c>Mrs.</c>, <c>Dr.</c>, <c>Prof.</c>, <c>St.</c>, <c>No.</c>, <c>vs.</c>, <c>e.g.</c>,
///         <c>i.e.</c>, <c>Gen.</c>, <c>Col.</c>, <c>Rev.</c> and similar titles) never end a sentence: <c>Dr. Smith left.</c> is one sentence.
///         An initial such as <c>J.</c> does not end a sentence either, except that a quoted initial that is followed by
///         an upper-case word does (<c>He said "I." Then he left.</c> is two sentences). A dotted acronym such as
///         <c>U.S.</c> ends a sentence only before one of a fixed list of common sentence-starting words (<c>The</c>,
///         <c>He</c>, <c>In</c>, <c>But</c> and similar): <c>the U.S. He left</c> is two sentences, <c>the U.S. Army</c>
///         is one. Any other abbreviation (<c>etc.</c>, <c>Inc.</c>, <c>Co.</c>) ends a sentence before a word that
///         starts with an upper-case letter, so <c>pens, etc. The end</c> is two sentences but <c>pens, etc. and more</c>
///         and <c>pens, etc. 5 more</c> are one. The lists are tuned to the open corpus and are not complete, so
///         rare abbreviations can be split wrongly.
///     </para>
///     <para>
///         The tokenizer never changes characters: tokens are slices of the source string, and the offset of each
///         token (<see cref="Token.Start"/>) indexes the whole source string, including line breaks that lie inside a
///         sentence, so <c>text.Substring(token.Start, token.Length)</c> is always the token text. There is no Unicode
///         normalization and no case folding in the tokenizer: a decomposed <c>e</c> plus combining accent stays the
///         two characters that it was. The tagger itself folds each token for its lexicon lookup and features
///         (Unicode NFC, curly quotes to straight quotes, dashes to two hyphens, the ellipsis character to three
///         periods), but that is internal; the tokens that you get back are as written.
///     </para>
///     <para>
///         It is a set of static, stateless methods and is safe for concurrent use. To tag tokens that another
///         component produced, use <see cref="CreateSentence(IReadOnlyList{string})"/>.
///     </para>
/// </remarks>
/// <example>
///     Split a text into sentences and tokens without tagging it. The line break inside the text is ordinary white
///     space (a single one does not end a sentence), and the offsets index the whole text:
///     <code>
///     string text = "Dr. Smith doesn't smoke.\r\nShe's well-known.";
///
///     foreach (Sentence sentence in Tokenizer.Tokenize(text))
///     {
///         // First sentence: Dr. | Smith | does | n't | smoke | .    Second: She | 's | well-known | .
///         Console.WriteLine(string.Join(" | ", sentence.Tokens.Select(token => token.Text)));
///
///         foreach (Token token in sentence.Tokens)
///         {
///             // Always true: the offset is a position in the whole text, here also after the line break
///             Console.WriteLine(text.Substring(token.Start, token.Length) == token.Text);
///         }
///     }
///     </code>
/// </example>
public static class Tokenizer
{
    /// <summary>
    ///     The abbreviations (without the period) that are written with a capital letter and keep the period unless they end the text.
    /// </summary>
    private static readonly HashSet<string> TitleAbbreviations = new(StringComparer.Ordinal)
    {
        "mr", "mrs", "ms", "dr", "st", "jr", "sr", "no", "prof", "capt", "gen", "col", "sgt", "lt", "rev", "hon", "mt", "ft",
        "gov", "sen", "rep", "pres", "cmdr", "adm", "maj", "cpl", "pvt", "supt", "messrs"
    };

    /// <summary>
    ///     The abbreviations (without the period) that keep the period in any letter case unless they end the text.
    /// </summary>
    private static readonly HashSet<string> SentenceEndingAbbreviations = new(StringComparer.Ordinal)
    {
        "etc", "inc", "co", "corp", "ltd", "vs", "v", "e.g", "i.e", "ft"
    };

    /// <summary>
    ///     Splits a text into sentences, each with its tokens.
    /// </summary>
    /// <remarks>
    ///     White space (any Unicode white-space character, including CR and LF) only separates chunks; each chunk is then
    ///     split at contractions, leading and trailing punctuation, dashes and final periods by the rules in the
    ///     remarks of <see cref="Tokenizer"/>, so <c>doesn't</c> gives <c>does</c> and <c>n't</c> and
    ///     <c>"Hi,"</c> gives four tokens. Sentence boundaries follow terminal punctuation, abbreviations,
    ///     capitalization and blank lines (also described there); a single line break does not end a sentence. A text
    ///     without any token yields an empty list. The offsets index <paramref name="text"/> and the characters are
    ///     never changed.
    /// </remarks>
    /// <param name="text">The text to split; it may contain any number of sentences and line breaks.</param>
    /// <returns>The sentences in text order; empty when the text has no token.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <c>null</c>.</exception>
    public static IReadOnlyList<Sentence> Tokenize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var tokens = TokenizeWords(text);
        return SentenceSplitter.Split(text, tokens);
    }

    /// <summary>
    ///     Creates a sentence from tokens that the caller already produced.
    /// </summary>
    /// <remarks>
    ///     The tokens are validated but not changed or split: punctuation marks and clitics such as <c>n't</c> must
    ///     already be tokens of their own (<c>Hello,</c> stays one token), and capitalization and spelling are kept as
    ///     given. The tokens are joined with single spaces into the sentence text (<see cref="Sentence.Text"/>), which
    ///     then is the source string of the token offsets (<see cref="Sentence.Start"/> is 0), so the offsets index
    ///     the joined text and not the text that the tokens came from. Use this to tag text that was tokenized by another
    ///     component; the tokens must follow the convention of <see cref="Tokenizer"/> for best accuracy.
    /// </remarks>
    /// <param name="tokens">The tokens; none may be <c>null</c>, empty or contain white space.</param>
    /// <returns>The sentence; an empty list of tokens gives an empty sentence.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tokens"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">A token is <c>null</c>, empty or contains white space.</exception>
    /// <example>
    ///     Create a sentence from tokens that another component produced. The punctuation is supplied as tokens, the
    ///     capital letters are kept, and the offsets refer to the joined text:
    ///     <code>
    ///     Sentence sentence = Tokenizer.CreateSentence(new[] { "Hello", ",", "World", "!" });
    ///
    ///     Console.WriteLine(sentence.Text);   // Hello , World !
    ///     Console.WriteLine(string.Join(" ", sentence.Tokens));   // Hello@0 ,@6 World@8 !@14
    ///     TaggedSentence tagged = tagger.Tag(sentence);
    ///     </code>
    /// </example>
    public static Sentence CreateSentence(IReadOnlyList<string> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        // Validate each token and place it after the previous one, separated by one space
        var result = new Token[tokens.Count];
        var builder = new System.Text.StringBuilder();
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (string.IsNullOrEmpty(token) || ContainsWhiteSpace(token))
            {
                throw new ArgumentException($"Token {i} is null, empty or contains white space.", nameof(tokens));
            }

            if (i > 0)
            {
                builder.Append(' ');
            }

            result[i] = new Token(token, builder.Length);
            builder.Append(token);
        }

        return new Sentence(builder.ToString(), result);
    }

    /// <summary>
    ///     Splits a text into tokens without looking for sentence boundaries.
    /// </summary>
    /// <remarks>
    ///     This is the word-level part of the tokenizer; the conformance test compares it with the gold tokens of the
    ///     open corpus sentence by sentence.
    /// </remarks>
    /// <param name="text">The text.</param>
    /// <returns>The tokens in text order.</returns>
    internal static List<Token> TokenizeWords(string text)
    {
        var tokens = new List<Token>();
        var length = text.Length;
        var index = 0;
        while (index < length)
        {
            // Skip white space, then take the next white-space delimited chunk
            if (char.IsWhiteSpace(text[index]))
            {
                index++;
                continue;
            }

            var start = index;
            while (index < length && !char.IsWhiteSpace(text[index]))
            {
                index++;
            }

            // The text ends after this chunk when only white space follows
            var rest = index;
            while (rest < length && char.IsWhiteSpace(text[rest]))
            {
                rest++;
            }

            TokenizeChunk(text, start, index, rest >= length, tokens);
        }

        return tokens;
    }

    /// <summary>
    ///     Determines whether a string contains a white-space character.
    /// </summary>
    /// <param name="value">The string.</param>
    /// <returns><c>true</c> if any character is white space.</returns>
    private static bool ContainsWhiteSpace(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsWhiteSpace(value[i]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Determines whether a character is an apostrophe in any of its common forms.
    /// </summary>
    /// <param name="c">The character.</param>
    /// <returns><c>true</c> for the ASCII apostrophe and the typographic variants.</returns>
    private static bool IsApostrophe(char c) => c == '\'' || c == '\u2019' || c == '\u2018' || c == '\u201B' || c == '\u02BC';

    /// <summary>
    ///     Determines whether a character is an ASCII letter.
    /// </summary>
    /// <param name="c">The character.</param>
    /// <returns><c>true</c> for A to Z and a to z.</returns>
    private static bool IsAsciiLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

    /// <summary>
    ///     Determines whether a character is split off the front of a word as a token of its own.
    /// </summary>
    /// <param name="c">The character.</param>
    /// <returns><c>true</c> for opening brackets and quotes and currency symbols.</returns>
    private static bool IsLeadingPunctuation(char c) =>
        c == '(' || c == '[' || c == '{' || c == '<' || c == '"' || c == '`' || c == '$' || c == '\u00A3' || c == '\u20AC' ||
        c == '\u201C' || c == '\u201D' || c == '\u201E' || c == '\u00AB' || c == '\u00BB' || IsApostrophe(c);

    /// <summary>
    ///     Determines whether a character is split off the end of a word as a token of its own.
    /// </summary>
    /// <param name="c">The character.</param>
    /// <returns><c>true</c> for closing brackets and quotes, sentence punctuation and the percent sign.</returns>
    private static bool IsTrailingPunctuation(char c) =>
        c == ',' || c == ';' || c == ':' || c == '!' || c == '?' || c == ')' || c == ']' || c == '}' || c == '"' || c == '%' ||
        c == '>' || c == '`' || c == '\u201C' || c == '\u201D' || c == '\u201E' || c == '\u00AB' || c == '\u00BB' || IsApostrophe(c);

    /// <summary>
    ///     Determines whether a character is a dash or the ellipsis character that always forms a token of its own.
    /// </summary>
    /// <param name="c">The character.</param>
    /// <returns><c>true</c> for en dash, em dash, horizontal bar and the ellipsis character.</returns>
    private static bool IsSeparatorSymbol(char c) => c == '\u2013' || c == '\u2014' || c == '\u2015' || c == '\u2026';

    /// <summary>
    ///     Splits one white-space delimited chunk into tokens and appends them.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="start">The first character of the chunk.</param>
    /// <param name="end">The exclusive end of the chunk.</param>
    /// <param name="atEnd">Whether the chunk is the last one of the text.</param>
    /// <param name="tokens">The destination.</param>
    private static void TokenizeChunk(string text, int start, int end, bool atEnd, List<Token> tokens)
    {
        // Leading punctuation: one token per character, except a quote that starts a clitic form ('s, 'em, 'Tis)
        while (start < end)
        {
            var c = text[start];
            if (IsLeadingPunctuation(c) && !(IsApostrophe(c) && StartsClitic(text, start, end)))
            {
                tokens.Add(new Token(text.Substring(start, 1), start));
                start++;
            }
            else
            {
                break;
            }
        }

        // Trailing punctuation: peeled from the end and emitted after the core in text order
        var trailing = new List<Token>();
        var quoted = false;
        while (end > start)
        {
            var c = text[end - 1];
            if (c == '.')
            {
                var run = DotRunBefore(text, start, end);
                if (run >= 2)
                {
                    PeelDotRun(text, start, end, run, trailing);
                    end -= run;
                    continue;
                }

                if (KeepsPeriod(text, start, end - 1, atEnd, quoted))
                {
                    break;
                }

                trailing.Add(new Token(".", end - 1));
                end--;
            }
            else if (IsTrailingPunctuation(c) || c == '\u2026')
            {
                quoted |= c == '"' || c == '\u201D' || c == '\'' || c == '\u2019' || c == ')' || c == ']';
                trailing.Add(new Token(text.Substring(end - 1, 1), end - 1));
                end--;
            }
            else
            {
                break;
            }
        }

        if (end > start)
        {
            SplitCore(text, start, end, tokens);
        }

        for (var i = trailing.Count - 1; i >= 0; i--)
        {
            tokens.Add(trailing[i]);
        }
    }

    /// <summary>
    ///     Counts the periods at the end of a range.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="start">The start of the range.</param>
    /// <param name="end">The exclusive end of the range.</param>
    /// <returns>The number of consecutive periods before <paramref name="end"/>.</returns>
    private static int DotRunBefore(string text, int start, int end)
    {
        var run = 0;
        while (end - 1 - run >= start && text[end - 1 - run] == '.')
        {
            run++;
        }

        return run;
    }

    /// <summary>
    ///     Peels a run of two or more periods from the end of a chunk into tokens.
    /// </summary>
    /// <remarks>
    ///     The corpus convention: a run that is the whole rest of the chunk (<c>...</c> between spaces) is one token per
    ///     period; a run of three is one ellipsis token; a run of four is the sentence period followed by an ellipsis
    ///     (<c>men....</c>); longer runs are one token.
    /// </remarks>
    /// <param name="text">The text.</param>
    /// <param name="start">The start of the chunk part that is still unprocessed.</param>
    /// <param name="end">The exclusive end of the unprocessed part; the run ends here.</param>
    /// <param name="run">The length of the run.</param>
    /// <param name="trailing">The collected trailing tokens, in right-to-left order.</param>
    private static void PeelDotRun(string text, int start, int end, int run, List<Token> trailing)
    {
        var first = end - run;
        if (first == start)
        {
            // Dots between spaces: each is a token
            for (var i = end - 1; i >= first; i--)
            {
                trailing.Add(new Token(".", i));
            }
        }
        else if (run == 4)
        {
            trailing.Add(new Token("...", first + 1));
            trailing.Add(new Token(".", first));
        }
        else
        {
            trailing.Add(new Token(text.Substring(first, run), first));
        }
    }

    /// <summary>
    ///     Determines whether an apostrophe at the start of a range begins a clitic word (<c>'s</c>, <c>'em</c>, <c>'Tis</c>).
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="start">The position of the apostrophe.</param>
    /// <param name="end">The exclusive end of the chunk.</param>
    /// <returns><c>true</c> if the apostrophe is part of the word.</returns>
    private static bool StartsClitic(string text, int start, int end)
    {
        // Look at the letters that follow the apostrophe up to the first non-letter
        var letters = start + 1;
        while (letters < end && IsAsciiLetter(text[letters]))
        {
            letters++;
        }

        var word = text.Substring(start + 1, letters - start - 1).ToLowerInvariant();
        return word is "s" or "m" or "re" or "ve" or "ll" or "d" or "em" or "tis" or "twas";
    }

    /// <summary>
    ///     Decides whether a single period at the end of a word stays attached to it.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="start">The start of the word.</param>
    /// <param name="dot">The position of the period (the word before it is <c>[start, dot)</c>).</param>
    /// <param name="atEnd">Whether the word is in the last chunk of the text.</param>
    /// <param name="quoted">Whether a closing quote follows the period inside the chunk (<c>"a."</c>).</param>
    /// <returns><c>true</c> if the period belongs to the token (an abbreviation, an initial or a dotted acronym).</returns>
    private static bool KeepsPeriod(string text, int start, int dot, bool atEnd, bool quoted)
    {
        var length = dot - start;
        if (length <= 0)
        {
            return false;
        }

        // A single letter is an initial, a list marker or a letter that is being talked about ("a."); the pronoun I
        // and the last word of a text are the exceptions because there the period is far more likely a full stop
        if (length == 1 && char.IsLetter(text[start]))
        {
            if (quoted)
            {
                return true;
            }

            return text[start] != 'I' && !atEnd;
        }

        // A dotted acronym: letters separated by periods (U.S, p.m, D.C)
        if (IsDottedAcronym(text, start, dot))
        {
            return true;
        }

        // Known abbreviations keep the period, except that at the end of the text it is far more likely a full stop
        var word = text.Substring(start, length);
        var lower = word.ToLowerInvariant();
        if (atEnd)
        {
            return false;
        }

        if (TitleAbbreviations.Contains(lower) && char.IsUpper(word[0]))
        {
            return true;
        }

        return SentenceEndingAbbreviations.Contains(lower);
    }

    /// <summary>
    ///     Determines whether a range is a dotted acronym such as <c>U.S</c> or <c>p.m</c>.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="start">The start of the range.</param>
    /// <param name="end">The exclusive end of the range.</param>
    /// <returns><c>true</c> for a letter followed by one or more period and letter pairs.</returns>
    private static bool IsDottedAcronym(string text, int start, int end)
    {
        if ((end - start) < 3 || (end - start) % 2 == 0)
        {
            return false;
        }

        for (var i = start; i < end; i++)
        {
            var expectLetter = (i - start) % 2 == 0;
            if (expectLetter ? !char.IsLetter(text[i]) : text[i] != '.')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Splits the core of a chunk (no leading or trailing punctuation) into tokens.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="start">The first character of the core.</param>
    /// <param name="end">The exclusive end of the core.</param>
    /// <param name="tokens">The destination.</param>
    private static void SplitCore(string text, int start, int end, List<Token> tokens)
    {
        // Addresses stay whole
        if (IsAddress(text, start, end))
        {
            tokens.Add(new Token(text.Substring(start, end - start), start));
            return;
        }

        // Double hyphens and dash characters separate words; everything between is a word piece
        var pieceStart = start;
        var i = start;
        while (i < end)
        {
            var c = text[i];
            if (c == '-' && i + 1 < end && text[i + 1] == '-')
            {
                var run = 0;
                while (i + run < end && text[i + run] == '-')
                {
                    run++;
                }

                SplitWord(text, pieceStart, i, tokens);
                tokens.Add(new Token(text.Substring(i, run), i));
                i += run;
                pieceStart = i;
            }
            else if (IsSeparatorSymbol(c))
            {
                SplitWord(text, pieceStart, i, tokens);
                tokens.Add(new Token(text.Substring(i, 1), i));
                i++;
                pieceStart = i;
            }
            else
            {
                i++;
            }
        }

        SplitWord(text, pieceStart, end, tokens);
    }

    /// <summary>
    ///     Splits a word piece at a contraction and appends the parts.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="start">The first character of the piece.</param>
    /// <param name="end">The exclusive end of the piece.</param>
    /// <param name="tokens">The destination.</param>
    private static void SplitWord(string text, int start, int end, List<Token> tokens)
    {
        if (end <= start)
        {
            return;
        }

        var split = ContractionSplit(text, start, end);
        if (split < 0)
        {
            tokens.Add(new Token(text.Substring(start, end - start), start));
            return;
        }

        tokens.Add(new Token(text.Substring(start, split - start), start));
        tokens.Add(new Token(text.Substring(split, end - split), split));
    }

    /// <summary>
    ///     Finds where a word is split into a stem and a contraction part.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="start">The first character of the word.</param>
    /// <param name="end">The exclusive end of the word.</param>
    /// <returns>The index of the first character of the second part, or -1 if the word is not a contraction.</returns>
    private static int ContractionSplit(string text, int start, int end)
    {
        var length = end - start;

        // The irregular forms first: ca|n't, wo|n't, sha|n't, ai|n't and can|not
        if (length == 6 && EqualsLower(text, start, "cannot"))
        {
            return start + 3;
        }

        if (length == 6 && EqualsLower(text, start, "shan't"))
        {
            return start + 3;
        }

        if (length == 5 && (EqualsLower(text, start, "can't") || EqualsLower(text, start, "won't") || EqualsLower(text, start, "ain't")))
        {
            return start + 2;
        }

        // A stem of letters followed by n't
        if (length >= 4 && (text[end - 3] == 'n' || text[end - 3] == 'N') && IsApostrophe(text[end - 2]) && (text[end - 1] == 't' || text[end - 1] == 'T'))
        {
            var stemIsLetters = true;
            for (var i = start; i < end - 3 && stemIsLetters; i++)
            {
                stemIsLetters = IsAsciiLetter(text[i]);
            }

            if (stemIsLetters)
            {
                return end - 3;
            }
        }

        // A stem of letters and apostrophes followed by 's, 're, 've, 'll, 'd or 'm
        var apostrophe = -1;
        for (var i = end - 1; i > start; i--)
        {
            if (IsApostrophe(text[i]))
            {
                apostrophe = i;
                break;
            }
        }

        if (apostrophe > start && IsClitic(text, apostrophe + 1, end) && IsAsciiLetter(text[start]))
        {
            for (var i = start; i < apostrophe; i++)
            {
                if (!IsAsciiLetter(text[i]) && !IsApostrophe(text[i]))
                {
                    return -1;
                }
            }

            return apostrophe;
        }

        return -1;
    }

    /// <summary>
    ///     Determines whether letters after an apostrophe form a clitic.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="start">The first letter after the apostrophe.</param>
    /// <param name="end">The exclusive end.</param>
    /// <returns><c>true</c> for s, re, ve, ll, d and m in any case.</returns>
    private static bool IsClitic(string text, int start, int end)
    {
        var length = end - start;
        if (length < 1 || length > 2)
        {
            return false;
        }

        var first = char.ToLowerInvariant(text[start]);
        if (length == 1)
        {
            return first is 's' or 'd' or 'm';
        }

        var second = char.ToLowerInvariant(text[start + 1]);
        return (first == 'r' && second == 'e') || (first == 'v' && second == 'e') || (first == 'l' && second == 'l');
    }

    /// <summary>
    ///     Compares text with a lower-case word, ignoring case and treating every apostrophe form as an ASCII apostrophe.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="start">The first character to compare.</param>
    /// <param name="lower">The lower-case word; must not be longer than the text from <paramref name="start"/>.</param>
    /// <returns><c>true</c> if the text starts with the word.</returns>
    private static bool EqualsLower(string text, int start, string lower)
    {
        for (var i = 0; i < lower.Length; i++)
        {
            var c = text[start + i];
            var expected = lower[i];
            if (expected == '\'' ? !IsApostrophe(c) : char.ToLowerInvariant(c) != expected)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Determines whether a range is a URL or an e-mail address.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="start">The start of the range.</param>
    /// <param name="end">The exclusive end of the range.</param>
    /// <returns><c>true</c> for <c>http://</c>, <c>https://</c> and <c>www.</c> addresses and for <c>name@host.tld</c>.</returns>
    private static bool IsAddress(string text, int start, int end)
    {
        var span = text.Substring(start, end - start);
        if (span.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            span.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            (span.StartsWith("www.", StringComparison.OrdinalIgnoreCase) && span.Length > 4))
        {
            return true;
        }

        // An e-mail address has text on both sides of one at-sign and a period in the host part
        var at = span.IndexOf('@');
        return at > 0 && at < span.Length - 3 && span.IndexOf('@', at + 1) < 0 && span.IndexOf('.', at + 1) > at + 1 &&
            span[span.Length - 1] != '.';
    }
}
