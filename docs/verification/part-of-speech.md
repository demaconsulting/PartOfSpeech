<!-- cspell:ignore zorbulon -->

# System Verification Design

This document describes the system-level verification strategy for the Part Of Speech.

## Verification Approach

The Part Of Speech system is verified through system-level integration tests that exercise the library as a whole from
the perspective of a consumer. The tests use only the public API: they create a tagger from a weights stream, tag text,
sentences and token lists, and assert on the observable results, without relying on knowledge of internal
implementation details. No mocking or stubbing is used at the system level. Most tests load **tiny models**: small
weights files that a test builds in memory with hand-set weights (or that a 40-sentence deterministic trainer
produces), together with the real embedded lexicon, so that they do not depend on the quality of the shipped model.
The tiny models are test infrastructure in `TestSupport/` (`TinyModels.cs`, `TinyTrainer.cs`); they use the same
feature extractor and the same weights writer that the trainer uses, so the tests also prove that those can be reused
unchanged. The tests of the default tagger and of the embedded weights use the real shipped model, but only for
properties that do not depend on its accuracy: that it loads, that it is one shared instance, and that it gives the
unmistakable tags of a few hand-written sentences (these sentences were written once and not tuned; the accuracy of the
model is measured by the evaluation of the trainer and of a later release, never by these tests).

System tests reside in `PartOfSpeechTests.cs` within the `DemaConsulting.PartOfSpeech.Tests` project. The data and
licensing tests reside in `PartOfSpeechDataTests.cs` in the same project, with the helper `DataFiles.cs`; they read the
committed data files, the project file, the package license, the third-party notice and the training configuration, and
assert on their content, format and checksums;
each test proves one obligation of one requirement, and a test that finds a file missing or a table empty fails instead
of passing on nothing. The Python tools that produce the data have developer unit tests in `tools/data/tests/`, which are
not requirements evidence; they are reviewed with the tools in the data tools review sets (see the Companion Artifact
Structure of the design introduction).

Two groups of requirements need methods beyond an ordinary assertion:

- **Throughput** (`PartOfSpeech-Lib-Throughput`). The test (`PartOfSpeechThroughputTests.cs`) builds a synthetic weights
  file of the shape of a real model
  (150,000 occupied buckets in a bucket space of 2^21, random weights from a seeded generator, with the occupied
  buckets taken from the features of the first 4,000 sentences of the open training set so that the lookups hit as in
  production), loads it with the real embedded lexicon, and tags the pre-tokenized sentences of the open development set
  on one thread. The measurement (`TestSupport/ThroughputMeter.cs`) is capability-based and noise-robust: a warm-up of
  at least two runs over all sentences and 0.5 seconds (so that tiered compilation is finished), then timed windows
  with `Stopwatch`, each window repeating the run over all sentences until at least 0.25 seconds have elapsed. The
  first window whose rate exceeds the gate ends the measurement and proves the capability. A window below the gate may
  have been slowed by other processes, so the meter sleeps for 200 milliseconds, which lets the contention clear, and
  takes the next window; it gives up only when 30 seconds of windows are used up or 60 windows were taken, and then
  the best rate of all windows is compared with the gate. One fast window is enough to pass, because CPU time stolen
  by other processes can only slow a window and cannot make it faster, while a slow implementation is slow in every
  window and fails. The test class is in the xUnit collection `Throughput`, defined with
  `DisableParallelization = true`, so that it runs alone in its test host, after the parallel test classes have
  finished. The test hosts of the different target frameworks are separate processes that `dotnet test` may run at the
  same time; xUnit cannot serialize them, and the retry with backoff is the protection against that, also on a runner
  with few cores. The gate is 50,000 tokens per second; the measured rate is far higher (see Measured Results). The
  best rate and the number of windows are written to the test output.

  **Where the 50,000 figure is proven.** The 50,000 gate is enforced in every run in which the library is not
  instrumented: `build.ps1` and any local `dotnet test`. The CI pipeline runs the tests with code coverage
  (`--coverlet`), and coverlet rewrites the library to count the execution of every line, which slows the hot loops by a
  factor of 20 or more (a run on the CI runner measured 49,000 tokens per second with coverage, against several hundred
  thousand without). In an instrumented run the 50,000 gate cannot be proven, so the meter detects the instrumentation
  (the library assembly contains coverlet's tracker type, `Coverlet.Core.Instrumentation.Tracker`, or the environment
  variable `CORECLR_ENABLE_PROFILING` or `COR_ENABLE_PROFILING` is `1`) and applies a relaxed gate of 5,000 tokens per
  second instead: one tenth of the requirement and about five times below the slowest instrumented rate measured (23,800
  tokens per second), so that the test still proves that the loop is sane and fails for a loop that is an order of
  magnitude too slow or broken. The relaxed gate is not evidence of the requirement; the evidence of the requirement is
  the non-instrumented run. A failing assertion states the rate, whether the run was instrumented and which gate applied.
  The same method at the unit level is in _TagDecoder Verification_.
