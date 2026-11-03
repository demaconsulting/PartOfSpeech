// cspell:ignore EBCA
using System.Text;

namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     The templates of the features that the perceptron scores, in the order in which their buckets are produced.
/// </summary>
/// <remarks>
///     The numeric value of a member is part of the weights format: it seeds the hash of the feature, so changing the
///     order or the set requires a new <see cref="FeatureExtractor.FeatureSetVersion"/>.
/// </remarks>
internal enum FeatureKind
{
    /// <summary>A constant feature that gives every tag a bias.</summary>
    Bias = 0,

    /// <summary>The token with its original capitalization (after folding).</summary>
    Word = 1,

    /// <summary>The lower-cased token.</summary>
    Lower = 2,

    /// <summary>The shape bits of the token (capitalization, digits, hyphen, apostrophe, no letters).</summary>
    Shape = 3,

    /// <summary>The first character of the lower-cased token.</summary>
    Prefix1 = 4,

    /// <summary>The first two characters of the lower-cased token.</summary>
    Prefix2 = 5,

    /// <summary>The first three characters of the lower-cased token.</summary>
    Prefix3 = 6,

    /// <summary>The first four characters of the lower-cased token.</summary>
    Prefix4 = 7,

    /// <summary>The last character of the lower-cased token.</summary>
    Suffix1 = 8,

    /// <summary>The last two characters of the lower-cased token.</summary>
    Suffix2 = 9,

    /// <summary>The last three characters of the lower-cased token.</summary>
    Suffix3 = 10,

    /// <summary>The last four characters of the lower-cased token.</summary>
    Suffix4 = 11,

    /// <summary>The token is the first one that contains a letter or a digit.</summary>
    Initial = 12,

    /// <summary>The lower-cased previous token.</summary>
    Prev1 = 13,

    /// <summary>The lower-cased token before the previous one.</summary>
    Prev2 = 14,

    /// <summary>The lower-cased next token.</summary>
    Next1 = 15,

    /// <summary>The lower-cased token after the next one.</summary>
    Next2 = 16,

    /// <summary>The pair of the previous and the next token.</summary>
    PrevNext = 17,

    /// <summary>The tag of the previous token.</summary>
    PrevTag1 = 18,

    /// <summary>The tag of the token before the previous one.</summary>
    PrevTag2 = 19,

    /// <summary>The pair of the two previous tags.</summary>
    PrevTagPair = 20,

    /// <summary>The previous tag together with the lower-cased token.</summary>
    PrevTagLower = 21,
}

/// <summary>
///     Computes the hashed feature buckets of a token: the single feature definition that decoding and (in a later
///     phase) training share.
/// </summary>
/// <remarks>
///     <para>
///         The feature set is that of the averaged-perceptron spike: 22 templates per token over the token, its
///         affixes, its shape, its neighbors and the two previous tags. A trainer and the decoder must see exactly the
///         same buckets, which is why the definition lives in one place and has a version
///         (<see cref="FeatureSetVersion"/>) that is stored in every weights file.
///     </para>
///     <para>
///         Differences from the spike are deliberate. Features are hashed directly (FNV-1a over the UTF-16 code units of
///         each string, combined with a Murmur3-style mixer) instead of formatting a string and taking a CRC-32, so no
///         strings are built per token. Tokens are folded first (NFC normalization, typographic quotes, dashes and the
///         ellipsis mapped to their ASCII forms) because the training corpora are ASCII-normalized while real text is
///         not. The previous tags are passed as <see cref="Upos"/> ordinals, so a trainer decides whether to use gold or
///         predicted history.
///     </para>
///     <para>
///         All members are static; the only state is the caller's <see cref="DecoderWorkspace"/>.
///     </para>
/// </remarks>
internal static class FeatureExtractor
{
    /// <summary>
    ///     The version of the feature set; it is stored in the weights file and must match.
    /// </summary>
    public const ushort FeatureSetVersion = 1;

