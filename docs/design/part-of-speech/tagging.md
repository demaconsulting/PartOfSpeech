## Tagging

![Tagging Structure](TaggingView.svg)

### Overview

Tagging is the subsystem that assigns a Universal POS tag to every token and is the public face of the library. It
contains five units:

- **FeatureExtractor** (`FeatureExtractor.cs`): the single definition of the hashed features of a token: 22 base
  templates and five switchable feature groups, of which the embedded model enables the clause cues. It is shared
  between decoding and training (the trainer tool).
- **TagDecoder** (`TagDecoder.cs`, `DecoderWorkspace.cs`, `ScoreSlice.cs`): the greedy left-to-right decoder that scores
  the features with the weights, restricts the candidates with the lexicon and the constraints, and turns the scores
  into probabilities. `DecoderWorkspace` is its reusable per-thread scratch memory and `ScoreSlice` is the read-only
  list over the flat array of alternatives; both are internal helpers of the decoder, not units of their own.
- **TokenConstraint** (`TokenConstraint.cs`): the public value with which a caller restricts or steers the tag of one
  token.
- **Glossary** (`Glossary.cs`, `GlossaryBuilder.cs`, `GlossaryEntry.cs`, `GlossaryOptions.cs`, `GlossaryMatch.cs`,
  `GlossaryIndex.cs`, `GlossaryWorkspace.cs`, `GlossaryInflector.cs`, `GlossaryConstraints.cs`): the set of domain terms
  with their allowed tags, the matcher that finds them in a sentence and the compilation of a match into per-token
  constraints. `GlossaryIndex`, `GlossaryWorkspace`, `GlossaryInflector` and `GlossaryConstraints` are internal helpers
  of the unit.
- **Tagger** (`Tagger.cs`, `TaggerOptions.cs`, `TaggedSentence.cs`, `TaggedToken.cs`, `TagScore.cs`): the public entry
  point, its options and the immutable result types.

The subsystem uses Tokenization to split text, ModelStorage for the weights and the lexicon, and Upos for tags.

### Interfaces

Exposed (public API, namespace `DemaConsulting.PartOfSpeech`):

- `Tagger.Default`, `Tagger.Load(Stream, TaggerOptions?)`, `Tagger.Load(Stream, Stream, TaggerOptions?)`,
  `Tagger.Options`, and three `Tag` overloads: text, `Sentence`, and `IReadOnlyList<string>` tokens, the latter two with
  an optional constraint list, and the same three with a `Glossary` (the text overload without constraints).
- `TaggerOptions` (`MaxAlternatives`, 0 to 16, default 3), `TaggedSentence` (also `GlossaryMatches`), `TaggedToken`
  (text, offsets, tag, confidence, alternatives, `IsConstrained`, `GlossaryMatch`, `IsGlossaryHead`), `TagScore` (tag,
  probability) and `TokenConstraint` (`None`, `Only`, `Prefer`, `Penalize`, `And`).
- `Glossary` (`Create`, `Empty`, `Entries`, `Options`, `FindMatches`), `GlossaryBuilder`, `GlossaryEntry` (with the
  `With...` methods), `GlossaryOptions`, `GlossaryMode` and `GlossaryMatch`.

Consumed: `Tokenizer.Tokenize` and `Tokenizer.CreateSentence` (Tokenization); `Lexicon`, `PerceptronModel` and
`EmbeddedModel` (ModelStorage); `UposSet`, `Upos` (Upos).

### Design

`Tagger` is a thin, immutable facade. It owns one `TagDecoder` (built from a `PerceptronModel` and a `Lexicon`) and the
maximum number of alternatives. `Tag(string)` tokenizes into sentences and tags each; `Tag(Sentence, constraints)`
passes the sentence tokens to the decoder; `Tag(IReadOnlyList<string>, constraints)` builds a sentence with
`Tokenizer.CreateSentence` first. `Tagger.Default` is a `Lazy<Tagger>` over the embedded weights and lexicon (created
once, thread-safe, never fails in a released package; a missing or damaged resource would be a broken package and is
reported as `InvalidOperationException` or `InvalidDataException`).

For each sentence the decoder performs these steps (the detail is in _TagDecoder Design_):

1. `FeatureExtractor` folds every token once into the per-thread workspace and computes everything that does not depend
   on tags (18 of the 22 base feature buckets, plus the buckets of the feature groups that the model names).
2. For token _i_ the extractor adds the four features that use the two previous tags (and the history features of the
   groups), the weights of all buckets are summed per tag as integers, and the lexicon lookup gives the tags that the
   token may take.
