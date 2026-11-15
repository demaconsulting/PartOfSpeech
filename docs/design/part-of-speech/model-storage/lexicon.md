### Lexicon

![ModelStorage Structure](ModelStorageView.svg)

#### Purpose

`Lexicon` reads, searches and writes the compiled word lexicon (format `POSL`, version 3), reads the analyses of a
spelling (the base word, tag and features of each form that the spelling can be) and applies the token-shape rules
that turn the lexicon tags of a word into the tags that the tagger may choose. The source files are `Lexicon.cs`
(reader, search, analyses, shape rules) and `LexiconWriter.cs` (parser of the source tables and compiler; build-time
and test code that the runtime never calls).

#### Data Model

**Runtime object** (`Lexicon`, immutable after `Load`, safe for concurrent use): the verified uncompressed payload,
the mask palette (`int[]`), the byte offset of every block (`int[]`), the block shift, the validated analyses tables
(feature sets, templates, lists, and the position of the list index), and the counts `SpellingCount`, `EntryCount`,
`MaxSpellingBytes`, `BlockCount`, `MaskCount`, `AnalysisCount`, `FeatureSetCount`, `TemplateCount` and `ListCount`.
`ScratchSize` is twice `MaxSpellingBytes`; it is the size of the buffer that a lookup needs (the encoded key and the
rebuilt spelling).

**File format `POSL` version 3**, little endian. The header is 56 bytes (`Lexicon.HeaderLength`):

| Offset | Size | Field | Rule |
| ---: | ---: | --- | --- |
| 0 | 4 | magic | ASCII `POSL` |
| 4 | 2 | formatVersion | 3 |
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
| 40 | 2 | featureSetCount | feature sets of the analyses, at least 1 (the empty set) |
| 42 | 2 | reserved | zero |
| 44 | 4 | templateCount | analysis templates |
| 48 | 4 | listCount | template lists, 1 to 65,535 (list 0 is the empty list) |
| 52 | 4 | analysisCount | number of analyses of all spellings (the sum of the list sizes over the spellings) |

The payload has seven parts. (1) The mask palette: `maskCount` values of `uint32`, bit _i_ for tag _i_, only the low
17 bits, non-zero, distinct, sorted ascending. (2) The block index: `blockCount` values of `uint32`, byte offsets into the
data region, ascending, the first 0. (3) The data region. The first spelling of a block is stored whole:
`varint length, UTF-8 bytes, varint maskIndex`. Every other spelling is front-coded against the one before it:
`varint sharedPrefixBytes, varint suffixLength, suffix bytes, varint maskIndex`. Spellings are strictly ascending in UTF-8
byte order (the sort order of the source table), unique, lower case, NFC and without space or tab. Every varint that is
used as a length or an index (`sharedPrefixBytes`, `suffixLength`, `maskIndex` and the head length) is read with
`BinaryFormat.ReadVarintInt32`, so a value above 2^31-1 is reported as `InvalidDataException` and never wraps to a
negative number. Parts (1) to (3) are byte for byte what the tag lookup reads; it never reads parts (4) to (7).

The analyses section follows the data region (its start is found by walking the last block):

- (4) The feature-set table: `featureSetCount` entries of `varint length, ASCII bytes`. A feature set is text in
  Universal Dependencies notation (`Name=Value` pairs joined by `|`, names ascending, for example
  `Person=3|Tense=Pres`). Entry 0 is the empty text; the entries are strictly ascending (so they are distinct).
- (5) The template palette: `templateCount` entries of `uint8 tag, varint featureSetIndex, varint cut, varint
  suffixLength, suffix bytes`. A template is the edit that turns the spelling of a form into its base word: keep the
  spelling without its last `cut` UTF-8 bytes and append the suffix. `test` for `tests` is cut 1 with an empty suffix;
  `go` for `went` is cut 4 with the suffix `go`; a base word equal to the spelling is cut 0 with an empty suffix.
  Templates are distinct.
