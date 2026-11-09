# System Design

This document provides the system-level design for the Part Of Speech.

![Part Of Speech Structure](PartOfSpeechView.svg)

## Architecture

The Part Of Speech is a .NET library that splits English text into sentences and tokens and tags every token with one
of the 17 Universal POS tags, with a calibrated confidence, ranked alternatives and optional per-token constraints. It
has zero runtime package dependencies and ships as one NuGet package. The system consists of two units and three
subsystems:

- **Upos** (unit): the tag vocabulary, its text forms and sets of tags; see _Upos Design_.
- **ModelTrainer** (unit): the build-time tool that trains the embedded weights from the committed corpora with the
  library's own feature extractor, lexicon restriction and weights writer, selects every setting on development data
  only, and checks that the committed weights are current; it is not shipped and not part of the runtime; see
  _ModelTrainer Design_.
- **Tokenization** (subsystem): the tokenizer and the sentence splitter, which follow the tokenization convention of the
  training data; see _Tokenization Design_.
- **ModelStorage** (subsystem): the binary formats of the weights (`POSW`) and the lexicon (`POSL`), their readers and
  writers, and the lexicon and the weights that are embedded in the package; see _ModelStorage Design_.
- **Tagging** (subsystem): the feature extractor, the greedy decoder, the token constraints, the glossary and the public
  `Tagger`; see _Tagging Design_.

Relationships (the unit-level call graph has no cycle): `Tagger` uses `Tokenizer`, `TagDecoder`, `EmbeddedModel`,
`TokenConstraint` and `Glossary`; `Glossary` uses `Tokenizer`, `FeatureExtractor`, `TokenConstraint` and `UposSet`;
`TagDecoder`
uses `FeatureExtractor`, `PerceptronModel`, `Lexicon` and `UposSet`; `FeatureExtractor` uses `Upos`, `UposSet` and
`Lexicon` (the lookups of the feature groups); `Lexicon` and
`PerceptronModel` use `BinaryFormat` and `UposSet`; `PerceptronModel` and `PerceptronModelWriter` also use the constants
and the group type of `FeatureExtractor` (the feature set versions and the known groups); `EmbeddedModel` uses `Lexicon`
and `PerceptronModel`; `Tokenizer` uses `SentenceSplitter`; and `ModelTrainer` uses `FeatureExtractor`,
`Lexicon`, `PerceptronModel`, `PerceptronModelWriter`, `BinaryFormat` and `Upos` to train, and `Tagger`, `TagDecoder`
and `TokenConstraint` to score a weights file and to run the glossary experiment through the real decoder, and `Glossary`
to run the glossary evaluation through the real tagger, while nothing
in the library uses `ModelTrainer`.

All public types live in the one namespace `DemaConsulting.PartOfSpeech`; the folders `Tokenization`, `ModelStorage` and
`Tagging` mirror the subsystems but are not namespaces. The `ModelStorage` and most `Tagging` classes are internal and are
visible to the test project, to the tool `tools/LexiconCompiler` and to the trainer through `InternalsVisibleTo`.

### Data Assets

The repository also holds data assets: training corpora, evaluation sets and the lexicon. They are files, not software
items, so they have no unit design, and the system requirements in the _Data and Licensing_ section of the system
requirements cover them. Two compiled forms are consumed at run time: the lexicon, which `tools/LexiconCompiler` compiles
into the binary resource `src/DemaConsulting.PartOfSpeech/Resources/lexicon.bin` that the package embeds, and the
weights, which the unit `ModelTrainer` trains into `src/DemaConsulting.PartOfSpeech/Resources/weights.bin`, which the
package embeds as well (the _Embedded Model_ system requirements cover it).

- **Training corpora**: sentences labeled with the 17 Universal POS tags, from four sources: the open corpus, MASC, a
  silver corpus whose labels were written by a model and mapped by documented rules, and a small imperative source
  (`data/imperative/`: 900 sentences that start with an imperative verb, from US federal-government instructional
  text, tagged by two blind models that agree on every token, a model review of a sample, selected by position and
  lexicon frame and never by the PartOfSpeech model, and kept disjoint from every evaluation set). The weights are
  trained from the sources that have a positive weight in `training.json`: the open corpus, MASC, silver and the
  imperative source (weight 1, so it is in the mix of the embedded weights; `--source imperative:0` leaves it out of an
  experimental run)
