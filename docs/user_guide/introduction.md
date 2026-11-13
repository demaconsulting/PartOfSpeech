# Introduction

## Purpose

This document is the user guide for the Part Of Speech, a .NET library that splits English text into sentences and
tokens and tags every token with one of the 17 Universal POS tags, with a confidence, ranked alternatives and optional
per-token constraints and glossaries of domain terms.

The figure below shows how the pieces fit together: your application calls the `Tagger`, which splits the text with the
tokenization subsystem and tags it with the embedded model and the lexicon, and returns immutable results with a
confidence and ranked alternatives for every token. The model and lexicon are built ahead of time by build-time tools
that are not part of the package.

![Architecture: an application calls the Tagger, which uses tokenization, the model and Upos](architecture.svg)

## Scope

This user guide covers:

- Installation of the library
- Tagging text and pre-tokenized sentences, results and offsets
- Reading confidences and alternatives, and choosing a confidence threshold
- Constraining the tag of a token
- Giving the tagger a glossary of domain terms
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
- **Linting Enforcement**: markdownlint, TextLint, and yamllint are enforced before any build proceeds
- **Automated Audit Documentation**: Each release ships with generated requirements, justifications,
  trace matrix, and quality reports
- **CodeQL and SonarCloud**: Security and quality analysis runs on every build

# Installation

Install the library using the .NET CLI:

```bash
dotnet add package DemaConsulting.PartOfSpeech
```

The package targets .NET Standard 2.0 (and so runs on .NET Framework 4.8.1 and every .NET version that implements it),
.NET 8, .NET 9, .NET 10 and .NET 11. It has no package dependencies. The package also contains a Markdown API reference
of the public API under its `api/` folder, generated from the XML documentation of the library.

**The trained model is part of the package.** The package contains the tokenizer, the tagger, the file formats, the
compiled word lexicon (639,711 bytes compressed) and the trained averaged-perceptron weights (553,641 bytes compressed,
1,234,530 bytes uncompressed, about 0.6 MB and 1.2 MB). `Tagger.Default` returns a shared tagger that uses them, so the
samples below need no file and no download. You can still create a tagger from your own weights file (see _Loading a
Weights File_).

The tagger decides token by token from left to right. For each token it scores the allowed tags from the word and its
ending, the words around it, the tags it chose before and, in the embedded model, clause cues: the kind of the
neighboring words (for example a determiner or a modal), the distance to the nearest determiner or modal, and whether
the current clause already has a subject-like tag and a verb tag. The cues were designed for words that are both noun and
verb, such as the first word of an instruction. The embedded model does not use tags of the words to its right.

