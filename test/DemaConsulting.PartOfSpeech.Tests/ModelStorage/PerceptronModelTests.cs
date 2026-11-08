namespace DemaConsulting.PartOfSpeech.Tests.ModelStorage;

/// <summary>
///     Unit tests of <see cref="PerceptronModel"/> and <see cref="PerceptronModelWriter"/>.
/// </summary>
public class PerceptronModelTests
{
    /// <summary>
    ///     The feature set version that the tests write.
    /// </summary>
    private const int FeatureSet = FeatureExtractor.FeatureSetVersion;

    /// <summary>
    ///     Creates the rows of a small model: bucket 10 with tags 2 and 4, bucket 20 with tag 1, and the first and last bucket.
    /// </summary>
    private static List<KeyValuePair<int, short[]>> SmallRows(int hashBits = 18)
    {
        short[] Row(params (int Tag, short Weight)[] weights)
        {
            var row = new short[17];
            foreach (var (tag, weight) in weights)
            {
                row[tag] = weight;
            }

            return row;
        }

        return new List<KeyValuePair<int, short[]>>
        {
            new(10, Row((2, 7), (4, -3))),
            new(20, Row((1, 100))),
            new(0, Row((16, 5))),
            new((1 << hashBits) - 1, Row((0, 1), (16, -1))),
        };
    }

    /// <summary>
    ///     Writes the small model.
    /// </summary>
    private static byte[] SmallFile(bool compress = false) =>
        PerceptronModelWriter.Compile(18, 5.4f, 0.25f, FeatureSet, SmallRows(), compress);

    /// <summary>
    ///     Recomputes the checksum of an uncompressed file after its payload was changed.
    /// </summary>
    private static byte[] RecomputeChecksum(byte[] file)
    {
        BinaryFormat.WriteUInt32(file, 36, BinaryFormat.Fnv1a(file, PerceptronModel.HeaderLength, file.Length - PerceptronModel.HeaderLength));
        return file;
    }

    /// <summary>
    ///     Writes a compressed model of 3000 rows, whose payload is large enough for several blocks of Deflate data.
    /// </summary>
    private static byte[] LargeCompressedFile()
    {
        var rows = new List<KeyValuePair<int, short[]>>();
        var random = new Random(5);
        for (var bucket = 0; bucket < 3000; bucket++)
        {
            var row = new short[17];
            row[random.Next(17)] = (short)random.Next(1, 3000);
            rows.Add(new KeyValuePair<int, short[]>(bucket * 7, row));
        }

        return PerceptronModelWriter.Compile(18, 1f, 1f, FeatureSet, rows, true);
    }

    /// <summary>
    ///     Returns a copy of the stored bytes (the bytes after the header) of a file.
    /// </summary>
    private static byte[] StoredRegion(byte[] file) => file.Skip(PerceptronModel.HeaderLength).ToArray();

    /// <summary>
    ///     Returns a copy of a file whose stored region was replaced, with the stored length adjusted and, on request,
    ///     with the checksum recomputed over the new region.
    /// </summary>
    private static byte[] WithStoredRegion(byte[] file, byte[] stored, bool recomputeChecksum)
    {
        var changed = file.Take(PerceptronModel.HeaderLength).Concat(stored).ToArray();
        BinaryFormat.WriteUInt32(changed, 32, (uint)stored.Length);
        if (recomputeChecksum)
        {
            BinaryFormat.WriteUInt32(changed, 36, BinaryFormat.Fnv1a(stored, 0, stored.Length));
        }

        return changed;
    }

    /// <summary>
    ///     Proves that a written model loads with the header values and the right sizes.
    /// </summary>
    [Fact]
    public void PerceptronModel_Load_RoundTripsWriterOutput()
    {
        // Arrange: the small model written through a stream
        using var stream = new MemoryStream();
        PerceptronModelWriter.Write(stream, 18, 5.4f, 0.25f, FeatureSet, SmallRows(), false);
        stream.Position = 0;

        // Act: load it
        var model = PerceptronModel.Load(stream, FeatureSet);

        // Assert: counts and header values
        Assert.Equal(4, model.RowCount);
        Assert.Equal(6, model.WeightCount);
        Assert.Equal(18, model.HashBits);
        Assert.Equal(0.25f, model.Scale);
    }