- (6) The list palette: `listCount` entries of `varint count, count x varint templateIndex`. List 0 is empty and no
  other list is; a list names at most 255 templates, each at most once, with tags that never decrease; lists are
  distinct. The analyses of a spelling are in the order (tag, base word, features): the base word and the features of
  two templates with one tag decide their order for the spelling (see the validation below).
- (7) The list index: `spellingCount` values of `uint16`, the list of spelling _i_ (in ascending order) at offset
  2 x _i_ from the start of the index, which closes the payload.

A spelling with no analysis has list 0. A lexicon compiled without an analyses table is a version 3 file with one
feature set (the empty one), no template, one list (the empty one) and every spelling on list 0. The base word of an
analysis is lower case NFC text of 1 to 255 UTF-8 bytes; the features are the subset of UD features that the source
tables record (`Degree`, `Number`, `Person`, `Tense`, `VerbForm`).

For the committed lexicon the file is 762,836 bytes (262,635 spellings, 339,819 tag entries, 225 masks, 16 spellings
per block, Deflate). Its inflated payload is 2,157,443 bytes (the `payloadLength` field); the uncompressed file image,
the 56-byte header plus that payload, is 2,157,499 bytes, which is the "uncompressed" size quoted elsewhere. The
analyses section holds 290,365 analyses of 258,255 spellings (4,380 spellings have none) in 14 feature sets, 414
templates and 804 lists (including the empty one); it accounts for 531,214 bytes of the inflated payload and 123,125
bytes of the stored file. The NuGet package holds the resource once per target framework. Creating `Tagger.Default`
(inflating, validating every entry and every analysis, and building the tables) takes about 50 ms on the first access
in a cold process (.NET 10, measured), and about 25 ms for a warm in-process `Tagger.Load(weights, lexicon)`.

The checksum covers the stored bytes, so a file whose Deflate data was cut cannot pass it; see the strict Deflate
validation in the `BinaryFormat` design. The reader accepts exactly version 3: any other version, including 2, is
refused with `Unsupported lexicon file version 2; this build reads version 3.` (the text names the version found). The
only tool that writes lexicon files, `LexiconCompiler`, is not shipped, so no released file has another version.

#### Key Methods

- **`Load(Stream)` and `Load(byte[])`**: `BinaryFormat.ReadHeader` and `ReadPayload` (with the maximum payload length
  `MaxPayloadLength`, 16 MiB, which is far above the 2,157,443 payload bytes of the committed lexicon; the stored-bytes
  checksum and the strict Deflate validation are described in the `BinaryFormat` design), then the header fields
  (tag count 17, block shift 1 to 8, block count equal to the ceiling formula, the reserved fields zero, at least one
  feature set and one list, at most 65,535 lists), the palette and block index, the analyses section, and `Validate`,
  which walks every spelling once and checks that spellings ascend strictly, prefixes and lengths fit, mask indexes
  exist and the counts equal the header. A lexicon that loads cannot fail in `TryGetTags`, `TryFindKey`,
  `AnalyzedTags`, `GetAnalyses` or `HasAnalysis`. Counts are checked against the payload before any array is sized
  from them, so a hostile header cannot make the reader allocate more than the payload justifies.
