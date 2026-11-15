namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     Selects which tags <see cref="Tagger.GetPossibleTags(string, PossibleTagsMode)"/> reports for a word.
/// </summary>
/// <remarks>
///     <para>
///         The two modes answer two different questions. <see cref="LexiconOnly"/> is the dictionary question: which
///         tags does the lexicon list for this spelling? <see cref="WithShapeRules"/> is the tagger question: which
///         tags can the tagger give this word, whatever the context and the model? The second answer is the lexicon
///         answer widened by the token-shape rules (see
///         [Terminology](../DemaConsulting.PartOfSpeech.md#terminology)), and it is never empty.
///     </para>
///     <para>
///         The default of the lookup is <see cref="LexiconOnly"/>, which is also <c>default(PossibleTagsMode)</c>. Pass
///         <see cref="WithShapeRules"/> explicitly when you need the tags the tagger can choose from. The enum is
///         immutable data and safe to use from any thread.
///     </para>
/// </remarks>
/// <seealso cref="WordTags"/>
public enum PossibleTagsMode
{
    /// <summary>
    ///     The tags that the lexicon lists for the folded, lower-cased word; the empty set when the word is not a lexicon spelling.
    /// </summary>
    /// <remarks>
    ///     An empty result does not by itself mean "no part of speech": check <see cref="WordTags.Match"/> to tell a word that
    ///     the lexicon does not know from one that it knows. A capitalized word has the same tags as its lower-case form, and
    ///     a digit or punctuation token has only what the lexicon lists for its spelling.
    /// </remarks>
    LexiconOnly = 0,

    /// <summary>
    ///     Exactly the tags that the tagger allows for the word on its own: the lexicon tags, extended by the token-shape
    ///     rules (all 17 tags when the word is not a lexicon spelling).
    /// </summary>
    /// <remarks>
    ///     The extension follows the shape of the token itself: a token without a letter or digit also allows PUNCT, SYM
    ///     and X; a token with a digit also allows NUM, SYM, X, NOUN, PROPN and ADJ; a token that starts with an
    ///     upper-case letter also allows PROPN and X. The position of the word in a sentence never matters. The
    ///     result equals the tags of <see cref="TaggedToken.Tag"/> and <see cref="TaggedToken.Alternatives"/> of the same
    ///     token when the tagger reports all alternatives (<see cref="TaggerOptions.MaxAlternatives"/> 16) and no glossary or
    ///     constraint narrows the token.
    /// </remarks>
    WithShapeRules = 1,
}