    /// <summary>
    ///     Proves that the Deflate and the raw form give the same model.
    /// </summary>
    [Fact]
    public void PerceptronModel_Load_Compressed_And_Uncompressed_Equal()
    {
        // Arrange / Act: load both forms
        var plain = PerceptronModel.Load(SmallFile(false), FeatureSet);
        var packed = PerceptronModel.Load(SmallFile(true), FeatureSet);

        // Assert: identical scores for every bucket of the space that has a row
        foreach (var bucket in new[] { 0, 10, 20, (1 << 18) - 1, 11 })
        {
            var a = new int[17];
            var b = new int[17];
            plain.AddScores(bucket, a);
            packed.AddScores(bucket, b);
            Assert.Equal(a, b);
        }
    }

    /// <summary>
    ///     Proves that the uncompressed image of a compressed file equals the file written uncompressed, so a staleness check can compare images.
    /// </summary>
    [Fact]
    public void PerceptronModel_ToUncompressedImage_CompressedAndPlain_SameImageAsPlainFile()
    {
        // Arrange: the same model written plain, compressed and compressed with a large payload
        var plain = SmallFile(false);
        var packed = SmallFile(true);
        var large = LargeCompressedFile();

        // Act
        var fromPacked = PerceptronModel.ToUncompressedImage(packed);
        var fromPlain = PerceptronModel.ToUncompressedImage(plain);
        var fromLarge = PerceptronModel.ToUncompressedImage(large);

        // Assert: the image is the plain file itself, loads, and is also the plain form of the large file
        Assert.Equal(plain, fromPacked);
        Assert.Equal(plain, fromPlain);
        Assert.Equal(0, BinaryFormat.ReadUInt16(fromLarge, 6) & BinaryFormat.FlagDeflate);
        Assert.Equal(3000, PerceptronModel.Load(fromLarge, FeatureSet).RowCount);
    }

    /// <summary>
    ///     Proves that an invalid file or a file of another feature set has no image.
    /// </summary>
    [Fact]
    public void PerceptronModel_ToUncompressedImage_InvalidFile_Throws()
    {
        // Arrange: a damaged copy and a file of another feature set
        var damaged = SmallFile(false);
        damaged[damaged.Length - 3] ^= 0x55;
        var otherSet = PerceptronModelWriter.Compile(18, 1f, 1f, FeatureSet + 1, SmallRows(), false);

        // Act / Assert
        Assert.Throws<InvalidDataException>(() => PerceptronModel.ToUncompressedImage(damaged));
        Assert.Throws<InvalidDataException>(() => PerceptronModel.ToUncompressedImage(otherSet));
        Assert.Throws<ArgumentNullException>(() => PerceptronModel.ToUncompressedImage(null!));
    }

    /// <summary>
    ///     Proves that the weights of a row are added to the matching tags and accumulate over buckets.
    /// </summary>
    [Fact]
    public void PerceptronModel_AddScores_SumsRowWeights()
    {
        // Arrange: the small model and a score vector that already holds a value
        var model = PerceptronModel.Load(SmallFile(), FeatureSet);
        var scores = new int[17];
        scores[2] = 1;

        // Act: add two buckets
        model.AddScores(10, scores);
        model.AddScores(20, scores);

        // Assert: tag 2 got +7, tag 4 got -3, tag 1 got +100, everything else is untouched
        var expected = new int[17];
        expected[2] = 8;
        expected[4] = -3;
        expected[1] = 100;
        Assert.Equal(expected, scores);
    }

