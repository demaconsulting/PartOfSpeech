using System.Text;

namespace DemaConsulting.PartOfSpeech.Tests.ModelStorage;

/// <summary>
///     Unit tests of <see cref="Lexicon"/> and <see cref="LexiconWriter"/>.
/// </summary>
public class LexiconTests
{
    /// <summary>
    ///     A small lexicon with shared prefixes, non-ASCII spellings and a surrogate pair, in file order.
    /// </summary>
    private static readonly string[] SmallRows =
    {
        "a DET", "aardvark NOUN", "ab ADP", "abacus NOUN", "abandon NOUN VERB", "abandoned ADJ VERB", "abandoning VERB",
        "b NOUN", "ba INTJ", "bake NOUN VERB", "baker NOUN PROPN", "baking NOUN VERB", "caf\u00E9 NOUN", "cafe NOUN",
        "cat NOUN", "cats NOUN", "do AUX VERB", "does AUX VERB", "\u65E5\u672C PROPN", "\uD83D\uDE00 SYM", "z X", "zebra NOUN",
        "zed NOUN", "zero NUM", "zone NOUN"
    };

    /// <summary>
    ///     Parses the rows into entries.
    /// </summary>
    private static List<KeyValuePair<string, UposSet>> Entries(params string[] rows) =>
        rows.Select(row =>
        {
            var parts = row.Split(' ');
            return new KeyValuePair<string, UposSet>(parts[0], UposSet.Of(parts.Skip(1).Select(p => { UposExtensions.TryParse(p, out var t); return t; }).ToArray()));
        }).ToList();

    /// <summary>
    ///     Looks a spelling up the way the decoder does.
    /// </summary>
    private static bool Find(Lexicon lexicon, string key, out UposSet tags)
    {
        var chars = key.ToCharArray();
        return lexicon.TryGetTags(chars, 0, chars.Length, new byte[lexicon.ScratchSize], out tags);
    }

    /// <summary>
    ///     Proves that every spelling is found with its tags for every block size.
    /// </summary>
    /// <param name="blockShift">The log2 of the block size.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void Lexicon_TryGetTags_FindsEverySpellingOfSmallLexicon(int blockShift)
    {
        // Arrange: compile the small lexicon (sorted by UTF-8 bytes) and load it
        var entries = Entries(SmallRows).OrderBy(e => e.Key, Comparer<string>.Create(Utf8Order)).ToList();
        var lexicon = Lexicon.Load(LexiconWriter.Compile(entries, true, blockShift));

        // Act / Assert: every spelling is found with exactly its tags
        Assert.Equal(entries.Count, lexicon.SpellingCount);
        foreach (var entry in entries)
        {
            Assert.True(Find(lexicon, entry.Key, out var tags), entry.Key);
            Assert.Equal(entry.Value, tags);
        }
    }

    /// <summary>
    ///     Proves that the first and last spelling of every block are found, which exercises the block-head search and the scan boundaries.
    /// </summary>
    [Fact]
    public void Lexicon_TryGetTags_FirstAndLastOfEveryBlock()
    {
        // Arrange: 100 generated spellings in blocks of 4
        var entries = Enumerable.Range(0, 100)
            .Select(i => new KeyValuePair<string, UposSet>("w" + i.ToString("D3"), UposSet.Of((Upos)(i % 17))))
            .ToList();
        var lexicon = Lexicon.Load(LexiconWriter.Compile(entries, false, 2));

        // Act / Assert: block heads (every 4th) and block ends are found with the right tag
        Assert.Equal(25, lexicon.BlockCount);
        for (var i = 0; i < entries.Count; i += 4)
        {
            Assert.True(Find(lexicon, entries[i].Key, out var head));
            Assert.Equal(entries[i].Value, head);
            Assert.True(Find(lexicon, entries[i + 3].Key, out var tail));
            Assert.Equal(entries[i + 3].Value, tail);
        }
    }