- **Thread safety** (`PartOfSpeech-Lib-ThreadSafe`). One tagger is shared by 8 tasks that each tag the fixture texts
  (and a text with non-ASCII characters) 200 times in different orders; every result is compared with the result of a
  sequential run (text, offsets, tags, confidences and alternatives), so that shared scratch memory, a race in a lazy
  initializer or a mutable result would show as a difference.

The allocation behavior (no allocation in the feature extraction after warm-up, only result arrays in the decoder, no
allocation in the lexicon shape rules) is verified at the unit level with `GC.GetAllocatedBytesForCurrentThread`, which
exists on .NET 8 and later; those three tests are not compiled for .NET Framework. The measurement
(`TestSupport/AllocationMeter.cs`) is robust against the runtime's own background activity: a warm-up of at least 500
rounds and 0.3 seconds, so that JIT compilation and tiered compilation are finished, then the minimum of the bytes
allocated over 10 measured batches. An operation that allocates on every round allocates in every batch and so still
fails; only a one-time or sporadic runtime allocation is filtered out.

## Test Environment

- **Framework**: xUnit v3 running under the .NET SDK, on the target frameworks `net8.0`, `net9.0`, `net10.0` and
  `net481` (which stands for `netstandard2.0`)
- **Configuration**: the throughput tests are meaningful in Release configuration, which `build.ps1` and the CI pipeline
  use; in Debug the gate may fail on a slow machine. The 50,000 tokens per second gate applies to runs without code
  coverage (`build.ps1`); the CI pipeline runs with coverage and the tests then apply the relaxed gate of 5,000 tokens
  per second (see the Throughput method above)
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, databases, or network access required
- **Isolation**: Each test method creates its own tagger and models; the embedded lexicon is a shared, immutable,
  lazily loaded resource. No test changes shared state
- **Files**: The data and licensing tests, the embedded-lexicon test and the embedded-weights test read `data/`,
  `PACKAGE-LICENSE.md`, `LICENSE`, `THIRD-PARTY-NOTICES.md`, the library project file,
  `tools/DemaConsulting.PartOfSpeech.Trainer/training.json` and
  `src/DemaConsulting.PartOfSpeech/Resources/lexicon.bin` and `weights.bin` from the working copy. They find the
  repository root by walking up from the test binaries to the solution file, so they run from any checkout location.
  The checkout uses LF line endings (`.gitattributes`), which the checksums depend on. No network access is needed;
  the UD English-EWT treebank is not required.
- **Platform evidence**: the platform requirements are proven by one end-to-end test that runs on the CI matrix (Windows,
  Linux and macOS, with .NET 8, 9 and 10, and .NET Framework 4.8.1 for `netstandard2.0`); the requirements carry source
  filters so that only runs on that platform count.

## Acceptance Criteria

The system passes when every system test passes on every platform and target framework of the CI matrix, the unit and
subsystem tests of all software items pass, and the ReqStream trace matrix shows every requirement covered by a passing
test. A system test fails if any observable result differs from the expected one: a wrong or missing tag, a wrong offset,
an exception of another type than specified, a result that differs between a sequential and a parallel run, or a
throughput of 50,000 tokens per second or less (5,000 tokens per second or less in a run with code coverage).

