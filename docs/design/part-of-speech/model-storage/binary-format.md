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
The payload of a format is stored after its fixed header, raw or as a raw Deflate stream (no zlib or gzip wrapper). Both
formats are at version 2: version 1 (unreleased) put a checksum over the inflated payload, which let a truncated
Deflate stream pass; version 2 checksums the stored bytes and the reader inflates strictly (see Key Methods). Version 1
files are refused as an unsupported version.

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
  that the stored length equals the remaining file length exactly (shorter means truncated, longer means trailing
  bytes), rejects a payload length above the format's `maxPayloadLength` before any inflation (a guard against
  decompression bombs; the message names the limit), requires equal stored and payload lengths for a raw payload,
  verifies the FNV-1a checksum of the **stored bytes** (format version 2: the Deflate bytes of a compressed file, the
  payload of a raw one) before anything is inflated, and only then restores the payload (raw copy, or strict
  inflation into a buffer of exactly the declared size, see `Decompress`).
- **`Compress(byte[])`**: raw Deflate through `DeflateStream`. Compression uses the smallest-size level where the target
  framework has it (.NET 6 and later) and the optimal level otherwise; the compressed bytes therefore differ between
  runtimes, which is why compatibility checks compare uncompressed payloads. An empty input, for which some runtimes
  write no byte at all, is stored as the complete two-byte stream of one empty final block (`03 00`), so that every
  stored region holds a complete stream and a missing stream is never mistaken for an empty payload.
- **`Decompress(file, offset, count, expectedLength)`**: inflates a region strictly. It accepts the region if and only
  if all of the following hold, and otherwise throws `InvalidDataException`: (1) the data is valid Deflate; (2) it
  inflates to exactly `expectedLength` bytes (a stream that ends earlier is "shorter than declared", one with more data
  is "longer than declared"; only `expectedLength + 1` bytes are ever inflated, so a hostile stream cannot allocate or
  produce more than the capped declared size); (3) the Deflate stream reaches its own end (a final block with its
  end-of-block code) **inside** the region; (4) the Deflate data uses the region to its last byte, so no byte follows
  it. Any exception of another type that the inflater raises for bad data (`InvalidOperationException`,
  `ArgumentException`, `IOException`, `IndexOutOfRangeException`, `NotSupportedException`) is wrapped into
  `InvalidDataException`.
- **`PopCount(ulong)`**: `BitOperations.PopCount` on .NET 6 and later, and a bit-parallel software count on
  `netstandard2.0`.
- **`Compare(byte[], int, int, byte[], int, int)`**: lexicographic comparison of byte ranges as unsigned bytes, the sort
  order of UTF-8 text, used by the lexicon search.
- **`ReadAllBytes(Stream)`**: reads a stream from its current position to its end; throws `ArgumentNullException` for
  `null`.

##### Strict Deflate validation

`DeflateStream` has no way to ask whether the stream ended properly: a stream whose last byte is missing can deliver
all of its data (the last byte often holds only the end-of-block code and padding) and then report the end of data
exactly as a complete stream does, on some runtimes silently and on others after a delay. It also reads its input in
chunks, so the position of a plain source stream after the end says nothing about the bytes that the data used. The
first reviewed implementation withheld the last byte only; it caught trailing bytes but not a stream cut off after its
last data, so a file whose stored length was reduced by one byte passed.

The region is therefore served through a private `RegionStream` that observes what the inflater asks for. It hands out
the bytes of the region, withholds the **last** byte until every earlier byte was read and a read still wants data, and
records a read that asks for data after the last byte was handed out. An inflater reads more input if and only if it
has consumed all input it was given and still needs data, which gives two facts that do not depend on read-ahead sizes:

- a read **beyond the region** means the inflater was starved: the stream has no end of stream inside the region (it is
  cut off, at any point, including a missing last byte), rejected with "cut off";
- an **unread last byte** after the output was read to its declared length and the end-of-data check (`ReadByte`) was
  made means the stream ended earlier and the rest is trailing garbage, rejected with "trailing bytes".

A complete stream that fills the region exactly (as every file the writers produce) never makes a read beyond the
region and always requests the last byte, so it is accepted. The behavior was verified on `net481` (the managed
inflater), `net8.0`, `net9.0` and `net10.0` (zlib-based inflaters) with the regression tests of the three units; the
library itself is `netstandard2.0` and only uses `Stream.Read`, so the same contract applies to every host. If a host
inflater asked for input differently, the result would be a rejected good file (the tests of that host would show it),
never an accepted bad one.

##### Format version 2 and the integrity model

The version 2 checksum covers the stored bytes, not the inflated payload. A truncation or any other change of the
compressed region, and any trailing bytes inside it, therefore fail the checksum before a byte is inflated, and the
inflater is never run on damaged data. The header lengths need no checksum of their own because each is checked against
an independent measurement: the stored length against the file size, the payload length against the cap and against the
inflated size (both too large and too small are rejected), and the reserved and count fields against the structure of
the payload. A forged file whose checksum was recomputed after tampering is outside the integrity model (FNV-1a is not
a cryptographic hash), but it is still rejected whenever its Deflate stream is incomplete or has trailing bytes, because
the strict inflation does not depend on the checksum.

Alternatives that were rejected: a sentinel final block appended by the writer (the `Flush` that produces a known
marker does nothing on .NET Framework, so the writer could not produce it on every target); a terminator inside the
payload (the missing byte can be one that holds no payload data, so the terminator is inflated before the cut); and a
hand-written inflater (about 150 lines of security-sensitive code that the project would have to own and review, for
a property that the observed read behavior already guarantees).

#### Error Handling

Every structural defect of a file is reported as `InvalidDataException` with a message that names the kind of file and
the defect (wrong magic, unsupported version, unknown flags, truncated, trailing bytes, checksum mismatch, bad
Deflate, Deflate stream cut off, Deflate data shorter or longer than declared). No other exception type, no unbounded
allocation and no loop that depends on the data can occur: the output buffer has the capped declared size, and the
inflation loop ends when the inflater returns no data. Argument errors (`null` stream) are `ArgumentNullException`.
Nothing is caught inside the unit except the inflater's non-data exceptions that are wrapped as described above; the
readers let the exception reach the caller of `Tagger.Load`.

#### Dependencies

- `System.IO.Compression.DeflateStream` of the base class library; no OTS item and no shared package.

#### Callers

- **Lexicon** and **PerceptronModel** (readers and writers) and **EmbeddedModel** (through the readers).
- The test suite, which exercises the primitives directly.
