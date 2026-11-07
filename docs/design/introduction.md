# Introduction

This document provides the detailed design for the Part Of Speech, a .NET library for part-of-speech tagging of
English text with the 17 Universal POS tags.

## Purpose

The purpose of this document is to serve as the design entry point and provide detailed design
specifications for the Part Of Speech system. This documentation enables formal code
review by providing implementation specifications, supports compliance auditing by maintaining
clear traceability from requirements through design to code, aids maintenance by documenting
system structure and interactions, and ensures quality assurance through detailed technical
specifications.

This document is intended for:

- Software developers implementing and maintaining the system
- Code reviewers validating implementation against design
- Compliance auditors tracing requirements through design to implementation
- Quality assurance teams validating system behavior

## Scope

This document covers the detailed design of the Part Of Speech system and its constituent
software items, specifically:

- **PartOfSpeech (System)** — The complete .NET part-of-speech tagging library
- **Upos (Unit)** — The tag vocabulary, its text forms and sets of tags
- **ModelTrainer (Unit)** — The build-time tool that trains the embedded weights, selects settings on development data
  only and checks that the committed weights are current
- **Tokenization (Subsystem)** — Splits text into sentences and tokens in the convention of the training data
  - **Tokenizer (Unit)** — Word-level tokenization and creation of sentences from given tokens
  - **SentenceSplitter (Unit)** — Groups tokens into sentences
- **ModelStorage (Subsystem)** — The weights and lexicon binary formats and the embedded lexicon
  - **BinaryFormat (Unit)** — Primitives that both formats share
  - **Lexicon (Unit)** — Reads, searches and writes the compiled lexicon and applies the token-shape rules
  - **PerceptronModel (Unit)** — Reads, writes and scores with the perceptron weights
  - **EmbeddedModel (Unit)** — Lazy, thread-safe access to the data embedded in the package
- **Tagging (Subsystem)** — Assigns tags with confidences, alternatives, constraints and glossary terms
  - **FeatureExtractor (Unit)** — The single definition of the hashed features
  - **TagDecoder (Unit)** — The greedy decoder with lexicon restriction and softmax confidence
  - **TokenConstraint (Unit)** — The caller's restriction or preference for the tag of one token
  - **Glossary (Unit)** — The caller's domain terms with allowed tags, their matching and their constraints
  - **Tagger (Unit)** — The public entry point, its options and the result types
- **Data assets and tools (not software items)** — Training corpora, evaluation sets, the lexicon, the third-party
  notice and the package license, covered at system level together with the build-time tools that produce and compile
  the data and the lexicon

The following OTS items are also covered:

- **ApiMark** — API reference generation tool
- **BuildMark** — build-notes documentation tool
- **FileAssert** — document assertion tool
- **Pandoc** — Markdown-to-HTML conversion tool
- **ReqStream** — requirements traceability tool
- **ReviewMark** — file review enforcement tool
- **SarifMark** — SARIF report conversion tool
- **SonarMark** — SonarCloud quality report tool
- **SysML2Tools** — architecture model lint and diagram rendering tool
- **VersionMark** — tool-version documentation tool
- **WeasyPrint** — HTML-to-PDF conversion tool
- **xUnit** — unit-testing framework

Version applicability: This design applies to all versions of the Part Of Speech.

The following topics are explicitly excluded from this design documentation:

- External library internals and third-party OTS components
- Build pipeline configuration and CI/CD processes
- Deployment, packaging, and distribution mechanisms
- Infrastructure and hosting environment details
- Test projects and test infrastructure

## Software Structure

The software structure is modeled in SysML2 under `docs/sysml2/` and rendered to the
diagram below by SysML2Tools as part of the build pipeline. AI agents should query the
SysML2 model directly (see the `sysml2tools-query` skill) rather than parsing this
diagram or the prose below.

![Software Structure](SoftwareStructureView.svg)

The system has two units directly under it (`Upos`, the tag vocabulary that every part uses, and `ModelTrainer`, the
build-time tool that trains the embedded weights) and three subsystems:
`Tokenization` (text to sentences and tokens), `ModelStorage` (the binary formats of the weights and the lexicon and the
embedded lexicon and weights) and `Tagging` (features, decoding, constraints, glossaries and the public `Tagger`). The dependencies
between them have no unit-level call cycle, but the subsystem graph is not acyclic: `Tagging` uses `Tokenization`,
`ModelStorage` and `Upos`; `ModelStorage` uses `Upos` and, for constants (`PerceptronModel` and its writer read the
feature set versions and the known feature groups of `FeatureExtractor`), `Tagging`; `Tokenization` uses nothing else.
`ModelTrainer` is not part of the
runtime: it uses `Tagging` (the feature extractor, the decoder), `ModelStorage` (the weights writer and reader, the
lexicon) and `Upos` through `InternalsVisibleTo`, and nothing in the library uses it. Each unit is one class or a few
tightly coupled files, as listed in the folder layout below. The Trainer is a software unit although it is a build-time
tool (unlike `tools/LexiconCompiler`), because its headline claims (determinism, no drift from the runtime definitions,
development-only selection, provenance of the embedded model) need requirement-level evidence.