- **Analyses validation** (all `InvalidDataException`, with the failing table named): feature sets are ASCII, well
  formed (`Name=Value` pairs with non-empty ASCII alphanumeric names and values, names ascending) and strictly
  ascending; a template has a tag below 17, a feature-set index below the table size, a cut of at most
  `maxSpellingBytes`, a suffix of at most 255 bytes that is strict UTF-8 and does not begin with a continuation byte,
  and no template repeats; lists follow the rules above; the three palettes and the list index must fill the payload
  exactly. While walking the spellings `Validate` also checks that every list index is below `listCount`, that the tags
  of the spelling's list are a subset of the spelling's tags (the join of the two tables), that every cut is at most
  the spelling length and falls on a character boundary of the spelling (so the composed base word is valid UTF-8),
  that the base word has 1 to 255 bytes, that the list sizes add up to `analysisCount`, that a composed base word is in
  Unicode normalization form C, and that the analyses are in the documented order. The tag order is checked once per
  list when the palette is read; the order of two neighboring templates with one tag depends on the spelling (the base
  words are `spelling` without `cut` bytes plus the suffix, so the same two templates can be in order for one spelling
  and out of order for another), so it is checked for each spelling that uses such a list: the two composed base words
  are compared as UTF-8 bytes without composing them (from the first byte after the shorter kept prefix, then by
  length) and, when they are equal, the feature-set indexes are compared, which is the order of the feature texts
  because the table is ascending. The comparison is the one of `LexiconWriter.CompareAnalyses`, which orders by tag,
  then base word bytes, then feature text bytes, and equal analyses are refused as well. Lists whose neighbors never
  share a tag are not compared. The normalization test costs nothing for text without a character from U+0300 on
  (every character below it is stable under form C, which a test proves for every pair of them): such a base word is
  not composed as a string, so only the rare base word with another character is decoded and normalized. The two
  checks (and `ValidateAnalyses`, which runs them for each of the 262,635 spellings) are compiled fully optimized at
  their first call (`MethodImplOptions.AggressiveOptimization`), because the load runs too briefly for tiered
  compilation to replace unoptimized code. Measured on .NET 10 (median of 21 cold processes each), the first access of
  `Tagger.Default` takes the same time with the checks as without them (49.6 against 49.9 ms); compiled like the rest of
  the load, the same checks added about 12 ms.
- **`TryGetTags(char[] chars, int offset, int length, byte[] scratch, out UposSet tags)`**: looks up the folded,
  lower-cased spelling in a region of a character buffer (the decoder keeps all tokens of a sentence in one buffer, hence
  the `offset`). It calls `TryFindKey` and drops the ordinal and key length. Allocation-free.
- **`TryFindKey(chars, offset, length, scratch, out ordinal, out tags, out keyLength)`** (and `TryFind`, the same
  without the key length): the search itself. The key is encoded to UTF-8 in `scratch` (a key longer than
  `MaxSpellingBytes` characters or bytes returns `false` at once); a binary search over the block heads (stored whole,
  compared in place) finds the block whose head is the last one not greater than the key; a linear scan rebuilds each
  following spelling in the scratch buffer and compares bytes. It also reports the ordinal of the spelling (its position
  in ascending order, which addresses its list in the index) and the number of UTF-8 bytes of the key, which stay at the
  start of `scratch`. Allocation-free.
- **`AnalyzedTags(ordinal)`**: the tags that the analyses of a spelling name, read through the list index from a
  precomputed mask per list (two bytes read, no allocation). It is a subset of the spelling's tags; the difference is the
  set of tags that the lexicon lists without an analysis.
- **`GetAnalyses(ordinal)`**: decodes the spelling of its block into a new buffer and composes each analysis (the first
  `n - cut` bytes plus the suffix, as a string; the feature string is shared with the table). It allocates and is the only
  allocating path of the analyses.
- **`HasAnalysis(ordinal, tag, spelling..., lemma...)`**: tells whether one analysis of the spelling has this tag and
  composes exactly this base word, comparing UTF-8 bytes without composing a string (tag first, then the length, then the
  prefix and the suffix). Allocation-free; `IsFormOf` uses it.
- **`AllowedTags(bool known, UposSet lexiconTags, bool hasLetterOrDigit, bool hasDigit, bool firstUpper)`**: the
  token-shape rules of `data/lexicon/README.md`. An unknown spelling allows all 17 tags. Otherwise start from the lexicon
  tags and add PUNCT, SYM and X when the token has no letter or digit; NUM, SYM, X, NOUN, PROPN and ADJ when it has a
  digit; PROPN and X when its first character is an upper-case letter. The result is never empty. The three shape sets
  are precomputed `static readonly` `UposSet` values (not built with `UposSet.Of(params ...)` per call), so this path
  allocates nothing; it runs for every known token of a sentence.
