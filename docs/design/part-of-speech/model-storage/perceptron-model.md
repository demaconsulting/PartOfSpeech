### PerceptronModel

![ModelStorage Structure](ModelStorageView.svg)

#### Purpose

`PerceptronModel` reads, validates and scores with the weights of the averaged perceptron (format `POSW`, version 2),
and `PerceptronModelWriter` encodes them. The runtime only reads weights; the writer is used by the tests now and by
the trainer of a later release. The source files are `PerceptronModel.cs` and `PerceptronModelWriter.cs`.

#### Data Model

The model maps a feature bucket (the hash of one feature, below 2^`hashBits`) to a sparse row of integer weights, one
`int16` weight per tag that has one. Only occupied buckets have a row. The runtime structure, built once by `Load` and
immutable afterwards (safe for concurrent use):

- `ulong[] occupancy`: one bit per bucket (256 KiB at `hashBits` 21), set when the bucket has a row;
- `int[] rankBase`: for every 64-bit word of `occupancy`, the number of rows in all earlier words;
- `int[] rowStart`: the index of the first weight of each row, plus an end marker;
- `byte[] tags` and `short[] weights`: the tag and the quantized value of every weight, row after row;
- `HashBits`, `Temperature` (the softmax temperature in score units that calibrates confidences) and `Scale` (the factor
  from an integer score to the real score scale).

**File format `POSW` version 2**, little endian. The header is 48 bytes:

| Offset | Size | Field | Rule |
| ---: | ---: | --- | --- |
| 0 | 4 | magic | ASCII `POSW` |
| 4 | 2 | formatVersion | 2 |
| 6 | 2 | flags | bit 0: payload is raw Deflate |
| 8 | 2 | featureSetVersion | the feature set the weights were trained with |
| 10 | 1 | tagCount | 17 |
| 11 | 1 | hashBits | 18 to 24 |
| 12 | 4 | temperature | `float32`, finite and greater than zero |
| 16 | 4 | scale | `float32`, finite and greater than zero |
| 20 | 4 | rowCount | at most 2^hashBits |
| 24 | 4 | weightCount | from rowCount to 17 times rowCount |
| 28 | 4 | payloadLength | inflated payload bytes (exact), at most `PerceptronModel.MaxPayloadLength` (256 MiB) |
| 32 | 4 | storedLength | bytes after the header (the whole rest of the file) |
| 36 | 4 | checksum | FNV-1a 32 of the stored bytes (Deflate bytes, or the payload if the flag is clear) |
| 40 | 8 | reserved | zero |

The payload has four sections, stored column by column so that Deflate works well:

1. **Bucket gaps**: `rowCount` varints. The first bucket is the first gap minus 1; each later bucket is the previous
   bucket plus its gap; every gap is at least 1 (buckets ascend strictly) and every bucket is below 2^hashBits.
2. **Row sizes**: `rowCount` bytes, each 1 to 17; their sum equals `weightCount`.
3. **Tags**: `weightCount` bytes, each 0 to 16, strictly ascending within a row.
4. **Weights**: `weightCount` values of `int16`, never 0 and never -32768.

The payload length must therefore equal the gaps plus `rowCount` plus `weightCount` plus 2 times `weightCount`.

Version 2 replaced version 1 (never released) because a version 1 file could pass its checksum, which covered the
inflated payload, with the last byte of its Deflate data removed and the stored length adjusted; see the strict
Deflate validation in the `BinaryFormat` design.

#### Key Methods

- **`Load(Stream, int expectedFeatureSetVersion)` and `Load(byte[], int)`**: `BinaryFormat.ReadHeader` and
  `ReadPayload` (magic, version, flags, lengths, checksum of the stored bytes, strict Deflate validation; the payload
  length is checked against the constant
  `MaxPayloadLength` of 256 MiB, a per-format bound that does not depend on the equally untrusted row and weight
  counts), then the feature set number (must equal the expected value;
  the caller, not this unit, knows the extractor), the tag count, `hashBits`, temperature, scale, counts and reserved
  bytes, then the payload columns; every ordering and range rule above is checked while the structure is built, so
  `AddScores` needs no checks.
- **`AddScores(int bucket, int[] scores)`**: the innermost operation of the decoder. Tests the occupancy bit; when it
  is set, the row index is `rankBase[word]` plus the population count of the lower bits of the word, and the weights of
  that row are added to `scores[tag]`. A bucket without a row adds nothing. Precondition: `0 <= bucket < 2^HashBits`
  and `scores` has 17 elements.
- **`PerceptronModelWriter.Compile(hashBits, temperature, scale, featureSetVersion, rows, compress)` and `Write`**:
  takes the occupied buckets as a bucket and a dense 17-element `int16` vector each, orders them by bucket, skips rows
  whose weights are all zero, and writes the sparse layout above. Postcondition: the file loads with the same weights.

Size note for the later release: the spike's sparse `int16` model had about 105,000 rows and 286,000 weights, which is
about 1.07 MB uncompressed and 0.6 MB with Deflate; the trainer chooses `hashBits` and pruning to stay near 1 MB.

#### Error Handling

All structural defects (wrong magic, unsupported version, feature set mismatch, wrong tag count, `hashBits` outside 18 to
24, non-positive or non-finite temperature or scale, non-zero reserved bytes, inconsistent counts, checksum, truncation,
trailing bytes (after the file or inside the stored region), a Deflate stream that is cut off or inflates to a size other
than declared, zero gap, out-of-range bucket, row size 0 or above 17, tags out of order, weight 0 or -32768) throw
`InvalidDataException` when the file is loaded. `Load` throws `ArgumentNullException` for `null`. The writer throws
`ArgumentNullException`, `ArgumentOutOfRangeException` (numeric parameters, bucket out of range, feature set above 16
bits) or `ArgumentException` (wrong vector length, weight -32768, repeated bucket).

#### Dependencies

- **BinaryFormat** (validation, varints, checksum, Deflate, population count) and **Upos** (`UposExtensions.Count`).
- It does not depend on `FeatureExtractor`: the expected feature set version is an argument.

#### Callers

- **EmbeddedModel** (`Load` of the embedded weights, when they exist) and **Tagger** (`Load`).
- **TagDecoder** (`AddScores`, `HashBits`, `Temperature`, `Scale`).
- The test suite and, in a later release, the trainer (`PerceptronModelWriter`).
