### EmbeddedModel Verification

This document describes the unit-level verification strategy for the `EmbeddedModel` unit.

#### Verification Approach

The unit is verified by unit tests in `ModelStorage/EmbeddedModelTests.cs` against the real assembly: the embedded
lexicon resource is real and the absence of a weights resource is a real property of this build. Nothing is mocked. The
`Lazy<T>` fields are static and cannot be reset by a test, so the thread test proves that concurrent access from many
threads returns one shared instance, not that the very first access is concurrent; the first access in a test run
happens in whichever test reaches it first, and the parallel test is the one that releases its threads together.

#### Test Environment

No setup beyond the standard test runner: the lexicon is an embedded resource of the library assembly under test.

#### Acceptance Criteria

All tests pass. A test fails when the lexicon resource is missing or does not load with the documented counts, repeated
or concurrent access returns different instances, or a build without a weights resource does not report `null`.

#### Test Scenarios

- **The lexicon is present and loads**: `EmbeddedModel_Lexicon_IsPresentAndLoads` (the resource is part of the assembly
  and loads with the documented counts).
- **One instance**: `EmbeddedModel_Lexicon_SecondAccess_ReturnsSameInstance` and
  `EmbeddedModel_Lexicon_ParallelAccess_ReturnsSingleInstance` (many threads asking at once all receive one instance).
- **No weights**: `EmbeddedModel_Weights_NoResource_ReturnsNull` (reports no weights instead of failing). This test is
  replaced by a test that the embedded weights load when a trained model is embedded.
