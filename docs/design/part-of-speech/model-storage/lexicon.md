### Lexicon

![ModelStorage Structure](ModelStorageView.svg)

#### Purpose

`Lexicon` reads, searches and writes the compiled word lexicon (format `POSL`, version 2) and applies the token-shape
rules that turn the lexicon tags of a word into the tags that the tagger may choose. The source files are `Lexicon.cs`
(reader, search, shape rules) and `LexiconWriter.cs` (parser of the source table and compiler; build-time and test code
that the runtime never calls).

#### Data Model

**Runtime object** (`Lexicon`, immutable after `Load`, safe for concurrent use): the verified uncompressed payload,
the mask palette (`int[]`), the byte offset of every block (`int[]`), the block shift, and the counts
`SpellingCount`, `EntryCount`, `MaxSpellingBytes`, `BlockCount` and `MaskCount`. `ScratchSize` is twice
`MaxSpellingBytes`; it is the size of the buffer that a lookup needs (the encoded key and the rebuilt spelling).

**File format `POSL` version 2**, little endian. The header is 40 bytes:

| Offset | Size | Field | Rule |
| ---: | ---: | --- | --- |
| 0 | 4 | magic | ASCII `POSL` |
| 4 | 2 | formatVersion | 2 |
| 6 | 2 | flags | bit 0: payload is raw Deflate |
| 8 | 1 | tagCount | 17 |
| 9 | 1 | blockShift | 1 to 8 (4 gives 16 spellings per block) |
| 10 | 2 | maskCount | distinct tag masks in the palette |
| 12 | 4 | spellingCount | number of spellings |
| 16 | 4 | entryCount | total number of tag entries |
| 20 | 4 | blockCount | ceil(spellingCount / 2^blockShift) |
| 24 | 4 | payloadLength | inflated payload bytes (exact), at most `Lexicon.MaxPayloadLength` (16 MiB) |
| 28 | 4 | storedLength | bytes after the header (the whole rest of the file) |
| 32 | 4 | checksum | FNV-1a 32 of the stored bytes (Deflate bytes, or the payload if the flag is clear) |
| 36 | 2 | maxSpellingBytes | longest UTF-8 spelling |
| 38 | 2 | reserved | zero |

The payload has three parts. (1) The mask palette: `maskCount` values of `uint32`, bit _i_ for tag _i_, only the low
17 bits, non-zero, distinct, sorted ascending. (2) The block index: `blockCount` values of `uint32`, byte offsets into the
data region, ascending, the first 0. (3) The data region. The first spelling of a block is stored whole:
`varint length, UTF-8 bytes, varint maskIndex`. Every other spelling is front-coded against the one before it:
`varint sharedPrefixBytes, varint suffixLength, suffix bytes, varint maskIndex`. Spellings are strictly ascending in UTF-8
byte order (the sort order of the source table), unique, lower case, NFC and without space or tab. Every varint that is
used as a length or an index (`sharedPrefixBytes`, `suffixLength`, `maskIndex` and the head length) is read with
`BinaryFormat.ReadVarintInt32`, so a value above 2^31-1 is reported as `InvalidDataException` and never wraps to a
negative number.

For the committed lexicon the file is 641,418 bytes (262,667 spellings, 339,880 entries, 230 masks, 16 spellings per
block, Deflate). The change from version 1 to version 2 (the checksum covers the stored bytes) did not change the
size: only the version and checksum fields differ.

Version 2 replaced version 1 because a version 1 file could pass its checksum (computed over the inflated payload) with
the last byte of its Deflate data removed and the stored length adjusted; see the strict Deflate validation in the
`BinaryFormat` design. Version 1 was never released.

#### Key Methods