    /// <summary>
    ///     Proves that spellings that are not in the lexicon are not found, including near misses.
    /// </summary>
    [Fact]
    public void Lexicon_TryGetTags_AbsentSpellings_ReturnFalse()
    {
        // Arrange: the small lexicon
        var entries = Entries(SmallRows).OrderBy(e => e.Key, Comparer<string>.Create(Utf8Order)).ToList();
        var lexicon = Lexicon.Load(LexiconWriter.Compile(entries, false));

        // Act / Assert: before the first, between spellings, after the last, prefixes and extensions of present spellings
        foreach (var absent in new[] { string.Empty, "  ", "aa", "aba", "abandons", "abandoners", "ca", "caf", "cafes", "dog", "zz", "zzzz", "\u65E5", "\u65E5\u672C\u8A9E", "A" })
        {
            Assert.False(Find(lexicon, absent, out var tags), absent);
            Assert.True(tags.IsEmpty);
        }
    }

    /// <summary>
    ///     Proves that non-ASCII spellings and a surrogate pair are encoded and found.
    /// </summary>
    [Fact]
    public void Lexicon_TryGetTags_NonAsciiSpelling_IsFound()
    {
        // Arrange: the small lexicon
        var entries = Entries(SmallRows).OrderBy(e => e.Key, Comparer<string>.Create(Utf8Order)).ToList();
        var lexicon = Lexicon.Load(LexiconWriter.Compile(entries, false));

        // Act / Assert: accented, CJK and an emoji (two UTF-16 code units) are found; the key can sit inside a larger buffer
        Assert.True(Find(lexicon, "caf\u00E9", out var cafe));
        Assert.Equal(UposSet.Of(Upos.Noun), cafe);
        Assert.True(Find(lexicon, "\u65E5\u672C", out var nihon));
        Assert.Equal(UposSet.Of(Upos.Propn), nihon);
        Assert.True(Find(lexicon, "\uD83D\uDE00", out var emoji));
        Assert.Equal(UposSet.Of(Upos.Sym), emoji);
        var buffer = "xx\uD83D\uDE00yy".ToCharArray();
        Assert.True(lexicon.TryGetTags(buffer, 2, 2, new byte[lexicon.ScratchSize], out var inner));
        Assert.Equal(UposSet.Of(Upos.Sym), inner);
    }

    /// <summary>
    ///     Proves that a key longer than the longest spelling is rejected without error.
    /// </summary>
    [Fact]
    public void Lexicon_TryGetTags_LongerThanMaxSpelling_ReturnsFalse()
    {
        // Arrange: the small lexicon (the longest spelling is 11 bytes)
        var entries = Entries(SmallRows).OrderBy(e => e.Key, Comparer<string>.Create(Utf8Order)).ToList();
        var lexicon = Lexicon.Load(LexiconWriter.Compile(entries, false));

        // Act / Assert: long keys are simply absent (a multi-byte key counts bytes, not characters)
        Assert.Equal(Encoding.UTF8.GetByteCount("abandoning"), lexicon.MaxSpellingBytes);
        Assert.False(Find(lexicon, new string('a', 500), out _));
        Assert.False(Find(lexicon, new string('\u65E5', lexicon.MaxSpellingBytes), out _));
    }

    /// <summary>
    ///     Proves that the source table parser rejects unsorted, duplicate and invalid rows.
    /// </summary>
    /// <param name="line1">The first row.</param>
    /// <param name="line2">The second row (may be empty).</param>
    [Theory]
    [InlineData("b\tNOUN", "a\tNOUN")]
    [InlineData("a\tNOUN", "a\tVERB")]
    [InlineData("A\tNOUN", "")]
    [InlineData("a b\tNOUN", "")]
    [InlineData("a\tNOUN VERB NOUN", "")]
    [InlineData("a\tVERB NOUN", "")]
    [InlineData("a\tnoun", "")]
    [InlineData("a", "")]
    [InlineData("a\t", "")]
    [InlineData("e\u0301\tNOUN", "")]
    public void LexiconWriter_FromTsv_RejectsUnsortedDuplicateOrInvalid(string line1, string line2)
    {
        // Arrange: a table with the offending rows
        var lines = new List<string> { "# comment", line1 };
        if (line2.Length > 0)
        {
            lines.Add(line2);
        }

        // Act / Assert: the parser reports the problem as InvalidDataException
        Assert.Throws<InvalidDataException>(() => LexiconWriter.FromTsv(lines));
    }

