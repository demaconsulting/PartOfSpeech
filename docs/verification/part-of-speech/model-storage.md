## ModelStorage Verification

This document describes the subsystem-level verification strategy for the ModelStorage subsystem (`BinaryFormat`,
`Lexicon`, `PerceptronModel` and `EmbeddedModel` working together).

### Verification Approach

The subsystem is verified by integration tests in `ModelStorage/ModelStorageTests.cs` that exercise both file formats
together with the real embedded lexicon and the real source table `data/lexicon/lexicon.tsv`. Nothing is mocked. Small
files come from the real writers (`LexiconWriter`, `PerceptronModelWriter`, through the test helper `TinyModels`), so
every test proves a writer and a reader against each other; the committed binary resource is compared with the table in
three independent ways: by decoding it, by looking up every spelling, and by compiling the table afresh. The same
comparison runs outside the tests as the `--check` step of `LexiconCompiler` in `build.ps1`, which compares uncompressed
images because the Deflate bytes depend on the runtime.

### Test Environment

The embedded-lexicon tests read `data/lexicon/lexicon.tsv` and `src/DemaConsulting.PartOfSpeech/Resources/lexicon.bin`
from the working copy (through `DataFiles.cs`, which finds the repository root). The checkout uses LF line endings and
treats `*.bin` as binary (`.gitattributes`). No network is needed.

### Acceptance Criteria

All tests pass. A test fails when a format does not start with its magic and version, a reader accepts another version,
the committed lexicon differs from the table in any spelling or tag (or has an extra spelling), a lookup misses a spelling
of the table or finds one that is not in it, threads receive different lexicon instances, or a written file does not
load back with the same content.

### Test Scenarios

- **Versioned formats**: `ModelStorage_Formats_StartWithMagicAndVersion` writes one file of each format and asserts the
  magic (`POSL`, `POSW`), version 2 and the Deflate flag, then changes the version (to the retired version 1, and to 3)
  and asserts that both readers throw
  `InvalidDataException`.
- **The embedded lexicon encodes the table**: `ModelStorage_EmbeddedLexicon_EncodesLexiconTsv` asserts that the decoded
  embedded lexicon equals the independent reading of the table (spellings, order and tags), that the committed file and
  the embedded resource are the same bytes, and that a fresh compilation equals the uncompressed image of the committed
  file.
- **Every spelling round trips**: `ModelStorage_EmbeddedLexicon_EveryTsvSpellingRoundTrips` looks every one of the 262,635
  spellings up and asserts its tags, and probes spellings that are not in the table (near misses and an over-long
  key) for false positives.
- **Loaded once under parallel first access**: `ModelStorage_EmbeddedLexicon_ParallelFirstAccess_LoadsOnce` releases 16
  threads together and asserts that all receive the same instance. The static cannot be reset, so the test proves a shared
  instance under parallel access, not literally the first access.
- **Weights writer and reader round trip**: `ModelStorage_WeightsWriterAndReader_RoundTrip` loads a Deflate weights file
  and a tiny lexicon from streams and asserts the summed weights of a feature bucket, the temperature, the scale and the
  spelling and entry counts; `ModelStorage_WeightsWriterAndReader_FeatureGroupsRoundTrip` does the same for a weights
  file with feature groups and asserts the mask, the feature set number 2 and the header position of the mask.
- **The embedded weights enable the clause-cue group**: `ModelStorage_EmbeddedWeights_EnableTheClauseCueGroup` asserts
  feature set 2, the mask of the clause-cue group (and no other group) at header offset 40 and zero header bytes from
  offset 44 on in the committed weights file, and the loaded model with exactly that group.
