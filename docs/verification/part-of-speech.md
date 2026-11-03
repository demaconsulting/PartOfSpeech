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
notice, and assert on their content, format and checksums. The Python tools that produce the data have developer unit
tests in `tools/data/tests/`, which are not requirements evidence.

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

### Licensing: Third-Party Notice Attributes MASC and Open English WordNet

**Test**: `PartOfSpeech_ThirdPartyNotices_Content_AttributesMascAndOpenEnglishWordNet`

Reads `THIRD-PARTY-NOTICES.md` with white space collapsed and asserts that it contains, for MASC and for Open English
WordNet, the title, the creators, the source link, the license name and link, and the statement that the data was
converted, and that it reproduces the Princeton WordNet license (copyright line, disclaimer and title clause).

### Licensing: Notice Is Packed at the Package Root

**Test**: `PartOfSpeech_ThirdPartyNotices_Packaging_NoticeIsPackedAtPackageRoot`

Loads the library project file and asserts that exactly one item includes `THIRD-PARTY-NOTICES.md`, that the file
exists, and that the item has `Pack` set to true and a package path that is the package root.

### Licensing: Only Open Licenses Are Used

**Tests**: `PartOfSpeech_DataLicensing_Folders_DeclareAllowedLicensesOnly`,
`PartOfSpeech_DataLicensing_Tree_ContainsNoRestrictedCorpusFiles`

The first test asserts that the `README.md` of every data folder declares a license that is in the allowed set (CC0
1.0, CC BY 3.0 US, CC BY 4.0, public domain, MIT) and that no row of the source tables names a share-alike,
non-commercial, no-derivatives, Wikipedia or CC BY license. The second test walks the repository (without dependencies,
build output and the git-ignored download cache) and asserts that no CoNLL-U file exists, that no path below `data/`
names SemCor, the Brown corpus or the English Web Treebank, and that the UD English-EWT folder holds only its
documentation.

### Provenance: Sources Are Recorded and Checksums Match

**Tests**: `PartOfSpeech_DataProvenance_Folders_HaveReadmeLicenseAndSource`,
`PartOfSpeech_DataProvenance_Manifest_ChecksumsMatchCommittedFiles`

The first test asserts that every data folder has a `README.md` with a license line and a record of the source, and that
the source tables of the open corpus, gold v2 and silver data exist. The second test reads `data/MANIFEST.tsv` and
asserts that it lists exactly the committed data files (Markdown files and the manifest itself excluded) and that the
size and SHA-256 of every file match.

### Data: Only Universal POS Tags Are Used

**Tests**: `PartOfSpeech_TrainingData_Corpora_UseOnlySeventeenUposTags`,
`PartOfSpeech_EvaluationData_SingleTargetSets_UseOnlyDocumentedClasses`

The first test reads the training corpora and the token-level evaluation corpora and asserts that every token carries one
of the 17 Universal POS tags (the placeholder is allowed only for masked tokens) and that the tokens of every sentence
concatenate to its text. The second test asserts that every target of gold v2 and of the shape probe has one of the
documented coarse classes.

### Data: Training and Evaluation Data Are Isolated

**Tests**: `PartOfSpeech_Leakage_TrainingVersusEvaluation_NoSharedDocumentOrSentence`,
`PartOfSpeech_Leakage_Report_RecordsNoOverlaps`

The first test collects the documents and sentence keys (lower-case letters and digits, at least 25 characters) of all
evaluation sets and asserts that no training document and no training sentence matches. The second test asserts that the
committed leakage report covers every training set and records zero document and zero exact sentence overlaps. The test
`PartOfSpeech_Leakage_SentenceKey_KeepsLowerCaseLettersAndDigits` checks the sentence key on a fixture; it supports the
isolation test and is not linked to a requirement.

### Data: Lexicon Coverage Meets the Gate

**Tests**: `PartOfSpeech_Lexicon_Coverage_TrainAndDev_MeetsThreshold`,
`PartOfSpeech_Lexicon_Format_RowsAreSortedUniqueAndValid`

The first test reads the lexicon and asserts that, for the non-disputed tokens of the open training and development sets,
the lexicon and the token-shape rules allow the gold tag for at least 99.9 percent of the tokens. The second test
asserts that the lexicon rows are sorted by UTF-8 bytes, unique and valid, and that the counts in its header are
correct. The test `PartOfSpeech_Lexicon_TokenShapeRules_AllowTagsByShape` checks the token-shape rules on a small
fixture; it supports the coverage test and is not linked to a requirement.

## Acceptance Criteria

A system-level test run passes when all scenarios above pass without error or exception beyond
those explicitly asserted. Any unexpected exception, wrong exception type, wrong return value, or
data and licensing finding (a missing notice element, a disallowed license, a checksum difference, an invalid tag, a
shared training and evaluation sentence, or a lexicon coverage below the gate) constitutes a failure.
