namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     The 17 Universal POS tags that the tagger assigns.
/// </summary>
/// <remarks>
///     <para>
///         The members are in alphabetical order of their Universal Dependencies spelling (the tag set is described at
///         https://universaldependencies.org/u/pos/). The numeric values (0 to 16)
///         are part of the binary formats: they are the tag order of the lexicon masks and of the weight classes, so the
///         order must never change without a new format version. Each member below has a one-line definition with an
///         example; the examples follow the conventions of the training data (which differ from the Universal
///         Dependencies guidelines in a few places, for example possessive words such as <c>my</c> are determiners).
///     </para>
///     <para>
///         ## Telling similar tags apart
///     </para>
///     <list type="bullet">
///     <item><description>
///     <see cref="Adp"/>, <see cref="Part"/> and <see cref="Sconj"/>: <see cref="Adp"/> is a preposition with a noun
///     phrase or a gerund after it (<c>in the house</c>, <c>of smoking</c>, and <c>to</c> in <c>went to Paris</c>);
///     <see cref="Part"/> is a small function word that belongs to a verb or a noun and is not a preposition (the
///     infinitive marker <c>to</c> in <c>want to go</c>, <c>not</c>, <c>n't</c>, the possessive <c>'s</c>);
///     <see cref="Sconj"/> opens a clause that has its own subject (<c>because it rained</c>, <c>if he comes</c>).
///     </description></item>
///     <item><description>
///     <see cref="Aux"/> and <see cref="Verb"/>: a verb is <see cref="Aux"/> only when it helps another verb (<c>has</c>
///     in <c>has gone</c>, <c>is</c> in <c>is running</c>, <c>does</c> in <c>does not know</c>, the modals <c>can</c> and
///     <c>will</c>); the same word as the main verb is <see cref="Verb"/> (<c>has</c> in <c>has a car</c>, <c>is</c> in
///     <c>She is tall</c>, <c>do</c> in <c>do homework</c>).
///     </description></item>
///     <item><description>
///     <see cref="Propn"/> and <see cref="Noun"/>: <see cref="Propn"/> is for names (<c>London</c>, <c>Ada</c>,
///     <c>English</c>); every other noun is <see cref="Noun"/>, also when it is capitalized at the start of a sentence.
///     </description></item>
///     <item><description>
///     <see cref="X"/> and <see cref="Sym"/>: <see cref="Sym"/> is a symbol that has a meaning of its own (<c>$</c>,
///     <c>%</c>, <c>+</c>); <see cref="X"/> is for what cannot be analyzed as an English word (a foreign word, a web or
///     e-mail address, a list marker).
///     </description></item>
///     </list>
/// </remarks>
public enum Upos : byte
{
    /// <summary>Adjective (<c>ADJ</c>): describes a noun, for example <c>red</c> in <c>a red car</c>; also an ordinal (<c>third</c>) and a participle that describes a state (<c>a broken window</c>).</summary>
    Adj = 0,

    /// <summary>Adposition (<c>ADP</c>): a preposition that links a noun phrase or a gerund to the rest of the sentence, for example <c>in</c> in <c>in the house</c>, <c>of</c> in <c>of smoking</c>, <c>to</c> in <c>went to Paris</c>.</summary>
    Adp = 1,

    /// <summary>Adverb (<c>ADV</c>): modifies a verb, an adjective or another adverb, for example <c>quickly</c> in <c>ran quickly</c>, <c>very</c> in <c>very tall</c>; also the particle of a phrasal verb (<c>up</c> in <c>give up</c>).</summary>
    Adv = 2,

    /// <summary>Auxiliary (<c>AUX</c>): a verb that only helps another verb, for example <c>has</c> in <c>has gone</c>, <c>is</c> in <c>is running</c>, <c>does</c> in <c>does not know</c>, and the modals <c>can</c>, <c>will</c>; the same word as a main verb is <see cref="Verb"/>.</summary>
    Aux = 3,

    /// <summary>Coordinating conjunction (<c>CCONJ</c>): links words or clauses of equal rank, for example <c>and</c>, <c>or</c>, <c>but</c>.</summary>
    Cconj = 4,

    /// <summary>Determiner (<c>DET</c>): introduces a noun and says which or how many, for example <c>the</c>, <c>a</c>, <c>this</c>, and the possessive words <c>my</c>, <c>your</c>, <c>his</c> before a noun.</summary>
    Det = 5,

    /// <summary>Interjection (<c>INTJ</c>): an exclamation that stands outside the grammar of the sentence, for example <c>oh</c>, <c>wow</c>, <c>hello</c>.</summary>
    Intj = 6,

    /// <summary>Noun (<c>NOUN</c>): a common noun, for example <c>cat</c>, <c>water</c>, <c>idea</c>; a name is <see cref="Propn"/>.</summary>
    Noun = 7,

    /// <summary>Numeral (<c>NUM</c>): a cardinal number in digits or words, for example <c>3</c>, <c>1,000</c>, <c>seven</c>; an ordinal (<c>third</c>) is <see cref="Adj"/>.</summary>
    Num = 8,

    /// <summary>Particle (<c>PART</c>): a small function word that belongs to a verb or a noun and is not a preposition: the infinitive marker <c>to</c> in <c>want to go</c>, <c>not</c>, <c>n't</c> in <c>does n't</c>, and the possessive <c>'s</c> in <c>John 's</c>.</summary>
    Part = 9,

    /// <summary>Pronoun (<c>PRON</c>): stands in for a noun phrase, for example <c>she</c>, <c>them</c>, <c>who</c>, <c>this</c> in <c>I like this</c>.</summary>
    Pron = 10,

    /// <summary>Proper noun (<c>PROPN</c>): the name of a particular person, place or thing, for example <c>London</c>, <c>Ada</c>, <c>English</c>.</summary>
    Propn = 11,

    /// <summary>Punctuation (<c>PUNCT</c>): a punctuation mark, for example <c>.</c>, <c>,</c>, <c>?</c>, brackets and quotation marks.</summary>
    Punct = 12,

    /// <summary>Subordinating conjunction (<c>SCONJ</c>): opens a subordinate clause that has its own subject, for example <c>because</c> in <c>because it rained</c>, <c>if</c>, <c>while</c>, <c>that</c> in <c>know that she left</c>.</summary>
    Sconj = 13,

    /// <summary>Symbol (<c>SYM</c>): a symbol with a meaning of its own that is not punctuation, for example <c>$</c>, <c>%</c>, <c>+</c>, <c>=</c>.</summary>
    Sym = 14,

    /// <summary>Verb (<c>VERB</c>): a main verb, for example <c>run</c>, <c>eat</c>, and <c>is</c> in <c>She is tall</c>; a verb that only helps another verb is <see cref="Aux"/>.</summary>
    Verb = 15,

    /// <summary>Other (<c>X</c>): a token that cannot be analyzed as an English word, for example a foreign word, a web or e-mail address (<c>www.example.com</c>) or a list marker.</summary>
    X = 16,
}
