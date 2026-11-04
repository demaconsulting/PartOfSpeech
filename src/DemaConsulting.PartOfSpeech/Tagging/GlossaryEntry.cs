// cspell:ignore Contoso
namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     One domain term of a <see cref="Glossary"/>: the words of the term, the tags that its head word may take and an
///     optional definition.
/// </summary>
/// <remarks>
///     <para>
///         An entry says "when the words of this term occur in a sentence, the head word of the term has one of these
///         tags". The <strong>head</strong> is the word of the term that takes the tags of the entry. By default it is
///         the last word (<c>Contoso Hub</c> has the head <c>Hub</c>, <c>bleed valve</c> has <c>valve</c>); a phrasal
///         verb has its head on the first word (<c>shut down</c> has the head <c>shut</c>). The term may be a single
///         word (<c>torque</c>) or several words (<c>bleed valve</c>). The exact rule for the phrasal verb: when the
///         tags include <see cref="Upos.Verb"/>, the term has
///         at least two words and every word after the first is one of a small closed list of particle words (<c>about</c>,
///         <c>across</c>, <c>ahead</c>, <c>along</c>, <c>apart</c>, <c>around</c>, <c>aside</c>, <c>away</c>,
///         <c>back</c>, <c>by</c>, <c>down</c>, <c>forth</c>, <c>forward</c>, <c>in</c>, <c>off</c>, <c>on</c>,
///         <c>onto</c>, <c>out</c>, <c>over</c>, <c>through</c>, <c>together</c>, <c>up</c>, <c>upon</c>,
///         <c>within</c>) that the lexicon of the library also allows as adposition, adverb or particle, the head word
///         is the first word. So <c>shut down</c>, <c>carry out</c> and <c>set up</c> have the head <c>shut</c>,
///         <c>carry</c> and <c>set</c>, while <c>bleed valve</c>, <c>take care</c> and <c>shut down valve</c> keep the
///         last word, and so do <c>look after</c> (<c>after</c> is not in the list) and <c>look forward to</c>
///         (<c>to</c> is not in the list): for these, <see cref="WithHead(int)"/> sets the head explicitly and
///         overrides the default in both directions. The other words of a term
///         are not
///         given the entry's tags, but they are kept from being read as verbs when the entry is a noun (see
///         <see cref="Glossary"/>), and <see cref="WithTokenTags(int, UposSet)"/> sets their tags explicitly (for the
///         product name <c>Contoso Hub</c>, <c>Contoso</c> is then a proper noun too).
///     </para>
///     <para>
///         The term is split into tokens by the same tokenizer that splits the text, so that the tokens of a term and
///         the tokens of a sentence agree: <c>bleed-valve</c> is one token and does not match the text
///         <c>bleed valve</c>, a contraction such as <c>don't</c> becomes the two tokens <c>do</c> and <c>n't</c>, and
///         punctuation inside a word (<c>3.5</c>, <c>R&amp;D</c>, <c>and/or</c>) is treated as in running text. See
///         <see cref="Tokens"/> and <see cref="Glossary"/>.
///     </para>
///     <para>
///         An entry is immutable: the <c>With...</c> methods return a changed copy and leave the original untouched, so
///         an entry can be shared between glossaries and threads. The class has no public setters because a glossary
///         must not change after it was built. The definition is carried through to the results (see
///         <see cref="GlossaryMatch.Entry"/>) and is never used for tagging.
///     </para>
///     <para>
///         <strong>Thread safety:</strong> instances are immutable and may be used from any number of threads.
///     </para>
/// </remarks>
/// <example>
///     Describe a technical name, a multiword compound with a definition, a case-sensitive acronym and a verb with an
///     irregular past tense; the <c>With...</c> methods chain because each returns a new entry:
///     <code>
///     var valve = new GlossaryEntry("bleed valve", Upos.Noun, "A valve that releases trapped air.");
///     var acronym = new GlossaryEntry("LRU", Upos.Noun).WithCaseSensitive(true);
///     var bleed = new GlossaryEntry("bleed", UposSet.Of(Upos.Verb, Upos.Noun))
///         .WithVariants("bled");
///     var shutDown = new GlossaryEntry("shut down", Upos.Verb);
///     var takeCare = new GlossaryEntry("take care", Upos.Verb);
///
///     Console.WriteLine(string.Join(" | ", valve.Tokens));   // bleed | valve
///     Console.WriteLine(valve.HeadIndex);                     // 1
///     Console.WriteLine(shutDown.HeadIndex);                  // 0 (inferred: down is a listed particle word)
///     Console.WriteLine(takeCare.HeadIndex);                  // 1 (care is not a particle word)
///     </code>
/// </example>
public sealed class GlossaryEntry
{
    /// <summary>
    ///     The term as the tokenizer splits it in running text.
    /// </summary>
    private readonly string[] _tokens;

