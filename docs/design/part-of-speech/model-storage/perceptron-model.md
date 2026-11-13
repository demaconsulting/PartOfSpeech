### PerceptronModel

![ModelStorage Structure](ModelStorageView.svg)

#### Purpose

`PerceptronModel` reads, validates and scores with the weights of the averaged perceptron (format `POSW`, version 2),
and `PerceptronModelWriter` encodes them. The runtime only reads weights; the writer is used by the trainer and the
tests. The weights file also names the feature groups that the weights were trained with (`FeatureGroups`), which the
decoder follows. The source files are `PerceptronModel.cs` and `PerceptronModelWriter.cs`.

#### Data Model

The model maps a feature bucket (the hash of one feature, below 2^`hashBits`) to a sparse row of integer weights, one
`int16` weight per tag that has one. Only occupied buckets have a row. The runtime structure, built once by `Load` and
immutable afterwards (safe for concurrent use):

- `ulong[] occupancy`: one bit per bucket (256 KiB at `hashBits` 21), set when the bucket has a row;
- `int[] rankBase`: for every 64-bit word of `occupancy`, the number of rows in all earlier words;
- `int[] rowStart`: the index of the first weight of each row, plus an end marker;
- `byte[] tags` and `short[] weights`: the tag and the quantized value of every weight, row after row;
- `HashBits`, `Temperature` (the softmax temperature in score units that calibrates confidences), `Scale` (the factor
  from an integer score to the real score scale) and `FeatureGroups` (the groups of context features, see
  `FeatureExtractor`; `None` for the 22 base templates).

**File format `POSW` version 2**, little endian. The header is 48 bytes:

| Offset | Size | Field | Rule |
| ---: | ---: | --- | --- |
| 0 | 4 | magic | ASCII `POSW` |
| 4 | 2 | formatVersion | 2 |
| 6 | 2 | flags | bit 0: payload is raw Deflate |
| 8 | 2 | featureSetVersion | the feature set of the weights: 1 without feature groups, 2 with groups |
| 10 | 1 | tagCount | 17 |
| 11 | 1 | hashBits | 18 to 24 |
| 12 | 4 | temperature | `float32`, finite and greater than zero |
| 16 | 4 | scale | `float32`, finite and greater than zero |
| 20 | 4 | rowCount | at most 2^hashBits |
| 24 | 4 | weightCount | from rowCount to 17 times rowCount |
| 28 | 4 | payloadLength | inflated payload bytes (exact), at most `PerceptronModel.MaxPayloadLength` (256 MiB) |
| 32 | 4 | storedLength | bytes after the header (the whole rest of the file) |
| 36 | 4 | checksum | FNV-1a 32 of the stored bytes (Deflate bytes, or the payload if the flag is clear) |
| 40 | 4 | featureGroups | `uint32` mask of `FeatureGroups` (bits 0 to 3), 0 without groups; other bits rejected |
| 44 | 4 | reserved | zero |

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

**Feature group mask and compatibility.** The mask (bit 0 position cues, bit 1 clause cues, bit 2 right preview,
bit 3 inflection, bit 4 title-case line; bit 5 and every higher bit are reserved for later groups) occupies the first
four of the eight reserved bytes (zero in a file without groups), so the format version stays 2 and
the layout is unchanged. The feature set number
follows from the mask: 1 if and only if the mask is 0, otherwise 2. A file without groups therefore keeps all eight reserved
bytes at zero and feature set 1, and this build reads it (backward compatible). A reader that does not know the
mask fails loudly on a file with groups, twice over: it expects feature set 1 and finds 2, and it finds
non-zero reserved bytes; so it never tags with the wrong features (not forward compatible by design).

#### Key Methods

