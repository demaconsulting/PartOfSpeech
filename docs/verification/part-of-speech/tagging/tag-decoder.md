### TagDecoder Verification

This document describes the unit-level verification strategy for the `TagDecoder` unit.

#### Verification Approach

The unit is verified by unit tests in `Tagging/TagDecoderTests.cs` that construct a `TagDecoder` directly from a
`PerceptronModel` and a `Lexicon`. The model and lexicon are small files written by the real writers (hand-set weights
through `TinyModels.WeightsBuilder`, so every score and every analytic probability is known), except in the throughput
test. Nothing is mocked. Four methods deserve a description:

- **Analytic confidences.** With hand-set scores and a known temperature, the expected confidence is the softmax
  `exp(s_i / T) / sum(exp(s_j / T))` over the candidates, computed in the test, and the test compares to a tolerance of
  1e-9; tags are compared exactly. The temperature test repeats the case with a lower and a higher temperature and
  asserts that the tag is unchanged while the distribution sharpens or flattens.
- **Platform-independent ties.** Equal scores are given to several tags and the test asserts that the lowest tag number
  wins, which is what makes results the same on every platform.
- **Throughput.** A synthetic weights file of 150,000 rows in 2^21 buckets (`TestSupport/SyntheticModel.cs`, random
  weights from a seeded generator, with the occupied buckets taken from the features of real sentences) is loaded with
  the real embedded lexicon; the real development sentences are decoded on one thread by
  `Tagging/TagDecoderThroughputTests.cs`, using the capability-based method of `TestSupport/ThroughputMeter.cs`: a
  warm-up of at least two runs and 0.5 seconds, then timed windows with `Stopwatch`, each window repeating the run
  over all sentences for at least 0.25 seconds. The first window above the gate ends the measurement and passes; after
  a window below the gate (possibly slowed by other processes) the meter sleeps for 200 milliseconds and takes another
  window, until 30 seconds or 60 windows are used up, and then the best rate is compared with the gate. The test class
  is in the xUnit collection `Throughput` (`DisableParallelization = true`), so that it runs alone in its test host
  after the parallel test classes. The gate is 50,000 tokens per second in every run without code coverage (`build.ps1`),
  where the best measured rate (written to the test output together with the number of windows) is several hundred
  thousand, usually in the first window. In a run with code coverage, as in CI, coverlet's instrumentation slows the
  decoder by a factor of 20 or more (24,000 to 64,000 tokens per second measured), so the meter detects it (see
  _System Verification_, Throughput) and applies a relaxed gate of 5,000 tokens per second; that run proves that the
  decoder is sane, not the 50,000 requirement, which only the non-instrumented run proves.
- **Allocation.** On .NET 8 and later, the noise-robust method of `TestSupport/AllocationMeter.cs` (warm-up of at least
  500 decodes and 0.3 seconds, then the minimum over 10 batches) reads `GC.GetAllocatedBytesForCurrentThread` before and
  after 100 decodes of a
  50-token sentence with 3 alternatives per token, and the bytes per call must not exceed
  `50 * 64 + 150 * 16 + 256` (the result array of tokens, the flat array of alternatives and a small allowance), so
  any per-sentence scratch allocation would fail the test.

#### Test Environment

No setup beyond the standard test runner; the throughput test and the allowed-set test read corpus and lexicon files of
`data/` through `DataFiles.cs`. The throughput test (`Tagging/TagDecoderThroughputTests.cs`) is meaningful in Release
configuration. The allocation test is not compiled for `net481`.

#### Acceptance Criteria

All tests pass. A test fails when a tag outside the candidates is chosen, a tie is not resolved to the lowest tag, a
confidence differs from the softmax by more than the tolerance, an alternative list is unsorted, too long or does not
sum with the confidence to one, a constraint does not have the specified effect, the throughput is 50,000 tokens per
second or less (5,000 or less in a run with code coverage), or the allocation bound is exceeded.

