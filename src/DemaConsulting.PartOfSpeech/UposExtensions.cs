namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     Conversions between <see cref="Upos"/> values and their Universal Dependencies spelling.
/// </summary>
/// <remarks>
///     The spelling is the one used by every data file of the repository (<c>NOUN</c>, <c>CCONJ</c>, <c>X</c>), so the
///     conversions are the single place where the enum and the text formats meet. All members are stateless and
///     thread-safe.
/// </remarks>
/// <example>
///     Convert between tags and their Universal Dependencies spelling:
///     <code>
///     string spelling = Upos.Cconj.ToTagString();                   // "CCONJ"
///     bool parsed = UposExtensions.TryParse("NOUN", out Upos tag);  // true, Upos.Noun
///     </code>
/// </example>
public static class UposExtensions
{
    /// <summary>
    ///     The number of Universal POS tags (17).
    /// </summary>
    public const int Count = 17;

    /// <summary>
    ///     The spelling of every tag, indexed by the numeric value of the <see cref="Upos"/> member.
    /// </summary>
    private static readonly string[] TagStrings =
    {
        "ADJ", "ADP", "ADV", "AUX", "CCONJ", "DET", "INTJ", "NOUN", "NUM", "PART", "PRON", "PROPN", "PUNCT", "SCONJ",
        "SYM", "VERB", "X"
    };

    /// <summary>
    ///     Gets the Universal Dependencies spelling of a tag.
    /// </summary>
    /// <param name="tag">The tag.</param>
    /// <returns>The upper-case spelling, for example <c>ADJ</c> or <c>CCONJ</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tag"/> is not one of the 17 defined values.</exception>
    public static string ToTagString(this Upos tag)
    {
        // Reject values that were cast from an arbitrary number: a silent wrong spelling would corrupt data files
        if ((uint)tag >= Count)
        {
            throw new ArgumentOutOfRangeException(nameof(tag), tag, "The value is not one of the 17 Universal POS tags.");
        }

        return TagStrings[(int)tag];
    }

    /// <summary>
    ///     Parses the Universal Dependencies spelling of a tag.
    /// </summary>
    /// <remarks>
    ///     The comparison is ordinal and case-sensitive on purpose: the data files only ever contain the upper-case
    ///     spelling, and accepting other spellings would hide mistakes in those files.
    /// </remarks>
    /// <param name="text">The text to parse; <c>null</c> never parses.</param>
    /// <param name="tag">Receives the tag when the text is a valid spelling; <see cref="Upos.Adj"/> otherwise.</param>
    /// <returns><c>true</c> if the text is the exact spelling of one of the 17 tags.</returns>
    public static bool TryParse(string? text, out Upos tag)
    {
        // Linear search over 17 entries: no dictionary is needed and nothing is allocated
        if (text != null)
        {
            for (var i = 0; i < TagStrings.Length; i++)
            {
                if (string.Equals(TagStrings[i], text, StringComparison.Ordinal))
                {
                    tag = (Upos)i;
                    return true;
                }
            }
        }

        tag = Upos.Adj;
        return false;
    }
}