- **Evaluation sets**: a split of the open corpus, a single-target gold set, a probe of difficult noun and verb shapes
  and a challenge set; the UD English-EWT treebank is evaluation-only and is fetched on demand, never stored. Only the
  two development splits were used to select the model and the confidence thresholds; the other sets were evaluated
  once for each frozen model by the trainer (see _ModelTrainer Design_). A hard-shape
  development set (`data/eval/shapes-dev/`: real US federal-government sentences, five shapes, controls, labeled blind
  by two models; a standard tranche and a harder second tranche that a pre-registered rule selects from the contested
  cases of another model family, never from the PartOfSpeech model) is the one evaluation file that is used to select
  settings; it is development only, read by the trainer
  through a guard of its own, never trained on, kept disjoint from the training sets and from every other evaluation set
  (the two tranches share documents but no sentence), and never reported as a held-out figure
- **Evaluation record**: `docs/evaluation/` holds the selection of the two recommended confidence thresholds
  (`operating-points.json`, `confidence-thresholds.md`) and the results of the one held-out run (`test-results.json`,
  `test-results.md`), all generated by the trainer, and a `README.md` that explains how to read and reproduce them and
  what they do not show. It is plain Markdown and JSON read on GitHub, not a generated document, and no treebank text is
  in it. The packed API reference and the user guide quote a few of its figures; tests compare
  those copies with the generated files. The record describes the embedded model: the corrected corpora minus the
  withdrawn `prenominal-ing` corrections, the clause-cue feature group (see _Context Features_ in the Data Flow section)
  and the imperative training source. The held-out sets are evaluated once per model; the record is a disclosed look at
  sets that earlier models of the family also scored (`docs/evaluation/disclosure.md`, which both generated reports
  carry) and stands as a record of its own model, not as a correction of another. The evaluation `README.md` shows the
  previous model beside it and states which comparisons are fair, and the run log `RUN-LOG.md` ("Corpus cleanup C",
  "Context-feature experiment" and "Imperative training data") has the protocol and the selection history
- **Lexicon**: a table that maps a lower-case spelling to the Universal POS tags it may carry, with a coverage report;
  compiled into the embedded `POSL` file
- **Weights**: the trained `POSW` file, 521,695 bytes compressed and 1,157,499 bytes uncompressed, with the training
  configuration `training.json` (which records the SHA-256 of the uncompressed image) and the run log `RUN-LOG.md` next
  to the trainer
- **Checks**: the checksum manifest and the leakage report (which covers the four training sources)
- **Corrections overlay**: `data/corrections/` holds the label corrections for the three correctable training corpora (the
  imperative source is linted by the conventions linter but never corrected) as tables, and
  the rows are applied to the corpus files (`corrections.py apply --write`). `mechanical.tsv` holds the rows that the
  mechanical rules of the conventions linter write
  (the rule knows the tag), `adjudicated.tsv` the decisions of two independent blind taggers (a tag, a flag or a
  `disputed` mark; produced by `adjudicate.py`, never by the generator), `candidates.tsv` the cases that need a judgment,
  and `exceptions.tsv` the reviewed cases that stay as they are. A row addresses one token (source, split, sentence id,
  token index) and carries the old and the new tag and status and its provenance; the key is unique in each table, and
  an adjudicated row replaces the mechanical row of the same token (the mechanical row stays in its file and is ignored
  by the validator and by the application). Adjudicated rows that were withdrawn on purpose (`corrections.py withdraw`:
  the 878 rows of the detector `prenominal-ing`, after the pre-registered follow-up of the trainer run log) stay in
  `withdrawn-<detector>.tsv` as provenance; that table is never applied, the validator checks its header and its `# input`
  lines as those of `adjudicated.tsv`, and requires the old value in the
  corpora for every row of it, in both states. A table is valid against the corpora when all of its rows in effect match
  them before the application or all after it (a mixture is an error, and a second application is refused). The
  corpora, the lexicon and the weights are built from the corrected files, and the tables stay as the provenance of the
  change; the generator check reverses the applied rows in a temporary copy to prove that the tables are still its
  output. The held-out evaluation record was reissued for the retrained model (see _Evaluation record_ above)
