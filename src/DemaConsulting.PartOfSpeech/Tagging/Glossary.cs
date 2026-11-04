// cspell:ignore Contoso
using System.Collections.ObjectModel;

namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     An immutable collection of domain terms that the tagger must tag correctly: single words and multiword terms,
///     each with the tags its head word may take and an optional definition.
/// </summary>
/// <remarks>
///     <para>
///         <strong>Why it exists:</strong> a general tagger reads <c>bleed valve</c> or <c>torque wrench</c> as a verb
///         followed by a noun because <c>bleed</c> and <c>torque</c> are also verbs. In a technical text that confident
///         wrong tag is the costly error, and the glossary of the domain already knows the right answer. A glossary lets
///         you state it once; the tagger then finds the terms in every sentence and applies them as per-token
///         constraints (see <see cref="TokenConstraint"/>), so the tags, the confidences and the tags of the
///         neighboring tokens all follow. The words used here (constraint, raw score, temperature, softmax) are
///         defined in [Terminology](../DemaConsulting.PartOfSpeech.md#terminology).
///     </para>
///     <para>
///         <strong>Using it:</strong> collect entries with a <see cref="GlossaryBuilder"/> (or give them to
///         <see cref="Create(IEnumerable{GlossaryEntry}, GlossaryOptions?)"/>), then pass the glossary to
///         <see cref="Tagger.Tag(string, Glossary)"/>, <see cref="Tagger.Tag(Sentence, Glossary, IReadOnlyList{TokenConstraint}?)"/>
///         or <see cref="Tagger.Tag(IReadOnlyList{string}, Glossary, IReadOnlyList{TokenConstraint}?)"/>. The glossary
///         belongs to the call, not to the tagger, so one shared tagger (including <see cref="Tagger.Default"/>) can serve
///         any number of glossaries. The tagged tokens report their match (<see cref="TaggedToken.GlossaryMatch"/>) and the
///         sentence lists all matches (<see cref="TaggedSentence.GlossaryMatches"/>); <see cref="FindMatches(Sentence)"/> finds
///         the terms without tagging, for example to show their definitions.
///     </para>
///     <para>
///         <strong>Matching:</strong> a sentence is searched left to right. At each position the longest term that
///         matches wins, the search continues after it, and matches never overlap (<c>bleed valve</c> beats <c>valve</c>).
///         A term matches when its tokens equal the tokens of the sentence one by one. The term is split by the same
///         tokenizer as the text (see <see cref="GlossaryEntry.Tokens"/>), which has consequences: <c>bleed-valve</c>
///         is one token and does not match the text <c>bleed valve</c> (add both spellings as two entries; a variant
///         cannot do it, because a variant must have the same number of tokens as the term); a contraction in a term is split like in text
///         (<c>don't</c> gives <c>do</c> and <c>n't</c>), so give such a term a head with
///         <see cref="GlossaryEntry.WithHead(int)"/>; punctuation inside a word (<c>3.5</c>, <c>R&amp;D</c>,
///         <c>and/or</c>) is handled as in text; a term never matches across two sentences; and tokens that you pass
///         in pre-tokenized form must follow the tokenizer's convention, so <c>valve.</c> (a period stuck to the word)
///         does not match <c>valve</c>. Typographic apostrophes, quotes, dashes and the ellipsis character are
///         matched like their ASCII forms, and text is compared after Unicode normalization.
///     </para>
///     <para>
///         <strong>Letter case:</strong> terms are case-insensitive by default, because a term at the start of a sentence
///         is capitalized. <see cref="GlossaryOptions.CaseSensitive"/> changes the default and
///         <see cref="GlossaryEntry.WithCaseSensitive(bool)"/> overrides it for one entry, which an acronym needs: the
///         case-sensitive entry <c>IT</c> leaves the pronoun <c>it</c> alone. A case-sensitive and a case-insensitive
///         entry for the same term may coexist; where both match, the case-sensitive one wins.
///     </para>
///     <para>
///         <strong>Inflection:</strong> a glossary lists lemmas, text has inflected words, so the forms of the <em>head
///         word</em> are matched too (switch off with <see cref="GlossaryOptions.Inflect"/> or
///         <see cref="GlossaryEntry.WithInflection(bool)"/>). The rules are regular English only. For a noun entry the
///         plural: <c>s</c>, <c>x</c>, <c>z</c>, <c>ch</c>, <c>sh</c> take <c>es</c> (<c>box</c>, <c>boxes</c>), a consonant
///         and <c>y</c> become <c>ies</c> (<c>battery</c>, <c>batteries</c>), otherwise <c>s</c>; an all-capital acronym takes
///         a lower-case <c>s</c> (<c>LRU</c>, <c>LRUs</c>). For a verb entry the third person with the same rule, the past form
///         (<c>isolate</c>, <c>isolated</c>; <c>carry</c>, <c>carried</c>; <c>stop</c>, <c>stopped</c> for a short word that ends
///         in consonant, vowel, consonant) and the present participle (<c>isolating</c>, <c>carrying</c>, <c>stopping</c>,
///         <c>tying</c>). A generated form gets derived tags: a plural is a noun, a third-person form a verb, a past form
///         a verb or an adjective (<c>an isolated valve</c>), a present participle a verb, an adjective or a noun. Proper
///         nouns and adjectives are not inflected. Irregular forms (<c>bled</c>, <c>leaves</c>) and the doubling of
///         longer words (<c>permitted</c>) are not generated; list them with
///         <see cref="GlossaryEntry.WithVariants(string[])"/>. An irregular form that is identical to the lemma
///         (<c>set</c>, <c>cut</c>, <c>put</c>) needs no variant, because the term itself already matches it. A term as
///         written always wins over a listed variant, and a
///         variant over a generated form; a variant that repeats the term or another variant is ignored without an error.
///     </para>
///     <para>
///         <strong>What a match does:</strong> in <see cref="GlossaryMode.Hard"/> mode (the default) the head word of the
///         term can only take the tags of the entry. Hard mode trusts the entries completely, so a wrong entry forces a
///         wrong tag; it fits curated lists such as the ASD-STE100 technical names and verbs. Soft mode is for lists that are not curated or
///         may be wrong: in <see cref="GlossaryMode.Soft"/> mode those tags get a boost of
///         <see cref="GlossaryOptions.SoftStrength"/> times the model temperature, so the model can still disagree when it
///         is sure. With wrong entries, hard mode makes confident errors, while soft mode stays near the accuracy of tagging
///         without a glossary; the full measurements are in section E9 of
///         https://github.com/demaconsulting/PartOfSpeech/blob/main/tools/DemaConsulting.PartOfSpeech.Trainer/RUN-LOG.md. The other words of a noun term (<c>bleed</c> in
///         <c>bleed valve</c>) can no longer be a verb or an
///         auxiliary in hard mode, and are penalized for these tags in soft mode; the second word of a two-word verb term
///         whose head is the first word (the particle of a phrasal verb such as <c>shut down</c>; see
///         <see cref="GlossaryEntry"/> for how the head is chosen and <see cref="GlossaryEntry.WithHead(int)"/> for
///         setting it) is limited to adposition, adverb and particle in hard mode and
///         boosted for these tags in soft mode, and every other word (for example <c>it</c> in <c>take it apart</c>) is
///         left to the model, unless you set their tags with
///         <see cref="GlossaryEntry.WithTokenTags(int, UposSet)"/>. The tagged token reports
///         <see cref="TaggedToken.IsConstrained"/>. The tag that is
///         chosen becomes history for the tokens after it like any other decision, so the tags of the neighbors after a
///         match can change; the tokens before the first match of a sentence never change.
///     </para>
///     <para>
///         <strong>Confidence:</strong> the confidence of a glossary token measures the choice among the tags that are
///         still allowed after the constraints (a softmax over those tags, see
///         [Confidence and alternatives](../DemaConsulting.PartOfSpeech.md#confidence-and-alternatives)). It is <em>not</em>
///         evidence that the entry is correct: a hard entry that leaves one tag always gives the confidence 1.0 and no
///         alternatives, also when the entry is wrong, and a head word with several tags or in soft mode gets the softmax
///         over the tags that remain, where a soft boost raises the confidence of a preferred tag. Do not use the
///         confidence of a glossary head to judge the glossary.
///     </para>
///     <para>
///         <strong>Combining with your own constraints:</strong> the tagging methods that take a glossary also take your
///         per-token constraints (the <c>constraints</c> parameter, one per token). The two sources are combined per token:
///         when you give no constraint for a token (<see cref="TokenConstraint.None"/>), the constraint of the glossary
///         applies, hard or soft; when the glossary has no constraint for the token (it is not part of a match, or the entry
///         leaves it to the model), yours applies; when both exist they are combined with
///         <see cref="TokenConstraint.And(TokenConstraint)"/>. If the allowed tags of the two have no tag in common (for
///         example you allow only <c>VERB</c> for a head word that a hard entry limits to <c>NOUN</c>), your constraint
///         wins and the glossary constraint is dropped for that token, because your constraint is explicit and per call,
///         while the glossary is the default for all calls. A soft glossary constraint allows every tag, so it never
///         conflicts; it adds its boost to your constraint.
///     </para>
///     <para>
///         <strong>Cost:</strong> building a glossary tokenizes every term and creates its inflected forms once, and is
///         linear in the number of entries (see the section Costs of the namespace documentation for the order of
///         magnitude). A tagger compiles the constraints of a glossary the first time that a sentence contains a match of
///         it, keeps them for as long as the glossary object is alive, and reuses them for every later call; a new
///         glossary object, also one built from the same entries, is compiled again, so create it once and keep it. Matching
///         then costs at most the length of the longest term per token, whatever the size of the glossary, and does not
///         allocate per token for ordinary text; a token with an unusual Unicode character (anything beyond Latin-1 other than
///         the typographic quotes, dashes and the ellipsis) is normalized on a slower path that allocates. Tagging a
///         sentence with a glossary allocates nothing extra when the sentence has no match, once the per-thread matching
///         workspace has grown to the sentence length (the first use and longer sentences grow it); a sentence with matches
///         allocates one array sized by its token count (one small value per token, no object per token) and a few
///         objects per match.
///     </para>
///     <para>
///         <strong>Thread safety:</strong> a glossary is immutable after it is created, and every member may be called
///         concurrently from any number of threads. The entries are immutable too.
///     </para>
///     <para>
///         <strong>Exceptions:</strong> creating a glossary throws <see cref="ArgumentNullException"/> for a <c>null</c>
///         list or entry, <see cref="ArgumentOutOfRangeException"/> for invalid options and
///         <see cref="ArgumentException"/> for two entries that are duplicates (the same tokens under the same case rule).
///     </para>
/// </remarks>
/// <example>
///     A list of technical names: the head word of each is a noun wherever it occurs, also at the start of a sentence
///     and in the plural, although the general model reads <c>bleed</c> and <c>torque</c> as verbs:
///     <code>
///     Glossary glossary = new GlossaryBuilder
///     {
///         { "bleed valve", Upos.Noun, "A valve that releases trapped air." },
///         { "torque wrench", Upos.Noun },
///         { "accumulator", Upos.Noun },
///     }.Build();
///
///     TaggedSentence sentence = tagger.Tag("Bleed valves need a torque wrench.", glossary)[0];
///     foreach (GlossaryMatch match in sentence.GlossaryMatches)
///     {
///         Console.WriteLine($"{match.Text} -> {sentence.Tokens[match.HeadToken].Tag}");
///     }
///     </code>
///     A multiword compound whose first word is also a verb, with a definition that is read from the match, and an
///     irregular form that the inflection rules cannot produce:
///     <code>
///     Glossary glossary = Glossary.Create(new[]
///     {
///         new GlossaryEntry("bleed valve", Upos.Noun, "A valve that releases trapped air."),
///         new GlossaryEntry("bleed", UposSet.Of(Upos.Verb, Upos.Noun)).WithVariants("bled"),
///     });
///
///     foreach (GlossaryMatch match in glossary.FindMatches(Tokenizer.Tokenize("Close the bleed valve.")[0]))
///     {
///         Console.WriteLine($"{match.Text}: {match.Entry.Definition}");
///     }
///     </code>
///     An acronym with case sensitivity: the entry <c>IT</c> leaves the pronoun <c>it</c> alone:
///     <code>
///     Glossary glossary = new GlossaryBuilder
///     {
///         new GlossaryEntry("IT", Upos.Noun).WithCaseSensitive(true),
///     }.Build();
///
///     Console.WriteLine(glossary.FindMatches(Tokenizer.Tokenize("Call IT now.")[0]).Count);   // 1
///     Console.WriteLine(glossary.FindMatches(Tokenizer.Tokenize("Call it now.")[0]).Count);   // 0
///     </code>
///     Soft mode for a glossary of unknown quality: the tags of an entry are boosted, and the model decides when it is
///     clearly sure of another tag:
///     <code>
///     var options = new GlossaryOptions { Mode = GlossaryMode.Soft };
///     Glossary glossary = new GlossaryBuilder(options) { { "torque wrench", Upos.Noun } }.Build();
///
///     TaggedSentence sentence = tagger.Tag(new[] { "Use", "a", "torque", "wrench", "." }, glossary);
///     foreach (TaggedToken token in sentence.Tokens)
///     {
///         Console.WriteLine($"{token.Text} {token.Tag} {token.Confidence:0.00} {token.IsGlossaryHead}");
///     }
///     </code>
///     Edge cases: overlapping entries (the longest wins), punctuation around a term, and phrasal verbs whose particle is
///     not in the list of particle words, so that the head has to be set (see
///     <see cref="GlossaryEntry.WithHead(int)"/>):
///     <code>
///     Glossary glossary = Glossary.Create(new[]
///     {
///         new GlossaryEntry("bleed valve", Upos.Noun),
///         new GlossaryEntry("bleed valve cover", Upos.Noun),
///         new GlossaryEntry("Contoso Hub", Upos.Propn),
///         new GlossaryEntry("look after", Upos.Verb).WithHead(0),        // after is not a listed particle word
///         new GlossaryEntry("look forward to", Upos.Verb).WithHead(0),   // to is not a listed particle word
///     });
///
///     string[] texts =
///     {
///         "Remove the bleed valve cover.",       // the longest term wins: bleed valve cover, head 4
///         "Close the bleed valve.",              // bleed valve, head 3
///         "Restart (Contoso Hub), then wait.",   // brackets and comma are separate tokens: Contoso Hub, head 3
///         "Restart the Contoso Hub.",            // the final period is a separate token: Contoso Hub, head 3
///         "Please look after the pump.",         // look after, head 1 (look)
///         "They look forward to the test.",      // look forward to, head 1 (look)
///     };
///     foreach (string text in texts)
///     {
///         foreach (GlossaryMatch match in glossary.FindMatches(Tokenizer.Tokenize(text)[0]))
///         {
///             Console.WriteLine($"{match.Text} (head {match.HeadToken})");
///         }
///     }
///     </code>
/// </example>
public sealed class Glossary
{
    /// <summary>
    ///     The glossary that has no entries.
    /// </summary>
    private static readonly Glossary EmptyGlossary = new(new GlossaryEntry[0], new GlossaryOptions());

