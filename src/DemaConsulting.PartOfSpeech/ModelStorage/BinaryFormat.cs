using System.IO.Compression;
#if NET6_0_OR_GREATER
using System.Numerics;
#endif

namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     Primitives shared by the two binary formats: little-endian fields, LEB128 varints, the FNV-1a checksum, header
///     validation, Deflate wrapping and a portable population count.
/// </summary>
/// <remarks>
///     The package targets <c>netstandard2.0</c> and must have no runtime dependency, so none of the span-based
///     <c>BinaryPrimitives</c> or <c>System.Memory</c> APIs are used: everything works on plain byte arrays. All
///     members are stateless and thread-safe. Every malformed input is reported as an
///     <see cref="InvalidDataException"/> with a message that names the problem, never as an index or overflow error.
/// </remarks>
internal static class BinaryFormat
{
    /// <summary>
    ///     The FNV-1a 32-bit offset basis.
    /// </summary>
    public const uint FnvOffsetBasis = 2166136261;

    /// <summary>
    ///     The FNV-1a 32-bit prime.
    /// </summary>
    public const uint FnvPrime = 16777619;

    /// <summary>
    ///     The flag bit of a file header that marks a raw Deflate payload.
    /// </summary>
    public const int FlagDeflate = 1;

    /// <summary>
    ///     Reads a little-endian 16-bit unsigned integer.
    /// </summary>
    /// <param name="data">The buffer.</param>
    /// <param name="offset">The offset of the first byte.</param>
    /// <returns>The value.</returns>
    public static ushort ReadUInt16(byte[] data, int offset) => (ushort)(data[offset] | (data[offset + 1] << 8));