- **`Load(Stream)` and `Load(byte[])`**: load a file of any supported feature set: feature set 1 with the mask 0, or
  feature set 2 with a non-zero mask of known groups. **`Load(Stream, int expectedFeatureSetVersion)` and
  `Load(byte[], int)`** additionally require the feature set number to equal the expected value (`FeatureSetVersion`
  accepts only models without groups). All four use `BinaryFormat.ReadHeader` and
  `ReadPayload` (magic, version, flags, lengths, checksum of the stored bytes, strict Deflate validation; the payload
  length is checked against the constant
  `MaxPayloadLength` of 256 MiB, a per-format bound that does not depend on the equally untrusted row and weight
  counts), then the feature set number, the tag count, `hashBits`, temperature, scale, counts, the group mask (known
  bits only, agreeing with the feature set number) and the reserved bytes, then the payload columns; every ordering and
  range rule above is checked while the structure is built, so `AddScores` needs no checks.
- **`AddScores(int bucket, int[] scores)`**: the innermost operation of the decoder. Tests the occupancy bit; when it
  is set, the row index is `rankBase[word]` plus the population count of the lower bits of the word, and the weights of
  that row are added to `scores[tag]`. A bucket without a row adds nothing. Precondition: `0 <= bucket < 2^HashBits`
  and `scores` has 17 elements.
- **`PerceptronModelWriter.Compile(hashBits, temperature, scale, featureSetVersion, rows, compress, featureGroups = 0)`
  and `Write`**: takes the occupied buckets as a bucket and a dense 17-element `int16` vector each, orders them by
  bucket, skips rows whose weights are all zero, and writes the sparse layout above, with the group mask at offset 40.
  The default mask 0 writes zero at that offset, the layout of a model without feature groups. The caller chooses the
  feature set number (`FeatureExtractor.FeatureSetVersionFor(groups)`); the writer refuses unknown mask bits.
  Postcondition: the file loads with the same weights.

- **`ToUncompressedImage(byte[] file)`**: validates the file completely with `Load` (any supported feature set, because
  a stale or foreign feature set is what a staleness check must report as invalid), then returns the header with the
  compression flag cleared, the payload and stored lengths replaced by the payload length, the checksum recomputed over
  the payload (the group mask is kept), followed by the inflated payload. The Deflate bytes differ between runtimes but
  this image is a function of the weights only, so the trainer compares it (and its SHA-256) to decide whether a
  committed file is current. Postcondition: the compressed and the plain form of the same weights give the same image.
  An invalid file throws `InvalidDataException`.

Size note: the shipped model (selected on development data, see _ModelTrainer Design_) has 129,882 rows and 324,906
weights at 19 hash bits and scale 1/8, which is 1,234,530 bytes uncompressed and 553,641 bytes with Deflate.

#### Error Handling

All structural defects (wrong magic, unsupported version, feature set mismatch or a feature set other than 1 or 2, a
group mask with an unknown bit or one that disagrees with the feature set, wrong tag count, `hashBits` outside 18 to
24, non-positive or non-finite temperature or scale, non-zero reserved bytes, inconsistent counts, checksum, truncation,
trailing bytes (after the file or inside the stored region), a Deflate stream that is cut off or inflates to a size other
than declared, zero gap, out-of-range bucket, row size 0 or above 17, tags out of order, weight 0 or -32768) throw
`InvalidDataException` when the file is loaded. `Load` throws `ArgumentNullException` for `null`. The writer throws
`ArgumentNullException`, `ArgumentOutOfRangeException` (numeric parameters, bucket out of range, feature set above 16
bits, unknown group bit) or `ArgumentException` (wrong vector length, weight -32768, repeated bucket).

#### Dependencies

- **BinaryFormat** (validation, varints, checksum, Deflate, population count) and **Upos** (`UposExtensions.Count`).
- It depends on **FeatureExtractor** only for constants (`FeatureSetVersion`, `GroupsFeatureSetVersion`,
  `FeatureSetVersionFor`, `KnownGroupsMask`) and the `FeatureGroups` type, so there is no call cycle; with the
  expected-version overloads the caller, not this unit, knows the extractor.

#### Callers

- **EmbeddedModel** (`Load` of the embedded weights) and **Tagger** (`Load`).
- **TagDecoder** (`AddScores`, `HashBits`, `Temperature`, `Scale`, `FeatureGroups`).
- The test suite and the trainer (`PerceptronModelWriter`, `ToUncompressedImage`).