    /// <summary>
    ///     The shared empty list of matches.
    /// </summary>
    private static readonly IReadOnlyList<GlossaryMatch> NoMatches = Array.AsReadOnly(new GlossaryMatch[0]);

    /// <summary>
    ///     The entries in the order added.
    /// </summary>
    private readonly GlossaryEntry[] _entries;

    /// <summary>
    ///     The read-only view of the entries that callers see.
    /// </summary>
    private readonly ReadOnlyCollection<GlossaryEntry> _entryView;

    /// <summary>
    ///     The private copy of the options.
    /// </summary>
    private readonly GlossaryOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="Glossary"/> class from validated entries and options.
    /// </summary>
    /// <param name="entries">The entries; a private array.</param>
    /// <param name="options">The validated copy of the options.</param>
    /// <param name="hashMask">The hash mask of the index (a test hook; all ones in production).</param>
    private Glossary(GlossaryEntry[] entries, GlossaryOptions options, ulong hashMask = ulong.MaxValue)
    {
        _entries = entries;
        _entryView = Array.AsReadOnly(entries);
        _options = options;
        Index = new GlossaryIndex(_entryView, options, hashMask);
    }

    /// <summary>
    ///     Gets the glossary that has no entries.
    /// </summary>
    /// <remarks>
    ///     Tagging with it gives the same tags as tagging without a glossary, and its
    ///     <see cref="TaggedSentence.GlossaryMatches"/> are empty.
    /// </remarks>
    public static Glossary Empty => EmptyGlossary;

