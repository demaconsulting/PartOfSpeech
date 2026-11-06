## ModelStorage

![ModelStorage Structure](ModelStorageView.svg)

### Overview

ModelStorage holds the two binary formats of the library and the code that reads and writes them:

- the **weights file** (magic `POSW`), the averaged-perceptron weights, which callers load from a stream and which the
  package embeds (the trained model of the default tagger);
- the **lexicon file** (magic `POSL`), the compiled word lexicon, which is embedded in the package.

It contains four units:

- **BinaryFormat** (`BinaryFormat.cs`): the primitives both formats share (little-endian fields, varints, checksum,
  header validation, Deflate, population count).
- **Lexicon** (`Lexicon.cs`, `LexiconWriter.cs`): reads, searches and writes the lexicon format and applies the
  token-shape rules that turn lexicon tags into allowed tags.
- **PerceptronModel** (`PerceptronModel.cs`, `PerceptronModelWriter.cs`): reads and writes the weights format and
  scores a feature bucket.
- **EmbeddedModel** (`EmbeddedModel.cs`): lazily loads the lexicon and the weights that are embedded in the assembly.

The subsystem has no public type. Its classes are internal; the tests, the `LexiconCompiler` tool and the trainer
see them through `InternalsVisibleTo`. The subsystem does not know the feature definition: a weights file carries a
feature set number that the caller compares with the one it expects.

### Interfaces

Exposed (internal to the library and its friends):

- `Lexicon.Load`, `Lexicon.TryGetTags`, `Lexicon.AllowedTags`, `LexiconWriter.FromTsv`, `LexiconWriter.Compile`.
- `PerceptronModel.Load`, `PerceptronModel.ToUncompressedImage`, `PerceptronModel.AddScores`, the properties `HashBits`,
  `Temperature` and `Scale`, `PerceptronModelWriter.Compile`.
- `EmbeddedModel.Lexicon` and `EmbeddedModel.Weights`.

Consumed: `UposSet` and `UposExtensions` (Upos); `FeatureExtractor.FeatureSetVersion` (Tagging) by `EmbeddedModel`
and `PerceptronModel.ToUncompressedImage` only, as a constant.

The following points make up the contract with the trainer; the exact byte layouts are in
_PerceptronModel Design_ and _Lexicon Design_:

- The weights writer takes `hashBits` (18 to 24), the temperature, the weight scale, the feature set version and the
  occupied buckets with a dense 17-element `int16` weight vector each, and writes the sparse `POSW` file.
- A reader accepts a weights file only if its feature set version equals the one that its caller passes; the decoder
  passes `FeatureExtractor.FeatureSetVersion`, so changing the extractor requires a new version number.
- `PerceptronModel.ToUncompressedImage` gives the deterministic uncompressed form of a (compressed or plain) weights file;
  the trainer's `--check` compares uncompressed images because Deflate bytes differ between runtimes.
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

`EmbeddedModel` is the composition point. It holds one `Lazy<Lexicon>` and one `Lazy<PerceptronModel>` with
`LazyThreadSafetyMode.ExecutionAndPublication`, so the first access loads the data once even when many threads ask at
the same time, and later accesses return the same instance. Both resources exist in every package; a missing one is an
`InvalidOperationException` (a broken package), never a silent fallback.

The lexicon resource is generated, not hand written. The tool `tools/LexiconCompiler` compiles
`data/lexicon/lexicon.tsv` into `src/DemaConsulting.PartOfSpeech/Resources/lexicon.bin` with `LexiconWriter`, and the
same tool with `--check` (run by `build.ps1`) and a unit test fail when the committed binary does not encode the table.
Compilation at build time was
rejected because the compiler needs the library, the library build has four target frameworks, and a clean checkout
should build with nothing to run. The weights resource is generated the same way by the unit `ModelTrainer`
(`train` writes `src/DemaConsulting.PartOfSpeech/Resources/weights.bin`; `train --check`, run by `build.ps1` and the CI
build, and a unit test fail when the committed binary is not what the committed corpora and `training.json` produce).
The package contains each resource once per target framework: the lexicon resource is 639,711 bytes (1,626,269
uncompressed) and the weights resource 478,835 bytes (1,080,650 uncompressed, 116,662 rows, 282,426 weights, 19 hash
bits, scale 1/8), which is why the package is several MB (see "Package size" in the system design).
