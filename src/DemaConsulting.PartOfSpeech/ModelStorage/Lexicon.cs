using System.Runtime.CompilerServices;
using System.Text;

namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     The compiled word lexicon (format <c>POSL</c> version 3): a read-only map from a lower-case spelling to the set
///     of Universal POS tags that the spelling may carry, and, for most spellings, the analyses (base word, tag and
///     features) of the forms that the spelling can be.
/// </summary>
/// <remarks>
///     <para>
///         The lexicon exists so that the decoder can restrict a known word to the tags the data supports. The file
///         stores the spellings sorted by their UTF-8 bytes in blocks of <c>2^blockShift</c>; the first spelling of a
///         block is stored whole, the others are front-coded against their predecessor, and every spelling points into
///         a palette of distinct tag masks. A lookup binary-searches the block heads in place, then scans one block,
///         so it is exact (no hash false positives) and allocates nothing.
///     </para>
///     <para>
///         Behind the data region the payload holds the analyses section: a table of feature sets, a palette of
///         templates (a tag, a feature set and the edit that turns the spelling into its base word: drop <c>cut</c>
///         bytes from the end, append a suffix), a palette of lists of templates, and one two-byte list index per
///         spelling in the order of the spellings. The tag lookups never read this section.
///     </para>
///     <para>
///         The class is immutable after <see cref="Load(byte[])"/> and safe for concurrent lookups; each caller
///         supplies its own scratch buffer. <see cref="Load(byte[])"/> validates the whole file (header, checksum,
///         order, bounds, and every analysis against its spelling), so a successful load guarantees that lookups
///         cannot fail on malformed data.
///     </para>
/// </remarks>
internal sealed class Lexicon
{
    /// <summary>
    ///     The magic of a lexicon file.
    /// </summary>
    public const string Magic = "POSL";

    /// <summary>
    ///     The length of the fixed header in bytes.
    /// </summary>
    public const int HeaderLength = 56;

    /// <summary>
    ///     The format version that this build reads and writes.
    /// </summary>
    public const int FormatVersion = 3;

    /// <summary>
    ///     The largest uncompressed payload length that is accepted (16 MiB; the compiled lexicon needs about 2.2 MB).
    /// </summary>
    public const int MaxPayloadLength = 16 * 1024 * 1024;

    /// <summary>
    ///     The largest number of analyses of one spelling.
    /// </summary>
    public const int MaxAnalysesPerSpelling = 255;

    /// <summary>
    ///     The largest length of a base word in UTF-8 bytes.
    /// </summary>
    public const int MaxLemmaBytes = 255;

    /// <summary>
    ///     The largest number of distinct lists of analysis templates (the list index of a spelling has 16 bits).
    /// </summary>
    public const int MaxLists = ushort.MaxValue;

    /// <summary>
    ///     The description of the file kind used in error messages.
    /// </summary>
    private const string Kind = "lexicon file";

    /// <summary>
    ///     The value of <c>MethodImplOptions.AggressiveOptimization</c>, which <c>netstandard2.0</c> does not name: the method is compiled fully optimized at its first call.
    /// </summary>
    private const MethodImplOptions Optimized = (MethodImplOptions)512;

    /// <summary>
    ///     The tags that a token without a letter or digit may take, in addition to its lexicon tags.
    /// </summary>
    private static readonly UposSet NoLetterOrDigitTags = UposSet.Of(Upos.Punct, Upos.Sym, Upos.X);

    /// <summary>
    ///     The tags that a token with a digit may take, in addition to its lexicon tags.
    /// </summary>
    private static readonly UposSet DigitTags = UposSet.Of(Upos.Num, Upos.Sym, Upos.X, Upos.Noun, Upos.Propn, Upos.Adj);

    /// <summary>
    ///     The tags that a token with an upper-case first letter may take, in addition to its lexicon tags.
    /// </summary>
    private static readonly UposSet FirstUpperTags = UposSet.Of(Upos.Propn, Upos.X);

    /// <summary>
    ///     The decompressed payload: mask palette, block offsets, the data region and the analyses section.
    /// </summary>
    private readonly byte[] _payload;

    /// <summary>
    ///     The distinct tag masks, indexed by the palette index stored with every spelling.
    /// </summary>
    private readonly int[] _masks;

    /// <summary>
    ///     The position of the first byte of every block inside <see cref="_payload"/>.
    /// </summary>
    private readonly int[] _blockStarts;

    /// <summary>
    ///     The log2 of the number of spellings per block.
    /// </summary>
    private readonly int _blockShift;

    /// <summary>
    ///     The exclusive end of the data region inside <see cref="_payload"/>; set once while loading, when the last block was walked.
    /// </summary>
    private int _dataEnd;

    /// <summary>
    ///     The validated analyses section; set once while loading.
    /// </summary>
    private AnalysisTables _analyses = AnalysisTables.Empty;

    /// <summary>
    ///     Initializes a new instance of the <see cref="Lexicon"/> class from validated parts.
    /// </summary>
    /// <param name="payload">The decompressed payload.</param>
    /// <param name="masks">The mask palette.</param>
    /// <param name="blockStarts">The block start positions.</param>
    /// <param name="blockShift">The log2 of the block size.</param>
    /// <param name="spellingCount">The number of spellings.</param>
    /// <param name="entryCount">The number of spelling and tag pairs.</param>
    /// <param name="maxSpellingBytes">The longest spelling in UTF-8 bytes.</param>
    private Lexicon(byte[] payload, int[] masks, int[] blockStarts, int blockShift, int spellingCount, int entryCount, int maxSpellingBytes)
    {
        _payload = payload;
        _masks = masks;
        _blockStarts = blockStarts;
        _blockShift = blockShift;
        _dataEnd = payload.Length;
        SpellingCount = spellingCount;
        EntryCount = entryCount;
        MaxSpellingBytes = maxSpellingBytes;
    }

    /// <summary>
    ///     Gets the number of distinct spellings.
    /// </summary>
    public int SpellingCount { get; }

    /// <summary>
    ///     Gets the total number of spelling and tag pairs (the sum of the tag counts of all spellings).
    /// </summary>
    public int EntryCount { get; }

    /// <summary>
    ///     Gets the length in UTF-8 bytes of the longest spelling.
    /// </summary>
    public int MaxSpellingBytes { get; }

    /// <summary>
    ///     Gets the number of blocks.
    /// </summary>
    public int BlockCount => _blockStarts.Length;

    /// <summary>
    ///     Gets the number of distinct tag masks in the palette.
    /// </summary>
    public int MaskCount => _masks.Length;

    /// <summary>
    ///     Gets the size of the scratch buffer that <see cref="TryGetTags"/> and <see cref="TryFind"/> need.
    /// </summary>
    public int ScratchSize => 2 * MaxSpellingBytes;

    /// <summary>
    ///     Gets the total number of analyses of all spellings.
    /// </summary>
    public int AnalysisCount { get; private set; }

    /// <summary>
    ///     Gets the number of distinct feature sets of the analyses (the empty set is always entry 0).
    /// </summary>
    public int FeatureSetCount => _analyses.FeatureSets.Length;

    /// <summary>
    ///     Gets the number of distinct analysis templates.
    /// </summary>
    public int TemplateCount => _analyses.TemplateTags.Length;

    /// <summary>
    ///     Gets the number of distinct lists of analysis templates (list 0, the empty list, is always present).
    /// </summary>
    public int ListCount => _analyses.ListTags.Length;

    /// <summary>
    ///     Gets the number of spellings that have at least one analysis.
    /// </summary>
    public int AnalyzedSpellingCount { get; private set; }