    /// <summary>
    ///     The read-only view of the tokens that callers see.
    /// </summary>
    private readonly IReadOnlyList<string> _tokenView;

    /// <summary>
    ///     The term as the tokenizer splits it at the end of a text, or <c>null</c> when that is the same as <see cref="_tokens"/>.
    /// </summary>
    private readonly string[]? _endTokens;

    /// <summary>
    ///     The explicit variants as written.
    /// </summary>
    private readonly string[] _variants;

    /// <summary>
    ///     The read-only view of the variants that callers see.
    /// </summary>
    private readonly IReadOnlyList<string> _variantView;

    /// <summary>
    ///     The tokens of each variant in running text.
    /// </summary>
    private readonly string[][] _variantTokens;

    /// <summary>
    ///     The tokens of each variant at the end of a text, or <c>null</c> for a variant that does not differ.
    /// </summary>
    private readonly string[]?[] _variantEndTokens;

    /// <summary>
    ///     The explicit tags of the non-head tokens, indexed by token, or <c>null</c> when there are none.
    /// </summary>
    private readonly UposSet?[]? _tokenTags;

    /// <summary>
    ///     Initializes a new instance of the <see cref="GlossaryEntry"/> class for a term with one tag.
    /// </summary>
    /// <param name="term">The term: one word or several words separated by white space. It must contain at least one token with a letter or a digit.</param>
    /// <param name="tag">The tag that the head word of the term takes.</param>
    /// <param name="definition">An optional definition that is carried through to the results; it never influences the tagging. <c>null</c> for none.</param>
    /// <exception cref="ArgumentNullException"><paramref name="term"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="term"/> is empty or has no token with a letter or a digit.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tag"/> is not one of the 17 defined values.</exception>
    public GlossaryEntry(string term, Upos tag, string? definition = null)
        : this(term, UposSet.Of(tag), definition)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="GlossaryEntry"/> class for a term with several allowed tags.
    /// </summary>
    /// <remarks>
    ///     With several tags the head word is limited to those tags (hard mode) and the model chooses among them, so a
    ///     word that is a noun and a verb in your domain can list both and still be kept from becoming an adjective.
    /// </remarks>
    /// <param name="term">The term: one word or several words separated by white space. It must contain at least one token with a letter or a digit.</param>
    /// <param name="tags">The tags that the head word of the term may take; at least one.</param>
    /// <param name="definition">An optional definition that is carried through to the results; it never influences the tagging. <c>null</c> for none.</param>
    /// <exception cref="ArgumentNullException"><paramref name="term"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="term"/> is empty or has no token with a letter or a digit, or <paramref name="tags"/> is empty.</exception>
    public GlossaryEntry(string term, UposSet tags, string? definition = null)
    {
        ArgumentNullException.ThrowIfNull(term);
        if (tags.IsEmpty)
        {
            throw new ArgumentException("The entry needs at least one tag.", nameof(tags));
        }

        // The term is split exactly like running text, so that term and sentence agree on what a token is
        _tokens = TokenizeTerm(term, nameof(term), out _endTokens);
        _tokenView = Array.AsReadOnly(_tokens);
        Term = term;
        Tags = tags;
        Definition = definition;
        HeadIndex = DefaultHead(_tokens, tags);
        _variants = new string[0];
        _variantView = Array.AsReadOnly(_variants);
        _variantTokens = new string[0][];
        _variantEndTokens = new string[]?[0];
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="GlossaryEntry"/> class as a copy of another entry (the base of the <c>With...</c> methods).
    /// </summary>
    /// <param name="other">The entry to copy.</param>
    /// <param name="head">The head index of the copy.</param>
    /// <param name="definition">The definition of the copy.</param>
    /// <param name="caseSensitive">The case rule of the copy.</param>
    /// <param name="mode">The mode of the copy.</param>
    /// <param name="inflect">The inflection rule of the copy.</param>
    /// <param name="variants">The variants of the copy as written.</param>
    /// <param name="variantTokens">The tokens of the variants in running text.</param>
    /// <param name="variantEndTokens">The tokens of the variants at the end of a text.</param>
    /// <param name="tokenTags">The explicit non-head tags of the copy.</param>
    private GlossaryEntry(
        GlossaryEntry other,
        int head,
        string? definition,
        bool? caseSensitive,
        GlossaryMode? mode,
        bool? inflect,
        string[] variants,
        string[][] variantTokens,
        string[]?[] variantEndTokens,
        UposSet?[]? tokenTags)
    {
        _tokens = other._tokens;
        _tokenView = other._tokenView;
        _endTokens = other._endTokens;
        Term = other.Term;
        Tags = other.Tags;
        HeadIndex = head;
        Definition = definition;
        CaseSensitive = caseSensitive;
        Mode = mode;
        Inflect = inflect;
        _variants = variants;
        _variantView = Array.AsReadOnly(variants);
        _variantTokens = variantTokens;
        _variantEndTokens = variantEndTokens;
        _tokenTags = tokenTags;
    }

    /// <summary>
    ///     Gets the term as it was supplied.
    /// </summary>
    public string Term { get; }

    /// <summary>
    ///     Gets the term as the tokenizer splits it in running text.
    /// </summary>
    /// <remarks>
    ///     This is the sequence that the glossary looks for. A term is split exactly like a text that continues after
    ///     it, so <c>bleed valve</c> gives two tokens, <c>well-known</c> and <c>COVID-19</c> stay one token each,
    ///     <c>don't</c> gives <c>do</c> and <c>n't</c>, and <c>U.S.</c> keeps its period. Matching is on whole tokens,
    ///     so <c>bleed-valve</c> (one token) does not match the text <c>bleed valve</c> (two tokens). Where a
    ///     term that ends in a period is split differently at the end of a text (<c>etc.</c> gives <c>etc</c> and
    ///     <c>.</c> there), the glossary matches that form too.
    /// </remarks>
    public IReadOnlyList<string> Tokens => _tokenView;

    /// <summary>
    ///     Gets the tags that the head word of the term may take.
    /// </summary>
    /// <value>Never empty.</value>
    public UposSet Tags { get; }

    /// <summary>
    ///     Gets the definition that was supplied, or <c>null</c>.
    /// </summary>
    /// <remarks>
    ///     The definition is carried through to the results (<see cref="GlossaryMatch.Entry"/>) so that an application
    ///     can show it; it is never used for tagging.
    /// </remarks>
    public string? Definition { get; }

    /// <summary>
    ///     Gets the index in <see cref="Tokens"/> of the head word, the token that receives the tags of the entry.
    /// </summary>
    /// <remarks>
    ///     The head is chosen when the entry is created and never changes except through <see cref="WithHead(int)"/>,
    ///     so it is the same in every glossary and every tagger that uses the entry. See the remarks of
    ///     <see cref="GlossaryEntry"/> for the default.
    /// </remarks>
    /// <value>The inferred head of the term (the first token of a phrasal verb with a listed particle word, otherwise the last token) unless <see cref="WithHead(int)"/> set it.</value>
    public int HeadIndex { get; }

    /// <summary>
    ///     Gets whether this entry is case-sensitive, or <c>null</c> when the glossary decides (<see cref="GlossaryOptions.CaseSensitive"/>).
    /// </summary>
    public bool? CaseSensitive { get; }

    /// <summary>
    ///     Gets the mode of this entry, or <c>null</c> when the glossary decides (<see cref="GlossaryOptions.Mode"/>).
    /// </summary>
    public GlossaryMode? Mode { get; }

    /// <summary>
    ///     Gets whether the inflected forms of the head word are matched, or <c>null</c> when the glossary decides (<see cref="GlossaryOptions.Inflect"/>).
    /// </summary>
    public bool? Inflect { get; }

    /// <summary>
    ///     Gets the explicit variant spellings of the whole term that were supplied with <see cref="WithVariants(string[])"/>.
    /// </summary>
    /// <remarks>
    ///     A variant that spells the term, or that was listed twice, is not in the list (see
    ///     <see cref="WithVariants(string[])"/>).
    /// </remarks>
    /// <value>Empty by default.</value>
    public IReadOnlyList<string> Variants => _variantView;

    /// <summary>
    ///     Gets the term as the tokenizer splits it at the end of a text when that differs from <see cref="Tokens"/>.
    /// </summary>
    internal string[]? EndTokens => _endTokens;

    /// <summary>
    ///     Gets the tokens of every variant in running text.
    /// </summary>
    internal string[][] VariantTokens => _variantTokens;

    /// <summary>
    ///     Gets the tokens of every variant at the end of a text (<c>null</c> for a variant that does not differ).
    /// </summary>
    internal string[]?[] VariantEndTokens => _variantEndTokens;

    /// <summary>
    ///     Gets the tokens of the term as an array (not copied; callers must not modify it).
    /// </summary>
    internal string[] TokenArray => _tokens;

    /// <summary>
    ///     Gets the explicit tags that were set for a non-head token.
    /// </summary>
    /// <remarks>
    ///     The head token has no override: its tags are <see cref="Tags"/>. An override that was set for a token and
    ///     then became the head by <see cref="WithHead(int)"/> is ignored while the token is the head.
    /// </remarks>
    /// <param name="tokenIndex">The index in <see cref="Tokens"/>.</param>
    /// <returns>The tags set with <see cref="WithTokenTags(int, UposSet)"/>, or <c>null</c> when none were set or the token is the head.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tokenIndex"/> is negative or not less than the number of tokens.</exception>
    public UposSet? GetTokenTags(int tokenIndex)
    {
        if (tokenIndex < 0 || tokenIndex >= _tokens.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(tokenIndex), tokenIndex, "The token index is outside the tokens of the term.");
        }

        return tokenIndex == HeadIndex || _tokenTags == null ? null : _tokenTags[tokenIndex];
    }