    /// <summary>
    ///     Reads a little-endian 32-bit unsigned integer.
    /// </summary>
    /// <param name="data">The buffer.</param>
    /// <param name="offset">The offset of the first byte.</param>
    /// <returns>The value.</returns>
    public static uint ReadUInt32(byte[] data, int offset) =>
        (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));

    /// <summary>
    ///     Reads a little-endian 32-bit unsigned integer that must fit a positive or zero <see cref="int"/>.
    /// </summary>
    /// <param name="data">The buffer.</param>
    /// <param name="offset">The offset of the first byte.</param>
    /// <param name="field">The field name for the error message.</param>
    /// <returns>The value.</returns>
    /// <exception cref="InvalidDataException">The value is larger than <see cref="int.MaxValue"/>.</exception>
    public static int ReadInt32Field(byte[] data, int offset, string field)
    {
        var value = ReadUInt32(data, offset);
        if (value > int.MaxValue)
        {
            throw new InvalidDataException($"Invalid {field}: {value} is too large.");
        }

        return (int)value;
    }

    /// <summary>
    ///     Reads a little-endian IEEE-754 single-precision number.
    /// </summary>
    /// <param name="data">The buffer.</param>
    /// <param name="offset">The offset of the first byte.</param>
    /// <returns>The value.</returns>
    public static float ReadSingle(byte[] data, int offset)
    {
        // Assemble the bytes in the machine order so the result does not depend on the endianness of the host
        var bytes = new[] { data[offset], data[offset + 1], data[offset + 2], data[offset + 3] };
        if (!BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        return BitConverter.ToSingle(bytes, 0);
    }

    /// <summary>
    ///     Writes a little-endian 16-bit unsigned integer.
    /// </summary>
    /// <param name="data">The buffer.</param>
    /// <param name="offset">The offset of the first byte.</param>
    /// <param name="value">The value.</param>
    public static void WriteUInt16(byte[] data, int offset, ushort value)
    {
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
    }

    /// <summary>
    ///     Writes a little-endian 32-bit unsigned integer.
    /// </summary>
    /// <param name="data">The buffer.</param>
    /// <param name="offset">The offset of the first byte.</param>
    /// <param name="value">The value.</param>
    public static void WriteUInt32(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
        data[offset + 2] = (byte)(value >> 16);
        data[offset + 3] = (byte)(value >> 24);
    }

    /// <summary>
    ///     Writes a little-endian IEEE-754 single-precision number.
    /// </summary>
    /// <param name="data">The buffer.</param>
    /// <param name="offset">The offset of the first byte.</param>
    /// <param name="value">The value.</param>
    public static void WriteSingle(byte[] data, int offset, float value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (!BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        Array.Copy(bytes, 0, data, offset, 4);
    }

    /// <summary>
    ///     Determines whether a single-precision number is finite and greater than zero.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <returns><c>true</c> for a finite positive number; <c>false</c> for zero, negatives, NaN and infinities.</returns>
    public static bool IsFinitePositive(float value) => value > 0 && !float.IsInfinity(value);

    /// <summary>
    ///     Appends an unsigned LEB128 varint (seven bits per byte, low group first).
    /// </summary>
    /// <param name="output">The stream to append to.</param>
    /// <param name="value">The value.</param>
    public static void WriteVarint(Stream output, uint value)
    {
        // Emit seven bits at a time; the high bit of a byte says that more bytes follow
        while (value >= 0x80)
        {
            output.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }

        output.WriteByte((byte)value);
    }

    /// <summary>
    ///     Reads an unsigned LEB128 varint.
    /// </summary>
    /// <param name="data">The buffer.</param>
    /// <param name="position">The position of the first byte; advanced past the varint.</param>
    /// <param name="end">The exclusive end of the readable region.</param>
    /// <returns>The value.</returns>
    /// <exception cref="InvalidDataException">The varint runs past <paramref name="end"/> or has more than 32 bits.</exception>
    public static uint ReadVarint(byte[] data, ref int position, int end)
    {
        uint value = 0;
        for (var shift = 0; shift < 35; shift += 7)
        {
            // A varint that is cut off by the end of the region is a truncated file, not an index error
            if (position >= end)
            {
                throw new InvalidDataException("Truncated varint.");
            }

            var b = data[position++];

            // The fifth byte may only carry the top four bits of a 32-bit value
            if (shift == 28 && b > 0x0F)
            {
                throw new InvalidDataException("Overlong varint.");
            }

            value |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return value;
            }
        }

        throw new InvalidDataException("Overlong varint.");
    }

    /// <summary>
    ///     Reads an unsigned LEB128 varint that is used as a length or an index and must fit a 32-bit signed integer.
    /// </summary>
    /// <param name="data">The buffer.</param>
    /// <param name="position">The position of the first byte; advanced past the varint.</param>
    /// <param name="end">The exclusive end of the readable region.</param>
    /// <returns>The value, never negative.</returns>
    /// <exception cref="InvalidDataException">
    ///     The varint runs past <paramref name="end"/>, has more than 32 bits or is larger than 2147483647.
    /// </exception>
    public static int ReadVarintInt32(byte[] data, ref int position, int end)
    {
        var value = ReadVarint(data, ref position, end);

        // A value above int.MaxValue would wrap to a negative number in a cast and bypass range checks
        if (value > int.MaxValue)
        {
            throw new InvalidDataException("Invalid varint: the value is larger than 2147483647.");
        }

        return (int)value;
    }

    /// <summary>
    ///     Computes the FNV-1a 32-bit hash of a byte range.
    /// </summary>
    /// <param name="data">The buffer.</param>
    /// <param name="offset">The offset of the first byte.</param>
    /// <param name="count">The number of bytes.</param>
    /// <returns>The hash.</returns>
    public static uint Fnv1a(byte[] data, int offset, int count)
    {
        var hash = FnvOffsetBasis;
        for (var i = offset; i < offset + count; i++)
        {
            hash = (hash ^ data[i]) * FnvPrime;
        }

        return hash;
    }

    /// <summary>
    ///     Reads a whole stream into a byte array.
    /// </summary>
    /// <param name="stream">The stream, read from its current position to its end.</param>
    /// <returns>The bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <c>null</c>.</exception>
    public static byte[] ReadAllBytes(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>
    ///     Validates the first fields that every file of the family shares: length, magic, format version and flags.
    /// </summary>
    /// <param name="file">The whole file.</param>
    /// <param name="magic">The expected four ASCII characters.</param>
    /// <param name="headerLength">The fixed header length of the format.</param>
    /// <param name="supportedVersion">The only format version that this build reads.</param>
    /// <param name="kind">A name for the file kind used in messages, for example <c>weights file</c>.</param>
    /// <returns>The <c>flags</c> field (offset 6).</returns>
    /// <exception cref="InvalidDataException">The file is shorter than its header, has another magic or another version.</exception>
    public static int ReadHeader(byte[] file, string magic, int headerLength, int supportedVersion, string kind)
    {
        // Check the magic first so that a file of another kind is reported as such even when it is short
        if (file.Length < 4 || file[0] != magic[0] || file[1] != magic[1] || file[2] != magic[2] || file[3] != magic[3])
        {
            throw new InvalidDataException($"Not a {kind}: the magic '{magic}' is missing.");
        }

        if (file.Length < headerLength)
        {
            throw new InvalidDataException($"The {kind} is truncated: the header needs {headerLength} bytes but the file has {file.Length}.");
        }

        var version = ReadUInt16(file, 4);
        if (version != supportedVersion)
        {
            throw new InvalidDataException($"Unsupported {kind} version {version}; this build reads version {supportedVersion}.");
        }

        var flags = ReadUInt16(file, 6);
        if ((flags & ~FlagDeflate) != 0)
        {
            throw new InvalidDataException($"Invalid {kind}: unknown flag bits 0x{flags:X4}.");
        }

        return flags;
    }

    /// <summary>
    ///     Extracts and verifies the payload of a file: length, optional Deflate and checksum.
    /// </summary>
    /// <param name="file">The whole file.</param>
    /// <param name="headerLength">The header length.</param>
    /// <param name="flags">The <c>flags</c> field.</param>
    /// <param name="payloadLength">The declared uncompressed payload length.</param>
    /// <param name="maxPayloadLength">The largest uncompressed payload length that the format accepts (a guard against decompression bombs in a corrupt header).</param>
    /// <param name="storedLength">The declared number of stored bytes after the header.</param>
    /// <param name="checksum">The declared FNV-1a checksum of the uncompressed payload.</param>
    /// <param name="kind">A name for the file kind used in messages.</param>
    /// <returns>The uncompressed payload.</returns>
    /// <exception cref="InvalidDataException">A declared length does not match the file or the checksum differs.</exception>
    public static byte[] ReadPayload(byte[] file, int headerLength, int flags, int payloadLength, int maxPayloadLength, int storedLength, uint checksum, string kind)
    {
        // The stored length must describe the rest of the file exactly: shorter is truncated, longer has trailing bytes
        var available = file.Length - headerLength;
        if (storedLength > available)
        {
            throw new InvalidDataException($"The {kind} is truncated: {storedLength} payload bytes are declared but {available} are present.");
        }

        if (storedLength < available)
        {
            throw new InvalidDataException($"The {kind} has {available - storedLength} unexpected trailing bytes.");
        }

        if (payloadLength > maxPayloadLength)
        {
            throw new InvalidDataException($"Invalid {kind}: the payload length {payloadLength} is larger than the maximum {maxPayloadLength}.");
        }

        // Stored raw or inflated into a buffer of exactly the declared size
        byte[] payload;
        if ((flags & FlagDeflate) != 0)
        {
            payload = Decompress(file, headerLength, storedLength, payloadLength);
        }
        else
        {
            if (storedLength != payloadLength)
            {
                throw new InvalidDataException($"Invalid {kind}: an uncompressed payload must have equal stored and payload lengths.");
            }

            payload = new byte[payloadLength];
            Buffer.BlockCopy(file, headerLength, payload, 0, payloadLength);
        }

        if (Fnv1a(payload, 0, payload.Length) != checksum)
        {
            throw new InvalidDataException($"The {kind} is corrupt: the checksum does not match.");
        }

        return payload;
    }

    /// <summary>
    ///     Compresses bytes into a raw Deflate stream.
    /// </summary>
    /// <param name="data">The bytes.</param>
    /// <returns>The compressed bytes.</returns>
    public static byte[] Compress(byte[] data)
    {
        // The smallest-size level exists from .NET 6 on; older targets use the best level they have
#if NET6_0_OR_GREATER
        const CompressionLevel level = CompressionLevel.SmallestSize;
#else
        const CompressionLevel level = CompressionLevel.Optimal;
#endif
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, level, true))
        {
            deflate.Write(data, 0, data.Length);
        }

        return output.ToArray();
    }

    /// <summary>
    ///     Inflates a raw Deflate region into a buffer of an exactly known size.
    /// </summary>
    /// <param name="file">The buffer that holds the compressed region.</param>
    /// <param name="offset">The offset of the region.</param>
    /// <param name="count">The length of the region.</param>
    /// <param name="expectedLength">The declared uncompressed length.</param>
    /// <returns>The uncompressed bytes.</returns>
    /// <remarks>
    ///     <see cref="DeflateStream"/> reads its input in large chunks, so the position of a plain stream after the end
    ///     of the Deflate data says nothing about how many bytes the data really used. The region is therefore served
    ///     through a stream that withholds its last byte until every byte before it has been read: the inflater asks
    ///     for that last byte only when it still needs input, so the byte is read if and only if the Deflate data
    ///     extends to the end of the region. Bytes after the end of the Deflate data are never read and are rejected.
    /// </remarks>
    /// <exception cref="InvalidDataException">
    ///     The data is not valid Deflate, does not inflate to exactly the declared length, or does not fill the stored
    ///     region exactly (it ends early and leaves trailing bytes, or it is cut off).
    /// </exception>
    public static byte[] Decompress(byte[] file, int offset, int count, int expectedLength)
    {
        var result = new byte[expectedLength];
        using var source = new HoldBackStream(file, offset, count);
        using var deflate = new DeflateStream(source, CompressionMode.Decompress);

        // Fill the buffer completely; a short read means the stream ended early
        var filled = 0;
        while (filled < expectedLength)
        {
            var read = deflate.Read(result, filled, expectedLength - filled);
            if (read == 0)
            {
                throw new InvalidDataException("The compressed payload is shorter than declared.");
            }

            filled += read;
        }

        // Anything after the declared length means the header lies about the size
        if (deflate.ReadByte() >= 0)
        {
            throw new InvalidDataException("The compressed payload is longer than declared.");
        }

        // The Deflate data must also use the whole stored region; bytes that it never asked for are trailing garbage
        if (!source.IsExhausted)
        {
            throw new InvalidDataException("The compressed payload is followed by unexpected trailing bytes inside the stored region.");
        }

        return result;
    }

    /// <summary>
    ///     A read-only stream over a byte range that hands out its last byte only after every earlier byte was read, and
    ///     only to a read that still wants data.
    /// </summary>
    /// <remarks>
    ///     It lets <see cref="Decompress"/> tell whether a Deflate decoder needed the very last byte of its region
    ///     without relying on how far the decoder reads ahead.
    /// </remarks>
    private sealed class HoldBackStream : Stream
    {
        /// <summary>
        ///     The buffer that holds the range.
        /// </summary>
        private readonly byte[] _buffer;

        /// <summary>
        ///     The exclusive end of the range in <see cref="_buffer"/>.
        /// </summary>
        private readonly int _end;

        /// <summary>
        ///     The index of the next byte to hand out.
        /// </summary>
        private int _next;

        /// <summary>
        ///     Initializes a new instance of the <see cref="HoldBackStream"/> class.
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        /// <param name="offset">The offset of the range.</param>
        /// <param name="count">The length of the range.</param>
        public HoldBackStream(byte[] buffer, int offset, int count)
        {
            _buffer = buffer;
            _next = offset;
            _end = offset + count;
        }

        /// <summary>
        ///     Gets a value that indicates whether every byte of the range, including the last one, was read.
        /// </summary>
        public bool IsExhausted => _next == _end;

        /// <inheritdoc />
        public override bool CanRead => true;

        /// <inheritdoc />
        public override bool CanSeek => false;

        /// <inheritdoc />
        public override bool CanWrite => false;

        /// <inheritdoc />
        public override long Length => throw new NotSupportedException();

        /// <inheritdoc />
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count)
        {
            // Hand out everything but the last byte; the last byte only goes out alone, when all else was read
            var remaining = _end - _next;
            var n = remaining > 1 ? Math.Min(count, remaining - 1) : Math.Min(count, remaining);
            if (n <= 0)
            {
                return 0;
            }

            Buffer.BlockCopy(_buffer, _next, buffer, offset, n);
            _next += n;
            return n;
        }

        /// <inheritdoc />
        public override void Flush()
        {
        }

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /// <inheritdoc />
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>
    ///     Counts the set bits of a 64-bit word.
    /// </summary>
    /// <param name="value">The word.</param>
    /// <returns>The number of bits that are 1.</returns>
    public static int PopCount(ulong value)
    {
#if NET6_0_OR_GREATER
        return BitOperations.PopCount(value);
#else
        // Bit-parallel population count: pair, nibble and byte sums, then one multiply to add the bytes
        value -= (value >> 1) & 0x5555555555555555UL;
        value = (value & 0x3333333333333333UL) + ((value >> 2) & 0x3333333333333333UL);
        value = (value + (value >> 4)) & 0x0F0F0F0F0F0F0F0FUL;
        return (int)((value * 0x0101010101010101UL) >> 56);
#endif
    }

    /// <summary>
    ///     Compares two byte ranges lexicographically as unsigned bytes (the sort order of UTF-8 text).
    /// </summary>
    /// <param name="a">The first buffer.</param>
    /// <param name="aOffset">The offset of the first range.</param>
    /// <param name="aLength">The length of the first range.</param>
    /// <param name="b">The second buffer.</param>
    /// <param name="bOffset">The offset of the second range.</param>
    /// <param name="bLength">The length of the second range.</param>
    /// <returns>A negative number, zero or a positive number as the first range sorts before, equal to or after the second.</returns>
    public static int Compare(byte[] a, int aOffset, int aLength, byte[] b, int bOffset, int bLength)
    {
        var common = Math.Min(aLength, bLength);
        for (var i = 0; i < common; i++)
        {
            var difference = a[aOffset + i] - b[bOffset + i];
            if (difference != 0)
            {
                return difference;
            }
        }

        return aLength - bLength;
    }
}
