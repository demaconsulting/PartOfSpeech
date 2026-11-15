### Tagger Verification

This document describes the unit-level verification strategy for the `Tagger` unit (`Tagger`, `TaggerOptions`,
`TaggedSentence`, `TaggedToken`, `TagScore`, `PossibleTagsMode`, `WordMatch`, `WordAnalysis`, `WordTags`).

#### Verification Approach

The unit is verified by unit tests in `Tagging/TaggerTests.cs` that use the public API with tiny models: the tiny
trained model of `TinyTrainer` (which learns the fixture sentences) with the embedded lexicon or with a lexicon of the
fixture words, and hand-set models from `TinyModels` where a test needs exact scores. The decoder, extractor, readers and
tokenizer are real, so these tests also prove that the facade connects them correctly; nothing is mocked. The
thread-safety test shares one tagger between 8 threads and compares every result with a sequential run; the immutability
test tries to modify results and options through the interfaces that they expose. The tests of `Tagger.Default` use the
shipped model, but only for properties that do not depend on its accuracy (it exists, it is one shared instance, and it
tags four hand-written sentences whose tags are unmistakable; the sentences were written once and are not tuned, and
the accuracy of the model is measured by the trainer's evaluation, never by these tests). The word lookup is verified on
the real embedded lexicon (its `tests`, `the`, `is`, `went`, `well-tested` and similar entries are real data) and,
where a test needs exact control of the analyses or of the longest spelling, on small custom lexicons that `TinyModels`
compiles with the real writer; the tests for a word that is longer than the longest spelling compare the answer with
the answer of a lexicon that can look the whole word up.

#### Test Environment

No setup beyond the standard test runner: models are built in memory (the default tagger uses the embedded
resources), no files and no network.

#### Acceptance Criteria

All tests pass on every target framework. A test fails when a tag, offset or text is wrong, an invalid argument is not
rejected with the specified exception, the default tagger is not one shared instance or does not tag, a parallel result
differs from the sequential one, a result can be modified after it is returned, or an alternative list violates its
invariants (excludes the tag, sorted, limited, sums with the confidence to at most one), the known-word flag differs
from the lexicon membership of the folded, lower-cased spelling or changes with a glossary, or a margin differs from the
confidence minus the best alternative (or from the smallest value that the confidence allows when there is none), is
negative, or allocates; or a word lookup differs from the documented rule: a word that is not recognized is reported
as a word with no tags, a tag, analysis or match differs from the lexicon, a hyphenated word is matched against
anything but its whole spelling or its last part (whatever the length of the word), the tags of a compound head are
not those of the whole word, the answer to the form question disagrees with the analyses, an invalid word, mode or tag
is accepted, a parallel lookup differs from the sequential one, or a lookup of an ASCII word allocates.

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
  `Cat`, `CAT` and an unlisted spelling), `Tagger_Tag_KnownWord_FoldedSpelling_UsesTheLexiconLookup` (a theory over the
  curly apostrophe, the em dash, the ellipsis and a decomposed accent, which match the lexicon rows `n't`, `--`, `...`
  and `café`, and an unaccented spelling that does not),
  `Tagger_Tag_KnownWord_GlossaryHeadNotInLexicon_IsUnknownButConstrained` (the
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
- **Word lookup, tags and modes** (embedded lexicon unless a custom lexicon is named; real data, no mock):
  `Tagger_GetPossibleTags_DefaultMode_IsLexiconOnly`, `Tagger_GetPossibleTags_Case_IgnoresLetterCase` (a theory:
  `TESTS`, `Tests` and `tESTS` give a result equal to the one of `tests`, analyses included),
  `Tagger_GetPossibleTags_ShapeRules_ExtendTheLexiconTags` (a theory over `valve`, `Valve`, `VALVE`, `vALVE`, `1st`,
  `--`, the em dash and `n't` forms: the raw tags are the lexicon's, the shape tags add PROPN and X for a capital first
  letter, the digit tags for a digit and the punctuation tags for a word without a letter or digit, and both modes
  know the word), `Tagger_GetPossibleTags_FoldedSpelling_UsesTheTaggerLookup` (a theory over the curly apostrophe, the
  en dash, em dash and ellipsis characters, a decomposed accent, an unlisted spelling: the same lookup as a token),
  `Tagger_GetPossibleTags_UnknownWord_IsNotRecognizedNotEmptyKnown` (a made-up word gives `NotRecognized`, empty tags
  in the lexicon mode and all 17 in the shape mode, and equals `default` in the lexicon mode),
  `Tagger_GetPossibleTags_CustomLexicon_UsesItsOwnLexicon` (two taggers with different lexicons do not mix their
  answers) and `Tagger_GetPossibleTags_Glossary_DoesNotChangeTheResult` (a glossary changes the tag of a token in a
  sentence and not the lookup of the word).
- **Word lookup, analyses**: `Tagger_GetPossibleTags_Tests_ReturnsBothAnalysesInDocumentedOrder` (`test NOUN
  Number=Plur` then `test VERB Person=3|Tense=Pres`, `GetFeature`, the UD notation),
  `Tagger_GetPossibleTags_TagsWithoutAnalysis_ReportsRuleAndAttestedTags` (`the` with ADV, PRON and PROPN without an
  analysis, `testing` with the rule-added ADJ, `ran`, and `paris`, a proper noun without any analysis),
  `Tagger_GetPossibleTags_ClosedClassAndIrregularForms_HaveTheBaseWord` (`of` is its own base word, `is` has `be` as AUX
  and as VERB, `went` has `go`, and an empty feature set reads as an empty string),
  `Tagger_GetPossibleTags_CustomLexiconWithoutAnalyses_HasNoAnalyses` (tags and match, no analyses,
  `TagsWithoutAnalysis` equals `Tags`, `IsFormOf` false),
  `Tagger_GetPossibleTags_CustomLexiconWithAnalyses_ReportsItsAnalyses` (a custom lexicon with non-ASCII and
  apostrophe base words), `WordTags_Equality_FollowsTheLookupResult` (equality, hash code, `==`, `default`,
  `ToString`) and `WordAnalysis_Members_WorkOnRealAndDefaultValues` (equality, `GetFeature` for present, absent and
  null names, `ToString`, the default value).
- **Word lookup, hyphenated words and long words**:
  `Tagger_GetPossibleTags_CompoundHead_UsesLastPartAndTagsStayTheWholeToken` (`well-tested`: `CompoundHead`, the
  analyses of `tested`, `IsKnownWord` false, empty tags in the lexicon mode and all 17 in the shape mode),
  `Tagger_GetPossibleTags_CompoundHead_HeadIsTheLastPartOnly` (a theory over `pre-flight`, `WELL-Tested`,
  `very-state-of-the-art`, a made-up prefix before `flight`, and `state-of-the-art-style`: only the last part counts,
  nothing is split further), `Tagger_GetPossibleTags_CompoundWhole_PrefersTheSpelling` (`built-in` and `User-Friendly`
  are spellings with their own analyses, not those of `in` or `friendly`),
  `Tagger_GetPossibleTags_CompoundHead_Malformed_IsNotRecognized` (a theory: `-ed`, `well-`, `a--b`, `well--tested`, a
  leading or trailing hyphen, an unknown last part, the en dash, the em dash, and the typographic hyphens U+2010 and
  U+2011 as separators), `Tagger_GetPossibleTags_CompoundHead_TooLongWhole_UsesTheLastPart` (a word that fits, one
  character too long and one with a million characters, each with the known last part `tested`: all `CompoundHead`
  with the analyses of `tested`, `IsFormOf` true, empty or full tags by mode),
  `Tagger_GetPossibleTags_CompoundHead_TooLongWhole_Otherwise_IsNotRecognized` (an over-long word with no hyphen, an
  unknown last part, a last part that is itself too long, a leading, doubled or trailing hyphen, or a dash character:
  `NotRecognized`, no analyses, `IsFormOf` false) and
  `Tagger_GetPossibleTags_TooLongWhole_EqualsTheAnswerOfTheNormalPath` (tens of thousands of generated words made of
  pieces such as `ab`, `well`, `tested`, `-`, `--`, en dash, ellipsis, upper case, accents and U+2010, with and
  without over-long prefixes, are looked up in a lexicon whose longest spelling is six bytes and in the same lexicon
  plus a 200-character spelling, so that no word is too long for the second; `Match`, `Tags` in both modes,
  `TagsWithoutAnalysis`, the analyses and `IsFormOf` must be identical, and more than 10,000 of the words are too long
  for the first), `Tagger_GetPossibleTags_VeryLongWord_IsNotRecognizedWithoutError` (a million ASCII characters, a
  million non-ASCII characters and a hyphenated word with halves of 500,000 characters: `NotRecognized`, no
  exception, and the next lookup of an ordinary word is right),
  `Tagger_GetPossibleTags_WordsWithManyCharacters_NeverGrowTheWorkspaceBeyondTheLongestSpelling` (on a new thread: after
  the folding of the largest word that is folded, an ellipsis for every byte of the longest spelling, as a word and as
  the word of `IsFormOf`, the lookups of 100 times `é` (200 bytes, so not a spelling, but folded as a whole), of words
  with as many characters as the longest spelling has bytes, of words with one more and of words of a million characters
  (`é`, ellipsis, em dash, `a`) are all `NotRecognized` and the character buffer of the thread's workspace, read by
  reflection, keeps its size between 6 and 16 times the longest spelling) and
  `Tagger_GetPossibleTags_UnpairedSurrogate_DoesNotThrow`.
- **Word lookup, arguments**: `Tagger_GetPossibleTags_NullWord_Throws` (also a null base word of `IsFormOf`),
  `Tagger_GetPossibleTags_EmptyOrWhiteSpace_Throws` (a theory over an empty word, a space, a tab, a no-break space, a
  newline, an em space and a phrase, for the word and the base word),
  `Tagger_GetPossibleTags_WhiteSpaceRule_EqualsTokenizerRule` (all 65,536 UTF-16 code units: the lookup refuses a word
  exactly when `Tokenizer.CreateSentence` refuses it as a token) and
  `Tagger_GetPossibleTags_UndefinedModeOrTag_Throws`.
- **Form question**: `Tagger_IsFormOf_Analyses_AnswerTheQuestion` (a theory of true and false cases: `tests` of `test`
  as noun and as verb, `TESTS` of `Test`, `went` of `go`, `is` of `be`, an apostrophe base word with typographic
  apostrophe and case, compound heads in any case, a wrong tag, a base word that is not an analysis, an unknown word,
  a proper noun without analysis, a tag without analysis, the whole spelling `built-in` against its head, an unknown
  head and a malformed compound) and `Tagger_IsFormOf_EveryAnalysisOfSampleWords_IsAForm` (every analysis of a sample
  of words, compound heads included, is a form, and a decomposed non-ASCII word and base word and a curly apostrophe
  are normalized in a custom lexicon).
- **Thread safety**: `Tagger_GetPossibleTags_ConcurrentCalls_GiveTheSameResultsAsOneThread` (eight threads look up words,
  ask the form question and tag text in between; every answer equals the one of a single thread).
- **Span overload** (.NET 8 and later): `Tagger_GetPossibleTags_Span_EqualsStringOverload` (words of every kind, also
  a slice of a longer text, give the result of the string overload in both modes) and
  `Tagger_GetPossibleTags_Span_InvalidInput_Throws`.
- **Allocation** (.NET 8 and later, where the per-thread allocation counter exists; `TestSupport/AllocationMeter.cs`, the
  minimum of the batches after warm-up): `Tagger_GetPossibleTags_DoesNotAllocate` (14 ASCII words in both modes through
  the string and the span overload: known, capitalized, unknown, digit, punctuation, case variants, compounds, malformed
  compounds and two words of 307 characters, one with a known last part, allocate 0 bytes per batch of 100 rounds,
  including reading `TagsWithoutAnalysis`), `Tagger_IsFormOf_DoesNotAllocate` (true and false cases, a compound head and
  an over-long compound: 0 bytes) and `Tagger_GetPossibleTags_AllocationOfTheOtherCases_IsRecordedAndBounded` (records,
  without a threshold, what the cases that may allocate cost, and asserts only the statements of the documentation: a
  million ASCII characters allocate 0 bytes and `GetAnalyses` allocates, except for a word without analyses). Measured
  values (.NET 10, Windows x64, bytes per call): non-ASCII word that is already NFC 0, curly apostrophe 0, decomposed
  word 32, span with an NFC non-ASCII character 32, with a decomposed one 64, a million non-ASCII characters that are
  already NFC 0, `GetAnalyses` of `tests` 360, of `well` (five analyses) 600, of the head of `well-tested` 368, of an
  unknown word 0. The tests do not run for `net481` (no per-thread allocation counter).
