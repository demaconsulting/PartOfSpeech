### Tokenizer Verification

This document describes the unit-level verification strategy for the `Tokenizer` unit (`Tokenizer`, `Token`, `Sentence`).

#### Verification Approach

The unit is verified by unit tests in `Tokenization/TokenizerTests.cs` that call `Tokenizer.Tokenize` and
`Tokenizer.CreateSentence` with small texts, one fixture per rule of the tokenization convention (each rule of
`data/CONVENTIONS.md`), and assert the exact token texts and offsets. The unit is stateless and has one internal
dependency, `SentenceSplitter`; it is not mocked, because the tests check tokens, not sentence boundaries. Rules with many
cases are theories. The corpus-wide evidence (99.8 percent gate on 14,980 sentences) is at subsystem level; the corpus
residue that the rules deliberately do not reproduce is: the period of a sentence-final "no." (3 sentences, kept split
because it is a common sentence end), "CO." (1), "I." before a capitalized word or as a Roman numeral (3), `£9` and `£70`
(2, gold keeps the currency symbol attached, the rule splits it like `$`) and U+2015 inside words (5, gold keeps it whole).

#### Test Environment

No setup beyond the standard test runner: no files and no network.

#### Acceptance Criteria

All tests pass on every target framework. A test fails when a token text or offset differs from the expected one, when a
rule fixture is not tokenized as the convention says, or when an invalid argument is not rejected with the specified
exception.

#### Test Scenarios

- **Offsets index the original text**: `Tokenizer_Tokenize_Offsets_IndexOriginalText`;
  `Tokenizer_Tokenize_AdversarialWhiteSpace_KeepsOffsets` (tabs, line breaks, non-breaking spaces and runs of spaces
  separate tokens and never end up in them); `Tokenizer_Tokenize_SurrogatePairs_StayWhole` (characters outside the
  basic plane stay in one token).
- **Punctuation is separate**: `Tokenizer_Tokenize_Punctuation_IsSeparated` (sentence punctuation, brackets and quotes),
  `Tokenizer_Tokenize_LeadingCurrencySymbol_IsSeparated` and `Tokenizer_Tokenize_Percent_IsSeparated`.
- **Contractions are split**: `Tokenizer_Tokenize_NotContractions_AreSplit` (a theory over `n't`),
  `Tokenizer_Tokenize_IrregularContractions_AreSplit` (`ca`, `wo`, `sha`, `ai` and `cannot`),
  `Tokenizer_Tokenize_Clitics_AreSplit` (`'s`, `'m`, `'re`, `'ve`, `'ll`, `'d`), `Tokenizer_Tokenize_CaseIsPreserved`,
  `Tokenizer_Tokenize_TypographicApostrophe_IsSplitButKept` (recognized, but the token keeps the original character)
  and `Tokenizer_Tokenize_WholeWordExceptions_StayWhole` (words that only look like contractions).
- **Compounds, numbers and symbols stay whole**: `Tokenizer_Tokenize_HyphenatedCompounds_StayWhole`,
  `Tokenizer_Tokenize_DoubleHyphen_IsSeparate`, `Tokenizer_Tokenize_SlashAndAmpersandWords_StayWhole` (standalone ones
  are tokens) and `Tokenizer_Tokenize_NumbersAndTimes_StayWhole` (numbers, times, ordinals and decades).
- **Periods**: `Tokenizer_Tokenize_Abbreviations_KeepPeriod`, `Tokenizer_Tokenize_InitialsAndAcronyms_KeepPeriod` and
  `Tokenizer_Tokenize_SentenceFinalI_SplitsPeriod` (the pronoun I and a final letter do not keep the period, a quoted
  letter does).
- **Ellipsis**: `Tokenizer_Tokenize_TrailingEllipsis_IsSeparate` (dots between spaces are one token each) and
  `Tokenizer_Tokenize_InnerEllipsis_StaysWhole` (inside a word, or in front of it, it stays attached).
- **Addresses**: `Tokenizer_Tokenize_UrlsAndEmail_StayWhole` (single tokens without trailing punctuation).
- **Typographic punctuation**: `Tokenizer_Tokenize_TypographicQuotesAndDashes_AreSeparate` (quotes, dashes and the
  ellipsis character are separate tokens that keep their characters).
- **Pre-tokenized input**: `Tokenizer_CreateSentence_JoinsTokensWithSpaces` (single spaces, offsets index the joined
  text) and `Tokenizer_CreateSentence_InvalidTokens_Throw` (null, empty and white-space tokens).
- **Input validation**: `Tokenizer_Tokenize_NullText_ThrowsArgumentNullException` and
  `Tokenizer_Tokenize_EmptyOrWhiteSpace_ReturnsNoSentences`.
