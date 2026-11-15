namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     Tells how a word was found in the lexicon by <see cref="Tagger.GetPossibleTags(string, PossibleTagsMode)"/>.
/// </summary>
/// <remarks>
///     <para>
///         This is the explicit "recognized or not" answer of the word lookup. It exists so that a word the lexicon does
///         not know cannot be mistaken for a word that has no tags: in <see cref="PossibleTagsMode.LexiconOnly"/> mode both
///         have an empty tag set, and in <see cref="PossibleTagsMode.WithShapeRules"/> mode an unrecognized word has all 17
///         tags. Always test the match, never <c>Tags.IsEmpty</c>.
///     </para>
///     <para>
///         The default value is <see cref="NotRecognized"/>, so <c>default(WordMatch)</c> and <c>default(WordTags)</c>
///         mean "not recognized". The enum is immutable data and safe to use from any thread.
///     </para>
/// </remarks>
/// <seealso cref="WordTags.Match"/>
public enum WordMatch
{
    /// <summary>
    ///     The word is not a lexicon spelling and, for a hyphenated word, its last part is not one either (the default value).
    /// </summary>
    /// <remarks>
    ///     The word has no analyses. This says that the lexicon has no evidence about the word; it is not a statement that
    ///     the word does not exist or has no part of speech.
    /// </remarks>
    NotRecognized = 0,

    /// <summary>
    ///     The folded, lower-cased word is a lexicon spelling.
    /// </summary>
    /// <remarks>
    ///     This is the match that the tagger itself makes: <see cref="WordTags.IsKnownWord"/> is <c>true</c> and equals
    ///     <see cref="TaggedToken.IsKnownWord"/> of the same token. A hyphenated word whose whole spelling is listed
    ///     (<c>built-in</c>) is a <see cref="Spelling"/>, and its analyses are those of the whole spelling.
    /// </remarks>
    Spelling = 1,

    /// <summary>
    ///     The word is a hyphenated word that is not a lexicon spelling, but its last part is a spelling.
    /// </summary>
    /// <remarks>
    ///     A word is a hyphenated compound when it contains the hyphen-minus and no part between hyphens is empty
    ///     (<c>well-tested</c>; not <c>-ed</c>, <c>well-</c> or <c>a--b</c>). The analyses of the result are those of the last
    ///     part (the head of the compound). The tagger itself does not look at the parts of a hyphenated word, so for such a
    ///     word <see cref="WordTags.IsKnownWord"/> is <c>false</c> and <see cref="WordTags.Tags"/> is what the tagger sees for the
    ///     whole token; the head only adds analyses and never changes the tags or the tagging.
    /// </remarks>
    CompoundHead = 2,
}
