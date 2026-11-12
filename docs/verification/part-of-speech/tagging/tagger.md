### Tagger Verification

This document describes the unit-level verification strategy for the `Tagger` unit (`Tagger`, `TaggerOptions`,
`TaggedSentence`, `TaggedToken`, `TagScore`).

#### Verification Approach

The unit is verified by unit tests in `Tagging/TaggerTests.cs` that use the public API with tiny models: the tiny
trained model of `TinyTrainer` (which learns the fixture sentences) with the embedded lexicon or with a lexicon of the
fixture words, and hand-set models from `TinyModels` where a test needs exact scores. The decoder, extractor, readers and
tokenizer are real, so these tests also prove that the facade connects them correctly; nothing is mocked. The
thread-safety test shares one tagger between 8 threads and compares every result with a sequential run; the immutability
test tries to modify results and options through the interfaces that they expose. The tests of `Tagger.Default` use the
shipped model, but only for properties that do not depend on its accuracy (it exists, it is one shared instance, and it
tags four hand-written sentences whose tags are unmistakable; the sentences were written once and are not tuned, and
the accuracy of the model is measured by the trainer's evaluation, never by these tests).

#### Test Environment

No setup beyond the standard test runner: models are built in memory (the default tagger uses the embedded
resources), no files and no network.

#### Acceptance Criteria

All tests pass on every target framework. A test fails when a tag, offset or text is wrong, an invalid argument is not
rejected with the specified exception, the default tagger is not one shared instance or does not tag, a parallel result
differs from the sequential one, a result can be modified after it is returned, or an alternative list violates its
invariants (excludes the tag, sorted, limited, sums with the confidence to at most one), the known-word flag differs
from the lexicon membership of the lower-cased spelling or changes with a glossary, or a margin differs from the
confidence minus the best alternative (or from the smallest value that the confidence allows when there is none), is
negative, or allocates.

#### Test Scenarios

- **Tag text**: `Tagger_Tag_Text_ReturnsTaggedSentences` (a text is split into sentences whose tokens carry the learned
  tags).
- **Tag tokens**: `Tagger_Tag_Tokens_ReturnsTags` (tokens are tagged as given and the sentence text is the joined
  tokens) and `Tagger_Tag_Tokens_EmptyTokens_ReturnsEmptySentence`.
- **Offsets**: `Tagger_Tag_Text_TokenTextMatchesOffsets`.
- **Load**: `Tagger_Load_NullStreams_Throw` (both overloads), `Tagger_Load_InvalidOptions_Throw` (the number of
  alternatives must be 0 to 16) and `Tagger_Load_CustomLexicon_IsUsed`.
- **Default**: `Tagger_Default_EmbeddedModel_ReturnsSameWorkingTagger` (every read returns the same tagger, with the
  default options, that tags with the embedded model),
  `Tagger_Default_ParallelAccess_ReturnsSingleInstance` (16 threads released together all receive one instance) and
  `Tagger_Default_ObviousSentences_AreTaggedCorrectly` (a theory over four hand-written sentences with unmistakable
  tags).
- **Thread safety and immutability**: `Tagger_Tag_Parallel_MatchesSequential` (eight threads) and
  `Tagger_Tag_ResultsAreImmutable` (results and options cannot be modified through the interfaces they expose).
- **Input validation**: `Tagger_Tag_NullArguments_Throw` (every overload) and
  `Tagger_Tag_ConstraintLengthMismatch_Throws` (one constraint per token).
- **Any text**: `Tagger_Tag_Text_UnpairedSurrogate_DoesNotThrow` and `Tagger_Tag_Tokens_UnpairedSurrogate_DoesNotThrow`
  (a lone high surrogate, a lone low surrogate and a reversed pair inside a word: no exception, token text and offsets
  round-trip, the result is the same on a second call) and `Tagger_Tag_Text_ValidSurrogatePair_RoundTrips` (an emoji
  stays one token with the right offsets).
- **Options**: `Tagger_Options_MaxAlternativesLimitsAlternatives`.
- **Glossary overloads**: `Tagger_Tag_Glossary_NullArguments_Throw` (a null glossary, text, sentence or token list is
  refused by every overload that takes a glossary), `Tagger_Tag_Glossary_ConstraintLengthMismatch_Throws` (with and without
  a match), `Tagger_Tag_Glossary_EmptyGlossary_GivesTheResultWithoutGlossary` (the three overloads give the tags,
  confidences and alternatives of the calls without a glossary, and no match) and `Tagger_Tag_Glossary_Match_IsAppliedAndReported`
  (a hard entry decides the tag of its head word with certainty, and the match is reported on the token and on the
  sentence with its offset and definition). What a glossary does to the tags is verified in _Glossary Verification_.
- **Alternatives**: `Tagger_Tag_Alternatives_ExcludeBestAndSumWithConfidence`.
- **Known word**: tests with hand-set weights and a lexicon of chosen rows (`CreateHandSet`).
  `Tagger_Tag_KnownWord_LexiconSpelling_IsTrueAndUnlistedSpellingIsFalse` (text input: listed words are known; an
  unlisted word and a period are not), `Tagger_Tag_KnownWord_CaseVariant_UsesTheLexiconLookup` (a theory over `cat`,
  `Cat`, `CAT` and an unlisted spelling), `Tagger_Tag_KnownWord_GlossaryHeadNotInLexicon_IsUnknownButConstrained` (the
  head of a hard entry that the lexicon does not list is not known, is constrained, is the head, is a noun with the
  confidence 1, and its neighbor keeps its own flag) and
  `Tagger_Tag_KnownWord_GlossaryHeadInLexicon_IsKnownAndConstrained` (a listed head is
  known and constrained; every flag equals the one of the text tagged without the glossary). The glossary check of
  `Tagger_Tag_Glossary_EmptyGlossary_GivesTheResultWithoutGlossary` also includes the flag, because the helper
  `Describe` writes it.
- **Margin**: `Tagger_Tag_Margin_SeveralAlternatives_IsConfidenceMinusRunnerUp` (a theory over 3 and 16 alternatives
  for a
  word with three allowed tags, against the analytic value to 1e-9),
  `Tagger_Tag_Margin_OneAlternative_IsConfidenceMinusRunnerUp` (one alternative gives the value that 16 give),
  `Tagger_Tag_Margin_NoAlternativesRequested_IsTheSmallestMarginTheConfidenceAllows` (with 0 alternatives the margin is
  `2c - 1`, below the exact margin, and 0 for a confidence below one half), `Tagger_Tag_Margin_SingleAllowedTag_IsOne`
  (a single-tag lexicon entry, a hard constraint on a listed word and on an unlisted word, with 0, 1 and 3 alternatives),
  `Tagger_Tag_Margin_TiedTags_IsZero` (with and without alternatives), `Tagger_Tag_DefaultToken_HasNoMarginAndIsNotKnown`
  and `Tagger_Tag_Margin_DoesNotAllocate` (.NET 8 and later: reading the margin and the flag of a token with alternatives
  allocates 0 bytes per batch of 100 reads, which fails when the margin is read through `Alternatives`).
- **Feature groups**: `Tagger_Load_ModelWithFeatureGroups_TagsWithTheGroups` (a toy model trained with all four groups
  loads through the public loader, its header names all four groups, and the tagger, which decodes the right preview
  group in two passes, tags at least 90% of the fixture tokens right; what each group does is verified in
  _FeatureExtractor Verification_ and _TagDecoder Verification_).
