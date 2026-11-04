### Tagger

![Tagging Structure](TaggingView.svg)

#### Purpose

`Tagger` is the public entry point of the library. It loads a model, tags a text, a sentence or a list of tokens, and
returns immutable results with tags, confidences and ranked alternatives. Its source files also hold the options and the
result types: `Tagger.cs`, `TaggerOptions.cs`, `TaggedSentence.cs`, `TaggedToken.cs` and `TagScore.cs`.

#### Data Model

- **`Tagger`** (public, sealed, immutable): the `TagDecoder` and the maximum number of alternatives. The static
  `Default` is a `Lazy<Tagger>` with `ExecutionAndPublication`. `Options` returns a new `TaggerOptions` copy.
- **`TaggerOptions`** (public, sealed): `MaxAlternatives` (init-only, default 3, valid range 0 to 16 given by the
  constant `MaxAlternativesLimit`). It is validated when a tagger is created, not when it is set.
- **`TaggedSentence`** (public, sealed): `Text`, `Start`, `Length` (copied from the `Sentence` that was tagged, so the
  offsets index the same source string) and `Tokens` (a read-only list). Its constructor is internal.
- **`TaggedToken`** (public, read-only struct): `Text`, `Start`, `Length`, `Tag` (`Upos`), `Confidence` (the probability
  of the tag among the candidates, calibrated by the model temperature), `Alternatives` (an `IReadOnlyList<TagScore>`
  backed by a `ScoreSlice` over the sentence's flat array; empty when only one tag was possible or none was requested)
  and `IsConstrained`. `ToString` gives `text/TAG`.
- **`TagScore`** (public, read-only struct): `Tag` and `Probability`.
- Invariants: the alternatives of a token exclude its tag, are in descending probability, number at most
  `MaxAlternatives`, and together with the confidence sum to at most 1; the token text equals the source at its offset.

#### Key Methods

- **`Load(Stream weights, TaggerOptions? options = null)`**: validates the arguments and options, loads the weights with
  `PerceptronModel.Load` (expecting `FeatureExtractor.FeatureSetVersion`) and uses the embedded lexicon. The stream is
  read to its end and not closed.
- **`Load(Stream weights, Stream lexicon, TaggerOptions? options = null)`**: the same with a caller-supplied `POSL`
  lexicon.
- **`Tag(string text)`**: `Tokenizer.Tokenize`, then one `Tag(Sentence)` per sentence; an empty text gives an empty list.
- **`Tag(Sentence sentence, IReadOnlyList<TokenConstraint>? constraints = null)`**: `TagDecoder.Decode` over the sentence
  tokens, wrapped in a `TaggedSentence`.
- **`Tag(IReadOnlyList<string> tokens, IReadOnlyList<TokenConstraint>? constraints = null)`**: `Tokenizer.CreateSentence`
  and then the sentence overload; the tagged sentence's text is the tokens joined by single spaces.
- **`Default`**: the tagger over the embedded weights and lexicon: `new Tagger(EmbeddedModel.Weights,
  EmbeddedModel.Lexicon, default options)` created once by the `Lazy` (`ExecutionAndPublication`), so every caller, also
  concurrent first callers, receives the same instance. A missing resource (`InvalidOperationException`) or a damaged one
  (`InvalidDataException`) means a broken package; the `Lazy` caches the failure, so every access fails the same way.

#### Error Handling

- Creation: `ArgumentNullException` for a null stream; `ArgumentOutOfRangeException` when `MaxAlternatives` is outside 0
  to 16; `InvalidDataException` (from the readers) for a damaged file or one of another format or feature set version.
- Tagging: `ArgumentNullException` for a null text, sentence or token list; `ArgumentException` for an invalid token (from
  `CreateSentence`) or a constraint list whose length differs from the token count (from the decoder). No text makes
  tagging fail: a token with an unpaired surrogate (not valid UTF-16) is handled by `FeatureExtractor`, which counts it
  as U+FFFD for the features only, so the tokens, their text and offsets are those of the input.
- All of these are propagated to the caller; nothing is caught.

#### Dependencies

- **Tokenizer** (Tokenization), **TagDecoder**, **TokenConstraint** and **FeatureExtractor** (Tagging), **PerceptronModel**,
  **Lexicon** and **EmbeddedModel** (ModelStorage), **Upos**.
- Base class library only.

#### Callers

- Library consumers (the public API) and the test suite.