- **`Load(Stream)` and `Load(byte[])`**: `BinaryFormat.ReadHeader` and `ReadPayload` (with the maximum payload length
  `MaxPayloadLength`, 16 MiB, which is far above the 1,635,304 bytes of the committed lexicon; the stored-bytes
  checksum and the strict Deflate validation are described in the `BinaryFormat` design), then the header fields
  (tag count
  17, block shift 1 to 8, block count equal to the ceiling formula, reserved field zero), the palette and block index,
  and `Validate`, which walks every spelling once and checks that spellings ascend strictly, prefixes and lengths fit,
  mask indexes exist and the counts equal the header. A lexicon that loads cannot fail in `TryGetTags`.
- **`TryGetTags(char[] chars, int offset, int length, byte[] scratch, out UposSet tags)`**: looks up the folded,
  lower-cased spelling in a region of a character buffer (the decoder keeps all tokens of a sentence in one buffer, hence
  the `offset`). The key is encoded to UTF-8 in `scratch` (an over-long key returns `false` at once); a binary search
  over the block heads (stored whole, compared in place) finds the block whose head is the last one not greater than the
  key; a linear scan rebuilds each following spelling in the scratch buffer and compares bytes. Allocation-free.
- **`AllowedTags(bool known, UposSet lexiconTags, bool hasLetterOrDigit, bool hasDigit, bool firstUpper)`**: the
  token-shape rules of `data/lexicon/README.md`. An unknown spelling allows all 17 tags. Otherwise start from the lexicon
  tags and add PUNCT, SYM and X when the token has no letter or digit; NUM, SYM, X, NOUN, PROPN and ADJ when it has a
  digit; PROPN and X when its first character is an upper-case letter. The result is never empty. The three shape sets
  are precomputed `static readonly` `UposSet` values (not built with `UposSet.Of(params ...)` per call), so this path
  allocates nothing; it runs for every known token of a sentence.
- **`AllowedTags(string token, Lexicon lexicon)`**: computes the shape facts with `char.IsLetterOrDigit`,
  `char.IsDigit` and `char.IsUpper` and applies the rules; used by tests so that the decoder's allowed set can be
  compared with an independent reading of the rules.
- **`Enumerate()` and `ToUncompressedImage(byte[])`**: decode every spelling with its tags; return the uncompressed
  file image (the header with the Deflate flag cleared, the stored length set to the payload length and the checksum
  recomputed over the payload, then the payload), which `LexiconCompiler --check` compares because the Deflate bytes
  depend on the runtime.
- **`LexiconWriter.FromTsv(IEnumerable<string>)`**: parses the source table: a comment line starts with `#`, a row is a
  lower-case NFC spelling without space, a tab and the tags in alphabetical order separated by spaces; spellings must
  ascend strictly in UTF-8 byte order; a header comment that records `spellings=N, tag entries=M` is checked against
  the counts. Throws `InvalidDataException` naming the line.
- **`LexiconWriter.Compile(entries, compress, blockShift = 4)` and `Write`**: validates again (empty spelling or tag set,
  order, more than 65,535 masks or a longer spelling), builds the palette, the block index and the front-coded data, and
  writes the header and payload. The uncompressed output is deterministic.

#### Error Handling

Loading reports every defect as `InvalidDataException` (through `BinaryFormat` for the shared checks, and directly for
structure). `Load(Stream)` throws `ArgumentNullException` for `null`. `TryGetTags` never throws on a loaded lexicon; a key
that cannot be in the lexicon returns `false`. `FromTsv` and `Compile` throw `InvalidDataException`,
`ArgumentException` or `ArgumentOutOfRangeException` for table and parameter errors, so that a bad table fails at
compile time and not at lookup time.

#### Dependencies

- **BinaryFormat** (validation, varints, checksum, Deflate, byte comparison) and **Upos** (`UposSet`, tag parsing), same
  system.
- Base class library only (`System.Text.Encoding`, `Regex` in the writer).

#### Callers

- **EmbeddedModel** (`Load`) and **Tagger** (`Load(Stream, Stream, ...)`).
- **TagDecoder** (`TryGetTags`, `AllowedTags`).
- The tool `LexiconCompiler` (`FromTsv`, `Compile`, `ToUncompressedImage`) and the test suite.
