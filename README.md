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

- **Tokenizer and sentence splitter**: English text becomes sentences and tokens, with character offsets into your
  original string
- **Tagger**: an averaged-perceptron tagger over the 17 Universal POS tags, with a word lexicon that restricts known
  words
- **Confidences and alternatives**: every tag comes with a calibrated probability and the ranked competing tags
- **Constraints and glossaries**: restrict or steer single tokens, or give the tagger a glossary of domain terms
- **Pre-tokenized input**: tag tokens that you already have
- **No dependencies, no downloads**: the lexicon and the trained weights are embedded in the package. It runs on
  .NET Standard 2.0, .NET 8, 9 and 10, tags hundreds of thousands of tokens per second on one thread and is thread-safe
- **API reference in the package**: a generated Markdown reference is packed with the library
- **Continuous Compliance**: evidence is generated on every CI run, following the
  [Continuous Compliance][link-continuous-compliance] methodology

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
so it needs no file and no download. The first read inflates and validates the embedded files once (the weights are
477,899 bytes compressed and 1,077,990 bytes uncompressed, the lexicon 639,617 bytes compressed). To use other weights,
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

The default is hard mode: the entries are trusted completely, so a wrong entry forces a wrong tag. That fits a curated
list; use soft mode for a list that may be wrong. The measured cost is in section E9 of the
[trainer run log][link-run-log].

## Accuracy and Confidence

The model was selected on development data only, its confidence thresholds too, and it was then evaluated once on
held-out data (after the corpora had been corrected and the model retrained; the [trainer run log][link-run-log] has the
decisions and the gates). The tagger is accurate on ordinary running text. Its most costly mistake is a harmful error: a
noun tagged as a verb, or a verb tagged as a noun. It makes some on every test set and noticeably more on short,
technical and imperative sentences built to be hard, a limitation that is reported and was not tuned away. The labels of
the test sets were written by language models, and the results come from one run.

See the evaluation report for accuracy, harmful-error rates and recommended thresholds: the [test results][link-test-results]
have every held-out set and the [confidence thresholds][link-thresholds] have the coverage and harmful-rate tables. The
[evaluation guide][link-evaluation] explains how to read them.

**Consumers that need a low harmful rate must use a confidence threshold.** Treat a token below the threshold as
unknown. A rule checker should use the `strict` threshold; `balanced` suits consumers that can tolerate more errors or
show the result to a person. The thresholds were chosen on development data and measured on the held-out open test
split:

- `strict` (0.90) answers 86.5% of the noun-verb decisions on the development data with 4.8 harmful errors in 1,000
  answered, and 87.7% with 5.7 in 1,000 on the open test split.
- `balanced` (0.85) answers 89.8% with 7.7 in 1,000 on the development data, and 90.9% with 6.5 in 1,000 on the open
  test split.

The `strict` target (at most 5 harmful errors in 1,000 answered) was met on the development data and is slightly
exceeded on the held-out open test split, where its 95% interval includes 5. A token with a single allowed tag has
confidence 1.0 without being a decision. The [user guide][link-user-guide] and
the packed API reference have the details.

## Documentation

Generated documentation includes build notes, the user guide, the code quality report, requirements with
justifications and the requirements trace matrix. The [evaluation report][link-evaluation] is plain Markdown in the
repository.

## Data

The repository holds the open training data, the evaluation data and a part-of-speech lexicon for the 17 Universal POS
tags in the [data folder][link-data], each source with its origin, license and recipe, verified against checksums. At run
time the library uses only the lexicon and the trained weights, both embedded in the assembly.
`tools/DemaConsulting.PartOfSpeech.Trainer` trains the weights from the open corpus, MASC and the silver corpus, and
`train --check` verifies that the embedded weights are exactly what the committed data produces. The corpora and the
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
  Tatoeba CC0 export. The silver data (Tatoeba and Project Gutenberg text) and the evaluation sets follow the same terms.
- **UD English-EWT** (CC BY-SA 4.0) is used for **evaluation only**: it is not distributed, nothing is trained on it,
  and the repository ignores its files.

No CC BY-SA or NonCommercial data is committed to this repository or included in the package. Language models wrote the
tags of the open corpus and of the silver data, and no human has reviewed them. In the owner's position, GitHub claims
no ownership of model outputs, so the repository owner holds the rights in them and dedicates them to the public domain
under CC0 1.0. That is the owner's position, not legal advice or a warranty.

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
[link-run-log]: https://github.com/demaconsulting/PartOfSpeech/blob/main/tools/DemaConsulting.PartOfSpeech.Trainer/RUN-LOG.md
[link-evaluation]: https://github.com/demaconsulting/PartOfSpeech/blob/main/docs/evaluation/README.md
[link-test-results]: https://github.com/demaconsulting/PartOfSpeech/blob/main/docs/evaluation/test-results.md
[link-thresholds]: https://github.com/demaconsulting/PartOfSpeech/blob/main/docs/evaluation/confidence-thresholds.md
[link-notices]: https://github.com/demaconsulting/PartOfSpeech/blob/main/THIRD-PARTY-NOTICES.md
[link-package-license]: https://github.com/demaconsulting/PartOfSpeech/blob/main/PACKAGE-LICENSE.md
[link-wordsense]: https://github.com/demaconsulting/WordSense
[link-cc-by-3]: https://creativecommons.org/licenses/by/3.0/us/
[link-cc-by-4]: https://creativecommons.org/licenses/by/4.0/
