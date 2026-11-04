<!-- cspell:ignore Inflectable -->

### Glossary Verification

This document describes the unit-level verification strategy for the `Glossary` unit (`Glossary`, `GlossaryBuilder`,
`GlossaryEntry`, `GlossaryOptions`, `GlossaryMatch` and their internal helpers).

#### Verification Approach

The unit is verified by unit tests in `Tagging/GlossaryTests.cs` (entries, builder, options, immutability and the
inflection rules), `Tagging/GlossaryMatchingTests.cs` (matching, tokenization conventions, constraints, tagging with a
glossary, results, allocation and thread safety) and `Tagging/GlossaryThroughputTests.cs` (matching speed). The pure
parts (entry validation, the inflection tables, the matcher, the constraint rules and their merge) are tested directly,
through the public API and through the internal helpers that the test project can see; nothing is mocked. What a
glossary does to tags needs a tagger, so the constraint, confidence and result tests use a **hand-set tiny model**
(`TestSupport/TinyModels.cs`) in which every score is known, together with a lexicon of the test words: a test can then
state that the model alone reads `bleed` as a verb by a known margin, that the glossary changes it, and that a soft
strength of a given size flips a close decision but not a decisive one, without depending on the quality of a trained
model. One test runs the documented examples against the shipped model, but only for what the constraints guarantee (the
head tag, the forbidden tags, the unchanged pronoun), never for a prediction of the model.

Three methods go beyond an ordinary assertion:

- **Hash collisions.** The index takes a hash mask as a test hook, so that a test can force every token of a glossary
  onto one hash value and prove that matching still compares the characters and gives the same matches.
- **Allocation** (`TestSupport/AllocationMeter.cs`, as for the decoder; compiled for .NET 8 and later only, because
  `GC.GetAllocatedBytesForCurrentThread` does not exist elsewhere). The matcher is measured on a long sentence of
  ordinary text against a large glossary with many matches, and must allocate nothing per call; tagging with a glossary
  is measured against tagging without one: a sentence without a match must allocate no more, the extra bytes for a
  fixed number of tokens must be bounded per match, and the extra bytes for a fixed number of matches must grow by no
  more than one constraint slot per token; a soft glossary must allocate like a hard one after its first use. All
  comparisons are one-sided with a small tolerance for measurement noise.
- **Throughput** (`TestSupport/ThroughputMeter.cs`, the best-of-N method that is described in the system verification
  document). The test class is in the non-parallel throughput collection. A glossary of 10,000 word pairs from the open
  training text is matched against the development sentences, and the rate must exceed ten times the tagging gate, on a
  glossary that really matches; the instrumented-run relaxation is the one of the tagging gate.

Thread safety is verified with eight threads that share one glossary and the shared default tagger, hard and soft, and
compare every result with a sequential run, and with a race in which eight threads use a fresh soft glossary for the
first time at the same moment (the compiled constraint tables are created lazily once per tagger).

#### Test Environment

No setup beyond the standard test runner: the models are built in memory, the lexicon of the test words is compiled in
memory, the throughput test reads the open training and development corpora from the working copy through
`DataFiles.cs`, and the shipped default tagger is used by the parallel test and the documented examples. No network.

#### Acceptance Criteria

All tests pass on every target framework (the allocation tests on .NET 8 and later). A test fails when an invalid entry,
option or glossary is accepted, an entry changes when a copy is made, a glossary or its match list can be changed, the
longest term does not win or matches overlap, a term matches across sentences or with a different letter case than its
rule says, a hash collision gives a wrong match, a tokenization convention is not followed, a generated inflected form
is wrong or is generated for the wrong word, a hard head word leaves its tags or has a confidence other than 1 with one
tag, a word in front of the head of a noun term can be a verb, the particle rule applies to another shape of term than a
two-token verb term with its head first, a
soft strength does not equal the strength times the temperature, the caller's constraint does not win over a contradicting
glossary constraint, the tokens before the first match change, a match is not reported on its tokens or sentence, matching
allocates per token, a parallel result differs from the sequential one, or matching is slower than the bound.

#### Test Scenarios

