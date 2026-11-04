namespace DemaConsulting.PartOfSpeech.Tests.ModelStorage;

/// <summary>
///     Unit tests of <see cref="BinaryFormat"/>.
/// </summary>
public class BinaryFormatTests
{
    /// <summary>
    ///     Proves that 16-bit, 32-bit and single-precision fields are little endian and round trip.
    /// </summary>
    [Fact]
    public void BinaryFormat_ReadWrite_LittleEndianPrimitives_RoundTrip()
    {
        // Arrange: a buffer and values with distinct bytes
        var buffer = new byte[16];

        // Act: write the three kinds of field
        BinaryFormat.WriteUInt16(buffer, 0, 0x1234);
        BinaryFormat.WriteUInt32(buffer, 2, 0xA1B2C3D4);
        BinaryFormat.WriteSingle(buffer, 8, 5.4f);

        // Assert: the byte order is little endian and the values come back
        Assert.Equal(0x34, buffer[0]);
        Assert.Equal(0x12, buffer[1]);
        Assert.Equal(0xD4, buffer[2]);
        Assert.Equal(0xA1, buffer[5]);
        Assert.Equal((ushort)0x1234, BinaryFormat.ReadUInt16(buffer, 0));
        Assert.Equal(0xA1B2C3D4u, BinaryFormat.ReadUInt32(buffer, 2));
        Assert.Equal(5.4f, BinaryFormat.ReadSingle(buffer, 8));
        Assert.Throws<InvalidDataException>(() => BinaryFormat.ReadInt32Field(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, 0, "test field"));
    }

    /// <summary>
    ///     Proves that varints round trip at every length boundary.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="length">The expected encoded length in bytes.</param>
    [Theory]
    [InlineData(0u, 1)]
    [InlineData(1u, 1)]
    [InlineData(127u, 1)]
    [InlineData(128u, 2)]
    [InlineData(16383u, 2)]
    [InlineData(16384u, 3)]
    [InlineData(2097151u, 3)]
    [InlineData(2097152u, 4)]
    [InlineData(268435455u, 4)]
    [InlineData(268435456u, 5)]
    [InlineData(uint.MaxValue, 5)]
    public void BinaryFormat_Varint_RoundTripsBoundaryValues(uint value, int length)
    {
        // Arrange: write the value
        using var stream = new MemoryStream();
        BinaryFormat.WriteVarint(stream, value);
        var bytes = stream.ToArray();

        // Act: read it back
        var position = 0;
        var result = BinaryFormat.ReadVarint(bytes, ref position, bytes.Length);

        // Assert: same value, whole input consumed, minimal length
        Assert.Equal(value, result);
        Assert.Equal(bytes.Length, position);
        Assert.Equal(length, bytes.Length);
    }

