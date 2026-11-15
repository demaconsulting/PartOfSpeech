// cspell:ignore zzqxv
namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     The result of looking up one word with <see cref="Tagger.GetPossibleTags(string, PossibleTagsMode)"/>: the tags the word
///     can take, whether the lexicon recognizes it, and the analyses (base word, tag, features) of its forms.
/// </summary>
/// <remarks>
///     <para>
///         <strong>Unknown versus empty:</strong> a recognized word never has an empty <see cref="Tags"/> set, but an
///         unrecognized word has one in <see cref="PossibleTagsMode.LexiconOnly"/> mode (and all 17 tags in
///         <see cref="PossibleTagsMode.WithShapeRules"/> mode). Test <see cref="Match"/> (or <see cref="IsKnownWord"/>) to
///         tell "the lexicon does not know this word" from anything else; never test <c>Tags.IsEmpty</c>. The default
///         value of the struct is "not recognized, no tags".
///     </para>
///     <para>
///         <strong>Tags come from the whole token:</strong> <see cref="Tags"/> is what the tagger itself sees for the word as
///         one token, so a hyphenated word that is not a lexicon spelling gets the tags of an unknown word even when its last
///         part is known (<see cref="WordMatch.CompoundHead"/>); the head adds analyses only. The lexicon is a restriction
///         list built from several sources, not a gold dictionary, so it can list a tag that no analysis explains;
///         <see cref="TagsWithoutAnalysis"/> names those tags.
///     </para>
///     <para>
///         The struct is immutable and thread-safe. It keeps a reference to the lexicon of the tagger that created it (to
///         produce the analyses on request), so two results are equal when they describe the same lexicon entry of the same
///         lexicon with the same tags and match.
///     </para>
/// </remarks>
/// <example>
///     Ask which tags a word can take and whether the lexicon knows it, then read the readings of a form:
///     <code>
///     WordTags valve = Tagger.Default.GetPossibleTags("Valve");
///     Console.WriteLine(valve.Tags);            // NOUN
///     Console.WriteLine(valve.IsKnownWord);     // True
///
///     WordTags unknown = Tagger.Default.GetPossibleTags("zzqxv");
///     Console.WriteLine(unknown.Match);         // NotRecognized
///
///     foreach (WordAnalysis analysis in Tagger.Default.GetPossibleTags("tests").GetAnalyses())
///     {
///         Console.WriteLine(analysis);          // test NOUN Number=Plur, then test VERB Person=3|Tense=Pres
///     }
///     </code>
/// </example>
/// <seealso cref="Tagger.GetPossibleTags(string, PossibleTagsMode)"/>
/// <seealso cref="WordAnalysis"/>
public readonly struct WordTags : IEquatable<WordTags>
{
    /// <summary>
    ///     The lexicon that holds the analyses, or <c>null</c> for a word that was not recognized.
    /// </summary>
    private readonly Lexicon? _lexicon;

    /// <summary>
    ///     The position of the matched spelling (the whole word or the head of a compound) in the lexicon, or -1.
    /// </summary>
    private readonly int _ordinal;

    /// <summary>
    ///     The tags that at least one analysis of the matched spelling names.
    /// </summary>
    private readonly UposSet _analyzedTags;

    /// <summary>
    ///     Initializes a new instance of the <see cref="WordTags"/> struct.
    /// </summary>
    /// <remarks>
    ///     The lookup creates these values itself; the constructor does not check its arguments. A result without a
    ///     lexicon stores no position, so a "not recognized" result with empty tags equals <c>default</c>.
    /// </remarks>
    /// <param name="lexicon">The lexicon that holds the analyses, or <c>null</c> when the word was not recognized.</param>
    /// <param name="ordinal">The position of the matched spelling in <paramref name="lexicon"/>, or -1.</param>
    /// <param name="tags">The tags of the result.</param>
    /// <param name="match">How the word was found.</param>
    /// <param name="analyzedTags">The tags that an analysis of the matched spelling names.</param>
    internal WordTags(Lexicon? lexicon, int ordinal, UposSet tags, WordMatch match, UposSet analyzedTags)
    {
        _lexicon = lexicon;
        _ordinal = lexicon == null ? 0 : ordinal;
        Tags = tags;
        Match = match;
        _analyzedTags = analyzedTags;
    }

    /// <summary>
    ///     Gets the tags that the word can take, according to the mode of the lookup.
    /// </summary>
    /// <remarks>
    ///     In <see cref="PossibleTagsMode.LexiconOnly"/> mode these are the tags that the lexicon lists for the whole
    ///     folded, lower-cased word, and the empty set when the word is not a spelling. In
    ///     <see cref="PossibleTagsMode.WithShapeRules"/> mode they are the tags that the tagger allows for the token on its
    ///     own, never empty. Whether the word is known is answered by <see cref="Match"/>, not by this set.
    /// </remarks>
    public UposSet Tags { get; }

    /// <summary>
    ///     Gets how the word was found in the lexicon.
    /// </summary>
    /// <remarks>
    ///     <see cref="WordMatch.NotRecognized"/> is the explicit answer for a word the lexicon has no evidence for; it is
    ///     also the value of <c>default(WordTags)</c>.
    /// </remarks>
    public WordMatch Match { get; }

    /// <summary>
    ///     Gets a value that indicates whether the folded, lower-cased word is a lexicon spelling.
    /// </summary>
    /// <remarks>
    ///     This is <c>true</c> exactly when <see cref="Match"/> is <see cref="WordMatch.Spelling"/>, and it equals
    ///     <see cref="TaggedToken.IsKnownWord"/> of the same word as a token of the tagger. A hyphenated compound whose last
    ///     part is known is not a known word for the tagger, so it is <c>false</c> for <see cref="WordMatch.CompoundHead"/>.
    /// </remarks>
    public bool IsKnownWord => Match == WordMatch.Spelling;

    /// <summary>
    ///     Gets the tags of <see cref="Tags"/> that no analysis names: the tags that are attested without an analysis.
    /// </summary>
    /// <remarks>
    ///     The lexicon lists a tag without an analysis when it was added by a rule from a related form (for example the
    ///     adjective reading of a participle), listed for a closed-class word, or only attested in a corpus. In
    ///     <see cref="PossibleTagsMode.WithShapeRules"/> mode the set also holds the tags that the token shape adds and, for
    ///     a word that is not recognized, all 17 tags. For a word of a lexicon without an analyses table this equals
    ///     <see cref="Tags"/>. The set is empty when every tag has an analysis.
    /// </remarks>
    public UposSet TagsWithoutAnalysis => UposSet.FromMask(Tags.Mask & ~_analyzedTags.Mask);

    /// <summary>
    ///     Determines whether two results are equal.
    /// </summary>
    /// <param name="left">The first result.</param>
    /// <param name="right">The second result.</param>
    /// <returns><c>true</c> if the results describe the same entry of the same lexicon with the same tags and match.</returns>
    public static bool operator ==(WordTags left, WordTags right) => left.Equals(right);

    /// <summary>
    ///     Determines whether two results differ.
    /// </summary>
    /// <param name="left">The first result.</param>
    /// <param name="right">The second result.</param>
    /// <returns><c>true</c> if the results differ.</returns>
    public static bool operator !=(WordTags left, WordTags right) => !left.Equals(right);

    /// <summary>
    ///     Gets the analyses of the word: every combination of base word, tag and features that the lexicon records for it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         For a <see cref="WordMatch.Spelling"/> these are the analyses of the whole spelling; for a
    ///         <see cref="WordMatch.CompoundHead"/> they are those of its last part, so the base word is the head's, not the
    ///         compound's. They are in a fixed order (tag in <see cref="Upos"/> order, then base word, then features), distinct,
    ///         and every tag of an analysis is one of the tags that the lexicon lists for the spelling. The list is empty when
    ///         the word is not recognized, when the lexicon records no analysis for the spelling (a proper name, for example) and
    ///         when the lexicon has no analyses table at all.
    ///     </para>
    ///     <para>
    ///         This method allocates the list and one string per base word (feature strings are shared), so call it only for
    ///         words you need the analyses of; <see cref="Tags"/>, <see cref="Match"/> and
    ///         <see cref="TagsWithoutAnalysis"/> allocate nothing. Use <see cref="Tagger.IsFormOf(string, string, Upos)"/> to ask
    ///         for one reading without allocating.
    ///     </para>
    /// </remarks>
    /// <returns>The analyses, possibly an empty list; the list is a new array that the caller may keep.</returns>
    public IReadOnlyList<WordAnalysis> GetAnalyses()
    {
        if (_lexicon == null || Match == WordMatch.NotRecognized)
        {
            return Array.Empty<WordAnalysis>();
        }

        // Convert the lexicon's analyses of the matched spelling into the public type
        var stored = _lexicon.GetAnalyses(_ordinal);
        if (stored.Count == 0)
        {
            return Array.Empty<WordAnalysis>();
        }

        var result = new WordAnalysis[stored.Count];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = new WordAnalysis(stored[i].Lemma, stored[i].Tag, stored[i].Features);
        }

        return result;
    }

    /// <summary>
    ///     Determines whether this result equals another one.
    /// </summary>
    /// <param name="other">The result to compare with.</param>
    /// <returns><c>true</c> if both describe the same entry of the same lexicon and have the same tags and match.</returns>
    public bool Equals(WordTags other) =>
        Match == other.Match
        && Tags == other.Tags
        && _analyzedTags == other._analyzedTags
        && _ordinal == other._ordinal
        && ReferenceEquals(_lexicon, other._lexicon);

    /// <summary>
    ///     Determines whether this result is equal to an object.
    /// </summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns><c>true</c> if <paramref name="obj"/> is a <see cref="WordTags"/> that is equal to this one.</returns>
    public override bool Equals(object? obj) => obj is WordTags other && Equals(other);

    /// <summary>
    ///     Gets a hash code for the result.
    /// </summary>
    /// <returns>A hash code of match, tags and position; equal results have equal hash codes.</returns>
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = (int)Match;
            hash = (hash * 397) ^ Tags.GetHashCode();
            hash = (hash * 397) ^ _analyzedTags.GetHashCode();
            return (hash * 397) ^ _ordinal;
        }
    }

    /// <summary>
    ///     Formats the result for diagnostics.
    /// </summary>
    /// <returns>For example <c>Spelling NOUN|VERB</c>, <c>CompoundHead</c> followed by the tags, or <c>NotRecognized</c> followed by the (possibly empty) tags.</returns>
    public override string ToString() => $"{Match} {Tags}".TrimEnd();
}