    /// <summary>
    ///     The most features that one token has.
    /// </summary>
    public const int MaxFeatures = 22;

    /// <summary>
    ///     The number of features that do not depend on the previous tags.
    /// </summary>
    public const int StaticFeatureCount = 18;

    /// <summary>
    ///     The history value that stands for the start of the sentence as "previous tag" (the spike's <c>-S-</c>).
    /// </summary>
    public const int StartTag1 = 17;

    /// <summary>
    ///     The history value that stands for the second position before the sentence start (the spike's <c>-S2-</c>).
    /// </summary>
    public const int StartTag2 = 18;

    /// <summary>
    ///     Token fact bit: the token contains a letter or a digit.
    /// </summary>
    public const byte FlagLetterOrDigit = 1;

    /// <summary>
    ///     Token fact bit: the token contains a digit.
    /// </summary>
    public const byte FlagDigit = 2;

    /// <summary>
    ///     Token fact bit: the first character of the token is an upper-case letter.
    /// </summary>
    public const byte FlagFirstUpper = 4;

    /// <summary>
    ///     Shape bit: the first character is an upper-case letter.
    /// </summary>
    public const byte ShapeUpper = 1;

    /// <summary>
    ///     Shape bit: the token has more than one character, has an upper-case letter and no lower-case letter.
    /// </summary>
    public const byte ShapeAllUpper = 2;

    /// <summary>
    ///     Shape bit: the token contains a digit.
    /// </summary>
    public const byte ShapeDigit = 4;

    /// <summary>
    ///     Shape bit: the token contains a hyphen.
    /// </summary>
    public const byte ShapeHyphen = 8;

    /// <summary>
    ///     Shape bit: the token contains an apostrophe.
    /// </summary>
    public const byte ShapeApostrophe = 16;

    /// <summary>
    ///     Shape bit: the token contains no letter and no digit.
    /// </summary>
    public const byte ShapePunctuation = 32;

    /// <summary>
    ///     The hash of the sentence-start sentinel word before the first token (<c>-S-</c>).
    /// </summary>
    private static readonly uint HashStart1 = HashString("-S-");

    /// <summary>
    ///     The hash of the sentinel word two positions before the first token (<c>-S2-</c>).
    /// </summary>
    private static readonly uint HashStart2 = HashString("-S2-");

    /// <summary>
    ///     The hash of the sentinel word after the last token (<c>-E-</c>).
    /// </summary>
    private static readonly uint HashEnd1 = HashString("-E-");

    /// <summary>
    ///     The hash of the sentinel word two positions after the last token (<c>-E2-</c>).
    /// </summary>
    private static readonly uint HashEnd2 = HashString("-E2-");

    /// <summary>
    ///     Loads the tokens of a sentence into a workspace and computes everything that does not depend on tags.
    /// </summary>
    /// <param name="workspace">The workspace to fill.</param>
    /// <param name="tokens">The tokens as written.</param>
    /// <param name="hashBits">The bucket space exponent of the model.</param>
    public static void Prepare(DecoderWorkspace workspace, IReadOnlyList<string> tokens, int hashBits)
    {
        workspace.Begin(tokens.Count);
        for (var i = 0; i < tokens.Count; i++)
        {
            SetToken(workspace, i, tokens[i]);
        }

        Finish(workspace, hashBits);
    }

