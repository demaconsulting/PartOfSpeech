## xUnit Verification

This document provides the verification evidence for the xUnit OTS software item. Requirements
for this OTS item are defined in the xUnit OTS Software Requirements document.

### Required Functionality

xUnit v3 (xunit.v3 and xunit.runner.visualstudio) is the unit-testing framework used by the
project. It discovers and runs all test methods and writes TRX result files that feed into coverage
reporting and requirements traceability. Passing tests confirm the framework is functioning
correctly.

### Verification Approach

xUnit is verified by self-validation evidence from the CI pipeline. Each scenario names a specific
test method that xUnit must discover, execute, and record in a TRX result file. A passing pipeline
run for all scenarios constitutes evidence that both requirements are satisfied. The named tests are
ordinary unit tests of the library that are stable and fast; they were chosen from different test
classes and kinds (plain facts, theories and tests of different software items) so that discovery
and reporting are shown for each.

### Test Scenarios

#### Upos_Values_AreSeventeenInAlphabeticalOrder

**Scenario**: xUnit discovers and runs this test; the test verifies the number and order of the tags.

**Expected**: xUnit executes the test, the test passes, and the result appears in the TRX output.

**Requirement coverage**: `PartOfSpeech-OTS-xUnit-Execute`, `PartOfSpeech-OTS-xUnit-Report`.

#### Upos_ToTagString_RoundTripsAllTags

**Scenario**: xUnit discovers and runs this test; the test verifies that every tag survives a round trip through its
name.

**Expected**: xUnit executes the test, the test passes, and the result appears in the TRX output.

**Requirement coverage**: `PartOfSpeech-OTS-xUnit-Execute`, `PartOfSpeech-OTS-xUnit-Report`.

#### Upos_TryParse_UnknownText_ReturnsFalse

**Scenario**: xUnit discovers and runs this test, a theory with several data rows, and records every row; the test
verifies that text that is not an exact tag name does not parse.

**Expected**: xUnit executes every row, the test passes, and the result appears in the TRX output.

**Requirement coverage**: `PartOfSpeech-OTS-xUnit-Execute`, `PartOfSpeech-OTS-xUnit-Report`.

#### UposSet_AddRemove_BehaveAsSet

**Scenario**: xUnit discovers and runs this test; the test verifies that adding and removing tags behaves like a set.

**Expected**: xUnit executes the test, the test passes, and the result appears in the TRX output.

**Requirement coverage**: `PartOfSpeech-OTS-xUnit-Execute`, `PartOfSpeech-OTS-xUnit-Report`.

#### UposSet_All_ContainsSeventeen

**Scenario**: xUnit discovers and runs this test; the test verifies that the full set has all 17 tags.

**Expected**: xUnit executes the test, the test passes, and the result appears in the TRX output.

**Requirement coverage**: `PartOfSpeech-OTS-xUnit-Execute`, `PartOfSpeech-OTS-xUnit-Report`.

#### Tokenizer_Tokenize_Punctuation_IsSeparated

**Scenario**: xUnit discovers and runs this test; the test verifies that sentence punctuation, brackets and quotes are
separate tokens.

**Expected**: xUnit executes the test, the test passes, and the result appears in the TRX output.

**Requirement coverage**: `PartOfSpeech-OTS-xUnit-Execute`, `PartOfSpeech-OTS-xUnit-Report`.

#### Lexicon_TryGetTags_AbsentSpellings_ReturnFalse

**Scenario**: xUnit discovers and runs this test; the test verifies that spellings that are not in a lexicon are not
found.

**Expected**: xUnit executes the test, the test passes, and the result appears in the TRX output.

**Requirement coverage**: `PartOfSpeech-OTS-xUnit-Execute`, `PartOfSpeech-OTS-xUnit-Report`.

#### BinaryFormat_Fnv1a_MatchesKnownVectors

**Scenario**: xUnit discovers and runs this test; the test verifies the checksum against published test vectors.

**Expected**: xUnit executes the test, the test passes, and the result appears in the TRX output.

**Requirement coverage**: `PartOfSpeech-OTS-xUnit-Execute`, `PartOfSpeech-OTS-xUnit-Report`.

### Requirements Coverage

- **`PartOfSpeech-OTS-xUnit-Execute`**: Upos_Values_AreSeventeenInAlphabeticalOrder,
  Upos_ToTagString_RoundTripsAllTags, Upos_TryParse_UnknownText_ReturnsFalse,
  UposSet_AddRemove_BehaveAsSet, UposSet_All_ContainsSeventeen,
  Tokenizer_Tokenize_Punctuation_IsSeparated, Lexicon_TryGetTags_AbsentSpellings_ReturnFalse,
  BinaryFormat_Fnv1a_MatchesKnownVectors
- **`PartOfSpeech-OTS-xUnit-Report`**: Upos_Values_AreSeventeenInAlphabeticalOrder,
  Upos_ToTagString_RoundTripsAllTags, Upos_TryParse_UnknownText_ReturnsFalse,
  UposSet_AddRemove_BehaveAsSet, UposSet_All_ContainsSeventeen,
  Tokenizer_Tokenize_Punctuation_IsSeparated, Lexicon_TryGetTags_AbsentSpellings_ReturnFalse,
  BinaryFormat_Fnv1a_MatchesKnownVectors
