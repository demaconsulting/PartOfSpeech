# System Design

This document provides the system-level design for the Part Of Speech.

![Part Of Speech Structure](PartOfSpeechView.svg)

## Architecture

The Part Of Speech is a .NET library that splits English text into sentences and tokens and tags every token with one
of the 17 Universal POS tags, with a calibrated confidence, ranked alternatives and optional per-token constraints. It
has zero runtime package dependencies and ships as one NuGet package. The system consists of one unit and three
subsystems:

- **Upos** (unit): the tag vocabulary, its text forms and sets of tags; see _Upos Design_.
- **Tokenization** (subsystem): the tokenizer and the sentence splitter, which follow the tokenization convention of the
  training data; see _Tokenization Design_.
- **ModelStorage** (subsystem): the binary formats of the weights (`POSW`) and the lexicon (`POSL`), their readers and
  writers, and the lexicon that is embedded in the package; see _ModelStorage Design_.
- **Tagging** (subsystem): the feature extractor, the greedy decoder, the token constraints and the public `Tagger`;
  see _Tagging Design_.

Relationships (the unit-level call graph has no cycle): `Tagger` uses `Tokenizer`, `TagDecoder`, `EmbeddedModel` and
`TokenConstraint`; `TagDecoder`
uses `FeatureExtractor`, `PerceptronModel`, `Lexicon` and `UposSet`; `FeatureExtractor` uses `Upos`; `Lexicon` and
`PerceptronModel` use `BinaryFormat` and `UposSet`; `EmbeddedModel` uses `Lexicon`, `PerceptronModel` and the constant
`FeatureExtractor.FeatureSetVersion`; `Tokenizer` uses `SentenceSplitter`.

All public types live in the one namespace `DemaConsulting.PartOfSpeech`; the folders `Tokenization`, `ModelStorage` and
`Tagging` mirror the subsystems but are not namespaces. The `ModelStorage` classes are internal and are visible to the
test project, to the tool `tools/LexiconCompiler` and to the trainer of a later release through `InternalsVisibleTo`.

### Data Assets

The repository also holds data assets: training corpora, evaluation sets and the lexicon. They are files, not software
items, so they have no unit design, and the system requirements in the _Data and Licensing_ section of the system
requirements cover them. Only one of them is consumed at run time: the lexicon, which `tools/LexiconCompiler` compiles
into the binary resource `src/DemaConsulting.PartOfSpeech/Resources/lexicon.bin` that the package embeds.

- **Training corpora**: sentences labeled with the 17 Universal POS tags, from three sources: the open corpus, MASC and
  a silver corpus whose labels were written by a model and mapped by documented rules
- **Evaluation sets**: a split of the open corpus, a single-target gold set, a probe of difficult noun and verb shapes
  and a challenge set; the UD English-EWT treebank is evaluation-only and is fetched on demand, never stored
- **Lexicon**: a table that maps a lower-case spelling to the Universal POS tags it may carry, with a coverage report;
  compiled into the embedded `POSL` file
- **Checks**: the checksum manifest and the leakage report
- **Third-party notice**: `THIRD-PARTY-NOTICES.md`, packed in the NuGet package
- **Build-time tools**: the Python scripts that import, check and regenerate the data, and the C# tool
  `tools/LexiconCompiler` that compiles the lexicon; they are not shipped and are not part of the runtime

## External Interfaces

The system exposes the following public API to external consumers (all in the namespace `DemaConsulting.PartOfSpeech`):

- **Tagger**: `Tagger.Load(Stream weights, TaggerOptions?)` and `Tagger.Load(Stream weights, Stream lexicon,
  TaggerOptions?)` create a tagger; `Tagger.Default` returns the tagger with the embedded model and currently always
  throws `InvalidOperationException` because the package embeds no trained model yet; `Tag(string)`,
  `Tag(Sentence, constraints)` and `Tag(IReadOnlyList<string>, constraints)` tag text, a sentence or tokens.
- **Tokenizer**: `Tokenize(string)` returns sentences with tokens; `CreateSentence(IReadOnlyList<string>)` builds a
  sentence from tokens.
- **Results**: `TaggedSentence`, `TaggedToken`, `TagScore`, `Sentence` and `Token`, all immutable.
- **Control**: `TaggerOptions` (`MaxAlternatives`) and `TokenConstraint` (`None`, `Only`, `Prefer`, `Penalize`, `And`).
- **Vocabulary**: `Upos`, `UposExtensions` (`ToTagString`, `TryParse`, `Count`) and `UposSet`.

| Interface | Direction | Format | Constraints |
| --- | --- | --- | --- |
| `Tagger.Load(Stream, ...)` | Inbound | `POSW` weights file | Valid file of a supported version |
| `Tagger.Load(Stream, Stream, ...)` | Inbound | `POSW` and `POSL` files | Both valid; streams read to the end |
| `Tagger.Default` | Outbound | `Tagger` instance | Throws until a model is embedded |
| `Tagger.Tag(...)`, `Tokenizer.*` | Inbound/Outbound | `string` or tokens in, results out | Arguments not null |
| `TokenConstraint` | Inbound | Value passed with the tokens | One per token, same count |
| Embedded lexicon resource | Internal | `POSL` file in the assembly | Matches `data/lexicon/lexicon.tsv` |
| `THIRD-PARTY-NOTICES.md` | Outbound | Markdown file at the package root | Present in every package |
| NuGet package `api/` folder | Outbound | Markdown API reference | Generated by ApiMark at build |

