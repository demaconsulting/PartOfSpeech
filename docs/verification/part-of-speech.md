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
model is measured by the held-out evaluation of the trainer, which is a documented procedure (once for each frozen
model; each evaluation of the model family is disclosed as such in the evaluation record) and not a test,
and its committed results are tied to the shipped model by the evaluation record tests, never by the tests of the tagger).

System tests reside in `PartOfSpeechTests.cs` within the `DemaConsulting.PartOfSpeech.Tests` project. The data and
licensing tests reside in `PartOfSpeechDataTests.cs` in the same project, with the helper `DataFiles.cs`; they read the
committed data files, the project file, the package license, the third-party notice and the training configuration, and
assert on their content, format and checksums. The evaluation record tests reside in `PartOfSpeechEvaluationTests.cs`;
they read the committed evaluation record (`docs/evaluation`) and the documents that copy figures from it, parse the
JSON with regular expressions and compare the copies with the generated files;
each test proves one obligation of one requirement, and a test that finds a file missing or a table empty fails instead
of passing on nothing. The Python tools that produce the data have developer unit tests in `tools/data/tests/`. Those of
the adjudication tool and the overlay (`test_corrections_*.py`) are requirements evidence for the
`PartOfSpeech-Adjudication-*` requirements: `lint.ps1` runs them through `tools/data/tests/run_trx.py`, which writes
`artifacts/python-data-tools.trx`, the quality-checks job uploads the artifacts folder, and ReqStream reads the file with
the other TRX files and matches a requirement's test by the method name. All other tool tests are not requirements
evidence; they are reviewed with the tools in the data tools review sets (see the Companion Artifact
Structure of the design introduction). The unit tests of the correction tools (`test_corrections_*.py`, including those
of the adjudication tool `adjudicate.py`), the validation of the corrections (`adjudicated.tsv` included) against
the corpora, the state-aware check that the generator reproduces the committed mechanical tables (in the applied state it
reverses the applied rows in a temporary copy first), the conventions linter with `--tier fix --enforce` and a
report-only pass of its check tier run as a gate in `lint.ps1`, which the quality-checks job of the workflow runs
(standard library only, about one minute); a failing step fails the lint run. The linter fails on a violation of the fix
tier; the 298 violations of the check tier that need a judgment are reported and do not fail the run.

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
  The same method at the unit level is in _TagDecoder Verification_. The glossary throughput requirement
  (`PartOfSpeech-Lib-GlossaryThroughput`) uses the same meter and the same two gates for tagging the development sentences
  with the embedded model and a glossary of 10,000 word pairs taken from the training text (the glossary overhead is
  written to the test output as well); its unit-level counterpart, the speed of the matching alone, is in
  _Glossary Verification_.
- **Thread safety** (`PartOfSpeech-Lib-ThreadSafe`). One tagger is shared by 8 tasks that each tag the fixture texts
  (and a text with non-ASCII characters) 200 times in different orders; every result is compared with the result of a
  sequential run (text, offsets, tags, confidences and alternatives), so that shared scratch memory, a race in a lazy
  initializer or a mutable result would show as a difference. A second test does the same with one shared glossary, hard
  and soft, and compares the matches as well.