3. The candidates are the tags that the lexicon and the shape rules allow, intersected with the allowed set of the
   token's `TokenConstraint`; if that is empty the constraint wins. Soft adjustments are added to the real scores.
4. The best candidate (lowest tag number on a tie) becomes the tag, the softmax over the candidates at the model
   temperature gives the confidence and the ranked alternatives, and the tag enters the history for the next token.
   A model with the right preview group runs steps 2 to 4 twice: the first pass only chooses tags, the second pass
   sees them for the next two tokens and produces the result.

This design keeps one decision procedure in one place: restriction, constraints and confidence all work on the same
candidate set, so a constraint can never produce a tag that the confidence does not describe.

**Glossary flow.** A call with a glossary adds two steps around the decoder and changes nothing inside it. Before
decoding, the `Glossary` finds its terms in the sentence (the longest term at each position from the left, no overlaps)
and `GlossaryConstraints` turns the matches into one `TokenConstraint` per token, merged with the constraints of the
caller; after decoding, every token of a match gets its `GlossaryMatch` and the sentence gets the list. The glossary
constraints are the same hard (`Only`) and soft (`Prefer`) constraints that a caller can build, so the decoder's
rules for candidates, adjustments, confidence and history apply unchanged. The compiled constraints depend on the
temperature of the model (the soft strength is in units of the temperature), so `Tagger` keeps one compiled table per
glossary in a `ConditionalWeakTable`; the glossary itself belongs to the call, so the shared `Tagger.Default` can serve
any number of glossaries and the options of a tagger stay a copy of what was loaded. A call without a match takes the
plain decoding path.

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
  tags, so the temperature of the trained model was refitted for 17 tags (on open development data, by the trainer's
  `calibrate` command and recorded in `training.json`). The lexicon restriction at decode time was kept. For training
  the restriction mode was chosen by measurement on development data: the spike's unrestricted training broke with its
  lexicon, which missed 2% of the gold tags, but the present lexicon covers the training data by construction, so three
  modes were compared (unrestricted, restricted history, fully restricted) and mode B (the history comes from the
  restricted guess, the update from the unrestricted guess) was selected; the log is in `RUN-LOG.md` of the trainer.
- **Extra features.** The feature set also contains the previous-two-tags pair (`p12`) and the previous-and-next
  neighbor pair (`m1n1`), which are not in the spike's base template list.
- **Context features.** The spike scores a token from its own spelling, two words on each side and two tags only. The
  feature groups add the class of the neighbors, the distance to a determiner or a modal and a clause state from the
  tags decided so far (the embedded model), and optionally the position in the sentence, the stem of an inflected word
  and the tags of a first pass for the next two tokens (switches that are off in the embedded model).
- **Tag history type.** Previous tags are `Upos` ordinals plus two start sentinels, not strings, and the extractor takes
  them as arguments, so a trainer can pass gold or predicted history.

**Contract with the Trainer.** The trainer reuses `FeatureExtractor` unchanged, so that training and decoding
see identical buckets, and writes weights with `PerceptronModelWriter` using the feature set version that
`FeatureExtractor.FeatureSetVersionFor` gives for the groups of context features it trained with: 1 without a group, 2
with at least one (the embedded model has the clause cues and so feature set 2), together with the group mask in the
header. The decoder takes the groups from
the weights file, so a model is always scored with the features it was trained with. Any change of the template set,
the hash, the fold or a group requires a new feature set version; readers refuse a weights file of an unknown version or
mask. The trainer also chooses `hashBits` (18 to 24; the shipped model uses 19), the
temperature (fitted for 17 tags) and the weight scale (the shipped model uses 1/8). The byte layouts are in
_ModelStorage_ (`POSW` and `POSL`). The feature set was not extended for glossary support: a trained "glossary says X"
feature was measured on development data and rejected in favor of score boosts of
`TokenConstraint.Prefer` (see _ModelTrainer Design_, _Glossary Decision_), so the glossary does not change the feature
set.

**Thread safety.** `Tagger`, `TagDecoder`, `Lexicon` and `PerceptronModel` hold read-only data, and so does a
`Glossary` after it was created. All per-call scratch
memory is a `[ThreadStatic]` workspace (the decoder's and the glossary matcher's) that grows to the largest sentence and
is reused; the decoder resolves a
caller-supplied constraint list before it takes the workspace and runs no caller code while it holds it, and the glossary
copies its matches out of its workspace before it returns. The compiled constraints of a glossary are created once per
tagger by a thread-safe lazy table. The results are
allocated once per sentence (one `TaggedToken` array and one flat `TagScore`
array whose slices back the alternatives, and with a glossary the match objects and the constraint array of the sentence)
and are immutable.