- **Entry validation**: `GlossaryEntry_Ctor_NullTerm_ThrowsArgumentNullException`,
  `GlossaryEntry_Ctor_EmptyOrWhiteSpaceTerm_ThrowsArgumentException`,
  `GlossaryEntry_Ctor_TermWithoutLetterOrDigit_ThrowsArgumentException`,
  `GlossaryEntry_Ctor_EmptyTags_ThrowsArgumentException`,
  `GlossaryEntry_Ctor_UndefinedTag_ThrowsArgumentOutOfRangeException`,
  `GlossaryEntry_WithHead_OutOfRange_ThrowsArgumentOutOfRangeException`,
  `GlossaryEntry_WithVariants_WrongTokenCount_ThrowsArgumentException`,
  `GlossaryEntry_WithVariants_NullEmptyOrWithoutWord_Throws`,
  `GlossaryEntry_WithTokenTags_HeadEmptyOrOutOfRange_Throws` and
  `GlossaryEntry_WithMode_UndefinedMode_ThrowsArgumentOutOfRangeException` (each refuses the invalid input with the
  specified exception).
- **Entry values**: `GlossaryEntry_HeadIndex_DefaultsToLastToken`,
  `GlossaryEntry_WithHead_ValidIndex_ReturnsNewEntryAndLeavesOriginal`,
  `GlossaryEntry_With_ReturnsNewEntryAndLeavesOriginal`, `GlossaryEntry_With_Chained_KeepsEarlierSettings`,
  `GlossaryEntry_Definition_IsCarriedUnchanged`, `GlossaryEntry_WithVariants_Valid_ReplacesEarlierVariants`,
  `GlossaryEntry_GetTokenTags_ReturnsOverrideOrNull` and `GlossaryEntry_GetTokenTags_TokenBecomesHead_ReturnsNull`.
- **Builder and options**: `GlossaryBuilder_CollectionInitializer_BuildsEntriesInOrder`,
  `GlossaryBuilder_Add_Chained_CountsAndEnumerates`, `GlossaryBuilder_Add_NullEntry_ThrowsArgumentNullException`,
  `GlossaryBuilder_Build_IsSnapshot` (a later `Add` does not change a built glossary),
  `GlossaryBuilder_Constructor_InvalidSoftStrength_ThrowsArgumentOutOfRangeException` (not finite, zero, negative),
  `GlossaryBuilder_Constructor_UndefinedMode_ThrowsArgumentOutOfRangeException`,
  `GlossaryOptions_Defaults_AreCaseInsensitiveHardInflecting`,
  `GlossaryOptions_DefaultSoftStrength_IsFiniteAndPositive` (the literal value is never asserted, because it is a
  decision recorded elsewhere) and `Glossary_Create_NullEntriesOrNullItem_ThrowsArgumentNullException`.
- **Duplicates**: `GlossaryBuilder_Add_DuplicateSameCase_ThrowsArgumentException`,
  `GlossaryBuilder_Add_CaseSensitiveAndInsensitiveSameTerm_Allowed`,
  `GlossaryBuilder_Add_CaseSensitiveEntries_DuplicateOnlyWhenExactlyEqual`,
  `GlossaryBuilder_Add_GlossaryWideCaseSensitive_DecidesDuplicates` and `Glossary_Create_Duplicate_ThrowsArgumentException`.
- **Immutability**: `Glossary_Options_ReturnsCopy`, `Glossary_Empty_HasNoEntries`,
  `Glossary_Create_SourceChangedAfterwards_DoesNotChangeGlossary`, `Glossary_Immutable_EntriesAreReadOnly` (no cast back
  to a writable list) and `Glossary_FindMatches_ResultList_IsReadOnly`.
- **Letter case**: `Glossary_FindMatches_CaseInsensitiveDefault_MatchesAnyCase`,
  `Glossary_FindMatches_CaseSensitiveEntry_ExactCaseOnly` (`IT` against `it`),
  `Glossary_FindMatches_CaseSensitiveWinsOverInsensitiveAtSameLength` and
  `Glossary_FindMatches_GlossaryWideCaseSensitive_CanBeOverriddenPerEntry`.
- **Matching**: `Glossary_FindMatches_LongestFirst_PrefersLongerTerm`, `Glossary_FindMatches_LeftToRight_DoesNotOverlap`,
  `Glossary_FindMatches_AdjacentMatches_AreAllFound`, `Glossary_FindMatches_PrefixOfLongerTerm_NoMatchWhenIncomplete`,
  `Glossary_FindMatches_LongerWalkWithoutTerm_FallsBackToShorterTerm`,
  `Glossary_FindMatches_TermSplitAcrossSentences_NoMatch`,
  `Glossary_FindMatches_EmptySentenceOrEmptyGlossary_NoMatches`, `Glossary_FindMatches_NullSentence_ThrowsArgumentNullException`
  and `Glossary_FindMatches_HashCollision_ResolvedByComparison` (every token forced onto one hash value).
