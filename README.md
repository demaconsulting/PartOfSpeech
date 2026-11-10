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

![Architecture: an application calls the Tagger, which uses tokenization, the model and Upos][architecture]

## Features

- 🔤 **Tokenizer** - English text to sentences and tokens with offsets
- 🏷️ **Tagger** - 17 Universal POS tags from an averaged perceptron
- 📊 **Confidences** - calibrated probabilities and ranked alternatives for every tag
- 🔒 **Constraints** - restrict or steer the tag of single tokens
- 📚 **Glossaries** - tag domain terms correctly, in hard or soft mode
- 🧩 **Pre-tokenized input** - tag tokens that you already have
- 📦 **No dependencies** - lexicon and weights are embedded, nothing to download
- ⚡ **Fast** - hundreds of thousands of tokens per second, thread-safe
- 🎯 **Wide support** - .NET Standard 2.0 and .NET 8 to 10
- 📄 **API reference** - generated Markdown reference packed with the library
- ✅ [**Continuous Compliance**][link-continuous-compliance] - evidence generated on every CI run

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

`Tagger.Default` is a shared, thread-safe tagger that uses the model and the lexicon that are embedded in the package,
so it needs no file and no download. The first read inflates and validates the embedded files once. To use other weights,
create a tagger with `Tagger.Load(stream)`. The [user guide][link-user-guide] explains confidences and alternatives,
constraints, glossaries and the tokenization conventions.

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

The default is hard mode: the entries are trusted completely, so a wrong entry forces a wrong tag. Use hard mode for
curated lists and soft mode for lists that may contain errors.

## Accuracy

Held-out results of the embedded model. A harmful error is a noun tagged as a verb or a verb tagged as a noun; the rate
is per 1,000 noun-verb decisions. `shape-probe` and `challenge-set` are small sets of hard sentences.

| Set | Accuracy (%) | Harmful per 1,000 |
| --- | ---: | ---: |
| `open-test` | 96.75 | 18.4 |
| `gold-v2-test` | 94.87 | 26.7 |
| `shape-probe` | 93.21 | 48.6 |
| `challenge-set` | 89.34 | 0.0 |
| `ud-test-mapped` | 93.03 | 18.6 |

Recommended confidence thresholds, with the share of the noun-verb decisions that they answer and the harmful errors per
1,000 answered decisions on the open test split:

| Threshold | Value | Coverage (%) | Harmful per 1,000 answered |
| --- | ---: | ---: | ---: |
| `strict` | 0.95 | 83.7 | 2.4 |
| `balanced` | 0.75 | 94.7 | 7.5 |

Rule checkers should use `strict`; treat tokens below the threshold as unknown.

The [evaluation report][link-evaluation] has the details.

## Documentation

Generated documentation includes build notes, the user guide, the code quality report, requirements with
justifications and the requirements trace matrix.

## Data

The repository holds the open training data, the evaluation data and a part-of-speech lexicon for the 17 Universal POS
tags in the [data folder][link-data], each source with its origin, license and recipe, verified against checksums. At run
time the library uses only the lexicon and the trained weights, both embedded in the assembly. The trainer in
`tools/DemaConsulting.PartOfSpeech.Trainer` reproduces the weights from the committed data. The corpora and the
evaluation sets are not shipped.

## Credits and Licenses

PartOfSpeech is MIT licensed. The package also embeds a lexicon and trained weights that are derived from third-party
data, so its license is the file [PACKAGE-LICENSE.md][link-package-license] (it starts with the SPDX expression
`MIT AND CC-BY-4.0 AND CC-BY-3.0-US`; nuget.org accepts only expressions of OSI or FSF approved licenses, and CC BY 3.0
US is neither). The attribution notices are in [THIRD-PARTY-NOTICES.md][link-notices]; both files are packed in the
NuGet package.

- **MASC**: the Manually Annotated Sub-Corpus of the Open American National Corpus
  ([CC BY 3.0 US][link-cc-by-3]), converted to Universal POS tags; the lexicon and the trained weights were derived in
  part from the converted data.
- **Open English WordNet** ([CC BY 4.0][link-cc-by-4]), which carries the Princeton WordNet notice: its word forms were
  reduced to the lexicon.
- **The open part-of-speech corpus** of the [WordSense][link-wordsense] project (a related project with taggers of its
  own): annotations and selection are CC0 1.0, and the sentences are public domain in the United States or come from the
  Tatoeba CC0 export. The silver data (Tatoeba and Project Gutenberg text), the imperative, compound and headings training
  sources (text of US federal government documents; 17 U.S.C. 105) and the evaluation sets follow the same terms.
- **UD English-EWT** (CC BY-SA 4.0) is used for **evaluation only**: it is not distributed, nothing is trained on it,
  and the repository ignores its files.

No CC BY-SA or NonCommercial data is committed to this repository or included in the package. Language models wrote the
tags of the open corpus, of the silver data and of the imperative, compound and headings sources, and no human has
reviewed them. In the owner's
position, GitHub claims no ownership of model outputs, so the repository owner holds the rights in them and dedicates
them to the public domain under CC0 1.0. That is the owner's position, not legal advice or a warranty.

## Contributing

Contributions are welcome. See [CONTRIBUTING.md][link-contributing] for setup, coding standards and the pull request
process.

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
[link-evaluation]: https://github.com/demaconsulting/PartOfSpeech/blob/main/docs/evaluation/README.md
[link-notices]: https://github.com/demaconsulting/PartOfSpeech/blob/main/THIRD-PARTY-NOTICES.md
[link-package-license]: https://github.com/demaconsulting/PartOfSpeech/blob/main/PACKAGE-LICENSE.md
[link-wordsense]: https://github.com/demaconsulting/WordSense
[link-cc-by-3]: https://creativecommons.org/licenses/by/3.0/us/
[link-cc-by-4]: https://creativecommons.org/licenses/by/4.0/
[architecture]: https://raw.githubusercontent.com/demaconsulting/PartOfSpeech/main/docs/images/architecture.png
