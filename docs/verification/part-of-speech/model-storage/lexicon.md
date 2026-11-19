### Lexicon Verification

This document describes the unit-level verification strategy for the `Lexicon` unit (reader, search, analyses,
token-shape rules and the writer of the source tables).

#### Verification Approach

The unit is verified by unit tests in `ModelStorage/LexiconTests.cs`. Small lexicons are compiled in memory from
hand-written rows (and hand-written analyses) by the real `LexiconWriter` (so the writer and the reader are verified
against each other), and corrupted variants are produced by changing bytes of those files and recomputing the checksum,
so that only the structure check can catch the damage. The lookup is tested over several block sizes so that
the block-head search and the scan boundaries are exercised, and the shape rules are tested against an independent
implementation of the same rules in the data tests helper (`DataFiles.cs`) for every spelling of the committed lexicon.
The analyses are tested on every shape that the template encoding has (the spelling itself, a cut with a suffix, a
different word, non-ASCII base words, a cut next to a multi-byte character, seven analyses on one spelling), compressed
and not, and on the real embedded lexicon. The coverage test uses the real binary lexicon and the development corpus.
Nothing is mocked.

#### Test Environment

The rule-agreement and coverage tests read `data/lexicon/lexicon.tsv` and the development corpus of `data/open/` through
`DataFiles.cs`;
the other tests need nothing beyond the standard test runner. No network is needed.

#### Acceptance Criteria

All tests pass. A test fails when a spelling of a small lexicon is not found with its tags or its position, a spelling
that is absent is found, a damaged file is accepted (including every inconsistency of the analyses section), a
source-table rule violation is not reported, an analysis is not read back exactly as written, the allowed tags differ
from the rule (for any lexicon spelling or shape variant), or the coverage of the non-disputed development tokens is
below 99.9 percent.

#### Test Scenarios

- **Lookup**: `Lexicon_TryGetTags_FindsEverySpellingOfSmallLexicon` (a theory over block sizes),
  `Lexicon_TryGetTags_FirstAndLastOfEveryBlock`, `Lexicon_TryGetTags_AbsentSpellings_ReturnFalse` (including near
  misses), `Lexicon_TryGetTags_NonAsciiSpelling_IsFound` (and a surrogate pair) and
  `Lexicon_TryGetTags_LongerThanMaxSpelling_ReturnsFalse`; `Lexicon_TryFind_ReportsThePositionOfEverySpelling` (a theory
  over block sizes: the ordinal of every spelling is its index in ascending order).
- **Analyses**: `Lexicon_Compile_WithoutAnalyses_ReadsBackWithEmptyAnalyses` (a lexicon compiled without an analyses
  table is a version 3 file with the one empty feature set, no template and the one empty list; it has no analysis, its
  `AnalyzedTags` and `GetAnalyses` are empty and the tags enumerate as written),
  `Lexicon_GetAnalyses_EveryShapeOfAnalysis_RoundTrips` (a theory over compressed and plain files: every shape of
  template (no cut, a cut inside the word, the whole word replaced, a suffix only, non-ASCII base words, a cut next to a
  multi-byte character, seven analyses on one spelling) reads back with its base word, tag and features in the given
  order whatever the input order, `AnalyzedTags` is the set of their tags and a subset of the tags of the spelling, the
  tags themselves are unchanged, and the counts add up), `Lexicon_HasAnalysis_ComparesTagAndComposedBaseWord` (true only
  for an analysis that exists: a wrong tag, the spelling itself as the base word, a prefix of the base word or another
  word is refused, also for non-ASCII) and `Lexicon_GetAnalyses_EmbeddedTests_ReturnsNounAndVerbInOrder` (the embedded
  lexicon gives `test NOUN Number=Plur` and `test VERB Person=3|Tense=Pres` for `tests`, and the rule-added adjective of
  `testing` has no analysis).
