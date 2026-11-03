# Part Of Speech

[![GitHub forks][badge-forks]][link-forks]
[![GitHub stars][badge-stars]][link-stars]
[![GitHub contributors][badge-contributors]][link-contributors]
[![License][badge-license]][link-license]
[![Build][badge-build]][link-build]
[![Quality Gate][badge-quality]][link-quality]
[![Security][badge-security]][link-security]
[![NuGet][badge-nuget]][link-nuget]

DEMA Consulting .NET library for part-of-speech analysis of text.

## Features

This library provides:

- **Simple Library Structure**: Demo class with example methods
- **Multi-Platform Support**: Builds and runs on Windows, Linux, and macOS
- **Multi-Runtime Support**: Targets .NET Standard 2.0, .NET 8, 9, and 10
- **xUnit v3**: Modern unit testing with xUnit framework version 3
- **Comprehensive CI/CD**: GitHub Actions workflows with quality checks and builds
- **Linting Enforcement**: markdownlint, cspell, and yamllint enforced on every CI run
- **Continuous Compliance**: Compliance evidence generated automatically on every CI run, following
  the [Continuous Compliance][link-continuous-compliance] methodology
- **SonarCloud Integration**: Quality gate and security analysis on every build
- **Documentation Generation**: Automated build notes, user guide, code quality reports,
  requirements, justifications, and trace matrix
- **Requirements Traceability**: Requirements linked to passing tests with auto-generated trace matrix

## Installation

Install the library using the .NET CLI:

```bash
dotnet add package PartOfSpeech
```

## Usage

```csharp
using PartOfSpeech;

var demo = new Demo();
var result = demo.DemoMethod("World"); // result = "Hello, World!"
```

## Documentation

Generated documentation includes:

- **Build Notes**: Release information and changes
- **User Guide**: Comprehensive usage documentation
- **Code Quality Report**: CodeQL and SonarCloud analysis results
- **Requirements**: Functional and non-functional requirements
- **Requirements Justifications**: Detailed requirement rationale
- **Trace Matrix**: Requirements to test traceability

## Data

The repository holds the open training data, the evaluation data and a part-of-speech lexicon for the 17 Universal POS
tags in the [data folder][link-data]. Every data source is recorded with its origin, license and how it was produced, and
the files are verified against checksums. The library does not use the data at run time yet.

## Credits and Licenses

PartOfSpeech is MIT licensed. The data below is open, and its attribution and license notices are in
[THIRD-PARTY-NOTICES.md][link-notices], which is also packed in the NuGet package.

- **MASC**: the Manually Annotated Sub-Corpus of the Open American National Corpus
  ([CC BY 3.0 US][link-cc-by-3]). Its Penn Treebank parts of speech were converted to Universal POS tags.
- **Open English WordNet** ([CC BY 4.0][link-cc-by-4]), which carries the Princeton WordNet notice. Its word forms
  were reduced to a lexicon that maps a spelling to the Universal POS tags it may carry.
- **The open part-of-speech corpus** of the [WordSense][link-wordsense] project: the annotations and the selection are
  CC0 1.0, and the sentences are public domain in the United States or come from the Tatoeba CC0 export. The same holds
  for the silver data (Tatoeba and Project Gutenberg text) and the evaluation sets.
- **UD English-EWT** (CC BY-SA 4.0) is used for **evaluation only**. It is not distributed, nothing is trained on it,
  and the repository ignores its files.

No CC BY-SA or NonCommercial data is committed to this repository or included in the package. Language models wrote the
tags of the open corpus and of the silver data, and no human has reviewed them. In the owner's position, GitHub claims
no ownership of model outputs, so the repository owner holds the rights in them and dedicates them to the public domain
under CC0 1.0. That is the owner's position, not legal advice or a warranty.

## Contributing

Contributions are welcome. See [CONTRIBUTING.md][link-contributing] for development setup, coding
standards, and the pull request process.

## License

Copyright (c) DEMA Consulting. Licensed under the MIT License. See [LICENSE][link-license] for details.

By contributing to this project, you agree that your contributions will be licensed under the MIT License.

<!-- Badge References -->
[badge-forks]: https://img.shields.io/github/forks/demaconsulting/PartOfSpeech?style=plastic
[badge-stars]: https://img.shields.io/github/stars/demaconsulting/PartOfSpeech?style=plastic
[badge-contributors]: https://img.shields.io/github/contributors/demaconsulting/PartOfSpeech?style=plastic
[badge-license]: https://img.shields.io/github/license/demaconsulting/PartOfSpeech?style=plastic
[badge-build]: https://img.shields.io/github/actions/workflow/status/demaconsulting/PartOfSpeech/build_on_push.yaml?style=plastic
[badge-quality]: https://sonarcloud.io/api/project_badges/measure?project=demaconsulting_PartOfSpeech&metric=alert_status
[badge-security]: https://sonarcloud.io/api/project_badges/measure?project=demaconsulting_PartOfSpeech&metric=security_rating
[badge-nuget]: https://img.shields.io/nuget/v/PartOfSpeech?style=plastic

<!-- Link References -->
[link-forks]: https://github.com/demaconsulting/PartOfSpeech/network/members
[link-stars]: https://github.com/demaconsulting/PartOfSpeech/stargazers
[link-contributors]: https://github.com/demaconsulting/PartOfSpeech/graphs/contributors
[link-license]: https://github.com/demaconsulting/PartOfSpeech/blob/main/LICENSE
[link-build]: https://github.com/demaconsulting/PartOfSpeech/actions/workflows/build_on_push.yaml
[link-quality]: https://sonarcloud.io/dashboard?id=demaconsulting_PartOfSpeech
[link-security]: https://sonarcloud.io/dashboard?id=demaconsulting_PartOfSpeech
[link-nuget]: https://www.nuget.org/packages/PartOfSpeech
[link-continuous-compliance]: https://github.com/demaconsulting/ContinuousCompliance
[link-contributing]: https://github.com/demaconsulting/PartOfSpeech/blob/main/CONTRIBUTING.md
[link-data]: https://github.com/demaconsulting/PartOfSpeech/blob/main/data/README.md
[link-notices]: https://github.com/demaconsulting/PartOfSpeech/blob/main/THIRD-PARTY-NOTICES.md
[link-wordsense]: https://github.com/demaconsulting/WordSense
[link-cc-by-3]: https://creativecommons.org/licenses/by/3.0/us/
[link-cc-by-4]: https://creativecommons.org/licenses/by/4.0/