- **Package license and third-party notice**: `PACKAGE-LICENSE.md` (the license of the package: the MIT code and the
  CC BY data) and `THIRD-PARTY-NOTICES.md` (attribution and change statements, also for the weights), both packed at
  the package root
- **Build-time tools**: the Python scripts that import, check and regenerate the data, including the conventions
  linter (`check_conventions.py`), the generator of the mechanical part of the corrections overlay
  (`make_corrections.py`) and the adjudication tool (`adjudicate.py`: the selection of the sentences, the blind batches
  for the taggers, the check of their outputs, the decision rule and the table `adjudicated.tsv`), and the C# tool
  `tools/LexiconCompiler` that compiles the lexicon; they are not shipped and are not part of the runtime. The taggers
  themselves are language models that run outside the repository, and their outputs are not committed. The unit
  tests of the correction tools and a report-only linter pass run in `lint.ps1`, which the quality-checks job of the
  workflow runs; they are developer tooling and not requirements evidence

## External Interfaces

The system exposes the following public API to external consumers (all in the namespace `DemaConsulting.PartOfSpeech`):

- **Tagger**: `Tagger.Load(Stream weights, TaggerOptions?)` and `Tagger.Load(Stream weights, Stream lexicon,
  TaggerOptions?)` create a tagger; `Tagger.Default` returns the one shared tagger that uses the embedded weights and
  lexicon (created on first use, thread-safe, no file needed); `Tag(string)`,
  `Tag(Sentence, constraints)` and `Tag(IReadOnlyList<string>, constraints)` tag text, a sentence or tokens, and the
  same three with a `Glossary` tag them with the terms of a glossary applied.
- **Tokenizer**: `Tokenize(string)` returns sentences with tokens; `CreateSentence(IReadOnlyList<string>)` builds a
  sentence from tokens.
- **Results**: `TaggedSentence`, `TaggedToken`, `TagScore`, `Sentence` and `Token`, all immutable; a tagged token and a
  tagged sentence also report the glossary matches.
- **Control**: `TaggerOptions` (`MaxAlternatives`) and `TokenConstraint` (`None`, `Only`, `Prefer`, `Penalize`, `And`).
- **Glossary**: `Glossary` (`Create`, `Empty`, `FindMatches`), `GlossaryBuilder`, `GlossaryEntry`, `GlossaryOptions`,
  `GlossaryMode` and `GlossaryMatch`.
- **Vocabulary**: `Upos`, `UposExtensions` (`ToTagString`, `TryParse`, `Count`) and `UposSet`.

| Interface | Direction | Format | Constraints |
| --- | --- | --- | --- |
| `Tagger.Load(Stream, ...)` | Inbound | `POSW` weights file | Valid file of a supported version |
| `Tagger.Load(Stream, Stream, ...)` | Inbound | `POSW` and `POSL` files | Both valid; streams read to the end |
| `Tagger.Default` | Outbound | `Tagger` instance | Created on first use from the embedded data; one shared instance |
| `Tagger.Tag(...)`, `Tokenizer.*` | Inbound/Outbound | `string` or tokens in, results out | Arguments not null |
| `TokenConstraint` | Inbound | Value passed with the tokens | One per token, same count |
| `Glossary` | Inbound | Immutable set of terms passed with the text | Not null; built from valid entries |
| Embedded lexicon resource | Internal | `POSL` file in the assembly | Matches `data/lexicon/lexicon.tsv` |
| Embedded weights resource | Internal | `POSW` file in the assembly | Matches `training.json` and its image hash |
| `PACKAGE-LICENSE.md` | Outbound | Markdown file at the package root | Named by the nuspec; starts with the SPDX line |
| `THIRD-PARTY-NOTICES.md` | Outbound | Markdown file at the package root | Present in every package |
| NuGet package `api/` folder | Outbound | Markdown API reference | Generated by ApiMark at build |
| Confidence guidance | Outbound | Namespace reference section, `TaggedToken` link | Equals `docs/evaluation/*.json` |