- **Writer and source tables**: `LexiconWriter_FromTsv_RejectsUnsortedDuplicateOrInvalid`,
  `LexiconWriter_FromTsv_CountsMatchHeaderComment` (the header counts are checked and the committed table passes with
  its own counts) and `LexiconWriter_Write_UncompressedAndCompressed_DecodeEqual`;
  `LexiconWriter_AnalysesFromTsv_ParsesRowsAndChecksHeaderCounts` (rows, features and the header counts) and
  `LexiconWriter_AnalysesFromTsv_RejectsUnsortedUnknownOrInvalid` (a theory of rows that are unsorted or repeated, name
  a spelling or a tag that the lexicon lacks, have no analysis, a missing tag, an extra cell or a lower-case tag, list
  the analyses of a spelling out of order, or have malformed or unordered features or a base word that the fold of the
  tagger would change: not NFC, with a capital letter or with a typographic quote);
  `LexiconWriter_Compile_InvalidAnalyses_Throws` (a theory of scenarios: unknown or unsorted spellings, a tag that the
  spelling lacks, unordered or duplicate analyses, an empty list, too many analyses, malformed or unordered features, an
  empty, white-space, too long, not NFC, not well-formed (unpaired surrogate), upper-case (ASCII and non-ASCII) or
  typographic (apostrophe, dash, ellipsis) base word);
  `FeatureExtractor_IsFoldFixedPoint_AgreesWithTheFoldOfTheWorkspace` (the test for a stored base word equals "the
  lower-cased folded form that the decoder's workspace holds is the text" for every UTF-16 code unit and for words that
  mix the cases of the rule, so the writer applies the fold that `IsFormOf` uses);
  `LexiconWriter_Compile_MoreThanMaximumLists_Throws` (the largest accepted number of lists compiles and loads, one more
  is refused); `LexiconWriter_Compile_Analyses_IsDeterministic` (the same tables compile to the same bytes in any input
  order of the analyses, and the compressed and the plain file have the same uncompressed image).
- **Validation on load**: `Lexicon_Load_CorruptChecksum_Throws` (a flipped payload byte),
  `Lexicon_Load_Truncated_Throws` (truncation and trailing bytes), `Lexicon_Load_TagCountMismatch_Throws` and
  `Lexicon_Load_InvalidStructureWithValidChecksum_Throws` (a block index that does not start at zero),
  `Lexicon_Load_OversizedVarintWithValidChecksum_Throws` (a varint above 2^31-1 at each length or index site, with the
  header lengths and the checksum fixed, must give `InvalidDataException` and no other exception type),
  `Lexicon_Load_DeflateTrailingBytesInStoredRegion_Throws` (a compressed file with bytes appended after the Deflate data
  and the stored length raised to match is refused: with the old checksum by the checksum of the stored bytes, and with
  a recomputed checksum by the strict inflation), `Lexicon_Load_DeflateLastByteRemoved_Throws` (the last compressed byte
  removed and the stored length adjusted: refused by the checksum when it is left alone, and with a "cut off" message
  when it is recomputed, so that only the strict inflation can catch it), `Lexicon_Load_DeflateCutAtAnyPoint_Throws`
  (every proper prefix of the compressed region, with the stored length and the checksum adjusted, gives
  `InvalidDataException` and no other exception type), `Lexicon_Load_DeflatePayloadLengthTooLarge_Throws` and
  `Lexicon_Load_DeflatePayloadLengthTooSmall_Throws` (a declared payload length that is too large or too small, with
  intact stored bytes, is "shorter" or "longer than declared") and `Lexicon_Load_PayloadLengthAboveMaximum_Throws` (a
  declared payload length above 16 MiB is refused before inflation).