    /// <summary>
    ///     Proves that a bucket without a row adds nothing, including buckets in the same 64-bit word as an occupied one.
    /// </summary>
    [Fact]
    public void PerceptronModel_AddScores_EmptyBucket_AddsNothing()
    {
        // Arrange: the small model
        var model = PerceptronModel.Load(SmallFile(), FeatureSet);
        var scores = new int[17];

        // Act: neighbors of occupied buckets and a far bucket
        foreach (var bucket in new[] { 9, 11, 19, 21, 63, 64, 1000, 200000 })
        {
            model.AddScores(bucket, scores);
        }

        // Assert: still all zero
        Assert.All(scores, s => Assert.Equal(0, s));
    }

    /// <summary>
    ///     Proves that the first and the last bucket of the space are found.
    /// </summary>
    [Fact]
    public void PerceptronModel_AddScores_FirstAndLastBucket_AreFound()
    {
        // Arrange: the small model with rows at bucket 0 and at the last bucket
        var model = PerceptronModel.Load(SmallFile(), FeatureSet);
        var first = new int[17];
        var last = new int[17];

        // Act: read both
        model.AddScores(0, first);
        model.AddScores((1 << 18) - 1, last);

        // Assert: the stored weights
        Assert.Equal(5, first[16]);
        Assert.Equal(1, last[0]);
        Assert.Equal(-1, last[16]);
    }

    /// <summary>
    ///     Proves that the temperature in the header is exposed unchanged.
    /// </summary>
    [Fact]
    public void PerceptronModel_Temperature_IsReadFromHeader()
    {
        // Arrange / Act: load the small model
        var model = PerceptronModel.Load(SmallFile(), FeatureSet);

        // Assert: the temperature written
        Assert.Equal(5.4f, model.Temperature);
    }

    /// <summary>
    ///     Proves that another magic is rejected.
    /// </summary>
    [Fact]
    public void PerceptronModel_Load_BadMagic_Throws()
    {
        // Arrange: a lexicon file offered as weights
        var file = LexiconWriter.Compile(new[] { new KeyValuePair<string, UposSet>("a", UposSet.Of(Upos.Noun)) }, false);

        // Act / Assert: not a weights file
        var ex = Assert.Throws<InvalidDataException>(() => PerceptronModel.Load(file, FeatureSet));
        Assert.Contains("POSW", ex.Message);
    }

    /// <summary>
    ///     Proves that another format version is rejected with its number.
    /// </summary>
    [Fact]
    public void PerceptronModel_Load_UnsupportedVersion_Throws()
    {
        // Arrange: the small file with version 9
        var file = SmallFile();
        BinaryFormat.WriteUInt16(file, 4, 9);

        // Act / Assert: the version is named
        var ex = Assert.Throws<InvalidDataException>(() => PerceptronModel.Load(file, FeatureSet));
        Assert.Contains("version 9", ex.Message);
    }

    /// <summary>
    ///     Proves that weights of another feature set are rejected.
    /// </summary>
    [Fact]
    public void PerceptronModel_Load_FeatureSetMismatch_Throws()
    {
        // Arrange: the small file, expected under another feature set
        var file = SmallFile();

        // Act / Assert: the mismatch is named
        var ex = Assert.Throws<InvalidDataException>(() => PerceptronModel.Load(file, FeatureSet + 1));
        Assert.Contains("Feature set mismatch", ex.Message);
    }

    /// <summary>
    ///     Proves that a non-finite or non-positive temperature or scale is rejected on load and on write.
    /// </summary>
    [Fact]
    public void PerceptronModel_Load_InvalidTemperatureOrScale_Throws()
    {
        // Arrange: files with a zero temperature, a NaN scale and a negative scale
        var zero = SmallFile();
        BinaryFormat.WriteSingle(zero, 12, 0f);
        var nan = SmallFile();
        BinaryFormat.WriteSingle(nan, 16, float.NaN);
        var negative = SmallFile();
        BinaryFormat.WriteSingle(negative, 16, -1f);

        // Act / Assert: the loader and the writer both refuse
        Assert.Throws<InvalidDataException>(() => PerceptronModel.Load(zero, FeatureSet));
        Assert.Throws<InvalidDataException>(() => PerceptronModel.Load(nan, FeatureSet));
        Assert.Throws<InvalidDataException>(() => PerceptronModel.Load(negative, FeatureSet));
        Assert.Throws<ArgumentOutOfRangeException>(() => PerceptronModelWriter.Compile(18, 0f, 1f, FeatureSet, SmallRows(), false));
        Assert.Throws<ArgumentOutOfRangeException>(() => PerceptronModelWriter.Compile(18, 1f, float.PositiveInfinity, FeatureSet, SmallRows(), false));
        Assert.Throws<ArgumentOutOfRangeException>(() => PerceptronModelWriter.Compile(17, 1f, 1f, FeatureSet, SmallRows(), false));
    }