The binary formats are interfaces too. The weights file is the interface to the trainer of a later release and to
callers who train their own models; both formats are specified byte by byte in _ModelStorage Design_.

## Dependencies

The Part Of Speech has zero runtime NuGet dependencies — it is implemented exclusively
against the .NET Base Class Library (including `System.IO.Compression.DeflateStream`). The following OTS items are used
for building and verifying this system (not consumed at runtime); see _OTS Integration Design_ (`docs/design/ots.md`)
and each item's dedicated design document for details:

- **ApiMark** — generates the Markdown API reference that is packed with the library; see _ApiMark Design_
- **BuildMark** — generates build-notes documentation; see _BuildMark Design_
- **FileAssert** — validates generated documents against acceptance criteria; see
  _FileAssert Design_
- **Pandoc** — converts Markdown documentation to HTML; see _Pandoc Design_
- **ReqStream** — enforces requirements-to-test traceability; see _ReqStream Design_
- **ReviewMark** — enforces file review coverage and currency; see _ReviewMark Design_
- **SarifMark** — converts CodeQL SARIF results to markdown; see _SarifMark Design_
- **SonarMark** — generates SonarCloud quality reports; see _SonarMark Design_
- **SysML2Tools** — lints the architecture model and renders its diagrams; see _SysML2Tools Design_
- **VersionMark** — captures and publishes tool-version information; see _VersionMark Design_
- **WeasyPrint** — converts HTML documentation to PDF; see _WeasyPrint Design_
- **xUnit** — executes unit and integration tests; see _xUnit Design_

The following third-party data sets are used to build the data assets. They are data, not software, so they are not OTS
items and have no OTS design document. Their attribution and license notices are in `THIRD-PARTY-NOTICES.md`, and each
source is recorded with its origin, license and production recipe in the `README.md` of its folder under `data/`, and every
source document of the corpora in a per-document `sources.tsv` (for MASC: genre, the corpus-wide license and the counts):

- **MASC** (CC BY 3.0 US): Penn Treebank trees converted to Universal POS tags for training data
- **Open English WordNet** (CC BY 4.0, carrying the Princeton WordNet notice): word forms reduced to the lexicon
- **Moby Part-of-Speech List** (public domain): word forms of the lexicon
- **Open part-of-speech corpus of the WordSense project** (CC0 1.0 annotations; public-domain or CC0 sentences): the
  open training and evaluation data
- **Tatoeba** (CC0 export) and **Project Gutenberg** (public domain in the United States): sentences of the silver data
- **UD English-EWT** (CC BY-SA 4.0): evaluation only, fetched on demand and never committed

The build-time data tools are Python scripts that use only the Python standard library. They are run by developers and
are not part of the build, the tests or the package. The lexicon compiler is a small C# console program that references
the library; `build.ps1` runs it with `--check` so that a stale embedded lexicon fails the build. Their files and tests
are covered by review-sets (see _Design Introduction_, Companion Artifact Structure).

## Risk Control Measures

N/A - Part Of Speech is a text-analysis library with no safety-critical functionality
requiring risk control measures (IEC 62304 §5.3.3).

Tags are statistical estimates, not facts, and the library therefore reports a confidence and alternatives with every
tag instead of hiding uncertainty; callers that need certainty for a word can constrain it. Two checks protect the
integrity of what the library ships:

- **Format checks**: every binary file carries a magic, a format version, a feature set number (weights) and a checksum
  of its payload, and a reader refuses anything that does not match, so a damaged or incompatible file fails when it is
  loaded and never yields wrong tags.
- **Embedded data check**: a unit test and the `--check` step of the build fail when the embedded lexicon does not encode
  `data/lexicon/lexicon.tsv`.

The data assets are protected by two release gates, which the data tests of the system verify:

- **License gate**: only CC0, CC BY, public-domain and project-licensed data is committed. Share-alike,
  non-commercial and no-derivatives data, UD English-EWT and CoNLL-U files are rejected.
- **Isolation gate**: no training document and no training sentence, of any length, appears in an evaluation set, so that
  measured accuracy is not inflated by training data. The data tools enforce it on the training side: a long match
  (a key of 25 or more letters and digits) shows shared source text and excludes the whole training document; a
  shorter exact match is a sentence that recurs in unrelated texts and is dropped alone. The evaluation sets are never
  changed.

## Data Flow

**Tagging path** (run time):

1. **Input**: a text, or a list of tokens, and optionally one `TokenConstraint` per token. A weights stream is loaded
   once by `Tagger.Load`: the reader verifies the file and builds the sparse weight rows.
