<!-- cspell:ignore zorbulon -->

# System Verification Design

This document describes the system-level verification strategy for the Part Of Speech.

## Verification Approach

The Part Of Speech system is verified through system-level integration tests that exercise the library as a whole from
the perspective of a consumer. The tests use only the public API: they create a tagger from a weights stream, tag text,
sentences and token lists, and assert on the observable results, without relying on knowledge of internal
implementation details. No mocking or stubbing is used at the system level. Because the trained model of a later release
does not exist yet, the tests load **tiny models**: small weights files that a test builds in memory with hand-set
weights (or that a 40-sentence deterministic trainer produces), together with the real embedded lexicon. The tiny
models are test infrastructure in `TestSupport/` (`TinyModels.cs`, `TinyTrainer.cs`); they use the same feature
extractor and the same weights writer that the trainer of a later release will use, so the tests also prove that those
can be reused unchanged.

System tests reside in `PartOfSpeechTests.cs` within the `DemaConsulting.PartOfSpeech.Tests` project. The data and
licensing tests reside in `PartOfSpeechDataTests.cs` in the same project, with the helper `DataFiles.cs`; they read the
committed data files, the project file and the third-party notice, and assert on their content, format and checksums;
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
  best rate and the number of windows are written to the test output. The same method at the unit level is in
  _TagDecoder Verification_.
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
  use; in Debug the gate may fail on a slow machine
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, databases, or network access required
- **Isolation**: Each test method creates its own tagger and models; the embedded lexicon is a shared, immutable,
  lazily loaded resource. No test changes shared state
- **Files**: The data and licensing tests and the embedded-lexicon test read `data/`, `THIRD-PARTY-NOTICES.md`, the
  library project file and `src/DemaConsulting.PartOfSpeech/Resources/lexicon.bin` from the working copy. They find the
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
throughput of 50,000 tokens per second or less.

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
- The synthetic weights file is about 0.9 MB with Deflate (random weights compress poorly); a trained model is expected
  to be smaller.
- The embedded lexicon is 641,418 bytes; the package is about 3.2 MB because the resource is embedded once per target
  framework.
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

### Default Model Unavailable

**Test**: `PartOfSpeech_Default_NoEmbeddedModel_ThrowsInvalidOperationException`

Reads `Tagger.Default` and asserts an `InvalidOperationException` whose message says that there is no trained model and
names `Tagger.Load`. This test is replaced in the release that embeds the model.

### Thread Safety

**Test**: `PartOfSpeech_Concurrency_ParallelTagging_MatchesSequential`

Method described under Verification Approach.

### Throughput

**Test**: `PartOfSpeech_Performance_SingleThread_ExceedsFiftyThousandTokensPerSecond`

Method described under Verification Approach; the measured rate is written to the test output.

### No Runtime Dependencies

**Test**: `PartOfSpeech_Packaging_ProjectFile_HasNoRuntimePackageReferences`

Reads the library project file and asserts that every package reference is a private asset (the ApiMark build
reference), so that none flows to consumers, and that the lexicon is an embedded resource.

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