The allocation behavior (no allocation in the feature extraction after warm-up, only result arrays in the decoder, no
allocation in the lexicon shape rules) is verified at the unit level with `GC.GetAllocatedBytesForCurrentThread`, which
exists on .NET 8 and later; those three tests, and the allocation tests of the glossary (see _Glossary Verification_),
are not compiled for .NET Framework. The measurement
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
  `src/DemaConsulting.PartOfSpeech/Resources/lexicon.bin` and `weights.bin` from the working copy. The evaluation record
  tests read `docs/evaluation/*`, the manifest `data/MANIFEST.tsv`, the pins of the treebank, `README.md`, the user guide,
  `NamespaceDoc.cs`, `TaggedToken.cs` and `.fileassert.yaml`; when the git-ignored local cache of the treebank exists they
  also read its test file to prove that none of its text was committed. They find the
  repository root by walking up from the test binaries to the solution file, so they run from any checkout location.
  The checkout uses LF line endings (`.gitattributes`), which the checksums depend on. No network access is needed;
  the UD English-EWT treebank is not required (the treebank check of the evaluation record tests runs its cache part only
  where a developer has fetched the treebank).
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
- The embedded weights are 521,695 bytes (1,157,499 uncompressed) and the embedded lexicon 639,711 bytes (1,626,269
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
model, and compares its hash bits, scale, temperature and feature groups with `training.json` (the groups named by its
`features` list, none when the member is absent, so the header mask of the file and the configuration cannot differ) and
the SHA-256 of its uncompressed image
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

Reads the corpus paths of `training.json` and asserts for each that it is one of the six training corpora (open, MASC,
silver, imperative, compound and headings), that the file
is committed, that it is not a restricted corpus, that its data folder declares an allowed license, and that the section
of the third-party notice on the embedded weights names MASC, its license, the open corpus, the silver corpus and the
imperative source. That
nothing was selected on held-out data is verified by the guard tests of the `ModelTrainer` unit.

### Evaluation Results Record the Shipped Model

**Test**: `PartOfSpeech_Evaluation_Results_RecordTheShippedModelAndInputs` (in `PartOfSpeechEvaluationTests.cs`)

Reads `training.json`, `operating-points.json`, `test-results.json` and the manifest and asserts that the model image
hash of the selection and of the results equals the `imageSha256` of the configuration, that the selection hash of the
results equals the SHA-256 of the committed bytes of `operating-points.json`, that the rule ids agree, that the hash of
each input that is a committed data file (open test, gold v2 test, shape probe, challenge set) and of each development
file of the selection equals the manifest, and that the hash of the treebank test file equals its pin. It also asserts
that the two reports quote the same model and selection hashes. A retrained model, another selection or a changed data
file therefore fails until the evaluation is redone. The held-out run itself is not repeated by the tests: it is a
procedure of the trainer that runs once for each frozen model (see _ModelTrainer Verification_), and no test opens a
held-out data file.

### Evaluation Record States the Figures of the Reports

**Test**: `PartOfSpeech_Evaluation_Record_StatesTheFiguresOfTheReports`

Reads the figures of `test-results.json` (the harmful-error rate of the open test split, of gold v2 and of the shape
probe, and the rate, coverage and counts of the strict and the balanced point) and asserts that the sentences of
`docs/evaluation/README.md` that state them contain the same figures, formatted as the documents format them. The
figures are quoted from the generated reports, never typed independently.

### Evaluation Record Contains No Treebank Text

**Test**: `PartOfSpeech_Evaluation_Record_ContainsNoTreebankText`

Asserts that the five files of the evaluation record exist, that none contains a text in the form of a treebank sentence
id, and, when the git-ignored cache of the pinned treebank test file is present, that none contains one of its sentence
ids or one of its sentences of 25 or more characters. The cache part is skipped silently in CI, where the treebank is not
fetched; the form check always runs, and the data licensing test `PartOfSpeech_DataLicensing_UdEwt_IsNotCommitted`
asserts that no treebank file is committed.

### Confidence Guidance Is Documented

**Tests**: `PartOfSpeech_Evaluation_ConfidenceGuidance_MatchesTheCommittedFigures` and the FileAssert test
`ApiMark_ConfidenceGuidanceDocumented`

The first reads the committed selection and results and asserts that the bullets of the section "Confidence thresholds"
of
`NamespaceDoc.cs` state the two thresholds with their development and open test figures, that the section states the rate
without a threshold, the advice to use a threshold and the three absolute links to the evaluation record, that the rows
of the user guide and its example state the same thresholds and figures, that `.fileassert.yaml` asserts the same
thresholds, that `TaggedToken` links to the section and states no threshold, and that none of the documents still says
that no threshold is recommended. The FileAssert test opens the generated API reference after the build and asserts that
the namespace page carries the heading, both thresholds, the advice and the links and that the page of
`TaggedToken.Confidence` links to the section, which proves the generated result and not only the source comment. It runs
in `build.ps1` and in the CI build, immediately after the API reference is generated.

### Root README Tables Equal the Record

**Test**: `PartOfSpeech_Evaluation_ReadmeTables_MatchTheCommittedFigures` (in `PartOfSpeechEvaluationTests.cs`)

Parses the two tables of the root `README.md` and compares them with the committed record: each row of the table of
held-out results must state the accuracy (two decimals) and the harmful-error rate per 1,000 decisions (one decimal) of
its set in `test-results.json`, and each row of the table of thresholds must state the threshold of
`operating-points.json` (two decimals) with the coverage and the harmful-error rate per 1,000 answered decisions (one
decimal each) of its point on the open test split in `test-results.json`. It also asserts that the open test split is in
the first table and that the second table has exactly the two points `strict` and `balanced`. The README copy cannot
drift from the record: a reissued evaluation fails this test until the README is updated.

### Glossary: Hard and Soft Entries

**Tests**: `PartOfSpeech_Glossary_HardEntry_ForcesHeadTag`, `PartOfSpeech_Glossary_SoftEntry_PreferenceShiftsCloseDecision`,
`PartOfSpeech_Glossary_DocumentedExamples_GiveTheClaimedResults`

The first gives the word "cat" the scores NOUN 3 and VERB 1, tags a text with a hard glossary entry that says VERB, and
asserts NOUN without the glossary and VERB with it (also when capitalized), with confidence 1, `IsConstrained` and
`IsGlossaryHead`. The second tags with soft entries of a small and of a large strength and asserts that the small
preference keeps NOUN with a lower confidence and the large one gives VERB, with a confidence below 1. The third runs the
examples of the documentation (the technical-name list, the acronym with case sensitivity and the soft example) with the
embedded default model and asserts only what the constraints guarantee: the heads are nouns with confidence 1 also in the
plural and at the start of a sentence, their first words are not verbs, the lower-case pronoun next to the acronym `IT`
is tagged exactly as without a glossary, and the head of the soft match is constrained with a confidence in the range 0
to 1. It never asserts a prediction of the model.

### Glossary: Results

**Test**: `PartOfSpeech_Glossary_Results_ReportMatchesOnTokensAndSentences`

Tags a text of two sentences with a glossary of two entries (one with a definition) and asserts that the first
sentence has no match, that the second lists its two matches in text order with texts that equal the original string at
their offsets and the definition of the entry, that each token of a span carries its match, and that exactly the head
tokens are flagged.

### Thread Safety

**Tests**: `PartOfSpeech_Concurrency_ParallelTagging_MatchesSequential`,
`PartOfSpeech_Concurrency_GlossaryTagging_MatchesSequential`

Method described under Verification Approach.

### Throughput

**Tests**: `PartOfSpeech_Performance_SingleThread_ExceedsFiftyThousandTokensPerSecond`,
`PartOfSpeech_Performance_GlossaryTagging_ExceedsFiftyThousandTokensPerSecond`

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
`PartOfSpeech_DataCorrections_*`, `PartOfSpeech_TrainingData_*`, `PartOfSpeech_EvaluationData_*`, `PartOfSpeech_Leakage_*`,
`PartOfSpeech_ShapesDev_*`, `PartOfSpeech_ShapesDevHard_*`, `PartOfSpeech_ShapesDevNew_*`, `PartOfSpeech_Imperative_*`,
`PartOfSpeech_Compound_*`, `PartOfSpeech_Headings_*`, `PartOfSpeech_TargetedSources_*`
and `PartOfSpeech_Lexicon_*` tests of `PartOfSpeechDataTests.cs`

One test per obligation, as named in the requirements of the _Data and Licensing_ section; each reads the committed files
and fails when a file is missing or empty.

The `PartOfSpeech_DataCorrections_*` tests read the corrections of `data/corrections/`. `Tables_AreWellFormed`
asserts the column lists of the tables, that every correction row (of `mechanical.tsv` and, when it exists,
`adjudicated.tsv`) is valid on its own (legal source and split pair,
token index, tags among the 17 and never the masked placeholder, status cells with known statuses and flags in order, a
change, a provenance of the form `rule:<id>` or `llm:<model>:<run>`), that the rows are in key order, and that no
token has two rows in one table; it fails when the mechanical table or the candidates are empty. A candidate row is
valid on its own when its address, token, span (a positive integer), current tag (one of the 17), current status, candidate
id (lower-case words) and hint and context (not empty) are well formed, in the pending and in the applied state alike.
`RowCases_MatchSharedFixture`
runs the same row validation over `tools/data/tests/corrections-cases.tsv`, one row and one expected verdict each, the file
that the Python validator is tested against, so that the two implementations cannot drift apart; it fails when a verdict
differs or the file has too few cases. `CandidateCases_MatchSharedFixture` does the same for the candidate fields over
`tools/data/tests/candidate-cases.tsv` (the one check that only the Python validator makes is that the candidate id is a
rule or a detector of its registry). `Rows_MatchCommittedCorpora` reads the sentences that the rows name from the
committed corpora and asserts for every correction row in effect and every exception row that the sentence, its document
id, the token index and the token form exist, and that all correction rows in effect match the corpus in the pending
state (old tag and status) or all in the applied state (new tag and status); a mechanical row that an adjudicated row
replaces is not in effect, so it is not compared. A mixture, a row that matches neither state, and an empty table fail.
`Rows_AreApplied` reads the same rows in effect and asserts that none is pending (its token still has the old tag and status
cell) and that every one is applied (its token has the new tag and status cell); a row whose token is missing or has another
value counts as not applied, and an empty table fails. The check that the corpora have no violation of the fix tier of the
conventions linter is made by the linter in `lint.ps1` (`--tier fix --enforce`) and by `tools/data/tests`, not by this test.
`Adjudicated_RowsAreLlmDecisions` asserts for every row of `adjudicated.tsv` that the provenance is
`llm:opus+sol:<batch>` (a `rule:` row fails), that the row is valid on its own and is exactly one of a tag change with
the base status kept, a `disputed` mark with the tag and the flags kept, or a flag change with the tag and the base kept,
and that a token that also has a mechanical row starts from the same old tag and status with another change (the
replacement of the mechanical row); it fails when the table is missing or empty.
`Candidates_AddressExistingTokens` asserts the same for the candidate rows, including that the span stays inside the
sentence. The tests do not depend on the tools; the unit tests of the tools, the validation of the tables, the check that
the generator reproduces the mechanical tables and a report-only conventions linter pass run in `lint.ps1` (see the
Verification Approach).

The hard-shape development set (`data/eval/shapes-dev/`) is covered by the existing tests (its folder is among the
licensed folders, its README among the provenance statements, its source table among the source tables, its corpus among
the evaluation corpora, so the tag set, rejoin, manifest, document and sentence tests read it) and by the
`PartOfSpeech_ShapesDev_*` tests, which read the committed files:
`Header_AnnouncesDevSplit` asserts the `dev split` and development-only header lines;
`Items_HaveShapeRoleTargetAndLemma` parses the file with the trainer's parser and asserts that every shape holds 1 to 120
target items, that controls exist and that every control has the lemma of a target of its shape;
`Quotas_AreMetByTheCommittedSet` asserts exactly 120 targets per shape, no lemma over its cap (4; 12 for n-measure),
exactly 100 controls with at most 2 per lemma and shape, and at most 30% authored sentences per shape and for the
controls (the quota gate of `shapes_dev.py build`, applied to the committed file);
`Targets_MatchShapeClassAndAreNeverDisputed` reads the tags and statuses directly and asserts the class that the shape
and the role ask for (NOUN or PROPN for a noun shape, VERB for a verb shape, the other class for a control) and that no
judged word is `disputed`; `Authored_ShareIsCappedPerShapeAndForControls` asserts at most 30% authored sentences for every
shape and for the controls and that the origin agrees with the authored pseudo-document;
`Tokens_AreReproducedByTheTokenizer` asserts that `Tokenizer.Tokenize` yields one sentence with the stored tokens for
every sentence; `Sources_ListEveryDocumentWithItsCount` asserts that the table lists exactly the documents with the counts
of the file; `Sources_AreOpenAndHaveNoFaaMaterial` asserts a US federal-work, public-domain or project-written basis for
every row and no FAA mention in any cell; `ExcludedSources_AreRecordedAndAbsentFromTheSet` asserts that
`excluded-sources.tsv` (the documents dropped after sourcing: id, URL, reason, decision date) is well formed and that none
of its documents or URLs is used by the set; `ExcludedSources_NothingUsedMatchesByIdOrNormalizedUrl` asserts that the
record lists `nist-hb44-2024` and `nist-hb130-2024` and that no source row and no sentence document of the set has an
excluded id or an excluded URL (compared by lower-case host without `www.` and path without a trailing slash; the same
rule as `check_licenses.py` and the builder, which refuse a match, and fail when the record is missing or malformed);
`LeakageReport_RecordsNoOverlapWithOtherEvaluationSets` asserts that the
committed report names every other evaluation set once with the sentence count of the file and no document overlap, no
exact overlap and no shared probe word 5-gram; `Sentences_AreDisjointFromTheOtherEvaluationSets` asserts, by reading the
evaluation files, that no document and no sentence is shared; `Builder_SeesHeldOutSetsOnlyThroughOpaqueKeys` asserts that
the source of `shapes_dev.py` names no probe file and uses only `held_out_overlap_keys`, `key_overlap`, `gram_overlap`,
`require_sets` and `PROBE_GRAM_WORDS` of the leakage module, and that the probe is read through `sentence_column` only.
Each fails when a file is missing or empty. The developer-run Python tests (`tools/data/tests/test_shapes_dev.py`) add
the construction-time evidence: a probe whose id, shape, lemma, gold and target columns hold garbage (invalid UTF-8
included) gives the same key set and the same candidates and report as a correct one, the braces around the target do
not change the keys, a probe row without a sentence column is an error, the builder holds only a frozen set of integer
hashes, a build that uses a source listed in the committed or the work-folder exclusion table (by id or by URL under
another id) is refused with nothing written, the committed rows are kept in the written record, `check_licenses.py`
reports an excluded id or URL in `dev.tsv` or `sources.tsv` and a missing, zero-byte, header-less or malformed record (a
header-only record is valid), the build and the `candidates`, `decide`, `sample`, `report` and `exclusions` commands
refuse a missing, zero-byte or header-less committed record in both profiles, the decided items of a source that only the
committed record excludes (the work-folder table absent) are dropped by `decide`, the lemma counts, `sample`, `report` and
the build, and a missed quota
(targets per shape, controls, lemma caps, authored share) refuses the build unless `--allow-short` is given. The extended
`Leakage_Report_RecordsNoOverlaps` requires `shapes-dev` and `shapes-dev-hard` among the evaluation sets of every
training set.

The hard tranche (`hard.tsv`) is covered by the same existing tests (its source table is among the source tables and its
corpus among the evaluation corpora) and by the `PartOfSpeech_ShapesDevHard_*` data tests, which read the committed files:
`Header_AnnouncesDevSplitAndCarriesNoSelectionNumbers` asserts the `dev split`, `hard tranche` and development-only header
lines and that the file has no comment key beyond the documented ones (so no q and no tagger output);
`Items_AreWellFormedRealAndNeverDisputed` parses the file with the trainer's parser and asserts the class of the judged
word for its role, that no judged word is `disputed`, that no item is authored, and that every control has the lemma of
a target of its shape; `Quotas_AreMetByTheCommittedTranche` asserts 80/40/70/0/80 to 100/100/100/0/100 targets per
shape
(compound, subject-initial, imperative, measure, modal; no `n-measure` item), 50 to 60 controls, at most 4 targets per
lemma and shape and at most 2 controls per lemma and shape (the quota gate of `shapes_dev.py --tranche hard build`);
`Tokens_AreReproducedByTheTokenizer` asserts one tokenized sentence with the stored tokens;
`Sources_AreListedOpenAndNotExcluded` asserts that `hard-sources.tsv` lists exactly the documents of the file with their
counts, a US federal-work or public-domain basis for every row, no FAA mention, and no excluded id or normalized URL;
`LeakageReport_RecordsNoOverlapAndSharedDocumentsWithTheStandardTranche` asserts that `hard-leakage-report.tsv` names
every other evaluation set and the standard tranche once, with no exact or near-duplicate overlap, no shared probe word
5-gram and no document overlap except with the standard tranche; `Sentences_AreDisjointFromTheOtherEvaluationSets`
asserts, by reading the files, that no sentence is shared with any other evaluation set (the standard tranche included)
and no document except with the standard tranche (and that the tranches do share documents, which is allowed); the
standard `Sentences_AreDisjointFromTheOtherEvaluationSets` treats both tranches as one family.
`PartOfSpeech_ShapesDevHard_Selection_UsesNoPartOfSpeechModel` scans `shapes_dev.py` and the harness
`tools/data/hmm-tags` for a weights file, a decoder, a tagger or the shapes evaluation, asserts that the harness has no
project reference and no reference to this library, and that the solution does not contain it. The developer-run Python
tests `tools/data/tests/test_shapes_dev_hard.py` cover the pipeline on a miniature folder: the `hmm.tsv` reader (header,
count, a `|` in a token, a `#` token that is data), the contested band (the lower edge inside, the upper edge outside),
the order (hardest first), the lemma cap and the budget, the standard-tranche exclusion (an exact key and a
same-document near duplicate, with their reasons), controls without a band, the quota gate (floors, caps, authored
items), the build (`hard.tsv`, `hard-sources.tsv`, ids, no rewrite of the exclusion record, refusal of an excluded
source), the exclusions (every command, both profiles and every round, honors a document listed by URL under another
source id and refuses one id with two different URLs in the committed and work-folder tables, leaving the work folder
unchanged), the hand-check sample (at least half from q below 0.5), the q-band table, rounds, and `check_leakage.py` and
`check_licenses.py` on a hard tranche (a shared document with the standard tranche is allowed, an exact or near
duplicate of it fails). One regression test builds the standard profile on a fixture and compares the bytes with hashes
taken before the hard profile existed; an optional one rebuilds the committed standard files from their work folder
(`SHAPES_DEV_STANDARD_WORK`).

The three single-shape tranches (`compound-head.tsv`, `headings.tsv`, `named-state.tsv`) are covered by the same
existing tests (their source tables are among the source tables and their corpora among the evaluation corpora) and
by the `PartOfSpeech_ShapesDevNew_*` data tests, each of which loops over the three tranches and names the tranche
and the sentence in a failure: `Headers_AnnounceTheTrancheAndTheDevSplit` asserts the `<tranche> tranche` and
`dev split` header and the ids; `Items_AreWellFormedRealAndNeverDisputed` parses the file with the trainer's parser
and asserts the shape, the role, the tag of the judged word (NOUN or PROPN for a target and VERB for a control of
`compound-head` and `named-state`; the exact tag of the `kind` for `headings`), that no judged word is `disputed`
and that no item is authored; `Quotas_AreMetByTheCommittedTranches` asserts the floors and caps (80 to 100 and 15
to 20; 40 to 50 per kind and 12 to 20; 40 to 100 and 12 to 20), at most 4 targets and 2 controls per lemma, at
most 12 `headings` targets per document, no `headings` line that ends in a page number or a page range and at most
25% `-ing` heads in `compound-head`;
`Tokens_AreReproducedByTheTokenizer` asserts one tokenized sentence with the stored tokens;
`Sources_AreListedOpenAndNotExcluded` asserts the exact documents with their counts, an open basis, no FAA mention
and no excluded id or URL; `LeakageReports_RecordNoOverlap` asserts that every report names every other evaluation
set and tranche, with no exact or near-duplicate overlap, no shared probe 5-gram and no shared document except
among the three new tranches; `Sentences_AreDisjointFromTheOtherEvaluationSets` asserts, by reading the files,
that no sentence is shared with any other evaluation set, tranche or the imperative source and no document with
any of them but a new tranche; and `Selection_UsesNoPartOfSpeechModel` scans `shapes_dev_extra.py` for a weights
file, a decoder, a tagger or the shapes evaluation and for a direct read of a held-out set. The developer-run
Python tests `tools/data/tests/test_shapes_dev_extra.py` cover the frames (the complement of the
`n-compound-head` frame, the state-name frame, the heading frames), the heading filter and extraction (a trailing page
number or page range is refused), the exact-tag decision and the frame rules after labeling, the selection caps, the
quota gates, the hand-check sample and gate, and the registry of the shapes. `tools/data/tests/test_shapes_dev.py`
covers that the builders of the standard and hard tranches see the sentence keys of the three newer tranches (a
rebuild cannot take one of their sentences) and that the builder of the newer tranches does not see its own.

The imperative training source (`data/imperative/`) is covered by the existing tests (its folder is among the licensed
folders, its README among the provenance statements, its source table among the source tables, its corpus among the
training corpora, so the tag set, rejoin, manifest, document, sentence, leakage-report and training-configuration tests
read it) and by the `PartOfSpeech_Imperative_*` data tests, which read the committed files:
`Corpus_IsWellFormedAndWithinQuotas` asserts the `use = training` and column header lines and no development-split line,
600 to 900 sentences with the ids `imperative-0001` upward without a gap, a VERB first token that the lexicon lists as
both noun and verb, `unanimous` for every token, at most 6 sentences per lemma (the lower-cased first word, which is the
base form) and at most 30 per document (the quota gate of `imperative_data.py build`, applied to the committed file);
`Sources_AreListedOpenAndNotExcluded` asserts that `sources.tsv` lists exactly the documents of the file with their
counts, a US federal-work or public-domain basis for every row, no FAA mention, no document that the exclusion record of
the development set lists (by id or normalized URL), and that `excluded-documents.tsv` and `dropped-sentences.tsv` have
their columns, hold ids and counts only and name no used document;
`Documents_AreInNoOtherSourceTableAndNoEvaluationSet` asserts that no document id or normalized URL is in the source
tables of open, gold v2, silver, MASC and both tranches of the development set or in its exclusion record, and that no
document is an evaluation document;
`LeakageReport_RecordsNoOverlapWithAnyEvaluationSet` asserts that the committed report names every evaluation set once
for the imperative source, with no document overlap, no exact sentence overlap and no near duplicate (`check_leakage.py`
fails on an imperative near duplicate; the near duplicates of the other training sources are only reported);
`Selection_UsesNoPartOfSpeechModel` scans `imperative_data.py` for a weights
file, a decoder, a tagger or the shapes evaluation and for a probe file or probe reader; `Notice_NamesTheSourceAndItsBasis`
asserts that the third-party notice names `data/imperative/`, 17 U.S.C. 105, the CC0 dedication, the source table and
that the embedded weights were learned from the source (weight 1). Each fails when a file is missing or empty. The generic
`Leakage_Documents_*` and `Leakage_Sentences_*` tests read the imperative corpus as a training corpus and compare it with
every evaluation corpus (both tranches included, the probe through `EvaluationItems`). The developer-run Python tests
`tools/data/tests/test_imperative_data.py` cover the builder on a miniature folder (see `tools/data/README.md`); the
checks `check_leakage.py`, `check_licenses.py` and `check_conventions.py` (fix tier, enforced, imperative source included)
run on the committed files.

The compound-head and headings training sources (`data/compound/` and `data/headings/`) are covered the same way: their
folders are among the licensed folders, their READMEs among the provenance statements, their source tables among the
source tables and their corpora among the training corpora, so the tag set, rejoin, manifest, document, sentence,
leakage-report and training-configuration tests read them. The `PartOfSpeech_Compound_*`, `PartOfSpeech_Headings_*` and
`PartOfSpeech_TargetedSources_*` data tests read the committed files:

- `Compound_Corpus_IsWellFormedAndWithinQuotas` asserts the `use = training` and column header lines and no
  development-split line, 600 to 800 sentences with the ids `compound-0001` upward without a gap, a lower-case head
  that the lexicon lists as both noun and verb, tagged NOUN or PROPN for kind `noun` and VERB after a noun for kind
  `verb` (at most 15 percent), `unanimous` for every token, at least 200 head lemmas, at most 4 sentences per head
  lemma and 20 per document.
- `Headings_Corpus_IsWellFormedAndWithinQuotas` asserts the same header lines, at most 600 lines of 2 to 10 tokens
  without a final period with the ids `headings-0001` upward, the judged word tagged as its stratum says (NOUN, VERB at
  token 0, PROPN), the three strata present with the floors of the noun and proper strata met and no cap exceeded, at
  most 4 lines per lemma and 25 per document.
- `TargetedSources_Sources_AreListedOpenAndNotExcluded` (one case per source) asserts the exact documents with their
  counts, an open basis, no FAA mention, no excluded id or URL, no used document in the record of left-out documents and
  the columns of the records.
- `TargetedSources_Documents_AreInNoOtherSourceTableAndNoEvaluationSet` asserts that no document id or normalized URL
  is in any other source table (the imperative source included) or in the exclusion record, and that no document is an
  evaluation document.
- `TargetedSources_Sentences_AreDisjointFromEveryOtherTrainingSource` asserts, by reading the files, that no sentence of
  either source is a sentence of another training source or of the other new source, and that no document is a document
  of the open, MASC, silver or imperative data (the two new sources share documents, one pool).
- `TargetedSources_LeakageReport_RecordsNoOverlapWithAnyEvaluationSet` and
  `TargetedSources_TrainingLeakageReport_RecordsNoOverlapWithAnotherTrainingSet` assert that the committed reports name
  every evaluation set and every other training set once, with no document overlap (but the shared pool of the two new
  sources), no exact sentence overlap and no near duplicate.
- `TargetedSources_Selection_UsesNoPartOfSpeechModel` scans `compound_heading_data.py` for a weights file, a decoder, a
  tagger or the shapes evaluation and for a probe file or probe reader.
- `TargetedSources_Notice_NamesTheSourcesAndTheirBasis` asserts that the third-party notice names `data/compound/` and
  `data/headings/`, 17 U.S.C. 105, the CC0 dedication, the two source tables and weight 0 (not part of the mix).

The developer-run Python tests `tools/data/tests/test_compound_heading_data.py` cover the builder on a miniature folder,
including the gate of `check_leakage.py` on the near duplicates of both sources and its comparison with the other
training sets, and the lint-only registration of both sources is covered by `tools/data/tests/test_corrections_linter.py`;
the checks `check_leakage.py`, `check_licenses.py` and `check_conventions.py` (fix tier, enforced, both sources
included) run on the committed files.

### Adjudication Tool

**Tests**: the Python unit tests named in the `PartOfSpeech-Adjudication-*` requirements
(`tools/data/tests/test_corrections_adjudication.py`)

Each requirement is proven on a miniature data folder (a few sentences of every source) and a miniature work folder, so
no model is called and no corpus text of the real data is needed. `BlindExportCarriesNoLabels` reads the exported batch
files and asserts exactly three columns, opaque ids, no label, status, flag or sentence id in any cell, and that the key
is outside the `batches` folder. `OutputIsValidated` feeds one correct output and one output per kind of defect (header,
row count, order, copied cell, tag, confidence, flag, a part gap, both a whole file and parts, a BOM, a blank line) and
asserts the error for each. `RetryLimitIsEnforced` runs `validate-output` on an output that stays invalid and asserts
the first attempt is `invalid`, the second is `skipped` with the counter at the limit, a later call changes nothing even
when a valid output appears, the counter never passes the limit when a valid retry follows, and `decide` lists a skipped
batch as incomplete and stages nothing for its sentences. `DecisionRule` runs a table of cases through `decide_token`
(agreement on another tag, agreement on the label, disagreement, flags, a mechanical row) and asserts the outcome, the
new tag, the new status and the row kind, and that every staged row is valid. `DecisionScope` asserts the decision set
of a sentence and that the tokens outside it get no row. `AdjudicatedRowsSupersedeMechanicalRows` asserts the
replacement, every other second row as an error, that validate, apply and the linter use the effective rows, and that
the mechanical file keeps its hash. `DroppedDetectorsLeaveNoCorrections` asserts that a dropped class loses its tag and
flag rows and keeps its `disputed` marks, and the gate on given verdicts. `TableHeaderIsStrict` mutates the metadata
lines of a valid table (each of the five lines removed, repeated or changed, an input line missing, malformed, unknown
or repeated, an unknown metadata line) and asserts a finding for each, and runs `validate` on a file with removed lines.
`WithdrawnRowsStayAsProvenance` withdraws the rows of one detector on the miniature data folder and asserts that the
rows move byte for byte (a dry run writes nothing), that the remaining rows still apply while the withdrawn tokens keep
the old value, that `withdraw` refuses the applied state, an existing table, a bad detector, a blank reason, a detector
without rows and a row that would replace a mechanical row, that a span belongs to every token it covers, and that
`validate` reports each defect of a withdrawn table (header lines, provenance, a token that no candidate of the
detector covers, a token that is also in a current table, the new value in the corpus), mutates its `# input` lines (one
missing, unknown, repeated, malformed or with a stale hash) and asserts a finding for each, and asserts that a complete
table is accepted in the pending and in the applied state (where the corpus files have other hashes by design).

### Platform Support

**Test**: `PartOfSpeech_Tagging_Text_TagsEveryTokenWithEmbeddedLexicon` with a source filter per requirement

The platform requirements (Windows, Linux, macOS, .NET 8, .NET 9, .NET 10 and .NET Standard 2.0 through .NET Framework
4.8.1) are proven by the scenario _Tagging: Embedded Lexicon, Whole Pipeline_ passing under each platform and framework.