## External Interface Simulation

The system has no external interfaces requiring simulation. It is a pure in-process .NET library; the only inputs are
strings, token lists and streams, and the only files it reads are the weights stream that the caller supplies and its own
embedded lexicon. System tests supply weights from memory streams and verify returned values and thrown exceptions. The
data and licensing tests read repository files only; they simulate nothing and use no network.

## Measured Results

Recorded from the development of this release (Release configuration, one thread, 26,232 tokens of the open development
set, synthetic model of 150,000 rows in 2^21 buckets, real embedded lexicon, best window after a warm-up); they
are information for reviewers, not acceptance values:

- `Tagger.Tag(IReadOnlyList<string>)`: about 350,000 to 760,000 tokens per second.
- `TagDecoder.Decode`: about 410,000 to 900,000 tokens per second.
- The values vary with the framework and with other test classes running in parallel; the lowest observed value is seven
  times the gate. Both throughput tests write their measured rate to the test output.
- With code coverage (`dotnet test --coverlet`, as in CI), the same tests measured 24,000 to 64,000 tokens per second
  on the development machine and 49,000 tokens per second on the CI runner (Linux, .NET 8 and .NET 9); this is why an
  instrumented run applies the relaxed gate of 5,000 tokens per second and not the requirement of 50,000.
- The synthetic weights file is about 0.9 MB with Deflate (random weights compress poorly); a trained model is expected
  to be smaller.
- The embedded weights are 492,971 bytes (1,111,898 uncompressed) and the embedded lexicon 641,418 bytes (1,635,344
  uncompressed); the package is larger because both resources are embedded once per target framework (the package size
  is stated, approximately, in the "Package size" item of the design document `part-of-speech.md`).
- Tokenizer conformance on the 14,980 sentences of the open corpus: 99.907 percent exact; sentence boundaries on joined
  consecutive sentences: recall 100.00 percent and precision 99.95 percent (the splitter lists were tuned while looking
  at these documents, so these two values are optimistic).

## System-Level Test Scenarios

### Tagging: Embedded Lexicon, Whole Pipeline (Tiny Weights)

**Test**: `PartOfSpeech_Tagging_Text_TagsEveryTokenWithEmbeddedLexicon`

Loads a tiny trained weights file (40 fixture sentences, deterministic trainer) through the public `Tagger.Load(Stream)`,
which uses the embedded lexicon, tags "The cat sat on the mat." and asserts the seven tokens, that every tag is defined
and allowed by the lexicon and token-shape rules, that every confidence is a probability above 0, and the exact tags
DET NOUN VERB ADP DET NOUN PUNCT. This single test exercises the tokenizer, the embedded resource, `DeflateStream`, the
feature extractor, the decoder and the result types, and it is the evidence for the platform requirements on every
operating system and framework.

### Tagging: Several Sentences

**Test**: `PartOfSpeech_Tagging_Text_MultipleSentences_AreSplitAndTagged`

Tags a text of three sentences with three different terminators and asserts the sentence texts, the token counts
(5, 7 and 5) and that the final question mark is tagged PUNCT.

### Offsets: Index the Original String

**Test**: `PartOfSpeech_Tagging_Text_OffsetsIndexTheOriginalString`

Tags a text with leading and repeated spaces, a tab, line breaks, a blank line and a contraction, and asserts two
sentences and that for every sentence and token the substring of the original text at its offset and length equals its
text.

### Pre-Tokenized Input

**Test**: `PartOfSpeech_Tagging_Tokens_AreTaggedAsGiven`

Tags the tokens `She sings don't .` (a token that the tokenizer would split) and asserts that the tokens are returned as
given, that the sentence text is the tokens joined by single spaces, and the tags of the first and the last token.

### Confidence: Probabilities Sum to One and Are Ordered

**Test**: `PartOfSpeech_Tagging_Confidence_ProbabilitiesSumToOneAndAreOrdered`

