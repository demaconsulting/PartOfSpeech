# System Verification Design

This document describes the system-level verification strategy for the Part Of Speech.

## Verification Approach

The Part Of Speech system is verified through system-level integration tests that
exercise the library as a whole from the perspective of a consumer. Tests instantiate the library
using its public API and assert on observable outputs, without relying on knowledge of internal
implementation details. No mocking or stubbing is required at the system level — the entire
integrated system is exercised as it would be used by a real caller.

System tests reside in `PartOfSpeechTests.cs` within the
`DemaConsulting.PartOfSpeech.Tests` project. The data and licensing tests reside in `PartOfSpeechDataTests.cs` in the
same project, with the helper `DataFiles.cs`; they read the committed data files, the project file and the third-party
notice, and assert on their content, format and checksums; each test proves one obligation of one requirement, and a test
that finds a file missing or a table empty fails instead of passing on nothing. The Python tools that produce the data
have developer unit tests in `tools/data/tests/`, which are not requirements evidence; they are reviewed with the
tools in the data tools review sets (see the Companion Artifact Structure of the design introduction).

## Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, databases, or network access required
- **Isolation**: Each test method constructs its own `Demo` instance; no shared state between tests
- **Files**: The data and licensing tests read `data/`, `THIRD-PARTY-NOTICES.md` and the library project file from the
  working copy. They find the repository root by walking up from the test binaries to the solution file, so they run
  from any checkout location. The checkout uses LF line endings (`.gitattributes`), which the checksums depend on.
  No network access is needed; the UD English-EWT treebank is not required.

## External Interface Simulation

The system has no external interfaces requiring simulation. It is a pure in-process .NET library
with no I/O, network calls, or platform services. System tests call the public API directly
with controlled inputs and verify returned values and thrown exceptions. The data and licensing tests read
repository files only; they simulate nothing and use no network.

## System-Level Test Scenarios

### Integration: Provides Expected Functionality

**Test**: `PartOfSpeech_SystemIntegration_DefaultConstruction_ReturnsExpectedGreeting`

Exercises end-to-end system behavior: constructs a `Demo` instance using the default constructor
and calls `DemoMethod` with a valid name. Asserts that the system produces the expected greeting
string `"Hello, System!"`, confirming that all components integrate correctly under default
configuration.

### Customization: Handles Configuration Properly

**Test**: `PartOfSpeech_SystemCustomization_CustomPrefix_ReturnsExpectedGreeting`

Verifies that the system correctly propagates a custom prefix supplied at construction time.
Constructs a `Demo` instance with prefix `"Welcome"`, calls `DemoMethod` with a valid name, and
asserts the result is `"Welcome, Integration!"`. Confirms that the configuration path through all
integrated components functions as expected.

### Validation: DemoMethod Null Input Throws ArgumentNullException

**Test**: `PartOfSpeech_SystemValidation_DemoMethodNullInput_ThrowsArgumentNullException`

Verifies that the system rejects a `null` argument to `DemoMethod` with `ArgumentNullException`.
Constructs a `Demo` instance with the default constructor and passes `null` to `DemoMethod`.
Confirms that the system boundary enforces the null-rejection contract.

### Validation: DemoMethod Empty Input Throws ArgumentException

**Test**: `PartOfSpeech_SystemValidation_DemoMethodEmptyInput_ThrowsArgumentException`

Verifies that the system rejects an empty-string argument to `DemoMethod` with `ArgumentException`.
Constructs a `Demo` instance with the default constructor and passes `string.Empty` to `DemoMethod`.
Confirms that the system boundary enforces the empty-string rejection contract.

### Validation: Constructor Null Prefix Throws ArgumentNullException

**Test**: `PartOfSpeech_SystemValidation_ConstructorNullPrefix_ThrowsArgumentNullException`

Verifies that the system rejects a `null` prefix argument at construction time with
`ArgumentNullException`. Attempts to construct a `Demo` instance with `null` as the prefix.
Confirms that the system boundary prevents invalid configuration from being established.

### Validation: Constructor Empty Prefix Throws ArgumentException

**Test**: `PartOfSpeech_SystemValidation_ConstructorEmptyPrefix_ThrowsArgumentException`

Verifies that the system rejects an empty-string prefix argument at construction time with
`ArgumentException`. Attempts to construct a `Demo` instance with `string.Empty` as the prefix.
Confirms that the system boundary prevents empty-string configuration from being established.

### Integration: Exposes Configured Prefix

**Test**: `PartOfSpeech_SystemIntegration_CustomPrefix_ExposesPrefix`

