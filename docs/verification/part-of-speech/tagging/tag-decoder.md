### TagDecoder Verification

This document describes the unit-level verification strategy for the `TagDecoder` unit.

#### Verification Approach

The unit is verified by unit tests in `Tagging/TagDecoderTests.cs` that construct a `TagDecoder` directly from a
`PerceptronModel` and a `Lexicon`. The model and lexicon are small files written by the real writers (hand-set weights
through `TinyModels.WeightsBuilder`, so every score and every analytic probability is known), except in the throughput
test and in the word lookup tests, which use the real embedded lexicon (the lookup of every spelling is compared with
the decode of the same word, because a sample could miss a rule). Nothing is mocked. Four methods deserve a description:

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
  warm-up of at least two runs and 0.5 seconds, then timed windows with `Stopwatch`, each window repeating the run over
  all sentences for at least 0.25 seconds. The first window above the gate ends the measurement and passes; after a
  window below the gate (possibly slowed by other processes) the meter sleeps for 200 milliseconds and takes another
  window, until 30 seconds or 60 windows are used up, and then the best rate is compared with the gate. The test class
  is in the xUnit collection `Throughput` (`DisableParallelization = true`), so that it runs alone in its test host
  after the parallel test classes. The gate is 50,000 tokens per second in every run without code coverage
  (`build.ps1`), where the best measured rate (written to the test output together with the number of windows) is
  several hundred thousand, usually in the first window. In a run with code coverage, as in CI, coverlet's
  instrumentation slows the decoder by a factor of 20 or more (24,000 to 64,000 tokens per second measured), so the
  meter detects it (see *System Verification*, Throughput) and applies a relaxed gate of 5,000 tokens per second; that
  run proves that the decoder is sane, not the 50,000 requirement, which only the non-instrumented run proves.
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
second or less (5,000 or less in a run with code coverage), the allocation bound is exceeded, the size of the result
token grows beyond 48 bytes on a 64-bit process, a token reports the known-word flag against the lexicon, or a word
lookup differs from the decoder's allowed set or known flag for any spelling of the embedded lexicon or any sample
shape, finds a hyphenated spelling or a compound head wrongly, disturbs a decode, or allocates for a huge input.

#### Test Scenarios

- **Greedy decoding**: `TagDecoder_Decode_UsesPreviousTagsAsHistory` and `TagDecoder_Decode_TieBreaksToLowestTag`.
- **Restriction**: `TagDecoder_Decode_Restricts_ToLexiconTags` (a known word can only receive its lexicon tags even if
  the weights prefer another), `TagDecoder_Decode_UnknownWord_ConsidersAllTags`,
  `TagDecoder_Decode_ShapeRules_ExtendAllowedSet` (upper-case first letter, digit, no letter or digit) and
  `TagDecoder_Decode_AllowedSet_MatchesDataFilesRules` (a sample of real lexicon spellings and shape variants against
  the data rule).
- **Confidence**: `TagDecoder_Decode_Confidence_IsSoftmaxAtTemperature`,
  `TagDecoder_Decode_Temperature_SharpensAndFlattens`, `TagDecoder_Decode_Alternatives_AreSortedAndLimited` and
  `TagDecoder_Decode_SingleAllowedTag_HasConfidenceOne` (no alternatives).
- **Constraints**: `TagDecoder_Decode_Constraint_Only_Intersects`,
  `TagDecoder_Decode_Constraint_DisjointFromLexicon_ConstraintWins`, `TagDecoder_Decode_Constraint_Boost_And_Penalty`
  (a boost flips a close decision and a penalty does too), `TagDecoder_Decode_Constraint_PropagatesToNextToken` and
  `TagDecoder_Decode_ReentrantConstraintList_DoesNotCorruptResult` (a constraint list whose indexer decodes another
  sentence on the same thread must not change the outer result, which equals the result with a plain array).
- **Empty input and arguments**: `TagDecoder_Decode_EmptySentence_ReturnsEmpty` (also that invalid arguments are
  rejected).