- **Term tokenization**: `GlossaryEntry_Tokens_FollowTokenizer`,
  `GlossaryEntry_EndTokens_TermEndingInKeptPeriod_DiffersAtEndOfText`,
  `Glossary_FindMatches_HyphenatedTermIsOneToken_DoesNotMatchSpaced`,
  `Glossary_FindMatches_ContractionInTerm_SplitsLikeTokenizer`,
  `Glossary_FindMatches_AbbreviationPeriod_MidTextAndEndOfText`, `Glossary_FindMatches_CurlyApostropheEqualsStraight`,
  `Glossary_FindMatches_PreTokenizedPunctuationStuckToWord_DoesNotMatch`,
  `Glossary_FindMatches_NonAsciiToken_SlowPathMatches` and `GlossaryIndex_FoldWithWorkspace_AgreesWithFoldToString` (the
  allocation-free fold agrees with the string fold).
- **Inflection and variants**: the table tests `GlossaryInflector_Plural_FollowsRegularRules`,
  `GlossaryInflector_Past_FollowsRegularRules` and `GlossaryInflector_PresentParticiple_FollowsRegularRules`; the tags of
  the forms in `GlossaryInflector_Inflect_Noun_GivesPluralNoun`, `GlossaryInflector_Inflect_Verb_GivesThreeFormsWithDerivedTags`,
  `GlossaryInflector_Inflect_NounAndVerb_UnitesTagsOfEqualSpelling` and
  `GlossaryInflector_Inflect_NounAndProperNoun_PluralHasBothTags`; no forms in
  `GlossaryInflector_Inflect_OtherTags_GivesNothing` and `GlossaryInflector_Inflect_NotInflectableWord_GivesNothing`;
  only the head word in `Glossary_Create_MultiwordEntry_InflectsOnlyHeadWord`,
  `Glossary_FindMatches_OnlyHeadWordIsInflected` and `Glossary_FindMatches_HeadWordMovedByWithHead_IsTheOneInflected`;
  the switches in `Glossary_Create_InflectionSwitches_ControlGeneratedForms` and
  `Glossary_FindMatches_InflectionSwitches_AreHonored`; forms and variants found and reported in
  `Glossary_FindMatches_InflectedForms_AreFoundAndReportedAsVariants` and `Glossary_FindMatches_ExplicitVariants_AreMatched`;
  and the precedence in `Glossary_FindMatches_TermBeatsVariantBeatsGeneratedForm` and
  `Glossary_FindMatches_VariantBeatsGeneratedForm_EarlierEntryWinsOtherwise`.
- **Hard constraints**: `GlossaryConstraints_NonHead_Rules_ProduceDocumentedConstraints` (the constraints of every rule
  in both modes), `Tagger_TagWithGlossary_HardNounTerm_HeadIsNounWithConfidenceOne` (the model alone makes the costly
  error, the glossary corrects it, with confidence exactly 1 and no alternatives),
  `Tagger_TagWithGlossary_NonHeadOfNounTerm_IsNeverVerbOrAuxiliary`,
  `Tagger_TagWithGlossary_NonHeadRule_NounRuleAppliesToNounAndProperNounEntriesOnly` (a word in front of the head of a
  verb or adjective entry stays with the model), `Tagger_TagWithGlossary_WithTokenTags_ForcesTheTokenTags`,
  `Tagger_TagWithGlossary_EntryDisjointFromLexicon_GlossaryWins` and `Tagger_TagWithGlossary_UnknownWord_TakesEntryTag`.