Tags "The cat saw a zorbulon." with 16 alternatives and asserts for every token that the confidence plus the alternative
probabilities sum to one (to 9 decimal places) and are in descending order, and that the unknown word has 16
alternatives.

### Lexicon Restriction: Known and Unknown Words

**Tests**: `PartOfSpeech_Tagging_KnownWord_StaysWithinAllowedTags`, `PartOfSpeech_Tagging_UnknownWord_MayTakeAnyTag`

The first gives weights that strongly favor INTJ for "the" and ADV for "cat", which the lexicon does not allow, and
asserts that neither is chosen and that every chosen tag is in the allowed set. The second gives an invented word a
strong INTJ weight, asserts that the lexicon allows all 17 tags for it, and that INTJ is chosen with 16 alternatives.

### Constraints: Allowed Set and Boost

**Tests**: `PartOfSpeech_Tagging_Constraint_AllowedSetForcesTag`, `PartOfSpeech_Tagging_Constraint_BoostShiftsCloseDecision`

The first asserts that the word "cat" is NOUN without a constraint and VERB with an allowed set of VERB, with confidence
1 and `IsConstrained` set. The second gives "cat" the scores NOUN 3 and VERB 1 and asserts that a boost of 1 for VERB
keeps NOUN with a lower confidence and a boost of 5 flips the decision.

### Load Model: From Stream and Invalid Files

**Tests**: `PartOfSpeech_Model_LoadFromStream_TagsText`, `PartOfSpeech_Model_InvalidWeights_ThrowInvalidDataException`

The first writes a weights file to a temporary file, loads it from a `FileStream` and asserts the tags of a sentence.
The second asserts that a file of another kind, a truncated file, a file of another format version (the message names
the version), a file with a changed byte (checksum) and an empty stream each throw `InvalidDataException` from
`Tagger.Load`.

### Default Model

**Test**: `PartOfSpeech_Default_EmbeddedModel_TagsTextWithoutFile`

Reads `Tagger.Default`, tags "The cat sat on the mat. She bought two red books." and asserts the unmistakable tags of the
two sentences (DET NOUN VERB ADP DET NOUN PUNCT and PRON VERB NUM ADJ NOUN PUNCT), that every read returns the same
instance, and that eight threads that read the property and tag the same text 20 times each get the same result as the
sequential run. The unit tests of `Tagger.Default` (same instance, parallel first access, four more simple sentences)
and of `EmbeddedModel.Weights` are described in _Tagger Verification_ and _EmbeddedModel Verification_.

### Embedded Weights Are Current

**Test**: `PartOfSpeech_Model_EmbeddedWeights_MatchTrainingConfig` (in `PartOfSpeechDataTests.cs`)

Reads the embedded weights resource and the committed `weights.bin` and asserts that they are the same bytes, loads the
model, and compares its hash bits, scale and temperature with `training.json` and the SHA-256 of its uncompressed image
with `imageSha256` (the values are read from the JSON text with a regular expression, because the JSON API is not part of
.NET Framework). This is cheap and runs in every test run on every framework. The complete proof that the configuration
and the committed corpora produce exactly this model is the retraining check `train --check`, which `build.ps1` and
the CI build run after the build (about 15 seconds; on Windows, Linux and macOS, so a deviation between the operating
systems would fail one of the jobs); its logic is verified by the `ModelTrainer` unit tests (see _ModelTrainer
Verification_). The check is supplementary evidence of the requirement, since a build step is not a test.

**Test**: `PartOfSpeech_Model_EmbeddedWeights_SizeFiguresInDocs_AreCurrent` (in `PartOfSpeechDataTests.cs`)

The same requirement is also served by a test that reads the embedded weights and asserts that the size figures stated in
the documentation (compressed and uncompressed bytes, and the number of rows where a document states it) equal the
embedded file, so a retrained model cannot leave a stale number behind; the message names the document to update. The
run log is excluded because it records the figures of historical runs.

### Weights Come From Committed, Allowed Corpora

**Test**: `PartOfSpeech_Model_TrainingConfig_UsesOnlyCommittedTrainingCorpora` (in `PartOfSpeechDataTests.cs`)

