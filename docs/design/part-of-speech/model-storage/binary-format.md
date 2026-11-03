### BinaryFormat

![ModelStorage Structure](ModelStorageView.svg)

#### Purpose

`BinaryFormat` holds the primitives that the weights format and the lexicon format share, so that both readers and
writers validate files in the same way. It is an internal static class with no state.

#### Data Model

There is no instance data. The constants are the FNV-1a 32-bit offset basis (2166136261) and prime (16777619), the
`FlagDeflate` bit (value 1, bit 0 of the `flags` field). The largest accepted payload length is not a constant of this
class: each format passes its own maximum to `ReadPayload` (`Lexicon.MaxPayloadLength` and
`PerceptronModel.MaxPayloadLength`).

Fixed header fields that both formats place at the same offsets: bytes 0 to 3 the ASCII magic; bytes 4 to 5 the format
version (`uint16`); bytes 6 to 7 the flags (`uint16`, only bit 0 is defined). All multi-byte integers are little endian.
The payload of a format is stored after its fixed header, raw or as a raw Deflate stream (no zlib or gzip wrapper).

#### Key Methods

- **`ReadUInt16`, `ReadUInt32`, `ReadSingle`, `WriteUInt16`, `WriteUInt32`, `WriteSingle`**: read and write
  little-endian values at an offset by shifting bytes, so that the result does not depend on the processor.
  `ReadInt32Field` reads a `uint32` count and throws `InvalidDataException` naming the field when it exceeds
  `Int32.MaxValue`.
- **`WriteVarint(Stream, uint)` and `ReadVarint(byte[], ref int, int)`**: unsigned LEB128, seven bits per byte with the
  high bit as the continuation flag. The reader refuses a varint that runs past the end of its region (truncated) and
  one that needs more than 32 bits (overlong, including a fifth byte above `0x0F`).
- **`ReadVarintInt32(byte[], ref int, int)`**: `ReadVarint` for a varint that is used as a length or an index. It throws
  `InvalidDataException` ("the value is larger than 2147483647") when the value does not fit a 32-bit signed integer, so
  that a cast can never produce a negative number that passes a range check.
- **`Fnv1a(byte[], int, int)`**: FNV-1a 32-bit over a byte range: for each byte, `hash = (hash xor byte) * prime`.
- **`ReadHeader(byte[] file, string magic, int headerLength, int supportedVersion, string kind)`**: checks, in this
  order, the magic (so that a file of another kind is reported as such even when it is short), the header length, the
  version and the flag bits; returns the flags. Postcondition: the first eight bytes of the header are valid.
- **`ReadPayload(file, headerLength, flags, payloadLength, maxPayloadLength, storedLength, checksum, kind)`**: requires
  that the stored length equals the remaining file length exactly (shorter means
  truncated, longer means trailing bytes), rejects a payload length above the format's `maxPayloadLength` before any
  inflation (a guard against decompression bombs; the message names the limit), restores the payload (raw copy, or
  inflate into a
  buffer of exactly the declared size), and verifies the FNV-1a checksum of the uncompressed payload.
- **`Compress(byte[])` and `Decompress(...)`**: raw Deflate through `DeflateStream`. Compression uses the smallest-size
  level where the target framework has it (.NET 6 and later) and the optimal level otherwise; the compressed bytes
  therefore differ between runtimes, which is why compatibility checks compare uncompressed payloads. Decompression
  refuses a stream that ends before or continues after the declared length, and a stored region that is not used
  exactly: bytes after the end of the Deflate data are not covered by the payload checksum (it is computed over the
  uncompressed bytes), so they must be detected here. `DeflateStream` reads its input ahead in chunks on every
  target framework (and differently on `netstandard2.0` running on .NET Framework, the .NET runtime and zlib-based
  builds), so neither the position of a `MemoryStream` after the end nor a re-compression is a reliable test.
  (Re-deflating and comparing is not acceptable: the output differs between runtimes.) The method instead reads the
  region through a private `HoldBackStream` that never hands out the last byte of the region unless a read still wants
  data after every earlier byte was read. An inflater asks for input only when it has not finished, so the last byte is
  requested if and only if the Deflate data extends to it; after the output was read to its declared length and the
  end-of-data check (`ReadByte`) was made, an unread last byte proves that the data ended earlier, and the region is
  rejected with a "trailing bytes" `InvalidDataException`. A region that is used exactly, as every file the writers
  produce, is accepted on every target framework. Known limit: a stream that is cut off inside its last block is not
  detectable through `DeflateStream` on every runtime (some return end of data silently); the payload checksum and the
  declared output length catch such files, and the empty payload (which .NET compresses to zero bytes) cannot be told
  from a cut-off stream, which is why no format payload is empty.
- **`PopCount(ulong)`**: `BitOperations.PopCount` on .NET 6 and later, and a bit-parallel software count on
  `netstandard2.0`.
- **`Compare(byte[], int, int, byte[], int, int)`**: lexicographic comparison of byte ranges as unsigned bytes, the sort
  order of UTF-8 text, used by the lexicon search.
- **`ReadAllBytes(Stream)`**: reads a stream from its current position to its end; throws `ArgumentNullException` for
  `null`.

#### Error Handling

Every structural defect of a file is reported as `InvalidDataException` with a message that names the kind of file and
the defect (wrong magic, unsupported version, unknown flags, truncated, trailing bytes, checksum mismatch, bad
Deflate). Argument errors (`null` stream) are `ArgumentNullException`. Nothing is caught inside the unit; the readers
let the exception reach the caller of `Tagger.Load`.

#### Dependencies

- `System.IO.Compression.DeflateStream` of the base class library; no OTS item and no shared package.

#### Callers

- **Lexicon** and **PerceptronModel** (readers and writers) and **EmbeddedModel** (through the readers).
- The test suite, which exercises the primitives directly.
