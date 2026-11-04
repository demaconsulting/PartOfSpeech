// cspell:ignore ying Inflectable
namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     One inflected spelling of a head word together with the tags that the inflected word may take.
/// </summary>
internal readonly struct InflectedForm
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="InflectedForm"/> struct.
    /// </summary>
    /// <param name="text">The inflected spelling.</param>
    /// <param name="tags">The tags the inflected word may take.</param>
    public InflectedForm(string text, UposSet tags)
    {
        Text = text;
        Tags = tags;
    }

    /// <summary>
    ///     Gets the inflected spelling.
    /// </summary>
    public string Text { get; }

    /// <summary>
    ///     Gets the tags that the inflected word may take.
    /// </summary>
    public UposSet Tags { get; }
}

/// <summary>
///     Generates the regular English inflected forms of the head word of a glossary term.
/// </summary>
/// <remarks>
///     <para>
///         A glossary lists lemmas (<c>valve</c>, <c>isolate</c>) but text contains <c>valves</c>, <c>isolated</c> and
///         <c>isolating</c>. Without generated forms a glossary would silently fail on most occurrences, so the matcher
///         also looks for the forms that this class produces. The rules are the regular ASCII English rules only: wrong
///         forms are harmless because a spelling that never occurs never matches, and the irregular forms (<c>bled</c>,
///         <c>leaves</c>, <c>permitted</c>) are not guessed but listed by the user as variants.
///     </para>
///     <para>
///         The class is a set of pure functions; it is stateless and thread-safe. The tags of a form are derived from
///         the tags of the entry: a noun gets noun plurals; a verb gets the third-person form (verb), the past form
///         (verb or adjective, as in <c>an isolated valve</c>) and the present participle (verb, adjective or noun, as in
///         <c>the bleeding</c>). Proper nouns, adjectives and every other tag get no forms.
///     </para>
/// </remarks>
internal static class GlossaryInflector
{
    /// <summary>
    ///     Generates the inflected forms of a head word.
    /// </summary>
    /// <remarks>
    ///     Nothing is generated for a word of fewer than two characters, for a word that does not end in a letter, or for
    ///     a word that has a character other than an ASCII letter or a hyphen (the rules are English spelling rules).
    ///     When two rules produce the same spelling (<c>bleeds</c> as a plural and as a third-person form) their tags are
    ///     united. A form that equals the word itself is not returned.
    /// </remarks>
    /// <param name="word">The head word as written in the term.</param>
    /// <param name="tags">The tags of the entry.</param>
    /// <returns>The forms with their tags, in rule order (plural, third person, past, present participle).</returns>
    public static IReadOnlyList<InflectedForm> Inflect(string word, UposSet tags)
    {
        var forms = new List<InflectedForm>();
        if (!IsInflectable(word))
        {
            return forms;
        }

        // Noun plurals: proper nouns keep their tag on the plural form of an entry that has both tags
        if (tags.Contains(Upos.Noun))
        {
            var plural = tags.Contains(Upos.Propn) ? UposSet.Of(Upos.Noun, Upos.Propn) : UposSet.Of(Upos.Noun);
            AddForm(forms, word, Plural(word), plural);
        }

        // Verb forms: third person, past or participle, present participle
        if (tags.Contains(Upos.Verb))
        {
            AddForm(forms, word, Plural(word), UposSet.Of(Upos.Verb));
            AddForm(forms, word, Past(word), UposSet.Of(Upos.Verb, Upos.Adj));
            AddForm(forms, word, PresentParticiple(word), UposSet.Of(Upos.Verb, Upos.Adj, Upos.Noun));
        }

        return forms;
    }