    /// <summary>
    ///     Proves that a changed payload byte is caught by the checksum, also in a Deflate file.
    /// </summary>
    [Fact]
    public void PerceptronModel_Load_ChecksumMismatch_Throws()
    {
        // Arrange: a raw file with one changed weight byte
        var file = SmallFile();
        file[file.Length - 1] ^= 0x01;

        // Act / Assert: reported as corrupt
        var ex = Assert.Throws<InvalidDataException>(() => PerceptronModel.Load(file, FeatureSet));
        Assert.Contains("checksum", ex.Message);
    }

    /// <summary>
    ///     Proves that a short file and a file with extra bytes are rejected.
    /// </summary>
    [Fact]
    public void PerceptronModel_Load_TruncatedOrTrailingBytes_Throws()
    {
        // Arrange: the small file cut short, extended, and shorter than its header
        var file = SmallFile();
        var cut = file.Take(file.Length - 2).ToArray();
        var extra = file.Concat(new byte[] { 0 }).ToArray();
        var header = file.Take(30).ToArray();

        // Act / Assert: each case has its own message
        Assert.Contains("truncated", Assert.Throws<InvalidDataException>(() => PerceptronModel.Load(cut, FeatureSet)).Message);
        Assert.Contains("trailing", Assert.Throws<InvalidDataException>(() => PerceptronModel.Load(extra, FeatureSet)).Message);
        Assert.Throws<InvalidDataException>(() => PerceptronModel.Load(header, FeatureSet));
        Assert.Throws<ArgumentNullException>(() => PerceptronModel.Load((byte[])null!, FeatureSet));
    }

    /// <summary>
    ///     Proves that rows whose tags or buckets are not strictly ascending are rejected even with a valid checksum.
    /// </summary>
    [Fact]
    public void PerceptronModel_Load_UnsortedTagsOrBuckets_Throws()
    {
        // Arrange: two rows (bucket 10: tags 2,4; bucket 20: tag 1) with payload [gaps 11,10][counts 2,1][tags 2,4,1][weights...]
        var rows = new List<KeyValuePair<int, short[]>>(SmallRows().Take(2));
        var unsortedTags = PerceptronModelWriter.Compile(18, 1f, 1f, FeatureSet, rows, false);
        (unsortedTags[PerceptronModel.HeaderLength + 4], unsortedTags[PerceptronModel.HeaderLength + 5]) =
            (unsortedTags[PerceptronModel.HeaderLength + 5], unsortedTags[PerceptronModel.HeaderLength + 4]);
        var zeroGap = PerceptronModelWriter.Compile(18, 1f, 1f, FeatureSet, rows, false);
        zeroGap[PerceptronModel.HeaderLength + 1] = 0;
        var zeroWeight = PerceptronModelWriter.Compile(18, 1f, 1f, FeatureSet, rows, false);
        zeroWeight[PerceptronModel.HeaderLength + 7] = 0;
        zeroWeight[PerceptronModel.HeaderLength + 8] = 0;

        // Act / Assert: all three are structural errors
        Assert.Throws<InvalidDataException>(() => PerceptronModel.Load(RecomputeChecksum(unsortedTags), FeatureSet));
        Assert.Throws<InvalidDataException>(() => PerceptronModel.Load(RecomputeChecksum(zeroGap), FeatureSet));
        Assert.Throws<InvalidDataException>(() => PerceptronModel.Load(RecomputeChecksum(zeroWeight), FeatureSet));
    }