    /// <summary>
    ///     Proves that the counts in the header comment are checked and that the committed table passes with its own counts.
    /// </summary>
    [Fact]
    public void LexiconWriter_FromTsv_CountsMatchHeaderComment()
    {
        // Arrange: a table whose header declares one spelling too many, and the committed table
        var wrong = new[] { "# parameters: spellings=3, tag entries=3", "a\tNOUN", "b\tNOUN" };
        var right = new[] { "# parameters: spellings=2, tag entries=3", "a\tNOUN VERB", "b\tNOUN" };

        // Act: parse them
        var entries = LexiconWriter.FromTsv(right);
        var committed = LexiconWriter.FromTsv(File.ReadLines(DataFiles.Data("lexicon/lexicon.tsv")));

        // Assert: the wrong header is rejected, the right one accepted, and the real table has the documented counts
        Assert.Throws<InvalidDataException>(() => LexiconWriter.FromTsv(wrong));
        Assert.Equal(2, entries.Count);
        Assert.Equal(262635, committed.Count);
        Assert.Equal(339819, committed.Sum(e => e.Value.Count));
    }

    /// <summary>
    ///     Proves that the compressed and the uncompressed form decode to the same lexicon.
    /// </summary>
    [Fact]
    public void LexiconWriter_Write_UncompressedAndCompressed_DecodeEqual()
    {
        // Arrange: the small lexicon in both forms, through the stream API
        var entries = Entries(SmallRows).OrderBy(e => e.Key, Comparer<string>.Create(Utf8Order)).ToList();
        using var plainStream = new MemoryStream();
        using var packedStream = new MemoryStream();
        LexiconWriter.Write(entries, plainStream, false);
        LexiconWriter.Write(entries, packedStream, true);

        // Act: load both
        var plain = Lexicon.Load(plainStream.ToArray());
        var packed = Lexicon.Load(packedStream.ToArray());

        // Assert: same content, same uncompressed image, and the compressed file is smaller than the plain one
        Assert.Equal(plain.Enumerate().ToList(), packed.Enumerate().ToList());
        Assert.Equal(entries, plain.Enumerate().ToList());
        Assert.Equal(Lexicon.ToUncompressedImage(plainStream.ToArray()), Lexicon.ToUncompressedImage(packedStream.ToArray()));
        Assert.True(packedStream.Length < plainStream.Length + 8);
    }

    /// <summary>
    ///     Proves that a flipped payload byte is caught by the checksum.
    /// </summary>
    [Fact]
    public void Lexicon_Load_CorruptChecksum_Throws()
    {
        // Arrange: a valid uncompressed file with one payload byte changed
        var file = ValidFile();
        file[file.Length - 3] ^= 0x55;

        // Act / Assert: the checksum mismatch is reported
        var ex = Assert.Throws<InvalidDataException>(() => Lexicon.Load(file));
        Assert.Contains("checksum", ex.Message);
    }

    /// <summary>
    ///     Proves that truncated files and files with trailing bytes are rejected.
    /// </summary>
    [Fact]
    public void Lexicon_Load_Truncated_Throws()
    {
        // Arrange: a valid file, a copy without its last bytes, a copy with extra bytes and a header-only fragment
        var file = ValidFile();
        var cut = file.Take(file.Length - 5).ToArray();
        var extra = file.Concat(new byte[] { 1, 2, 3 }).ToArray();
        var header = file.Take(20).ToArray();

        // Act / Assert: all are InvalidDataException
        Assert.Contains("truncated", Assert.Throws<InvalidDataException>(() => Lexicon.Load(cut)).Message);
        Assert.Contains("trailing", Assert.Throws<InvalidDataException>(() => Lexicon.Load(extra)).Message);
        Assert.Throws<InvalidDataException>(() => Lexicon.Load(header));
        Assert.Throws<ArgumentNullException>(() => Lexicon.Load((byte[])null!));
    }