    /// <summary>
    ///     Gets the regular plural of a noun or the third-person singular of a verb.
    /// </summary>
    /// <remarks>
    ///     An all-capital word of two or more letters (an acronym) gets a lower-case <c>s</c> (<c>LRU</c> gives
    ///     <c>LRUs</c>); otherwise <c>s</c>, <c>x</c>, <c>z</c>, <c>ch</c> and <c>sh</c> take <c>es</c>, a consonant plus
    ///     <c>y</c> becomes <c>ies</c>, and every other word takes <c>s</c>.
    /// </remarks>
    /// <param name="word">The word; see <see cref="Inflect"/> for what is accepted.</param>
    /// <returns>The plural or third-person form.</returns>
    public static string Plural(string word)
    {
        if (IsAcronym(word))
        {
            return word + "s";
        }

        var lower = word.ToLowerInvariant();
        var last = lower[lower.Length - 1];
        if (last == 's' || last == 'x' || last == 'z' || lower.EndsWith("ch", StringComparison.Ordinal) || lower.EndsWith("sh", StringComparison.Ordinal))
        {
            return word + "es";
        }

        if (EndsInConsonantY(lower))
        {
            return word.Substring(0, word.Length - 1) + "ies";
        }

        return word + "s";
    }

    /// <summary>
    ///     Gets the regular past tense and past participle of a verb.
    /// </summary>
    /// <remarks>
    ///     <c>e</c> takes <c>d</c>, a consonant plus <c>y</c> becomes <c>ied</c>, a monosyllabic word that ends in
    ///     consonant, vowel, consonant (not <c>w</c>, <c>x</c>, <c>y</c>) doubles the last consonant (<c>stop</c> gives
    ///     <c>stopped</c>), and every other word takes <c>ed</c>.
    /// </remarks>
    /// <param name="word">The word; see <see cref="Inflect"/> for what is accepted.</param>
    /// <returns>The past form.</returns>
    public static string Past(string word)
    {
        var lower = word.ToLowerInvariant();
        if (lower.EndsWith("e", StringComparison.Ordinal))
        {
            return word + "d";
        }

        if (EndsInConsonantY(lower))
        {
            return word.Substring(0, word.Length - 1) + "ied";
        }

        return DoublesFinalConsonant(lower) ? word + word[word.Length - 1] + "ed" : word + "ed";
    }

    /// <summary>
    ///     Gets the regular present participle (the <c>-ing</c> form) of a verb.
    /// </summary>
    /// <remarks>
    ///     <c>ie</c> becomes <c>ying</c>, a silent <c>e</c> is dropped (but not after <c>e</c>, <c>o</c> or <c>y</c>, so
    ///     <c>see</c> gives <c>seeing</c>), the doubling rule of <see cref="Past"/> applies, and every other word takes
    ///     <c>ing</c>.
    /// </remarks>
    /// <param name="word">The word; see <see cref="Inflect"/> for what is accepted.</param>
    /// <returns>The present participle.</returns>
    public static string PresentParticiple(string word)
    {
        var lower = word.ToLowerInvariant();
        if (lower.EndsWith("ie", StringComparison.Ordinal))
        {
            return word.Substring(0, word.Length - 2) + "ying";
        }

        if (lower.EndsWith("e", StringComparison.Ordinal) &&
            !lower.EndsWith("ee", StringComparison.Ordinal) &&
            !lower.EndsWith("oe", StringComparison.Ordinal) &&
            !lower.EndsWith("ye", StringComparison.Ordinal))
        {
            return word.Substring(0, word.Length - 1) + "ing";
        }

        return DoublesFinalConsonant(lower) ? word + word[word.Length - 1] + "ing" : word + "ing";
    }

    /// <summary>
    ///     Adds a form to the list, uniting the tags with an existing form of the same spelling.
    /// </summary>
    /// <param name="forms">The list.</param>
    /// <param name="word">The head word; a form that equals it is not added.</param>
    /// <param name="spelling">The inflected spelling.</param>
    /// <param name="tags">The tags of the form.</param>
    private static void AddForm(List<InflectedForm> forms, string word, string spelling, UposSet tags)
    {
        if (string.Equals(spelling, word, StringComparison.Ordinal))
        {
            return;
        }

        for (var i = 0; i < forms.Count; i++)
        {
            if (string.Equals(forms[i].Text, spelling, StringComparison.Ordinal))
            {
                forms[i] = new InflectedForm(spelling, forms[i].Tags.Union(tags));
                return;
            }
        }

        forms.Add(new InflectedForm(spelling, tags));
    }