Reads the corpus paths of `training.json` and asserts for each that it is one of the three training corpora, that the file
is committed, that it is not a restricted corpus, that its data folder declares an allowed license, and that the section
of the third-party notice on the embedded weights names MASC, its license, the open corpus and the silver corpus. That
nothing was selected on held-out data is verified by the guard tests of the `ModelTrainer` unit.

### Thread Safety

**Test**: `PartOfSpeech_Concurrency_ParallelTagging_MatchesSequential`

Method described under Verification Approach.

### Throughput

**Test**: `PartOfSpeech_Performance_SingleThread_ExceedsFiftyThousandTokensPerSecond`

Method described under Verification Approach; the measured rate is written to the test output.

### No Runtime Dependencies

**Test**: `PartOfSpeech_Packaging_ProjectFile_HasNoRuntimePackageReferences`

Reads the library project file and asserts that every package reference is a private asset (the ApiMark build
reference), so that none flows to consumers, and that the lexicon and the weights are embedded resources.

### Embedded Lexicon Is Current

**Test**: `PartOfSpeech_Lexicon_Embedded_EncodesLexiconTsv` (in `PartOfSpeechDataTests.cs`)

Reads `data/lexicon/lexicon.tsv` with the test helper (independent of the compiler), looks every spelling up in the
embedded lexicon and compares its tags, and asserts that the embedded lexicon has the same number of spellings and tag
entries, so that nothing is missing, different or extra. The subsystem test
`ModelStorage_EmbeddedLexicon_EncodesLexiconTsv` and the build step `LexiconCompiler --check` additionally compare the
committed binary with a fresh compilation.

### API Documentation Is Packaged

**Tests**: `ApiMark_ApiDocsPackaged`, `PartOfSpeech_XmlDocExamples_Compile`

The FileAssert assertions on the built package and the compile test of the documented examples are described in
_ApiMark Verification_.

### Package License

**Tests**: `PartOfSpeech_PackageLicense_ProjectFile_NamesLicenseFileAtPackageRoot`,
`PartOfSpeech_PackageLicense_File_CoversCodeAndEmbeddedData`, `PartOfSpeech_ThirdPartyNotices_Weights_AreCovered` and the
FileAssert test `Package_LicenseFilesPackaged`

The first asserts that the project file names `PACKAGE-LICENSE.md` as the license file, has no license expression (a
package has one or the other), packs the existing file at the package root. The second asserts that the file starts
with the SPDX expression `MIT AND CC-BY-4.0 AND CC-BY-3.0-US`, contains the MIT text of the repository `LICENSE` file,
names the two embedded files with their Creative Commons licenses and links, and points to the notice. The third
asserts that the
notice covers the weights (the MASC section and the section on the embedded weights). The FileAssert test opens the
built package after `dotnet pack` and asserts that `PACKAGE-LICENSE.md` and `THIRD-PARTY-NOTICES.md` are at the root with
their key statements and that the nuspec names the license file (`<license type="file">`), which proves the packed result
and not just the project file. The reasons for a license file instead of an expression are in the requirement.

### Data and Licensing

**Tests**: the `PartOfSpeech_ThirdPartyNotices_*`, `PartOfSpeech_DataLicensing_*`, `PartOfSpeech_DataProvenance_*`,
`PartOfSpeech_TrainingData_*`, `PartOfSpeech_EvaluationData_*`, `PartOfSpeech_Leakage_*` and `PartOfSpeech_Lexicon_*`
tests of `PartOfSpeechDataTests.cs`

One test per obligation, as named in the requirements of the _Data and Licensing_ section; each reads the committed files
and fails when a file is missing or empty.

### Platform Support

**Test**: `PartOfSpeech_Tagging_Text_TagsEveryTokenWithEmbeddedLexicon` with a source filter per requirement

The platform requirements (Windows, Linux, macOS, .NET 8, .NET 9, .NET 10 and .NET Standard 2.0 through .NET Framework
4.8.1) are proven by the scenario _Tagging: Embedded Lexicon, Whole Pipeline_ passing under each platform and framework.