    /// <summary>
    ///     Gets the number of entries.
    /// </summary>
    public int Count => _entries.Length;

    /// <summary>
    ///     Gets the entries in the order in which they were added.
    /// </summary>
    /// <remarks>
    ///     The list is a read-only view; it cannot be cast back to a writable collection.
    /// </remarks>
    public IReadOnlyList<GlossaryEntry> Entries => _entryView;

    /// <summary>
    ///     Gets a copy of the options that the glossary was created with.
    /// </summary>
    public GlossaryOptions Options => GlossaryOptions.CopyOf(_options);

    /// <summary>
    ///     Gets the compiled index.
    /// </summary>
    internal GlossaryIndex Index { get; }

    /// <summary>
    ///     Gets the entries as the array that backs <see cref="Entries"/> (not copied; callers must not modify it).
    /// </summary>
    internal GlossaryEntry[] EntryArray => _entries;

    /// <summary>
    ///     Gets the validated options without copying them (callers must not modify them).
    /// </summary>
    internal GlossaryOptions Settings => _options;

    /// <summary>
    ///     Creates a glossary from entries.
    /// </summary>
    /// <remarks>
    ///     The entries and the options are validated and copied, so changing the source collection or the options
    ///     afterwards has no effect. Every term and variant is tokenized and the inflected forms are generated now, so
    ///     that tagging pays only for matching. Two entries are duplicates when they have the same tokens (compared
    ///     without regard to letter case unless the entry is case-sensitive) under the same effective case rule; merge
    ///     the tags into one entry instead. A case-sensitive and a case-insensitive entry for the same term are not
    ///     duplicates.
    /// </remarks>
    /// <param name="entries">The entries; none may be <c>null</c>. An empty collection gives an empty glossary.</param>
    /// <param name="options">The settings, or <c>null</c> for the defaults.</param>
    /// <returns>The glossary.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> or one of its items is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The mode of <paramref name="options"/> is undefined, or its <see cref="GlossaryOptions.SoftStrength"/> is not finite and greater than zero.</exception>
    /// <exception cref="ArgumentException">Two entries are duplicates.</exception>
    /// <example>
    ///     Create a glossary from a list of entries and read its size:
    ///     <code>
    ///     Glossary glossary = Glossary.Create(new[]
    ///     {
    ///         new GlossaryEntry("accumulator", Upos.Noun),
    ///         new GlossaryEntry("isolate", Upos.Verb),
    ///     });
    ///     Console.WriteLine(glossary.Count);   // 2
    ///     </code>
    /// </example>
    public static Glossary Create(IEnumerable<GlossaryEntry> entries, GlossaryOptions? options = null) =>
        Create(entries, options, ulong.MaxValue);

