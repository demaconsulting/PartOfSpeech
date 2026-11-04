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
invariants (excludes the tag, sorted, limited, sums with the confidence to at most one).

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