The binary formats are interfaces too. The weights file is the interface between the trainer and the library (the trainer
writes it with `PerceptronModelWriter`, the tagger reads it with `PerceptronModel`) and to callers who train their own
models; both formats are specified byte by byte in _ModelStorage Design_. The Trainer's command line (_ModelTrainer
Design_) is an interface of the build only.

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

- **MASC** (CC BY 3.0 US): Penn Treebank trees converted to Universal POS tags for training data; the lexicon tags and
  the weights are derived in part from them
- **Open English WordNet** (CC BY 4.0, carrying the Princeton WordNet notice): word forms reduced to the lexicon
- **Moby Part-of-Speech List** (public domain): word forms of the lexicon
- **Open part-of-speech corpus of the WordSense project** (CC0 1.0 annotations; public-domain or CC0 sentences): the
  open training and evaluation data
- **Tatoeba** (CC0 export) and **Project Gutenberg** (public domain in the United States): sentences of the silver data
- **US federal-government instructional documents** (17 U.S.C. 105; Army, Navy, Agriculture, Forest Service, Bureau of
  Mines and Public Health Service): the sentences of the imperative training source and of the development set, each
  document with its quoted license basis in a `sources.tsv`
- **UD English-EWT** (CC BY-SA 4.0): evaluation only, fetched on demand and never committed

The build-time data tools are Python scripts that use only the Python standard library. They are run by developers and
are not part of the build, the tests or the package. The lexicon compiler is a small C# console program that references
the library; `build.ps1` runs it with `--check` so that a stale embedded lexicon fails the build. The trainer is the
unit `ModelTrainer`; `build.ps1` and the CI build run `train --check` so that stale embedded weights fail the build.
The files and tests of the tools are covered by review-sets (see _Design Introduction_, Companion Artifact Structure).

## Risk Control Measures

N/A - Part Of Speech is a text-analysis library with no safety-critical functionality
requiring risk control measures (IEC 62304 §5.3.3).

Tags are statistical estimates, not facts, and the library therefore reports a confidence and alternatives with every
tag instead of hiding uncertainty; callers that need certainty for a word can constrain it. These checks protect the
integrity of what the library ships:

- **Format checks**: every binary file carries a magic, a format version, a feature set number (weights) and a checksum
  of its stored bytes, and a reader refuses anything that does not match (including a Deflate stream that is cut off or
  has trailing bytes), so a damaged or incompatible file fails when it is
  loaded and never yields wrong tags.
- **Embedded data checks**: a unit test and the `--check` step of the build fail when the embedded lexicon does not
  encode `data/lexicon/lexicon.tsv`; a unit test fails when the embedded weights differ from the committed file or from
  the header values and the SHA-256 of the uncompressed image that `training.json` records, and the `train --check` step
  of the build and of the CI build (on all three operating systems) retrains the model from the committed corpora and
  fails when the result is not identical to the committed weights.
- **Development-only selection**: no setting of the model or of the confidence thresholds was chosen on the data that
  measures it. The trainer opens a held-out evaluation set only in the `evaluate` command, only with a selection of the
  thresholds that is valid for the frozen model and was committed first, and only once; the other commands have no code
  path to a held-out file (see _ModelTrainer Design_). The hard-shape development set is a development file with a guard
  of its own (`eval-shapes` only; both tranches): it may select settings, it is never trained on and the shape probe
  stays held-out.
- **Guidance consistency**: the thresholds and the figures that the packed documentation, the user guide and the root
  `README.md` state are checked by tests against the committed selection and results; the results name the model hash
  and the selection hash and the hashes of their inputs, so a retrained model or a changed data file fails a test until
  the evaluation is redone. A build step (`dotnet fileassert`) asserts that the generated API reference carries the
  guidance.

The data assets are protected by two release gates, which the data tests of the system verify:

- **License gate**: only CC0, CC BY, public-domain and project-licensed data is committed. Share-alike,
  non-commercial and no-derivatives data, UD English-EWT and CoNLL-U files are rejected. The package license
  `PACKAGE-LICENSE.md` names every license that covers part of the package, because nuget.org accepts only license
  expressions of licenses that the OSI or the FSF approve and `CC-BY-3.0-US` (MASC, which the weights and the lexicon
  tags derive from) is neither.