    /// <summary>
    ///     Determines whether the rules apply to a word.
    /// </summary>
    /// <param name="word">The word.</param>
    /// <returns><c>true</c> for two or more characters of ASCII letters and hyphens that end in a letter.</returns>
    private static bool IsInflectable(string word) =>
        word.Length >= 2 && IsAsciiLetter(word[word.Length - 1]) && word.All(c => IsAsciiLetter(c) || c == '-');

    /// <summary>
    ///     Determines whether a word is an acronym: two or more letters, all of them capitals.
    /// </summary>
    /// <param name="word">The word.</param>
    /// <returns><c>true</c> for an all-capital word of at least two letters.</returns>
    private static bool IsAcronym(string word)
    {
        var letters = 0;
        foreach (var c in word)
        {
            if (c >= 'a' && c <= 'z')
            {
                return false;
            }

            if (c >= 'A' && c <= 'Z')
            {
                letters++;
            }
        }

        return letters >= 2;
    }

    /// <summary>
    ///     Determines whether a lower-case word ends in a consonant followed by <c>y</c>.
    /// </summary>
    /// <param name="lower">The lower-case word of at least two characters.</param>
    /// <returns><c>true</c> for <c>battery</c>, <c>false</c> for <c>key</c>.</returns>
    private static bool EndsInConsonantY(string lower) =>
        lower[lower.Length - 1] == 'y' && lower.Length >= 2 && IsConsonant(lower[lower.Length - 2]);

    /// <summary>
    ///     Determines whether the final consonant of a word is doubled before a suffix that starts with a vowel.
    /// </summary>
    /// <param name="lower">The lower-case word.</param>
    /// <returns><c>true</c> for a monosyllabic word that ends in consonant, vowel, consonant, the last not <c>w</c>, <c>x</c> or <c>y</c>.</returns>
    private static bool DoublesFinalConsonant(string lower)
    {
        var length = lower.Length;
        if (length < 3)
        {
            return false;
        }

        var last = lower[length - 1];
        if (!IsConsonant(last) || last == 'w' || last == 'x' || last == 'y' || !IsVowel(lower[length - 2]) || !IsConsonant(lower[length - 3]))
        {
            return false;
        }

        // Monosyllabic: exactly one group of vowels in the whole word (hyphenated words are never doubled)
        var groups = 0;
        var inGroup = false;
        foreach (var c in lower)
        {
            if (c == '-')
            {
                return false;
            }

            var vowel = IsVowel(c);
            if (vowel && !inGroup)
            {
                groups++;
            }

            inGroup = vowel;
        }

        return groups == 1;
    }

    /// <summary>
    ///     Determines whether a lower-case character is one of <c>a e i o u</c>.
    /// </summary>
    /// <param name="c">The character.</param>
    /// <returns><c>true</c> for a vowel letter.</returns>
    private static bool IsVowel(char c) => c == 'a' || c == 'e' || c == 'i' || c == 'o' || c == 'u';

    /// <summary>
    ///     Determines whether a lower-case character is a consonant letter (<c>y</c> counts as a consonant).
    /// </summary>
    /// <param name="c">The character.</param>
    /// <returns><c>true</c> for a letter that is not a vowel.</returns>
    private static bool IsConsonant(char c) => c >= 'a' && c <= 'z' && !IsVowel(c);

    /// <summary>
    ///     Determines whether a character is an ASCII letter.
    /// </summary>
    /// <param name="c">The character.</param>
    /// <returns><c>true</c> for A to Z and a to z.</returns>
    private static bool IsAsciiLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
}
