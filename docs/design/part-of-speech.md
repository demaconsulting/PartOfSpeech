# System Design

This document provides the system-level design for the Part Of Speech.

![Part Of Speech Structure](PartOfSpeechView.svg)

## Architecture

The Part Of Speech is a minimal .NET library template demonstrating DEMA Consulting
best practices. The system consists of:

- **Demo Unit**: Simple greeting functionality demonstrating library patterns

There are no subsystems and no relationships between software items beyond the system exposing
the `Demo` unit directly through its public API; see _Demo Unit Design_ for the unit's internal
collaboration (none — `Demo` is a self-contained leaf class).

### Data Assets

The repository also holds data assets for the part-of-speech tagging that later phases implement. They are files, not
software items, so they have no unit design, and the system requirements in the _Data and Licensing_ section of the
system requirements cover them. The runtime does not consume them yet.

- **Training corpora**: sentences labeled with the 17 Universal POS tags, from three sources: the open corpus, MASC and
  a silver corpus whose labels were written by a model and mapped by documented rules
- **Evaluation sets**: a split of the open corpus, a single-target gold set, a probe of difficult noun and verb shapes
  and a challenge set; the UD English-EWT treebank is evaluation-only and is fetched on demand, never stored
- **Lexicon**: a table that maps a lower-case spelling to the Universal POS tags it may carry, with a coverage report
- **Checks**: the checksum manifest and the leakage report
- **Third-party notice**: `THIRD-PARTY-NOTICES.md`, packed in the NuGet package
- **Build-time tools**: Python scripts that import, check and regenerate the data; they are not shipped and are not
  part of the runtime

## External Interfaces

The system exposes the following public API to external consumers:

- **Demo()**: Default constructor; initializes the instance with the default prefix `"Hello"`
- **Demo(string prefix)**: Custom-prefix constructor; initializes the instance with the specified
  prefix. Throws `ArgumentNullException` if `prefix` is null; throws `ArgumentException` if
  `prefix` is an empty string.
- **Demo.Prefix**: Read-only property that returns the greeting prefix configured at construction
- **Demo.DemoMethod(string name)**: Returns a greeting string in the format `{prefix}, {name}!`.
  Throws `ArgumentNullException` if `name` is null; throws `ArgumentException` if `name` is an
  empty string.

| Interface                      | Direction        | Format                        | Constraints                  |
|--------------------------------|------------------|-------------------------------|------------------------------|
| `Demo()`                       | Inbound          | Constructor call              | None; always succeeds        |
| `Demo(string prefix)`          | Inbound          | Constructor call              | `prefix` non-null, non-empty |
| `Demo.Prefix`                  | Outbound         | `string` property read        | None; always succeeds        |
| `Demo.DemoMethod(string name)` | Inbound/Outbound | Method call / `string` return | `name` non-null, non-empty   |

The system has no runtime data interface in this phase: the data assets are not read by the library. The only data
interface of the packed library is the third-party notice file `THIRD-PARTY-NOTICES.md` (outbound, a Markdown file at the
package root). Every package contains it, and it carries the attributions and license notices of the third-party data.

## Dependencies

The Part Of Speech has zero runtime NuGet dependencies — it is implemented exclusively
against the .NET Base Class Library. The following OTS items are used for building and verifying
this system (not consumed at runtime); see _OTS Integration Design_ (`docs/design/ots.md`) and
each item's dedicated design document for details:

- **BuildMark** — generates build-notes documentation; see _BuildMark Design_
- **FileAssert** — validates generated documents against acceptance criteria; see
  _FileAssert Design_
- **Pandoc** — converts Markdown documentation to HTML; see _Pandoc Design_
- **ReqStream** — enforces requirements-to-test traceability; see _ReqStream Design_
- **ReviewMark** — enforces file review coverage and currency; see _ReviewMark Design_
- **SarifMark** — converts CodeQL SARIF results to markdown; see _SarifMark Design_
- **SonarMark** — generates SonarCloud quality reports; see _SonarMark Design_
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