The [evaluation record](https://github.com/demaconsulting/PartOfSpeech/blob/main/docs/evaluation/README.md) has the
results and the limits of the measurement, and the section _Choosing a Confidence Threshold_ says which confidence to
trust.

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
  lexicon can receive any of the 17 tags, so its confidence is spread over all of them. `TaggedToken.IsKnownWord` reports
  which case a token is: it is true when the lexicon has an entry for the spelling after the tagger folds it (Unicode
  NFC normalization; typographic quotes, dashes and the ellipsis mapped to ASCII) and lower-cases it, so `Cat` and `cat`
  are the same word, whatever a glossary or a constraint did to the tag.
- **The margin shows a close call.** `TaggedToken.Margin` is the confidence minus the probability of the best alternative,
  from 0 to 1. It is exact when at least one alternative is reported (the default is 3); with `MaxAlternatives` 0 it is
  the smallest value that the confidence allows, and for a token with one allowed tag it is 1. See _Using the Tagger
  in a Rule Checker_.
- **The lexicon is not a gold standard.** It records which tags a word is attested with, including some annotation noise
  (for example `the` also appears as PROPN), so a tag in the allowed set is not necessarily correct.
- **A low confidence is a reason to look.** Which confidence is low enough is measured: see _Choosing a Confidence
  Threshold_ below. Look at the alternatives to see what the competing readings are.

### Choosing a Confidence Threshold

The confidence is not equally reliable at every level, and the mistake that matters most is a noun that is tagged as a
verb or a verb that is tagged as a noun (a harmful error). The embedded model was measured on that mistake on held-out
data. A token is answered at a threshold when its confidence is at least the threshold; coverage is the share of the
noun-verb decisions that is answered.

| Threshold | Value | Coverage on the open test split | Harmful errors in 1,000 answered decisions |
| --- | ---: | ---: | ---: |
| strict | 0.90 | 89.7% | 4.6 |
| balanced | 0.75 | 94.9% | 9.1 |

On the development data the same thresholds answered 87.9% (strict, 4.3 harmful errors in 1,000) and 94.1% (balanced, 8.8)
of the decisions. The strict target of at most 5 in 1,000 was met by the point estimates on the open test split (4.6; its
95% interval is 2.4 to 7.2 and so includes 5) and on the held-out gold v2 test split (2 harmful errors in 821 answered
decisions) and is exceeded on the shape probe of short technical and imperative sentences (9.3 in 1,000 answered, four
errors in 429). The balanced target of at most 10 in 1,000 was met on the open test split (9.1; its interval is 6.1 to
12.2 and so includes 10) and exceeded on the gold v2 test split (10.2, 9 errors in 881 answered decisions) and on the
shape probe (20.2, 10 errors in 495).

Without a threshold, 19.1 of 1,000 noun-verb decisions of the open test split are harmful errors, and the rate is higher
on short technical and imperative text. Use these rules:

- **A consumer that needs a low harmful rate must use a threshold.** A rule checker, for example, uses `strict`:

  ```csharp
  foreach (TaggedToken token in Tagger.Default.Tag("Close the valve.")[0].Tokens)
  {
      bool trusted = token.Confidence >= 0.90;
      Console.WriteLine($"{token.Text}: {(trusted ? token.Tag.ToTagString() : "unknown")}");
  }
  ```

- **Treat a token below the threshold as unknown.** Do not act on its tag, show it to a person, or look at its
  alternatives and give the word a constraint or a glossary entry.
- **`balanced` is for consumers that can tolerate more errors** or that show the result to a person.
- **The thresholds apply to the embedded model.** Weights that you load need their own measurement.
- **Confidence 1.0 is not a measurement.** A token with one allowed tag is not a decision, and the confidence of a token
  that a hard glossary entry limits to one tag says nothing about whether the entry is right.

The development selection, the results on every evaluation set and their limits are in the
[evaluation record](https://github.com/demaconsulting/PartOfSpeech/blob/main/docs/evaluation/README.md); the same
guidance is in the API reference that is packed with the library.

### Technical Text

The thresholds above are chosen on ordinary text. Technical text is harder for the model: a noun that is spelled like a
verb (the head of a compound such as _the pressure check_ or a gerund such as _the shutdown testing_), a Capitalized
Defined Term (_Safe Mode_), the first word of a step line (_Close the valve._) and a base-form word in a clause
(_The pumps run._) are the words where a confident wrong tag is most likely, and the held-out evaluation does not
measure them. The model was therefore measured on four development tranches of such words (1,417 judged words, targets
and controls, from US federal technical documents; development data only, chosen from the contested words of another
classifier, so not the error rate of ordinary technical prose). At the strict threshold of the development selection
(0.90) 53.4% of the judged words are answered with 54.2 wrong answers in 1,000, and the harmful noun-verb errors are
48.7 in 1,000 answered decisions; at the balanced threshold (0.75) 73.7% are answered with 118.8 wrong answers in 1,000
and 121.6 harmful errors in 1,000 decisions. On such text the strict threshold is therefore far from the target of 5 in
1,000 that it reaches on ordinary text.

A consumer that needs a low error on technical text has a technical operating point: the first threshold with at most
10 wrong answers in 1,000 answers (1%) and at least 25% of the judged words answered. For the embedded model it is
**0.99**: 1 wrong answer in 357 (2.8 in 1,000, 95% interval 0.5 to 15.7 in 1,000) at a coverage of 25.2%. Read it with
its limits: it rests on one wrong answer, it clears the coverage floor by two answers, its interval reaches above 1%,
the threshold below it (0.98) has 13.2 wrong answers in 1,000 (6 in 454), and the figures are development figures. Use
`0.99` as the confidence below which a token in technical text is treated as unknown, expect about three in four of
such contested words to fall below it, and do not read 1% as a guarantee. The curve of every threshold, the rule and
its caveats are in
[`technical-operating-points.json`](https://github.com/demaconsulting/PartOfSpeech/blob/main/docs/evaluation/technical-operating-points.json)
and in the section "Technical text" of the
[selection report](https://github.com/demaconsulting/PartOfSpeech/blob/main/docs/evaluation/confidence-thresholds.md).

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

## Glossary

A `Glossary` is a list of domain terms with the tags that they take. A general model reads `bleed valve` as a verb and a
noun, because `bleed` is also a verb; a glossary tells the tagger once that the term is a noun, and the tagger applies
that to every occurrence. Entries are single words or several words, each with the tags of its head word and an optional
definition. Pass the glossary to a `Tag` call; it belongs to the call, so the shared `Tagger.Default` can serve any number
of glossaries.

### A list of technical names

```csharp
Glossary glossary = new GlossaryBuilder
{
    { "bleed valve", Upos.Noun, "A valve that releases trapped air." },
    { "torque wrench", Upos.Noun },
    { "accumulator", Upos.Noun },
}.Build();

TaggedSentence sentence = Tagger.Default.Tag("Bleed valves need a torque wrench.", glossary)[0];
foreach (GlossaryMatch match in sentence.GlossaryMatches)
{
    Console.WriteLine($"{match.Text} -> {sentence.Tokens[match.HeadToken].Tag}");
}
```

The head word of each term is a noun wherever the term occurs, also in the plural and at the start of a sentence. The
other words of a noun term (`bleed` here) can no longer be a verb or an auxiliary. `Tag(string, Glossary)`,
`Tag(Sentence, Glossary, constraints)` and `Tag(IReadOnlyList<string>, Glossary, constraints)` take a glossary; your own
constraints, if you pass any, are combined with those of the glossary per token, and yours win where the two allow no
tag in common.

### A multiword compound, a definition and an irregular form

```csharp
Glossary glossary = Glossary.Create(new[]
{
    new GlossaryEntry("bleed valve", Upos.Noun, "A valve that releases trapped air."),
    new GlossaryEntry("bleed", UposSet.Of(Upos.Verb, Upos.Noun)).WithVariants("bled"),
});

foreach (GlossaryMatch match in glossary.FindMatches(Tokenizer.Tokenize("Close the bleed valve.")[0]))
{
    Console.WriteLine($"{match.Text}: {match.Entry.Definition}");
}
```

At each position the longest term wins and matches never overlap, so `bleed valve` beats `bleed`. `FindMatches` finds
terms without tagging, for example to show definitions. The definition is never used for tagging; it travels with the
entry to the match.

### Letter case

Terms match in any letter case by default, because a term at the start of a sentence is capitalized. An acronym whose
lower-case spelling is another word needs a case-sensitive entry:

```csharp
Glossary glossary = new GlossaryBuilder
{
    new GlossaryEntry("IT", Upos.Noun).WithCaseSensitive(true),
}.Build();

Console.WriteLine(glossary.FindMatches(Tokenizer.Tokenize("Call IT now.")[0]).Count);   // 1
Console.WriteLine(glossary.FindMatches(Tokenizer.Tokenize("Call it now.")[0]).Count);   // 0
```

`GlossaryOptions.CaseSensitive` changes the default for the whole glossary and `WithCaseSensitive` overrides it per entry.
A case-sensitive and a case-insensitive entry for the same term may coexist; where both match, the case-sensitive one wins.

### Hard mode and soft mode

In hard mode (the default) the head word can only take the tags of the entry. In soft mode the tags get a preference of
`SoftStrength` times the temperature of the model, so the model can still disagree when it is sure:

```csharp
var options = new GlossaryOptions { Mode = GlossaryMode.Soft };
Glossary glossary = new GlossaryBuilder(options) { { "torque wrench", Upos.Noun } }.Build();

TaggedSentence sentence = Tagger.Default.Tag(new[] { "Use", "a", "torque", "wrench", "." }, glossary);
foreach (TaggedToken token in sentence.Tokens)
{
    Console.WriteLine($"{token.Text} {token.Tag} {token.Confidence:0.00} {token.IsGlossaryHead}");
}
```

Hard mode (the default) trusts the entries completely: **a wrong entry forces a wrong tag**, because the glossary
overrules the model, also where the model was right, and the tag is given with full confidence. That fits a curated list,
such as the technical names and verbs of ASD-STE100: every right entry helps and nothing disagrees. Soft mode is for a
list that is not curated or may be wrong (for example one that was extracted automatically): the model keeps its tag
when it is clearly sure, so a wrong entry costs much less. `GlossaryEntry.WithMode` sets the mode of one entry, and
`GlossaryOptions.SoftStrength` the strength (greater than zero; the default is `GlossaryOptions.DefaultSoftStrength`).

### Reading the results

- `TaggedToken.GlossaryMatch` is the match whose span contains the token, or `null`; `TaggedToken.IsGlossaryHead` is true
  for the head word of its match; `TaggedToken.IsConstrained` is true for every token of a match that the glossary
  constrained.
- `TaggedSentence.GlossaryMatches` lists the matches in text order. A `GlossaryMatch` has the `Entry` (and so the
  definition), `FirstToken`, `TokenCount` and `HeadToken` (indexes into the tokens of the sentence), `Start` and `Length`
  (offsets into the string that you passed in) and `Text` (the span as written), the effective `Mode`, and `IsVariant`
  (an inflected form or a listed variant was matched, not the term as written).
- **Confidence.** A head word that a hard entry limits to one tag has the confidence exactly 1.0 and no alternatives: there
  was no choice, so the value does not say that the model is sure. An entry with several tags, or soft mode, gives the
  softmax over the tags that remain, so a soft preference shows in the confidence.
- The chosen tag becomes the history of the following tokens like any other tag, so the neighbors after a match can change;
  the tokens before the first match are tagged exactly as without a glossary.

### Inflection

A glossary lists lemmas and text has plurals and verb forms, so the regular English forms of the head word match as well,
with the tags that fit the form:

- A noun entry matches its plural: `s`, `x`, `z`, `ch` and `sh` take `es` (`box`, `boxes`), a consonant and `y` become `ies`
  (`battery`, `batteries`), otherwise `s`; an acronym of capitals takes a lower-case `s` (`LRU`, `LRUs`).
- A verb entry matches the third person (the same rule as the plural), the past form (`isolated`, `carried`, `stopped`
  for a short word that ends in consonant, vowel, consonant) and the present participle (`isolating`, `carrying`,
  `stopping`, `tying`). A past form may be a verb or an adjective, a present participle a verb, an adjective or a noun.
- Only the head word is inflected: `bleed valve` matches `bleed valves`, not `bleeds valve`. Proper nouns, adjectives and
  other tags are not inflected.
- Irregular forms (`bled`, `leaves`) and the doubling of longer words (`permitted`) are not generated: list them with
  `WithVariants`, which takes whole-term spellings with the same number of tokens. A term as written wins over a listed
  variant, and a variant over a generated form. An irregular form that is identical to the lemma (`set`, `cut`) needs no
  variant, because the term already matches it, and a variant that repeats the term or another variant is ignored without
  an error.
- `GlossaryOptions.Inflect` and `GlossaryEntry.WithInflection` switch it off.

### Caveats

- **Tokenization.** A term is split into tokens like running text. `bleed-valve` is one token and does not match the text
  `bleed valve` (add both spellings as two entries; a variant cannot help, because it must have the same number of
  tokens as the term). A contraction in a term is split (`don't` becomes `do` and
  `n't`), so give such a term a head with `WithHead`. Punctuation inside a word (`3.5`, `R&D`, `and/or`) behaves as in text.
  A term never matches across two sentences, and a pre-tokenized token with punctuation stuck to it (`valve.`) does not
  match `valve`.
- **The head word.** The head word is the last word of the term, which is right for noun compounds (`bleed valve`),
  except for a phrasal verb: when the entry's tags include `VERB`, the term has at least two words, and every word after
  the first is one of a small closed list of particle words that the lexicon of the library also allows as adposition,
  adverb or particle, the head is the first word. The list is `about`, `across`, `ahead`, `along`, `apart`, `around`,
  `aside`, `away`, `back`, `by`, `down`, `forth`, `forward`, `in`, `off`, `on`, `onto`, `out`, `over`, `through`,
  `together`, `up`, `upon` and `within`. So `shut down`, `carry out`, `set up` and `switch off` are inferred with the head
  on the first word, while `take care`, `make sense` and `shut down valve` keep the last word. `WithHead` always
  overrides the default, in both directions, also `WithHead` with the last word, and a phrasal verb with a particle outside
  the list needs it. The verb tags then go to the head word
  (`shut`); `shuts` and `shutting` match as forms of the verb, and the particle `down` is
  limited to adposition, adverb and particle. That limit applies only to a verb term of exactly two words whose head is
  the first word; every other word of a term that is not a noun term (for example `it` in `take it apart`, the words in
  front of the head, and the words of an adjective phrase) is left to the model, and `WithTokenTags` sets the tags of one
  word explicitly. The inference looks the words up in the lexicon that is embedded in the library, also when your
  tagger was loaded with its own lexicon file; `GlossaryEntry.HeadIndex` tells which word was chosen.
- **Duplicates.** Two entries for the same tokens under the same case rule are refused; merge their tags into one entry.
- **Wrong entries.** See the hard and soft modes above. A hard entry with a wrong tag is tagged wrongly everywhere it
  occurs, with confidence 1.0.
- **Cost.** Building a glossary tokenizes every term and creates the inflected forms once. Matching a sentence costs
  about the length of the longest term per token, whatever the size of the glossary, and does not allocate per token for
  ordinary text; tagging with a glossary adds no allocation to a sentence without a match, and a sentence with matches
  adds one array sized by its token count (no object per token) and a few objects per match.
- **Thread safety.** A glossary and its entries are immutable and safe to share; a `GlossaryBuilder` is not thread-safe.

## Using the Tagger in a Rule Checker

A rule checker for controlled text acts on a tag only when the tag is trustworthy: a wrong noun or verb is the costly
error. Four things help:

- **The dictionary.** Give the terms that your domain knows to a `Glossary` (hard mode for a list that you trust
  completely, soft mode for a list that may contain errors; a wrong hard entry forces a wrong tag with the confidence
  1.0), or give a token that your checker already knows a constraint such as `TokenConstraint.Only(Upos.Noun)`.
- **The confidence**, read against the `strict` threshold of _Choosing a Confidence Threshold_.
- **The margin** and the runner-up `Alternatives[0]`: a margin near 0 is a close call. Keep `MaxAlternatives` at 1 or
  more (the default is 3) so that the margin is exact; with 0 it is the smallest value that the confidence allows. The
  margin is at least `2 * Confidence - 1`, so a token above the `strict` threshold has a large margin, and the margin
  adds information for the tokens below the threshold or as a stricter filter that you choose. No margin limit is
  measured and none is recommended: measure the limit on your own text.
- **The known-word flag.** `IsKnownWord` is false when the lexicon has no entry for the word. All 17 tags were then
  allowed (also for capitalized words, numbers and punctuation that the lexicon does not list), so a noun, verb or proper
  noun tag on such a word is a guess. The flag is about the lexicon, not about being a word: punctuation and numbers that
  the lexicon lists are known, and a capital letter never makes a word known.

A dictionary decides a token only when it leaves a single tag: a hard glossary entry or a `TokenConstraint.Only` with one
tag. That token has `IsConstrained` set, the confidence 1.0 and no `Alternatives`; the head word of an entry that the
lexicon does not list has `IsKnownWord` false, the tag of the entry and the confidence 1.0, and it is not inconclusive,
because the dictionary decided it (its confidence and margin are no measurement). `IsConstrained` alone is not enough: a
soft glossary entry, a `TokenConstraint.Prefer` or `Penalize`, and a constraint that allows several tags set it too, and
the model can still disagree, so those tokens go through the same checks as every other token. The confidence cannot
tell the cases apart, because a soft boost that is large enough rounds the confidence to exactly 1.0 while the other
tags are still allowed. The number of allowed tags can: a token with one allowed tag has no alternatives, and a token
with several allowed tags lists every other one (up to `MaxAlternatives`), also with the probability 0. So with
`MaxAlternatives` of 1 or more, `IsConstrained && Alternatives.Count == 0` is the test that the dictionary decided the
token; with `MaxAlternatives` 0 no token has alternatives and the test cannot be made.

```csharp
Glossary dictionary = new GlossaryBuilder
{
    { "manual override", Upos.Noun, "The mode in which an operator controls the unit by hand." },
    { "wiring schematic", Upos.Noun },
}.Build();

const double Strict = 0.90;          // the strict threshold from Choosing a Confidence Threshold
const double MinimumMargin = 0.20;   // your own limit, chosen from your own measurements; the package gives none

string Judge(TaggedToken token)
{
    if (token.IsConstrained && token.Alternatives.Count == 0)
    {
        return "dictionary";                       // one allowed tag; IsConstrained alone, or confidence 1.0, is not enough
    }

    if (token.Confidence < Strict)
    {
        return "inconclusive: low confidence";
    }

    if (!token.IsKnownWord && token.Tag is Upos.Noun or Upos.Verb or Upos.Propn)
    {
        return "inconclusive: unknown word";      // the lexicon has no evidence about the word
    }

    if (token.Margin < MinimumMargin)
    {
        return "inconclusive: close call";
    }

    return "conclusive";
}

foreach (TaggedSentence sentence in Tagger.Default.Tag("Select the manual override, then read the wiring schematic.", dictionary))
{
    foreach (TaggedToken token in sentence.Tokens)
    {
        Console.WriteLine($"{token.Text} {token.Tag.ToTagString()} {Judge(token)}");
    }
}
```

The order of the checks is: the dictionary left one tag, the confidence is below `strict`, the word is unknown and
the tag is a noun, verb or proper noun, the margin is below your limit; otherwise the tag is conclusive. When the checker
already knows the terms of each token, pass a `TokenConstraint.Only(Upos.Noun)` for those tokens to `Tag(sentence,
constraints)` instead of a glossary (see _Constraints_); those tokens report `IsConstrained`, the confidence 1.0 and no
alternatives.

## Loading a Weights File

```csharp
using var weights = File.OpenRead("model.posw");
Tagger tagger = Tagger.Load(weights);                           // embedded lexicon
using var lexicon = File.OpenRead("lexicon.posl");
Tagger custom = Tagger.Load(File.OpenRead("model.posw"), lexicon);   // your own lexicon file
```

A weights file with feature groups (the embedded model has the clause-cue group) also reads the lexicon to compute those
features, so a custom lexicon changes the features the model sees, not only the allowed tags.

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
embedded files once (the weights: 553,641 bytes compressed, 1,234,530 bytes uncompressed; the lexicon: 639,711 bytes
compressed, 1,626,269 bytes uncompressed; tens of milliseconds on a current machine), and every later read returns the
same thread-safe instance. A read does not fail in a released package; an `InvalidOperationException` or an
`InvalidDataException` would mean that the package is broken.

## Thread Safety

`Tagger`, `Tokenizer`, `TokenConstraint`, `Glossary`, `GlossaryEntry` and all result types are immutable and safe for use
from any number of threads
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
- `IReadOnlyList<TaggedSentence> Tag(string text, Glossary glossary)`: as `Tag(string)`, with the terms of the glossary
  applied.
- `TaggedSentence Tag(Sentence sentence, Glossary glossary, IReadOnlyList<TokenConstraint>? constraints = null)` and
  `TaggedSentence Tag(IReadOnlyList<string> tokens, Glossary glossary, IReadOnlyList<TokenConstraint>? constraints = null)`:
  as the overloads without a glossary; your constraints are combined with those of the glossary and win where the two
  allow no tag in common. A `null` glossary throws `ArgumentNullException` (use `Glossary.Empty` for none).

## TaggerOptions

- `int MaxAlternatives { get; init; }`: the most alternatives per token; 0 to 16, default 3.

## TaggedSentence

- `string Text`, `int Start`, `int Length`: the sentence and its place in the source string.
- `IReadOnlyList<TaggedToken> Tokens`: the tagged tokens in order.
- `IReadOnlyList<GlossaryMatch> GlossaryMatches`: the glossary terms that were found, in text order; empty without a
  glossary.

## TaggedToken

- `string Text`, `int Start`, `int Length`: the token and its place in the source string.
- `Upos Tag`: the chosen tag.
- `double Confidence`: the probability of the tag among the considered tags.
- `double Margin`: the confidence minus the probability of the best alternative, from 0 to 1; exact when at least one
  alternative is reported, otherwise `max(0, 2 * Confidence - 1)` (1 for a token with one allowed tag).
- `bool IsKnownWord`: true when the lexicon has an entry for the spelling of the token after it is folded (NFC
  normalization, typographic quotes, dashes and ellipsis mapped to ASCII) and lower-cased; not changed by a glossary
  or a constraint.
- `IReadOnlyList<TagScore> Alternatives`: the other considered tags by descending probability; read-only.
- `bool IsConstrained`: true when a `TokenConstraint` other than `None` applied to the token.
- `GlossaryMatch? GlossaryMatch`: the match whose span contains the token, or `null`.
- `bool IsGlossaryHead`: true for the head word of its match.

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

## Glossary Class

An immutable, thread-safe set of domain terms.

- `static Glossary Empty`: no entries; tagging with it gives the result of tagging without a glossary.
- `static Glossary Create(IEnumerable<GlossaryEntry> entries, GlossaryOptions? options = null)`: validates and copies
  the entries and the options; throws `ArgumentNullException` for a `null` list or entry, `ArgumentException` for
  duplicates and `ArgumentOutOfRangeException` for invalid options.
- `int Count`, `IReadOnlyList<GlossaryEntry> Entries` (in the order added), `GlossaryOptions Options` (a copy).
- `IReadOnlyList<GlossaryMatch> FindMatches(Sentence sentence)`: finds the terms without tagging.

## GlossaryBuilder

Collects entries for a collection initializer; not thread-safe.

- `GlossaryBuilder(GlossaryOptions? options = null)`, `int Count`.
- `GlossaryBuilder Add(GlossaryEntry entry)`, `Add(string term, Upos tag, string? definition = null)` and
  `Add(string term, UposSet tags, string? definition = null)`.
- `Glossary Build()`: a snapshot; the builder stays usable. The builder is enumerable.

## GlossaryEntry

One term; immutable. Every `With...` method returns a changed copy.

- `GlossaryEntry(string term, Upos tag, string? definition = null)` and
  `GlossaryEntry(string term, UposSet tags, string? definition = null)`.
- `string Term`, `IReadOnlyList<string> Tokens`, `UposSet Tags`, `string? Definition`, `int HeadIndex` (the first token
  of a phrasal verb with a listed particle word, otherwise the last token, unless `WithHead` set it),
  `bool? CaseSensitive`,
  `GlossaryMode? Mode`, `bool? Inflect` (`null`: the glossary default),
  `IReadOnlyList<string> Variants`, `UposSet? GetTokenTags(int tokenIndex)`.
- `WithDefinition(string?)`, `WithHead(int tokenIndex)` (overrides the inferred head, in both directions),
  `WithCaseSensitive(bool)`,
  `WithMode(GlossaryMode)`, `WithInflection(bool)`, `WithVariants(params string[])` and
  `WithTokenTags(int tokenIndex, UposSet tags)`.

## GlossaryOptions

- `bool CaseSensitive { get; init; }` (default `false`), `GlossaryMode Mode { get; init; }` (default `Hard`),
  `double SoftStrength { get; init; }` (in units of the model temperature; finite and greater than zero; default
  `GlossaryOptions.DefaultSoftStrength`) and `bool Inflect { get; init; }` (default `true`).

## GlossaryMode

`Hard` (the default; the head word takes only the tags of the entry, so a wrong entry forces a wrong tag; for curated
lists) and `Soft` (the tags get a preference; the model can disagree; for lists that are not curated or may be wrong).

## GlossaryMatch

One found term; immutable. `GlossaryEntry Entry`, `int FirstToken`, `int TokenCount`, `int HeadToken`, `int Start`,
`int Length`, `string Text`, `GlossaryMode Mode` and `bool IsVariant`.

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
corpus, MASC, the silver corpus, the imperative source, the compound sources and the technical-text sources and which
are embedded as a binary file
too. The attribution
and license notices of the third-party data are in
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
  Tatoeba CC0 export. The silver data (Tatoeba and Project Gutenberg text), the imperative, compound, headings and
  technical-text training sources (text of US federal government documents; 17 U.S.C. 105) and the evaluation sets follow
  the same terms.

## Evaluation-Only Data

The UD English-EWT treebank (CC BY-SA 4.0) is used for evaluation only. It is not distributed, nothing is trained on it,
and the repository ignores its files. No CC BY-SA or NonCommercial data is committed to the repository or included in the
package. The evaluation is run on it locally from a git-ignored cache; the recipe is in the
[evaluation record](https://github.com/demaconsulting/PartOfSpeech/blob/main/docs/evaluation/README.md), and the results
of every evaluation set are in its
[held-out report](https://github.com/demaconsulting/PartOfSpeech/blob/main/docs/evaluation/test-results.md).

# References

- [REF-1] Continuous Compliance Methodology (<https://github.com/demaconsulting/ContinuousCompliance>)
- [REF-2] Universal POS tags (<https://universaldependencies.org/u/pos/>)