    /// <summary>
    ///     Proves that extra bytes after the Deflate data, inside the stored region that the header declares, are
    ///     rejected: with the checksum of the stored bytes left alone, and with it recomputed to match the new region.
    /// </summary>
    [Fact]
    public void PerceptronModel_Load_DeflateTrailingBytesInStoredRegion_Throws()
    {
        // Arrange: a valid compressed file, and copies with three extra bytes behind the Deflate data whose stored
        // length is raised to match, once with the old checksum and once with a matching one
        var file = SmallFile(true);
        Assert.Equal(1, BinaryFormat.ReadUInt16(file, 6) & BinaryFormat.FlagDeflate);
        var padded = StoredRegion(file).Concat(new byte[] { 0x00, 0xFF, 0x42 }).ToArray();
        var staleChecksum = WithStoredRegion(file, padded, false);
        var freshChecksum = WithStoredRegion(file, padded, true);

        // Act / Assert: the original loads; the stale checksum is caught by the checksum, the fresh one by the strict inflation
        Assert.NotNull(PerceptronModel.Load(file, FeatureSet));
        Assert.Contains("checksum", Assert.Throws<InvalidDataException>(() => PerceptronModel.Load(staleChecksum, FeatureSet)).Message);
        Assert.Contains("trailing", Assert.Throws<InvalidDataException>(() => PerceptronModel.Load(freshChecksum, FeatureSet)).Message);
    }

    /// <summary>
    ///     Proves that a compressed file whose last Deflate byte was removed is rejected even when the header's stored
    ///     length is adjusted and the checksum is recomputed, so that the checksum cannot be what catches it.
    /// </summary>
    [Fact]
    public void PerceptronModel_Load_DeflateLastByteRemoved_Throws()
    {
        // Arrange: valid compressed files (the small one and one with several thousand rows) and copies without the last
        // compressed byte, with the stored length adjusted, once with the old checksum and once with a recomputed one
        foreach (var file in new[] { SmallFile(true), LargeCompressedFile() })
        {
            var shortened = StoredRegion(file).Take(StoredRegion(file).Length - 1).ToArray();
            var staleChecksum = WithStoredRegion(file, shortened, false);
            var freshChecksum = WithStoredRegion(file, shortened, true);

            // Act / Assert: the original loads; the shortened copies are invalid data, the second one by the strict inflation
            Assert.NotNull(PerceptronModel.Load(file, FeatureSet));
            Assert.Contains("checksum", Assert.Throws<InvalidDataException>(() => PerceptronModel.Load(staleChecksum, FeatureSet)).Message);
            Assert.Contains("cut off", Assert.Throws<InvalidDataException>(() => PerceptronModel.Load(freshChecksum, FeatureSet)).Message);
        }
    }

    /// <summary>
    ///     Proves that a compressed file cut in the middle of its Deflate data is rejected at every cut point, even when
    ///     the stored length and the checksum are adjusted to the shortened region.
    /// </summary>
    [Fact]
    public void PerceptronModel_Load_DeflateCutAtAnyPoint_Throws()
    {
        // Arrange: a valid compressed file with a payload large enough for several blocks of Deflate data, and its region
        var file = LargeCompressedFile();
        var stored = StoredRegion(file);
        Assert.NotNull(PerceptronModel.Load(file, FeatureSet));

        // Act / Assert: every proper prefix of the region is rejected as InvalidDataException, nothing else
        for (var length = 0; length < stored.Length; length += length < 64 ? 1 : 37)
        {
            var cut = WithStoredRegion(file, stored.Take(length).ToArray(), true);
            Assert.Throws<InvalidDataException>(() => PerceptronModel.Load(cut, FeatureSet));
        }

        Assert.Throws<InvalidDataException>(() => PerceptronModel.Load(WithStoredRegion(file, stored.Take(stored.Length - 1).ToArray(), true), FeatureSet));
    }

