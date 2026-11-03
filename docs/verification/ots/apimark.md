## ApiMark Verification

This document provides the verification evidence for the ApiMark OTS software item. Requirements
for this OTS item are defined in the ApiMark OTS Software Requirements document.

### Required Functionality

DemaConsulting.ApiMark generates the API reference of the PartOfSpeech package from its XML doc
comments, organized for gradual disclosure. A documented example must render as a fenced code
block and the terminology of the namespace documentation must survive into the generated pages.
ApiMark is also relied upon to enforce documentation coverage: `ApiMarkEnforceDocs=Public` with
severity `Error` fails the build when a public member lacks an XML summary.

### Verification Approach

ApiMark is verified by asserting the **real generated output of the real build**. The MSBuild
integration runs during the ordinary `dotnet build`, and the CI workflow then runs FileAssert
against the generated files in the same job, before the test step (tag `api-docs`). ApiMark's
`--validate` self-test is not used because it does not self-test generation. The shipped package is
asserted after `dotnet pack` (tag `package-docs`).

Each assertion checks content rather than existence, because a complete-looking file tree could
still hide examples rendered as prose or dropped remarks.

### Test Scenarios

#### ApiMark_ApiIndex

**Scenario**: ApiMark generates the API reference during the build.

**Expected**: `src/DemaConsulting.PartOfSpeech/generated/api/api.md` exists and names the
`DemaConsulting.PartOfSpeech` namespace.

**Requirement coverage**: `PartOfSpeech-OTS-ApiMark-Generate`.

#### ApiMark_ExampleCodeRendered

**Scenario**: A type documented with an `<example>` containing a `<code>` block is generated.

**Expected**: The `Tagger` type page contains a fenced `csharp` code block that calls `Tagger.Load`.

**Requirement coverage**: `PartOfSpeech-OTS-ApiMark-RenderExamples`.

#### ApiMark_TerminologyDocumented

**Scenario**: The terminology of the `NamespaceDoc` carrier is generated.

**Expected**: The namespace page `DemaConsulting.PartOfSpeech.md` contains the terms "UPOS",
"confidence", "lexicon" and "constraint".

**Requirement coverage**: `PartOfSpeech-OTS-ApiMark-RenderExamples`.

#### ApiMark_EntryPointDocumented

**Scenario**: The main entry points are documented and their pages are generated.

**Expected**: The `Tagger` page states the thread safety and the `InvalidDataException`, the
`Tagger.Tag` member page states `ArgumentNullException` and contains a fenced `csharp` example, and
the `TokenConstraint` page (the constraint hook) contains a fenced `csharp` example.

**What this proves**: only that the pages were generated from their documentation. It does not by
itself prove enforcement. Enforcement is proven by the build failing when a public member lacks an
XML summary: with severity `Error`, a green build cannot coexist with an undocumented public member.

**Requirement coverage**: `PartOfSpeech-OTS-ApiMark-EnforceDocs`.

#### PartOfSpeech_XmlDocExamples_Compile

**Scenario**: Every `<example><code>` block in the shipped XML documentation file of the package is
compiled with Roslyn against the real compiled API (an xUnit test of the test project, which runs on
every target framework of that project).

**Expected**: All documented examples compile without errors, and at least one example exists.

**Requirement coverage**: supporting evidence for `PartOfSpeech-OTS-ApiMark-RenderExamples`, because
the rendered examples are only useful to a reader if they are correct.

#### ApiMark_ApiDocsPackaged

**Scenario**: The package is created with `dotnet pack` after the build that generated the reference.

**Expected**: The single `.nupkg` contains `api/api.md`, `api/DemaConsulting.PartOfSpeech.md` and
`api/DemaConsulting.PartOfSpeech/Tagger.md` once each, with the index naming the namespace, the
namespace page containing "UPOS" and the `Tagger` page containing a fenced `csharp` block.

**Requirement coverage**: `PartOfSpeech-Lib-ApiDocsPackaged` (a software requirement; it verifies
the ApiMark packaging feature in use).

### Requirements Coverage

- **`PartOfSpeech-OTS-ApiMark-Generate`**: ApiMark_ApiIndex
- **`PartOfSpeech-OTS-ApiMark-RenderExamples`**: ApiMark_ExampleCodeRendered,
  ApiMark_TerminologyDocumented, PartOfSpeech_XmlDocExamples_Compile
- **`PartOfSpeech-OTS-ApiMark-EnforceDocs`**: ApiMark_EntryPointDocumented