- **Validation of the analyses section on load** (each corruption patches a valid file and recomputes the checksum):
  `Lexicon_Load_ListIndexOutOfRange_Throws`, `Lexicon_Load_ListTagsNotASubsetOfTheSpellingTags_Throws`,
  `Lexicon_Load_CutLongerThanSpelling_Throws`, `Lexicon_Load_CutInsideACharacter_Throws`,
  `Lexicon_Load_InvalidSuffix_Throws` (a theory: not UTF-8, and starting with a continuation byte),
  `Lexicon_Load_UnsortedFeatureSets_Throws`, `Lexicon_Load_MalformedFeatureSet_Throws`,
  `Lexicon_Load_TemplateIndexOutOfRange_Throws`, `Lexicon_Load_ListTagsDecrease_Throws`,
  `Lexicon_Load_SameTagAnalysesInWrongBaseWordOrder_Throws` and `Lexicon_Load_SameTagAnalysesInWrongFeatureOrder_Throws`
  (two templates of one tag exchanged in a list: tags that do not decrease are not enough, the base words and then the
  features must ascend), `Lexicon_Load_ListInOrderForOneSpellingButNotForAnother_Throws` (two spellings that use the
  same two templates in different orders; one is pointed at the list of the other, which composes the base words in the
  wrong order for it, so the order is checked for each spelling),
  `Lexicon_Load_GeneratedAnalyses_AcceptsWriterOrderAndRefusesExchangedNeighbors` (40 generated lexicons with ASCII,
  two-, three- and four-byte characters: every file that the writer produces loads and reads back in the written order,
  and exchanging two neighbors of one tag in a list is refused in every list where that is possible, which ties the
  comparison of the loader to the one of the writer, including UTF-8 byte order against UTF-16 order),
  `Lexicon_Load_ComposedBaseWordNotNormalized_Throws` (a template patched to compose `cafe` and U+0301),
  `Lexicon_Load_ComposedBaseWordNotFolded_Throws` (a theory: a suffix patched to an upper-case ASCII letter, an
  upper-case letter of two bytes or U+02BC is refused, and the unpatched file loads),
  `Lexicon_Load_LowerCaseTwoByteSuffixIsAcceptedAndKeptUpperCaseIsRefused` (a patched lower-case letter of two bytes is
  accepted; a template that keeps a capital of the spelling is refused),
  `Lexicon_Load_LowerCaseBaseWordsOfTwoBytes_AreAcceptedAndKeepTheirText` (base words with two-byte letters, kept or in
  the suffix, load and read back as written), `Lexicon_Load_UpperCaseLetterInAKeptPrefix_Throws` (a theory over the
  position of a capital in a 26-byte spelling, including the edges of the eight-byte steps of the scan) and
  `Lexicon_Load_CharactersNextToCapitalsInALongBaseWord_AreAccepted` (`@`, `[`, the grave accent, the brace, DEL and
  non-ASCII lower-case letters in long base words are accepted),
  `Lexicon_Load_CharactersBelowU0300_AreStableUnderNormalizationFormC` (the premise of the fast path: every character
  below U+0300 and every pair of them is in form C, and `a` with U+0300 is not),
  `FeatureExtractor_IsFoldStable_AgreesWithIsFoldFixedPointForEveryCharacter` (the character test of the scan equals the
  test for a text, for every character that is in form C by itself),
  `EmbeddedModel_Lexicon_EveryBaseWordIsAFixedPointOfTheFold` (all 290,365 base words of the embedded lexicon),
  `Lexicon_Load_ListZeroNotEmpty_Throws`, `Lexicon_Load_RepeatedTemplate_Throws`,
  `Lexicon_Load_AnalysesRegionsDoNotFillThePayload_Throws`, `Lexicon_Load_AnalysisCountMismatch_Throws`,
  `Lexicon_Load_TemplateCountLargerThanThePayload_Throws` (a hostile count is refused before an array is sized from it)
  and `Lexicon_Load_InvalidAnalysesHeaderField_Throws` (a theory over header offsets and values: no feature set, no
  list, more lists than the format allows, a reserved field that is not zero). That a file of version 2 is refused with
  a message that names both the version found (2) and the version that the build reads (`Lexicon.FormatVersion`, 3) is
  asserted in `ModelStorage_Formats_StartWithMagicAndVersion` (*ModelStorage Verification*).
- **Allowed tags by rule**: `Lexicon_AllowedTags_UnknownWord_AllowsAllSeventeen`,
  `Lexicon_AllowedTags_KnownWord_AllowsLexiconTags`, `Lexicon_AllowedTags_NoLetterOrDigit_AddsPunctSymX`,
  `Lexicon_AllowedTags_Digit_AddsNumSymXNounPropnAdj` and `Lexicon_AllowedTags_FirstUpper_AddsPropnX`.
- **No allocation** (.NET targets only, where the per-thread allocation counter exists):
  `Lexicon_AllowedTags_KnownTokenShapes_DoNotAllocate` (lookups of a punctuation, a digit and an upper-case-first token
  and all four shape-rule combinations allocate zero bytes after warm-up, taken as the minimum over 10 batches of 1000
  rounds by `TestSupport/AllocationMeter.cs`, and the same for `TryFind` and `AnalyzedTags` of an upper-case key; it
  supports the decoder requirement
  `PartOfSpeech-Tagging-TagDecoder-NoAllocation`). `HasAnalysis` is proved allocation-free through
  `Tagger_IsFormOf_DoesNotAllocate` (*Tagger Verification*), which runs it with the real lexicon.
- **Allowed tags against the reference rule**: `Lexicon_AllowedTags_MatchesDataFilesRuleForEveryLexiconSpelling` (the
  compiled rules agree with the reference implementation for every lexicon spelling and shape variants).
- **Coverage**: `Lexicon_AllowedTags_Dev_MeetsCoverageThreshold` (the binary lexicon with the shape rules allows the
  gold tag of at least 99.9 percent of the non-disputed development tokens).