    /// <summary>
    ///     Gets a copy of the entry with another definition.
    /// </summary>
    /// <param name="definition">The definition, or <c>null</c> for none.</param>
    /// <returns>The new entry; this entry is unchanged.</returns>
    public GlossaryEntry WithDefinition(string? definition) =>
        new(this, HeadIndex, definition, CaseSensitive, Mode, Inflect, _variants, _variantTokens, _variantEndTokens, _tokenTags);

    /// <summary>
    ///     Gets a copy of the entry whose head word is another token of the term.
    /// </summary>
    /// <remarks>
    ///     In plain words: use it to say which word of the term takes the tags of the entry. The head word receives the
    ///     tags of the entry and its inflected forms are matched. The call overrides the
    ///     default head in both directions, also <c>WithHead(last)</c>: without a call, the head is the first word of a
    ///     phrasal verb (the tags include <see cref="Upos.Verb"/>, the term has at least two words, and every word after
    ///     the first is a word of the closed particle list, see <see cref="GlossaryEntry"/>, that the lexicon allows as
    ///     adposition, adverb or particle, as for <c>shut down</c>) and the last word of every other term. Use the call
    ///     for a phrasal verb with a particle that is not in the list (<c>look after</c>: without the call, the head is
    ///     <c>after</c> and a hard entry forces it to be a verb), for a phrasal verb of three words whose last word is
    ///     not in the list (<c>look forward to</c>), or to keep the last word as the head of a term
    ///     that the rule takes for a phrasal verb. The rule that limits the particle applies only to a two-word verb
    ///     term whose head is the first word; the other words of a longer term are left to the model, and
    ///     <see cref="WithTokenTags(int, UposSet)"/> sets them.
    /// </remarks>
    /// <param name="tokenIndex">The index in <see cref="Tokens"/> of the head word.</param>
    /// <returns>The new entry; this entry is unchanged.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tokenIndex"/> is negative or not less than the number of tokens.</exception>
    /// <example>
    ///     Make the verb the head of a phrasal verb whose particle is not in the list of particle words:
    ///     <code>
    ///     var lookAfter = new GlossaryEntry("look after", Upos.Verb);
    ///     Console.WriteLine(lookAfter.HeadIndex);                  // 1 (after is the last word)
    ///     Console.WriteLine(lookAfter.WithHead(0).HeadIndex);      // 0 (look is the head)
    ///     </code>
    /// </example>
    public GlossaryEntry WithHead(int tokenIndex)
    {
        if (tokenIndex < 0 || tokenIndex >= _tokens.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(tokenIndex), tokenIndex, "The head index is outside the tokens of the term.");
        }

