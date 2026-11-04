# Part Of Speech

[![GitHub forks][badge-forks]][link-forks]
[![GitHub stars][badge-stars]][link-stars]
[![GitHub contributors][badge-contributors]][link-contributors]
[![License][badge-license]][link-license]
[![Build][badge-build]][link-build]
[![Quality Gate][badge-quality]][link-quality]
[![Security][badge-security]][link-security]
[![NuGet][badge-nuget]][link-nuget]

.NET library for part-of-speech tagging of English text, with calibrated confidences.

## Features

This library provides:

- **Tokenizer and Sentence Splitter**: English text becomes sentences and tokens in the convention of the training data,
  with character offsets into your original string
- **Part-of-Speech Tagger**: an averaged-perceptron tagger over the 17 Universal POS tags (ADJ, ADP, ADV, AUX, CCONJ,
  DET, INTJ, NOUN, NUM, PART, PRON, PROPN, PUNCT, SCONJ, SYM, VERB, X)
- **Lexicon-Restricted Decoding**: a known word can only receive tags that a compiled word lexicon allows; an unknown
  word may take any tag
- **Confidences and Alternatives**: every tag comes with a calibrated probability and the ranked competing tags
- **Constraints**: restrict or steer the tag of individual tokens, for example from a glossary
- **Glossary**: a list of domain terms with their allowed tags (single words and multiword terms such as `bleed valve`,
  with inflected forms, letter case rules, a hard or a soft mode and the matches reported in the results), so that
  technical terms are tagged correctly wherever they occur
- **Pre-Tokenized Input**: tag tokens that you already have
- **No Dependencies, No Downloads**: the library uses only the base class library and embeds its lexicon and its trained
  weights (about 1.1 MB, about 0.5 MB compressed); it is fast (hundreds of thousands of tokens per second on one thread)
  and thread-safe
- **Multi-Platform Support**: Builds and runs on Windows, Linux, and macOS
- **Multi-Runtime Support**: Targets .NET Standard 2.0, .NET 8, 9, and 10
- **API Reference in the Package**: a generated Markdown reference of the public API is packed with the library
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
dotnet add package DemaConsulting.PartOfSpeech
```

## Usage

```csharp
using DemaConsulting.PartOfSpeech;

foreach (TaggedSentence sentence in Tagger.Default.Tag("The cat sat on the mat. It didn't move."))
{
    foreach (TaggedToken token in sentence.Tokens)
    {
        Console.WriteLine($"{token.Text} {token.Tag.ToTagString()} {token.Confidence:0.00}");
    }
}
```

`Tagger.Default` is a shared, thread-safe tagger that uses the trained model and the lexicon that are embedded in the
package, so it needs no file and no download. The first read inflates and validates the embedded files once (the weights
are 492,971 bytes compressed and 1,111,898 bytes uncompressed, the lexicon 641,418 bytes compressed). To use other
weights, create a tagger with `Tagger.Load(stream)`. The model was selected on development data only; its accuracy is
measured separately, and this release recommends no confidence threshold until that measurement is published. The
[user guide][link-user-guide] explains how to read confidences and alternatives, how to constrain tags, how to use a
glossary and the tokenization conventions.

To tag domain terms correctly, give the tagger a glossary. The general model reads `bleed` in `bleed valve` as a verb;
the glossary says that the term is a noun, also in the plural and at the start of a sentence:

```csharp
Glossary glossary = new GlossaryBuilder
{
    { "bleed valve", Upos.Noun, "A valve that releases trapped air." },
    { "torque wrench", Upos.Noun },
}.Build();