2. **Tokenization**: `Tokenizer` splits the text into tokens by the convention of the training data and groups them
   into sentences; tokens are slices of the input with offsets. A token list becomes one sentence.
3. **Features**: for each sentence `FeatureExtractor` folds the tokens (NFC, typographic quotes and dashes mapped to
   ASCII) and computes 22 hashed feature buckets per token; four of them use the two previous tags.
4. **Scoring**: the decoder sums the integer weights of the buckets for each tag.
5. **Restriction**: the lexicon lookup gives the tags that a known word can carry; token-shape rules extend them; a
   constraint narrows them or adds to the scores.
6. **Decision**: the best candidate becomes the tag and the next token's history; a softmax at the model temperature
   turns the candidate scores into the confidence and the ranked alternatives.
7. **Output**: immutable `TaggedSentence` objects with `TaggedToken` values that carry the original text and offsets.

**Data path** (build time, not part of the runtime):

1. **Sources**: the open corpus, MASC, Tatoeba and Project Gutenberg text and the dictionary tables are read from
   pinned inputs whose SHA-256 values are recorded
2. **Import**: the tools convert every source to the shared corpus format with the 17 Universal POS tags, remove
   training documents or sentences that overlap evaluation data, and write sorted UTF-8 files with LF line endings
3. **Lexicon**: the tools build the spelling-to-tags lexicon from the dictionary tables and the tags attested in the
   training corpora, then measure its coverage of the open training and development sets
4. **Compilation**: `tools/LexiconCompiler` compiles the lexicon table into `Resources/lexicon.bin`, which is committed
   and embedded in the assembly
5. **Checks**: the tools write the leakage report and the checksum manifest
6. **Notices**: `THIRD-PARTY-NOTICES.md` carries the attributions of the sources, and the NuGet package includes it

**Later releases**: a trainer (phase 3) reads the corpora, extracts features with the same `FeatureExtractor`, trains an
averaged perceptron, and writes the `POSW` file with `PerceptronModelWriter`; the file is then embedded as
`Resources/weights.bin` and `Tagger.Default` starts to work. The tagging design records the contract.

## Design Constraints

- **Compliance**: All functionality must be traceable to requirements
- **Quality**: Zero warnings, full test coverage of requirements, complete documentation, including a generated API
  reference that is packed with the library
- **Portability**: Compatible across supported .NET platforms; no `Span<T>`, `stackalloc` or other APIs that
  `netstandard2.0` lacks, so the hot path uses arrays and a per-thread workspace
- **No runtime dependencies**: the base class library only
- **Determinism**: the same input gives the same tags on every platform; scores are exact integers up to the choice of
  the tag
- **Performance**: more than 50,000 tokens per second on one thread, with no allocation in the feature extraction and
  only the result arrays per sentence in the decoder. The throughput is enforced in runs without code coverage; coverage
  instrumentation slows the hot loops too much to prove it, so a run with coverage (CI) applies a relaxed gate (see the
  verification document)
- **Package size**: the lexicon resource is about 0.64 MB and is embedded once per target framework, so the package is
  about 3.2 MB; the trained weights of a later release are expected to add about 1 MB per target framework
- **Open licenses only**: No share-alike, non-commercial or no-derivatives data; attribution notices for every
  attribution-licensed source travel with the package
- **Data format**: Data files are tab-separated text (UTF-8, no byte order mark, LF line endings), so that their
  checksums are stable on every platform
- **Language**: Documentation and tag conventions use US English

### Platform Support

The library targets the following frameworks, enabling broad compatibility across modern .NET
runtimes and legacy environments:

| Target Framework | Runtime / Environment |
| --- | --- |
| `netstandard2.0` | .NET Standard 2.0 (implemented by .NET FX 4.8.1+) |
| `net8.0` | .NET 8 LTS |
| `net9.0` | .NET 9 |
| `net10.0` | .NET 10 |

The library is supported on the following operating systems:

- **Windows** — primary developer and CI platform
- **Linux** — CI/CD and containerized environments
- **macOS** — developer workstations using Apple platforms

Portability is achieved by restricting the implementation exclusively to Base Class Library (BCL)
APIs available across all target frameworks. No platform-specific native interop, OS-specific
APIs, or framework-version-specific features are used; the few optional speed-ups (for example hardware population
count on .NET 6 and later) are guarded by conditional compilation and have a portable fallback.

### Integration Patterns

- **NuGet Packaging**: Standard .NET library packaging and distribution, with the third-party notice at the package root
  and the Markdown API reference under `api/`
- **CI/CD Integration**: Automated build, test, and quality validation
- **Requirements Traceability**: All features linked to passing tests
- **Review Management**: Systematic file review using ReviewMark patterns

### Spike Deviations and Phase 3 Contract

The decoder is derived from an averaged-perceptron spike. The deviations from it (hash, integer scoring, folding, the
17-tag set, the extra pair features) and the contract with the later trainer (feature set version, writer, byte
layouts, temperature and scale) are recorded in _Tagging Design_ and _ModelStorage Design_ so that the trainer can be
built without reading the decoder source.
