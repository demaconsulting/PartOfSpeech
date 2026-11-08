// cspell:ignore EBCA FFFD aeiou
using System.Text;

namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     The templates of the features that the perceptron scores, in the order in which their buckets are produced.
/// </summary>
/// <remarks>
///     The numeric value of a member is part of the weights format: it seeds the hash of the feature, so changing the
///     value of a member requires a new <see cref="FeatureExtractor.FeatureSetVersion"/>. Members 0 to 21 are the base
///     templates; the members from 22 on belong to a <see cref="FeatureGroups"/> group and are only ever appended, so the
///     buckets of the base templates never move.
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

    /// <summary>Group <see cref="FeatureGroups.PositionCues"/>: the position of the token in the sentence, capped at three.</summary>
    PosIdx = 22,

    /// <summary>Group <see cref="FeatureGroups.PositionCues"/>: the index of the first content token (capped at two) and whether this is that token.</summary>
    FirstContent = 23,

    /// <summary>Group <see cref="FeatureGroups.PositionCues"/>: the terminal of the sentence together with the position of the token.</summary>
    EndPos = 24,

    /// <summary>Group <see cref="FeatureGroups.PositionCues"/>: the length bucket of the sentence.</summary>
    LenBucket = 25,

    /// <summary>Group <see cref="FeatureGroups.PositionCues"/>: the length bucket of the sentence together with the position of the token.</summary>
    LenPos = 26,

    /// <summary>Group <see cref="FeatureGroups.ClauseCues"/>: the token class of the previous token.</summary>
    PrevClass1 = 27,

    /// <summary>Group <see cref="FeatureGroups.ClauseCues"/>: the token class of the next token.</summary>
    NextClass1 = 28,

    /// <summary>Group <see cref="FeatureGroups.ClauseCues"/>: the pair of the token classes of the previous and the next token.</summary>
    PrevNextClass = 29,

    /// <summary>Group <see cref="FeatureGroups.ClauseCues"/>: the distance to the nearest preceding determiner or number within three tokens.</summary>
    DetDist = 30,

    /// <summary>Group <see cref="FeatureGroups.ClauseCues"/>: the distance to the nearest preceding modal or <c>to</c> within two tokens.</summary>
    ModalDist = 31,

    /// <summary>Group <see cref="FeatureGroups.ClauseCues"/>: what the predicted tags of the current clause contain so far (history feature).</summary>
    ClauseState = 32,

    /// <summary>Group <see cref="FeatureGroups.ClauseCues"/>: the clause state together with the previous tag (history feature).</summary>
    ClauseStatePrev = 33,

    /// <summary>Group <see cref="FeatureGroups.RightPreview"/>: the first-pass tag of the next token (second pass only).</summary>
    Rt1 = 34,

    /// <summary>Group <see cref="FeatureGroups.RightPreview"/>: the first-pass tag two tokens ahead (second pass only).</summary>
    Rt2 = 35,

    /// <summary>Group <see cref="FeatureGroups.RightPreview"/>: the pair of the first-pass tags one and two tokens ahead (second pass only).</summary>
    RtPair = 36,

    /// <summary>Group <see cref="FeatureGroups.RightPreview"/>: the previous tag of the current pass together with the first-pass tag of the next token (second pass only).</summary>
    RtPrev = 37,

    /// <summary>Group <see cref="FeatureGroups.Inflection"/>: the suffix kind and whether the stem is a verb or a noun in the lexicon.</summary>
    StemKind = 38,

    /// <summary>Group <see cref="FeatureGroups.Inflection"/>: the stem kind together with the previous tag (history feature).</summary>
    StemKindPrev = 39,
}

/// <summary>
///     The switchable groups of context features that extend the 22 base templates; the set of groups of a model is
///     stored in its weights file.
/// </summary>
/// <remarks>
///     <para>
///         With no group (<see cref="None"/>) the extractor produces exactly the 22 base templates, byte for byte as
///         before the groups existed. Bit 4 (value 16) is reserved for a later group and is rejected by the loader.
///     </para>
///     <para>
///         The groups are defined in <see cref="FeatureExtractor"/> (the feature definitions).
///     </para>
/// </remarks>
[Flags]
internal enum FeatureGroups : uint
{
    /// <summary>No group: the 22 base templates only.</summary>
    None = 0,

    /// <summary>Position cues: where the token is in the sentence, how long the sentence is and how it ends.</summary>
    PositionCues = 1,

    /// <summary>Clause cues: the classes of the neighbors, the distance to determiners and modals, and what the current clause contains.</summary>
    ClauseCues = 2,

    /// <summary>Right preview: the tags of the next two tokens from a first decoding pass (two-pass decoding).</summary>
    RightPreview = 4,

    /// <summary>Inflection: whether the stem of a token that ends in <c>ing</c>, <c>ed</c> or <c>s</c> is a verb or a noun in the lexicon.</summary>
    Inflection = 8,
}

/// <summary>
///     The class of a token for the clause cues and the position cues (the first matching rule wins, in the order of the members).
/// </summary>
internal enum TokenClass : byte
{
    /// <summary>The token has no letter and no digit.</summary>
    Punct = 0,

    /// <summary>One of can, could, may, might, must, shall, should, will, would, cannot.</summary>
    Modal = 1,

    /// <summary>The word <c>to</c>.</summary>
    To = 2,

    /// <summary>Only digits, or the lexicon lists NUM.</summary>
    Num = 3,

    /// <summary>The lexicon lists DET and none of NOUN, VERB, ADJ.</summary>
    Det = 4,

    /// <summary>The lexicon lists PRON and none of NOUN, VERB, ADJ.</summary>
    Pron = 5,

    /// <summary>The lexicon lists ADP and none of NOUN, VERB, ADJ.</summary>
    Adp = 6,

    /// <summary>The lexicon lists CCONJ or SCONJ and none of NOUN, VERB, ADJ.</summary>
    Conj = 7,

    /// <summary>The lexicon lists NOUN and VERB.</summary>
    NounVerb = 8,

    /// <summary>The lexicon lists NOUN and not VERB.</summary>
    NounOnly = 9,

    /// <summary>The lexicon lists VERB and not NOUN.</summary>
    VerbOnly = 10,

    /// <summary>The lexicon lists ADJ or ADV.</summary>
    AdjAdv = 11,

    /// <summary>Everything else, including spellings that are not in the lexicon.</summary>
    Other = 12,
}

