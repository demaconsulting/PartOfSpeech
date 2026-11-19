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
by asserting which buckets are produced for small hand-picked tokens. Nothing is mocked. That the fast path allocates
nothing is a design property that no test asserts; the opt-in benchmarks measure the allocation of tagging (see *System
Verification*).

#### Test Environment

No setup beyond the standard test runner; the reference-equivalence test reads corpus files of `data/open/`
through `DataFiles.cs`.

#### Acceptance Criteria

All tests pass. A test fails when the fast path and the reference give different buckets, a bucket is outside the bucket
space or depends on earlier use of the workspace, the number or kind of features differs from the definition, the fold
or the shape bits differ.

#### Test Scenarios

- **Feature set**: `FeatureExtractor_Extract_EmitsTwentyTwoTemplates` (a long first word has all 22 templates, a later
  word 21 because only the first word has the initial flag),
  `FeatureExtractor_Extract_ShortToken_OmitsLongPrefixesAndSuffixes` and
  `FeatureExtractor_Extract_TagHistory_ChangesOnlyTagFeatures`.
- **Shape**: `FeatureExtractor_Shape_FollowsSpikeBits` (shape letters and bits of the spike for typical tokens).
- **Initial flag**: `FeatureExtractor_Extract_InitialFlag_SkipsLeadingPunctuation`.
- **Context**: `FeatureExtractor_Extract_Neighbors_UseSentinelsAtBoundaries`.
- **Fold**: `FeatureExtractor_Extract_Fold_MapsTypographicQuotesAndDashes` and
  `FeatureExtractor_Extract_Fold_NfcNormalizesNonAscii` (decomposed and composed spellings give the same features);
  `FeatureExtractor_SetFolded_NormalizedText_EqualsSetToken` (the folding core, given the normalized text, records the
  same characters, flags, shape and hashes as `SetToken` for the raw text, for ASCII, capitalized, typographic,
  decomposed, malformed, supplementary and hyphenated tokens, which makes the word lookup and the decoder see identical
  tokens), `FeatureExtractor_SetFolded_SpanSlice_EqualsTheSameTextAsAToken` (.NET 8 and later: a slice of a longer text
  folds as the same text alone) and `FeatureExtractor_FoldsToDoubleHyphen_AgreesWithTheFoldingOfEveryCodeUnit` (for each
  of the 65,536 UTF-16 code units the helper is true exactly when folding the unit gives `--`). The string-and-offset
  form of the core (`SetFoldedPart`, `netstandard2.0` only) is exercised by the word lookup tests that run on `net481`.
- **Malformed text**: `FeatureExtractor_Extract_Fold_UnpairedSurrogate_UsesReplacementCharacter` (a lone high, a lone
  low and a reversed pair give the features of the same token with U+FFFD, the rest is still normalized, a valid pair is
  kept, and the fast path equals the reference).
- **Reference equivalence**: `FeatureExtractor_Extract_MatchesReferenceStringHashing`.
- **Buckets**: `FeatureExtractor_Buckets_AreInRangeAndStable` and
  `FeatureExtractor_Hash_IsCaseSensitiveWhereTemplatesAre`.
- **Feature groups**: `FeatureExtractor_Extract_NoGroups_ProducesOnlyBaseTemplates` (a workspace that served all groups
  and is configured for none gives the base stride, at most 22 buckets and the buckets of a fresh workspace and of the
  reference, so the switch-off feature stream is that of the 22 templates),
  `FeatureExtractor_Extract_GroupsMatchReference` (all 32 sets of groups against the reference on 60 corpus sentences
  with the embedded lexicon, gold tags as history and preview) and
  `FeatureExtractor_Extract_AllGroups_FillsBucketCapacity` (42 buckets for a token with every feature).
- **Position cues**: `FeatureExtractor_Extract_PositionCues_FollowTheDefinition` (first content token with function
  words, unknown spellings and an all-function sentence; the terminals `?`, `.` behind a closing quote and none;
  position cap) and `FeatureExtractor_Extract_PositionCues_LengthBuckets` (both ends of every length range).
- **Clause cues**: `FeatureExtractor_Extract_ClauseCues_ClassifiesTokens` (each of the 13 classes, the fast path and the
  reference), `FeatureExtractor_Extract_ClauseCues_ImperativeFollowedByDeterminer` (`Check the valve`, a modal and
  `to` before a verb, the distances), `FeatureExtractor_Advance_ClauseState_TracksSubjectVerbAndBoundaries` (the state
  before each token and the reset by `StartPass`) and `FeatureExtractor_Advance_ClauseState_BoundaryTokens` (the tokens
  that start a clause and those that do not).
- **Inflection**: `FeatureExtractor_Extract_Inflection_StemFacts` (the restored `e` of `making`, the undoubled consonant
  of `running` and `stopped`, `ied` and `ies` to `y`, `es`, no candidate for `pass`, `is`, `ing` and `red`, no feature
  for a word without suffix) and `FeatureExtractor_Extract_Inflection_SeparatesAvoidingFromBringAndThing`.
- **Title-case line**: `FeatureExtractor_Extract_TitleCaseLine_FlagArithmetic` (the boundary of the 60 percent test,
  59 and 60 of 100 and the small counts, with fewer than two words after the first one never marked),
  `FeatureExtractor_Extract_TitleCaseLine_WordTokensAndFinalMark` (the first word token is left out, also after
  punctuation and a number; tokens with a digit are no words; a final `.`, `!` or `?` ends the line and another final
  token does not),
  `FeatureExtractor_Extract_TitleCaseLine_FeaturesCarryCapitalizationAndFirst` (the values for an upper-first, an
  all-upper and a lower-case token, the first token, and the constant values on another line),
  `FeatureExtractor_Extract_TitleCaseLine_AddsExactlyTwoBuckets` (for every combination of the other four groups the
  group adds two buckets right behind the static ones and changes no other bucket),
  `FeatureExtractor_Extract_TitleCaseLine_MatchesReference` (the fast path equals the string-based reference for lines
  with digits, marks, an all-upper word and a non-ASCII capital) and
  `FeatureExtractor_Extract_TitleCaseLine_IsDeterministicAndStateless` (the same line gives the same buckets, and the
  line before does not leak into the next).
- **Right preview**: `FeatureExtractor_Extract_RightPreview_UsesFirstPassTagsOfNextTwoTokens` (absent in the first pass,
  four more buckets in the second with the end and beyond markers, inactive without the group).
