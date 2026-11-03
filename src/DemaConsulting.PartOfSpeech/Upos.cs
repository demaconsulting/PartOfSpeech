namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     The 17 Universal POS tags that the tagger assigns.
/// </summary>
/// <remarks>
///     The members are in alphabetical order of their Universal Dependencies spelling (the tag set is described at
///     https://universaldependencies.org/u/pos/). The numeric values (0 to 16)
///     are part of the binary formats: they are the tag order of the lexicon masks and of the weight classes, so the
///     order must never change without a new format version.
/// </remarks>
public enum Upos : byte
{
    /// <summary>Adjective (<c>ADJ</c>).</summary>
    Adj = 0,

    /// <summary>Adposition (<c>ADP</c>).</summary>
    Adp = 1,

    /// <summary>Adverb (<c>ADV</c>).</summary>
    Adv = 2,

    /// <summary>Auxiliary (<c>AUX</c>).</summary>
    Aux = 3,

    /// <summary>Coordinating conjunction (<c>CCONJ</c>).</summary>
    Cconj = 4,

    /// <summary>Determiner (<c>DET</c>).</summary>
    Det = 5,

    /// <summary>Interjection (<c>INTJ</c>).</summary>
    Intj = 6,

    /// <summary>Noun (<c>NOUN</c>).</summary>
    Noun = 7,

    /// <summary>Numeral (<c>NUM</c>).</summary>
    Num = 8,

    /// <summary>Particle (<c>PART</c>).</summary>
    Part = 9,

    /// <summary>Pronoun (<c>PRON</c>).</summary>
    Pron = 10,

    /// <summary>Proper noun (<c>PROPN</c>).</summary>
    Propn = 11,

    /// <summary>Punctuation (<c>PUNCT</c>).</summary>
    Punct = 12,

    /// <summary>Subordinating conjunction (<c>SCONJ</c>).</summary>
    Sconj = 13,

    /// <summary>Symbol (<c>SYM</c>).</summary>
    Sym = 14,

    /// <summary>Verb (<c>VERB</c>).</summary>
    Verb = 15,

    /// <summary>Other (<c>X</c>).</summary>
    X = 16,
}