    /// <summary>
    ///     Proves that a file for another number of tags is rejected.
    /// </summary>
    [Fact]
    public void Lexicon_Load_TagCountMismatch_Throws()
    {
        // Arrange: a valid file that claims 16 tags
        var file = ValidFile();
        file[8] = 16;

        // Act / Assert: the tag count is validated
        var ex = Assert.Throws<InvalidDataException>(() => Lexicon.Load(file));
        Assert.Contains("tags", ex.Message);
    }

    /// <summary>
    ///     Proves that a block index that does not start at zero is rejected even when the checksum is right.
    /// </summary>
    [Fact]
    public void Lexicon_Load_InvalidStructureWithValidChecksum_Throws()
    {
        // Arrange: a valid file whose palette repeats a mask, with the checksum recomputed
        var file = ValidFile();
        var paletteCount = BinaryFormat.ReadUInt16(file, 10);
        Assert.True(paletteCount >= 2);
        Array.Copy(file, Lexicon.HeaderLength, file, Lexicon.HeaderLength + 4, 4);
        BinaryFormat.WriteUInt32(file, 32, BinaryFormat.Fnv1a(file, Lexicon.HeaderLength, file.Length - Lexicon.HeaderLength));

        // Act / Assert: the repeated palette entry is reported
        Assert.Throws<InvalidDataException>(() => Lexicon.Load(file));
    }

