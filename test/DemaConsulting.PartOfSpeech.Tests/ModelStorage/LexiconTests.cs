// cspell:ignore Fcdefghijklmnopqrstuvwxyz mnopqrstuvwxyz jklmnopqrst uvwxyz
using System.Globalization;
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

        // Assert the layout so that a format change fails loudly instead of testing nothing; behind the 7 data bytes sit the
        // 6 bytes of the empty analyses section (the empty feature set, the empty list and two list indices)
        Assert.Equal(7 + 6, file.Length - dataStart);
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
    ///     Proves that the ordinal that <see cref="Lexicon.TryFind"/> reports is the position of the spelling in the table for every block size.
    /// </summary>
    /// <param name="blockShift">The log2 of the block size.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void Lexicon_TryFind_ReportsThePositionOfEverySpelling(int blockShift)
    {
        // Arrange: the small lexicon in the given block size
        var entries = Entries(SmallRows).OrderBy(e => e.Key, Comparer<string>.Create(Utf8Order)).ToList();
        var lexicon = Lexicon.Load(LexiconWriter.Compile(entries, false, blockShift));
        var scratch = new byte[lexicon.ScratchSize];

        // Act / Assert: every spelling is found at its position with its tags; an absent one has ordinal -1
        for (var i = 0; i < entries.Count; i++)
        {
            var key = entries[i].Key.ToCharArray();
            Assert.True(lexicon.TryFind(key, 0, key.Length, scratch, out var ordinal, out var tags), entries[i].Key);
            Assert.Equal(i, ordinal);
            Assert.Equal(entries[i].Value, tags);
        }

        var absent = "abandons".ToCharArray();
        Assert.False(lexicon.TryFind(absent, 0, absent.Length, scratch, out var missing, out _));
        Assert.Equal(-1, missing);
    }

    /// <summary>
    ///     Proves that a lexicon compiled without analyses is a version 3 file whose analyses section is empty.
    /// </summary>
    [Fact]
    public void Lexicon_Compile_WithoutAnalyses_ReadsBackWithEmptyAnalyses()
    {
        // Arrange / Act: compile the small lexicon with the overload that has no analyses and load it
        var entries = Entries(SmallRows).OrderBy(e => e.Key, Comparer<string>.Create(Utf8Order)).ToList();
        var lexicon = Lexicon.Load(LexiconWriter.Compile(entries, true));
        var scratch = new byte[lexicon.ScratchSize];
        var key = "cats".ToCharArray();
        Assert.True(lexicon.TryFind(key, 0, key.Length, scratch, out var ordinal, out _));

        // Assert: the empty feature set, no template, the empty list, no analysis anywhere
        Assert.Equal((ushort)Lexicon.FormatVersion, BinaryFormat.ReadUInt16(ValidFile(), 4));
        Assert.Equal(1, lexicon.FeatureSetCount);
        Assert.Equal(0, lexicon.TemplateCount);
        Assert.Equal(1, lexicon.ListCount);
        Assert.Equal(0, lexicon.AnalysisCount);
        Assert.Equal(0, lexicon.AnalyzedSpellingCount);
        Assert.Empty(lexicon.GetAnalyses(ordinal));
        Assert.True(lexicon.AnalyzedTags(ordinal).IsEmpty);
        Assert.Empty(lexicon.EnumerateAnalyses());
        Assert.Equal(entries, lexicon.Enumerate().ToList());
    }

    /// <summary>
    ///     Proves that analyses of every shape survive the round trip, compressed and not: no cut, a cut inside the word, the whole
    ///     word replaced, a suffix only, non-ASCII base words, a cut next to a multi-byte character and a spelling with seven analyses.
    /// </summary>
    /// <param name="compress">Whether the payload is stored as Deflate.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Lexicon_GetAnalyses_EveryShapeOfAnalysis_RoundTrips(bool compress)
    {
        // Arrange: spellings with analyses of every shape, and one without
        var entries = new[]
        {
            Entry("cats", Upos.Noun, Upos.Verb), Entry("went", Upos.Verb), Entry("caf\u00E9", Upos.Noun), Entry("caf\u00E9s", Upos.Noun),
            Entry("go", Upos.Verb), Entry("i", Upos.Noun), Entry("run", Upos.Adj, Upos.Adv, Upos.Noun, Upos.Verb), Entry("\u65E5\u672C", Upos.Propn),
            Entry("zzz", Upos.X)
        };
        var analyses = new[]
        {
            Row("cats", A("cat", Upos.Noun, "Number=Plur"), A("cat", Upos.Verb, "Person=3|Tense=Pres")),
            Row("went", A("go", Upos.Verb, "Tense=Past")),
            Row("caf\u00E9", A("cafe", Upos.Noun)),
            Row("caf\u00E9s", A("caf\u00E9", Upos.Noun, "Number=Plur")),
            Row("go", A("go", Upos.Verb, "VerbForm=Inf")),
            Row("i", A("ie", Upos.Noun)),
            Row("run", A("run", Upos.Adj, "Degree=Pos"), A("run", Upos.Adv), A("ran", Upos.Noun), A("run", Upos.Noun, "Number=Sing"),
                A("runs", Upos.Noun, "Number=Plur"), A("run", Upos.Verb, "Tense=Pres"), A("run", Upos.Verb, "VerbForm=Inf")),
            Row("\u65E5\u672C", A("\u65E5\u672C", Upos.Propn, "Number=Sing"))
        };

        // Act: compile in any order of input, load, and look every spelling up
        var lexicon = Lexicon.Load(TinyModels.CreateLexicon(entries, compress, analyses.AsEnumerable().Reverse()));
        var scratch = new byte[lexicon.ScratchSize];

        // Assert: each spelling has exactly its analyses in the given order, the tags that they name, and the lookup of tags is unchanged
        foreach (var row in analyses)
        {
            var key = row.Key.ToCharArray();
            Assert.True(lexicon.TryFind(key, 0, key.Length, scratch, out var ordinal, out var tags), row.Key);
            Assert.Equal(row.Value, lexicon.GetAnalyses(ordinal));
            Assert.Equal(UposSet.Of(row.Value.Select(a => a.Tag).ToArray()), lexicon.AnalyzedTags(ordinal));
            Assert.Equal(entries.Single(e => e.Key == row.Key).Value, tags);
            Assert.True(lexicon.AnalyzedTags(ordinal).Union(tags).Equals(tags));
        }

        var none = "zzz".ToCharArray();
        Assert.True(lexicon.TryFind(none, 0, none.Length, scratch, out var zzz, out _));
        Assert.Empty(lexicon.GetAnalyses(zzz));
        Assert.Equal(analyses.Select(a => a.Key).OrderBy(k => k, Comparer<string>.Create(Utf8Order)), lexicon.EnumerateAnalyses().Select(a => a.Key));
        Assert.Equal(analyses.Sum(a => a.Value.Count), lexicon.AnalysisCount);
        Assert.Equal(analyses.Length, lexicon.AnalyzedSpellingCount);
        Assert.Equal(7, analyses.Max(a => a.Value.Count));
    }

    /// <summary>
    ///     Proves that the analyses of a spelling can be tested for a tag and a base word without a string, including a base
    ///     word that differs from the spelling by a cut and a suffix, and that a wrong tag, base word or length is refused.
    /// </summary>
    [Fact]
    public void Lexicon_HasAnalysis_ComparesTagAndComposedBaseWord()
    {
        // Arrange: the fixture lexicon with cats (noun and verb of cat), went (go) and caf\u00E9 (cafe)
        var lexicon = Lexicon.Load(AnalyzedFile());

        // Act / Assert: the analyses that exist are found, nothing else is
        Assert.True(Has(lexicon, "cats", Upos.Noun, "cat"));
        Assert.True(Has(lexicon, "cats", Upos.Verb, "cat"));
        Assert.False(Has(lexicon, "cats", Upos.Adj, "cat"));
        Assert.False(Has(lexicon, "cats", Upos.Noun, "cats"));
        Assert.False(Has(lexicon, "cats", Upos.Noun, "ca"));
        Assert.False(Has(lexicon, "cats", Upos.Noun, "dat"));
        Assert.True(Has(lexicon, "went", Upos.Verb, "go"));
        Assert.False(Has(lexicon, "went", Upos.Verb, "gone"));
        Assert.True(Has(lexicon, "caf\u00E9", Upos.Noun, "cafe"));
        Assert.False(Has(lexicon, "caf\u00E9", Upos.Noun, "caf\u00E9"));
    }

    /// <summary>
    ///     Proves that the analyses are written in a deterministic order and that the compressed and the uncompressed file
    ///     have the same uncompressed image.
    /// </summary>
    [Fact]
    public void LexiconWriter_Compile_Analyses_IsDeterministic()
    {
        // Arrange / Act: compile the fixture twice uncompressed and once compressed
        var first = AnalyzedFile();
        var second = AnalyzedFile();
        var packed = AnalyzedFile(true);

        // Assert: byte-identical, and the same image after inflation
        Assert.Equal(first, second);
        Assert.Equal(first, Lexicon.ToUncompressedImage(packed));
    }

    /// <summary>
    ///     Proves that the writer refuses analyses that the format cannot represent or that break its rules.
    /// </summary>
    /// <param name="scenario">The name of the broken analyses.</param>
    [Theory]
    [InlineData("unknown-spelling")]
    [InlineData("unsorted-spellings")]
    [InlineData("tag-not-of-spelling")]
    [InlineData("unordered-analyses")]
    [InlineData("duplicate-analysis")]
    [InlineData("empty-list")]
    [InlineData("too-many-analyses")]
    [InlineData("features-without-equals")]
    [InlineData("features-unordered")]
    [InlineData("features-trailing-bar")]
    [InlineData("empty-lemma")]
    [InlineData("lemma-with-space")]
    [InlineData("lemma-too-long")]
    [InlineData("lemma-not-nfc")]
    [InlineData("lemma-unpaired-surrogate")]
    [InlineData("lemma-upper-case")]
    [InlineData("lemma-upper-case-non-ascii")]
    [InlineData("lemma-typographic-apostrophe")]
    [InlineData("lemma-typographic-dash")]
    [InlineData("lemma-ellipsis")]
    public void LexiconWriter_Compile_InvalidAnalyses_Throws(string scenario)
    {
        // Arrange: a lexicon of cat (noun) and cats (noun, verb), and analyses that break one rule
        var entries = Entries("cat NOUN", "cats NOUN VERB");
        var analyses = new List<KeyValuePair<string, IReadOnlyList<Lexicon.Analysis>>>();
        switch (scenario)
        {
            case "unknown-spelling": analyses.Add(Row("dog", A("dog", Upos.Noun))); break;
            case "unsorted-spellings": analyses.Add(Row("cats", A("cat", Upos.Noun))); analyses.Add(Row("cat", A("cat", Upos.Noun))); break;
            case "tag-not-of-spelling": analyses.Add(Row("cat", A("cat", Upos.Verb))); break;
            case "unordered-analyses": analyses.Add(Row("cats", A("cat", Upos.Verb), A("cat", Upos.Noun))); break;
            case "duplicate-analysis": analyses.Add(Row("cats", A("cat", Upos.Noun), A("cat", Upos.Noun))); break;
            case "empty-list": analyses.Add(Row("cat")); break;
            case "too-many-analyses":
                analyses.Add(Row("cat", Enumerable.Range(0, 256).Select(i => A("w" + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture), Upos.Noun)).ToArray()));
                break;
            case "features-without-equals": analyses.Add(Row("cat", A("cat", Upos.Noun, "Plural"))); break;
            case "features-unordered": analyses.Add(Row("cat", A("cat", Upos.Noun, "Tense=Pres|Number=Sing"))); break;
            case "features-trailing-bar": analyses.Add(Row("cat", A("cat", Upos.Noun, "Number=Sing|"))); break;
            case "empty-lemma": analyses.Add(Row("cat", A(string.Empty, Upos.Noun))); break;
            case "lemma-with-space": analyses.Add(Row("cat", A("big cat", Upos.Noun))); break;
            case "lemma-too-long": analyses.Add(Row("cat", A(new string('x', 256), Upos.Noun))); break;
            case "lemma-not-nfc": analyses.Add(Row("cat", A("cafe\u0301", Upos.Noun))); break;
            case "lemma-unpaired-surrogate": analyses.Add(Row("cat", A("ca\uD800", Upos.Noun))); break;
            case "lemma-upper-case": analyses.Add(Row("cat", A("Cat", Upos.Noun))); break;
            case "lemma-upper-case-non-ascii": analyses.Add(Row("cat", A("caf\u00C9", Upos.Noun))); break;
            case "lemma-typographic-apostrophe": analyses.Add(Row("cat", A("cat\u2019s", Upos.Noun))); break;
            case "lemma-typographic-dash": analyses.Add(Row("cat", A("cat\u2013s", Upos.Noun))); break;
            case "lemma-ellipsis": analyses.Add(Row("cat", A("cat\u2026", Upos.Noun))); break;
            default: throw new InvalidOperationException(scenario);
        }

        // Act / Assert: the writer reports the problem as ArgumentException
        Assert.Throws<ArgumentException>(() => LexiconWriter.Compile(entries, analyses, false));
    }

    /// <summary>
    ///     Proves that the format holds at most 65,535 distinct lists of templates and that the writer refuses one more, while the
    ///     reader accepts the largest lexicon the writer can produce.
    /// </summary>
    [Fact]
    public void LexiconWriter_Compile_MoreThanMaximumLists_Throws()
    {
        // Arrange: spellings that each need a list of their own (a base word that shares nothing with the spelling), one list too many for the second
        List<KeyValuePair<string, UposSet>> EntriesOf(int count) =>
            Enumerable.Range(0, count).Select(i => new KeyValuePair<string, UposSet>("w" + i.ToString("D5", System.Globalization.CultureInfo.InvariantCulture), UposSet.Of(Upos.Noun))).ToList();
        List<KeyValuePair<string, IReadOnlyList<Lexicon.Analysis>>> AnalysesOf(int count) =>
            Enumerable.Range(0, count).Select(i => Row("w" + i.ToString("D5", System.Globalization.CultureInfo.InvariantCulture),
                A("l" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), Upos.Noun))).ToList();
        var largest = Lexicon.MaxLists - 1;

        // Act: the largest number of non-empty lists is accepted and loads; one more is refused with the limit in the message
        var file = LexiconWriter.Compile(EntriesOf(largest), AnalysesOf(largest), false);
        var loaded = Lexicon.Load(file);
        var ex = Assert.Throws<ArgumentException>(() => LexiconWriter.Compile(EntriesOf(largest + 1), AnalysesOf(largest + 1), false));

        // Assert: the list count is the maximum and the message names it
        Assert.Equal(Lexicon.MaxLists, loaded.ListCount);
        Assert.Equal(largest, loaded.AnalyzedSpellingCount);
        Assert.Contains("65535", ex.Message);
    }

    /// <summary>
    ///     Proves that the analyses table is parsed into rows that the writer accepts, and that its header counts are checked.
    /// </summary>
    [Fact]
    public void LexiconWriter_AnalysesFromTsv_ParsesRowsAndChecksHeaderCounts()
    {
        // Arrange: a lexicon, a table with the counts in a header comment, and the same table with a wrong count
        var entries = Entries("cat NOUN", "cats NOUN VERB", "the DET");
        var right = new[]
        {
            "# spellings=2, analyses=3", "cat\tcat NOUN Number=Sing", "cats\tcat NOUN Number=Plur\tcat VERB Person=3|Tense=Pres", string.Empty
        };
        var wrong = new[] { "# spellings=2, analyses=4", "cat\tcat NOUN Number=Sing", "cats\tcat NOUN Number=Plur\tcat VERB Person=3|Tense=Pres" };

        // Act: parse and compile the right table
        var rows = LexiconWriter.AnalysesFromTsv(right, entries);
        var lexicon = Lexicon.Load(LexiconWriter.Compile(entries, rows, false));

        // Assert: the rows are as written, the file has them, and the wrong header is rejected
        Assert.Equal(new[] { "cat", "cats" }, rows.Select(r => r.Key));
        Assert.Equal(new[] { A("cat", Upos.Noun, "Number=Plur"), A("cat", Upos.Verb, "Person=3|Tense=Pres") }, rows[1].Value);
        Assert.Equal(3, lexicon.AnalysisCount);
        Assert.Throws<InvalidDataException>(() => LexiconWriter.AnalysesFromTsv(wrong, entries));
    }

    /// <summary>
    ///     Proves that the table parser rejects rows that are unsorted, duplicated, not for a lexicon spelling or malformed.
    /// </summary>
    /// <param name="line1">The first row.</param>
    /// <param name="line2">The second row (may be empty).</param>
    [Theory]
    [InlineData("cats\tcat NOUN", "cat\tcat NOUN")]
    [InlineData("cat\tcat NOUN", "cat\tcat NOUN")]
    [InlineData("dog\tdog NOUN", "")]
    [InlineData("cat\tcat VERB", "")]
    [InlineData("cat", "")]
    [InlineData("cat\t", "")]
    [InlineData("cat\tcat", "")]
    [InlineData("cat\tcat NOUN Number=Sing extra", "")]
    [InlineData("cat\tcat noun", "")]
    [InlineData("cats\tcat VERB\tcat NOUN", "")]
    [InlineData("cat\tcat NOUN Number", "")]
    [InlineData("cat\tcat NOUN Tense=Pres|Number=Sing", "")]
    [InlineData("cat\tc\u0061\u0301t NOUN", "")]
    [InlineData("cat\tCat NOUN", "")]
    [InlineData("cat\tcat\u2019s NOUN", "")]
    public void LexiconWriter_AnalysesFromTsv_RejectsUnsortedUnknownOrInvalid(string line1, string line2)
    {
        // Arrange: a table with the offending rows
        var lines = new List<string> { "# comment", line1 };
        if (line2.Length > 0)
        {
            lines.Add(line2);
        }

        // Act / Assert: the parser reports the problem as InvalidDataException
        Assert.Throws<InvalidDataException>(() => LexiconWriter.AnalysesFromTsv(lines, Entries("cat NOUN", "cats NOUN VERB")));
    }

    /// <summary>
    ///     Proves that the embedded lexicon gives both analyses of <c>tests</c> in the documented order, and the rule-added
    ///     tag of <c>testing</c> has none.
    /// </summary>
    [Fact]
    public void Lexicon_GetAnalyses_EmbeddedTests_ReturnsNounAndVerbInOrder()
    {
        // Arrange: the embedded lexicon and the spellings
        var lexicon = EmbeddedModel.Lexicon;
        var scratch = new byte[lexicon.ScratchSize];
        var tests = "tests".ToCharArray();
        var testing = "testing".ToCharArray();

        // Act
        Assert.True(lexicon.TryFind(tests, 0, tests.Length, scratch, out var testsOrdinal, out _));
        Assert.True(lexicon.TryFind(testing, 0, testing.Length, scratch, out var testingOrdinal, out _));

        // Assert: tag order, then base word and features in UD notation; the gerund noun and verb, no analysis for the adjective
        Assert.Equal(new[] { A("test", Upos.Noun, "Number=Plur"), A("test", Upos.Verb, "Person=3|Tense=Pres") }, lexicon.GetAnalyses(testsOrdinal));
        Assert.Equal(new[] { A("testing", Upos.Noun, "Number=Sing"), A("test", Upos.Verb, "VerbForm=Ger") }, lexicon.GetAnalyses(testingOrdinal));
    }

    /// <summary>
    ///     Proves that a list index that names a list that does not exist is rejected even when the checksum is right.
    /// </summary>
    [Fact]
    public void Lexicon_Load_ListIndexOutOfRange_Throws()
    {
        // Arrange: the fixture with the list index of the first spelling set to the list count
        var file = AnalyzedFile();
        var layout = ReadLayout(file);
        var changed = Patched(file, f => BinaryFormat.WriteUInt16(f, layout.Index, (ushort)layout.Lists.Length));

        // Act / Assert: the missing list is named
        Assert.Contains("missing list", Assert.Throws<InvalidDataException>(() => Lexicon.Load(changed)).Message);
    }

    /// <summary>
    ///     Proves that a spelling whose list names a tag that the spelling does not have is rejected.
    /// </summary>
    [Fact]
    public void Lexicon_Load_ListTagsNotASubsetOfTheSpellingTags_Throws()
    {
        // Arrange: cat (noun only, the second spelling) pointed at the list of cats, which has a verb analysis
        var file = AnalyzedFile();
        var layout = ReadLayout(file);
        var changed = Patched(file, f => BinaryFormat.WriteUInt16(f, layout.Index + 2, 3));

        // Act / Assert: the join with the tag set is checked
        Assert.Contains("tag that the spelling does not have", Assert.Throws<InvalidDataException>(() => Lexicon.Load(changed)).Message);
    }

    /// <summary>
    ///     Proves that a cut that is longer than the spelling is rejected.
    /// </summary>
    [Fact]
    public void Lexicon_Load_CutLongerThanSpelling_Throws()
    {
        // Arrange: the template of cat (cut 0) changed to cut 4, and cat has three bytes
        var file = AnalyzedFile();
        var layout = ReadLayout(file);
        var changed = Patched(file, f => f[layout.Templates[2] + 2] = 4);

        // Act / Assert: the cut is checked against the spelling
        Assert.Contains("cuts 4 bytes from a spelling of 3", Assert.Throws<InvalidDataException>(() => Lexicon.Load(changed)).Message);
    }

    /// <summary>
    ///     Proves that a cut that falls inside a multi-byte character of the spelling is rejected.
    /// </summary>
    [Fact]
    public void Lexicon_Load_CutInsideACharacter_Throws()
    {
        // Arrange: the template of caf\u00E9 (cut 2, the whole \u00E9) changed to cut 1, which leaves half of the character
        var file = AnalyzedFile();
        var layout = ReadLayout(file);
        Assert.Equal(2, file[layout.Templates[0] + 2]);
        var changed = Patched(file, f => f[layout.Templates[0] + 2] = 1);

        // Act / Assert: the character boundary is checked
        Assert.Contains("inside a character", Assert.Throws<InvalidDataException>(() => Lexicon.Load(changed)).Message);
    }

    /// <summary>
    ///     Proves that a suffix that is not valid UTF-8 or that starts inside a character is rejected.
    /// </summary>
    /// <param name="value">The first byte of the suffix of the template of went.</param>
    [Theory]
    [InlineData(0xFF)]
    [InlineData(0x80)]
    public void Lexicon_Load_InvalidSuffix_Throws(int value)
    {
        // Arrange: the suffix go of the template of went with its first byte replaced
        var file = AnalyzedFile();
        var layout = ReadLayout(file);
        Assert.Equal((byte)'g', file[layout.Templates[4] + 4]);
        var changed = Patched(file, f => f[layout.Templates[4] + 4] = (byte)value);

        // Act / Assert: the suffix is checked
        Assert.Contains("suffix of template 4", Assert.Throws<InvalidDataException>(() => Lexicon.Load(changed)).Message);
    }

    /// <summary>
    ///     Proves that feature sets that are not strictly ascending are rejected.
    /// </summary>
    [Fact]
    public void Lexicon_Load_UnsortedFeatureSets_Throws()
    {
        // Arrange: Number=Plur and Number=Sing exchanged
        var file = AnalyzedFile();
        var layout = ReadLayout(file);
        var changed = Patched(file, f =>
        {
            Encoding.ASCII.GetBytes("Sing").CopyTo(f, layout.FeatureSets[1] + 8);
            Encoding.ASCII.GetBytes("Plur").CopyTo(f, layout.FeatureSets[2] + 8);
        });

        // Act / Assert: the order is checked
        Assert.Contains("out of order", Assert.Throws<InvalidDataException>(() => Lexicon.Load(changed)).Message);
    }

    /// <summary>
    ///     Proves that a feature set that is not a list of <c>Name=Value</c> pairs is rejected.
    /// </summary>
    [Fact]
    public void Lexicon_Load_MalformedFeatureSet_Throws()
    {
        // Arrange: the equals sign of Number=Plur replaced by a comma
        var file = AnalyzedFile();
        var layout = ReadLayout(file);
        var changed = Patched(file, f => f[layout.FeatureSets[1] + 1 + 6] = (byte)',');

        // Act / Assert: the syntax is checked
        Assert.Contains("malformed", Assert.Throws<InvalidDataException>(() => Lexicon.Load(changed)).Message);
    }

    /// <summary>
    ///     Proves that a list that names a template that does not exist is rejected.
    /// </summary>
    [Fact]
    public void Lexicon_Load_TemplateIndexOutOfRange_Throws()
    {
        // Arrange: the first template of the list of cats changed to the template count
        var file = AnalyzedFile();
        var layout = ReadLayout(file);
        var changed = Patched(file, f => f[layout.Lists[3] + 1] = (byte)layout.Templates.Length);

        // Act / Assert: the template index is checked
        Assert.Contains("missing or repeated template", Assert.Throws<InvalidDataException>(() => Lexicon.Load(changed)).Message);
    }

    /// <summary>
    ///     Proves that a list whose tags decrease is rejected.
    /// </summary>
    [Fact]
    public void Lexicon_Load_ListTagsDecrease_Throws()
    {
        // Arrange: the two templates of the list of cats exchanged (the verb template first)
        var file = AnalyzedFile();
        var layout = ReadLayout(file);
        var changed = Patched(file, f => (f[layout.Lists[3] + 1], f[layout.Lists[3] + 2]) = (f[layout.Lists[3] + 2], f[layout.Lists[3] + 1]));

        // Act / Assert: the order of the tags is checked
        Assert.Contains("tags decrease", Assert.Throws<InvalidDataException>(() => Lexicon.Load(changed)).Message);
    }

    /// <summary>
    ///     Proves that two analyses with one tag must be in the order of their base words: the order that the documented
    ///     (tag, base word, features) rule gives, not merely tags that do not decrease.
    /// </summary>
    [Fact]
    public void Lexicon_Load_SameTagAnalysesInWrongBaseWordOrder_Throws()
    {
        // Arrange: cats with the noun analyses cat and cats (in that order), and the two templates of its list exchanged
        var file = TinyModels.CreateLexicon(
            new[] { Entry("cats", Upos.Noun) },
            false,
            new[] { Row("cats", A("cat", Upos.Noun, "Number=Sing"), A("cats", Upos.Noun, "Number=Plur")) });
        var layout = ReadLayout(file);
        Assert.Equal(new[] { A("cat", Upos.Noun, "Number=Sing"), A("cats", Upos.Noun, "Number=Plur") }, Lexicon.Load(file).GetAnalyses(0));
        var changed = Patched(file, f => (f[layout.Lists[1] + 1], f[layout.Lists[1] + 2]) = (f[layout.Lists[1] + 2], f[layout.Lists[1] + 1]));

        // Act / Assert: the order of the base words is checked although the tags are equal
        Assert.Contains("strictly ascending by tag, base word and features", Assert.Throws<InvalidDataException>(() => Lexicon.Load(changed)).Message);
    }

    /// <summary>
    ///     Proves that two analyses with one tag and one base word must be in the order of their features.
    /// </summary>
    [Fact]
    public void Lexicon_Load_SameTagAnalysesInWrongFeatureOrder_Throws()
    {
        // Arrange: cat with the noun analyses Number=Plur and Number=Sing of the same base word, and the two templates of its list exchanged
        var file = TinyModels.CreateLexicon(
            new[] { Entry("cat", Upos.Noun) },
            false,
            new[] { Row("cat", A("cat", Upos.Noun, "Number=Plur"), A("cat", Upos.Noun, "Number=Sing")) });
        var layout = ReadLayout(file);
        Assert.Equal(2, Lexicon.Load(file).GetAnalyses(0).Count);
        var changed = Patched(file, f => (f[layout.Lists[1] + 1], f[layout.Lists[1] + 2]) = (f[layout.Lists[1] + 2], f[layout.Lists[1] + 1]));

        // Act / Assert: the order of the features is checked
        Assert.Contains("strictly ascending by tag, base word and features", Assert.Throws<InvalidDataException>(() => Lexicon.Load(changed)).Message);
    }

    /// <summary>
    ///     Proves that the order of a list is checked against each spelling that uses it, because the base words that the same two
    ///     templates compose can be in a different order for another spelling.
    /// </summary>
    [Fact]
    public void Lexicon_Load_ListInOrderForOneSpellingButNotForAnother_Throws()
    {
        // Arrange: ca and cc use the same two templates (cb with Number=Sing, and the spelling itself with Number=Plur), in the order of their
        // base words: for ca the spelling comes first (ca before cb), for cc it comes second (cb before cc), so they have two different lists
        var file = TinyModels.CreateLexicon(
            new[] { Entry("ca", Upos.Noun), Entry("cc", Upos.Noun) },
            false,
            new[]
            {
                Row("ca", A("ca", Upos.Noun, "Number=Plur"), A("cb", Upos.Noun, "Number=Sing")),
                Row("cc", A("cb", Upos.Noun, "Number=Sing"), A("cc", Upos.Noun, "Number=Plur"))
            });
        var layout = ReadLayout(file);
        var lexicon = Lexicon.Load(file);
        Assert.Equal(new[] { A("ca", Upos.Noun, "Number=Plur"), A("cb", Upos.Noun, "Number=Sing") }, lexicon.GetAnalyses(0));
        Assert.Equal(new[] { A("cb", Upos.Noun, "Number=Sing"), A("cc", Upos.Noun, "Number=Plur") }, lexicon.GetAnalyses(1));

        // Act: point ca at the list of cc, which is in order for cc but composes cb before ca for ca
        var changed = Patched(file, f => BinaryFormat.WriteUInt16(f, layout.Index, BinaryFormat.ReadUInt16(f, layout.Index + 2)));

        // Assert: the list is accepted for cc and refused for ca
        Assert.Contains("spelling 0 are not strictly ascending", Assert.Throws<InvalidDataException>(() => Lexicon.Load(changed)).Message);
    }

    /// <summary>
    ///     Proves that the loader accepts every analysis order that the writer produces and refuses the exchange of two neighboring analyses with one
    ///     tag, for generated lexicons whose base words mix ASCII, two-byte, three-byte and four-byte characters (the writer orders by UTF-8 bytes,
    ///     which is not the order of the UTF-16 characters of a surrogate pair against a character of the Basic Multilingual Plane).
    /// </summary>
    [Fact]
    public void Lexicon_Load_GeneratedAnalyses_AcceptsWriterOrderAndRefusesExchangedNeighbors()
    {
        // Arrange: a fixed seed and an alphabet with one character of each UTF-8 length, the last two ordered differently as UTF-16 and as UTF-8
        var random = new Random(20240607);
        var alphabet = new[] { "a", "b", "c", "\u00E9", "\u00DF", "\uFF5E", "\uD83D\uDE00" };
        var tags = new[] { Upos.Adj, Upos.Noun, Upos.Verb };
        var featureSets = new[] { string.Empty, "Number=Plur", "Number=Sing", "Tense=Past" };
        string Word(int minimum, int maximum) => string.Concat(Enumerable.Range(0, random.Next(minimum, maximum + 1)).Select(_ => alphabet[random.Next(alphabet.Length)]));
        var refused = 0;
        var accepted = 0;

        for (var round = 0; round < 40; round++)
        {
            // Act: compile spellings with 1 to 5 analyses each, in the writer's order, and load
            var spellings = Enumerable.Range(0, 30).Select(_ => Word(1, 4)).Distinct().ToList();
            var entries = spellings.Select(s => Entry(s, tags)).ToList();
            var rows = new List<KeyValuePair<string, IReadOnlyList<Lexicon.Analysis>>>();
            foreach (var spelling in spellings)
            {
                var chosen = new List<Lexicon.Analysis>();
                for (var k = random.Next(1, 6); k > 0; k--)
                {
                    var prefix = new StringInfo(spelling).SubstringByTextElements(0, random.Next(0, new StringInfo(spelling).LengthInTextElements + 1));
                    var lemma = prefix + Word(prefix.Length == 0 ? 1 : 0, 2);
                    chosen.Add(A(lemma, tags[random.Next(tags.Length)], featureSets[random.Next(featureSets.Length)]));
                }

                chosen.Sort(LexiconWriter.CompareAnalyses);
                rows.Add(Row(spelling, chosen.Where((a, i) => i == 0 || LexiconWriter.CompareAnalyses(chosen[i - 1], a) != 0).ToArray()));
            }

            var file = TinyModels.CreateLexicon(entries, false, rows);
            var lexicon = Lexicon.Load(file);
            var layout = ReadLayout(file);
            accepted++;

            // Assert: every spelling reads back with its analyses in the order that was written
            foreach (var row in rows)
            {
                var key = row.Key.ToCharArray();
                Assert.True(lexicon.TryFind(key, 0, key.Length, new byte[lexicon.ScratchSize], out var ordinal, out _), row.Key);
                Assert.Equal(row.Value, lexicon.GetAnalyses(ordinal));
            }

            // Act / Assert: exchanging two neighbors of one tag whose indices have the same length in one list makes the file refused
            for (var l = 1; l < layout.Lists.Length; l++)
            {
                var position = layout.Lists[l];
                var count = BinaryFormat.ReadVarintInt32(file, ref position, file.Length);
                var items = new List<(int Position, int Length, int Template)>();
                for (var k = 0; k < count; k++)
                {
                    var start = position;
                    var template = BinaryFormat.ReadVarintInt32(file, ref position, file.Length);
                    items.Add((start, position - start, template));
                }

                var pair = Enumerable.Range(1, count - 1).FirstOrDefault(
                    k => file[layout.Templates[items[k - 1].Template]] == file[layout.Templates[items[k].Template]] && items[k - 1].Length == items[k].Length);
                if (pair == 0)
                {
                    continue;
                }

                var changed = Patched(file, f =>
                {
                    for (var b = 0; b < items[pair].Length; b++)
                    {
                        (f[items[pair - 1].Position + b], f[items[pair].Position + b]) = (f[items[pair].Position + b], f[items[pair - 1].Position + b]);
                    }
                });
                Assert.Contains("not strictly ascending", Assert.Throws<InvalidDataException>(() => Lexicon.Load(changed)).Message);
                refused++;
            }
        }

        Assert.Equal(40, accepted);
        Assert.True(refused > 100, $"only {refused} exchanges were tested");
    }

    /// <summary>
    ///     Proves that a base word that a template composes from a spelling must be a fixed point of the fold of the tagger, as the writer
    ///     requires: a valid-checksum file whose suffix is patched to hold an upper-case letter, an upper-case letter of two bytes or a typographic
    ///     apostrophe is refused, so that every base word of a loaded lexicon is one that <c>IsFormOf</c> can match.
    /// </summary>
    /// <param name="first">The first byte of the patched suffix (it replaces <c>g</c>).</param>
    /// <param name="second">The second byte of the patched suffix (it replaces <c>o</c>).</param>
    [Theory]
    [InlineData(0x47, 0x6F)]
    [InlineData(0x67, 0x4F)]
    [InlineData(0xC3, 0x96)]
    [InlineData(0xC3, 0x80)]
    [InlineData(0xCA, 0xBC)]
    public void Lexicon_Load_ComposedBaseWordNotFolded_Throws(int first, int second)
    {
        // Arrange: the base word go of went (template 4: tag, feature set, cut 4, suffix length 2, suffix go) with the suffix patched
        var file = AnalyzedFile();
        var layout = ReadLayout(file);
        Assert.Equal(new byte[] { 4, 2, (byte)'g', (byte)'o' }, file.Skip(layout.Templates[4] + 2).Take(4).ToArray());
        var changed = Patched(file, f =>
        {
            f[layout.Templates[4] + 4] = (byte)first;
            f[layout.Templates[4] + 5] = (byte)second;
        });

        // Act / Assert: the base word is not lower case or not free of typographic marks (the unpatched file loads)
        Assert.Equal("go", Lexicon.Load(file).GetAnalyses(3)[0].Lemma);
        Assert.Contains("lower case", Assert.Throws<InvalidDataException>(() => Lexicon.Load(changed)).Message);
    }

    /// <summary>
    ///     Proves that the same patch with a lower-case letter of two bytes is accepted, and that an upper-case letter in the part that a
    ///     template keeps from the spelling is refused.
    /// </summary>
    [Fact]
    public void Lexicon_Load_LowerCaseTwoByteSuffixIsAcceptedAndKeptUpperCaseIsRefused()
    {
        // Arrange: went with the suffix patched to the lower-case letter U+00F6; and a lexicon with the spelling Cats (the writer does not
        // check spellings of the entries) whose base word cats shares nothing with it, patched to cut nothing so that the base word keeps Cats
        var file = AnalyzedFile();
        var layout = ReadLayout(file);
        var lower = Patched(file, f =>
        {
            f[layout.Templates[4] + 4] = 0xC3;
            f[layout.Templates[4] + 5] = 0xB6;
        });
        var capital = TinyModels.CreateLexicon(new[] { Entry("Cats", Upos.Noun) }, false, new[] { Row("Cats", A("cats", Upos.Noun)) });
        var capitalLayout = ReadLayout(capital);
        Assert.Equal(new byte[] { 4, 4, (byte)'c', (byte)'a', (byte)'t', (byte)'s' }, capital.Skip(capitalLayout.Templates[0] + 2).Take(6).ToArray());
        var kept = Patched(capital, f => f[capitalLayout.Templates[0] + 2] = 0);

        // Act / Assert: the patched suffix is the base word of went, and the base word that keeps an upper-case spelling is refused
        Assert.True(Has(Lexicon.Load(lower), "went", Upos.Verb, "\u00F6"));
        Assert.Contains("lower case", Assert.Throws<InvalidDataException>(() => Lexicon.Load(kept)).Message);
    }

    /// <summary>
    ///     Proves that a lexicon that the writer compiles from lower-case base words is accepted by the loader and answers the form
    ///     question for them, whatever the case of the question.
    /// </summary>
    [Fact]
    public void Lexicon_Load_LowerCaseBaseWordsOfTwoBytes_AreAcceptedAndKeepTheirText()
    {
        // Arrange: spellings whose base words have two-byte letters (below U+0300) in the kept part and in the suffix
        var entries = new[] { Entry("caf\u00E9s", Upos.Noun), Entry("se\u00F1ora", Upos.Noun), Entry("\u0111", Upos.Noun) };
        var rows = new[]
        {
            Row("caf\u00E9s", A("caf\u00E9", Upos.Noun)),
            Row("se\u00F1ora", A("se\u00F1or", Upos.Noun)),
            Row("\u0111", A("\u0111\u00E9", Upos.Noun)),
        };

        // Act: compile and load (the loader tests every base word)
        var lexicon = Lexicon.Load(TinyModels.CreateLexicon(entries, false, rows));

        // Assert: every analysis is there with its own text
        Assert.True(Has(lexicon, "caf\u00E9s", Upos.Noun, "caf\u00E9"));
        Assert.True(Has(lexicon, "se\u00F1ora", Upos.Noun, "se\u00F1or"));
        Assert.True(Has(lexicon, "\u0111", Upos.Noun, "\u0111\u00E9"));
        Assert.Equal(3, lexicon.AnalysisCount);
    }

    /// <summary>
    ///     Proves that a capital letter is found wherever it stands in the part that a template keeps from a long spelling, at the edges of
    ///     the eight-byte steps of the scan too: the patched file keeps the whole spelling, and the unpatched file loads.
    /// </summary>
    /// <param name="position">The index of the capital letter in the spelling.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(24)]
    [InlineData(25)]
    public void Lexicon_Load_UpperCaseLetterInAKeptPrefix_Throws(int position)
    {
        // Arrange: a spelling with one capital whose base word is the lower-case alphabet (it keeps the letters before the capital)
        const string lower = "abcdefghijklmnopqrstuvwxyz";
        var spelling = lower.Substring(0, position) + char.ToUpperInvariant(lower[position]) + lower.Substring(position + 1);
        var file = TinyModels.CreateLexicon(new[] { Entry(spelling, Upos.Noun) }, false, new[] { Row(spelling, A(lower, Upos.Noun)) });
        var layout = ReadLayout(file);
        Assert.Equal(26 - position, file[layout.Templates[0] + 2]);
        var changed = Patched(file, f => f[layout.Templates[0] + 2] = 0);

        // Act / Assert: the file as written loads; with the cut changed to 0 the base word keeps the capital and is refused
        Assert.True(Has(Lexicon.Load(file), spelling, Upos.Noun, lower));
        Assert.Contains("lower case", Assert.Throws<InvalidDataException>(() => Lexicon.Load(changed)).Message);
    }

    /// <summary>
    ///     Proves that the ASCII characters next to the capitals (<c>@</c>, <c>[</c>, the grave accent, the brace, DEL) and non-ASCII
    ///     lower-case characters in a long base word that equals its spelling are accepted, wherever they stand.
    /// </summary>
    [Fact]
    public void Lexicon_Load_CharactersNextToCapitalsInALongBaseWord_AreAccepted()
    {
        // Arrange: base words that are their spellings and have the characters just outside A to Z, in the first and the later steps of the scan
        var texts = new[]
        {
            "a@[`{b\u007Fcdefghijklmnopqrstuvwxyz@[`{",
            "abcdefghijk\u00E9mnopqrstuvwxyz\u00F1",
            "\u00F1abcdefghijklmnopqrstuvwxyz",
            "abcdefghi@jklmnopqrst[uvwxyz",
        };
        var entries = texts.Select(t => Entry(t, Upos.Noun)).ToArray();
        var rows = texts.Select(t => Row(t, A(t, Upos.Noun))).ToArray();

        // Act: compile and load
        var lexicon = Lexicon.Load(TinyModels.CreateLexicon(entries, false, rows));

        // Assert: every base word is there as written
        Assert.Equal(texts.Length, lexicon.AnalysisCount);
        Assert.All(texts, t => Assert.True(Has(lexicon, t, Upos.Noun, t), t));
    }

    /// <summary>
    ///     Proves that a base word that a template composes from a spelling must be in Unicode normalization form C, as the writer and
    ///     the TSV parser require: a suffix that is a combining mark after a spelling that ends in a letter that it composes with is refused.
    /// </summary>
    [Fact]
    public void Lexicon_Load_ComposedBaseWordNotNormalized_Throws()
    {
        // Arrange: cafe with the base word caf\u00E9 (keep caf, cut 1, suffix C3 A9), then the template changed to keep cafe and append U+0301 (CC 81)
        var file = TinyModels.CreateLexicon(new[] { Entry("cafe", Upos.Noun) }, false, new[] { Row("cafe", A("caf\u00E9", Upos.Noun)) });
        var layout = ReadLayout(file);
        Assert.Equal("caf\u00E9", Lexicon.Load(file).GetAnalyses(0)[0].Lemma);
        Assert.Equal(new byte[] { 0, 1, 2, 0xC3, 0xA9 }, file.Skip(layout.Templates[0] + 1).Take(5).ToArray());
        var changed = Patched(file, f =>
        {
            f[layout.Templates[0] + 2] = 0;
            f[layout.Templates[0] + 4] = 0xCC;
            f[layout.Templates[0] + 5] = 0x81;
        });

        // Act / Assert: the base word cafe followed by U+0301 is not NFC (it composes to caf\u00E9)
        Assert.Contains("normalization form C", Assert.Throws<InvalidDataException>(() => Lexicon.Load(changed)).Message);
    }

    /// <summary>
    ///     Proves the premise of the fast path of the normalization check of the loader: a text made only of characters below U+0300 is
    ///     in normalization form C (every such character is stable and no pair of them composes), and U+0300 is the first combining mark.
    /// </summary>
    [Fact]
    public void Lexicon_Load_CharactersBelowU0300_AreStableUnderNormalizationFormC()
    {
        // Arrange / Act: test every character below U+0300 and every pair of them
        var unstable = new List<string>();
        for (var first = 0; first < 0x300; first++)
        {
            for (var second = 0; second < 0x300; second++)
            {
                var text = new string(new[] { (char)first, (char)second });
                if (!text.IsNormalized(NormalizationForm.FormC))
                {
                    unstable.Add($"U+{first:X4} U+{second:X4}");
                }
            }
        }

        // Assert: none is unstable, and the first character that is is the combining grave accent (a followed by it composes)
        Assert.Empty(unstable);
        Assert.False("a\u0300".IsNormalized(NormalizationForm.FormC));
    }

    /// <summary>
    ///     Proves that list 0 must be the empty list.
    /// </summary>
    [Fact]
    public void Lexicon_Load_ListZeroNotEmpty_Throws()
    {
        // Arrange: the count of list 0 changed to 1
        var file = AnalyzedFile();
        var layout = ReadLayout(file);
        var changed = Patched(file, f => f[layout.Lists[0]] = 1);

        // Act / Assert: list 0 is named
        Assert.Contains("list 0", Assert.Throws<InvalidDataException>(() => Lexicon.Load(changed)).Message);
    }

    /// <summary>
    ///     Proves that two equal templates are rejected.
    /// </summary>
    [Fact]
    public void Lexicon_Load_RepeatedTemplate_Throws()
    {
        // Arrange: the bytes of template 1 copied over template 2 (both take four bytes)
        var file = AnalyzedFile();
        var layout = ReadLayout(file);
        var changed = Patched(file, f => Array.Copy(f, layout.Templates[1], f, layout.Templates[2], 4));

        // Act / Assert: the repeat is named
        Assert.Contains("repeats an earlier template", Assert.Throws<InvalidDataException>(() => Lexicon.Load(changed)).Message);
    }

    /// <summary>
    ///     Proves that regions that do not fill the payload exactly are rejected, even when lengths and checksum are consistent.
    /// </summary>
    [Fact]
    public void Lexicon_Load_AnalysesRegionsDoNotFillThePayload_Throws()
    {
        // Arrange: one extra byte appended to the payload, with the stored length, the payload length and the checksum adjusted
        var file = AnalyzedFile();
        var changed = file.Concat(new byte[] { 0 }).ToArray();
        BinaryFormat.WriteUInt32(changed, 24, (uint)(changed.Length - Lexicon.HeaderLength));
        BinaryFormat.WriteUInt32(changed, 28, (uint)(changed.Length - Lexicon.HeaderLength));
        RecomputeChecksum(changed);

        // Act / Assert: the section must end exactly where the list index begins
        Assert.Contains("fill the payload exactly", Assert.Throws<InvalidDataException>(() => Lexicon.Load(changed)).Message);
    }

    /// <summary>
    ///     Proves that an analysis count in the header that differs from the lists of the spellings is rejected.
    /// </summary>
    [Fact]
    public void Lexicon_Load_AnalysisCountMismatch_Throws()
    {
        // Arrange: the declared number of analyses raised by one
        var file = AnalyzedFile();
        var changed = Patched(file, f => BinaryFormat.WriteUInt32(f, 52, BinaryFormat.ReadUInt32(f, 52) + 1));

        // Act / Assert: the count is checked against the data
        Assert.Contains("analyses", Assert.Throws<InvalidDataException>(() => Lexicon.Load(changed)).Message);
    }

    /// <summary>
    ///     Proves that a template count that cannot fit the payload is rejected before anything is allocated for it.
    /// </summary>
    [Fact]
    public void Lexicon_Load_TemplateCountLargerThanThePayload_Throws()
    {
        // Arrange: the declared number of templates set to the largest value the field allows
        var file = AnalyzedFile();
        var changed = Patched(file, f => BinaryFormat.WriteUInt32(f, 44, int.MaxValue));

        // Act / Assert: an InvalidDataException, not an out-of-memory error
        Assert.Contains("truncated", Assert.Throws<InvalidDataException>(() => Lexicon.Load(changed)).Message);
    }

    /// <summary>
    ///     Proves that the header fields of the analyses section are range checked.
    /// </summary>
    /// <param name="offset">The offset of the field.</param>
    /// <param name="value">The invalid value.</param>
    [Theory]
    [InlineData(40, 0)]
    [InlineData(48, 0)]
    [InlineData(48, 65536)]
    [InlineData(42, 1)]
    public void Lexicon_Load_InvalidAnalysesHeaderField_Throws(int offset, int value)
    {
        // Arrange: the fixture with one header field changed
        var file = AnalyzedFile();
        var changed = Patched(file, f =>
        {
            if (offset == 48)
            {
                BinaryFormat.WriteUInt32(f, offset, (uint)value);
            }
            else
            {
                BinaryFormat.WriteUInt16(f, offset, (ushort)value);
            }
        });

        // Act / Assert: the header is refused
        Assert.Throws<InvalidDataException>(() => Lexicon.Load(changed));
    }

    /// <summary>
    ///     Creates an entry of the fixture lexicons.
    /// </summary>
    private static KeyValuePair<string, UposSet> Entry(string spelling, params Upos[] tags) => new(spelling, UposSet.Of(tags));

    /// <summary>
    ///     Creates an analysis.
    /// </summary>
    private static Lexicon.Analysis A(string lemma, Upos tag, string features = "") => new(lemma, tag, features);

    /// <summary>
    ///     Creates the analyses of one spelling.
    /// </summary>
    private static KeyValuePair<string, IReadOnlyList<Lexicon.Analysis>> Row(string spelling, params Lexicon.Analysis[] analyses) => new(spelling, analyses);

    /// <summary>
    ///     Builds the fixture lexicon of the corruption tests: caf\u00E9, cat, cats and went, whose analyses have five templates and four lists.
    /// </summary>
    /// <remarks>
    ///     Template 0 is caf\u00E9 to cafe (cut 2), template 1 cats to cat (noun, cut 1), template 2 cat to cat (cut 0), template 3 cats to cat
    ///     (verb, cut 1) and template 4 went to go (cut 4, suffix go); the lists are 0 (empty), then one list per spelling in the order of the spellings.
    /// </remarks>
    private static byte[] AnalyzedFile(bool compress = false) =>
        TinyModels.CreateLexicon(
            new[] { Entry("cat", Upos.Noun), Entry("cats", Upos.Noun, Upos.Verb), Entry("caf\u00E9", Upos.Noun), Entry("went", Upos.Verb) },
            compress,
            new[]
            {
                Row("caf\u00E9", A("cafe", Upos.Noun)),
                Row("cat", A("cat", Upos.Noun, "Number=Sing")),
                Row("cats", A("cat", Upos.Noun, "Number=Plur"), A("cat", Upos.Verb, "Person=3|Tense=Pres")),
                Row("went", A("go", Upos.Verb, "Tense=Past"))
            });

    /// <summary>
    ///     Asks a lexicon whether a spelling has an analysis with a tag and a base word.
    /// </summary>
    private static bool Has(Lexicon lexicon, string spelling, Upos tag, string lemma)
    {
        var chars = spelling.ToCharArray();
        Assert.True(lexicon.TryFind(chars, 0, chars.Length, new byte[lexicon.ScratchSize], out var ordinal, out _), spelling);
        var spellingBytes = Encoding.UTF8.GetBytes(spelling);
        var lemmaBytes = Encoding.UTF8.GetBytes(lemma);
        return lexicon.HasAnalysis(ordinal, tag, spellingBytes, 0, spellingBytes.Length, lemmaBytes, 0, lemmaBytes.Length);
    }

    /// <summary>
    ///     Recomputes the checksum of an uncompressed file after its payload was changed.
    /// </summary>
    private static void RecomputeChecksum(byte[] file) =>
        BinaryFormat.WriteUInt32(file, 32, BinaryFormat.Fnv1a(file, Lexicon.HeaderLength, file.Length - Lexicon.HeaderLength));

    /// <summary>
    ///     Returns a changed copy of an uncompressed file with its checksum recomputed.
    /// </summary>
    private static byte[] Patched(byte[] file, Action<byte[]> change)
    {
        var copy = (byte[])file.Clone();
        change(copy);
        RecomputeChecksum(copy);
        return copy;
    }

    /// <summary>
    ///     The positions of the regions of the analyses section of an uncompressed file.
    /// </summary>
    /// <param name="FeatureSets">The position of every feature set.</param>
    /// <param name="Templates">The position of every template.</param>
    /// <param name="Lists">The position of every list.</param>
    /// <param name="Index">The position of the list index.</param>
    private sealed record Layout(int[] FeatureSets, int[] Templates, int[] Lists, int Index);

    /// <summary>
    ///     Walks an uncompressed file (the data region and the analyses section) and reports where the parts of the section are.
    /// </summary>
    private static Layout ReadLayout(byte[] file)
    {
        static int Varint(byte[] data, ref int position) => BinaryFormat.ReadVarintInt32(data, ref position, data.Length);

        // Reads a length and skips that many bytes (the length is read first: a compound assignment would use the old position)
        static void SkipCounted(byte[] data, ref int position)
        {
            var length = Varint(data, ref position);
            position += length;
        }

        // The data region follows the palette and the block index; skip every spelling with its palette index
        var spellings = BinaryFormat.ReadInt32Field(file, 12, "spelling count");
        var blockSize = 1 << file[9];
        var position = Lexicon.HeaderLength + (BinaryFormat.ReadUInt16(file, 10) * 4) + (BinaryFormat.ReadInt32Field(file, 20, "block count") * 4);
        for (var i = 0; i < spellings; i++)
        {
            if (i % blockSize == 0)
            {
                SkipCounted(file, ref position);
            }
            else
            {
                Varint(file, ref position);
                SkipCounted(file, ref position);
            }

            Varint(file, ref position);
        }

        // Feature sets, templates and lists in the order of the format
        var featureSets = new int[BinaryFormat.ReadUInt16(file, 40)];
        for (var i = 0; i < featureSets.Length; i++)
        {
            featureSets[i] = position;
            SkipCounted(file, ref position);
        }

        var templates = new int[BinaryFormat.ReadInt32Field(file, 44, "template count")];
        for (var i = 0; i < templates.Length; i++)
        {
            templates[i] = position++;
            Varint(file, ref position);
            Varint(file, ref position);
            SkipCounted(file, ref position);
        }

        var lists = new int[BinaryFormat.ReadInt32Field(file, 48, "list count")];
        for (var i = 0; i < lists.Length; i++)
        {
            lists[i] = position;
            var count = Varint(file, ref position);
            for (var k = 0; k < count; k++)
            {
                Varint(file, ref position);
            }
        }

        Assert.Equal(file.Length - (2 * spellings), position);
        return new Layout(featureSets, templates, lists, position);
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
