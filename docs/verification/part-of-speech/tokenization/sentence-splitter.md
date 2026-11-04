### SentenceSplitter Verification

This document describes the unit-level verification strategy for the `SentenceSplitter` unit.

#### Verification Approach

The unit is verified by unit tests in `Tokenization/SentenceSplitterTests.cs`. Each test builds a token list from a
small text (with `Tokenizer.TokenizeWords`, which is real and stateless, so that offsets and white space between tokens
are the real ones) and calls `SentenceSplitter.Split`, asserting the sentence texts. Nothing is mocked. White space
between tokens matters for blank lines and for closing quotes, so the fixtures use LF, CRLF and CR line breaks. The
corpus-wide boundary measure is at subsystem level.

#### Test Environment

No setup beyond the standard test runner: no files and no network.

#### Acceptance Criteria

All tests pass on every target framework. A test fails when a boundary is missing, extra or in the wrong place, or when
a token is lost or duplicated across sentences.

#### Test Scenarios

- **Terminal punctuation**: `SentenceSplitter_Split_PeriodQuestionExclamation_EndSentences` (a period, question mark
  and exclamation mark end a sentence before a capitalized word).
- **Closing quotes**: `SentenceSplitter_Split_ClosingQuoteStaysWithSentence` (a closing quote or bracket stays with the
  sentence it closes, an opening quote starts the next).
- **Abbreviations**: `SentenceSplitter_Split_AbbreviationDoesNotEndSentence` (titles and initials) and
  `SentenceSplitter_Split_FinalAbbreviationEndsSentence` (at the end of the text or before a capitalized word).
- **Lower-case continuation**: `SentenceSplitter_Split_NoSplitBeforeLowerCase` and
  `SentenceSplitter_Split_EllipsisBeforeLowerCaseContinues` (an ellipsis continues the sentence before a lower-case word
  and ends it before a capitalized one).
- **Paragraphs**: `SentenceSplitter_Split_BlankLineEndsSentence` (also without terminal punctuation, for LF, CRLF and
  CR) and `SentenceSplitter_Split_SingleLineBreakDoesNotEndSentence`.
- **No terminator**: `SentenceSplitter_Split_TextWithoutTerminator_IsOneSentence` (one sentence; an empty token list
  gives none).