- **Non-head rule**: `GlossaryConstraints_NonNominalRule_Default_IsParticlesOnly`,
  `GlossaryConstraints_Compile_ParticleRule_AppliesToTwoTokenVerbTermsWithHeadFirstOnly` (a two-token verb term with its
  head first gets the particle rule on its second token; a verb term with its head last, an adjective term with either
  head and a noun term do not; explicit tags still win),
  `GlossaryConstraints_Compile_ThreeTokenVerbTerm_NonHeadTokensAreUnconstrained` (no non-head token of a three-token verb
  term is constrained, whichever token is the head, unless explicit tags say so),
  `Tagger_TagWithGlossary_VerbTermWithHeadFirst_NonHeadIsLimitedToParticles` (the phrasal verb `shut down` with
  `WithHead(0)`), `Tagger_TagWithGlossary_ThreeTokenVerbTerm_NonHeadTokensAreLeftToTheModel` (the middle and last words
  of `shut it down` keep the model's tags and report no constraint) and
  `Tagger_TagWithGlossary_NonVerbTermOrWordBeforeTheHead_IsLeftToTheModel`.
- **Soft constraints**: `Tagger_TagWithGlossary_SoftMode_FlipsCloseDecisionButNotDecisiveOne`,
  `Tagger_TagWithGlossary_SoftMode_AdjustmentEqualsStrengthTimesTemperature` (compared with a hand-built
  `TokenConstraint.Prefer` on the same tagger, at a temperature other than 1),
  `Tagger_TagWithGlossary_SoftMode_RaisesConfidenceOfPreferredTag`, `Tagger_TagWithGlossary_PerEntryMode_OverridesGlossaryMode`
  and `GlossaryMatch_Mode_IsEntryModeOrGlossaryMode`; the strength validation is in the builder scenario.
- **Caller constraints**: `GlossaryConstraints_Merge_NoneIntersectionAndContradiction`,
  `Tagger_TagWithGlossary_CallerConstraints_AreMergedAndCallerWinsWhenDisjoint`,
  `Tagger_TagWithGlossary_CallerSoftConstraint_AddsToGlossaryAdjustment` and
  `Tagger_TagWithGlossary_ConstraintListOfWrongLength_ThrowsArgumentException`.
- **Tagging overloads**: `Tagger_TagWithGlossary_ThreeOverloads_Agree` (text, sentence and token list give the same tags),
  `Tagger_Tag_NullSecondArgument_StillBindsToConstraintsOverload` (an existing call `Tag(sentence, null)` still
  compiles and binds to the overload without a glossary) and
  `Tagger_TagWithGlossary_TokensBeforeFirstMatch_AreUnchanged` (the tokens in front of the first match equal the result
  without a glossary; the tokens after a match may differ through the history).
  The empty glossary (`Glossary_Empty_TagsLikeNoGlossary`: same tags and no match as without a glossary) and the null
  arguments of the three overloads (`Tagger_TagWithGlossary_NullArguments_ThrowArgumentNullException`) are verified here
  at the unit level and again, with the tagger as the unit under test, in _Tagger Verification_.
- **Results**: `GlossaryMatch_Properties_DescribeTheMatchedSpan` (first token, token count, head token, offset, length and
  text compared with the source, the entry and its definition),
  `GlossaryMatch_Start_InLaterSentence_IndexesWholeText`, `Tagger_TagWithGlossary_Results_ReportMatchesOnTokensAndSentences`
  (the match on every token of the span, `IsGlossaryHead` on the head only, `null` outside, the sentence list in text order)
  and `Tagger_TagWithGlossary_MatchList_IsReadOnly`.
- **Confidence**: `Tagger_TagWithGlossary_HardNounTerm_HeadIsNounWithConfidenceOne`,
  `Tagger_TagWithGlossary_HardHeadWithSeveralTags_SoftmaxOverRemainingTags` (the exact softmax, the other tag as the one
  alternative, and a sum of one) and `Tagger_TagWithGlossary_SoftMode_RaisesConfidenceOfPreferredTag`. A word of a match
  that has no constraint of its own is a member of the match without being constrained: the middle and last words of
  `shut it down` in `Tagger_TagWithGlossary_ThreeTokenVerbTerm_NonHeadTokensAreLeftToTheModel` report the match and no
  constraint, as does the closing period of a term such as `etc.` at the end of a text, so the constrained flag marks the
  words that the glossary decided and the match marks every word of the span.
- **Thread safety**: `Glossary_FindMatches_ParallelUse_MatchesSequentialResults`,
  `Glossary_Tag_ParallelUse_MatchesSequentialResults` (eight threads, hard and soft, the default tagger) and
  `Glossary_Tag_SoftFirstUseRace_CompilesCorrectly` (twenty rounds of eight threads that use a fresh soft glossary together).
- **No allocation**: `GlossaryIndex_Find_DoesNotAllocate`, `Glossary_Match_SentenceWithoutMatch_DoesNotAllocate`,
  `Tagger_TagWithGlossary_SentenceWithoutMatch_AddsNoAllocation` (a sentence without a match allocates no more with a
  glossary than without),
  `Tagger_TagWithGlossary_AllocationPerMatch_IsBoundedForFixedTokenCount` (with the tokens fixed, the extra bytes per
  match are positive and bounded),
  `Tagger_TagWithGlossary_AllocationPerToken_IsOneConstraintSlot` (with the matches fixed, the extra bytes per token do
  not exceed one constraint slot) and
  `Tagger_TagWithGlossary_SoftMode_AllocatesLikeHardModeAfterFirstUse` (soft allocates no more than hard). The
  comparisons allow a noise of 64 bytes per call.
- **Throughput**: `Glossary_FindMatches_TenThousandEntries_ExceedsTenTimesTheTaggingGate` (method described under
  Verification Approach; the measured rate is written to the test output).
