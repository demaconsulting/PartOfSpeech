### Lexicon Verification

This document describes the unit-level verification strategy for the `Lexicon` unit (reader, search, token-shape rules
and the writer of the source table).

#### Verification Approach

The unit is verified by unit tests in `ModelStorage/LexiconTests.cs`. Small lexicons are compiled in memory from
hand-written rows by the real `LexiconWriter` (so the writer and the reader are verified against each other), and
corrupted variants are produced by changing bytes of those files. The lookup is tested over several block sizes so that
the block-head search and the scan boundaries are exercised, and the shape rules are tested against an independent
implementation of the same rules in the data tests helper (`DataFiles.cs`) for every spelling of the committed lexicon.
The coverage test uses the real binary lexicon and the development corpus. Nothing is mocked.

#### Test Environment

The rule-agreement and coverage tests read `data/lexicon/lexicon.tsv` and the development corpus of `data/open/` through
`DataFiles.cs`;
the other tests need nothing beyond the standard test runner. No network is needed.

#### Acceptance Criteria

All tests pass. A test fails when a spelling of a small lexicon is not found with its tags, a spelling that is absent is
found, a damaged file is accepted, a source-table rule violation is not reported, the allowed tags differ from the rule
(for any lexicon spelling or shape variant), or the coverage of the non-disputed development tokens is below 99.9
percent.

#### Test Scenarios

- **Lookup**: `Lexicon_TryGetTags_FindsEverySpellingOfSmallLexicon` (a theory over block sizes),
  `Lexicon_TryGetTags_FirstAndLastOfEveryBlock`, `Lexicon_TryGetTags_AbsentSpellings_ReturnFalse` (including near
  misses), `Lexicon_TryGetTags_NonAsciiSpelling_IsFound` (and a surrogate pair) and
  `Lexicon_TryGetTags_LongerThanMaxSpelling_ReturnsFalse`.
- **Writer and source table**: `LexiconWriter_FromTsv_RejectsUnsortedDuplicateOrInvalid`,
  `LexiconWriter_FromTsv_CountsMatchHeaderComment` (the header counts are checked and the committed table passes with
  its own counts) and `LexiconWriter_Write_UncompressedAndCompressed_DecodeEqual`.
- **Validation on load**: `Lexicon_Load_CorruptChecksum_Throws` (a flipped payload byte), `Lexicon_Load_Truncated_Throws`
  (truncation and trailing bytes), `Lexicon_Load_TagCountMismatch_Throws` and
  `Lexicon_Load_InvalidStructureWithValidChecksum_Throws` (a block index that does not start at zero),
  `Lexicon_Load_OversizedVarintWithValidChecksum_Throws` (a varint above 2^31-1 at each length or index site, with the
  header lengths and the checksum fixed, must give `InvalidDataException` and no other exception type),
  `Lexicon_Load_DeflateTrailingBytesInStoredRegion_Throws` (a compressed file with bytes appended after the Deflate
  data and the stored length raised to match, so that the payload checksum is still right, is refused) and
  `Lexicon_Load_PayloadLengthAboveMaximum_Throws` (a declared payload length above 16 MiB is refused before inflation).
- **Allowed tags by rule**: `Lexicon_AllowedTags_UnknownWord_AllowsAllSeventeen`,
  `Lexicon_AllowedTags_KnownWord_AllowsLexiconTags`, `Lexicon_AllowedTags_NoLetterOrDigit_AddsPunctSymX`,
  `Lexicon_AllowedTags_Digit_AddsNumSymXNounPropnAdj` and `Lexicon_AllowedTags_FirstUpper_AddsPropnX`.
- **No allocation** (.NET targets only, where the per-thread allocation counter exists):
  `Lexicon_AllowedTags_KnownTokenShapes_DoNotAllocate` (lookups of a punctuation, a digit and an upper-case-first token
  and all four shape-rule combinations allocate zero bytes after warm-up; it supports the decoder requirement
  `PartOfSpeech-Tagging-TagDecoder-NoAllocation`).
- **Allowed tags against the reference rule**: `Lexicon_AllowedTags_MatchesDataFilesRuleForEveryLexiconSpelling` (the
  compiled rules agree with the reference implementation for every lexicon spelling and shape variants).
- **Coverage**: `Lexicon_AllowedTags_Dev_MeetsCoverageThreshold` (the binary lexicon with the shape rules allows the gold
  tag of at least 99.9 percent of the non-disputed development tokens).