The build-time tools are Python scripts that use only the Python standard library. They are run by developers and are
not part of the build, the tests or the package. Their files and tests are covered by review-sets (see _Design
Introduction_, Companion Artifact Structure).

## Risk Control Measures

N/A - Part Of Speech is a demonstration template with no safety-critical functionality
requiring risk control measures (IEC 62304 §5.3.3).

The data assets are protected by two release gates, which the data tests of the system verify:

- **License gate**: only CC0, CC BY, public-domain and project-licensed data is committed. Share-alike,
  non-commercial and no-derivatives data, UD English-EWT and CoNLL-U files are rejected.
- **Isolation gate**: no training document and no training sentence, of any length, appears in an evaluation set, so that
  measured accuracy is not inflated by training data. The data tools enforce it on the training side: a long match
  (a key of 25 or more letters and digits) shows shared source text and excludes the whole training document; a
  shorter exact match is a sentence that recurs in unrelated texts and is dropped alone. The evaluation sets are never
  changed.

## Data Flow

**Construction path:**

1. **Input**: Constructor parameter `prefix` (optional; defaults to `"Hello"` when using `Demo()`)
2. **Validation**: `Demo(string prefix)` rejects null with `ArgumentNullException`; rejects empty
   string with `ArgumentException`
3. **Storage**: Valid prefix stored for use in subsequent greeting calls

**Method-call path:**

1. **Input**: Method parameter `name` (required, non-empty string)
2. **Validation**: `DemoMethod` rejects null with `ArgumentNullException`; rejects empty string
   with `ArgumentException`
3. **Processing**: Simple string formatting combining stored prefix and supplied name
4. **Output**: Formatted greeting string in the format `{prefix}, {name}!`

**Data path** (build time, not part of the runtime):

1. **Sources**: the open corpus, MASC, Tatoeba and Project Gutenberg text and the dictionary tables are read from
   pinned inputs whose SHA-256 values are recorded
2. **Import**: the tools convert every source to the shared corpus format with the 17 Universal POS tags, remove
   training documents or sentences that overlap evaluation data, and write sorted UTF-8 files with LF line endings
3. **Lexicon**: the tools build the spelling-to-tags lexicon from the dictionary tables and the tags attested in the
   training corpora, then measure its coverage of the open training and development sets
4. **Checks**: the tools write the leakage report and the checksum manifest
5. **Notices**: `THIRD-PARTY-NOTICES.md` carries the attributions of the sources, and the NuGet package includes it

## Design Constraints

- **Simplicity**: Minimal functionality to serve as template
- **Compliance**: All functionality must be traceable to requirements
- **Quality**: Zero warnings, full test coverage, complete documentation
- **Portability**: Compatible across supported .NET platforms
- **Open licenses only**: No share-alike, non-commercial or no-derivatives data; attribution notices for every
  attribution-licensed source travel with the package
- **Data format**: Data files are tab-separated text (UTF-8, no byte order mark, LF line endings), so that their
  checksums are stable on every platform
- **Language**: Documentation and tag conventions use US English

### Platform Support

The library targets the following frameworks, enabling broad compatibility across modern .NET
runtimes and legacy environments:

| Target Framework   | Runtime / Environment                             |
|--------------------|---------------------------------------------------|
| `netstandard2.0`   | .NET Standard 2.0 (implemented by .NET FX 4.8.1+) |
| `net8.0`           | .NET 8 LTS                                        |
| `net9.0`           | .NET 9                                            |
| `net10.0`          | .NET 10                                           |

The library is supported on the following operating systems:

- **Windows** — primary developer and CI platform
- **Linux** — CI/CD and containerized environments
- **macOS** — developer workstations using Apple platforms

Portability is achieved by restricting the implementation exclusively to Base Class Library (BCL)
APIs available across all target frameworks. No platform-specific native interop, OS-specific
APIs, or framework-version-specific features are used.

### Integration Patterns

- **NuGet Packaging**: Standard .NET library packaging and distribution
- **CI/CD Integration**: Automated build, test, and quality validation
- **Requirements Traceability**: All features linked to passing tests
- **Review Management**: Systematic file review using ReviewMark patterns