- **Throughput**: `TagDecoder_Decode_SyntheticModel_ExceedsFiftyThousandTokensPerSecond`.
- **No allocation**: `TagDecoder_Decode_DoesNotAllocateInHotPath` (.NET 8 and later) and
  `TagDecoder_Decode_AllGroups_DoesNotAllocateInHotPath` (the same bound for a model with all groups, which decodes in
  two passes), `TagDecoder_Decode_ResultToken_KeepsItsSize` (the size of `TaggedToken` is at most 48 bytes on a 64-bit
  process, which the per-token part of the allocation bound counts on) and, for the lexicon shape rules that the decoder
  applies to every known token, `Lexicon_AllowedTags_KnownTokenShapes_DoNotAllocate` (described in the Lexicon
  verification document).
- **Known word**: `TagDecoder_Decode_KnownWord_ReportsLexiconMembership` (a word of one tag, a word of two tags and an
  absent word), `TagDecoder_Decode_KnownWord_IgnoresLetterCase` (`cat`, `Cat`, `CAT` and `cAt` are known, an unlisted
  spelling is not, and a typographic apostrophe variant of `don't` finds the entry),
  `TagDecoder_Decode_KnownWord_ShapeRulesDoNotMakeAWordKnown` (an unlisted capitalized word, number and punctuation mark
  are unknown and keep 17 candidates, the listed ones are known),
  `TagDecoder_Decode_KnownWord_IsIndependentOfConstraints` (hard and soft constraints, also one that is disjoint from
  the lexicon tags, leave the flag unchanged), `TagDecoder_Decode_KnownWord_AllGroups_EqualsPlainDecode` (a model with
  all groups, which uses the cached lexicon facts and two passes, reports the flags of a model without groups) and
  `TagDecoder_Decode_SingleAllowedTag_HasNoAlternativesAndSoftConstraintKeepsThem` (a theory over 1, 3 and 16
  alternatives: a token with one allowed tag, from a hard constraint on a listed or an unlisted word, has no
  alternatives; a constraint with two tags has one; a soft constraint with the boost 1e9 gives the confidence 1.0, the
  margin 1.0 and the alternative with the probability 0, so the number of alternatives, not the confidence, tells a
  forced tag).
- **Feature groups**: the decoder follows the groups of the weights header.
  `TagDecoder_Decode_ClauseCues_ImperativeBeforeDeterminer`
  (an ambiguous first word before a determiner becomes a verb; the same weights with a header without the group, and
  the word alone, give a noun), `TagDecoder_Decode_ClauseCues_ClauseStateFollowsChosenTags` (a verb chosen for the first
  token changes the second), `TagDecoder_Decode_PositionCues_UsePositionInSentence`,
  `TagDecoder_Decode_Inflection_SeparatesVerbStemFromLookalike`,
  `TagDecoder_Decode_TitleCaseLine_TitleLineChangesTheDecision` (the first word of a Title Case line is turned into a
  verb by the line feature; the same words with another line, a line that ends as a sentence, and a header without the
  group give a noun) and
  `TagDecoder_Decode_MatchesReferenceDecoder` (for each of
  the 32 sets of groups a toy model is trained with `TestSupport/TinyTrainer.cs` and the decoder is compared, tag for
  tag, with a plain reference decoder in the test that uses the string-based reference features, the lexicon
  restriction and the lowest-tag tie rule, in two passes for the preview group).
- **Right preview**: `TagDecoder_Decode_RightPreview_SecondPassSeesFirstPassTags` (the second pass sees the first-pass
  tag of the next token, the end of the sentence gives the end marker, a constraint acts in the first pass too, and a
  header without the group ignores the weights), `TagDecoder_Decode_RightPreview_WithoutPreviewWeights_EqualsOnePass`
  (tags, confidences and alternatives equal the one-pass decode of the same weights, so the first pass is the plain
  decode) and `TagDecoder_Decode_RightPreview_IsDeterministic` (repeated and concurrent decodes give identical text).