- **Isolation gate**: no training document and no training sentence, of any length, appears in an evaluation set, so that
  measured accuracy is not inflated by training data. The data tools enforce it on the training side: a long match
  (a key of 25 or more letters and numbers) shows shared source text and excludes the whole training document; a
  shorter exact match is a sentence that recurs in unrelated texts and is dropped alone. The evaluation sets are never
  changed.

## Data Flow

**Tagging path** (run time):

1. **Input**: a text, or a list of tokens, and optionally one `TokenConstraint` per token and a `Glossary`. A weights
   stream is loaded
   once by `Tagger.Load`: the reader verifies the file and builds the sparse weight rows.
2. **Tokenization**: `Tokenizer` splits the text into tokens by the convention of the training data and groups them
   into sentences; tokens are slices of the input with offsets. A token list becomes one sentence.
   With a glossary, the terms are then found in each sentence and turned into constraints, which are merged with those of
   the caller.
3. **Features**: for each sentence `FeatureExtractor` folds the tokens (NFC, typographic quotes and dashes mapped to
   ASCII) and computes the hashed feature buckets of every token: the 22 base templates (four of them use the two
   previous tags) and the features of the feature groups that the weights file names (see _Context Features_ below).
4. **Scoring**: the decoder sums the integer weights of the buckets for each tag.
5. **Restriction**: the lexicon lookup gives the tags that a known word can carry; token-shape rules extend them; a
   constraint narrows them or adds to the scores.
6. **Decision**: the best candidate becomes the tag and the next token's history; a softmax at the model temperature
   turns the candidate scores into the confidence and the ranked alternatives.
7. **Output**: immutable `TaggedSentence` objects with `TaggedToken` values that carry the original text and offsets, and,
   with a glossary, the matches that were found.

**Data path** (build time, not part of the runtime):

1. **Sources**: the open corpus, MASC, Tatoeba and Project Gutenberg text and the dictionary tables are read from
   pinned inputs whose SHA-256 values are recorded
2. **Import**: the tools convert every source to the shared corpus format with the 17 Universal POS tags, remove
   training documents or sentences that overlap evaluation data, and write sorted UTF-8 files with LF line endings
3. **Corrections**: the conventions linter finds the tokens whose label breaks a rule of `data/CONVENTIONS.md`;
   the generator writes the fixes of the mechanical rules and the cases that need a judgment to `data/corrections/`,
   the adjudication tool selects sentences from those cases and from the disagreements of off-the-shelf taggers, has two
   models tag them blind (without the labels) and writes the agreed corrections and the `disputed` marks to
   `adjudicated.tsv`, a validator checks all rows against the corpora, and `corrections.py apply --write` applies the rows
   in effect to the corpus files once. The steps below read the corrected files; `lint.ps1` enforces that the fix tier
   of the linter finds nothing left
4. **Lexicon**: the tools build the spelling-to-tags lexicon from the dictionary tables and the tags attested in the
   training corpora, then measure its coverage of the open training and development sets
5. **Compilation**: `tools/LexiconCompiler` compiles the lexicon table into `Resources/lexicon.bin`, which is committed
   and embedded in the assembly
6. **Training**: the unit `ModelTrainer` reads the training corpora named in `training.json` that have a positive weight
   (the open corpus, MASC, silver and the imperative source), extracts features with
   the same `FeatureExtractor`, restricts the tags with the same lexicon code, trains an averaged perceptron in integer
   arithmetic, quantizes and prunes the weights and writes the `POSW` file with `PerceptronModelWriter`; the file is
   committed as `Resources/weights.bin` and embedded, and `training.json` records the SHA-256 of its uncompressed image.
   Every setting was selected on the development sets only (the run log is `RUN-LOG.md` next to the trainer); `train
   --check` retrains and compares in the build
7. **Checks**: the tools write the leakage report and the checksum manifest
8. **Notices**: `THIRD-PARTY-NOTICES.md` carries the attributions of the sources (the weights included), and
   `PACKAGE-LICENSE.md` is the license of the package; the NuGet package includes both