#### Test Scenarios

- **Greedy decoding**: `TagDecoder_Decode_UsesPreviousTagsAsHistory` and `TagDecoder_Decode_TieBreaksToLowestTag`.
- **Restriction**: `TagDecoder_Decode_Restricts_ToLexiconTags` (a known word can only receive its lexicon tags even if the
  weights prefer another), `TagDecoder_Decode_UnknownWord_ConsidersAllTags`, `TagDecoder_Decode_ShapeRules_ExtendAllowedSet`
  (upper-case first letter, digit, no letter or digit) and `TagDecoder_Decode_AllowedSet_MatchesDataFilesRules` (a
  sample of real lexicon spellings and shape variants against the data rule).
- **Confidence**: `TagDecoder_Decode_Confidence_IsSoftmaxAtTemperature`, `TagDecoder_Decode_Temperature_SharpensAndFlattens`,
  `TagDecoder_Decode_Alternatives_AreSortedAndLimited` and `TagDecoder_Decode_SingleAllowedTag_HasConfidenceOne` (no
  alternatives).
- **Constraints**: `TagDecoder_Decode_Constraint_Only_Intersects`,
  `TagDecoder_Decode_Constraint_DisjointFromLexicon_ConstraintWins`, `TagDecoder_Decode_Constraint_Boost_And_Penalty`
  (a boost flips a close decision and a penalty does too), `TagDecoder_Decode_Constraint_PropagatesToNextToken` and
  `TagDecoder_Decode_ReentrantConstraintList_DoesNotCorruptResult` (a constraint list whose indexer decodes another
  sentence on the same thread must not change the outer result, which equals the result with a plain array).
- **Empty input and arguments**: `TagDecoder_Decode_EmptySentence_ReturnsEmpty` (also that invalid arguments are
  rejected).
- **Throughput**: `TagDecoder_Decode_SyntheticModel_ExceedsFiftyThousandTokensPerSecond`.
- **No allocation**: `TagDecoder_Decode_DoesNotAllocateInHotPath` (.NET 8 and later) and
  `TagDecoder_Decode_AllGroups_DoesNotAllocateInHotPath` (the same bound for a model with all groups, which decodes in two
  passes) and, for the lexicon shape rules
  that the decoder applies to every known token, `Lexicon_AllowedTags_KnownTokenShapes_DoNotAllocate` (described in
  the Lexicon verification document).
- **Feature groups**: the decoder follows the groups of the weights header.
  `TagDecoder_Decode_ClauseCues_ImperativeBeforeDeterminer`
  (an ambiguous first word before a determiner becomes a verb; the same weights with a header without the group, and
  the word alone, give a noun), `TagDecoder_Decode_ClauseCues_ClauseStateFollowsChosenTags` (a verb chosen for the first
  token changes the second), `TagDecoder_Decode_PositionCues_UsePositionInSentence`,
  `TagDecoder_Decode_Inflection_SeparatesVerbStemFromLookalike` and
  `TagDecoder_Decode_MatchesReferenceDecoder` (for each of
  the 16 sets of groups a toy model is trained with `TestSupport/TinyTrainer.cs` and the decoder is compared, tag for
  tag, with a plain reference decoder in the test that uses the string-based reference features, the lexicon
  restriction and the lowest-tag tie rule, in two passes for the preview group).
- **Right preview**: `TagDecoder_Decode_RightPreview_SecondPassSeesFirstPassTags` (the second pass sees the first-pass tag
  of the next token, the end of the sentence gives the end marker, a constraint acts in the first pass too, and a header
  without the group ignores the weights), `TagDecoder_Decode_RightPreview_WithoutPreviewWeights_EqualsOnePass` (tags,
  confidences and alternatives equal the one-pass decode of the same weights, so the first pass is the plain decode) and
  `TagDecoder_Decode_RightPreview_IsDeterministic` (repeated and concurrent decodes give identical text).