- **Word lookup** (`GetPossibleTags` and `IsFormOf` called on the decoder directly, with the real embedded lexicon and
  with hand-set lexicons; the decoder and the lookup share one thread-static workspace, so several tests put them side
  by side): `TagDecoder_GetPossibleTags_WithShapeRules_SampleTokens_EqualTheDecodeCandidates` (a theory over a model
  without and one with all groups: for hand-picked tokens the tags of the lookup equal `{Tag} U Alternatives` of a
  decode with 16 alternatives and no constraint, in the first, a middle and the last position, and the known flag
  equals the token's), `TagDecoder_GetPossibleTags_LexiconOnly_EqualsLexiconTryGetTags` (the raw lookup is the lexicon
  fact, which differs from the allowed set exactly by the shape rules),
  `TagDecoder_GetPossibleTags_EmbeddedLexicon_HasNoWhiteSpaceSpellings` (no spelling is empty or has white space, so
  refusing a phrase loses no answer, and the longest spelling is the length that the lookup guards),
  `TagDecoder_GetPossibleTags_EmbeddedLexicon_EverySpellingEqualsTheDecoder` (a theory over a model without and one
  with groups: every one of the 262,635 spellings, its Title-case form and 52 samples of other shapes give the
  decoder's allowed set and known flag, and the raw lookup equals the lexicon, about 525,000 words per model; the raw
  comparison covers the spellings that are stable under the fold),
  `TagDecoder_GetPossibleTags_HyphenatedSpellings_SpellingWinsAndHeadsMatchTheirPart` (all 8,711 hyphenated spellings
  are `Spelling`, never `CompoundHead`, and an unlisted prefix and a hyphen in front of the last part gives the analyses
  of that part),
  `TagDecoder_IsFormOf_EveryAnalysisOfEverySpelling_IsAForm` (all 290,365 analyses are forms, also with a Title-case
  word and an upper-case base word, and a changed base word or a tag that the spelling's analyses lack is not),
  `TagDecoder_GetPossibleTags_BetweenDecodes_DoesNotDisturbTheWorkspace` (decode, lookup, decode, with two decoders and
  lexicons on one thread, one with cached lexicon facts: every result equals the one without interleaving) and, on
  .NET 8 and later, `TagDecoder_GetPossibleTags_HugeAsciiInput_AllocatesNothing` (a word of a million ASCII
  characters, a compound with a prefix of 500,000 characters and a known last part, one with an unknown last part, and
  the form question for both, allocate 0 bytes after the first call, so no per-thread buffer grew; the compound has
  its head and the other words are not recognized).
- **No retention of request text** (the tests read the thread's workspace through `TagDecoder.CurrentThreadWorkspace`
  and count the elements that are not zero in the whole of every buffer, so a trace of an earlier, longer request would
  show): `TagDecoder_Decode_LeavesNoRequestTextInTheWorkspace` (a theory over a model without and one with all groups: a
  long request of invented words, among them tokens with the suffixes that make the inflection group build candidate
  spellings behind the last token, then a short request; the buffers are empty after each, and the results keep their
  text), `TagDecoder_Decode_Exception_LeavesNoRequestTextInTheWorkspace` (the same theory with a request whose last
  token is the default token, which has no text: the decode fails after it folded the tokens before it, nothing stays in
  the workspace and the next decode gives the tags it gave before),
  `TagDecoder_GetPossibleTags_And_IsFormOf_LeaveNoRequestTextInTheWorkspace` (a plain word, a hyphenated word, an
  over-long compound with and without a known last part, and the form question answered yes and no, each followed by the
  check) and `DecoderWorkspace_Clear_ErasesAPreparedSentence` (a prepared sentence with a lexicon key in the scratch
  buffer holds more than 50 non-zero elements and `Clear` leaves none, so the checks above are not vacuous; the lookup
  of an unlisted word also decoded spellings into the second half of the scratch buffer, the written mark lies beyond
  the first half but below the capacity of the buffer, and `Clear` leaves no byte of either half and resets the mark).