    /// <summary>
    ///     Finds the terms of the glossary in a sentence without tagging it.
    /// </summary>
    /// <remarks>
    ///     The search is the one that tagging uses: left to right, the longest term at each position, no overlaps. Use it to
    ///     detect terms or to read the definitions of the terms in a text. The result is a new list per call.
    /// </remarks>
    /// <param name="sentence">The sentence, from <see cref="Tokenizer"/>.</param>
    /// <returns>The matches in text order; empty when there are none.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sentence"/> is <c>null</c>.</exception>
    /// <example>
    ///     List the terms of a sentence with their definitions:
    ///     <code>
    ///     Glossary glossary = Glossary.Create(new[] { new GlossaryEntry("accumulator", Upos.Noun, "Stores pressure.") });
    ///     foreach (GlossaryMatch match in glossary.FindMatches(Tokenizer.Tokenize("Check the accumulators.")[0]))
    ///     {
    ///         Console.WriteLine($"{match.Text}: {match.Entry.Definition}");
    ///     }
    ///     </code>
    /// </example>
    public IReadOnlyList<GlossaryMatch> FindMatches(Sentence sentence)
    {
        ArgumentNullException.ThrowIfNull(sentence);
        return Match(sentence);
    }

    /// <summary>
    ///     Creates a glossary with a hash mask for the index (the test hook that forces hash collisions).
    /// </summary>
    /// <param name="entries">The entries.</param>
    /// <param name="options">The options or <c>null</c>.</param>
    /// <param name="hashMask">The mask applied to every hash of the index.</param>
    /// <returns>The glossary.</returns>
    internal static Glossary Create(IEnumerable<GlossaryEntry> entries, GlossaryOptions? options, ulong hashMask)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var settings = GlossaryOptions.CopyOf(options);