    /// <summary>
    ///     Proves that a declared payload length that is too large is rejected, even though the stored bytes and their
    ///     checksum are intact.
    /// </summary>
    /// <param name="extra">The number of bytes by which the declared payload length is too large.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(4096)]
    public void PerceptronModel_Load_DeflatePayloadLengthTooLarge_Throws(int extra)
    {
        // Arrange: a valid compressed file with a larger declared payload length
        var file = SmallFile(true);
        BinaryFormat.WriteUInt32(file, 28, BinaryFormat.ReadUInt32(file, 28) + (uint)extra);

        // Act / Assert: the inflated payload is shorter than declared
        var ex = Assert.Throws<InvalidDataException>(() => PerceptronModel.Load(file, FeatureSet));
        Assert.Contains("shorter", ex.Message);
    }

    /// <summary>
    ///     Proves that a declared payload length that is too small is rejected, even though the stored bytes and their
    ///     checksum are intact.
    /// </summary>
    /// <param name="missing">The number of bytes by which the declared payload length is too small.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void PerceptronModel_Load_DeflatePayloadLengthTooSmall_Throws(int missing)
    {
        // Arrange: a valid compressed file with a smaller declared payload length
        var file = SmallFile(true);
        BinaryFormat.WriteUInt32(file, 28, BinaryFormat.ReadUInt32(file, 28) - (uint)missing);

        // Act / Assert: the inflated payload is longer than declared
        var ex = Assert.Throws<InvalidDataException>(() => PerceptronModel.Load(file, FeatureSet));
        Assert.Contains("longer", ex.Message);
    }

    /// <summary>
    ///     Proves that a payload length above the format maximum is rejected before the payload is inflated.
    /// </summary>
    [Fact]
    public void PerceptronModel_Load_PayloadLengthAboveMaximum_Throws()
    {
        // Arrange: a valid compressed file whose header declares a payload one byte above the maximum
        var file = SmallFile(true);
        BinaryFormat.WriteUInt32(file, 28, (uint)PerceptronModel.MaxPayloadLength + 1);

        // Act / Assert: the limit is named
        var ex = Assert.Throws<InvalidDataException>(() => PerceptronModel.Load(file, FeatureSet));
        Assert.Contains("maximum", ex.Message);
    }

    /// <summary>
    ///     Proves that non-zero reserved bytes and unknown flag bits are rejected.
    /// </summary>
    [Fact]
    public void PerceptronModel_Load_ReservedNotZero_Throws()
    {
        // Arrange: files with a reserved byte set and a reserved flag set
        var reserved = SmallFile();
        reserved[44] = 1;
        var flag = SmallFile();
        BinaryFormat.WriteUInt16(flag, 6, 2);

        // Act / Assert: both are invalid
        Assert.Throws<InvalidDataException>(() => PerceptronModel.Load(reserved, FeatureSet));
        Assert.Throws<InvalidDataException>(() => PerceptronModel.Load(flag, FeatureSet));
    }