## Folder Layout

The source code folder structure mirrors the software structure organization, with file paths
and descriptions as follows. All public types are in the single namespace `DemaConsulting.PartOfSpeech`; the folders
are not namespaces.

```text
src/DemaConsulting.PartOfSpeech/
├── Upos.cs                     — The 17 tags (enumeration)
├── UposExtensions.cs           — Tag names and the tag count
├── UposSet.cs                  — Immutable set of tags
├── NamespaceDoc.cs             — API reference text of the namespace (not a software item)
├── Tokenization/
│   ├── Tokenizer.cs            — Word rules and pre-tokenized input
│   ├── Token.cs                — Token value type
│   ├── Sentence.cs             — Sentence result type
│   └── SentenceSplitter.cs     — Sentence boundary rules
├── ModelStorage/
│   ├── BinaryFormat.cs         — Shared primitives of both formats
│   ├── Lexicon.cs              — Lexicon reader, search and token-shape rules
│   ├── LexiconWriter.cs        — Lexicon table parser and compiler (build-time and tests)
│   ├── PerceptronModel.cs      — Weights reader and scoring
│   ├── PerceptronModelWriter.cs — Weights writer (used by the Trainer and the tests)
│   └── EmbeddedModel.cs        — Lazy access to the embedded resources
├── Tagging/
│   ├── FeatureExtractor.cs     — The hashed feature definition
│   ├── DecoderWorkspace.cs     — Per-thread scratch memory of the decoder
│   ├── ScoreSlice.cs           — Read-only list over the alternatives array
│   ├── TagDecoder.cs           — Greedy decoder
│   ├── TokenConstraint.cs      — Per-token constraint value
│   ├── Glossary.cs             — Immutable set of domain terms, matching of a sentence
│   ├── GlossaryBuilder.cs      — Collection-initializer builder
│   ├── GlossaryEntry.cs        — One term with its tags, head, definition and case, mode and inflection settings
│   ├── GlossaryOptions.cs      — Glossary-wide settings and the hard or soft mode
│   ├── GlossaryMatch.cs        — Result type: one found term
│   ├── GlossaryIndex.cs        — Trie matcher over the folded tokens of every spelling
│   ├── GlossaryWorkspace.cs    — Per-thread scratch memory of the matcher
│   ├── GlossaryInflector.cs    — Regular noun and verb forms of the head word
│   ├── GlossaryConstraints.cs  — Per-token constraints of a glossary, compiled for one tagger
│   ├── Tagger.cs               — Public entry point
│   ├── TaggerOptions.cs        — Options
│   ├── TaggedSentence.cs       — Result type
│   ├── TaggedToken.cs          — Result type
│   └── TagScore.cs             — Result type
└── Resources/
    ├── lexicon.bin             — Compiled lexicon (generated by tools/LexiconCompiler, committed, embedded)
    └── weights.bin             — Trained weights (generated by the Trainer, committed, embedded)
```

`DecoderWorkspace.cs` and `ScoreSlice.cs` are internal helpers of the decoder and belong to the `TagDecoder` unit.
`Token.cs` and `Sentence.cs` belong to the `Tokenizer` unit; `LexiconWriter.cs` to `Lexicon`;
`PerceptronModelWriter.cs` to `PerceptronModel`; the nine `Glossary*.cs` files to `Glossary`; and `TaggerOptions.cs`,
`TaggedSentence.cs`, `TaggedToken.cs` and `TagScore.cs` to `Tagger`.

The unit `ModelTrainer` is the tool project `tools/DemaConsulting.PartOfSpeech.Trainer/`, which is not shipped:

```text
tools/DemaConsulting.PartOfSpeech.Trainer/
├── Program.cs                  — Command line (train, eval, calibrate, compare, glossary-probe, glossary-experiment,
│                                 glossary-eval, thresholds, evaluate)
├── ConfigFile.cs               — Reads and writes training.json (System.Text.Json)
├── OperatingPointsFile.cs      — Reads and writes the selection and the results of the evaluation (System.Text.Json)
├── RunLog.cs                   — Formats the rows of the run log table
├── training.json               — The selected, committed configuration (including imageSha256)
├── README.md                   — How to run it, the determinism contract, the held-out gate
├── RUN-LOG.md                  — The pre-registered rules, the dev-only run log, the glossary decisions and the
│                                 protocol and outcome of the held-out evaluation
├── Training/                   — Corpus reader, random generator, integer averaged perceptron, trainer, builder, predictor
└── Evaluation/                 — Data guard, development and held-out sets, metrics, bootstrap, calibration, confidence
                                  curve and selection, held-out evaluator, reports, glossary experiment and evaluation
```

Further top-level folders hold files that are not software items and are therefore not part of the software
structure:

