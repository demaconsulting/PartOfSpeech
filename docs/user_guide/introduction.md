# Introduction

## Purpose

This document is the user guide for the Part Of Speech, a .NET library that splits English text into sentences and
tokens and tags every token with one of the 17 Universal POS tags, with a confidence, ranked alternatives and optional
per-token constraints.

## Scope

This user guide covers:

- Installation of the library
- Tagging text and pre-tokenized sentences, results and offsets
- Reading confidences and alternatives
- Constraining the tag of a token
- Loading a weights file, and the default model
- The tokenization conventions of the library
- API reference
- Data and license credits

# Continuous Compliance

This library follows the
[Continuous Compliance](https://github.com/demaconsulting/ContinuousCompliance) methodology, which ensures
compliance evidence is generated automatically on every CI run.

## Key Practices

- **Requirements Traceability**: Every requirement is linked to passing tests, and a trace matrix is
  auto-generated on each release
- **Linting Enforcement**: markdownlint, cspell, and yamllint are enforced before any build proceeds
- **Automated Audit Documentation**: Each release ships with generated requirements, justifications,
  trace matrix, and quality reports
- **CodeQL and SonarCloud**: Security and quality analysis runs on every build

# Installation

Install the library using the .NET CLI:

```bash
dotnet add package DemaConsulting.PartOfSpeech
```

The package targets .NET Standard 2.0 (and so runs on .NET Framework 4.8.1 and every .NET version that implements it),
.NET 8, .NET 9 and .NET 10. It has no package dependencies. The package also contains a Markdown API reference of the
public API under its `api/` folder, generated from the XML documentation of the library.

**The trained model is part of the package.** The package contains the tokenizer, the tagger, the file formats, the
compiled word lexicon (641,418 bytes compressed) and the trained averaged-perceptron weights (492,971 bytes compressed,
1,111,898 bytes uncompressed, about 0.5 MB and 1.1 MB). `Tagger.Default` returns a shared tagger that uses them, so the
samples below need no file and no download. You can still create a tagger from your own weights file (see _Loading a
Weights File_).

The model was selected on development data only, and this release does not recommend a confidence threshold: the
accuracy of the model and the guidance on what confidence is low enough to check a token again will be added after
the model has been evaluated on data that was not used to select it.

# Usage

## Tagging Text

```csharp
using DemaConsulting.PartOfSpeech;

var tagger = Tagger.Default;

foreach (TaggedSentence sentence in tagger.Tag("The cat sat on the mat. It didn't move."))
{
    foreach (TaggedToken token in sentence.Tokens)
    {
        Console.WriteLine($"{token.Start,3} {token.Text,-6} {token.Tag.ToTagString(),-6} {token.Confidence:0.00}");
    }
}
```

`Tag(string)` tokenizes the text, splits it into sentences and tags every token. `Tagger.Default` is created on first
use and is shared by every caller; you can also create your own tagger and share it: it is immutable and safe for
concurrent use (see _Thread Safety_).

## Offsets

Every `TaggedSentence` and `TaggedToken` carries `Start` and `Length` offsets into the string that you passed in, and a
token's `Text` is exactly the characters at that position (tokens are never normalized):

```csharp
const string text = "Dr. Smith doesn't smoke.";
TaggedSentence sentence = tagger.Tag(text)[0];
TaggedToken token = sentence.Tokens[3];   // "n't"
Console.WriteLine(text.Substring(token.Start, token.Length) == token.Text);   // True
```

The offsets of a sentence that comes from a text index the whole text, not the sentence. A sentence runs from its first
to its last token character.

## Pre-Tokenized Input

If you already have tokens, pass them to the tagger. They should follow the conventions of the library (see
_Tokenization Conventions_), because the model was trained on them:

```csharp
TaggedSentence sentence = tagger.Tag(new[] { "She", "does", "n't", "know", "." });
Console.WriteLine(string.Join(" ", sentence.Tokens));   // each token as text/TAG, for example She/PRON
```

The sentence text is the tokens joined by single spaces and the offsets index that text. Tokens must not be `null`,
empty or contain white space. You can also run the tokenizer alone and tag the result later:

```csharp
IReadOnlyList<Sentence> sentences = Tokenizer.Tokenize("Hello, world! How are you?");
TaggedSentence first = tagger.Tag(sentences[0]);
```

## Reading Confidences and Alternatives

Every token has a `Tag`, a `Confidence` and a list of `Alternatives`:

```csharp
var tagger = Tagger.Load(weights, new TaggerOptions { MaxAlternatives = 3 });
foreach (TaggedToken token in tagger.Tag("I saw her duck.")[0].Tokens)
{
    Console.WriteLine($"{token.Text}: {token.Tag} ({token.Confidence:P0})");
    foreach (TagScore alternative in token.Alternatives)
    {
        Console.WriteLine($"    or {alternative.Tag} ({alternative.Probability:P0})");
    }
}
```

`Confidence` is the probability of the chosen tag among the tags that the tagger considered for that word, scaled by the
temperature that was fitted to the model so that it can be read as a probability. `Alternatives` lists the other
considered tags in descending order of probability (at most `MaxAlternatives`, default 3; 0 to 16 are allowed); the
confidence and the alternatives together add up to at most 1 (exactly 1 when all considered tags are listed).

Read the numbers with these points in mind:

- **One possible tag gives confidence 1.** A word that the lexicon knows with a single tag (and no shape rule widens the
  choice) can only receive that tag, so its confidence is 1 whatever the model thinks, and it has no alternatives. That
  is correct by construction, but it says nothing about the model. To judge the model, look at tokens with several
  allowed tags, for example both NOUN and VERB.
- **Known words are restricted, unknown words are not.** A word that is in the lexicon receives only a tag that the
  lexicon and the token-shape rules allow (a capitalized word can also be PROPN or X, a token with a digit can also be NUM,
  SYM, X, NOUN, PROPN or ADJ, a token without a letter or digit can also be PUNCT, SYM or X). A word that is not in the
  lexicon can receive any of the 17 tags, so its confidence is spread over all of them.
- **The lexicon is not a gold standard.** It records which tags a word is attested with, including some annotation noise
  (for example `the` also appears as PROPN), so a tag in the allowed set is not necessarily correct.
- **A low confidence is a reason to look.** Use a threshold that suits your use, and look at the alternatives to see what
  the competing readings are.

## Constraints

A `TokenConstraint` lets you tell the tagger something that it cannot know, for example from a glossary. Pass one
constraint per token (use `TokenConstraint.None` for the others):

```csharp
Sentence sentence = Tokenizer.Tokenize("Please book a table.")[0];

var constraints = sentence.Tokens
    .Select(token => token.Text == "book" ? TokenConstraint.Only(Upos.Verb, Upos.Noun) : TokenConstraint.None)
    .ToArray();

TaggedSentence tagged = tagger.Tag(sentence, constraints);
Console.WriteLine(tagged.Tokens[1].IsConstrained);   // True
```

- `TokenConstraint.Only(...)` is a hard constraint: the word may take only these tags (intersected with what the lexicon
  allows; if nothing is left, your constraint wins, because a glossary is more authoritative than a dictionary).
- `TokenConstraint.Prefer(tag, boost)` and `TokenConstraint.Penalize(tag, penalty)` are soft constraints: they add to or
  subtract from the raw score of one tag, so the model can still disagree when it is sure. The amounts are in raw score
  units (they are divided by the temperature of the model like the model's own scores) and must be finite and greater
  than zero.
- `And` combines two constraints: it intersects the allowed sets and adds the adjustments, and it throws an
  `ArgumentException` when the two allowed sets have no tag in common.
- The tag that is chosen becomes the history of the following tokens, so a constraint also influences its neighbors.
- `TaggedToken.IsConstrained` is true for every token whose constraint was not `None`. A constraint that allows a single
  tag gives confidence 1.
- The list must have exactly one constraint per token, otherwise `ArgumentException` is thrown. `default(TokenConstraint)`
  is `None`.

## Loading a Weights File

```csharp
using var weights = File.OpenRead("model.posw");
Tagger tagger = Tagger.Load(weights);                           // embedded lexicon
using var lexicon = File.OpenRead("lexicon.posl");
Tagger custom = Tagger.Load(File.OpenRead("model.posw"), lexicon);   // your own lexicon file
```

The stream is read to its end and is not closed. `Load` throws `InvalidDataException` for a file that is damaged, is not
a weights file, or is of a format or feature set version that this build does not read; `ArgumentNullException` for a
`null` stream and `ArgumentOutOfRangeException` for options outside their range.

## The Default Tagger

```csharp
Tagger tagger = Tagger.Default;
TaggedSentence sentence = tagger.Tag("Dogs bark.")[0];
```

`Tagger.Default` is the shared tagger that uses the weights and the lexicon that are embedded in the package. It is
created on first use, so a program that only tokenizes pays nothing; the first read inflates and validates the two
embedded files once (the weights: 492,971 bytes compressed, 1,111,898 bytes uncompressed; the lexicon: 641,418 bytes
compressed, 1,635,344 bytes uncompressed; tens of milliseconds on a current machine), and every later read returns the
same thread-safe instance. A read does not fail in a released package; an `InvalidOperationException` or an
`InvalidDataException` would mean that the package is broken.

## Thread Safety

`Tagger`, `Tokenizer`, `TokenConstraint` and all result types are immutable and safe for use from any number of threads
at the same time. A tagger uses per-thread scratch memory internally, so sharing one tagger between threads is cheap and
recommended. Tagging more than 50,000 tokens per second on one thread is a tested requirement of the library; typical
speed on a modern machine is several hundred thousand tokens per second.

# Tokenization Conventions

The tagger was trained on text that is tokenized in one convention, so `Tokenizer` reproduces it exactly: on the 14,980
sentences of the open training corpus it gives the gold tokens for more than 99.9 percent of them. The convention is
specified in `data/CONVENTIONS.md` of the repository; in short:

| Text | Tokens |
| --- | --- |
| `Hello, world!` | `Hello` `,` `world` `!` |
| `"Hi" (and $5, 50%)` | `"` `Hi` `"` `(` `and` `$` `5` `,` `50` `%` `)` |
| `don't`, `can't`, `cannot`, `CAN'T` | `do` `n't`; `ca` `n't`; `can` `not`; `CA` `N'T` |
| `John's`, `I've`, `we're` | `John` `'s`; `I` `'ve`; `we` `'re` |
| `o'clock`, `gonna`, `D'Artagnan` | stay whole |
| `well-known`, `and/or`, `R&D`, `1,000`, `7:00` | stay whole |
| `forest--at` | `forest` `--` `at` |
| `Mr.`, `U.S.`, `p.m.`, `J. D.` | keep their period |
| `He left.` | `He` `left` `.` (the period of a sentence end is a separate token, also after `I`) |
| `Wait...` and `as...as` | `Wait` `...`; `as...as` stays whole |
| `https://example.com/a?b=1.` | one token; the final period is separate |
| typographic quotes, dashes, `…` | separate tokens that keep their original characters |

Sentence boundaries follow terminal punctuation (`.`, `!`, `?`, `...`) before a capitalized word, a digit or an opening
quote; abbreviations, initials and an ellipsis before a lower-case word do not end a sentence; closing quotes stay with
their sentence; a blank line always ends a sentence and a single line break does not. Where the library tags, it folds
typographic quotes, dashes and the ellipsis to their ASCII forms and normalizes the text (NFC) for its features, but your
tokens and offsets are never changed.

# API Reference

All public types are in the namespace `DemaConsulting.PartOfSpeech`. The package also contains a generated Markdown
reference with every member under `api/`.

## Tagger

The entry point. Immutable and thread-safe.

- `static Tagger Default`: the shared tagger that uses the embedded model and lexicon; created on first use.
- `static Tagger Load(Stream weights, TaggerOptions? options = null)`: creates a tagger from a weights stream with the
  embedded lexicon.
- `static Tagger Load(Stream weights, Stream lexicon, TaggerOptions? options = null)`: with your own lexicon file.
- `TaggerOptions Options`: a copy of the options the tagger was created with.
- `IReadOnlyList<TaggedSentence> Tag(string text)`: tokenizes, splits into sentences and tags.
- `TaggedSentence Tag(Sentence sentence, IReadOnlyList<TokenConstraint>? constraints = null)`: tags a sentence from
  `Tokenizer`.
- `TaggedSentence Tag(IReadOnlyList<string> tokens, IReadOnlyList<TokenConstraint>? constraints = null)`: tags tokens
  that you provide.

## TaggerOptions

- `int MaxAlternatives { get; init; }`: the most alternatives per token; 0 to 16, default 3.

## TaggedSentence

- `string Text`, `int Start`, `int Length`: the sentence and its place in the source string.
- `IReadOnlyList<TaggedToken> Tokens`: the tagged tokens in order.

## TaggedToken

- `string Text`, `int Start`, `int Length`: the token and its place in the source string.
- `Upos Tag`: the chosen tag.
- `double Confidence`: the probability of the tag among the considered tags.
- `IReadOnlyList<TagScore> Alternatives`: the other considered tags by descending probability; read-only.
- `bool IsConstrained`: true when a `TokenConstraint` other than `None` applied to the token.

## TagScore

- `Upos Tag`, `double Probability`.

## TokenConstraint

- `static TokenConstraint None`, `bool IsNone`, `UposSet Allowed`.
- `static TokenConstraint Only(UposSet)` and `Only(params Upos[])`: allow only these tags; at least one is required.
- `static TokenConstraint Prefer(Upos tag, double boost)` and `Penalize(Upos tag, double penalty)`: finite and greater
  than zero.
- `TokenConstraint And(TokenConstraint other)`: intersects the allowed sets, adds the adjustments; throws when the sets
  are disjoint.
- Equality by content (`Equals`, `==`, `!=`, `GetHashCode`) and `ToString`.

## Tokenizer

- `static IReadOnlyList<Sentence> Tokenize(string text)`: sentences with tokens; an empty or white-space text gives none;
  `null` throws `ArgumentNullException`.
- `static Sentence CreateSentence(IReadOnlyList<string> tokens)`: a sentence from tokens that you provide.

## Sentence and Token

- `Sentence`: `string Text`, `int Start`, `int Length`, `IReadOnlyList<Token> Tokens`. The offsets of its tokens index
  the whole source string.
- `Token`: `string Text`, `int Start`, `int Length` and a constructor `Token(string text, int start)`; equality by
  text and start.

## Upos

An enumeration of the 17 Universal POS tags in alphabetical order: `Adj`, `Adp`, `Adv`, `Aux`, `Cconj`, `Det`, `Intj`,
`Noun`, `Num`, `Part`, `Pron`, `Propn`, `Punct`, `Sconj`, `Sym`, `Verb`, `X`.

## UposExtensions

- `const int Count`: 17.
- `static string ToTagString(this Upos tag)`: the Universal POS name, for example `NOUN` or `CCONJ`; throws for a value
  that is not a tag.
- `static bool TryParse(string? text, out Upos tag)`: parses an exact upper-case name.

## UposSet

An immutable set of tags: `Empty`, `All`, `Of(params Upos[])`, `Count`, `IsEmpty`, `Contains`, `Add`, `Remove`, `Union`,
`Intersect`, equality by content, enumeration in ascending tag order and `ToString` (for example `NOUN|VERB`).

# Data and Licenses

The code is MIT licensed. The package also embeds a lexicon and trained weights that are derived from third-party data,
so its license is the file
[PACKAGE-LICENSE.md](https://github.com/demaconsulting/PartOfSpeech/blob/main/PACKAGE-LICENSE.md), which is packed at the
root of the NuGet package. It starts with the SPDX expression `MIT AND CC-BY-4.0 AND CC-BY-3.0-US`, states which license
covers which part, and is used instead of a license expression because nuget.org accepts only expressions of licenses
that the Open Source Initiative or the Free Software Foundation approve, which CC BY 3.0 US is not. The repository also
holds open data for the 17 Universal POS tags: training corpora, a part-of-speech lexicon and evaluation sets, described
in the [data folder](https://github.com/demaconsulting/PartOfSpeech/blob/main/data/README.md). The parts of the data that
the library uses at run time are the lexicon, which is compiled into a binary file that is embedded in the assembly and
restricts the tags of known words, and the weights, which the trainer tool of the repository learned from the open
corpus, MASC and the silver corpus and which are embedded as a binary file too. The attribution and license notices of
the third-party data are in
[THIRD-PARTY-NOTICES.md](https://github.com/demaconsulting/PartOfSpeech/blob/main/THIRD-PARTY-NOTICES.md), which is also
packed at the root of the NuGet package.

## Credits

- **MASC**, the Manually Annotated Sub-Corpus of the Open American National Corpus, is licensed CC BY 3.0 US
  (<https://creativecommons.org/licenses/by/3.0/us/>). Its Penn Treebank parts of speech were converted to Universal POS
  tags. The lexicon uses its tags, and the trained weights were learned in part from the converted data and are provided
  under the terms of this license.
- **Open English WordNet** is licensed CC BY 4.0 (<https://creativecommons.org/licenses/by/4.0/>) and carries the
  Princeton WordNet notice. Its word forms were reduced to a lexicon of spellings and the Universal POS tags they may
  carry; the lexicon is embedded in the package in a compiled form.
- **The open part-of-speech corpus** of the WordSense project (<https://github.com/demaconsulting/WordSense>): the
  annotations and the selection are CC0 1.0, and the sentences are public domain in the United States or come from the
  Tatoeba CC0 export. The silver data (Tatoeba and Project Gutenberg text) and the evaluation sets follow the same terms.

## Evaluation-Only Data

The UD English-EWT treebank (CC BY-SA 4.0) is used for evaluation only. It is not distributed, nothing is trained on it,
and the repository ignores its files. No CC BY-SA or NonCommercial data is committed to the repository or included in the
package.

# References

- [REF-1] Continuous Compliance Methodology (<https://github.com/demaconsulting/ContinuousCompliance>)
- [REF-2] Universal POS tags (<https://universaldependencies.org/u/pos/>)
