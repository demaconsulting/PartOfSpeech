## Tagging

![Tagging Structure](TaggingView.svg)

### Overview

Tagging is the subsystem that assigns a Universal POS tag to every token and is the public face of the library. It
contains four units:

- **FeatureExtractor** (`FeatureExtractor.cs`): the single definition of the 22 hashed features of a token. It is shared
  between decoding and, in a later release, training.
- **TagDecoder** (`TagDecoder.cs`, `DecoderWorkspace.cs`, `ScoreSlice.cs`): the greedy left-to-right decoder that scores
  the features with the weights, restricts the candidates with the lexicon and the constraints, and turns the scores
  into probabilities. `DecoderWorkspace` is its reusable per-thread scratch memory and `ScoreSlice` is the read-only
  list over the flat array of alternatives; both are internal helpers of the decoder, not units of their own.
- **TokenConstraint** (`TokenConstraint.cs`): the public value with which a caller restricts or steers the tag of one
  token.
- **Tagger** (`Tagger.cs`, `TaggerOptions.cs`, `TaggedSentence.cs`, `TaggedToken.cs`, `TagScore.cs`): the public entry
  point, its options and the immutable result types.

The subsystem uses Tokenization to split text, ModelStorage for the weights and the lexicon, and Upos for tags.

### Interfaces

Exposed (public API, namespace `DemaConsulting.PartOfSpeech`):

- `Tagger.Default`, `Tagger.Load(Stream, TaggerOptions?)`, `Tagger.Load(Stream, Stream, TaggerOptions?)`,
  `Tagger.Options`, and three `Tag` overloads: text, `Sentence`, and `IReadOnlyList<string>` tokens, the latter two with
  an optional constraint list.
- `TaggerOptions` (`MaxAlternatives`, 0 to 16, default 3), `TaggedSentence`, `TaggedToken` (text, offsets, tag,
  confidence, alternatives, `IsConstrained`), `TagScore` (tag, probability) and `TokenConstraint` (`None`, `Only`,
  `Prefer`, `Penalize`, `And`).

Consumed: `Tokenizer.Tokenize` and `Tokenizer.CreateSentence` (Tokenization); `Lexicon`, `PerceptronModel` and
`EmbeddedModel` (ModelStorage); `UposSet`, `Upos` (Upos).

### Design

`Tagger` is a thin, immutable facade. It owns one `TagDecoder` (built from a `PerceptronModel` and a `Lexicon`) and the
maximum number of alternatives. `Tag(string)` tokenizes into sentences and tags each; `Tag(Sentence, constraints)`
passes the sentence tokens to the decoder; `Tag(IReadOnlyList<string>, constraints)` builds a sentence with
`Tokenizer.CreateSentence` first. `Tagger.Default` is a `Lazy<Tagger>` over the embedded weights and lexicon and throws
`InvalidOperationException` while no weights are embedded.

For each sentence the decoder performs these steps (the detail is in _TagDecoder Design_):

1. `FeatureExtractor` folds every token once into the per-thread workspace and computes everything that does not depend
   on tags (18 of the 22 feature buckets).
2. For token _i_ the extractor adds the four features that use the two previous tags, the weights of all 22 buckets
   are summed per tag as integers, and the lexicon lookup gives the tags that the token may take.
3. The candidates are the tags that the lexicon and the shape rules allow, intersected with the allowed set of the
   token's `TokenConstraint`; if that is empty the constraint wins. Soft adjustments are added to the real scores.
4. The best candidate (lowest tag number on a tie) becomes the tag, the softmax over the candidates at the model
   temperature gives the confidence and the ranked alternatives, and the tag enters the history for the next token.

This design keeps one decision procedure in one place: restriction, constraints and confidence all work on the same
candidate set, so a constraint can never produce a tag that the confidence does not describe.

**Deviations from the averaged-perceptron spike** (all deliberate; the spike measured accuracy, this library productizes
it):

- **Hash instead of CRC-32.** The spike hashed a formatted feature string with CRC-32 and a bit mask. Here each string is
  hashed with FNV-1a over its UTF-16 code units and the template number and up to two values are combined with a Murmur3
  mixing step; no strings are built per token. Weights trained with the spike are therefore not interchangeable; the
  feature set version number pins the extractor and the hash.
- **Integer scoring.** Scores are the exact integer sum of `int16` weights, scaled once. Platform-independent tags
  follow, because no floating-point summation order is involved; only the probabilities, which use `Math.Exp`, can
  differ in the last bits between platforms.
- **Folding.** Features, the lexicon key and the neighbors use a folded token: NFC normalization, typographic quotes
  mapped to `'` and `"`, dashes to `--` and the ellipsis character to `...`, because the corpora are ASCII-normalized
  while real text is not. Token text and offsets always keep the original characters.
- **The 17-tag set.** The spike was calibrated for its own target set. This library tags with all 17 Universal POS
  tags, so the temperature of a trained model is refitted for 17 tags in the later release; the lexicon restriction
  at decode time was kept (training is unrestricted).
- **Extra features.** The feature set also contains the previous-two-tags pair (`p12`) and the previous-and-next
  neighbor pair (`m1n1`), which are not in the spike's base template list.
- **Tag history type.** Previous tags are `Upos` ordinals plus two start sentinels, not strings, and the extractor takes
  them as arguments, so a trainer can pass gold or predicted history.

**Contract with the phase 3 trainer.** The trainer reuses `FeatureExtractor` unchanged, so that training and decoding
see identical buckets, and writes weights with `PerceptronModelWriter` using `FeatureExtractor.FeatureSetVersion`
(currently 1). Any change of the template set, the hash or the fold requires a new feature set version; readers refuse
a weights file of another version. The trainer also chooses `hashBits` (18 to 24), refits the temperature for 17 tags
and writes the weight scale. The byte layouts are in _ModelStorage_ (`POSW` and `POSL`).

**Thread safety.** `Tagger`, `TagDecoder`, `Lexicon` and `PerceptronModel` hold read-only data. All per-call scratch
memory is a `[ThreadStatic]` workspace that grows to the largest sentence and is reused; the decoder resolves a
caller-supplied constraint list before it takes the workspace and runs no caller code while it holds it. The results are
allocated once per sentence (one `TaggedToken` array and one flat `TagScore`
array whose slices back the alternatives) and are immutable.