/// <summary>
///     Computes the hashed feature buckets of a token: the single feature definition that decoding and training
///     share.
/// </summary>
/// <remarks>
///     <para>
///         The feature set is that of the averaged-perceptron spike: 22 templates per token over the token, its
///         affixes, its shape, its neighbors and the two previous tags. A trainer and the decoder must see exactly the
///         same buckets, which is why the definition lives in one place and has a version
///         (<see cref="FeatureSetVersion"/>) that is stored in every weights file.
///     </para>
///     <para>
///         On top of the 22 base templates, four switchable groups of context features exist (<see cref="FeatureGroups"/>):
///         position cues, clause cues, right preview and inflection. The groups of a model are stored as a mask in its
///         weights file; with the mask 0 (no group) the buckets are exactly those of the base templates. The state that
///         the groups need (the lexicon facts of the tokens, the clause state, the preview tags) lives in the
///         <see cref="DecoderWorkspace"/>, set up by <see cref="DecoderWorkspace.Configure"/> and advanced by
///         <see cref="Advance"/>, so that the decoder, the trainer and the quantized predictor run the same code.
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
    ///     The version of the feature set without a group; it is stored in the weights file and must match.
    /// </summary>
    /// <remarks>
    ///     A weights file with at least one group stores <see cref="GroupsFeatureSetVersion"/> instead (see
    ///     <see cref="FeatureSetVersionFor"/>).
    /// </remarks>
    public const ushort FeatureSetVersion = 1;

    /// <summary>
    ///     The version of the feature set that a weights file with at least one <see cref="FeatureGroups"/> group stores.
    /// </summary>
    public const ushort GroupsFeatureSetVersion = 2;

    /// <summary>
    ///     The most features that one token has without a group.
    /// </summary>
    public const int MaxFeatures = 22;

    /// <summary>
    ///     The most features that one token has with all groups (buffers that may hold group features need this size).
    /// </summary>
    /// <remarks>
    ///     18 static base features, 5 position, 5 clause and 1 inflection feature that do not depend on tags, then 4 base,
    ///     2 clause, 1 inflection and 4 preview features that do.
    /// </remarks>
    public const int BucketCapacity = 40;

    /// <summary>
    ///     The number of features that do not depend on the previous tags (without a group).
    /// </summary>
    public const int StaticFeatureCount = 18;

    /// <summary>
    ///     The mask of the groups that this build knows; a weights file with another bit set is rejected.
    /// </summary>
    public const uint KnownGroupsMask = (uint)(FeatureGroups.PositionCues | FeatureGroups.ClauseCues | FeatureGroups.RightPreview | FeatureGroups.Inflection);

    /// <summary>
    ///     The value of the boundary marker in place of a token class (before the first and after the last token).
    /// </summary>
    public const int ClassBoundary = 13;

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
    ///     The tags that a lexicon entry may list for a token of the open classes (the exceptions of the closed classes).
    /// </summary>
    private const int OpenClassMask = (1 << (int)Upos.Noun) | (1 << (int)Upos.Verb) | (1 << (int)Upos.Adj);

    /// <summary>
    ///     The tags that only function words have: a token whose lexicon tags are all among them is function-only.
    /// </summary>
    private const int FunctionOnlyMask =
        (1 << (int)Upos.Det) | (1 << (int)Upos.Pron) | (1 << (int)Upos.Adp) | (1 << (int)Upos.Cconj) | (1 << (int)Upos.Sconj)
        | (1 << (int)Upos.Part) | (1 << (int)Upos.Aux) | (1 << (int)Upos.Punct) | (1 << (int)Upos.Sym);

    /// <summary>
    ///     The predicted tags that make the clause state record a subject-like token.
    /// </summary>
    private const int SubjectLikeMask =
        (1 << (int)Upos.Det) | (1 << (int)Upos.Pron) | (1 << (int)Upos.Noun) | (1 << (int)Upos.Propn) | (1 << (int)Upos.Num);

    /// <summary>
    ///     The predicted tags that make the clause state record a verb.
    /// </summary>
    private const int VerbLikeMask = (1 << (int)Upos.Verb) | (1 << (int)Upos.Aux);

    /// <summary>
    ///     The clause state bit: a subject-like tag was predicted since the last clause boundary.
    /// </summary>
    private const int ClauseSubject = 1;

    /// <summary>
    ///     The clause state bit: a verb tag was predicted since the last clause boundary.
    /// </summary>
    private const int ClauseVerb = 2;

    /// <summary>
    ///     The words that make a token a <see cref="TokenClass.Modal"/>, lower-case.
    /// </summary>
    private static readonly string[] ModalWords = { "can", "could", "may", "might", "must", "shall", "should", "will", "would", "cannot" };

    /// <summary>
    ///     The tokens that start a new clause when they stand alone (as written after folding).
    /// </summary>
    private static readonly string[] ClauseBoundaryTokens = { ",", ";", ":", "(", "--" };

    /// <summary>
    ///     The tokens that close a quotation or a bracket (as written after folding).
    /// </summary>
    private static readonly string[] ClosingTokens = { "\"", "'", "''", ")", "]", "}" };

    /// <summary>
    ///     Gets the version that the weights file of a set of groups stores: the base version for no group, otherwise the groups version.
    /// </summary>
    /// <param name="groups">The groups.</param>
    /// <returns>The feature set version.</returns>
    public static ushort FeatureSetVersionFor(FeatureGroups groups) => groups == FeatureGroups.None ? FeatureSetVersion : GroupsFeatureSetVersion;

    /// <summary>
    ///     Gets the number of slots that the tag-independent buckets of every token take for a set of groups.
    /// </summary>
    /// <param name="groups">The groups.</param>
    /// <returns>The stride of <see cref="DecoderWorkspace.StaticBuckets"/>.</returns>
    public static int StaticStrideFor(FeatureGroups groups)
    {
        var stride = StaticFeatureCount;
        if ((groups & FeatureGroups.PositionCues) != 0)
        {
            stride += 5;
        }

        if ((groups & FeatureGroups.ClauseCues) != 0)
        {
            stride += 5;
        }

        if ((groups & FeatureGroups.Inflection) != 0)
        {
            stride += 1;
        }

        return stride;
    }

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
    ///     <see cref="Finish"/>. Only tokens with non-ASCII characters allocate (for the NFC normalization). A token
    ///     that is not well-formed UTF-16 does not fail: each unpaired surrogate counts as U+FFFD, for the features only.
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
                source = NormalizeNonAscii(text);
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
        var stride = workspace.StaticStride;
        for (var i = 0; i < count; i++)
        {
            var slot = i * stride;
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

        // The groups add their tag-independent buckets behind the base ones; without a group nothing more happens
        workspace.ClauseState = 0;
        workspace.PreviewActive = false;
        if (workspace.Groups != FeatureGroups.None)
        {
            FinishGroups(workspace, hashBits);
        }
    }

    /// <summary>
    ///     Starts a decoding pass over the prepared sentence: clears the clause state and selects whether the preview features are active.
    /// </summary>
    /// <remarks>
    ///     <see cref="Finish"/> starts a pass without preview. A decoder or trainer with the <see cref="FeatureGroups.RightPreview"/> group
    ///     calls this with <paramref name="preview"/> <c>true</c> before the second pass, after it put the tags of the first pass
    ///     into <see cref="DecoderWorkspace.Preview"/> (see <see cref="DecoderWorkspace.SetPreview"/>). Without that group the
    ///     preview stays inactive, whatever the argument. Call it also before decoding the same prepared sentence again.
    /// </remarks>
    /// <param name="workspace">The workspace that <see cref="Prepare"/> or <see cref="Finish"/> filled.</param>
    /// <param name="preview">Whether the preview features are active in this pass.</param>
    public static void StartPass(DecoderWorkspace workspace, bool preview)
    {
        workspace.ClauseState = 0;
        workspace.PreviewActive = preview && (workspace.Groups & FeatureGroups.RightPreview) != 0;
    }

    /// <summary>
    ///     Records the decision for a token as history for the features of the following tokens.
    /// </summary>
    /// <remarks>
    ///     Call it after every decision, in token order, with the tag that becomes <c>previous1</c> of the next token: the
    ///     decoder passes the chosen tag, the trainer the tag of its history mode. It updates the clause state of the
    ///     <see cref="FeatureGroups.ClauseCues"/> group and is a no-op for every other set of groups, including none.
    /// </remarks>
    /// <param name="workspace">The workspace.</param>
    /// <param name="index">The token that was decided.</param>
    /// <param name="tag">The tag of the token as <see cref="Upos"/> ordinal.</param>
    public static void Advance(DecoderWorkspace workspace, int index, int tag)
    {
        if ((workspace.Groups & FeatureGroups.ClauseCues) == 0)
        {
            return;
        }

        // A clause boundary (a punctuation token of the list, or a predicted conjunction) clears the state; any other token adds to it
        if (workspace.ClauseBoundary[index] != 0 || tag == (int)Upos.Sconj || tag == (int)Upos.Cconj)
        {
            workspace.ClauseState = 0;
            return;
        }

        var bit = 1 << tag;
        var state = workspace.ClauseState;
        if ((bit & SubjectLikeMask) != 0)
        {
            state |= ClauseSubject;
        }

        if ((bit & VerbLikeMask) != 0)
        {
            state |= ClauseVerb;
        }

        workspace.ClauseState = state;
    }

    /// <summary>
    ///     Produces the buckets of one token given the two previous tags.
    /// </summary>
    /// <param name="workspace">The workspace that <see cref="Prepare"/> or <see cref="Finish"/> filled.</param>
    /// <param name="index">The token.</param>
    /// <param name="previous1">The tag before the token as <see cref="Upos"/> ordinal, or <see cref="StartTag1"/> at the start.</param>
    /// <param name="previous2">The tag two before the token as <see cref="Upos"/> ordinal, or <see cref="StartTag2"/> at the start.</param>
    /// <param name="hashBits">The bucket space exponent of the model.</param>
    /// <param name="buckets">Receives the buckets; at least <see cref="MaxFeatures"/> elements without a group, <see cref="BucketCapacity"/> with groups.</param>
    /// <returns>The number of buckets written.</returns>
    public static int Extract(DecoderWorkspace workspace, int index, int previous1, int previous2, int hashBits, int[] buckets)
    {
        // The tag-independent part was computed once for the sentence
        var count = workspace.StaticCount[index];
        Array.Copy(workspace.StaticBuckets, index * workspace.StaticStride, buckets, 0, count);

        // The four features that use the previous tags
        var mask = (1u << hashBits) - 1;
        var p1 = (uint)previous1;
        var p2 = (uint)previous2;
        buckets[count++] = (int)(Mix(FeatureKind.PrevTag1, p1, 0) & mask);
        buckets[count++] = (int)(Mix(FeatureKind.PrevTag2, p2, 0) & mask);
        buckets[count++] = (int)(Mix(FeatureKind.PrevTagPair, p2, p1) & mask);
        buckets[count++] = (int)(Mix(FeatureKind.PrevTagLower, p1, workspace.LowerHash[index]) & mask);
        if (workspace.Groups != FeatureGroups.None)
        {
            count = ExtractGroups(workspace, index, p1, mask, buckets, count);
        }

        return count;
    }

    /// <summary>
    ///     Appends the tag-independent buckets of the groups to the buckets of every token, after the base ones.
    /// </summary>
    /// <remarks>
    ///     The order is position cues, clause cues, inflection. The lexicon facts of the tokens are looked up once per
    ///     sentence here (position cues, clause cues and right preview) and cached in the workspace, where the decoder reuses them.
    /// </remarks>
    /// <param name="workspace">The workspace whose base buckets were just computed.</param>
    /// <param name="hashBits">The bucket space exponent of the model.</param>
    private static void FinishGroups(DecoderWorkspace workspace, int hashBits)
    {
        var groups = workspace.Groups;
        var count = workspace.Count;
        var mask = (1u << hashBits) - 1;
        var position = (groups & FeatureGroups.PositionCues) != 0;
        var clause = (groups & FeatureGroups.ClauseCues) != 0;
        var inflection = (groups & FeatureGroups.Inflection) != 0;
        workspace.EnsureGroupArrays(count);

        // Lexicon facts of every token (one lookup each), then the sentence-level facts that the groups share
        workspace.LexCached = false;
        if (position || clause || (groups & FeatureGroups.RightPreview) != 0)
        {
            FillLexicon(workspace);
        }

        var firstContent = count;
        var terminal = 0;
        var lengthBucket = 0;
        if (position)
        {
            for (var i = 0; i < count; i++)
            {
                var tags = workspace.LexMask[i];
                if (tags == 0 || (tags & ~FunctionOnlyMask) != 0)
                {
                    firstContent = i;
                    break;
                }
            }

            terminal = SentenceTerminal(workspace);
            lengthBucket = LengthBucket(count);
        }

        if (clause)
        {
            for (var i = 0; i < count; i++)
            {
                workspace.Classes[i] = (byte)ClassOf(workspace, i);
                workspace.ClauseBoundary[i] = TokenIsOneOf(workspace, i, ClauseBoundaryTokens) ? (byte)1 : (byte)0;
            }
        }

        if (inflection)
        {
            // The stem candidates are built behind the last token; make room for the longest token and one more character
            var longest = 0;
            for (var i = 0; i < count; i++)
            {
                longest = Math.Max(longest, workspace.Length[i]);
            }

            workspace.EnsureChars(longest + 1);
        }

        var stride = workspace.StaticStride;
        var buckets = workspace.StaticBuckets;
        for (var i = 0; i < count; i++)
        {
            var slot = i * stride;
            var n = workspace.StaticCount[i];
            if (position)
            {
                var pos = (uint)Math.Min(i, 3);
                buckets[slot + n++] = (int)(Mix(FeatureKind.PosIdx, pos, 0) & mask);
                buckets[slot + n++] = (int)(Mix(FeatureKind.FirstContent, (uint)Math.Min(firstContent, 2), i == firstContent ? 1u : 0u) & mask);
                buckets[slot + n++] = (int)(Mix(FeatureKind.EndPos, (uint)terminal, pos) & mask);
                buckets[slot + n++] = (int)(Mix(FeatureKind.LenBucket, (uint)lengthBucket, 0) & mask);
                buckets[slot + n++] = (int)(Mix(FeatureKind.LenPos, (uint)lengthBucket, pos) & mask);
            }

            if (clause)
            {
                var previousClass = i >= 1 ? (uint)workspace.Classes[i - 1] : ClassBoundary;
                var nextClass = i + 1 < count ? (uint)workspace.Classes[i + 1] : ClassBoundary;
                buckets[slot + n++] = (int)(Mix(FeatureKind.PrevClass1, previousClass, 0) & mask);
                buckets[slot + n++] = (int)(Mix(FeatureKind.NextClass1, nextClass, 0) & mask);
                buckets[slot + n++] = (int)(Mix(FeatureKind.PrevNextClass, previousClass, nextClass) & mask);
                buckets[slot + n++] = (int)(Mix(FeatureKind.DetDist, (uint)DistanceToClass(workspace, i, 3, TokenClass.Det, TokenClass.Num), 0) & mask);
                buckets[slot + n++] = (int)(Mix(FeatureKind.ModalDist, (uint)DistanceToClass(workspace, i, 2, TokenClass.Modal, TokenClass.To), 0) & mask);
            }

            if (inflection)
            {
                var stem = StemValueOf(workspace, i);
                workspace.StemValue[i] = stem;
                if (stem != 0)
                {
                    buckets[slot + n++] = (int)(Mix(FeatureKind.StemKind, stem, 0) & mask);
                }
            }

            workspace.StaticCount[i] = n;
        }

        workspace.FirstContent = firstContent;
    }

    /// <summary>
    ///     Appends the buckets of the groups that depend on the decoding state (the history features and the preview features).
    /// </summary>
    /// <param name="workspace">The workspace.</param>
    /// <param name="index">The token.</param>
    /// <param name="previous1">The tag before the token as <see cref="Upos"/> ordinal or <see cref="StartTag1"/>.</param>
    /// <param name="mask">The bucket mask.</param>
    /// <param name="buckets">The bucket buffer.</param>
    /// <param name="count">The number of buckets so far.</param>
    /// <returns>The new number of buckets.</returns>
    private static int ExtractGroups(DecoderWorkspace workspace, int index, uint previous1, uint mask, int[] buckets, int count)
    {
        var groups = workspace.Groups;
        if ((groups & FeatureGroups.ClauseCues) != 0)
        {
            var state = (uint)workspace.ClauseState;
            buckets[count++] = (int)(Mix(FeatureKind.ClauseState, state, 0) & mask);
            buckets[count++] = (int)(Mix(FeatureKind.ClauseStatePrev, state, previous1) & mask);
        }

        if ((groups & FeatureGroups.Inflection) != 0)
        {
            var stem = workspace.StemValue[index];
            if (stem != 0)
            {
                buckets[count++] = (int)(Mix(FeatureKind.StemKindPrev, stem, previous1) & mask);
            }
        }

        if (workspace.PreviewActive)
        {
            // The first-pass tags one and two tokens ahead; StartTag1 stands for the end of the sentence and StartTag2 for beyond it
            var next = index + 1;
            var after = index + 2;
            var tag1 = next < workspace.Count ? (uint)workspace.Preview[next] : (uint)StartTag1;
            uint tag2 = StartTag1;
            if (after < workspace.Count)
            {
                tag2 = (uint)workspace.Preview[after];
            }
            else if (after > workspace.Count)
            {
                tag2 = StartTag2;
            }
            buckets[count++] = (int)(Mix(FeatureKind.Rt1, tag1, 0) & mask);
            buckets[count++] = (int)(Mix(FeatureKind.Rt2, tag2, 0) & mask);
            buckets[count++] = (int)(Mix(FeatureKind.RtPair, tag1, tag2) & mask);
            buckets[count++] = (int)(Mix(FeatureKind.RtPrev, previous1, tag1) & mask);
        }

        return count;
    }

    /// <summary>
    ///     Looks up every token of the workspace in its lexicon and caches the tag set of the token.
    /// </summary>
    /// <remarks>
    ///     Without a lexicon every token counts as not in the lexicon (empty tag set).
    /// </remarks>
    /// <param name="workspace">The workspace.</param>
    private static void FillLexicon(DecoderWorkspace workspace)
    {
        var lexicon = workspace.Lexicon;
        for (var i = 0; i < workspace.Count; i++)
        {
            var known = false;
            var tags = UposSet.Empty;
            if (lexicon != null)
            {
                var length = workspace.Length[i];
                known = lexicon.TryGetTags(workspace.Chars, workspace.Start[i] + length, length, workspace.LexiconScratch, out tags);
            }

            workspace.LexKnown[i] = known ? (byte)1 : (byte)0;
            workspace.LexMask[i] = known ? tags.Mask : 0;
        }

        workspace.LexCached = true;
    }

    /// <summary>
    ///     Classifies a token (the first matching rule of <see cref="TokenClass"/> wins).
    /// </summary>
    /// <param name="workspace">The workspace with the lexicon facts of the token.</param>
    /// <param name="index">The token.</param>
    /// <returns>The class.</returns>
    private static TokenClass ClassOf(DecoderWorkspace workspace, int index)
    {
        if ((workspace.Flags[index] & FlagLetterOrDigit) == 0)
        {
            return TokenClass.Punct;
        }

        var chars = workspace.Chars;
        var length = workspace.Length[index];
        var lowerStart = workspace.Start[index] + length;
        if (IsOneOf(chars, lowerStart, length, ModalWords))
        {
            return TokenClass.Modal;
        }

        if (length == 2 && chars[lowerStart] == 't' && chars[lowerStart + 1] == 'o')
        {
            return TokenClass.To;
        }

        var tags = workspace.LexMask[index];
        if ((tags & (1 << (int)Upos.Num)) != 0 || AllDigits(chars, lowerStart, length))
        {
            return TokenClass.Num;
        }

        if ((tags & OpenClassMask) == 0)
        {
            if ((tags & (1 << (int)Upos.Det)) != 0)
            {
                return TokenClass.Det;
            }

            if ((tags & (1 << (int)Upos.Pron)) != 0)
            {
                return TokenClass.Pron;
            }

            if ((tags & (1 << (int)Upos.Adp)) != 0)
            {
                return TokenClass.Adp;
            }

            if ((tags & ((1 << (int)Upos.Cconj) | (1 << (int)Upos.Sconj))) != 0)
            {
                return TokenClass.Conj;
            }
        }

        var noun = (tags & (1 << (int)Upos.Noun)) != 0;
        var verb = (tags & (1 << (int)Upos.Verb)) != 0;
        if (noun && verb)
        {
            return TokenClass.NounVerb;
        }

        if (noun)
        {
            return TokenClass.NounOnly;
        }

        if (verb)
        {
            return TokenClass.VerbOnly;
        }

        return (tags & ((1 << (int)Upos.Adj) | (1 << (int)Upos.Adv))) != 0 ? TokenClass.AdjAdv : TokenClass.Other;
    }

    /// <summary>
    ///     Finds the distance to the nearest preceding token of one of two classes.
    /// </summary>
    /// <param name="workspace">The workspace with the token classes.</param>
    /// <param name="index">The token.</param>
    /// <param name="window">The farthest distance that counts.</param>
    /// <param name="first">The first class.</param>
    /// <param name="second">The second class.</param>
    /// <returns>The distance (1 is the previous token), or 0 when there is none within the window.</returns>
    private static int DistanceToClass(DecoderWorkspace workspace, int index, int window, TokenClass first, TokenClass second)
    {
        for (var distance = 1; distance <= window && index - distance >= 0; distance++)
        {
            var tokenClass = (TokenClass)workspace.Classes[index - distance];
            if (tokenClass == first || tokenClass == second)
            {
                return distance;
            }
        }

        return 0;
    }

    /// <summary>
    ///     Classifies how the sentence ends: <c>.</c> (0), <c>!</c> (1), <c>?</c> (2), <c>:</c> (3), another token or none (4).
    /// </summary>
    /// <remarks>
    ///     A final closing quote or bracket is skipped (one token), so the terminal before it counts.
    /// </remarks>
    /// <param name="workspace">The workspace.</param>
    /// <returns>The terminal code.</returns>
    private static int SentenceTerminal(DecoderWorkspace workspace)
    {
        var last = workspace.Count - 1;
        if (last >= 0 && TokenIsOneOf(workspace, last, ClosingTokens))
        {
            last--;
        }

        if (last < 0)
        {
            return 4;
        }

        // Only a single-character token can be one of the four terminals
        if (workspace.Length[last] == 1)
        {
            var c = workspace.Chars[workspace.Start[last]];
            return c switch
            {
                '.' => 0,
                '!' => 1,
                '?' => 2,
                ':' => 3,
                _ => 4,
            };
        }

        return 4;
    }

    /// <summary>
    ///     Gets the bucket of a sentence length: 1-3 (0), 4-6 (1), 7-10 (2), 11-15 (3), 16-25 (4), 26 and more (5).
    /// </summary>
    /// <param name="count">The number of tokens.</param>
    /// <returns>The bucket.</returns>
    private static int LengthBucket(int count)
    {
        if (count <= 3)
        {
            return 0;
        }

        if (count <= 6)
        {
            return 1;
        }

        if (count <= 10)
        {
            return 2;
        }

        if (count <= 15)
        {
            return 3;
        }

        return count <= 25 ? 4 : 5;
    }

    /// <summary>
    ///     Computes the suffix kind and the stem facts of an inflected token.
    /// </summary>
    /// <remarks>
    ///     A token that ends in <c>ing</c> (kind 1), otherwise <c>ed</c> (kind 2), otherwise <c>s</c> (kind 3) has stem
    ///     candidates of at least two characters, at most three lexicon lookups: for <c>ing</c> and <c>ed</c> the stem, the
    ///     stem with <c>e</c> restored and the stem with a doubled final consonant undone (and <c>ied</c> to <c>y</c>); for
    ///     <c>s</c> the token without <c>s</c> (not after <c>ss</c>), without <c>es</c> and with <c>ies</c> replaced by <c>y</c>.
    ///     The value has the kind in bits 0-1, "a candidate lists VERB" in bit 2 and "a candidate lists NOUN" in bit 3.
    /// </remarks>
    /// <param name="workspace">The workspace; the candidates are built behind its last token.</param>
    /// <param name="index">The token.</param>
    /// <returns>The value, or 0 for a token without such a suffix.</returns>
    private static byte StemValueOf(DecoderWorkspace workspace, int index)
    {
        var length = workspace.Length[index];
        var chars = workspace.Chars;
        var start = workspace.Start[index] + length;
        var end = start + length;
        int kind;
        if (length >= 3 && chars[end - 3] == 'i' && chars[end - 2] == 'n' && chars[end - 1] == 'g')
        {
            kind = 1;
        }
        else if (length >= 2 && chars[end - 2] == 'e' && chars[end - 1] == 'd')
        {
            kind = 2;
        }
        else if (length >= 1 && chars[end - 1] == 's')
        {
            kind = 3;
        }
        else
        {
            return 0;
        }

        var tags = 0;
        if (kind == 3)
        {
            var endsEs = length >= 2 && chars[end - 2] == 'e';
            if (!(length >= 2 && chars[end - 2] == 's') && length - 1 >= 2)
            {
                tags |= LookupTags(workspace, start, length - 1);
            }

            if (endsEs && length - 2 >= 2)
            {
                tags |= LookupTags(workspace, start, length - 2);
            }

            if (endsEs && length >= 3 && chars[end - 3] == 'i' && length - 3 >= 2)
            {
                tags |= LookupTagsWith(workspace, start, length - 3, 'y');
            }
        }
        else
        {
            var stem = length - (kind == 1 ? 3 : 2);
            if (stem >= 2)
            {
                tags |= LookupTags(workspace, start, stem);
                tags |= LookupTagsWith(workspace, start, stem, 'e');
                if (stem >= 3 && chars[start + stem - 1] == chars[start + stem - 2] && IsConsonant(chars[start + stem - 1]))
                {
                    tags |= LookupTags(workspace, start, stem - 1);
                }
            }

            if (kind == 2 && length >= 5 && chars[end - 3] == 'i')
            {
                tags |= LookupTagsWith(workspace, start, length - 3, 'y');
            }
        }

        var value = kind;
        value |= (tags & (1 << (int)Upos.Verb)) != 0 ? 4 : 0;
        value |= (tags & (1 << (int)Upos.Noun)) != 0 ? 8 : 0;
        return (byte)value;
    }

    /// <summary>
    ///     Looks up a part of the lower-cased token in the lexicon of the workspace.
    /// </summary>
    /// <param name="workspace">The workspace.</param>
    /// <param name="offset">The index in <see cref="DecoderWorkspace.Chars"/> of the first character.</param>
    /// <param name="length">The number of characters.</param>
    /// <returns>The tag mask, 0 when the spelling is not in the lexicon (or there is no lexicon).</returns>
    private static int LookupTags(DecoderWorkspace workspace, int offset, int length)
    {
        var lexicon = workspace.Lexicon;
        if (lexicon == null)
        {
            return 0;
        }

        return lexicon.TryGetTags(workspace.Chars, offset, length, workspace.LexiconScratch, out var tags) ? tags.Mask : 0;
    }

    /// <summary>
    ///     Looks up a part of the lower-cased token with one more character in the lexicon of the workspace.
    /// </summary>
    /// <param name="workspace">The workspace.</param>
    /// <param name="offset">The index in <see cref="DecoderWorkspace.Chars"/> of the first character.</param>
    /// <param name="length">The number of characters of the part.</param>
    /// <param name="extra">The character to append (the part is copied behind the last token).</param>
    /// <returns>The tag mask, 0 when the spelling is not in the lexicon (or there is no lexicon).</returns>
    private static int LookupTagsWith(DecoderWorkspace workspace, int offset, int length, char extra)
    {
        if (workspace.Lexicon == null)
        {
            return 0;
        }

        // The area behind the last token was reserved by FinishGroups
        var scratch = workspace.CharsUsed;
        Array.Copy(workspace.Chars, offset, workspace.Chars, scratch, length);
        workspace.Chars[scratch + length] = extra;
        return LookupTags(workspace, scratch, length + 1);
    }

    /// <summary>
    ///     Determines whether a lower-case character is an ASCII consonant letter.
    /// </summary>
    /// <param name="c">The character.</param>
    /// <returns><c>true</c> for a letter other than a, e, i, o, u.</returns>
    private static bool IsConsonant(char c) => c >= 'a' && c <= 'z' && c != 'a' && c != 'e' && c != 'i' && c != 'o' && c != 'u';

    /// <summary>
    ///     Determines whether a range of characters consists of digits only.
    /// </summary>
    /// <param name="chars">The buffer.</param>
    /// <param name="start">The first character.</param>
    /// <param name="length">The number of characters.</param>
    /// <returns><c>true</c> when the range is not empty and every character is a digit.</returns>
    private static bool AllDigits(char[] chars, int start, int length)
    {
        for (var i = 0; i < length; i++)
        {
            if (!char.IsDigit(chars[start + i]))
            {
                return false;
            }
        }

        return length > 0;
    }

    /// <summary>
    ///     Determines whether a range of characters equals one of the words.
    /// </summary>
    /// <param name="chars">The buffer.</param>
    /// <param name="start">The first character.</param>
    /// <param name="length">The number of characters.</param>
    /// <param name="words">The candidates.</param>
    /// <returns><c>true</c> when one of the words is equal to the range.</returns>
    private static bool IsOneOf(char[] chars, int start, int length, string[] words)
    {
        foreach (var word in words)
        {
            if (word.Length != length)
            {
                continue;
            }

            var same = true;
            for (var i = 0; i < length && same; i++)
            {
                same = chars[start + i] == word[i];
            }

            if (same)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Determines whether the folded form of a token equals one of the texts.
    /// </summary>
    /// <param name="workspace">The workspace.</param>
    /// <param name="index">The token.</param>
    /// <param name="texts">The texts.</param>
    /// <returns><c>true</c> when one is equal.</returns>
    private static bool TokenIsOneOf(DecoderWorkspace workspace, int index, string[] texts) =>
        IsOneOf(workspace.Chars, workspace.Start[index], workspace.Length[index], texts);

    /// <summary>
    ///     Folds a token to the form that features and the lexicon use, as a string.
    /// </summary>
    /// <remarks>
    ///     This is the readable twin of the character-buffer folding inside <see cref="SetToken"/>; the tests prove that
    ///     both agree.
    /// </remarks>
    /// <param name="token">The token as written.</param>
    /// <returns>The NFC-normalized token with typographic quotes, dashes and ellipsis mapped to ASCII; an unpaired surrogate becomes U+FFFD.</returns>
    public static string Fold(string token)
    {
        var builder = new StringBuilder(token.Length);
        foreach (var c in NormalizeNonAscii(token))
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
    ///     Normalizes a token to NFC without ever failing, even for text that is not well-formed UTF-16.
    /// </summary>
    /// <remarks>
    ///     <see cref="string.Normalize(NormalizationForm)"/> throws <see cref="ArgumentException"/> for an unpaired
    ///     surrogate, so each one is replaced by U+FFFD first (for the features only; the caller's text is never
    ///     changed). A well-formed token is not copied. Should a runtime still reject the text (for example, on
    ///     .NET Framework with other invalid code points), the text with the replacements is used without
    ///     normalization. The result depends only on the text, so it is deterministic.
    /// </remarks>
    /// <param name="text">The token as written.</param>
    /// <returns>The normalized, well-formed text.</returns>
    private static string NormalizeNonAscii(string text)
    {
        var clean = ReplaceUnpairedSurrogates(text);
        try
        {
            return clean.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            return clean;
        }
    }

    /// <summary>
    ///     Replaces every unpaired surrogate by U+FFFD; a valid surrogate pair is kept.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns><paramref name="text"/> itself when it is well-formed, otherwise a corrected copy.</returns>
    private static string ReplaceUnpairedSurrogates(string text)
    {
        char[]? copy = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (!char.IsSurrogate(c))
            {
                continue;
            }

            // A high surrogate is valid when a low one follows it, and a low surrogate when a high one precedes it
            var paired = char.IsHighSurrogate(c)
                ? i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])
                : i > 0 && char.IsHighSurrogate(text[i - 1]);
            if (paired)
            {
                continue;
            }

            copy ??= text.ToCharArray();
            copy[i] = '\uFFFD';
        }

        return copy is null ? text : new string(copy);
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
    public static int[] ReferenceBuckets(IReadOnlyList<string> tokens, int index, string previous1, string previous2, int hashBits) =>
        ReferenceBuckets(tokens, index, previous1, previous2, hashBits, FeatureGroups.None, null);

    /// <summary>
    ///     Computes all buckets of one token from readable strings, including the buckets of feature groups.
    /// </summary>
    /// <remarks>
    ///     The group features are computed with string operations that follow the written definition of each group, not the
    ///     character-buffer code of the fast path; the order of the buckets is the one of <see cref="Extract"/>: the base
    ///     buckets that do not depend on tags, then those of the position, clause and inflection groups, then the four tag
    ///     features, then the history features of the clause and inflection groups, then the preview features.
    /// </remarks>
    /// <param name="tokens">The tokens of the sentence as written.</param>
    /// <param name="index">The token.</param>
    /// <param name="previous1">The previous tag spelling (<c>NOUN</c>, <c>-S-</c>).</param>
    /// <param name="previous2">The tag spelling before that (<c>-S2-</c> at the start).</param>
    /// <param name="hashBits">The bucket space exponent.</param>
    /// <param name="groups">The groups that are on.</param>
    /// <param name="lexicon">The lexicon that the groups read, or <c>null</c> for none (every spelling is unknown).</param>
    /// <param name="history">The tag spellings chosen for the tokens before <paramref name="index"/> (the clause state reads them); needed with <see cref="FeatureGroups.ClauseCues"/>.</param>
    /// <param name="preview">The first-pass tag spelling of every token for the second pass of <see cref="FeatureGroups.RightPreview"/>, or <c>null</c> for the first pass.</param>
    /// <returns>The buckets.</returns>
    /// <exception cref="ArgumentException"><see cref="FeatureGroups.ClauseCues"/> is on and <paramref name="history"/> is <c>null</c> or too short.</exception>
    public static int[] ReferenceBuckets(
        IReadOnlyList<string> tokens,
        int index,
        string previous1,
        string previous2,
        int hashBits,
        FeatureGroups groups,
        Lexicon? lexicon,
        IReadOnlyList<string>? history = null,
        IReadOnlyList<string>? preview = null)
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
        ReferenceGroupStatics(result, tokens, folded, lower, index, hashBits, groups, lexicon);
        result.Add(ReferenceBucket(FeatureKind.PrevTag1, previous1, null, hashBits));
        result.Add(ReferenceBucket(FeatureKind.PrevTag2, previous2, null, hashBits));
        result.Add(ReferenceBucket(FeatureKind.PrevTagPair, previous2, previous1, hashBits));
        result.Add(ReferenceBucket(FeatureKind.PrevTagLower, previous1, low, hashBits));
        ReferenceGroupHistory(result, folded, lower, index, previous1, hashBits, groups, lexicon, history, preview);
        return result.ToArray();
    }

    /// <summary>
    ///     Computes the bucket of a group feature from its numeric values (the slow reference of the fast path).
    /// </summary>
    /// <param name="kind">The template.</param>
    /// <param name="a">The first value.</param>
    /// <param name="b">The second value (0 when the template has none).</param>
    /// <param name="hashBits">The bucket space exponent.</param>
    /// <returns>The bucket.</returns>
    public static int ReferenceBucketOfCodes(FeatureKind kind, uint a, uint b, int hashBits) =>
        (int)(Mix(kind, a, b) & ((1u << hashBits) - 1));

    /// <summary>
    ///     Classifies a token from its readable forms and its lexicon tags (the slow reference of the class of the fast path).
    /// </summary>
    /// <param name="folded">The folded token.</param>
    /// <param name="lower">The lower-cased folded token.</param>
    /// <param name="tags">The tags that the lexicon lists for <paramref name="lower"/> (empty when it is not listed).</param>
    /// <returns>The class.</returns>
    public static TokenClass ReferenceTokenClass(string folded, string lower, UposSet tags)
    {
        if (!folded.Any(char.IsLetterOrDigit))
        {
            return TokenClass.Punct;
        }

        if (ModalWords.Contains(lower))
        {
            return TokenClass.Modal;
        }

        if (lower == "to")
        {
            return TokenClass.To;
        }

        if (tags.Contains(Upos.Num) || lower.All(char.IsDigit))
        {
            return TokenClass.Num;
        }

        var open = tags.Contains(Upos.Noun) || tags.Contains(Upos.Verb) || tags.Contains(Upos.Adj);
        if (!open && tags.Contains(Upos.Det))
        {
            return TokenClass.Det;
        }

        if (!open && tags.Contains(Upos.Pron))
        {
            return TokenClass.Pron;
        }

        if (!open && tags.Contains(Upos.Adp))
        {
            return TokenClass.Adp;
        }

        if (!open && (tags.Contains(Upos.Cconj) || tags.Contains(Upos.Sconj)))
        {
            return TokenClass.Conj;
        }

        if (tags.Contains(Upos.Noun))
        {
            return tags.Contains(Upos.Verb) ? TokenClass.NounVerb : TokenClass.NounOnly;
        }

        if (tags.Contains(Upos.Verb))
        {
            return TokenClass.VerbOnly;
        }

        return tags.Contains(Upos.Adj) || tags.Contains(Upos.Adv) ? TokenClass.AdjAdv : TokenClass.Other;
    }

    /// <summary>
    ///     Computes the suffix kind and stem facts of a lower-cased token with string operations (the slow reference of the fast path).
    /// </summary>
    /// <param name="lower">The lower-cased folded token.</param>
    /// <param name="lexicon">The lexicon, or <c>null</c> for none.</param>
    /// <returns>The value (kind 1 <c>ing</c>, 2 <c>ed</c>, 3 <c>s</c> in bits 0-1; a stem lists VERB in bit 2; a stem lists NOUN in bit 3), or 0 when the token has none of the suffixes.</returns>
    public static int ReferenceStemValue(string lower, Lexicon? lexicon)
    {
        var candidates = new List<string>();
        int kind;
        if (lower.EndsWith("ing", StringComparison.Ordinal))
        {
            kind = 1;
        }
        else if (lower.EndsWith("ed", StringComparison.Ordinal))
        {
            kind = 2;
        }
        else if (lower.EndsWith("s", StringComparison.Ordinal))
        {
            kind = 3;
        }
        else
        {
            return 0;
        }

        if (kind == 3)
        {
            if (!lower.EndsWith("ss", StringComparison.Ordinal) && lower.Length - 1 >= 2)
            {
                candidates.Add(lower.Substring(0, lower.Length - 1));
            }

            if (lower.EndsWith("es", StringComparison.Ordinal) && lower.Length - 2 >= 2)
            {
                candidates.Add(lower.Substring(0, lower.Length - 2));
            }

            if (lower.EndsWith("ies", StringComparison.Ordinal) && lower.Length - 3 >= 2)
            {
                candidates.Add(lower.Substring(0, lower.Length - 3) + "y");
            }
        }
        else
        {
            var stem = lower.Substring(0, lower.Length - (kind == 1 ? 3 : 2));
            if (stem.Length >= 2)
            {
                candidates.Add(stem);
                candidates.Add(stem + "e");
                var last = stem[stem.Length - 1];
                if (stem.Length >= 3 && stem[stem.Length - 2] == last && last >= 'a' && last <= 'z' && "aeiou".IndexOf(last) < 0)
                {
                    candidates.Add(stem.Substring(0, stem.Length - 1));
                }
            }

            if (kind == 2 && lower.EndsWith("ied", StringComparison.Ordinal) && lower.Length >= 5)
            {
                candidates.Add(lower.Substring(0, lower.Length - 3) + "y");
            }
        }

        var verb = candidates.Any(c => ReferenceLexiconTags(lexicon, c).Contains(Upos.Verb));
        var noun = candidates.Any(c => ReferenceLexiconTags(lexicon, c).Contains(Upos.Noun));
        return kind | (verb ? 4 : 0) | (noun ? 8 : 0);
    }

    /// <summary>
    ///     Looks up a lower-cased spelling in a lexicon with plain calls.
    /// </summary>
    /// <param name="lexicon">The lexicon, or <c>null</c>.</param>
    /// <param name="lower">The spelling.</param>
    /// <returns>The tags of the spelling; empty when it is not listed or there is no lexicon.</returns>
    private static UposSet ReferenceLexiconTags(Lexicon? lexicon, string lower)
    {
        if (lexicon == null)
        {
            return UposSet.Empty;
        }

        var key = lower.ToCharArray();
        return lexicon.TryGetTags(key, 0, key.Length, new byte[lexicon.ScratchSize], out var tags) ? tags : UposSet.Empty;
    }

    /// <summary>
    ///     Appends the tag-independent group buckets of one token, computed from readable forms.
    /// </summary>
    /// <param name="result">Receives the buckets.</param>
    /// <param name="tokens">The tokens as written.</param>
    /// <param name="folded">The folded tokens.</param>
    /// <param name="lower">The lower-cased folded tokens.</param>
    /// <param name="index">The token.</param>
    /// <param name="hashBits">The bucket space exponent.</param>
    /// <param name="groups">The groups.</param>
    /// <param name="lexicon">The lexicon.</param>
    private static void ReferenceGroupStatics(
        List<int> result,
        IReadOnlyList<string> tokens,
        string[] folded,
        string[] lower,
        int index,
        int hashBits,
        FeatureGroups groups,
        Lexicon? lexicon)
    {
        var count = tokens.Count;
        if (groups == FeatureGroups.None)
        {
            return;
        }

        var tags = lower.Select(word => ReferenceLexiconTags(lexicon, word)).ToArray();
        if ((groups & FeatureGroups.PositionCues) != 0)
        {
            var pos = (uint)Math.Min(index, 3);
            var firstContent = count;
            for (var i = 0; i < count; i++)
            {
                if (tags[i].IsEmpty || (tags[i].Mask & ~FunctionOnlyMask) != 0)
                {
                    firstContent = i;
                    break;
                }
            }

            var last = count - 1;
            if (last >= 0 && ClosingTokens.Contains(folded[last]))
            {
                last--;
            }

            var terminal = 4;
            if (last >= 0)
            {
                var found = Array.IndexOf(new[] { ".", "!", "?", ":" }, folded[last]);
                terminal = found >= 0 ? found : 4;
            }

            var bucket = 5;
            var upperBounds = new[] { 3, 6, 10, 15, 25 };
            for (var b = upperBounds.Length - 1; b >= 0; b--)
            {
                if (count <= upperBounds[b])
                {
                    bucket = b;
                }
            }
            result.Add(ReferenceBucketOfCodes(FeatureKind.PosIdx, pos, 0, hashBits));
            result.Add(ReferenceBucketOfCodes(FeatureKind.FirstContent, (uint)Math.Min(firstContent, 2), index == firstContent ? 1u : 0u, hashBits));
            result.Add(ReferenceBucketOfCodes(FeatureKind.EndPos, (uint)terminal, pos, hashBits));
            result.Add(ReferenceBucketOfCodes(FeatureKind.LenBucket, (uint)bucket, 0, hashBits));
            result.Add(ReferenceBucketOfCodes(FeatureKind.LenPos, (uint)bucket, pos, hashBits));
        }

        if ((groups & FeatureGroups.ClauseCues) != 0)
        {
            var classes = Enumerable.Range(0, count).Select(i => (int)ReferenceTokenClass(folded[i], lower[i], tags[i])).ToArray();
            var previousClass = index >= 1 ? classes[index - 1] : ClassBoundary;
            var nextClass = index + 1 < count ? classes[index + 1] : ClassBoundary;
            int Distance(int window, params TokenClass[] wanted)
            {
                for (var d = 1; d <= window && index - d >= 0; d++)
                {
                    if (wanted.Contains((TokenClass)classes[index - d]))
                    {
                        return d;
                    }
                }

                return 0;
            }

            result.Add(ReferenceBucketOfCodes(FeatureKind.PrevClass1, (uint)previousClass, 0, hashBits));
            result.Add(ReferenceBucketOfCodes(FeatureKind.NextClass1, (uint)nextClass, 0, hashBits));
            result.Add(ReferenceBucketOfCodes(FeatureKind.PrevNextClass, (uint)previousClass, (uint)nextClass, hashBits));
            result.Add(ReferenceBucketOfCodes(FeatureKind.DetDist, (uint)Distance(3, TokenClass.Det, TokenClass.Num), 0, hashBits));
            result.Add(ReferenceBucketOfCodes(FeatureKind.ModalDist, (uint)Distance(2, TokenClass.Modal, TokenClass.To), 0, hashBits));
        }

        if ((groups & FeatureGroups.Inflection) != 0)
        {
            var stem = ReferenceStemValue(lower[index], lexicon);
            if (stem != 0)
            {
                result.Add(ReferenceBucketOfCodes(FeatureKind.StemKind, (uint)stem, 0, hashBits));
            }
        }
    }

    /// <summary>
    ///     Appends the group buckets of one token that depend on the decoding state, computed from readable forms.
    /// </summary>
    /// <param name="result">Receives the buckets.</param>
    /// <param name="folded">The folded tokens.</param>
    /// <param name="lower">The lower-cased folded tokens.</param>
    /// <param name="index">The token.</param>
    /// <param name="previous1">The previous tag spelling.</param>
    /// <param name="hashBits">The bucket space exponent.</param>
    /// <param name="groups">The groups.</param>
    /// <param name="lexicon">The lexicon.</param>
    /// <param name="history">The tag spellings of the tokens before the token.</param>
    /// <param name="preview">The first-pass tag spellings, or <c>null</c> in the first pass.</param>
    private static void ReferenceGroupHistory(
        List<int> result,
        string[] folded,
        string[] lower,
        int index,
        string previous1,
        int hashBits,
        FeatureGroups groups,
        Lexicon? lexicon,
        IReadOnlyList<string>? history,
        IReadOnlyList<string>? preview)
    {
        var previousCode = TagCode(previous1);
        if ((groups & FeatureGroups.ClauseCues) != 0)
        {
            if (history == null || history.Count < index)
            {
                throw new ArgumentException("The clause cues need the tags chosen so far.", nameof(history));
            }

            var subject = false;
            var verb = false;
            for (var j = 0; j < index; j++)
            {
                var tag = UposExtensions.TryParse(history[j], out var parsed) ? parsed : throw new ArgumentException($"'{history[j]}' is not a tag.", nameof(history));
                if (ClauseBoundaryTokens.Contains(folded[j]) || tag == Upos.Sconj || tag == Upos.Cconj)
                {
                    subject = false;
                    verb = false;
                }
                else
                {
                    subject |= tag == Upos.Det || tag == Upos.Pron || tag == Upos.Noun || tag == Upos.Propn || tag == Upos.Num;
                    verb |= tag == Upos.Verb || tag == Upos.Aux;
                }
            }

            var state = (subject ? 1u : 0u) | (verb ? 2u : 0u);
            result.Add(ReferenceBucketOfCodes(FeatureKind.ClauseState, state, 0, hashBits));
            result.Add(ReferenceBucketOfCodes(FeatureKind.ClauseStatePrev, state, previousCode, hashBits));
        }

        if ((groups & FeatureGroups.Inflection) != 0)
        {
            var stem = ReferenceStemValue(lower[index], lexicon);
            if (stem != 0)
            {
                result.Add(ReferenceBucketOfCodes(FeatureKind.StemKindPrev, (uint)stem, previousCode, hashBits));
            }
        }

        if ((groups & FeatureGroups.RightPreview) != 0 && preview != null)
        {
            var count = folded.Length;
            var tag1 = index + 1 < count ? TagCode(preview[index + 1]) : StartTag1;
            uint tag2 = StartTag1;
            if (index + 2 < count)
            {
                tag2 = TagCode(preview[index + 2]);
            }
            else if (index + 2 > count)
            {
                tag2 = StartTag2;
            }
            result.Add(ReferenceBucketOfCodes(FeatureKind.Rt1, tag1, 0, hashBits));
            result.Add(ReferenceBucketOfCodes(FeatureKind.Rt2, tag2, 0, hashBits));
            result.Add(ReferenceBucketOfCodes(FeatureKind.RtPair, tag1, tag2, hashBits));
            result.Add(ReferenceBucketOfCodes(FeatureKind.RtPrev, previousCode, tag1, hashBits));
        }
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
