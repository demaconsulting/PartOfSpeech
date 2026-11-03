### FeatureExtractor Verification

This document describes the unit-level verification strategy for the `FeatureExtractor` unit.

#### Verification Approach

The unit is verified by unit tests in `Tagging/FeatureExtractorTests.cs`. The fast, allocation-free path (`SetToken`,
`Finish`, `Extract`) is checked against a **reference implementation** inside the unit (`ReferenceBucket` and
`ReferenceBuckets`) that computes the buckets from readable strings in the way the feature set is defined, so the
central test compares both paths over thousands of corpus tokens with varying tag histories and both extreme bucket
space sizes (18 and 24 bits). Single behaviors (shape bits, the initial flag, the boundary markers, the fold) are checked
by asserting which buckets are produced for small hand-picked tokens. The allocation test uses
`GC.GetAllocatedBytesForCurrentThread` through `TestSupport/AllocationMeter.cs` (a thorough warm-up, then the minimum
over 10 batches of 100 rounds), which exists on .NET 8 and later; it is compiled only there.
Nothing is mocked.

#### Test Environment

No setup beyond the standard test runner; the reference-equivalence test reads corpus files of `data/open/`
through `DataFiles.cs`. The allocation test is not compiled for `net481`.

#### Acceptance Criteria

All tests pass. A test fails when the fast path and the reference give different buckets, a bucket is outside the bucket
space or depends on earlier use of the workspace, the number or kind of features differs from the definition, the fold
or the shape bits differ, or extraction allocates after warm-up.

#### Test Scenarios

- **Feature set**: `FeatureExtractor_Extract_EmitsTwentyTwoTemplates` (a long first word has all 22 templates, a later
  word 21 because only the first word has the initial flag), `FeatureExtractor_Extract_ShortToken_OmitsLongPrefixesAndSuffixes`
  and `FeatureExtractor_Extract_TagHistory_ChangesOnlyTagFeatures`.
- **Shape**: `FeatureExtractor_Shape_FollowsSpikeBits` (shape letters and bits of the spike for typical tokens).
- **Initial flag**: `FeatureExtractor_Extract_InitialFlag_SkipsLeadingPunctuation`.
- **Context**: `FeatureExtractor_Extract_Neighbors_UseSentinelsAtBoundaries`.
- **Fold**: `FeatureExtractor_Extract_Fold_MapsTypographicQuotesAndDashes` and
  `FeatureExtractor_Extract_Fold_NfcNormalizesNonAscii` (decomposed and composed spellings give the same features).
- **Reference equivalence**: `FeatureExtractor_Extract_MatchesReferenceStringHashing`.
- **Buckets**: `FeatureExtractor_Buckets_AreInRangeAndStable` and `FeatureExtractor_Hash_IsCaseSensitiveWhereTemplatesAre`.
- **No allocation**: `FeatureExtractor_Extract_DoesNotAllocateAfterWarmUp` (zero bytes after the workspace has grown;
  .NET 8 and later).
