## ModelStorage

![ModelStorage Structure](ModelStorageView.svg)

### Overview

ModelStorage holds the two binary formats of the library and the code that reads and writes them:

- the **weights file** (magic `POSW`), the averaged-perceptron weights, which callers load from a stream and which a
  later release embeds;
- the **lexicon file** (magic `POSL`), the compiled word lexicon, which is embedded in the package now.

It contains four units:

- **BinaryFormat** (`BinaryFormat.cs`): the primitives both formats share (little-endian fields, varints, checksum,
  header validation, Deflate, population count).
- **Lexicon** (`Lexicon.cs`, `LexiconWriter.cs`): reads, searches and writes the lexicon format and applies the
  token-shape rules that turn lexicon tags into allowed tags.
- **PerceptronModel** (`PerceptronModel.cs`, `PerceptronModelWriter.cs`): reads and writes the weights format and
  scores a feature bucket.
- **EmbeddedModel** (`EmbeddedModel.cs`): lazily loads the lexicon (and, in a later release, the weights) that are
  embedded in the assembly.

The subsystem has no public type. Its classes are internal; the tests, the `LexiconCompiler` tool and the later trainer
see them through `InternalsVisibleTo`. The subsystem does not know the feature definition: a weights file carries a
feature set number that the caller compares with the one it expects.

### Interfaces

Exposed (internal to the library and its friends):

- `Lexicon.Load`, `Lexicon.TryGetTags`, `Lexicon.AllowedTags`, `LexiconWriter.FromTsv`, `LexiconWriter.Compile`.
- `PerceptronModel.Load`, `PerceptronModel.AddScores`, the properties `HashBits`, `Temperature` and `Scale`,
  `PerceptronModelWriter.Compile`.
- `EmbeddedModel.Lexicon` and `EmbeddedModel.Weights`.

Consumed: `UposSet` and `UposExtensions` (Upos); `FeatureExtractor.FeatureSetVersion` (Tagging) by `EmbeddedModel`
only, as a constant.

The following points make up the contract with the future trainer (phase 3); the exact byte layouts are in
_PerceptronModel Design_ and _Lexicon Design_:

- The weights writer takes `hashBits` (18 to 24), the temperature, the weight scale, the feature set version and the
  occupied buckets with a dense 17-element `int16` weight vector each, and writes the sparse `POSW` file.
- A reader accepts a weights file only if its feature set version equals the one that its caller passes; the decoder
  passes `FeatureExtractor.FeatureSetVersion`, so changing the extractor requires a new version number.
- The embedded weights resource is named `DemaConsulting.PartOfSpeech.Resources.weights.bin` and the lexicon resource
  `DemaConsulting.PartOfSpeech.Resources.lexicon.bin`; both names are fixed with a `LogicalName` in the project file, so
  they do not depend on the root namespace.

### Design

Both formats follow one layout: a fixed little-endian header (magic, format version, flags, counts, payload length,
stored length, checksum), then a payload that is optionally compressed with raw Deflate. `BinaryFormat` validates the
magic, the version (both formats are at version 2), the flags, the lengths and the FNV-1a checksum of the stored bytes
before either reader inflates or looks at a payload byte, and then inflates a compressed payload strictly: the Deflate
stream must reach its own end inside the stored region, fill the region exactly and inflate to exactly the declared
payload length, so a truncated or padded file is refused even when its header lengths were adjusted. Every unit can
therefore assume a verified payload and only checks the structure inside it.

A reader never trusts counts: it rebuilds the structure from the payload and validates every invariant (sorted unique
spellings, ascending buckets and tags, no zero weights) at load time, so the lookup and scoring code needs no checks and
cannot fail on malformed data.

`EmbeddedModel` is the composition point. It holds one `Lazy<Lexicon>` and one `Lazy<PerceptronModel?>` with
`LazyThreadSafetyMode.ExecutionAndPublication`, so the first access loads the data once even when many threads ask at
the same time, and later accesses return the same instance. The weights resource does not exist in this release, so
`EmbeddedModel.Weights` is `null`.

The lexicon resource is generated, not hand written. The tool `tools/LexiconCompiler` compiles
`data/lexicon/lexicon.tsv` into `src/DemaConsulting.PartOfSpeech/Resources/lexicon.bin` with `LexiconWriter`, and the
same tool with `--check` (run by `build.ps1`) and a unit test fail when the committed binary does not encode the table.
Compilation at build time was
rejected because the compiler needs the library, the library build has four target frameworks, and a clean checkout
should build with nothing to run. The package contains the resource once per target framework, which is why the package
is about 3.2 MB while the resource is 0.64 MB.