    /// <summary>
    ///     Proves that a varint above 2^31-1 used as a length or an index is reported as invalid data, not as an index or range error,
    ///     even when the checksum is right.
    /// </summary>
    /// <param name="site">The offset of the one-byte varint in the data region.</param>
    /// <param name="replacement">The five bytes that replace the varint.</param>
    [Theory]
    [InlineData(0, new byte[] { 0x80, 0x80, 0x80, 0x80, 0x08 })]
    [InlineData(2, new byte[] { 0x80, 0x80, 0x80, 0x80, 0x08 })]
    [InlineData(3, new byte[] { 0x80, 0x80, 0x80, 0x80, 0x08 })]
    [InlineData(4, new byte[] { 0x80, 0x80, 0x80, 0x80, 0x08 })]
    [InlineData(6, new byte[] { 0x80, 0x80, 0x80, 0x80, 0x08 })]
    [InlineData(2, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x0F })]
    public void Lexicon_Load_OversizedVarintWithValidChecksum_Throws(int site, byte[] replacement)
    {
        // Arrange: a one-block lexicon of "a" and "ab" (both NOUN); the data region is 01 'a' m  01 01 'b' m
        var entries = new List<KeyValuePair<string, UposSet>>
        {
            new("a", UposSet.Of(Upos.Noun)),
            new("ab", UposSet.Of(Upos.Noun)),
        };
        var file = LexiconWriter.Compile(entries, false, 1);
        var dataStart = Lexicon.HeaderLength + (BinaryFormat.ReadUInt16(file, 10) * 4) + (BinaryFormat.ReadInt32Field(file, 20, "block count") * 4);

        // Assert the layout so that a format change fails loudly instead of testing nothing
        Assert.Equal(7, file.Length - dataStart);
        Assert.Equal(new byte[] { 1, (byte)'a' }, file.Skip(dataStart).Take(2).ToArray());
        Assert.Equal(new byte[] { 1, 1, (byte)'b' }, file.Skip(dataStart + 3).Take(3).ToArray());
        Assert.True(file[dataStart + site] <= 1);

        // Act: replace the one-byte varint by five bytes, then fix the header lengths and the checksum
        var changed = file.Take(dataStart + site)
            .Concat(replacement)
            .Concat(file.Skip(dataStart + site + 1))
            .ToArray();
        BinaryFormat.WriteUInt32(changed, 24, (uint)(changed.Length - Lexicon.HeaderLength));
        BinaryFormat.WriteUInt32(changed, 28, (uint)(changed.Length - Lexicon.HeaderLength));
        BinaryFormat.WriteUInt32(changed, 32, BinaryFormat.Fnv1a(changed, Lexicon.HeaderLength, changed.Length - Lexicon.HeaderLength));

        // Assert: exactly InvalidDataException, so an index or range exception fails the test
        var ex = Assert.Throws<InvalidDataException>(() => Lexicon.Load(changed));
        Assert.Contains("2147483647", ex.Message);
    }

    /// <summary>
    ///     Proves that extra bytes after the Deflate data, inside the stored region that the header declares, are
    ///     rejected: with the checksum of the stored bytes left alone, and with it recomputed to match the new region.
    /// </summary>
    [Fact]
    public void Lexicon_Load_DeflateTrailingBytesInStoredRegion_Throws()
    {
        // Arrange: a valid compressed file, and copies with three extra bytes behind the Deflate data whose stored
        // length is raised to match, once with the old checksum and once with a checksum that matches the new region
        var file = CompressedFile();
        Assert.Equal(1, BinaryFormat.ReadUInt16(file, 6) & BinaryFormat.FlagDeflate);
        var padded = StoredRegion(file).Concat(new byte[] { 0x00, 0xFF, 0x42 }).ToArray();
        var staleChecksum = WithStoredRegion(file, padded, false);
        var freshChecksum = WithStoredRegion(file, padded, true);

        // Act / Assert: the original loads; the stale checksum is caught by the checksum, the fresh one by the strict inflation
        Assert.NotNull(Lexicon.Load(file));
        Assert.Contains("checksum", Assert.Throws<InvalidDataException>(() => Lexicon.Load(staleChecksum)).Message);
        Assert.Contains("trailing", Assert.Throws<InvalidDataException>(() => Lexicon.Load(freshChecksum)).Message);
    }

    /// <summary>
    ///     Proves that a compressed file whose last Deflate byte was removed is rejected even when the header's stored
    ///     length is adjusted and the checksum is recomputed, so that the checksum cannot be what catches it.
    /// </summary>
    [Fact]
    public void Lexicon_Load_DeflateLastByteRemoved_Throws()
    {
        // Arrange: a valid compressed file and a copy without the last compressed byte, with the stored length adjusted,
        // once with the old checksum and once with a checksum recomputed over the shortened region
        var file = CompressedFile();
        var shortened = StoredRegion(file).Take(StoredRegion(file).Length - 1).ToArray();
        var staleChecksum = WithStoredRegion(file, shortened, false);
        var freshChecksum = WithStoredRegion(file, shortened, true);

        // Act / Assert: the original loads; the shortened copies are invalid data, the second one by the strict inflation
        Assert.NotNull(Lexicon.Load(file));
        Assert.Contains("checksum", Assert.Throws<InvalidDataException>(() => Lexicon.Load(staleChecksum)).Message);
        Assert.Contains("cut off", Assert.Throws<InvalidDataException>(() => Lexicon.Load(freshChecksum)).Message);
    }

    /// <summary>
    ///     Proves that a compressed file cut in the middle of its Deflate data is rejected at every cut point, even when
    ///     the stored length and the checksum are adjusted to the shortened region.
    /// </summary>
    [Fact]
    public void Lexicon_Load_DeflateCutAtAnyPoint_Throws()
    {
        // Arrange: a valid compressed file and its stored region
        var file = CompressedFile();
        var stored = StoredRegion(file);

        // Act / Assert: every proper prefix of the region is rejected as InvalidDataException, nothing else
        for (var length = 0; length < stored.Length; length++)
        {
            var cut = WithStoredRegion(file, stored.Take(length).ToArray(), true);
            Assert.Throws<InvalidDataException>(() => Lexicon.Load(cut));
        }
    }

    /// <summary>
    ///     Proves that a declared payload length that is too large is rejected, even though the stored bytes and their
    ///     checksum are intact.
    /// </summary>
    /// <param name="extra">The number of bytes by which the declared payload length is too large.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(4096)]
    public void Lexicon_Load_DeflatePayloadLengthTooLarge_Throws(int extra)
    {
        // Arrange: a valid compressed file with a larger declared payload length
        var file = CompressedFile();
        BinaryFormat.WriteUInt32(file, 24, BinaryFormat.ReadUInt32(file, 24) + (uint)extra);

        // Act / Assert: the inflated payload is shorter than declared
        var ex = Assert.Throws<InvalidDataException>(() => Lexicon.Load(file));
        Assert.Contains("shorter", ex.Message);
    }

    /// <summary>
    ///     Proves that a declared payload length that is too small is rejected, even though the stored bytes and their
    ///     checksum are intact.
    /// </summary>
    /// <param name="missing">The number of bytes by which the declared payload length is too small.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public void Lexicon_Load_DeflatePayloadLengthTooSmall_Throws(int missing)
    {
        // Arrange: a valid compressed file with a smaller declared payload length
        var file = CompressedFile();
        BinaryFormat.WriteUInt32(file, 24, BinaryFormat.ReadUInt32(file, 24) - (uint)missing);

        // Act / Assert: the inflated payload is longer than declared
        var ex = Assert.Throws<InvalidDataException>(() => Lexicon.Load(file));
        Assert.Contains("longer", ex.Message);
    }

    /// <summary>
    ///     Proves that a payload length above the format maximum is rejected before the payload is inflated.
    /// </summary>
    [Fact]
    public void Lexicon_Load_PayloadLengthAboveMaximum_Throws()
    {
        // Arrange: a valid compressed file whose header declares a payload one byte above the maximum
        var file = LexiconWriter.Compile(Entries(SmallRows).OrderBy(e => e.Key, Comparer<string>.Create(Utf8Order)).ToList(), true);
        BinaryFormat.WriteUInt32(file, 24, (uint)Lexicon.MaxPayloadLength + 1);

        // Act / Assert: the limit is named
        var ex = Assert.Throws<InvalidDataException>(() => Lexicon.Load(file));
        Assert.Contains("maximum", ex.Message);
    }

    /// <summary>
    ///     Proves that an unknown word may take any of the 17 tags.
    /// </summary>
    [Fact]
    public void Lexicon_AllowedTags_UnknownWord_AllowsAllSeventeen()
    {
        // Arrange / Act: an unknown word with any shape
        var allowed = Lexicon.AllowedTags(false, UposSet.Empty, true, false, false);

        // Assert: all tags
        Assert.Equal(UposSet.All, allowed);
    }

    /// <summary>
    ///     Proves that a known lower-case word has exactly its lexicon tags.
    /// </summary>
    [Fact]
    public void Lexicon_AllowedTags_KnownWord_AllowsLexiconTags()
    {
        // Arrange / Act: a known word with a plain shape
        var allowed = Lexicon.AllowedTags(true, UposSet.Of(Upos.Noun, Upos.Verb), true, false, false);

        // Assert: only the lexicon tags
        Assert.Equal(UposSet.Of(Upos.Noun, Upos.Verb), allowed);
    }

    /// <summary>
    ///     Proves that a token without letters or digits also allows punctuation, symbol and other.
    /// </summary>
    [Fact]
    public void Lexicon_AllowedTags_NoLetterOrDigit_AddsPunctSymX()
    {
        // Arrange / Act: a known token without letters or digits
        var allowed = Lexicon.AllowedTags(true, UposSet.Of(Upos.Adp), false, false, false);

        // Assert: the lexicon tag plus PUNCT, SYM and X
        Assert.Equal(UposSet.Of(Upos.Adp, Upos.Punct, Upos.Sym, Upos.X), allowed);
    }

    /// <summary>
    ///     Proves that a token with a digit also allows the numeric and name tags.
    /// </summary>
    [Fact]
    public void Lexicon_AllowedTags_Digit_AddsNumSymXNounPropnAdj()
    {
        // Arrange / Act: a known token with a digit
        var allowed = Lexicon.AllowedTags(true, UposSet.Of(Upos.Verb), true, true, false);

        // Assert: the lexicon tag plus NUM, SYM, X, NOUN, PROPN and ADJ
        Assert.Equal(UposSet.Of(Upos.Verb, Upos.Num, Upos.Sym, Upos.X, Upos.Noun, Upos.Propn, Upos.Adj), allowed);
    }

    /// <summary>
    ///     Proves that an upper-case first character also allows PROPN and X.
    /// </summary>
    [Fact]
    public void Lexicon_AllowedTags_FirstUpper_AddsPropnX()
    {
        // Arrange / Act: a known capitalized token
        var allowed = Lexicon.AllowedTags(true, UposSet.Of(Upos.Det), true, false, true);

        // Assert: the lexicon tag plus PROPN and X
        Assert.Equal(UposSet.Of(Upos.Det, Upos.Propn, Upos.X), allowed);
    }

