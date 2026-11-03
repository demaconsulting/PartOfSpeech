using System.Text;

namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     The compiled word lexicon (format <c>POSL</c> version 1): a read-only map from a lower-case spelling to the set
///     of Universal POS tags that the spelling may carry.
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
///         The class is immutable after <see cref="Load(byte[])"/> and safe for concurrent lookups; each caller
///         supplies its own scratch buffer. <see cref="Load(byte[])"/> validates the whole file (header, checksum,
///         order, bounds), so a successful load guarantees that lookups cannot fail on malformed data.
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
    public const int HeaderLength = 40;

    /// <summary>
    ///     The format version that this build reads and writes.
    /// </summary>
    public const int FormatVersion = 1;

    /// <summary>
    ///     The largest uncompressed payload length that is accepted (16 MiB; the compiled lexicon needs about 1.6 MB).
    /// </summary>
    public const int MaxPayloadLength = 16 * 1024 * 1024;

    /// <summary>
    ///     The description of the file kind used in error messages.
    /// </summary>
    private const string Kind = "lexicon file";

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
    ///     The decompressed payload: mask palette, block offsets and the data region.
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
    ///     The exclusive end of the data region inside <see cref="_payload"/>.
    /// </summary>
    private readonly int _dataEnd;

    /// <summary>
    ///     The log2 of the number of spellings per block.
    /// </summary>
    private readonly int _blockShift;

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
    ///     Gets the size of the scratch buffer that <see cref="TryGetTags"/> needs.
    /// </summary>
    public int ScratchSize => 2 * MaxSpellingBytes;

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

        // Walk every entry once: this proves the structure so lookups never need to defend against it
        lexicon.Validate();
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
    public bool TryGetTags(char[] chars, int offset, int length, byte[] scratch, out UposSet tags)
    {
        tags = UposSet.Empty;
        if (length == 0 || length > MaxSpellingBytes)
        {
            return false;
        }

        // Encode the key as UTF-8 into the first half of the scratch buffer; a longer key cannot be present
        var keyLength = EncodeUtf8(chars, offset, length, scratch, MaxSpellingBytes);
        if (keyLength < 0)
        {
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
        return ScanBlock(block, scratch, keyLength, out tags);
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
    ///     Reads the uncompressed form of a lexicon file: the header with the compression flag cleared followed by the payload.
    /// </summary>
    /// <remarks>
    ///     The Deflate bytes differ between runtimes, but the uncompressed form is deterministic, so the compiler
    ///     tool compares this form to decide whether a committed binary is current.
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
        if (BinaryFormat.ReadUInt16(file, 38) != 0)
        {
            throw new InvalidDataException("Invalid lexicon file: the reserved field is not zero.");
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

        return new HeaderFields(blockShift, maskCount, spellingCount, entryCount, blockCount, payloadLength, storedLength, checksum, maxSpellingBytes);
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
    /// <param name="tags">Receives the tags when the key is found.</param>
    /// <returns><c>true</c> if the key is in the block.</returns>
    private bool ScanBlock(int block, byte[] scratch, int keyLength, out UposSet tags)
    {
        tags = UposSet.Empty;

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
    ///     Walks every entry of the file once and checks the structure the lookups rely on.
    /// </summary>
    /// <exception cref="InvalidDataException">An entry is malformed, the order is violated or the counts do not match.</exception>
    private void Validate()
    {
        var current = new byte[MaxSpellingBytes];
        var previous = new byte[MaxSpellingBytes];
        var previousLength = 0;
        var length = 0;
        var spellings = 0;
        long entries = 0;
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
    }

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
    /// <param name="Checksum">The payload checksum.</param>
    /// <param name="MaxSpellingBytes">The longest spelling in bytes.</param>
    private readonly record struct HeaderFields(
        int BlockShift,
        int MaskCount,
        int SpellingCount,
        int EntryCount,
        int BlockCount,
        int PayloadLength,
        int StoredLength,
        uint Checksum,
        int MaxSpellingBytes);
}