```text
data/                           — Training corpora, evaluation sets, lexicon, label corrections, checksums, leakage report
docs/evaluation/                — The evaluation record: thresholds, held-out results, how to read and reproduce them
tools/data/                     — Build-time Python tools and their tests that produce, lint and check the data
tools/LexiconCompiler/          — Build-time C# tool that compiles the lexicon table into Resources/lexicon.bin
PACKAGE-LICENSE.md              — The license of the NuGet package (MIT code and CC BY data), packed at the package root
THIRD-PARTY-NOTICES.md          — Third-party attribution and license notices, packed in the NuGet package
```

## Document Conventions

Throughout this document:

- Class names, method names, property names, and file names appear in `monospace` font.
- The word **shall** denotes a design constraint that the implementation must satisfy.
- Section headings within each unit chapter follow a consistent structure: overview, data model,
  methods/algorithms, and interactions with other units.
- Text tables are used in preference to diagrams, which may not render in all PDF viewers.

## Companion Artifact Structure

Each software item has corresponding artifacts in parallel directory trees:

- Requirements: `docs/reqstream/{system}/.../{item}.yaml` (kebab-case)
- Design docs: `docs/design/{system}/.../{item}.md` (kebab-case)
- Verification design: `docs/verification/{system}/.../{item}.md` (kebab-case)
- Source code: `src/{System}/.../{Item}.cs` (PascalCase for C#)
- Tests: `test/{System}.Tests/.../{Item}Tests.cs` (PascalCase for C#)
- SysML2 model: `docs/sysml2/model/{system}/.../{item}.sysml` (kebab-case)
- Review-sets: defined in `.reviewmark.yaml`

The data assets are not software items and have no parallel design or verification files. Their system-level
requirements are in the _Data and Licensing_ section of `docs/reqstream/{system}.yaml` (and the package license and the
embedded model requirements in the same file), their tests are in
`test/{System}.Tests/{System}DataTests.cs`, and the data folders carry their own `README.md` files. The evaluation record
`docs/evaluation/` is not a software item either: its generated files are produced by the unit `ModelTrainer`, its
`README.md` and the copies of its figures in the package documentation are covered by the system requirements
`PartOfSpeech-Model-EvaluationReported` and `PartOfSpeech-Lib-ConfidenceGuidance`, and their consistency tests are in
`test/{System}.Tests/{System}EvaluationTests.cs`. The build-time
tools have developer unit tests in `tools/data/tests/`, which are not requirements evidence. The Python files of the
tools and their tests are reviewed in three review-sets of `.reviewmark.yaml`: `PartOfSpeech-DataConversion` (the tools
that import and convert data and build the lexicon), `PartOfSpeech-DataChecks` (the tools that check, record and
verify it) and `PartOfSpeech-DataCorrections` (the conventions linter, the generator and validator of the
corrections in `data/corrections/` and the adjudication tool, with their tests and fixtures); the system design and the
system requirements are
their context. The data tools run in two places: `lint.ps1` runs the unit tests of the correction tools, the validation
and the state-aware regeneration check of the correction tables, the fix tier of the linter with `--enforce` and a
report-only pass of the check tier (the quality-checks job of the workflow runs
`lint.ps1`, and it is the only workflow entry that can host Python checks), while the C# tests of the system read the
committed tables and corpora and are the requirements evidence. The C# tool `tools/LexiconCompiler` has
its own review-set, `PartOfSpeech-LexiconCompiler`; it has no requirements of its own because its result is proven by
the system requirement `PartOfSpeech-Data-EmbeddedLexiconCurrent`. The Trainer, in contrast, is the unit `ModelTrainer`
with the full set of artifacts: `docs/reqstream/{system}/model-trainer.yaml`, `docs/design/{system}/model-trainer.md`,
`docs/verification/{system}/model-trainer.md`, `docs/sysml2/model/{system}/model-trainer.sysml`, the tests in
`test/{System}.Tests/ModelTrainer/ModelTrainerTests.cs` (the engine files of the tool are compiled into the test
project, so the tests run the real code on every target framework) and the review-set `PartOfSpeech-ModelTrainer`.

Test files follow the same tree as the sources: the subsystem tests are `Tokenization/TokenizationTests.cs`,
`ModelStorage/ModelStorageTests.cs` and `Tagging/TaggingTests.cs`, the unit tests are named `{Unit}Tests.cs` in the
folder of their subsystem (`UposTests.cs` for the unit directly under the system, and `ModelTrainer/ModelTrainerTests.cs`
for the trainer; the unit `Glossary` has `GlossaryTests.cs`, `GlossaryMatchingTests.cs` and `GlossaryThroughputTests.cs`),
and the system tests are `PartOfSpeechTests.cs`, `PartOfSpeechDataTests.cs` and `PartOfSpeechEvaluationTests.cs`. The
helpers `TestSupport/TinyModels.cs`, `TinyTrainer.cs` and
`SyntheticModel.cs` build small and synthetic models for the tests and belong to the `Tagging` review-set.

## References

- Part Of Speech User Guide — the compiled User Guide document for this repository.
- Part Of Speech Repository — the PartOfSpeech source repository hosted on
  GitHub.