TaggedSentence sentence = Tagger.Default.Tag("Bleed valves need a torque wrench.", glossary)[0];
foreach (GlossaryMatch match in sentence.GlossaryMatches)
{
    Console.WriteLine($"{match.Text} -> {sentence.Tokens[match.HeadToken].Tag}");   // Bleed valves -> Noun
}
```

The default is hard mode: the entries are trusted completely, so a wrong entry forces a wrong tag. That fits a curated
list such as the ASD-STE100 technical names and verbs; use soft mode for a list that is not curated or may be wrong.
The measured cost is in section E9 of the [trainer run log][link-run-log].

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
the files are verified against checksums. At run time the library uses only the lexicon and the trained weights:
`tools/LexiconCompiler` compiles the lexicon into a binary file, and `tools/DemaConsulting.PartOfSpeech.Trainer` trains
the weights from the open corpus, MASC and the silver corpus (the tool, its settings and its run log are in the
repository, and `train --check` verifies that the embedded weights are exactly what the committed data produces). Both
binary files are embedded in the assembly. The corpora and the evaluation sets are not shipped.

## Credits and Licenses

PartOfSpeech is MIT licensed. The package also embeds a lexicon and trained weights that are derived from third-party
data, so the license of the package is the file [PACKAGE-LICENSE.md][link-package-license] (it starts with the SPDX
expression `MIT AND CC-BY-4.0 AND CC-BY-3.0-US`; nuget.org accepts only expressions of OSI or FSF approved licenses, and
CC BY 3.0 US is neither). The attribution and license notices of the third-party data are in
[THIRD-PARTY-NOTICES.md][link-notices]. Both files are packed at the root of the NuGet package.

- **MASC**: the Manually Annotated Sub-Corpus of the Open American National Corpus
  ([CC BY 3.0 US][link-cc-by-3]). Its Penn Treebank parts of speech were converted to Universal POS tags; the lexicon
  and the trained weights were derived in part from the converted data.
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

Copyright (c) DEMA Consulting. The code is licensed under the MIT License. See [LICENSE][link-license] for details; the
license of the NuGet package, which also covers the embedded data, is [PACKAGE-LICENSE.md][link-package-license].

By contributing to this project, you agree that your contributions will be licensed under the MIT License.

<!-- Badge References -->
[badge-forks]: https://img.shields.io/github/forks/demaconsulting/PartOfSpeech?style=plastic
[badge-stars]: https://img.shields.io/github/stars/demaconsulting/PartOfSpeech?style=plastic
[badge-contributors]: https://img.shields.io/github/contributors/demaconsulting/PartOfSpeech?style=plastic
[badge-license]: https://img.shields.io/github/license/demaconsulting/PartOfSpeech?style=plastic
[badge-build]: https://img.shields.io/github/actions/workflow/status/demaconsulting/PartOfSpeech/build_on_push.yaml?style=plastic
[badge-quality]: https://sonarcloud.io/api/project_badges/measure?project=demaconsulting_PartOfSpeech&metric=alert_status
[badge-security]: https://sonarcloud.io/api/project_badges/measure?project=demaconsulting_PartOfSpeech&metric=security_rating
[badge-nuget]: https://img.shields.io/nuget/v/DemaConsulting.PartOfSpeech?style=plastic

<!-- Link References -->
[link-forks]: https://github.com/demaconsulting/PartOfSpeech/network/members
[link-stars]: https://github.com/demaconsulting/PartOfSpeech/stargazers
[link-contributors]: https://github.com/demaconsulting/PartOfSpeech/graphs/contributors
[link-license]: https://github.com/demaconsulting/PartOfSpeech/blob/main/LICENSE
[link-build]: https://github.com/demaconsulting/PartOfSpeech/actions/workflows/build_on_push.yaml
[link-quality]: https://sonarcloud.io/dashboard?id=demaconsulting_PartOfSpeech
[link-security]: https://sonarcloud.io/dashboard?id=demaconsulting_PartOfSpeech
[link-nuget]: https://www.nuget.org/packages/DemaConsulting.PartOfSpeech
[link-continuous-compliance]: https://github.com/demaconsulting/ContinuousCompliance
[link-contributing]: https://github.com/demaconsulting/PartOfSpeech/blob/main/CONTRIBUTING.md
[link-data]: https://github.com/demaconsulting/PartOfSpeech/blob/main/data/README.md
[link-user-guide]: https://github.com/demaconsulting/PartOfSpeech/blob/main/docs/user_guide/introduction.md
[link-run-log]: https://github.com/demaconsulting/PartOfSpeech/blob/main/tools/DemaConsulting.PartOfSpeech.Trainer/RUN-LOG.md
[link-notices]: https://github.com/demaconsulting/PartOfSpeech/blob/main/THIRD-PARTY-NOTICES.md
[link-package-license]: https://github.com/demaconsulting/PartOfSpeech/blob/main/PACKAGE-LICENSE.md
[link-wordsense]: https://github.com/demaconsulting/WordSense
[link-cc-by-3]: https://creativecommons.org/licenses/by/3.0/us/
[link-cc-by-4]: https://creativecommons.org/licenses/by/4.0/