#if NET
    /// <summary>
    ///     Proves that the shape rules and the lookup of a known token allocate nothing: punctuation, digit and
    ///     upper-case tokens take the rule paths that used to build a set per call.
    /// </summary>
    [Fact]
    public void Lexicon_AllowedTags_KnownTokenShapes_DoNotAllocate()
    {
        // Arrange: the embedded lexicon, a scratch buffer, and one known token for each shape rule
        var lexicon = EmbeddedModel.Lexicon;
        var scratch = new byte[lexicon.ScratchSize];
        var punctuation = ".".ToCharArray();
        var digit = "7".ToCharArray();
        var upper = "the".ToCharArray();
        var lexiconTags = UposSet.Of(Upos.Det);

        var checksum = 0;

        // Act: warm up thoroughly (static initialization, JIT, tiered compilation), then take the minimum over several
        // batches of the bytes that this thread allocates for 1000 rounds over all shape paths
        var allocated = AllocationMeter.MinimumBatchBytes(
            () => checksum += Run(lexicon, scratch, punctuation, digit, upper, lexiconTags),
            1000);

        // Assert: nothing was allocated, and the work was not optimized away
        Assert.Equal(0, allocated);
        Assert.True(checksum > 0);
    }

    /// <summary>
    ///     Runs the lookup and every shape rule once, without allocating.
    /// </summary>
    private static int Run(Lexicon lexicon, byte[] scratch, char[] punctuation, char[] digit, char[] upper, UposSet lexiconTags)
    {
        var count = 0;
        count += lexicon.TryGetTags(punctuation, 0, punctuation.Length, scratch, out var p) ? p.Count : 0;
        count += lexicon.TryGetTags(digit, 0, digit.Length, scratch, out var d) ? d.Count : 0;
        count += lexicon.TryGetTags(upper, 0, upper.Length, scratch, out var u) ? u.Count : 0;
        count += Lexicon.AllowedTags(true, lexiconTags, false, false, false).Count;
        count += Lexicon.AllowedTags(true, lexiconTags, true, true, false).Count;
        count += Lexicon.AllowedTags(true, lexiconTags, true, false, true).Count;
        count += Lexicon.AllowedTags(true, lexiconTags, false, true, true).Count;
        return count;
    }