    /// <summary>
    ///     Proves that the writer rejects bad rows: wrong length, the weight -32768 and a repeated bucket.
    /// </summary>
    [Fact]
    public void PerceptronModelWriter_Compile_InvalidRows_Throw()
    {
        // Arrange: three invalid row sets
        var shortRow = new[] { new KeyValuePair<int, short[]>(1, new short[3]) };
        var minValue = new short[17];
        minValue[0] = short.MinValue;
        var minRow = new[] { new KeyValuePair<int, short[]>(1, minValue) };
        var one = new short[17];
        one[0] = 1;
        var duplicate = new[] { new KeyValuePair<int, short[]>(1, one), new KeyValuePair<int, short[]>(1, one) };
        var outside = new[] { new KeyValuePair<int, short[]>(1 << 18, one) };

        // Act / Assert: each is refused; all-zero rows are dropped silently
        Assert.Throws<ArgumentException>(() => PerceptronModelWriter.Compile(18, 1f, 1f, FeatureSet, shortRow, false));
        Assert.Throws<ArgumentException>(() => PerceptronModelWriter.Compile(18, 1f, 1f, FeatureSet, minRow, false));
        Assert.Throws<ArgumentException>(() => PerceptronModelWriter.Compile(18, 1f, 1f, FeatureSet, duplicate, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => PerceptronModelWriter.Compile(18, 1f, 1f, FeatureSet, outside, false));
        var empty = PerceptronModel.Load(PerceptronModelWriter.Compile(18, 1f, 1f, FeatureSet, new[] { new KeyValuePair<int, short[]>(5, new short[17]) }, false), FeatureSet);
        Assert.Equal(0, empty.RowCount);
    }

    /// <summary>
    ///     Proves that the feature group mask is stored little-endian at offset 40 with feature set 2, that the reader returns it,
    ///     and that the uncompressed image keeps it.
    /// </summary>
    /// <param name="mask">The groups as a mask.</param>
    [Theory]
    [InlineData(1u)]
    [InlineData(2u)]
    [InlineData(4u)]
    [InlineData(8u)]
    [InlineData(9u)]
    [InlineData(15u)]
    public void PerceptronModel_Load_FeatureGroups_RoundTripsMask(uint mask)
    {
        // Arrange: the small model written with a group mask, compressed and not
        var groups = (FeatureGroups)mask;
        var featureSet = FeatureExtractor.FeatureSetVersionFor(groups);
        var plain = PerceptronModelWriter.Compile(18, 5.4f, 0.25f, featureSet, SmallRows(), false, mask);
        var packed = PerceptronModelWriter.Compile(18, 5.4f, 0.25f, featureSet, SmallRows(), true, mask);

        // Act: load both ways and build the image
        var model = PerceptronModel.Load(plain);
        var byVersion = PerceptronModel.Load(packed, FeatureExtractor.GroupsFeatureSetVersion);
        var image = PerceptronModel.ToUncompressedImage(packed);

        // Assert: version 2 layout, feature set 2, mask at offset 40, reserved bytes zero, same model
        Assert.Equal(groups, model.FeatureGroups);
        Assert.Equal(groups, byVersion.FeatureGroups);
        Assert.Equal((ushort)2, BinaryFormat.ReadUInt16(plain, 4));
        Assert.Equal((ushort)2, BinaryFormat.ReadUInt16(plain, 8));
        Assert.Equal(mask, BinaryFormat.ReadUInt32(plain, 40));
        Assert.Equal(mask & 0xFF, plain[40]);
        Assert.All(plain.Skip(44).Take(4), b => Assert.Equal(0, b));
        Assert.Equal(mask, BinaryFormat.ReadUInt32(image, 40));
        Assert.Equal(model.RowCount, byVersion.RowCount);
        Assert.Equal(groups, PerceptronModel.Load(image).FeatureGroups);
    }

    /// <summary>
    ///     Proves that a file without groups is written byte for byte as before the mask existed (feature set 1, zero mask and reserved
    ///     bytes) and that this build reads such a file, also the way that earlier callers ask for it.
    /// </summary>
    [Fact]
    public void PerceptronModel_Load_OldFileWithoutMask_LoadsAsNoGroups()
    {
        // Arrange: files written without the mask parameter, with an explicit zero mask, and the same with a header patched to the old layout
        var implicitMask = SmallFile(false);
        var explicitMask = PerceptronModelWriter.Compile(18, 5.4f, 0.25f, FeatureSet, SmallRows(), false, 0);
        var old = (byte[])implicitMask.Clone();
        for (var i = 40; i < PerceptronModel.HeaderLength; i++)
        {
            old[i] = 0;
        }

        // Act: load the three ways
        var byDefault = PerceptronModel.Load(old);
        var byVersion = PerceptronModel.Load(old, FeatureSet);

        // Assert: identical bytes, feature set 1, no groups, and every header byte from offset 40 on is zero
        Assert.Equal(implicitMask, explicitMask);
        Assert.Equal(implicitMask, old);
        Assert.Equal((ushort)1, BinaryFormat.ReadUInt16(old, 8));
        Assert.Equal(FeatureGroups.None, byDefault.FeatureGroups);
        Assert.Equal(FeatureGroups.None, byVersion.FeatureGroups);
        Assert.Equal(FeatureGroups.None, PerceptronModel.Load(SmallFile(true)).FeatureGroups);
    }

    /// <summary>
    ///     Proves that a mask with a bit that this build does not know (including the bit reserved for word clusters) is rejected, and that the writer refuses to write one.
    /// </summary>
    /// <param name="mask">The invalid mask.</param>
    [Theory]
    [InlineData(16u)]
    [InlineData(17u)]
    [InlineData(0x80000000u)]
    [InlineData(0xFFFFFFFFu)]
    public void PerceptronModel_Load_UnknownGroupBit_Throws(uint mask)
    {
        // Arrange: a file with groups whose mask was patched
        var file = PerceptronModelWriter.Compile(18, 5.4f, 0.25f, 2, SmallRows(), false, 1);
        BinaryFormat.WriteUInt32(file, 40, mask);

        // Act / Assert: the load names the mask, and the writer refuses it
        var ex = Assert.Throws<InvalidDataException>(() => PerceptronModel.Load(file));
        Assert.Contains("feature group mask", ex.Message);
        Assert.Throws<InvalidDataException>(() => PerceptronModel.Load(file, 2));
        Assert.Throws<InvalidDataException>(() => PerceptronModel.ToUncompressedImage(file));
        Assert.Throws<ArgumentOutOfRangeException>(() => PerceptronModelWriter.Compile(18, 1f, 1f, 2, SmallRows(), false, mask));
    }

    /// <summary>
    ///     Proves that the feature set of the header must agree with the mask: feature set 1 needs a zero mask, feature set 2 a non-zero mask,
    ///     and no other feature set is read.
    /// </summary>
    [Fact]
    public void PerceptronModel_Load_FeatureSetDisagreesWithMask_Throws()
    {
        // Arrange: groups under feature set 1, no groups under feature set 2, and feature set 3
        var groupsUnderOne = PerceptronModelWriter.Compile(18, 1f, 1f, 1, SmallRows(), false, 1);
        var noneUnderTwo = PerceptronModelWriter.Compile(18, 1f, 1f, 2, SmallRows(), false);
        var three = PerceptronModelWriter.Compile(18, 1f, 1f, 3, SmallRows(), false);

        // Act / Assert: each is rejected, by any way of loading
        Assert.Contains("does not match", Assert.Throws<InvalidDataException>(() => PerceptronModel.Load(groupsUnderOne)).Message);
        Assert.Contains("does not match", Assert.Throws<InvalidDataException>(() => PerceptronModel.Load(noneUnderTwo)).Message);
        Assert.Contains("Feature set mismatch", Assert.Throws<InvalidDataException>(() => PerceptronModel.Load(three)).Message);
        Assert.Throws<InvalidDataException>(() => PerceptronModel.Load(groupsUnderOne, 1));
        Assert.Throws<InvalidDataException>(() => PerceptronModel.Load(noneUnderTwo, 2));
    }

    /// <summary>
    ///     Proves that a file with groups fails the two checks that a reader from before the mask existed makes (feature set 1 only, reserved
    ///     bytes zero), so that such a reader fails loudly instead of decoding with the wrong features.
    /// </summary>
    [Fact]
    public void PerceptronModel_Load_FileWithGroups_FailsTheChecksOfAnOldReader()
    {
        // Arrange: a file with groups
        var file = PerceptronModelWriter.Compile(18, 5.4f, 0.25f, 2, SmallRows(), false, 5);

        // Act / Assert: this build asked for feature set 1 (what every earlier reader did) refuses it, and the old reserved range is not zero
        var ex = Assert.Throws<InvalidDataException>(() => PerceptronModel.Load(file, FeatureSet));
        Assert.Contains("Feature set mismatch", ex.Message);
        Assert.Contains(file.Skip(40).Take(8), b => b != 0);
        Assert.NotEqual(FeatureSet, (int)BinaryFormat.ReadUInt16(file, 8));
    }
}
