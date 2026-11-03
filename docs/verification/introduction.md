# Introduction

This document provides the verification design for the Part Of Speech, a .NET library for part-of-speech tagging of
English text with the 17 Universal POS tags.

## Purpose

The purpose of this document is to serve as the verification design entry point and document how
requirements will be tested across all software items in the Part Of Speech system. This
documentation enables formal review by mapping every requirement to named test scenarios, supports
compliance auditing by providing clear traceability from requirements through verification design
to tests, and ensures test completeness can be assessed without reading implementation code.

This document is intended for:

- Software developers implementing and maintaining tests
- Code reviewers validating test completeness against requirements
- Compliance auditors tracing requirements through verification design to tests
- Quality assurance teams validating test coverage and scenario adequacy

## Scope

This document covers the verification design for the Part Of Speech system and its
constituent software items, specifically:

- **PartOfSpeech (System)** — The complete .NET part-of-speech tagging library
- **Upos (Unit)** — The tag vocabulary, its text forms and sets of tags
- **Tokenization (Subsystem)** — with the **Tokenizer** and **SentenceSplitter** units
- **ModelStorage (Subsystem)** — with the **BinaryFormat**, **Lexicon**, **PerceptronModel** and **EmbeddedModel** units
- **Tagging (Subsystem)** — with the **FeatureExtractor**, **TagDecoder**, **TokenConstraint** and **Tagger** units

The following OTS items are also covered:

- **ApiMark** — API reference generation tool
- **BuildMark** — build-notes documentation tool
- **FileAssert** — document assertion tool
- **Pandoc** — Markdown-to-HTML conversion tool
- **ReqStream** — requirements traceability tool
- **ReviewMark** — file review enforcement tool
- **SarifMark** — SARIF report conversion tool
- **SonarMark** — SonarCloud quality report tool
- **VersionMark** — tool-version documentation tool
- **WeasyPrint** — HTML-to-PDF conversion tool
- **xUnit** — unit-testing framework

This verification documentation covers the same software items as the design documentation.

Version applicability: This verification design applies to all versions of the Part Of Speech
library.

The following topics are explicitly excluded from this verification documentation:

- Build pipeline and CI/CD process testing
- Infrastructure and hosting environment testing

## Companion Artifact Structure

Each software item covered by this document has corresponding artifacts in parallel directory
trees. In-house items have artifacts in these parallel locations:

- Requirements: `docs/reqstream/{system}/.../{item}.yaml` (kebab-case)
- Design docs: `docs/design/{system}/.../{item}.md` (kebab-case)
- Verification design: `docs/verification/{system}/.../{item}.md` (kebab-case)
- Source code: `src/{System}/.../{Item}.cs` (PascalCase for C#)
- Tests: `test/{System}.Tests/.../{Item}Tests.cs` (PascalCase for C#); subsystem tests are named after the subsystem
  (`Tokenization/TokenizationTests.cs`) and the system tests are `PartOfSpeechTests.cs` and `PartOfSpeechDataTests.cs`
- Test infrastructure (not software items): `test/{System}.Tests/TestSupport/` builds the tiny and synthetic models that
  the tests use; the throughput, thread-safety, allocation and tiny-model methods are described in the system, subsystem
  and unit verification documents
- SysML2 model: `docs/sysml2/model/{system}/.../{item}.sysml` (kebab-case)
OTS items have parallel artifacts in:

- Requirements: `docs/reqstream/ots/{ots-name}.yaml` (kebab-case)
- Verification: `docs/verification/ots/{ots-name}.md` (kebab-case)

Review-sets: defined in `.reviewmark.yaml`

## References

- Part Of Speech User Guide — the compiled User Guide document for this repository.
- Part Of Speech Repository — the PartOfSpeech source repository hosted on
  GitHub.