Verifies that the `Prefix` property exposes the prefix supplied at construction time. Constructs
a `Demo` instance with a custom prefix and reads the `Prefix` property. Confirms the system's
public API correctly surfaces the configured prefix to callers.

### Licensing: Notice Attributes MASC

**Test**: `PartOfSpeech_ThirdPartyNotices_Masc_IsAttributed`

Reads the MASC section of `THIRD-PARTY-NOTICES.md` with white space collapsed and asserts that it contains the title,
the creators, the source link, the name and link of the license and the citation of the corpus.

### Licensing: Notice Attributes Open English WordNet

**Test**: `PartOfSpeech_ThirdPartyNotices_OpenEnglishWordNet_IsAttributed`

Reads the Open English WordNet section of the notice and asserts that it contains the title, the creator, the two
source links and the name and link of the license.

### Licensing: Notice States the Conversion

**Test**: `PartOfSpeech_ThirdPartyNotices_Changes_AreStated`

Asserts that the MASC section states that the tags were converted to Universal POS tags and that the Open English
WordNet section states that the lexicon maps a spelling to the tags it may carry.

### Licensing: Notice Reproduces the Princeton WordNet License

**Test**: `PartOfSpeech_ThirdPartyNotices_PrincetonLicense_IsReproduced`

Reads the Princeton WordNet section of the notice and asserts that it contains the copyright line, the disclaimer and the
title clause of the license.

### Licensing: Notice Is Packed at the Package Root

**Test**: `PartOfSpeech_ThirdPartyNotices_Packaging_NoticeIsPackedAtPackageRoot`

Loads the library project file and asserts that exactly one item includes `THIRD-PARTY-NOTICES.md`, that the file
exists, and that the item has `Pack` set to true and a package path that is the package root.

### Licensing: Every Data Folder Declares an Allowed License

**Test**: `PartOfSpeech_DataLicensing_Folders_DeclareAllowedLicenses`

Asserts that the `README.md` of each of the seven data folders exists, declares a license, and that every license it
declares is in the allowed set (CC0 1.0, CC BY 3.0 US, CC BY 4.0, public domain, MIT).

### Licensing: No Source Has a Restricted License

**Test**: `PartOfSpeech_DataLicensing_SourceTables_RecordNoRestrictedLicense`

Reads the license basis of every row of the source tables of the open corpus, gold v2 and the silver data and asserts
that none names a share-alike, non-commercial, no-derivatives, Wikipedia or CC BY license; it fails if the tables are
empty.

### Licensing: UD English-EWT Is Not Committed

**Test**: `PartOfSpeech_DataLicensing_UdEwt_IsNotCommitted`

Walks the repository (without dependencies, build output and the git-ignored download cache) and asserts that no
CoNLL-U file exists, that no path below `data/` names the treebank, and that the UD English-EWT folder holds only its
documentation.

### Licensing: SemCor and Brown Are Not Committed

**Test**: `PartOfSpeech_DataLicensing_RestrictedCorpora_AreNotCommitted`

Walks the data folder and asserts that no path names SemCor or the Brown corpus.

### Provenance: READMEs Record Source and Production

**Test**: `PartOfSpeech_DataProvenance_Readmes_RecordSourceAndProduction`

For each of the eight data folders (the folder of UD English-EWT included) asserts that the `README.md` makes the
statements that the folder's own README is structured around (for example the headings and fields for source,
license basis, how produced, labels and sentences), that it names a pinned commit or SHA-256, and that every script of
the data tools that it names exists. The central `data/README.md` and `data/CONVENTIONS.md` must exist.

### Provenance: Every Document Is Listed in a Source List

**Test**: `PartOfSpeech_DataProvenance_Sources_ListEveryDocument`

Asserts that every document id of the open train, dev and test splits and of the challenge set appears in
`open/sources.tsv`, every document of gold v2 in its `sources.tsv`, every document of the silver data in
`silver/sources.tsv`, and every MASC document in `masc/sources.tsv` (and that every row of that list has sentences in
`masc.tsv`). It fails when a data set or a list is empty.

### Provenance: Source Rows Record License and URL

**Test**: `PartOfSpeech_DataProvenance_Sources_RowsRecordLicenseAndUrl`

Reads the three source tables and asserts, for every row, that the license basis begins with an allowed license class,
that the URL (and the terms URL where the table has one) is an absolute HTTPS URL without white space (an empty URL is
accepted only for the document that the project wrote), and that the sentence count is a positive number. For the MASC
table (one license for the whole corpus, no URL) it asserts that the id is a written-genre document of the listed genre,
that the license class is `CC BY 3.0 US`, that the sentence and token counts are positive and equal the counts of the
document in `masc.tsv`, and that the counts sum to the totals of the file.