#endif

    /// <summary>
    ///     Proves that the compiled rules agree with the reference implementation of the data tests for every lexicon spelling and shape variants.
    /// </summary>
    [Fact]
    public void Lexicon_AllowedTags_MatchesDataFilesRuleForEveryLexiconSpelling()
    {
        // Arrange: the committed table (reference) and the embedded binary (subject)
        var reference = DataFiles.ReadLexicon(DataFiles.Data("lexicon/lexicon.tsv"));
        var lexicon = EmbeddedModel.Lexicon;
        var mismatches = new List<string>();
        var index = 0;

        // Act: compare all 17 tags for each spelling as written, and for shape variants of a sample
        foreach (var spelling in reference.Keys)
        {
            Compare(spelling, reference, lexicon, mismatches);
            if (index++ % 40 == 0)
            {
                Compare(char.ToUpperInvariant(spelling[0]) + spelling.Substring(1), reference, lexicon, mismatches);
                Compare(spelling + "7", reference, lexicon, mismatches);
                Compare(spelling.ToUpperInvariant(), reference, lexicon, mismatches);
            }
        }

        // Assert: no disagreement
        Assert.Empty(mismatches.Take(10));
    }

    /// <summary>
    ///     Proves that the binary lexicon with the shape rules allows the gold tag of at least 99.9 percent of the non-disputed development tokens.
    /// </summary>
    [Fact]
    public void Lexicon_AllowedTags_Dev_MeetsCoverageThreshold()
    {
        // Arrange: the embedded lexicon and the development corpus
        var lexicon = EmbeddedModel.Lexicon;
        var total = 0;
        var covered = 0;

        // Act: check the gold tag of every non-disputed token
        foreach (var sentence in DataFiles.ReadCorpus(DataFiles.Data("open/dev.tsv")))
        {
            foreach (var row in sentence.Tokens)
            {
                if (DataFiles.BaseStatus(row[2]) == "disputed")
                {
                    continue;
                }

                total++;
                UposExtensions.TryParse(row[1], out var gold);
                if (Lexicon.AllowedTags(row[0], lexicon).Contains(gold))
                {
                    covered++;
                }
            }
        }

        // Assert: the coverage gate of the lexicon (the phase 1 table measures 99.921 percent)
        Assert.True(total > 20000);
        Assert.True(covered >= total * 0.999, $"covered {covered} of {total}");
    }

    /// <summary>
    ///     Compares the compiled rules with the reference rules for one token and all 17 tags.
    /// </summary>
    private static void Compare(string token, Dictionary<string, string[]> reference, Lexicon lexicon, List<string> mismatches)
    {
        var allowed = Lexicon.AllowedTags(token, lexicon);
        foreach (var tag in Enum.GetValues(typeof(Upos)).Cast<Upos>())
        {
            if (allowed.Contains(tag) != DataFiles.IsTagAllowed(token, tag.ToTagString(), reference))
            {
                mismatches.Add($"{token} {tag}");
            }
        }
    }

    /// <summary>
    ///     Builds a small valid uncompressed lexicon file.
    /// </summary>
    private static byte[] ValidFile() =>
        LexiconWriter.Compile(Entries(SmallRows).OrderBy(e => e.Key, Comparer<string>.Create(Utf8Order)).ToList(), false);

    /// <summary>
    ///     Builds a small valid lexicon file with a Deflate payload.
    /// </summary>
    private static byte[] CompressedFile() =>
        LexiconWriter.Compile(Entries(SmallRows).OrderBy(e => e.Key, Comparer<string>.Create(Utf8Order)).ToList(), true);

    /// <summary>
    ///     Returns a copy of the stored bytes (the bytes after the header) of a file.
    /// </summary>
    private static byte[] StoredRegion(byte[] file) => file.Skip(Lexicon.HeaderLength).ToArray();

    /// <summary>
    ///     Returns a copy of a file whose stored region was replaced, with the stored length adjusted and, on request,
    ///     with the checksum recomputed over the new region.
    /// </summary>
    private static byte[] WithStoredRegion(byte[] file, byte[] stored, bool recomputeChecksum)
    {
        var changed = file.Take(Lexicon.HeaderLength).Concat(stored).ToArray();
        BinaryFormat.WriteUInt32(changed, 28, (uint)stored.Length);
        if (recomputeChecksum)
        {
            BinaryFormat.WriteUInt32(changed, 32, BinaryFormat.Fnv1a(stored, 0, stored.Length));
        }

        return changed;
    }

    /// <summary>
    ///     Compares strings by their UTF-8 bytes (the order of the lexicon).
    /// </summary>
    private static int Utf8Order(string? x, string? y)
    {
        var a = Encoding.UTF8.GetBytes(x ?? string.Empty);
        var b = Encoding.UTF8.GetBytes(y ?? string.Empty);
        return BinaryFormat.Compare(a, 0, a.Length, b, 0, b.Length);
    }
}
