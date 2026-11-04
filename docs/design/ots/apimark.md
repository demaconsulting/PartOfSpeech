## ApiMark

### Purpose

DemaConsulting.ApiMark generates the API reference of the PartOfSpeech package from its XML doc
comments. It was chosen because its output is organized for *gradual disclosure* (an index page,
then a page per namespace, type, and member), which matches the stated audience: a coding agent
learning how to use the library, which reads a small targeted page far more effectively than one
large document. Generating the reference from the doc comments keeps it from drifting away from
the code it describes.

### Features Used

- MSBuild integration (`DemaConsulting.ApiMark.MSBuild`), so the reference is produced by the
  ordinary build rather than by a separate command
- Output redirection (`ApiMarkOutputDir`) into `src/DemaConsulting.PartOfSpeech/generated/api`
- Package inclusion (`ApiMarkPackDocs`), which places the generated `api/` tree in the NuGet package
- Documentation-coverage enforcement (`ApiMarkEnforceDocs`, `ApiMarkEnforceDocsSeverity`)
- The library description of the index page (`ApiMarkLibraryDescription`)
- Rendering of `<example>`/`<code>` blocks as fenced code blocks and of `<remarks>` prose
- Namespace description from a `NamespaceDoc` carrier class (`NamespaceDoc.cs`, an
  `internal static class NamespaceDoc` in the namespace, which is ApiMark's documented convention;
  ApiMark recognizes it by that shape and omits it from the type list and from enforcement; its XML
  summary, remarks and example become the namespace page; without it the namespace shows "No
  description provided"). The library has a single public namespace, `DemaConsulting.PartOfSpeech`
  (the folders `Tokenization`, `Tagging` and `ModelStorage` only organize the source files), so there is
  one carrier. Its documentation covers the package and target frameworks, the types and how they fit
  together, the terminology (UPOS, lexicon, weights, averaged perceptron, greedy, raw score, temperature,
  softmax, confidence, alternatives, constraint), the explanation of confidence and alternatives, the tokenization
  conventions, offsets and pre-tokenized input, the default model that throws until a later release, the loading
  cost, thread safety and the general error rules, each as a section of the namespace page. The sections are
  Markdown headings written into the remarks (ApiMark passes raw Markdown through, so the headings become
  anchors; ApiMark drops the bold and emphasis elements and renders a cross-reference to a namespace as plain
  text), and the other pages link to them with relative Markdown links, because a cross-reference cannot address a
  namespace section and the see-also element is not rendered on member pages. The carrier
  and the example-compile test are in the OTS-ApiMark review set because they are ApiMark's inputs
  and evidence. FileAssert asserts the terminology and the section headings on the namespace page, and the fenced
  example blocks, the documented thread safety and exceptions, the link to the confidence section and the
  tokenization rules on the `Tagger`, `Tagger.Tag`, `Tagger.Load`, `TaggedToken.Confidence`, `Tokenizer` and
  `TokenConstraint` pages. References to repository documents in XML docs use absolute GitHub URLs,
  because the packaged API reference has no access to repository files.

### Integration Pattern

ApiMark is referenced from the library project as a build-time-only `PackageReference` with
`PrivateAssets="All"`, so it is never a transitive dependency of consumers and is never shipped as
runtime code. The test project and the `LexiconCompiler` tool do not reference it and therefore
produce no API documentation. Versions are pinned inline in the project file; there is no central
package management. Unlike the other OTS items, ApiMark's *output* (the `api/` tree) is shipped in
the package; the tool itself is not.

The library project sets these properties:

- **`ApiMarkOutputDir`**: `$(MSBuildProjectDirectory)\generated\api`. The `generated/` folder is
  already excluded by `.gitignore`, `.markdownlint-cli2.yaml`, `.cspell.yaml` and `.reviewmark.yaml`,
  and covered by the rule that agents never read, lint, or modify generated content. The reference is
  therefore generated at build time and not committed, so no staleness check is needed.
- **`ApiMarkVisibility`**: `Public`.
- **`ApiMarkPackDocs`**: `true`, shipping the reference inside the `.nupkg` under `api/`.
- **`ApiMarkEnforceDocs`**: `Public`, matching the visibility so enforcement covers exactly the
  generated surface.
- **`ApiMarkEnforceDocsSeverity`**: `Error`, because the build treats warnings as errors and
  a warning nobody reads is no enforcement.
- **`ApiMarkLibraryDescription`**: the one-sentence description on the index page.

The targets run once per build, for the first target framework only, which is correct because the
public surface is identical across `netstandard2.0`, `net8.0`, `net9.0`, and `net10.0`. Regeneration
requires a real rebuild (`--no-incremental`), since an up-to-date incremental build skips the task.

### Documentation Enforcement

With severity `Error`, the build of the library fails with an `[Undocumented]` finding when a public
type or member lacks an XML summary, so a green build cannot coexist with an undocumented public
member. The compiler's own documentation warning (CS1591) is also an error through
`TreatWarningsAsErrors`.

### Example Correctness

Examples are written for agents, so a wrong example is worse than none. The test
`PartOfSpeech_XmlDocExamples_Compile` extracts every `<example><code>` block from the shipped XML
documentation file and compiles it with Roslyn against the real compiled API, on every target
framework of the test project. A snippet may use a variable `tagger` of type `Tagger` without
declaring it, because the examples that construct a tagger need a weights file that cannot ship
with the documentation; the examples are compiled, not run.

### Package Contents

The package `DemaConsulting.PartOfSpeech.*.nupkg` is asserted by FileAssert after `dotnet pack`
(test `ApiMark_ApiDocsPackaged`, tag `package-docs`): its zip entries `api/api.md`,
`api/DemaConsulting.PartOfSpeech.md` and `api/DemaConsulting.PartOfSpeech/Tagger.md` exist exactly
once and have the expected content.