    /// <summary>
    ///     Folds one token into the workspace and records its facts and hashes.
    /// </summary>
    /// <remarks>
    ///     Call <see cref="DecoderWorkspace.Begin"/> first, then this method for tokens <c>0..n-1</c> in order, then
    ///     <see cref="Finish"/>. Only tokens with non-ASCII characters allocate (for the NFC normalization).
    /// </remarks>
    /// <param name="workspace">The workspace.</param>
    /// <param name="index">The index of the token.</param>
    /// <param name="text">The token as written.</param>
    public static void SetToken(DecoderWorkspace workspace, int index, string text)
    {
        // Non-ASCII text is normalized to NFC first; pure ASCII (the common case) cannot change and is copied as is
        var source = text;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] >= 0x80)
            {
                source = text.Normalize(NormalizationForm.FormC);
                break;
            }
        }

        // Fold into the character buffer: every folded character takes at most three characters, plus the lower copy
        workspace.EnsureChars(6 * source.Length);
        var chars = workspace.Chars;
        var start = workspace.CharsUsed;
        var length = 0;
        foreach (var c in source)
        {
            length = AppendFolded(chars, start, length, c);
        }

        // Lower-case copy right behind the folded form, with the facts that the shape and the allowed-tag rules need
        var lowerStart = start + length;
        byte flags = 0;
        byte shape = 0;
        var hasUpper = false;
        var hasLower = false;
        for (var i = 0; i < length; i++)
        {
            var c = chars[start + i];
            chars[lowerStart + i] = char.ToLowerInvariant(c);
            if (char.IsLetterOrDigit(c))
            {
                flags |= FlagLetterOrDigit;
            }

            if (char.IsDigit(c))
            {
                flags |= FlagDigit;
                shape |= ShapeDigit;
            }

            hasUpper |= char.IsUpper(c);
            hasLower |= char.IsLower(c);
            shape |= c == '-' ? ShapeHyphen : (byte)0;
            shape |= c == '\'' ? ShapeApostrophe : (byte)0;
        }

        if (length > 0 && char.IsUpper(chars[start]))
        {
            flags |= FlagFirstUpper;
            shape |= ShapeUpper;
        }

        if (hasUpper && !hasLower && length > 1)
        {
            shape |= ShapeAllUpper;
        }

        if ((flags & FlagLetterOrDigit) == 0)
        {
            shape |= ShapePunctuation;
        }

        if (workspace.FirstAlphanumeric < 0 && (flags & FlagLetterOrDigit) != 0)
        {
            workspace.FirstAlphanumeric = index;
        }

        workspace.Start[index] = start;
        workspace.Length[index] = length;
        workspace.Flags[index] = flags;
        workspace.Shape[index] = shape;
        workspace.WordHash[index] = Hash(chars, start, length);
        workspace.LowerHash[index] = Hash(chars, lowerStart, length);
        workspace.CharsUsed = lowerStart + length;
    }

    /// <summary>
    ///     Computes the tag-independent buckets of every token after all tokens were set.
    /// </summary>
    /// <param name="workspace">The workspace.</param>
    /// <param name="hashBits">The bucket space exponent of the model.</param>
    public static void Finish(DecoderWorkspace workspace, int hashBits)
    {
        var mask = (1u << hashBits) - 1;
        var count = workspace.Count;
        var chars = workspace.Chars;
        for (var i = 0; i < count; i++)
        {
            var slot = i * StaticFeatureCount;
            var n = 0;
            var buckets = workspace.StaticBuckets;
            var lowerStart = workspace.Start[i] + workspace.Length[i];
            var length = workspace.Length[i];

            // Token features
            buckets[slot + n++] = (int)(Mix(FeatureKind.Bias, 0, 0) & mask);
            buckets[slot + n++] = (int)(Mix(FeatureKind.Word, workspace.WordHash[i], 0) & mask);
            buckets[slot + n++] = (int)(Mix(FeatureKind.Lower, workspace.LowerHash[i], 0) & mask);
            buckets[slot + n++] = (int)(Mix(FeatureKind.Shape, workspace.Shape[i], 0) & mask);

            // Prefixes and suffixes of one to four characters, only when the token is long enough
            for (var k = 1; k <= 4 && k <= length; k++)
            {
                buckets[slot + n++] = (int)(Mix(FeatureKind.Prefix1 + k - 1, Hash(chars, lowerStart, k), 0) & mask);
            }

            for (var k = 1; k <= 4 && k <= length; k++)
            {
                buckets[slot + n++] = (int)(Mix(FeatureKind.Suffix1 + k - 1, Hash(chars, lowerStart + length - k, k), 0) & mask);
            }

            // Position and neighbors
            if (i == workspace.FirstAlphanumeric)
            {
                buckets[slot + n++] = (int)(Mix(FeatureKind.Initial, 0, 0) & mask);
            }

            var previous1 = i >= 1 ? workspace.LowerHash[i - 1] : HashStart1;
            var previous2 = i >= 2 ? workspace.LowerHash[i - 2] : HashStart2;
            var next1 = i + 1 < count ? workspace.LowerHash[i + 1] : HashEnd1;
            var next2 = i + 2 < count ? workspace.LowerHash[i + 2] : HashEnd2;
            buckets[slot + n++] = (int)(Mix(FeatureKind.Prev1, previous1, 0) & mask);
            buckets[slot + n++] = (int)(Mix(FeatureKind.Prev2, previous2, 0) & mask);
            buckets[slot + n++] = (int)(Mix(FeatureKind.Next1, next1, 0) & mask);
            buckets[slot + n++] = (int)(Mix(FeatureKind.Next2, next2, 0) & mask);
            buckets[slot + n++] = (int)(Mix(FeatureKind.PrevNext, previous1, next1) & mask);
            workspace.StaticCount[i] = n;
        }
    }

    /// <summary>
    ///     Produces the buckets of one token given the two previous tags.
    /// </summary>
    /// <param name="workspace">The workspace that <see cref="Prepare"/> or <see cref="Finish"/> filled.</param>
    /// <param name="index">The token.</param>
    /// <param name="previous1">The tag before the token as <see cref="Upos"/> ordinal, or <see cref="StartTag1"/> at the start.</param>
    /// <param name="previous2">The tag two before the token as <see cref="Upos"/> ordinal, or <see cref="StartTag2"/> at the start.</param>
    /// <param name="hashBits">The bucket space exponent of the model.</param>
    /// <param name="buckets">Receives the buckets; at least <see cref="MaxFeatures"/> elements.</param>
    /// <returns>The number of buckets written.</returns>
    public static int Extract(DecoderWorkspace workspace, int index, int previous1, int previous2, int hashBits, int[] buckets)
    {
        // The tag-independent part was computed once for the sentence
        var count = workspace.StaticCount[index];
        Array.Copy(workspace.StaticBuckets, index * StaticFeatureCount, buckets, 0, count);

        // The four features that use the previous tags
        var mask = (1u << hashBits) - 1;
        var p1 = (uint)previous1;
        var p2 = (uint)previous2;
        buckets[count++] = (int)(Mix(FeatureKind.PrevTag1, p1, 0) & mask);
        buckets[count++] = (int)(Mix(FeatureKind.PrevTag2, p2, 0) & mask);
        buckets[count++] = (int)(Mix(FeatureKind.PrevTagPair, p2, p1) & mask);
        buckets[count++] = (int)(Mix(FeatureKind.PrevTagLower, p1, workspace.LowerHash[index]) & mask);
        return count;
    }

    /// <summary>
    ///     Folds a token to the form that features and the lexicon use, as a string.
    /// </summary>
    /// <remarks>
    ///     This is the readable twin of the character-buffer folding inside <see cref="SetToken"/>; the tests prove that
    ///     both agree.
    /// </remarks>
    /// <param name="token">The token as written.</param>
    /// <returns>The NFC-normalized token with typographic quotes, dashes and ellipsis mapped to ASCII.</returns>
    public static string Fold(string token)
    {
        var builder = new StringBuilder(token.Length);
        foreach (var c in token.Normalize(NormalizationForm.FormC))
        {
            switch (c)
            {
                case '\u2018':
                case '\u2019':
                case '\u201B':
                case '\u02BC':
                    builder.Append('\'');
                    break;
                case '\u201C':
                case '\u201D':
                case '\u201E':
                    builder.Append('"');
                    break;
                case '\u2013':
                case '\u2014':
                case '\u2015':
                    builder.Append("--");
                    break;
                case '\u2026':
                    builder.Append("...");
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    ///     Computes the bucket of one feature from its readable parts (the slow reference of the fast path).
    /// </summary>
    /// <remarks>
    ///     The meaning of <paramref name="a"/> and <paramref name="b"/> depends on the kind: the (folded, lower-cased where
    ///     the template says so) string for word, affix and neighbor templates; the shape letters of the spike
    ///     (<c>U A D H Q P</c> or <c>l</c>) for <see cref="FeatureKind.Shape"/>; the tag spelling (<c>NOUN</c>,
    ///     <c>-S-</c> or <c>-S2-</c>) for the tag templates, where the lower-cased word is <paramref name="b"/> for
    ///     <see cref="FeatureKind.PrevTagLower"/> and the previous tag is <paramref name="b"/> for
    ///     <see cref="FeatureKind.PrevTagPair"/> (<paramref name="a"/> is the older tag).
    /// </remarks>
    /// <param name="kind">The template.</param>
    /// <param name="a">The first value.</param>
    /// <param name="b">The second value, where the template has one.</param>
    /// <param name="hashBits">The bucket space exponent.</param>
    /// <returns>The bucket.</returns>
    public static int ReferenceBucket(FeatureKind kind, string? a, string? b, int hashBits)
    {
        uint first;
        uint second = 0;
        switch (kind)
        {
            case FeatureKind.Bias:
            case FeatureKind.Initial:
                first = 0;
                break;
            case FeatureKind.Shape:
                first = ShapeFromLetters(a ?? "l");
                break;
            case FeatureKind.PrevNext:
                first = HashString(a ?? string.Empty);
                second = HashString(b ?? string.Empty);
                break;
            case FeatureKind.PrevTag1:
            case FeatureKind.PrevTag2:
                first = TagCode(a);
                break;
            case FeatureKind.PrevTagPair:
                first = TagCode(a);
                second = TagCode(b);
                break;
            case FeatureKind.PrevTagLower:
                first = TagCode(a);
                second = HashString(b ?? string.Empty);
                break;
            default:
                first = HashString(a ?? string.Empty);
                break;
        }

        return (int)(Mix(kind, first, second) & ((1u << hashBits) - 1));
    }

    /// <summary>
    ///     Computes all buckets of one token from readable strings, the way the spike defines the features.
    /// </summary>
    /// <remarks>
    ///     Slow and allocation-heavy by design: it is the oracle that the tests compare the fast extractor with, and a
    ///     debugging aid for trainers. The buckets come in the same order as <see cref="Extract"/> produces them.
    /// </remarks>
    /// <param name="tokens">The tokens of the sentence as written.</param>
    /// <param name="index">The token.</param>
    /// <param name="previous1">The previous tag spelling (<c>NOUN</c>, <c>-S-</c>).</param>
    /// <param name="previous2">The tag spelling before that (<c>-S2-</c> at the start).</param>
    /// <param name="hashBits">The bucket space exponent.</param>
    /// <returns>The buckets.</returns>
    public static int[] ReferenceBuckets(IReadOnlyList<string> tokens, int index, string previous1, string previous2, int hashBits)
    {
        var folded = new string[tokens.Count];
        var lower = new string[tokens.Count];
        var first = -1;
        for (var i = 0; i < tokens.Count; i++)
        {
            folded[i] = Fold(tokens[i]);
            lower[i] = folded[i].ToLowerInvariant();
            if (first < 0 && folded[i].Any(char.IsLetterOrDigit))
            {
                first = i;
            }
        }

        var word = folded[index];
        var low = lower[index];
        var result = new List<int>
        {
            ReferenceBucket(FeatureKind.Bias, null, null, hashBits),
            ReferenceBucket(FeatureKind.Word, word, null, hashBits),
            ReferenceBucket(FeatureKind.Lower, low, null, hashBits),
            ReferenceBucket(FeatureKind.Shape, ShapeLetters(word), null, hashBits),
        };
        for (var k = 1; k <= 4; k++)
        {
            if (low.Length >= k)
            {
                result.Add(ReferenceBucket(FeatureKind.Prefix1 + k - 1, low.Substring(0, k), null, hashBits));
            }
        }

        for (var k = 1; k <= 4; k++)
        {
            if (low.Length >= k)
            {
                result.Add(ReferenceBucket(FeatureKind.Suffix1 + k - 1, low.Substring(low.Length - k), null, hashBits));
            }
        }

        if (index == first)
        {
            result.Add(ReferenceBucket(FeatureKind.Initial, null, null, hashBits));
        }

        var m1 = index >= 1 ? lower[index - 1] : "-S-";
        var m2 = index >= 2 ? lower[index - 2] : "-S2-";
        var n1 = index + 1 < tokens.Count ? lower[index + 1] : "-E-";
        var n2 = index + 2 < tokens.Count ? lower[index + 2] : "-E2-";
        result.Add(ReferenceBucket(FeatureKind.Prev1, m1, null, hashBits));
        result.Add(ReferenceBucket(FeatureKind.Prev2, m2, null, hashBits));
        result.Add(ReferenceBucket(FeatureKind.Next1, n1, null, hashBits));
        result.Add(ReferenceBucket(FeatureKind.Next2, n2, null, hashBits));
        result.Add(ReferenceBucket(FeatureKind.PrevNext, m1, n1, hashBits));
        result.Add(ReferenceBucket(FeatureKind.PrevTag1, previous1, null, hashBits));
        result.Add(ReferenceBucket(FeatureKind.PrevTag2, previous2, null, hashBits));
        result.Add(ReferenceBucket(FeatureKind.PrevTagPair, previous2, previous1, hashBits));
        result.Add(ReferenceBucket(FeatureKind.PrevTagLower, previous1, low, hashBits));
        return result.ToArray();
    }

    /// <summary>
    ///     Describes the shape of a folded token with the letters of the spike (<c>U</c>, <c>A</c>, <c>D</c>, <c>H</c>,
    ///     <c>Q</c>, <c>P</c> in this order, or <c>l</c> when none applies).
    /// </summary>
    /// <param name="word">The folded token.</param>
    /// <returns>The shape letters.</returns>
    public static string ShapeLetters(string word)
    {
        var letters = new StringBuilder();
        if (word.Length > 0 && char.IsUpper(word[0]))
        {
            letters.Append('U');
        }

        if (word.Length > 1 && word.Any(char.IsUpper) && !word.Any(char.IsLower))
        {
            letters.Append('A');
        }

        if (word.Any(char.IsDigit))
        {
            letters.Append('D');
        }

        if (word.IndexOf('-') >= 0)
        {
            letters.Append('H');
        }

        if (word.IndexOf('\'') >= 0)
        {
            letters.Append('Q');
        }

        if (!word.Any(char.IsLetterOrDigit))
        {
            letters.Append('P');
        }

        return letters.Length == 0 ? "l" : letters.ToString();
    }

    /// <summary>
    ///     Computes the FNV-1a hash of a string over its UTF-16 code units.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The 32-bit hash.</returns>
    public static uint HashString(string text)
    {
        var hash = BinaryFormat.FnvOffsetBasis;
        foreach (var c in text)
        {
            hash = (hash ^ c) * BinaryFormat.FnvPrime;
        }

        return hash;
    }

    /// <summary>
    ///     Computes the FNV-1a hash of a range of characters over their UTF-16 code units.
    /// </summary>
    /// <param name="chars">The buffer.</param>
    /// <param name="start">The first character.</param>
    /// <param name="length">The number of characters.</param>
    /// <returns>The 32-bit hash.</returns>
    private static uint Hash(char[] chars, int start, int length)
    {
        var hash = BinaryFormat.FnvOffsetBasis;
        for (var i = start; i < start + length; i++)
        {
            hash = (hash ^ chars[i]) * BinaryFormat.FnvPrime;
        }

        return hash;
    }

    /// <summary>
    ///     Combines the template and up to two values into a 32-bit hash (Murmur3 mixing of the two words, seeded by the template).
    /// </summary>
    /// <param name="kind">The template; seeds the hash so the same value under two templates gives different buckets.</param>
    /// <param name="a">The first value.</param>
    /// <param name="b">The second value (0 when the template has none).</param>
    /// <returns>The hash; the caller masks it to the bucket space.</returns>
    private static uint Mix(FeatureKind kind, uint a, uint b)
    {
        var hash = ((uint)kind + 1) * 0x9E3779B1u;
        hash = MixWord(hash, a);
        hash = MixWord(hash, b);
        hash ^= 8;
        hash ^= hash >> 16;
        hash *= 0x85EBCA6Bu;
        hash ^= hash >> 13;
        hash *= 0xC2B2AE35u;
        hash ^= hash >> 16;
        return hash;
    }

    /// <summary>
    ///     Mixes one 32-bit word into a running Murmur3 hash.
    /// </summary>
    /// <param name="hash">The running hash.</param>
    /// <param name="word">The word.</param>
    /// <returns>The new running hash.</returns>
    private static uint MixWord(uint hash, uint word)
    {
        word *= 0xCC9E2D51u;
        word = (word << 15) | (word >> 17);
        word *= 0x1B873593u;
        hash ^= word;
        hash = (hash << 13) | (hash >> 19);
        return (hash * 5) + 0xE6546B64u;
    }

    /// <summary>
    ///     Appends the folded form of one character to a buffer.
    /// </summary>
    /// <param name="chars">The buffer.</param>
    /// <param name="start">The index of the first folded character.</param>
    /// <param name="length">The number of folded characters so far.</param>
    /// <param name="c">The character to fold.</param>
    /// <returns>The new number of folded characters.</returns>
    private static int AppendFolded(char[] chars, int start, int length, char c)
    {
        var position = start + length;
        switch (c)
        {
            case '\u2018':
            case '\u2019':
            case '\u201B':
            case '\u02BC':
                chars[position] = '\'';
                return length + 1;
            case '\u201C':
            case '\u201D':
            case '\u201E':
                chars[position] = '"';
                return length + 1;
            case '\u2013':
            case '\u2014':
            case '\u2015':
                chars[position] = '-';
                chars[position + 1] = '-';
                return length + 2;
            case '\u2026':
                chars[position] = '.';
                chars[position + 1] = '.';
                chars[position + 2] = '.';
                return length + 3;
            default:
                chars[position] = c;
                return length + 1;
        }
    }

    /// <summary>
    ///     Converts shape letters into the shape bits.
    /// </summary>
    /// <param name="letters">The letters (<c>l</c> for none).</param>
    /// <returns>The bits.</returns>
    private static uint ShapeFromLetters(string letters)
    {
        uint bits = 0;
        foreach (var c in letters)
        {
            bits |= c switch
            {
                'U' => ShapeUpper,
                'A' => ShapeAllUpper,
                'D' => ShapeDigit,
                'H' => ShapeHyphen,
                'Q' => ShapeApostrophe,
                'P' => ShapePunctuation,
                _ => 0u,
            };
        }

        return bits;
    }

    /// <summary>
    ///     Converts a tag spelling into the history code that the features hash.
    /// </summary>
    /// <param name="spelling">A tag spelling, <c>-S-</c> or <c>-S2-</c>.</param>
    /// <returns>The code.</returns>
    /// <exception cref="ArgumentException">The spelling is none of them.</exception>
    private static uint TagCode(string? spelling)
    {
        if (spelling == "-S-")
        {
            return StartTag1;
        }

        if (spelling == "-S2-")
        {
            return StartTag2;
        }

        return UposExtensions.TryParse(spelling, out var tag)
            ? (uint)tag
            : throw new ArgumentException($"'{spelling}' is not a tag spelling or a sentence-start sentinel.", nameof(spelling));
    }
}