    /// <summary>
    ///     Computes the tags that the lexicon and the token-shape rules allow for a token.
    /// </summary>
    /// <remarks>
    ///     These are the normative rules of <c>data/lexicon/README.md</c> ("Token-Shape Rules"): an unknown spelling
    ///     allows all 17 tags, otherwise the lexicon tags apply, extended by the tags that the shape of the token
    ///     allows (no letter or digit: PUNCT, SYM, X; a digit: NUM, SYM, X, NOUN, PROPN, ADJ; an upper-case first
    ///     character: PROPN, X).
    /// </remarks>
    /// <param name="known">Whether the lower-cased token is in the lexicon.</param>
    /// <param name="lexiconTags">The lexicon tags of the token; ignored when <paramref name="known"/> is <c>false</c>.</param>
    /// <param name="hasLetterOrDigit">Whether the token contains a letter or a digit.</param>
    /// <param name="hasDigit">Whether the token contains a digit.</param>
    /// <param name="firstUpper">Whether the first character of the token is an upper-case letter.</param>
    /// <returns>The allowed tags; never empty.</returns>
    public static UposSet AllowedTags(bool known, UposSet lexiconTags, bool hasLetterOrDigit, bool hasDigit, bool firstUpper)
    {
        // An unknown word may take any tag: the lexicon has no evidence either way
        if (!known)
        {
            return UposSet.All;
        }

        // Extend the lexicon tags by what the shape of the token permits; the sets are precomputed because this runs for every token
        var allowed = lexiconTags;
        if (!hasLetterOrDigit)
        {
            allowed = allowed.Union(NoLetterOrDigitTags);
        }

        if (hasDigit)
        {
            allowed = allowed.Union(DigitTags);
        }

        if (firstUpper)
        {
            allowed = allowed.Union(FirstUpperTags);
        }

        return allowed;
    }

    /// <summary>
    ///     Computes the tags that the lexicon and the token-shape rules allow for a token given as text.
    /// </summary>
    /// <param name="token">The token as written (it is lower-cased for the lookup).</param>
    /// <param name="lexicon">The lexicon.</param>
    /// <returns>The allowed tags; never empty.</returns>
    public static UposSet AllowedTags(string token, Lexicon lexicon)
    {
        // Compute the shape facts with the same character classes as the data tests (char.IsLetterOrDigit and friends)
        var hasLetterOrDigit = false;
        var hasDigit = false;
        foreach (var c in token)
        {
            hasLetterOrDigit |= char.IsLetterOrDigit(c);
            hasDigit |= char.IsDigit(c);
        }

        var key = token.ToLowerInvariant().ToCharArray();
        var known = lexicon.TryGetTags(key, 0, key.Length, new byte[lexicon.ScratchSize], out var tags);
        return AllowedTags(known, tags, hasLetterOrDigit, hasDigit, token.Length > 0 && char.IsUpper(token[0]));
    }

    /// <summary>
    ///     Loads and validates a lexicon file.
    /// </summary>
    /// <param name="stream">A stream positioned at the start of the file; it is read to its end.</param>
    /// <returns>The lexicon.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidDataException">The file is not a valid lexicon of a supported version.</exception>
    public static Lexicon Load(Stream stream) => Load(BinaryFormat.ReadAllBytes(stream));

    /// <summary>
    ///     Loads and validates a lexicon file held in memory.
    /// </summary>
    /// <remarks>
    ///     The loader accepts exactly <see cref="FormatVersion"/>. A file of another version, such as a custom lexicon of
    ///     version 2, is refused with a message that names the version found and the version read, and is regenerated as
    ///     version 3 with the lexicon compiler (<c>dotnet run --project tools/LexiconCompiler -- --input my-lexicon.tsv
    ///     --analyses my-analyses.tsv --output my-lexicon.posl</c>).
    /// </remarks>
    /// <param name="file">The whole file.</param>
    /// <returns>The lexicon.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="file"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidDataException">The file is not a valid lexicon of a supported version.</exception>
    public static Lexicon Load(byte[] file)
    {
        ArgumentNullException.ThrowIfNull(file);

        // Header: magic, version, flags, then the counts that size every later structure
        var flags = BinaryFormat.ReadHeader(file, Magic, HeaderLength, FormatVersion, Kind);
        var header = ReadHeader(file);

        // Payload: bounds, checksum, then the three regions
        var payload = BinaryFormat.ReadPayload(file, HeaderLength, flags, header.PayloadLength, MaxPayloadLength, header.StoredLength, header.Checksum, Kind);
        var paletteBytes = (long)header.MaskCount * 4;
        var offsetBytes = (long)header.BlockCount * 4;
        if (paletteBytes + offsetBytes > payload.Length)
        {
            throw new InvalidDataException("The lexicon file is truncated: the palette and block index do not fit the payload.");
        }

        var masks = ReadMasks(payload, header.MaskCount);
        var dataStart = (int)(paletteBytes + offsetBytes);
        var blockStarts = ReadBlockStarts(payload, header.MaskCount * 4, header.BlockCount, dataStart);
        var lexicon = new Lexicon(payload, masks, blockStarts, header.BlockShift, header.SpellingCount, header.EntryCount, header.MaxSpellingBytes);

        // The data region ends where its last block ends; the analyses section follows and must fill the rest of the payload exactly
        lexicon._dataEnd = lexicon.FindDataEnd(dataStart);
        lexicon._analyses = ReadAnalysisTables(payload, lexicon._dataEnd, header);
        lexicon.AnalysisCount = header.AnalysisCount;

        // Walk every entry once: this proves the structure so lookups never need to defend against it
        lexicon.Validate(header.AnalysisCount);
        return lexicon;
    }

    /// <summary>
    ///     Looks up a lower-cased spelling.
    /// </summary>
    /// <param name="chars">The buffer that holds the characters of the key, already folded and lower-cased.</param>
    /// <param name="offset">The index of the first key character in <paramref name="chars"/>.</param>
    /// <param name="length">The number of characters of the key.</param>
    /// <param name="scratch">A buffer of at least <see cref="ScratchSize"/> bytes that the caller owns exclusively for the call.</param>
    /// <param name="tags">Receives the tags of the spelling; the empty set when it is not in the lexicon.</param>
    /// <returns><c>true</c> if the spelling is in the lexicon.</returns>
    public bool TryGetTags(char[] chars, int offset, int length, byte[] scratch, out UposSet tags) =>
        TryFindKey(chars, offset, length, scratch, out _, out tags, out _);

    /// <summary>
    ///     Looks up a lower-cased spelling and reports its position in the table.
    /// </summary>
    /// <remarks>
    ///     The same search as <see cref="TryFindKey"/>, for callers
    ///     that do not need the length of the key.
    /// </remarks>
    /// <param name="chars">The buffer that holds the characters of the key, already folded and lower-cased.</param>
    /// <param name="offset">The index of the first key character in <paramref name="chars"/>.</param>
    /// <param name="length">The number of characters of the key.</param>
    /// <param name="scratch">A buffer of at least <see cref="ScratchSize"/> bytes that the caller owns exclusively for the call.</param>
    /// <param name="ordinal">Receives the zero-based position of the spelling in ascending UTF-8 order; -1 when it is not in the lexicon.</param>
    /// <param name="tags">Receives the tags of the spelling; the empty set when it is not in the lexicon.</param>
    /// <returns><c>true</c> if the spelling is in the lexicon.</returns>
    public bool TryFind(char[] chars, int offset, int length, byte[] scratch, out int ordinal, out UposSet tags) =>
        TryFindKey(chars, offset, length, scratch, out ordinal, out tags, out _);

