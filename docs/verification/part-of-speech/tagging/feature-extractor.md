### FeatureExtractor Verification

This document describes the unit-level verification strategy for the `FeatureExtractor` unit.

#### Verification Approach

The unit is verified by unit tests in `Tagging/FeatureExtractorTests.cs`. The fast, allocation-free path (`SetToken`,
`Finish`, `Extract`) is checked against a **reference implementation** inside the unit (`ReferenceBucket` and
`ReferenceBuckets`) that computes the buckets from readable strings in the way the feature set is defined, so the
central test compares both paths over thousands of corpus tokens with varying tag histories and both extreme bucket
space sizes (18 and 24 bits). The feature groups have the same kind of oracle: the overload of `ReferenceBuckets` with
groups computes the group features from readable strings and the lexicon (written independently of the character-buffer
code of the fast path), and a theory compares both paths for every set of groups (16 masks), with the real embedded
lexicon and gold tags as history and as preview; hand-picked sentences with a tiny lexicon pin the definitions
(`Check the valve`, `avoiding` against `bring` and `thing`, the clause boundaries). Single behaviors (shape bits, the
initial flag, the boundary markers, the fold) are checked
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
- **Malformed text**: `FeatureExtractor_Extract_Fold_UnpairedSurrogate_UsesReplacementCharacter` (a lone high, a lone
  low and a reversed pair give the features of the same token with U+FFFD, the rest is still normalized, a valid pair is
  kept, and the fast path equals the reference).
- **Reference equivalence**: `FeatureExtractor_Extract_MatchesReferenceStringHashing`.
- **Buckets**: `FeatureExtractor_Buckets_AreInRangeAndStable` and `FeatureExtractor_Hash_IsCaseSensitiveWhereTemplatesAre`.
- **No allocation**: `FeatureExtractor_Extract_DoesNotAllocateAfterWarmUp` (zero bytes after the workspace has grown;
  .NET 8 and later) and `FeatureExtractor_Extract_AllGroups_DoesNotAllocateAfterWarmUp` (the same with all groups, both
  decoding passes and stem lookups).
- **Feature groups**: `FeatureExtractor_Extract_NoGroups_ProducesOnlyBaseTemplates` (a workspace that served all groups
  and is configured for none gives the base stride, at most 22 buckets and the buckets of a fresh workspace and of the
  reference, so the switch-off feature stream is that of the 22 templates),
  `FeatureExtractor_Extract_GroupsMatchReference` (all 16 sets of groups against the reference on 60 corpus sentences
  with the embedded lexicon, gold tags as history and preview) and `FeatureExtractor_Extract_AllGroups_FillsBucketCapacity`
  (40 buckets for a token with every feature).
- **Position cues**: `FeatureExtractor_Extract_PositionCues_FollowTheDefinition` (first content token with function words,
  unknown spellings and an all-function sentence; the terminals `?`, `.` behind a closing quote and none; position cap)
  and `FeatureExtractor_Extract_PositionCues_LengthBuckets` (both ends of every length range).
- **Clause cues**: `FeatureExtractor_Extract_ClauseCues_ClassifiesTokens` (each of the 13 classes, the fast path and the
  reference), `FeatureExtractor_Extract_ClauseCues_ImperativeFollowedByDeterminer` (`Check the valve`, a modal and
  `to` before a verb, the distances), `FeatureExtractor_Advance_ClauseState_TracksSubjectVerbAndBoundaries` (the state
  before each token and the reset by `StartPass`) and `FeatureExtractor_Advance_ClauseState_BoundaryTokens` (the tokens
  that start a clause and those that do not).
- **Inflection**: `FeatureExtractor_Extract_Inflection_StemFacts` (the restored `e` of `making`, the undoubled consonant
  of `running` and `stopped`, `ied` and `ies` to `y`, `es`, no candidate for `pass`, `is`, `ing` and `red`, no feature for
  a word without suffix) and `FeatureExtractor_Extract_Inflection_SeparatesAvoidingFromBringAndThing`.
- **Right preview**: `FeatureExtractor_Extract_RightPreview_UsesFirstPassTagsOfNextTwoTokens` (absent in the first pass,
  four more buckets in the second with the end and beyond markers, inactive without the group).
