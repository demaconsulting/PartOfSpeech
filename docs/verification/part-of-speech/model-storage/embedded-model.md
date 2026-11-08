### EmbeddedModel Verification

This document describes the unit-level verification strategy for the `EmbeddedModel` unit.

#### Verification Approach

The unit is verified by unit tests in `ModelStorage/EmbeddedModelTests.cs` against the real assembly: the embedded
lexicon and weights resources are real. Nothing is mocked. The
`Lazy<T>` fields are static and cannot be reset by a test, so the thread tests prove that concurrent access from many
threads returns one shared instance, not that the very first access is concurrent; the first access in a test run
happens in whichever test reaches it first, and the parallel tests are the ones that release their threads together.
The tests of the weights check only what does not depend on the accuracy of the model: that the resource exists, that
it loads (the loader validates the format, the checksum, the feature set and the group mask) with the
hash bits and the scale of the trained model, and that the model is not empty. That the embedded file enables the
clause-cue group is asserted at subsystem level (_ModelStorage Verification_), and that the embedded file is the one that
the training configuration describes is a system test (_System Verification_).

#### Test Environment

No setup beyond the standard test runner: the lexicon and the weights are embedded resources of the library assembly
under test.

#### Acceptance Criteria

All tests pass. A test fails when the lexicon or the weights resource is missing or does not load, the lexicon does not
have the documented counts, the weights do not have the hash bits and scale of the trained model, or repeated or
concurrent access returns different instances.

#### Test Scenarios

- **The lexicon is present and loads**: `EmbeddedModel_Lexicon_IsPresentAndLoads` (the resource is part of the assembly
  and loads with the documented counts).
- **The weights are present and load**: `EmbeddedModel_Weights_IsPresentAndLoads` (the resource is part of the
  assembly and loads as a model with 19 hash bits, scale 1/8, a positive temperature and a large number of rows).
- **One instance**: `EmbeddedModel_Lexicon_SecondAccess_ReturnsSameInstance`,
  `EmbeddedModel_Lexicon_ParallelAccess_ReturnsSingleInstance`, `EmbeddedModel_Weights_SecondAccess_ReturnsSameInstance`
  and `EmbeddedModel_Weights_ParallelAccess_ReturnsSingleInstance` (many threads asking at once all receive one
  instance).