    /// <summary>
    ///     Looks up a lower-cased spelling and reports its position in the table and the length of its UTF-8 key.
    /// </summary>
    /// <remarks>
    ///     This is the core of <see cref="TryGetTags"/>: the same search, the same allocation-free behavior, plus the
    ///     ordinal that addresses the analyses of the spelling and the length of the UTF-8 key, which stays in the first
    ///     half of <paramref name="scratch"/> after a successful call (the base word of an analysis is composed from it,
    ///     see <see cref="HasAnalysis"/>).
    /// </remarks>
    /// <param name="chars">The buffer that holds the characters of the key, already folded and lower-cased.</param>
    /// <param name="offset">The index of the first key character in <paramref name="chars"/>.</param>
    /// <param name="length">The number of characters of the key.</param>
    /// <param name="scratch">A buffer of at least <see cref="ScratchSize"/> bytes that the caller owns exclusively for the call.</param>
    /// <param name="ordinal">Receives the zero-based position of the spelling in ascending UTF-8 order; -1 when it is not in the lexicon.</param>
    /// <param name="tags">Receives the tags of the spelling; the empty set when it is not in the lexicon.</param>
    /// <param name="keyLength">Receives the number of UTF-8 bytes of the key at the start of <paramref name="scratch"/>; meaningful only when the result is <c>true</c>.</param>
    /// <returns><c>true</c> if the spelling is in the lexicon.</returns>
    public bool TryFindKey(char[] chars, int offset, int length, byte[] scratch, out int ordinal, out UposSet tags, out int keyLength)
    {
        tags = UposSet.Empty;
        ordinal = -1;
        keyLength = 0;
        if (length == 0 || length > MaxSpellingBytes)
        {
            return false;
        }

        // Encode the key as UTF-8 into the first half of the scratch buffer; a longer key cannot be present
        keyLength = EncodeUtf8(chars, offset, length, scratch, MaxSpellingBytes);
        if (keyLength < 0)
        {
            keyLength = 0;
            return false;
        }

        // Binary search for the last block whose head spelling is not greater than the key
        var low = 0;
        var high = _blockStarts.Length - 1;
        var block = -1;
        while (low <= high)
        {
            var middle = (low + high) >> 1;
            var position = _blockStarts[middle];
            var headLength = BinaryFormat.ReadVarintInt32(_payload, ref position, _dataEnd);
            var comparison = BinaryFormat.Compare(_payload, position, headLength, scratch, 0, keyLength);
            if (comparison == 0)
            {
                position += headLength;
                var maskIndex = BinaryFormat.ReadVarintInt32(_payload, ref position, _dataEnd);
                tags = UposSet.FromMask(_masks[maskIndex]);
                ordinal = middle << _blockShift;
                return true;
            }

            if (comparison < 0)
            {
                block = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        if (block < 0)
        {
            return false;
        }

        // Scan the block from its second spelling; the spellings ascend, so stop at the first one that is greater
        return ScanBlock(block, scratch, keyLength, out ordinal, out tags);
    }

    /// <summary>
    ///     Decodes every spelling with its tags, in the stored (ascending UTF-8) order.
    /// </summary>
    /// <returns>The spellings and their tag sets.</returns>
    public IEnumerable<KeyValuePair<string, UposSet>> Enumerate()
    {
        var buffer = new byte[MaxSpellingBytes];
        for (var block = 0; block < _blockStarts.Length; block++)
        {
            var position = _blockStarts[block];
            var count = BlockSpellingCount(block);
            var length = 0;
            for (var entry = 0; entry < count; entry++)
            {
                var maskIndex = ReadEntry(ref position, entry == 0, buffer, 0, ref length);
                yield return new KeyValuePair<string, UposSet>(
                    Encoding.UTF8.GetString(buffer, 0, length),
                    UposSet.FromMask(_masks[maskIndex]));
            }
        }
    }

    /// <summary>
    ///     Gets the tags that at least one analysis of a spelling names.
    /// </summary>
    /// <remarks>
    ///     The result is a subset of the tags of the spelling (checked when the file is loaded). The tags of the spelling
    ///     that are not in it are the tags that the lexicon lists without an analysis. The call reads two bytes and
    ///     allocates nothing.
    /// </remarks>
    /// <param name="ordinal">The position of the spelling, as reported by <see cref="TryFind"/>.</param>
    /// <returns>The tags of the analyses; the empty set when the spelling has none.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ordinal"/> is not the position of a spelling.</exception>
    public UposSet AnalyzedTags(int ordinal) => UposSet.FromMask(_analyses.ListTags[ListOf(ordinal)]);

    /// <summary>
    ///     Gets the analyses of a spelling in their stored order (tag, then base word, then features).
    /// </summary>
    /// <remarks>
    ///     This path allocates: it decodes the spelling of the block, composes one base word string per analysis and
    ///     returns an array. The feature strings are shared with the table.
    /// </remarks>
    /// <param name="ordinal">The position of the spelling, as reported by <see cref="TryFind"/>.</param>
    /// <returns>The analyses; an empty list when the spelling has none.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ordinal"/> is not the position of a spelling.</exception>
    public IReadOnlyList<Analysis> GetAnalyses(int ordinal)
    {
        var list = ListOf(ordinal);
        var first = _analyses.ListStarts[list];
        var count = _analyses.ListStarts[list + 1] - first;
        if (count == 0)
        {
            return Array.Empty<Analysis>();
        }

        // Decode the spelling, then apply every template of the list to it
        var spelling = DecodeSpelling(ordinal, out var spellingLength);
        var result = new Analysis[count];
        for (var i = 0; i < count; i++)
        {
            var template = _analyses.ListTemplates[first + i];
            var keep = spellingLength - _analyses.TemplateCuts[template];
            result[i] = new Analysis(
                Encoding.UTF8.GetString(spelling, 0, keep) + _analyses.TemplateSuffixTexts[template],
                (Upos)_analyses.TemplateTags[template],
                _analyses.FeatureSets[_analyses.TemplateFeatureSets[template]]);
        }

        return result;
    }

    /// <summary>
    ///     Tells whether a spelling has an analysis with a tag and a base word, without allocating.
    /// </summary>
    /// <param name="ordinal">The position of the spelling, as reported by <see cref="TryFind"/>.</param>
    /// <param name="tag">The tag of the analysis.</param>
    /// <param name="spelling">The buffer that holds the UTF-8 bytes of the spelling.</param>
    /// <param name="spellingOffset">The index of the first spelling byte in <paramref name="spelling"/>.</param>
    /// <param name="spellingLength">The number of spelling bytes.</param>
    /// <param name="lemma">The buffer that holds the UTF-8 bytes of the base word.</param>
    /// <param name="lemmaOffset">The index of the first base word byte in <paramref name="lemma"/>.</param>
    /// <param name="lemmaLength">The number of base word bytes.</param>
    /// <returns><c>true</c> if one analysis of the spelling has this tag and composes exactly this base word.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ordinal"/> is not the position of a spelling.</exception>
    public bool HasAnalysis(int ordinal, Upos tag, byte[] spelling, int spellingOffset, int spellingLength, byte[] lemma, int lemmaOffset, int lemmaLength)
    {
        var list = ListOf(ordinal);
        var end = _analyses.ListStarts[list + 1];
        for (var i = _analyses.ListStarts[list]; i < end; i++)
        {
            // Cheap tests first: the tag, then the length of the composed base word
            var template = _analyses.ListTemplates[i];
            if (_analyses.TemplateTags[template] != (byte)tag)
            {
                continue;
            }

            var suffix = _analyses.TemplateSuffixes[template];
            var keep = spellingLength - _analyses.TemplateCuts[template];
            if (keep + suffix.Length != lemmaLength
                || BinaryFormat.Compare(spelling, spellingOffset, keep, lemma, lemmaOffset, keep) != 0
                || BinaryFormat.Compare(suffix, 0, suffix.Length, lemma, lemmaOffset + keep, suffix.Length) != 0)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    ///     Decodes every spelling that has analyses together with them, in the stored (ascending UTF-8) order.
    /// </summary>
    /// <returns>The spellings and their analyses; spellings without an analysis are skipped.</returns>
    public IEnumerable<KeyValuePair<string, IReadOnlyList<Analysis>>> EnumerateAnalyses()
    {
        var ordinal = 0;
        foreach (var entry in Enumerate())
        {
            var analyses = GetAnalyses(ordinal++);
            if (analyses.Count > 0)
            {
                yield return new KeyValuePair<string, IReadOnlyList<Analysis>>(entry.Key, analyses);
            }
        }
    }

    /// <summary>
    ///     Reads the uncompressed form of a lexicon file: the header with the compression flag cleared followed by the payload.
    /// </summary>
    /// <remarks>
    ///     The Deflate bytes differ between runtimes, but the uncompressed form is deterministic, so the compiler
    ///     tool compares this form to decide whether a committed binary is current. The checksum of the uncompressed form
    ///     covers the payload, which is what the stored bytes of that form are.
    /// </remarks>
    /// <param name="file">The whole file.</param>
    /// <returns>The uncompressed file image.</returns>
    /// <exception cref="InvalidDataException">The file is not a valid lexicon of a supported version.</exception>
    public static byte[] ToUncompressedImage(byte[] file)
    {
        // Validate completely first so a damaged file never produces a plausible-looking image
        _ = Load(file);
        var flags = BinaryFormat.ReadHeader(file, Magic, HeaderLength, FormatVersion, Kind);
        var header = ReadHeader(file);
        var payload = BinaryFormat.ReadPayload(file, HeaderLength, flags, header.PayloadLength, MaxPayloadLength, header.StoredLength, header.Checksum, Kind);
        var image = new byte[HeaderLength + payload.Length];
        Buffer.BlockCopy(file, 0, image, 0, HeaderLength);
        BinaryFormat.WriteUInt16(image, 6, (ushort)(flags & ~BinaryFormat.FlagDeflate));
        BinaryFormat.WriteUInt32(image, 28, (uint)payload.Length);
        BinaryFormat.WriteUInt32(image, 32, BinaryFormat.Fnv1a(payload, 0, payload.Length));
        Buffer.BlockCopy(payload, 0, image, HeaderLength, payload.Length);
        return image;
    }

    /// <summary>
    ///     Encodes characters as UTF-8 into a buffer, replacing unpaired surrogates by U+FFFD like <see cref="Encoding.UTF8"/>.
    /// </summary>
    /// <param name="chars">The characters.</param>
    /// <param name="offset">The index of the first character to encode.</param>
    /// <param name="length">The number of characters to encode.</param>
    /// <param name="buffer">The destination, starting at index 0.</param>
    /// <param name="limit">The maximum number of bytes to produce.</param>
    /// <returns>The number of bytes written, or -1 if the encoding would exceed <paramref name="limit"/>.</returns>
    internal static int EncodeUtf8(char[] chars, int offset, int length, byte[] buffer, int limit)
    {
        var count = 0;
        var end = offset + length;
        var index = offset;
        while (index < end)
        {
            var c = (int)chars[index++];

            // Combine a valid surrogate pair into one code point; any other surrogate becomes U+FFFD
            if (c >= 0xD800 && c <= 0xDFFF)
            {
                if (c <= 0xDBFF && index < end && chars[index] >= 0xDC00 && chars[index] <= 0xDFFF)
                {
                    c = 0x10000 + ((c - 0xD800) << 10) + (chars[index] - 0xDC00);
                    index++;
                }
                else
                {
                    c = 0xFFFD;
                }
            }

            // Emit 1 to 4 bytes
            if (c < 0x80)
            {
                if (count + 1 > limit)
                {
                    return -1;
                }

                buffer[count++] = (byte)c;
            }
            else if (c < 0x800)
            {
                if (count + 2 > limit)
                {
                    return -1;
                }

                buffer[count++] = (byte)(0xC0 | (c >> 6));
                buffer[count++] = (byte)(0x80 | (c & 0x3F));
            }
            else if (c < 0x10000)
            {
                if (count + 3 > limit)
                {
                    return -1;
                }

                buffer[count++] = (byte)(0xE0 | (c >> 12));
                buffer[count++] = (byte)(0x80 | ((c >> 6) & 0x3F));
                buffer[count++] = (byte)(0x80 | (c & 0x3F));
            }
            else
            {
                if (count + 4 > limit)
                {
                    return -1;
                }

                buffer[count++] = (byte)(0xF0 | (c >> 18));
                buffer[count++] = (byte)(0x80 | ((c >> 12) & 0x3F));
                buffer[count++] = (byte)(0x80 | ((c >> 6) & 0x3F));
                buffer[count++] = (byte)(0x80 | (c & 0x3F));
            }
        }

        return count;
    }

    /// <summary>
    ///     Reads and validates the fixed header fields after magic, version and flags.
    /// </summary>
    /// <param name="file">The whole file.</param>
    /// <returns>The header fields.</returns>
    /// <exception cref="InvalidDataException">A field has an invalid value.</exception>
    private static HeaderFields ReadHeader(byte[] file)
    {
        var tagCount = file[8];
        if (tagCount != UposExtensions.Count)
        {
            throw new InvalidDataException($"Invalid lexicon file: it has {tagCount} tags but this build uses {UposExtensions.Count}.");
        }

        var blockShift = file[9];
        if (blockShift < 1 || blockShift > 8)
        {
            throw new InvalidDataException($"Invalid lexicon file: the block shift {blockShift} is outside 1 to 8.");
        }

        var maskCount = BinaryFormat.ReadUInt16(file, 10);
        var spellingCount = BinaryFormat.ReadInt32Field(file, 12, "spelling count");
        var entryCount = BinaryFormat.ReadInt32Field(file, 16, "entry count");
        var blockCount = BinaryFormat.ReadInt32Field(file, 20, "block count");
        var payloadLength = BinaryFormat.ReadInt32Field(file, 24, "payload length");
        var storedLength = BinaryFormat.ReadInt32Field(file, 28, "stored length");
        var checksum = BinaryFormat.ReadUInt32(file, 32);
        var maxSpellingBytes = BinaryFormat.ReadUInt16(file, 36);
        if (BinaryFormat.ReadUInt16(file, 38) != 0 || BinaryFormat.ReadUInt16(file, 42) != 0)
        {
            throw new InvalidDataException("Invalid lexicon file: a reserved field is not zero.");
        }

        // The analyses section: one feature set (the empty one) and one list (the empty one) are always present
        var featureSetCount = BinaryFormat.ReadUInt16(file, 40);
        var templateCount = BinaryFormat.ReadInt32Field(file, 44, "template count");
        var listCount = BinaryFormat.ReadInt32Field(file, 48, "list count");
        var analysisCount = BinaryFormat.ReadInt32Field(file, 52, "analysis count");
        if (featureSetCount == 0 || listCount == 0 || listCount > MaxLists)
        {
            throw new InvalidDataException(
                $"Invalid lexicon file: {featureSetCount} feature sets and {listCount} lists are outside the range 1 to {MaxLists} (the empty feature set and the empty list are required).");
        }

        // The block count is fully determined by the spelling count and the block size
        var blockSize = 1 << blockShift;
        var expectedBlocks = (int)(((long)spellingCount + blockSize - 1) >> blockShift);
        if (blockCount != expectedBlocks)
        {
            throw new InvalidDataException($"Invalid lexicon file: {blockCount} blocks do not fit {spellingCount} spellings.");
        }

        if (maxSpellingBytes == 0 && spellingCount > 0)
        {
            throw new InvalidDataException("Invalid lexicon file: the maximum spelling length is zero.");
        }

        return new HeaderFields(
            blockShift, maskCount, spellingCount, entryCount, blockCount, payloadLength, storedLength, checksum, maxSpellingBytes,
            featureSetCount, templateCount, listCount, analysisCount);
    }

    /// <summary>
    ///     Finds the end of the data region by walking its last block.
    /// </summary>
    /// <param name="dataStart">The position of the data region (after the palette and the block index).</param>
    /// <returns>The exclusive end of the data region inside the payload.</returns>
    /// <exception cref="InvalidDataException">The last block is malformed.</exception>
    private int FindDataEnd(int dataStart)
    {
        if (_blockStarts.Length == 0)
        {
            return dataStart;
        }

        var last = _blockStarts.Length - 1;
        var position = _blockStarts[last];
        var count = BlockSpellingCount(last);
        var buffer = new byte[MaxSpellingBytes];
        var length = 0;
        for (var entry = 0; entry < count; entry++)
        {
            ReadEntry(ref position, entry == 0, buffer, 0, ref length);
        }

        return position;
    }

    /// <summary>
    ///     Reads and validates the analyses section that follows the data region.
    /// </summary>
    /// <param name="payload">The payload.</param>
    /// <param name="start">The position of the section (the end of the data region).</param>
    /// <param name="header">The header fields.</param>
    /// <returns>The tables of the section.</returns>
    /// <exception cref="InvalidDataException">A table is malformed or the regions do not fill the payload exactly.</exception>
    private static AnalysisTables ReadAnalysisTables(byte[] payload, int start, HeaderFields header)
    {
        // The list index (two bytes per spelling) closes the payload; the three palettes sit between the data and the index
        var indexBytes = (long)header.SpellingCount * 2;
        if (start + indexBytes > payload.Length)
        {
            throw new InvalidDataException("The lexicon file is truncated: the analyses section does not fit the payload.");
        }

        var limit = payload.Length - (int)indexBytes;
        var position = start;

        // Feature sets: strictly ascending, entry 0 is the empty set; every entry takes at least one byte
        if (header.FeatureSetCount > limit - position)
        {
            throw new InvalidDataException("The lexicon file is truncated: the feature set table does not fit the payload.");
        }

        var featureSets = new string[header.FeatureSetCount];
        for (var i = 0; i < featureSets.Length; i++)
        {
            var length = BinaryFormat.ReadVarintInt32(payload, ref position, limit);
            if (length > limit - position)
            {
                throw new InvalidDataException($"Invalid lexicon file: feature set {i} runs past the table.");
            }

            for (var k = 0; k < length; k++)
            {
                if (payload[position + k] >= 0x80)
                {
                    throw new InvalidDataException($"Invalid lexicon file: feature set {i} is not ASCII.");
                }
            }

            var text = Encoding.ASCII.GetString(payload, position, length);
            position += length;
            var ordered = i == 0 ? text.Length == 0 : text.Length > 0 && string.CompareOrdinal(featureSets[i - 1], text) < 0;
            if (!ordered || !IsValidFeatureSet(text))
            {
                throw new InvalidDataException($"Invalid lexicon file: feature set {i} is malformed, repeated or out of order.");
            }

            featureSets[i] = text;
        }

        // Templates: a template takes at least four bytes (tag, feature set, cut, suffix length), so the count is bounded by the payload
        if ((long)header.TemplateCount * 4 > limit - position)
        {
            throw new InvalidDataException("The lexicon file is truncated: the template palette does not fit the payload.");
        }

        var templateTags = new byte[header.TemplateCount];
        var templateFeatureSets = new int[header.TemplateCount];
        var templateCuts = new int[header.TemplateCount];
        var templateSuffixes = new byte[header.TemplateCount][];
        var templateSuffixTexts = new string[header.TemplateCount];
        var templateClean = new bool[header.TemplateCount];
        var seenTemplates = new HashSet<string>(StringComparer.Ordinal);
        for (var t = 0; t < header.TemplateCount; t++)
        {
            if (position >= limit)
            {
                throw new InvalidDataException($"The lexicon file is truncated: template {t} is missing.");
            }

            var tag = payload[position++];
            var featureSet = BinaryFormat.ReadVarintInt32(payload, ref position, limit);
            var cut = BinaryFormat.ReadVarintInt32(payload, ref position, limit);
            var suffixLength = BinaryFormat.ReadVarintInt32(payload, ref position, limit);
            if (tag >= UposExtensions.Count || featureSet >= featureSets.Length || cut > header.MaxSpellingBytes
                || suffixLength > MaxLemmaBytes || suffixLength > limit - position)
            {
                throw new InvalidDataException($"Invalid lexicon file: template {t} has a tag, feature set, cut or suffix out of range.");
            }

            var suffix = new byte[suffixLength];
            Buffer.BlockCopy(payload, position, suffix, 0, suffixLength);
            position += suffixLength;
            var text = TryDecodeUtf8(suffix);
            if (text == null || (suffixLength > 0 && (suffix[0] & 0xC0) == 0x80))
            {
                throw new InvalidDataException($"Invalid lexicon file: the suffix of template {t} is not valid UTF-8 or starts inside a character.");
            }

            if (!seenTemplates.Add($"{tag}/{featureSet}/{cut}/{Convert.ToBase64String(suffix)}"))
            {
                throw new InvalidDataException($"Invalid lexicon file: template {t} repeats an earlier template.");
            }

            templateTags[t] = tag;
            templateFeatureSets[t] = featureSet;
            templateCuts[t] = cut;
            templateSuffixes[t] = suffix;
            templateSuffixTexts[t] = text;
            templateClean[t] = LeadingClean(suffix, suffix.Length) == suffix.Length;
        }

        // Lists: list 0 is empty, every other list is not; tags never decrease (the order within a tag needs the spelling and is checked per spelling), a template appears once, lists are distinct
        if (header.ListCount > limit - position)
        {
            throw new InvalidDataException("The lexicon file is truncated: the list palette does not fit the payload.");
        }

        var listStarts = new int[header.ListCount + 1];
        var listTags = new int[header.ListCount];
        var items = new List<int>();
        var seenLists = new HashSet<string>(StringComparer.Ordinal);
        var used = new bool[header.TemplateCount];
        var listTied = new bool[header.ListCount];
        for (var l = 0; l < header.ListCount; l++)
        {
            listStarts[l] = items.Count;
            var count = BinaryFormat.ReadVarintInt32(payload, ref position, limit);
            if (count > MaxAnalysesPerSpelling || (count == 0) != (l == 0))
            {
                throw new InvalidDataException($"Invalid lexicon file: list {l} has {count} templates (list 0 must be empty, every other list not, at most {MaxAnalysesPerSpelling}).");
            }

            var key = new StringBuilder();
            var lastTag = -1;
            var mask = 0;
            for (var k = 0; k < count; k++)
            {
                var template = BinaryFormat.ReadVarintInt32(payload, ref position, limit);
                if (template >= header.TemplateCount || used[template] || templateTags[template] < lastTag)
                {
                    throw new InvalidDataException($"Invalid lexicon file: list {l} names a missing or repeated template or its tags decrease.");
                }

                // Two neighbors with one tag are ordered by their base words and features, which only a spelling can tell
                listTied[l] |= templateTags[template] == lastTag;
                used[template] = true;
                lastTag = templateTags[template];
                mask |= 1 << lastTag;
                items.Add(template);
                key.Append(template).Append(',');
            }

            for (var k = items.Count - count; k < items.Count; k++)
            {
                used[items[k]] = false;
            }

            if (!seenLists.Add(key.ToString()))
            {
                throw new InvalidDataException($"Invalid lexicon file: list {l} repeats an earlier list.");
            }

            listTags[l] = mask;
        }

        listStarts[header.ListCount] = items.Count;

        // The three palettes must end exactly where the list index begins
        if (position != limit)
        {
            throw new InvalidDataException("Invalid lexicon file: the analyses regions do not fill the payload exactly.");
        }

        return new AnalysisTables(
            featureSets, templateTags, templateFeatureSets, templateCuts, templateSuffixes, templateSuffixTexts, templateClean, listStarts,
            items.ToArray(), listTags, listTied, limit);
    }

    /// <summary>
    ///     Decodes bytes as strict UTF-8.
    /// </summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The text, or <c>null</c> if the bytes are not valid UTF-8.</returns>
    private static string? TryDecodeUtf8(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    /// <summary>
    ///     Tells whether a text is a well-formed feature set: <c>Name=Value</c> pairs joined by <c>|</c>, names ascending.
    /// </summary>
    /// <remarks>
    ///     The empty text is the empty feature set. A name starts with an ASCII letter and continues with letters and
    ///     digits, a value consists of ASCII letters and digits; nothing may be empty and no name may repeat.
    /// </remarks>
    /// <param name="text">The text.</param>
    /// <returns><c>true</c> if the text is well formed.</returns>
    internal static bool IsValidFeatureSet(string text)
    {
        if (text.Length == 0)
        {
            return true;
        }

        string? previousName = null;
        foreach (var pair in text.Split('|'))
        {
            var equals = pair.IndexOf('=');
            if (equals <= 0 || equals == pair.Length - 1)
            {
                return false;
            }

            var name = pair.Substring(0, equals);
            if (!((name[0] >= 'A' && name[0] <= 'Z') || (name[0] >= 'a' && name[0] <= 'z')) || !IsAsciiAlphanumeric(name)
                || !IsAsciiAlphanumeric(pair.Substring(equals + 1))
                || (previousName != null && string.CompareOrdinal(previousName, name) >= 0))
            {
                return false;
            }

            previousName = name;
        }

        return true;
    }

    /// <summary>
    ///     Tells whether a text is not empty and has only ASCII letters and digits.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns><c>true</c> if every character is an ASCII letter or digit.</returns>
    private static bool IsAsciiAlphanumeric(string text)
    {
        foreach (var c in text)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')))
            {
                return false;
            }
        }

        return text.Length > 0;
    }

    /// <summary>
    ///     Gets the list index of a spelling.
    /// </summary>
    /// <param name="ordinal">The position of the spelling.</param>
    /// <returns>The list index, below <see cref="ListCount"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ordinal"/> is not the position of a spelling.</exception>
    private int ListOf(int ordinal)
    {
        if ((uint)ordinal >= (uint)SpellingCount)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal), ordinal, "The ordinal is not the position of a spelling.");
        }

        return BinaryFormat.ReadUInt16(_payload, _analyses.IndexStart + (ordinal * 2));
    }

    /// <summary>
    ///     Decodes one spelling of the table into a new buffer.
    /// </summary>
    /// <param name="ordinal">The position of the spelling.</param>
    /// <param name="length">Receives the length of the spelling in bytes.</param>
    /// <returns>A buffer that holds the spelling at its start.</returns>
    private byte[] DecodeSpelling(int ordinal, out int length)
    {
        var buffer = new byte[MaxSpellingBytes];
        var block = ordinal >> _blockShift;
        var target = ordinal & ((1 << _blockShift) - 1);
        var position = _blockStarts[block];
        length = 0;
        for (var entry = 0; entry <= target; entry++)
        {
            ReadEntry(ref position, entry == 0, buffer, 0, ref length);
        }

        return buffer;
    }

    /// <summary>
    ///     Reads the mask palette and validates it.
    /// </summary>
    /// <param name="payload">The payload.</param>
    /// <param name="maskCount">The number of masks.</param>
    /// <returns>The masks.</returns>
    /// <exception cref="InvalidDataException">A mask is empty, uses a bit above the 17th or repeats.</exception>
    private static int[] ReadMasks(byte[] payload, int maskCount)
    {
        var masks = new int[maskCount];
        var seen = new HashSet<int>();
        for (var i = 0; i < maskCount; i++)
        {
            var mask = BinaryFormat.ReadUInt32(payload, i * 4);
            if (mask == 0 || mask >= (1u << UposExtensions.Count) || !seen.Add((int)mask))
            {
                throw new InvalidDataException($"Invalid lexicon file: palette entry {i} is empty, out of range or repeated.");
            }

            masks[i] = (int)mask;
        }

        return masks;
    }

    /// <summary>
    ///     Reads the block index and validates that it ascends from the start of the data region.
    /// </summary>
    /// <param name="payload">The payload.</param>
    /// <param name="indexStart">The position of the index in the payload.</param>
    /// <param name="blockCount">The number of blocks.</param>
    /// <param name="dataStart">The position of the data region.</param>
    /// <returns>The absolute position of every block inside the payload.</returns>
    /// <exception cref="InvalidDataException">The index does not start at 0, does not ascend or points outside the data.</exception>
    private static int[] ReadBlockStarts(byte[] payload, int indexStart, int blockCount, int dataStart)
    {
        var starts = new int[blockCount];
        var dataLength = payload.Length - dataStart;
        long previous = -1;
        for (var i = 0; i < blockCount; i++)
        {
            long offset = BinaryFormat.ReadUInt32(payload, indexStart + (i * 4));
            if ((i == 0 && offset != 0) || offset <= previous || offset >= dataLength)
            {
                throw new InvalidDataException($"Invalid lexicon file: the offset of block {i} is invalid.");
            }

            starts[i] = dataStart + (int)offset;
            previous = offset;
        }

        return starts;
    }

    /// <summary>
    ///     Gets the number of spellings of a block (the last block may be shorter).
    /// </summary>
    /// <param name="block">The block index.</param>
    /// <returns>The number of spellings.</returns>
    private int BlockSpellingCount(int block) => Math.Min(1 << _blockShift, SpellingCount - (block << _blockShift));

    /// <summary>
    ///     Decodes one entry into the running spelling buffer.
    /// </summary>
    /// <param name="position">The position of the entry; advanced past it.</param>
    /// <param name="first">Whether this is the first entry of a block (stored whole).</param>
    /// <param name="buffer">The spelling buffer; it holds the previous spelling on entry and the decoded one on return.</param>
    /// <param name="bufferOffset">The offset of the spelling inside <paramref name="buffer"/>; at least <see cref="MaxSpellingBytes"/> bytes must follow.</param>
    /// <param name="length">The length of the spelling in <paramref name="buffer"/>.</param>
    /// <returns>The palette index of the spelling.</returns>
    /// <exception cref="InvalidDataException">The entry is malformed.</exception>
    private int ReadEntry(ref int position, bool first, byte[] buffer, int bufferOffset, ref int length)
    {
        // A head repeats nothing; every other entry keeps a prefix of the previous spelling
        int shared;
        if (first)
        {
            shared = 0;
            length = 0;
        }
        else
        {
            shared = BinaryFormat.ReadVarintInt32(_payload, ref position, _dataEnd);
            if (shared > length)
            {
                throw new InvalidDataException("Invalid lexicon file: a shared prefix is longer than the previous spelling.");
            }
        }

        var suffix = BinaryFormat.ReadVarintInt32(_payload, ref position, _dataEnd);
        if (suffix > MaxSpellingBytes - shared || suffix > _dataEnd - position)
        {
            throw new InvalidDataException("Invalid lexicon file: a spelling is longer than declared or runs past the data.");
        }

        Buffer.BlockCopy(_payload, position, buffer, bufferOffset + shared, suffix);
        position += suffix;
        length = shared + suffix;
        var maskIndex = BinaryFormat.ReadVarintInt32(_payload, ref position, _dataEnd);
        if (maskIndex >= _masks.Length)
        {
            throw new InvalidDataException("Invalid lexicon file: a palette index is out of range.");
        }

        return maskIndex;
    }

    /// <summary>
    ///     Scans the spellings of one block after its head for the key.
    /// </summary>
    /// <param name="block">The block index.</param>
    /// <param name="scratch">The scratch buffer; the key is in the first half, the second half is used for decoding.</param>
    /// <param name="keyLength">The length of the key in bytes.</param>
    /// <param name="ordinal">Receives the position of the spelling when the key is found; -1 otherwise.</param>
    /// <param name="tags">Receives the tags when the key is found.</param>
    /// <returns><c>true</c> if the key is in the block.</returns>
    private bool ScanBlock(int block, byte[] scratch, int keyLength, out int ordinal, out UposSet tags)
    {
        tags = UposSet.Empty;
        ordinal = -1;

        // Decode into the second half of the scratch buffer; the first half keeps the key
        var position = _blockStarts[block];
        var count = BlockSpellingCount(block);
        var length = 0;
        for (var entry = 0; entry < count; entry++)
        {
            var maskIndex = ReadEntry(ref position, entry == 0, scratch, MaxSpellingBytes, ref length);

            // The head was already compared by the binary search
            if (entry == 0)
            {
                continue;
            }

            var comparison = BinaryFormat.Compare(scratch, MaxSpellingBytes, length, scratch, 0, keyLength);
            if (comparison == 0)
            {
                tags = UposSet.FromMask(_masks[maskIndex]);
                ordinal = (block << _blockShift) + entry;
                return true;
            }

            if (comparison > 0)
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>
    ///     Walks every entry of the file once and checks the structure the lookups rely on, including every analysis
    ///     against the spelling that it belongs to.
    /// </summary>
    /// <param name="declaredAnalyses">The number of analyses that the header declares.</param>
    /// <exception cref="InvalidDataException">An entry is malformed, the order is violated or the counts do not match.</exception>
    private void Validate(int declaredAnalyses)
    {
        var current = new byte[MaxSpellingBytes];
        var previous = new byte[MaxSpellingBytes];
        var previousLength = 0;
        var length = 0;
        var spellings = 0;
        long entries = 0;
        long analyses = 0;
        var analyzed = 0;
        var longest = 0;
        for (var block = 0; block < _blockStarts.Length; block++)
        {
            var position = _blockStarts[block];
            var count = BlockSpellingCount(block);
            for (var entry = 0; entry < count; entry++)
            {
                // Keep the previous spelling (also across blocks) to prove the strictly ascending order
                if (spellings > 0)
                {
                    Buffer.BlockCopy(current, 0, previous, 0, length);
                    previousLength = length;
                }

                var maskIndex = ReadEntry(ref position, entry == 0, current, 0, ref length);
                if (length == 0 || (spellings > 0 && BinaryFormat.Compare(previous, 0, previousLength, current, 0, length) >= 0))
                {
                    throw new InvalidDataException("Invalid lexicon file: the spellings are not strictly ascending.");
                }

                var listed = ValidateAnalyses(spellings, _masks[maskIndex], current, length);
                analyses += listed;
                analyzed += listed > 0 ? 1 : 0;
                spellings++;
                entries += BinaryFormat.PopCount((ulong)_masks[maskIndex]);
                longest = Math.Max(longest, length);
            }

            // Each block must end exactly where the next one starts
            var expectedEnd = block + 1 < _blockStarts.Length ? _blockStarts[block + 1] : _dataEnd;
            if (position != expectedEnd)
            {
                throw new InvalidDataException($"Invalid lexicon file: block {block} does not end where the next block starts.");
            }
        }

        if (spellings != SpellingCount || entries != EntryCount || longest != MaxSpellingBytes)
        {
            throw new InvalidDataException("Invalid lexicon file: the counts in the header do not match the data.");
        }

        if (analyses != declaredAnalyses)
        {
            throw new InvalidDataException($"Invalid lexicon file: the header declares {declaredAnalyses} analyses but the spellings have {analyses}.");
        }

        AnalyzedSpellingCount = analyzed;
    }

    /// <summary>
    ///     Checks the analyses of one spelling: the list exists, its tags are tags of the spelling, every template can be applied,
    ///     every composed base word is stored as the tagger folds text (NFC, lower case, no typographic marks) and the analyses are in the
    ///     documented order (tag, base word, features).
    /// </summary>
    /// <param name="ordinal">The position of the spelling.</param>
    /// <param name="mask">The tag mask of the spelling.</param>
    /// <param name="spelling">The buffer that holds the spelling.</param>
    /// <param name="length">The length of the spelling in bytes.</param>
    /// <returns>The number of analyses of the spelling.</returns>
    /// <exception cref="InvalidDataException">The list index, a tag, a cut, a composed base word or the order of the analyses is invalid.</exception>
    [MethodImpl(Optimized)]
    private int ValidateAnalyses(int ordinal, int mask, byte[] spelling, int length)
    {
        var list = BinaryFormat.ReadUInt16(_payload, _analyses.IndexStart + (ordinal * 2));
        if (list >= _analyses.ListTags.Length)
        {
            throw new InvalidDataException($"Invalid lexicon file: spelling {ordinal} names the missing list {list}.");
        }

        // Join integrity: an analysis never names a tag that the spelling does not have
        if ((_analyses.ListTags[list] & ~mask) != 0)
        {
            throw new InvalidDataException($"Invalid lexicon file: an analysis of spelling {ordinal} names a tag that the spelling does not have.");
        }

        // Each base word must be made of a whole-character prefix of the spelling and a valid suffix, and fit the length limit
        var first = _analyses.ListStarts[list];
        var end = _analyses.ListStarts[list + 1];
        var leadingClean = -1;
        for (var i = first; i < end; i++)
        {
            var template = _analyses.ListTemplates[i];
            var cut = _analyses.TemplateCuts[template];
            if (cut > length)
            {
                throw new InvalidDataException($"Invalid lexicon file: an analysis of spelling {ordinal} cuts {cut} bytes from a spelling of {length}.");
            }

            var keep = length - cut;
            var lemmaLength = keep + _analyses.TemplateSuffixes[template].Length;
            if ((keep < length && (spelling[keep] & 0xC0) == 0x80) || lemmaLength < 1 || lemmaLength > MaxLemmaBytes)
            {
                throw new InvalidDataException($"Invalid lexicon file: an analysis of spelling {ordinal} cuts inside a character or has a base word of {lemmaLength} bytes.");
            }

            // A base word is NFC, lower case and free of typographic marks (the fold of the tagger leaves it as it is); text made of
            // ASCII without capitals and of characters below U+0300 that the fold keeps always is, so only another base word is composed and tested
            if (!_analyses.TemplateClean[template])
            {
                ValidateBaseWord(ordinal, template, spelling, keep);
            }
            else
            {
                leadingClean = leadingClean >= 0 ? leadingClean : LeadingClean(spelling, length);
                if (keep > leadingClean)
                {
                    ValidateBaseWord(ordinal, template, spelling, keep);
                }
            }
        }

        // The documented order of a list is (tag, base word, features); lists with one analysis per tag need no test
        if (_analyses.ListTied[list])
        {
            for (var i = first + 1; i < end; i++)
            {
                var earlier = _analyses.ListTemplates[i - 1];
                var later = _analyses.ListTemplates[i];
                if (_analyses.TemplateTags[earlier] == _analyses.TemplateTags[later] && CompareAnalyses(spelling, length, earlier, later) >= 0)
                {
                    throw new InvalidDataException($"Invalid lexicon file: the analyses of spelling {ordinal} are not strictly ascending by tag, base word and features.");
                }
            }
        }

        return end - first;
    }

    /// <summary>
    ///     Counts the bytes at the start of a buffer that make up whole characters that no fold changes and that cannot combine: ASCII
    ///     without capitals and characters below U+0300 that the fold of the tagger keeps.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A text made only of such characters is a fixed point of the fold of the tagger (see
    ///         <see cref="FeatureExtractor.IsFoldFixedPoint"/>): it is in normalization form C, because every character below U+0300 is
    ///         stable under it (it has no decomposition that changes it, it is no combining mark and it never composes with a neighbor; a
    ///         test proves it for every pair), and neither the typographic folding nor the lower-casing changes it. The test is made on
    ///         the bytes: a capital letter of ASCII ends the count (eight bytes are tested at a time, then one by one), a two-byte character (U+0080 to U+02FF, lead bytes 0xC2 to
    ///         0xCB) is decoded and asked <see cref="FeatureExtractor.IsFoldStable"/>, and any other byte ends the count, so no
    ///         string is created for the committed lexicon. What follows the count is decided on the composed string.
    ///     </para>
    /// </remarks>
    /// <param name="bytes">The buffer.</param>
    /// <param name="length">The number of bytes to test.</param>
    /// <returns>The number of leading bytes that make up whole characters of this kind.</returns>
    [MethodImpl(Optimized)]
    private static int LeadingClean(byte[] bytes, int length)
    {
        // The common case, ASCII without capitals, is a tight loop (eight bytes at a time, then one by one); anything else is decided by the
        // rest of the scan, which is rarely called. Per byte lane: below 0x80 (no carry between lanes), and not from 0x41 to 0x5A, because
        // adding 0x3F sets the top bit from 0x41 on and adding 0x25 sets it from 0x5B on
        var count = 0;
        while (count + 8 <= length)
        {
            var word = BitConverter.ToUInt64(bytes, count);
            if ((word & 0x8080808080808080UL) != 0
                || ((word + 0x3F3F3F3F3F3F3F3FUL) & ~(word + 0x2525252525252525UL) & 0x8080808080808080UL) != 0)
            {
                break;
            }

            count += 8;
        }

        while (count < length)
        {
            var b = bytes[count];
            if (b >= 0x80 || (uint)(b - 'A') <= 'Z' - 'A')
            {
                return LeadingCleanFrom(bytes, count, length);
            }

            count++;
        }

        return count;
    }

    /// <summary>
    ///     Continues <see cref="LeadingClean"/> at a byte that is not a plain lower-case ASCII byte.
    /// </summary>
    /// <param name="bytes">The buffer.</param>
    /// <param name="start">The index of the byte to start at, which is the start of a character.</param>
    /// <param name="length">The number of bytes to test.</param>
    /// <returns>The index after the last byte that belongs to a whole character of this kind, counted from the start of the buffer.</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int LeadingCleanFrom(byte[] bytes, int start, int length)
    {
        var count = start;
        while (count < length)
        {
            var b = bytes[count];
            if (b < 0x80)
            {
                if (b >= (byte)'A' && b <= (byte)'Z')
                {
                    break;
                }

                count++;
            }
            else if (b >= 0xC2 && b <= 0xCB && count + 1 < length && (bytes[count + 1] & 0xC0) == 0x80
                && FeatureExtractor.IsFoldStable((char)(((b & 0x1F) << 6) | (bytes[count + 1] & 0x3F))))
            {
                count += 2;
            }
            else
            {
                break;
            }
        }

        return count;
    }

    /// <summary>
    ///     Checks that the base word that a template composes from a spelling is well-formed UTF-8 and a fixed point of the fold of the tagger.
    /// </summary>
    /// <remarks>
    ///     A stored base word is one that <c>IsFormOf</c> can match, which folds its argument first, so the base word must be in
    ///     normalization form C, lower case (<see cref="char.ToLowerInvariant(char)"/> of every character) and without typographic quotes, dashes and
    ///     ellipsis (<see cref="FeatureExtractor.IsFoldFixedPoint"/>).
    /// </remarks>
    /// <param name="ordinal">The position of the spelling, for the message.</param>
    /// <param name="template">The template.</param>
    /// <param name="spelling">The buffer that holds the spelling.</param>
    /// <param name="keep">The number of leading spelling bytes that the base word keeps.</param>
    /// <exception cref="InvalidDataException">The base word is not valid UTF-8, not NFC, not lower case or has a typographic mark.</exception>
    private void ValidateBaseWord(int ordinal, int template, byte[] spelling, int keep)
    {
        string? text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(spelling, 0, keep) + _analyses.TemplateSuffixTexts[template];
        }
        catch (DecoderFallbackException)
        {
            text = null;
        }

        if (text == null || !FeatureExtractor.IsFoldFixedPoint(text))
        {
            throw new InvalidDataException(
                $"Invalid lexicon file: an analysis of spelling {ordinal} has a base word that is not valid UTF-8 in normalization form C, in lower case and without typographic quotes, dashes and ellipsis.");
        }
    }

    /// <summary>
    ///     Orders the base words and features of two templates that share a tag, applied to one spelling, the way the writer does.
    /// </summary>
    /// <remarks>
    ///     The base word is the spelling without its last <c>cut</c> bytes followed by the suffix; the words are compared as
    ///     UTF-8 bytes without composing them. The feature sets are numbered in ascending order of their text, so the
    ///     order of the numbers is the order of the features.
    /// </remarks>
    /// <param name="spelling">The buffer that holds the spelling.</param>
    /// <param name="length">The length of the spelling in bytes.</param>
    /// <param name="left">The first template.</param>
    /// <param name="right">The second template.</param>
    /// <returns>A negative number, zero or a positive number as the analysis of <paramref name="left"/> is before, equal to or after that of <paramref name="right"/>.</returns>
    [MethodImpl(Optimized)]
    private int CompareAnalyses(byte[] spelling, int length, int left, int right)
    {
        var leftKeep = length - _analyses.TemplateCuts[left];
        var rightKeep = length - _analyses.TemplateCuts[right];
        var leftSuffix = _analyses.TemplateSuffixes[left];
        var rightSuffix = _analyses.TemplateSuffixes[right];
        var leftLength = leftKeep + leftSuffix.Length;
        var rightLength = rightKeep + rightSuffix.Length;

        // Both words start with the spelling up to the shorter of the two kept prefixes, so compare from there
        var common = Math.Min(leftKeep, rightKeep);
        var shorter = Math.Min(leftLength, rightLength);
        for (var k = common; k < shorter; k++)
        {
            var a = k < leftKeep ? spelling[k] : leftSuffix[k - leftKeep];
            var b = k < rightKeep ? spelling[k] : rightSuffix[k - rightKeep];
            if (a != b)
            {
                return a.CompareTo(b);
            }
        }

        if (leftLength != rightLength)
        {
            return leftLength.CompareTo(rightLength);
        }

        return _analyses.TemplateFeatureSets[left].CompareTo(_analyses.TemplateFeatureSets[right]);
    }

    /// <summary>
    ///     One analysis of a spelling: the base word, the tag and the features of one form that the spelling can be.
    /// </summary>
    /// <param name="Lemma">The base word, as the tagger folds text: NFC, lower case, no typographic quote, dash or ellipsis.</param>
    /// <param name="Tag">The tag of the form.</param>
    /// <param name="Features">The features in UD notation (<c>Number=Plur</c>, names ascending, joined by <c>|</c>); empty when none are recorded.</param>
    public readonly record struct Analysis(string Lemma, Upos Tag, string Features);

    /// <summary>
    ///     The validated numeric header fields.
    /// </summary>
    /// <param name="BlockShift">The log2 of the block size.</param>
    /// <param name="MaskCount">The number of palette masks.</param>
    /// <param name="SpellingCount">The number of spellings.</param>
    /// <param name="EntryCount">The number of spelling and tag pairs.</param>
    /// <param name="BlockCount">The number of blocks.</param>
    /// <param name="PayloadLength">The uncompressed payload length.</param>
    /// <param name="StoredLength">The stored payload length.</param>
    /// <param name="Checksum">The FNV-1a checksum of the stored bytes.</param>
    /// <param name="MaxSpellingBytes">The longest spelling in bytes.</param>
    /// <param name="FeatureSetCount">The number of feature sets.</param>
    /// <param name="TemplateCount">The number of analysis templates.</param>
    /// <param name="ListCount">The number of template lists.</param>
    /// <param name="AnalysisCount">The number of analyses of all spellings.</param>
    private readonly record struct HeaderFields(
        int BlockShift,
        int MaskCount,
        int SpellingCount,
        int EntryCount,
        int BlockCount,
        int PayloadLength,
        int StoredLength,
        uint Checksum,
        int MaxSpellingBytes,
        int FeatureSetCount,
        int TemplateCount,
        int ListCount,
        int AnalysisCount);

    /// <summary>
    ///     The tables of the analyses section after validation.
    /// </summary>
    private sealed class AnalysisTables
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="AnalysisTables"/> class.
        /// </summary>
        /// <param name="featureSets">The feature sets in UD notation; entry 0 is empty.</param>
        /// <param name="templateTags">The tag index of every template.</param>
        /// <param name="templateFeatureSets">The feature set index of every template.</param>
        /// <param name="templateCuts">The number of bytes that every template drops from the end of the spelling.</param>
        /// <param name="templateSuffixes">The UTF-8 suffix that every template appends.</param>
        /// <param name="templateSuffixTexts">The suffix of every template as text.</param>
        /// <param name="templateClean">Whether the suffix of every template is made of whole characters that the fold keeps (see <see cref="LeadingClean"/>).</param>
        /// <param name="listStarts">The start of every list inside <paramref name="listTemplates"/>, plus the total as the last entry.</param>
        /// <param name="listTemplates">The template indices of all lists, one list after the other.</param>
        /// <param name="listTags">The mask of the tags of every list.</param>
        /// <param name="listTied">Whether each list has two neighboring templates with the same tag.</param>
        /// <param name="indexStart">The position of the list index inside the payload.</param>
        public AnalysisTables(
            string[] featureSets,
            byte[] templateTags,
            int[] templateFeatureSets,
            int[] templateCuts,
            byte[][] templateSuffixes,
            string[] templateSuffixTexts,
            bool[] templateClean,
            int[] listStarts,
            int[] listTemplates,
            int[] listTags,
            bool[] listTied,
            int indexStart)
        {
            FeatureSets = featureSets;
            TemplateTags = templateTags;
            TemplateFeatureSets = templateFeatureSets;
            TemplateCuts = templateCuts;
            TemplateSuffixes = templateSuffixes;
            TemplateSuffixTexts = templateSuffixTexts;
            TemplateClean = templateClean;
            ListStarts = listStarts;
            ListTemplates = listTemplates;
            ListTags = listTags;
            ListTied = listTied;
            IndexStart = indexStart;
        }

        /// <summary>
        ///     Gets the state of a lexicon before its analyses section is read: no spelling has an analysis.
        /// </summary>
        public static AnalysisTables Empty { get; } = new(
            new[] { string.Empty }, Array.Empty<byte>(), Array.Empty<int>(), Array.Empty<int>(), Array.Empty<byte[]>(), Array.Empty<string>(),
            Array.Empty<bool>(), new[] { 0, 0 }, Array.Empty<int>(), new[] { 0 }, new[] { false }, 0);

        /// <summary>
        ///     Gets the feature sets in UD notation; entry 0 is empty.
        /// </summary>
        public string[] FeatureSets { get; }

        /// <summary>
        ///     Gets the tag index of every template.
        /// </summary>
        public byte[] TemplateTags { get; }

        /// <summary>
        ///     Gets the feature set index of every template.
        /// </summary>
        public int[] TemplateFeatureSets { get; }

        /// <summary>
        ///     Gets the number of bytes that every template drops from the end of the spelling.
        /// </summary>
        public int[] TemplateCuts { get; }

        /// <summary>
        ///     Gets the UTF-8 suffix that every template appends.
        /// </summary>
        public byte[][] TemplateSuffixes { get; }

        /// <summary>
        ///     Gets the suffix of every template as text.
        /// </summary>
        public string[] TemplateSuffixTexts { get; }

        /// <summary>
        ///     Gets whether the suffix of every template is made of whole characters that the fold keeps (see <see cref="LeadingClean"/>).
        /// </summary>
        public bool[] TemplateClean { get; }

        /// <summary>
        ///     Gets the start of every list inside <see cref="ListTemplates"/>, plus the total as the last entry.
        /// </summary>
        public int[] ListStarts { get; }

        /// <summary>
        ///     Gets the template indices of all lists, one list after the other.
        /// </summary>
        public int[] ListTemplates { get; }

        /// <summary>
        ///     Gets the mask of the tags of every list.
        /// </summary>
        public int[] ListTags { get; }

        /// <summary>
        ///     Gets whether each list has two neighboring templates with the same tag, so that the order of the list depends on the spelling.
        /// </summary>
        public bool[] ListTied { get; }

        /// <summary>
        ///     Gets the position of the list index (two bytes per spelling) inside the payload.
        /// </summary>
        public int IndexStart { get; }
    }
}