- **`AllowedTags(string token, Lexicon lexicon)`**: computes the shape facts with `char.IsLetterOrDigit`,
  `char.IsDigit` and `char.IsUpper` and applies the rules; used by tests so that the decoder's allowed set can be
  compared with an independent reading of the rules.
- **`Enumerate()`, `EnumerateAnalyses()` and `ToUncompressedImage(byte[])`**: decode every spelling with its tags (or
  with its analyses, skipping spellings that have none); return the uncompressed file image (the header with the Deflate
  flag cleared, the stored length set to the payload length and the checksum recomputed over the payload, then the
  payload), which `LexiconCompiler --check` compares because the Deflate bytes depend on the runtime. The image
  includes the analyses.
- **`LexiconWriter.FromTsv(IEnumerable<string>)`**: parses the source table: a comment line starts with `#`, a row is a
  lower-case NFC spelling without space, a tab and the tags in alphabetical order separated by spaces; spellings must
  ascend strictly in UTF-8 byte order; a header comment that records `spellings=N, tag entries=M` is checked against
  the counts. Throws `InvalidDataException` naming the line.
- **`LexiconWriter.AnalysesFromTsv(IEnumerable<string>, entries)`**: parses `data/lexicon/analyses.tsv` against the
  lexicon entries: a row is a spelling, a tab, and one or more tab-separated analyses `lemma TAG features` (the features
  omitted when empty); rows ascend strictly in UTF-8 byte order and name only spellings of the lexicon; the analyses
  of a row ascend by tag, base word and features, are distinct, and each tag is one of the spelling's tags; the header
  comment's `spellings=` and `analyses=` counts are checked. The syntax of a feature set is checked, the vocabulary of
  names and values is not (the build script and the data test enforce it, so a custom lexicon may carry others).
- **`LexiconWriter.Compile(entries, analyses, compress, blockShift = 4)`, `Compile(entries, compress, blockShift)` and
  `Write`**: validates again (empty spelling or tag set, order, more than 65,535 masks or a longer spelling, and for the
  analyses a tag the spelling lacks, more than 255 analyses of a spelling, analyses that are not strictly ascending by tag,
  base word and features, a base word that is empty, has white space, is not well-formed UTF-16 in normalization form C
  or is longer than 255 bytes, malformed features, more than 65,535 feature sets or lists), builds the palette, the block
  index and the front-coded data, derives the templates (the longest common prefix of spelling and base word, moved back
  to a character boundary of both, gives the cut and the suffix) and lists, and writes the header and payload. The
  overload without analyses writes a version 3 file without analyses. The output is deterministic: feature sets
  ascend, templates are ordered by descending use and then by their content, lists by first use.

#### Error Handling

Loading reports every defect as `InvalidDataException` (through `BinaryFormat` for the shared checks, and directly for
structure and analyses). `Load(Stream)` throws `ArgumentNullException` for `null`. `TryGetTags` and `TryFindKey` never
throw on a loaded lexicon; a key that cannot be in the lexicon returns `false`. `AnalyzedTags`, `GetAnalyses` and
`HasAnalysis` throw `ArgumentOutOfRangeException` for an ordinal that is not the position of a spelling (the callers
pass only ordinals that `TryFindKey` returned). `FromTsv`, `AnalysesFromTsv` and `Compile` throw
`InvalidDataException`, `ArgumentException` or `ArgumentOutOfRangeException` for table and parameter errors, so that a
bad table fails at compile time and not at lookup time.

#### Dependencies

- **BinaryFormat** (validation, varints, checksum, Deflate, byte comparison) and **Upos** (`UposSet`, tag parsing), same
  system.
- Base class library only (`System.Text.Encoding`, `Regex` in the writer).

#### Callers

- **EmbeddedModel** (`Load`) and **Tagger** (`Load(Stream, Stream, ...)`).
- **TagDecoder** (`TryGetTags`, `TryFindKey`, `AllowedTags`, `AnalyzedTags`, `GetAnalyses`, `HasAnalysis`).
- The tool `LexiconCompiler` (`FromTsv`, `AnalysesFromTsv`, `Compile`, `ToUncompressedImage`) and the test suite.