        // Copy first (the source may change or be lazy), then reject null items and duplicates under the effective case rule
        var copy = new List<GlossaryEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entries), "An entry is null.");
            }
            if (!seen.Add(entry.IdentityKey(entry.CaseSensitive ?? settings.CaseSensitive)))
            {
                throw new ArgumentException($"The glossary already has an entry for the term '{entry.Term}' under the same case rule.", nameof(entries));
            }

            copy.Add(entry);
        }

        return new Glossary(copy.ToArray(), settings, hashMask);
    }

    /// <summary>
    ///     Finds the matches in a sentence as the objects that the public results use.
    /// </summary>
    /// <remarks>
    ///     The per-thread workspace is used only inside this method and the matches are copied out before it returns, so
    ///     no code of the caller runs while the workspace holds results.
    /// </remarks>
    /// <param name="sentence">The sentence.</param>
    /// <returns>The matches in text order; a shared empty list when there are none.</returns>
    internal IReadOnlyList<GlossaryMatch> Match(Sentence sentence)
    {
        var tokens = sentence.TokenArray;
        if (_entries.Length == 0 || tokens.Length == 0)
        {
            return NoMatches;
        }

        var workspace = GlossaryIndex.GetThreadWorkspace();
        var found = Index.Find(tokens, workspace);
        if (found == 0)
        {
            return NoMatches;
        }

        var result = new GlossaryMatch[found];
        for (var m = 0; m < found; m++)
        {
            var form = Index.GetForm(workspace.MatchForm[m]);
            var first = workspace.MatchFirst[m];
            var count = workspace.MatchCount[m];
            var entry = _entries[form.EntryIndex];
            var last = tokens[first + count - 1];
            var start = tokens[first].Start;
            var length = last.Start + last.Length - start;
            var head = first + form.HeadOffset;
            result[m] = new GlossaryMatch(
                entry,
                form.EntryIndex,
                form.TagSetId,
                first,
                count,
                head,
                start,
                length,
                sentence.Text.Substring(start - sentence.Start, length),
                entry.Mode ?? _options.Mode,
                form.IsVariant,
                tokens[head].Start);
        }

        return Array.AsReadOnly(result);
    }
}