        return new(this, tokenIndex, Definition, CaseSensitive, Mode, Inflect, _variants, _variantTokens, _variantEndTokens, _tokenTags);
    }

    /// <summary>
    ///     Gets a copy of the entry with its own case rule instead of the glossary's.
    /// </summary>
    /// <param name="caseSensitive"><c>true</c> to match the exact letter case (for an acronym), <c>false</c> to ignore it.</param>
    /// <returns>The new entry; this entry is unchanged.</returns>
    public GlossaryEntry WithCaseSensitive(bool caseSensitive) =>
        new(this, HeadIndex, Definition, caseSensitive, Mode, Inflect, _variants, _variantTokens, _variantEndTokens, _tokenTags);

    /// <summary>
    ///     Gets a copy of the entry with its own mode instead of the glossary's.
    /// </summary>
    /// <param name="mode">The mode of this entry.</param>
    /// <returns>The new entry; this entry is unchanged.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mode"/> is not <see cref="GlossaryMode.Hard"/> or <see cref="GlossaryMode.Soft"/>.</exception>
    public GlossaryEntry WithMode(GlossaryMode mode)
    {
        if (mode != GlossaryMode.Hard && mode != GlossaryMode.Soft)
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "The mode must be Hard or Soft.");
        }

        return new(this, HeadIndex, Definition, CaseSensitive, mode, Inflect, _variants, _variantTokens, _variantEndTokens, _tokenTags);
    }

    /// <summary>
    ///     Gets a copy of the entry that switches the matching of inflected forms on or off for this entry.
    /// </summary>
    /// <remarks>
    ///     In plain words: with inflection on (the default of the glossary), the regular plural and verb forms of the head
    ///     word match too (<c>valves</c>, <c>isolated</c>); with it off, only the term and its variants match.
    /// </remarks>
    /// <param name="inflect"><c>true</c> to match the generated forms of the head word, <c>false</c> to match only the term and its variants.</param>
    /// <returns>The new entry; this entry is unchanged.</returns>
    /// <example>
    ///     Match only the exact word <c>tap</c>, not its plural:
    ///     <code>
    ///     Glossary glossary = Glossary.Create(new[] { new GlossaryEntry("tap", Upos.Noun).WithInflection(false) });
    ///
    ///     Console.WriteLine(glossary.FindMatches(Tokenizer.Tokenize("Open the tap.")[0]).Count);    // 1
    ///     Console.WriteLine(glossary.FindMatches(Tokenizer.Tokenize("Open the taps.")[0]).Count);   // 0
    ///     </code>
    /// </example>
    public GlossaryEntry WithInflection(bool inflect) =>
        new(this, HeadIndex, Definition, CaseSensitive, Mode, inflect, _variants, _variantTokens, _variantEndTokens, _tokenTags);

    /// <summary>
    ///     Gets a copy of the entry with explicit variant spellings of the whole term, for irregular forms and other names.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         In plain words: a variant is another spelling of the whole term that has the same number of words, such as
    ///         <c>bled</c> for <c>bleed</c> or <c>relief valve</c> for <c>release valve</c>. Use variants for forms that
    ///         the regular inflection rules do not produce (an irregular past tense, a spelling variant, an alternative
    ///         name). Each variant is split into tokens like the term and must give the
    ///         same number of tokens; it carries the full tag set of the entry on its head word. The variants replace any
    ///         variants that the entry already had. A variant matches with the same case rule as the entry and is reported
    ///         as a variant (<see cref="GlossaryMatch.IsVariant"/>).
    ///     </para>
    ///     <para>
    ///         <strong>No variant is needed for an irregular form that is identical to the lemma</strong>
    ///         (<c>set</c>, <c>cut</c>, <c>put</c>: the past tense of <c>set</c> is <c>set</c>): the term itself already
    ///         matches it. <strong>Redundant variants are ignored without an error</strong>: a variant that spells the
    ///         term (the same words in the same letter case) and a variant that is listed twice are dropped, so
    ///         <see cref="Variants"/> contains each extra spelling once, in the order given, and the match of the term is
    ///         reported as the term, not as a variant. A variant that spells a form that the inflection rules would also
    ///         generate (<c>bleeds</c>) is kept and wins over the generated form, so it carries the full tag set of the
    ///         entry instead of the tags derived for the form. A variant that differs from the term only in letter case is
    ///         kept but adds nothing while the entry is case-insensitive. An invalid variant still throws, also when it would have
    ///         been redundant.
    ///     </para>
    /// </remarks>
    /// <param name="variants">The variant spellings of the whole term; none may be <c>null</c> or empty.</param>
    /// <returns>The new entry; this entry is unchanged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="variants"/> or one of its items is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">A variant is empty, has no token with a letter or a digit, or gives another number of tokens than the term.</exception>
    /// <example>
    ///     List an irregular past tense and an alternative name; a variant that repeats the term or an earlier variant is
    ///     ignored:
    ///     <code>
    ///     var bleed = new GlossaryEntry("bleed", Upos.Verb).WithVariants("bled");
    ///     var again = bleed.WithVariants("bleed", "bled", "bled");
    ///     Console.WriteLine(string.Join(", ", again.Variants));   // bled
    ///
    ///     var valve = new GlossaryEntry("release valve", Upos.Noun).WithVariants("relief valve");
    ///     Console.WriteLine(valve.Variants.Count);                // 1
    ///     </code>
    /// </example>
    public GlossaryEntry WithVariants(params string[] variants)
    {
        ArgumentNullException.ThrowIfNull(variants);

        // Validate and split every variant like the term; an invalid variant must fail here, not inside the matcher.
        // A variant that spells the term or an earlier variant adds nothing and is dropped (the matcher would ignore it anyway)
        var copy = new List<string>(variants.Length);
        var tokens = new List<string[]>(variants.Length);
        var endTokens = new List<string[]?>(variants.Length);
        foreach (var variant in variants)
        {
            if (variant == null)
            {
                throw new ArgumentNullException(nameof(variants), "A variant is null.");
            }

            var split = TokenizeTerm(variant, nameof(variants), out var end);
            if (split.Length != _tokens.Length)
            {
                throw new ArgumentException(
                    $"The variant '{variant}' has {split.Length} token(s) but the term has {_tokens.Length}.",
                    nameof(variants));
            }

            if (split.SequenceEqual(_tokens, StringComparer.Ordinal) || tokens.Any(known => known.SequenceEqual(split, StringComparer.Ordinal)))
            {
                continue;
            }

            copy.Add(variant);
            tokens.Add(split);
            endTokens.Add(end);
        }

        return new(this, HeadIndex, Definition, CaseSensitive, Mode, Inflect, copy.ToArray(), tokens.ToArray(), endTokens.ToArray(), _tokenTags);
    }

    /// <summary>
    ///     Gets a copy of the entry with explicit tags for one non-head token of the term.
    /// </summary>
    /// <remarks>
    ///     In plain words: use it to say what one of the other words of the term is. Without it a non-head token of a noun term is only kept from being read as a verb or an auxiliary, the
    ///     second token of a two-token verb term whose head is the first word (the particle of a phrasal verb, see
    ///     <see cref="WithHead(int)"/>) is limited to adposition, adverb and particle (this rule was chosen on
    ///     development data, see section E9 of
    ///     https://github.com/demaconsulting/PartOfSpeech/blob/main/tools/DemaConsulting.PartOfSpeech.Trainer/RUN-LOG.md), and every other token (for example
    ///     <c>it</c> in <c>take it apart</c>) is left to the
    ///     model. Use it to say exactly what a word of the term is, for
    ///     example that <c>up</c> in the phrasal verb <c>set up</c> is an adverb: <c>new GlossaryEntry("set up",
    ///     Upos.Verb).WithTokenTags(1, UposSet.Of(Upos.Adv, Upos.Adp))</c>, or that the first word of the product name
    ///     <c>Contoso Hub</c>, which the model has never seen, is a proper noun like the head. The tags are applied in the
    ///     mode of the entry. A later call for the same token replaces the earlier one.
    /// </remarks>
    /// <param name="tokenIndex">The index in <see cref="Tokens"/> of a token that is not the head.</param>
    /// <param name="tags">The tags that the token may take; at least one.</param>
    /// <returns>The new entry; this entry is unchanged.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tokenIndex"/> is negative or not less than the number of tokens.</exception>
    /// <exception cref="ArgumentException"><paramref name="tokenIndex"/> is the head token (use the tags of the entry) or <paramref name="tags"/> is empty.</exception>
    /// <example>
    ///     Say that both words of a product name are proper nouns (the head <c>Hub</c> takes the tags of the entry, the
    ///     other word is set with this method):
    ///     <code>
    ///     var hub = new GlossaryEntry("Contoso Hub", Upos.Propn).WithTokenTags(0, UposSet.Of(Upos.Propn));
    ///
    ///     Console.WriteLine(hub.HeadIndex);                               // 1
    ///     Console.WriteLine(hub.GetTokenTags(0)?.Contains(Upos.Propn));   // True
    ///     Console.WriteLine(hub.GetTokenTags(1) is null);                 // True (the head has the tags of the entry)
    ///     </code>
    /// </example>
    public GlossaryEntry WithTokenTags(int tokenIndex, UposSet tags)
    {
        if (tokenIndex < 0 || tokenIndex >= _tokens.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(tokenIndex), tokenIndex, "The token index is outside the tokens of the term.");
        }

        if (tokenIndex == HeadIndex)
        {
            throw new ArgumentException("The head token takes the tags of the entry; use the Tags of the entry instead.", nameof(tokenIndex));
        }

        if (tags.IsEmpty)
        {
            throw new ArgumentException("The tags of a token must not be empty.", nameof(tags));
        }

        var copy = new UposSet?[_tokens.Length];
        if (_tokenTags != null)
        {
            Array.Copy(_tokenTags, copy, copy.Length);
        }

        copy[tokenIndex] = tags;
        return new(this, HeadIndex, Definition, CaseSensitive, Mode, Inflect, _variants, _variantTokens, _variantEndTokens, copy);
    }

    /// <summary>
    ///     Formats the entry for diagnostics.
    /// </summary>
    /// <returns>For example <c>bleed valve [NOUN]</c>.</returns>
    public override string ToString() => $"{Term} [{Tags}]";

    /// <summary>
    ///     Gets the key that makes two entries duplicates: the same folded token sequence under the same effective case rule.
    /// </summary>
    /// <remarks>
    ///     A case-insensitive key ignores the letter case; a case-sensitive key keeps it, so <c>IT</c> and <c>It</c> are
    ///     different case-sensitive entries while <c>it</c> and <c>IT</c> are the same case-insensitive entry.
    ///     Each token is written with its length so that no token sequence can be mistaken for another.
    /// </remarks>
    /// <param name="caseSensitive">The effective case rule of the entry.</param>
    /// <returns>The key.</returns>
    internal string IdentityKey(bool caseSensitive)
    {
        var builder = new System.Text.StringBuilder(caseSensitive ? "s:" : "i:");
        foreach (var token in _tokens)
        {
            var folded = GlossaryIndex.FoldToString(token, !caseSensitive);
            builder.Append(folded.Length).Append(':').Append(folded);
        }

        return builder.ToString();
    }

    /// <summary>
    ///     Splits a term or variant into tokens exactly like running text and validates it.
    /// </summary>
    /// <remarks>
    ///     The text is tokenized followed by a sentinel word that is dropped again, so that a final period that the
    ///     tokenizer keeps in the middle of a text (<c>U.S.</c>, <c>etc.</c>) is kept here too. When the text alone
    ///     (as the end of a text) splits differently, that second split is returned as <paramref name="endTokens"/>.
    /// </remarks>
    /// <param name="text">The term or variant.</param>
    /// <param name="parameterName">The parameter name for error messages.</param>
    /// <param name="endTokens">Receives the tokens at the end of a text when they differ from the result; otherwise <c>null</c>.</param>
    /// <returns>The tokens in running text.</returns>
    /// <exception cref="ArgumentException">The text has no token with a letter or a digit.</exception>
    private static string[] TokenizeTerm(string text, string parameterName, out string[]? endTokens)
    {
        // Running text: the sentinel keeps the chunk of the term from being the last chunk of the text
        var withSentinel = Tokenizer.TokenizeWords(text + " x");
        var middle = new string[Math.Max(0, withSentinel.Count - 1)];
        var hasWord = false;
        for (var i = 0; i < middle.Length; i++)
        {
            middle[i] = withSentinel[i].Text;
            hasWord |= ContainsLetterOrDigit(middle[i]);
        }

        if (!hasWord)
        {
            throw new ArgumentException("The term must contain at least one token with a letter or a digit.", parameterName);
        }

        // The end of a text: only a term that ends in a period can split differently
        var alone = Tokenizer.TokenizeWords(text);
        endTokens = null;
        if (alone.Count != middle.Length || !alone.Select(token => token.Text).SequenceEqual(middle, StringComparer.Ordinal))
        {
            endTokens = alone.Select(token => token.Text).ToArray();
        }

        return middle;
    }

    /// <summary>
    ///     The closed list of particle words that make a verb term a phrasal verb with its head on the first word.
    /// </summary>
    /// <remarks>
    ///     The list is a set of candidates in lower case; a word of it counts only when the lexicon also allows it as
    ///     adposition, adverb or particle. It is deliberately modest (the words that follow a verb in <c>shut down</c>,
    ///     <c>carry out</c>, <c>set up</c>, <c>switch off</c>) and has no word that is mainly a noun, so that
    ///     <c>take care</c> and <c>make sense</c> keep the last word as the head. The set is never changed after the
    ///     type is initialized, so reading it from several threads is safe.
    /// </remarks>
    private static readonly HashSet<string> ParticleWords = new(StringComparer.Ordinal)
    {
        "about", "across", "ahead", "along", "apart", "around", "aside", "away", "back", "by", "down", "forth",
        "forward", "in", "off", "on", "onto", "out", "over", "through", "together", "up", "upon", "within",
    };

    /// <summary>
    ///     Chooses the head of a term when no head is set explicitly.
    /// </summary>
    /// <remarks>
    ///     The head is the first token when the tags include <see cref="Upos.Verb"/>, the term has at least two tokens
    ///     and every token after the first is a particle word of the closed list <see cref="ParticleWords"/> that the
    ///     lexicon also allows as adposition, adverb or particle (the allowed set, as the tagger computes it, contains
    ///     at least one of these three tags; other tags of the word do not matter, so <c>down</c> and <c>out</c>
    ///     qualify); otherwise it is the last
    ///     token. The lexicon is the one embedded in the library (<see cref="EmbeddedModel.Lexicon"/>), because a
    ///     glossary does not belong to a tagger and its index needs the head before any tagger is known (the head decides
    ///     which word is inflected). The embedded lexicon is immutable and loaded once under a lock, and the lookup uses
    ///     no shared scratch memory, so the result is deterministic and the method is thread-safe. A term that does not
    ///     need the lexicon (one token, no verb tag) never loads it.
    /// </remarks>
    /// <param name="tokens">The tokens of the term.</param>
    /// <param name="tags">The tags of the entry.</param>
    /// <returns>The index of the head token.</returns>
    private static int DefaultHead(string[] tokens, UposSet tags)
    {
        var last = tokens.Length - 1;
        if (last < 1 || !tags.Contains(Upos.Verb))
        {
            return last;
        }

        // The key is the folded, lower-cased token, as the tagger looks it up, so the letter case of the term does not matter
        var lexicon = EmbeddedModel.Lexicon;
        var particles = UposSet.Of(Upos.Adp, Upos.Adv, Upos.Part);
        for (var k = 1; k <= last; k++)
        {
            var folded = GlossaryIndex.FoldToString(tokens[k], true);
            if (!ParticleWords.Contains(folded) || Lexicon.AllowedTags(folded, lexicon).Intersect(particles).IsEmpty)
            {
                return last;
            }
        }

        return 0;
    }

    /// <summary>
    ///     Determines whether a token contains a letter or a digit.
    /// </summary>
    /// <param name="token">The token.</param>
    /// <returns><c>true</c> if at least one character is a letter or a digit.</returns>
    private static bool ContainsLetterOrDigit(string token) => token.Any(char.IsLetterOrDigit);
}