**Glossary support**: a `Glossary` is an immutable set of domain terms that a caller passes to a `Tag` call. The tagger
finds the terms and gives their tokens hard (`TokenConstraint.Only`) or soft (`TokenConstraint.Prefer`) constraints, so
the decoder is unchanged and the feature set is not extended for it: a trained "glossary says X" feature was measured on
development data only and rejected in favor of score boosts (see _ModelTrainer Design_, _Glossary Decision_). The
default soft strength and the rule for the other words of a term were chosen on development data with the glossary
evaluation of the trainer; the protocol and the measured effect, including the cost of wrong entries, are in section E9
of `RUN-LOG.md` next to the trainer and are not repeated here. See _Glossary Design_ for the data model and algorithms.

**Context Features**: the 22 base templates look at the token and its spelling, its two neighbors on each side and the
two previous tags. `FeatureExtractor` can add four switchable feature groups, and a weights file names the groups it
was trained with in its header, so the decoder always scores with the features of the model (_FeatureExtractor Design_
has the definitions):

- **Position cues** (`PositionCues`, F1): the position in the sentence, the first content word, the sentence end and
  the sentence length.
- **Clause cues** (`ClauseCues`, F2): the classes of the neighbors (determiner, pronoun, preposition, conjunction,
  modal, `to`, number, noun-verb and so on, from the lexicon), the distance to the nearest determiner or modal, and a
  clause state that the decoder updates after every decision (whether a subject-like tag and a verb tag were decided
  since the last clause boundary).
- **Right preview** (`RightPreview`, F3): a second decoding pass that sees the tags of a first pass for the next two
  tokens.
- **Inflection** (`Inflection`, F4): whether the stem of a word that ends in `ing`, `ed` or `s` is a verb or a noun in
  the lexicon.

The embedded model enables the clause cues only. The choice is the result of a pre-registered experiment on development
data: every group alone, all four, and all four minus one were trained with five seeds each and scored on the hard
tranche of the shapes development set (the primary measure), with guards on the other development measures, the size
and the speed. The rule picked the arm with the fewest groups among those within 10% of the best primary measure,
provided that the 95% interval over the seeds of its difference from the previous model's arm lay entirely below
zero. The other groups stay in the
code as switches that are off in `training.json` (the trainer tests prove each of them against the decoder); a model
that enables one of them needs a new training run, a new selection of the thresholds and a new held-out record. The
protocol, the results with their caveats (the evidence is development evidence from one set that the groups were
designed against) and the adoption are in `RUN-LOG.md`, "Context-feature experiment". At run time the clause cues cost
seven more buckets per token, the lexicon lookup that the decoder already does for the restriction, and a clause state
that the decoder carries from token to token; the speed is measured by the throughput requirement and by the trainer,
and the size of the weights is stated under _Data Assets_.

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
  only the result arrays per sentence in the decoder; tagging with a glossary of 10,000 entries keeps the same bound, and
  finding the terms allocates nothing per token for ordinary text. The throughput is enforced in runs without code
  coverage; coverage
  instrumentation slows the hot loops too much to prove it, so a run with coverage (CI) applies a relaxed gate (see the
  verification document)
- **Package size**: the lexicon resource is 639,711 bytes and the weights resource is stated under _Data Assets_ (both
  already compressed), embedded once per target framework, so the four assemblies are 1.2 to 1.3 MB each as stored in
  the package and the package (with the API reference and the XML documentation files) is about 5.7 MB, measured with
  `dotnet pack` of the Release build (the exact size is not stated because it
  is not reproducible: the package holds timestamps and the SBOM)
- **Open licenses only**: No share-alike, non-commercial or no-derivatives data; attribution notices for every
  attribution-licensed source travel with the package, and the package license file covers the embedded data
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

- **NuGet Packaging**: Standard .NET library packaging and distribution, with the package license and the third-party
  notice at the package root and the Markdown API reference under `api/`
- **CI/CD Integration**: Automated build, test, and quality validation
- **Requirements Traceability**: All features linked to passing tests
- **Review Management**: Systematic file review using ReviewMark patterns

### Spike Deviations and the Contract with the Trainer

The decoder is derived from an averaged-perceptron spike. The deviations from it (hash, integer scoring, folding, the
17-tag set, the extra pair features) and the contract with the trainer (feature set version, writer, byte
layouts, temperature and scale) are recorded in _Tagging Design_ and _ModelStorage Design_, and the trainer's side of
the contract is in _ModelTrainer Design_.