### Provenance: The Manifest Lists Exactly the Data Files

**Test**: `PartOfSpeech_DataProvenance_Manifest_ListsExactlyTheCommittedFiles`

Reads `data/MANIFEST.tsv` and asserts that it lists exactly the committed data files (Markdown files and the manifest
itself excluded): no file is missing from the manifest and no listed file is missing from the folder.

### Provenance: Checksums Match

**Test**: `PartOfSpeech_DataProvenance_Manifest_ChecksumsMatchCommittedFiles`

Asserts that the size and the SHA-256 of every file that the manifest lists match the recorded values.

### Data: Only Universal POS Tags Are Used

**Test**: `PartOfSpeech_TrainingData_Corpora_UseOnlySeventeenUposTags`

Reads the training corpora and the token-level evaluation corpora and asserts that every token carries one of the 17
Universal POS tags (the placeholder is allowed only for masked tokens).

### Data: Tokens Rejoin to the Sentence Text

**Test**: `PartOfSpeech_TrainingData_Corpora_TokensRejoinToSentenceText`

Reads the training corpora and the token-level evaluation corpora and asserts that, for every sentence, the tokens
concatenated without spaces equal the sentence text without spaces; it fails if the corpora are empty.

### Data: Single-Target Sets Use the Documented Classes

**Test**: `PartOfSpeech_EvaluationData_SingleTargetSets_UseOnlyDocumentedClasses`

Asserts that every target of gold v2 and of the shape probe has one of the eleven documented coarse classes.

### Data: No Document Is Shared

**Test**: `PartOfSpeech_Leakage_Documents_TrainingAndEvaluationAreDisjoint`

Collects the document ids of all evaluation sets (open dev and test, gold v2, the shape probe and the challenge set) and
asserts that no training sentence of the open, MASC or silver corpus comes from one of them.

### Data: No Sentence Is Shared

**Test**: `PartOfSpeech_Leakage_Sentences_TrainingAndEvaluationAreDisjoint`

Collects the sentence keys (lower-case letters and digits; a text without any has no key) of all evaluation sets and
asserts that no training sentence has one of them, whatever the length of the key: a short sentence such as "Thank you."
is compared like a long one. The test `PartOfSpeech_Leakage_SentenceKey_KeepsLowerCaseLettersAndDigits` checks the
sentence key on fixtures, a short sentence included; it supports the sentence test and is not linked to a requirement.

### Data: The Leakage Report Shows No Overlap

**Test**: `PartOfSpeech_Leakage_Report_RecordsNoOverlaps`

Reads the committed leakage report and asserts that it covers every training set against every committed evaluation set
and the three UD English-EWT files, that every row has the six columns, and that no row records a document overlap or an
exact sentence overlap.

### Data: Lexicon Covers the Training Set

**Test**: `PartOfSpeech_Lexicon_Coverage_Train_MeetsThreshold`

Reads the lexicon and asserts that, for the non-disputed tokens of the open training set, the lexicon and the token-shape
rules allow the gold tag for at least 99.9 percent of the tokens. The test
`PartOfSpeech_Lexicon_TokenShapeRules_AllowTagsByShape` checks the token-shape rules on a small fixture; it supports the
coverage tests and is not linked to a requirement.

### Data: Lexicon Covers the Development Set

**Test**: `PartOfSpeech_Lexicon_Coverage_Dev_MeetsThreshold`

The same measurement for the non-disputed tokens of the open development set.

### Data: Lexicon Rows Are Well Formed

**Test**: `PartOfSpeech_Lexicon_Format_RowsAreSortedUniqueAndValid`

Asserts that every lexicon row is a spelling with alphabetical Universal POS tags, that the rows are sorted by the bytes
of their UTF-8 encoding and that no spelling occurs twice; it fails if the lexicon is empty.

### Data: Lexicon Header Counts Match

**Test**: `PartOfSpeech_Lexicon_Format_HeaderCountsMatchRows`

Asserts that the counts of spellings and tag entries stated in the header of the lexicon are the counts of its rows.

## Acceptance Criteria

A system-level test run passes when all scenarios above pass without error or exception beyond
those explicitly asserted. Any unexpected exception, wrong exception type, wrong return value, or
data and licensing finding (a missing notice element, a disallowed license, a checksum difference, an invalid tag, a
shared training and evaluation sentence, or a lexicon coverage below the gate) constitutes a failure.