    /// <summary>
    ///     Proves that a varint that is too long or cut off is rejected.
    /// </summary>
    [Fact]
    public void BinaryFormat_Varint_OverlongOrTruncated_Throws()
    {
        // Arrange: six continuation bytes, a fifth byte with too many bits and a cut-off varint
        var overlong = new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80, 0x01 };
        var tooManyBits = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x1F };
        var truncated = new byte[] { 0x80, 0x80 };

        // Act / Assert: each one throws InvalidDataException, never an index error
        Assert.Throws<InvalidDataException>(() =>
        {
            var p = 0;
            return BinaryFormat.ReadVarint(overlong, ref p, overlong.Length);
        });
        Assert.Throws<InvalidDataException>(() =>
        {
            var p = 0;
            return BinaryFormat.ReadVarint(tooManyBits, ref p, tooManyBits.Length);
        });
        Assert.Throws<InvalidDataException>(() =>
        {
            var p = 0;
            return BinaryFormat.ReadVarint(truncated, ref p, truncated.Length);
        });
    }

    /// <summary>
    ///     Proves the checksum against published FNV-1a 32-bit test vectors.
    /// </summary>
    [Fact]
    public void BinaryFormat_Fnv1a_MatchesKnownVectors()
    {
        // Arrange: the reference vectors of the FNV-1a 32-bit test suite
        var empty = new byte[0];
        var a = new[] { (byte)'a' };
        var foobar = System.Text.Encoding.ASCII.GetBytes("foobar");

        // Act / Assert: the hashes equal the published values (also over a sub-range)
        Assert.Equal(0x811C9DC5u, BinaryFormat.Fnv1a(empty, 0, 0));
        Assert.Equal(0xE40C292Cu, BinaryFormat.Fnv1a(a, 0, 1));
        Assert.Equal(0xBF9CF968u, BinaryFormat.Fnv1a(foobar, 0, 6));
        Assert.Equal(0xE40C292Cu, BinaryFormat.Fnv1a(new byte[] { 1, 2, (byte)'a' }, 2, 1));
    }

    /// <summary>
    ///     Proves that a file with another magic is rejected as the wrong kind of file.
    /// </summary>
    [Fact]
    public void BinaryFormat_ReadHeader_BadMagic_Throws()
    {
        // Arrange: a header-sized buffer with the wrong magic
        var file = new byte[16];
        file[0] = (byte)'X';

        // Act / Assert: the message names the missing magic
        var ex = Assert.Throws<InvalidDataException>(() => BinaryFormat.ReadHeader(file, "POSW", 16, 1, "weights file"));
        Assert.Contains("POSW", ex.Message);
    }

    /// <summary>
    ///     Proves that another format version and unknown flag bits are rejected with a version message.
    /// </summary>
    [Fact]
    public void BinaryFormat_ReadHeader_UnsupportedVersion_Throws()
    {
        // Arrange: a header with version 2 and one with a reserved flag bit
        var version = new byte[16];
        System.Text.Encoding.ASCII.GetBytes("POSW").CopyTo(version, 0);
        BinaryFormat.WriteUInt16(version, 4, 2);
        var flags = new byte[16];
        System.Text.Encoding.ASCII.GetBytes("POSW").CopyTo(flags, 0);
        BinaryFormat.WriteUInt16(flags, 4, 1);
        BinaryFormat.WriteUInt16(flags, 6, 0x0002);

        // Act / Assert: both are rejected, the first with the version in the message
        var ex = Assert.Throws<InvalidDataException>(() => BinaryFormat.ReadHeader(version, "POSW", 16, 1, "weights file"));
        Assert.Contains("version 2", ex.Message);
        Assert.Throws<InvalidDataException>(() => BinaryFormat.ReadHeader(flags, "POSW", 16, 1, "weights file"));
    }

    /// <summary>
    ///     Proves that a file shorter than its header is reported as truncated.
    /// </summary>
    [Fact]
    public void BinaryFormat_ReadHeader_Truncated_Throws()
    {
        // Arrange: a file with the right magic but only 10 bytes
        var file = new byte[10];
        System.Text.Encoding.ASCII.GetBytes("POSW").CopyTo(file, 0);

        // Act / Assert: the message says truncated
        var ex = Assert.Throws<InvalidDataException>(() => BinaryFormat.ReadHeader(file, "POSW", 48, 1, "weights file"));
        Assert.Contains("truncated", ex.Message);
        Assert.Throws<InvalidDataException>(() => BinaryFormat.ReadHeader(new byte[2], "POSW", 48, 1, "weights file"));
    }

    /// <summary>
    ///     Proves that compression and decompression round trip and that a wrong declared size is rejected.
    /// </summary>
    [Fact]
    public void BinaryFormat_Deflate_RoundTrips()
    {
        // Arrange: compressible data
        var data = Enumerable.Range(0, 5000).Select(i => (byte)(i % 7)).ToArray();

        // Act: compress and inflate
        var compressed = BinaryFormat.Compress(data);
        var inflated = BinaryFormat.Decompress(compressed, 0, compressed.Length, data.Length);

        // Assert: identical bytes, smaller storage, and wrong sizes are rejected in both directions
        Assert.Equal(data, inflated);
        Assert.True(compressed.Length < data.Length / 4);
        Assert.Throws<InvalidDataException>(() => BinaryFormat.Decompress(compressed, 0, compressed.Length, data.Length + 1));
        Assert.Throws<InvalidDataException>(() => BinaryFormat.Decompress(compressed, 0, compressed.Length, data.Length - 1));
    }

    /// <summary>
    ///     Proves that bytes after the end of the Deflate data but inside the stored region are rejected, whatever their
    ///     number and value, while every exactly sized region is accepted.
    /// </summary>
    /// <param name="length">The uncompressed length of the data.</param>
    /// <param name="random">Whether the data is incompressible (so that the Deflate data uses stored blocks) instead of repetitive.</param>
    [Theory]
    [InlineData(1, false)]
    [InlineData(100, true)]
    [InlineData(5000, false)]
    [InlineData(70000, true)]
    [InlineData(300000, false)]
    public void BinaryFormat_Decompress_TrailingBytesInStoredRegion_Throws(int length, bool random)
    {
        // Arrange: valid Deflate data of the requested kind
        var data = new byte[length];
        if (random)
        {
            new Random(length).NextBytes(data);
        }
        else
        {
            for (var i = 0; i < data.Length; i++)
            {
                data[i] = (byte)(i % 7);
            }
        }

        var compressed = BinaryFormat.Compress(data);

        // Act / Assert: the exact region is accepted; one to many extra bytes of any value, placed behind the data inside
        // a region of the file that has a prefix, are rejected as trailing bytes
        Assert.Equal(data, BinaryFormat.Decompress(compressed, 0, compressed.Length, data.Length));
        foreach (var extra in new[] { 1, 2, 3, 17, 9000 })
        {
            foreach (var value in new byte[] { 0x00, 0xFF, 0x5A })
            {
                var file = new byte[3 + compressed.Length + extra];
                Array.Copy(compressed, 0, file, 3, compressed.Length);
                for (var i = 0; i < extra; i++)
                {
                    file[3 + compressed.Length + i] = value;
                }

                var ex = Assert.Throws<InvalidDataException>(() => BinaryFormat.Decompress(file, 3, compressed.Length + extra, data.Length));
                Assert.Contains("trailing", ex.Message);
            }
        }
    }

    /// <summary>
    ///     Proves that a Deflate stream that is cut off is rejected at every cut point, in particular when only its last
    ///     byte is missing (which holds the end-of-block code, so all data can still be inflated), whatever the region
    ///     length that the caller passes.
    /// </summary>
    /// <param name="length">The uncompressed length of the data.</param>
    /// <param name="random">Whether the data is incompressible (stored blocks) instead of repetitive.</param>
    [Theory]
    [InlineData(1, false)]
    [InlineData(100, true)]
    [InlineData(5000, false)]
    [InlineData(70000, true)]
    public void BinaryFormat_Decompress_CutOffStream_Throws(int length, bool random)
    {
        // Arrange: valid Deflate data
        var data = new byte[length];
        if (random)
        {
            new Random(length).NextBytes(data);
        }
        else
        {
            for (var i = 0; i < data.Length; i++)
            {
                data[i] = (byte)(i % 7);
            }
        }

        var compressed = BinaryFormat.Compress(data);

        // Act / Assert: the last byte missing, with the region length adjusted, is reported as a cut-off stream
        var oneShort = Assert.Throws<InvalidDataException>(() => BinaryFormat.Decompress(compressed, 0, compressed.Length - 1, data.Length));
        Assert.Contains("cut off", oneShort.Message);

        // Act / Assert: every other cut point, from the empty region to a single missing byte, is rejected; the cut is
        // taken in a buffer that is longer than the region so that bytes after the region cannot be read by accident
        var cuts = new[] { 0, 1, 2, 3, compressed.Length / 4, compressed.Length / 2, compressed.Length - 3, compressed.Length - 2 };
        foreach (var cut in cuts.Where(c => c >= 0 && c < compressed.Length).Distinct())
        {
            Assert.Throws<InvalidDataException>(() => BinaryFormat.Decompress(compressed, 0, cut, data.Length));
        }
    }

    /// <summary>
    ///     Proves that a declared length that is not the inflated length is rejected in both directions, for compressible
    ///     and incompressible data.
    /// </summary>
    /// <param name="random">Whether the data is incompressible instead of repetitive.</param>
    /// <param name="delta">The difference between the declared length and the real length.</param>
    [Theory]
    [InlineData(false, 1)]
    [InlineData(false, 1000)]
    [InlineData(true, 1)]
    [InlineData(true, 1000)]
    [InlineData(false, -1)]
    [InlineData(false, -1000)]
    [InlineData(true, -1)]
    [InlineData(true, -1000)]
    public void BinaryFormat_Decompress_DeclaredLengthWrong_Throws(bool random, int delta)
    {
        // Arrange: valid Deflate data of 3000 bytes
        var data = new byte[3000];
        if (random)
        {
            new Random(3).NextBytes(data);
        }
        else
        {
            for (var i = 0; i < data.Length; i++)
            {
                data[i] = (byte)(i % 11);
            }
        }

        var compressed = BinaryFormat.Compress(data);

        // Act / Assert: a larger declared length finds the stream shorter, a smaller one finds it longer
        var ex = Assert.Throws<InvalidDataException>(() => BinaryFormat.Decompress(compressed, 0, compressed.Length, data.Length + delta));
        Assert.Contains(delta > 0 ? "shorter" : "longer", ex.Message);
    }

    /// <summary>
    ///     Proves that an empty payload is stored as a complete stream, so that it round trips and a cut-off empty
    ///     region is not mistaken for it.
    /// </summary>
    [Fact]
    public void BinaryFormat_Compress_EmptyPayload_StoresCompleteStream()
    {
        // Arrange / Act: compress nothing
        var compressed = BinaryFormat.Compress(Array.Empty<byte>());
        var inflated = BinaryFormat.Decompress(compressed, 0, compressed.Length, 0);

        // Assert: a non-empty complete stream that inflates to nothing; an empty region and a declared byte are rejected
        Assert.NotEmpty(compressed);
        Assert.Empty(inflated);
        Assert.Throws<InvalidDataException>(() => BinaryFormat.Decompress(compressed, 0, 0, 0));
        Assert.Throws<InvalidDataException>(() => BinaryFormat.Decompress(compressed, 0, compressed.Length, 1));
    }

    /// <summary>
    ///     Proves that the reader reports bytes that are not Deflate data as invalid data and never as another exception
    ///     type, for several kinds of garbage.
    /// </summary>
    [Fact]
    public void BinaryFormat_Decompress_GarbageRegion_Throws()
    {
        // Arrange: regions of one reserved block type (type 3), all ones, and pseudo-random bytes
        var reserved = new byte[] { 0x07, 0x00, 0x00, 0x00 };
        var ones = Enumerable.Repeat((byte)0xFF, 64).ToArray();
        var random = new byte[200];
        new Random(11).NextBytes(random);

        // Act / Assert: every case is InvalidDataException
        foreach (var region in new[] { reserved, ones, random })
        {
            Assert.Throws<InvalidDataException>(() => BinaryFormat.Decompress(region, 0, region.Length, 100));
        }
    }

    /// <summary>
    ///     Proves that the population count equals a bit-by-bit reference on every platform branch.
    /// </summary>
    [Fact]
    public void BinaryFormat_PopCount_MatchesReference()
    {
        // Arrange: edge values and pseudo-random words
        var random = new Random(7);
        var values = new List<ulong> { 0, 1, ulong.MaxValue, 0x8000000000000000UL, 0x5555555555555555UL };
        for (var i = 0; i < 1000; i++)
        {
            var bytes = new byte[8];
            random.NextBytes(bytes);
            values.Add(BitConverter.ToUInt64(bytes, 0));
        }

        // Act / Assert: every count equals the reference loop
        foreach (var value in values)
        {
            var expected = 0;
            for (var bit = 0; bit < 64; bit++)
            {
                expected += (int)((value >> bit) & 1);
            }

            Assert.Equal(expected, BinaryFormat.PopCount(value));
        }
    }

    /// <summary>
    ///     Proves that byte ranges compare like UTF-8 text.
    /// </summary>
    [Fact]
    public void BinaryFormat_Compare_OrdersUnsignedBytes()
    {
        // Arrange: ranges where the signed and unsigned order differ
        var low = new byte[] { 0x7F };
        var high = new byte[] { 0xC3, 0xA9 };
        var prefix = new byte[] { 1, 2 };
        var longer = new byte[] { 1, 2, 3 };

        // Act / Assert: unsigned order, and a prefix sorts first
        Assert.True(BinaryFormat.Compare(low, 0, 1, high, 0, 2) < 0);
        Assert.True(BinaryFormat.Compare(high, 0, 2, low, 0, 1) > 0);
        Assert.True(BinaryFormat.Compare(prefix, 0, 2, longer, 0, 3) < 0);
        Assert.Equal(0, BinaryFormat.Compare(prefix, 0, 2, longer, 0, 2));
    }
}
