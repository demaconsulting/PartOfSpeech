### TagDecoder

![Tagging Structure](TaggingView.svg)

#### Purpose

`TagDecoder` tags the tokens of one sentence with a greedy left-to-right decoder over the averaged-perceptron scores:
it scores the features, restricts the candidates with the lexicon and the constraints, chooses the best candidate, and
converts the scores into a calibrated confidence and ranked alternatives. A model with the right-preview feature group is
decoded in two passes (see `Decode`). The source files are `TagDecoder.cs`, the per-thread scratch memory
`DecoderWorkspace.cs` and the list `ScoreSlice.cs`.

#### Data Model

- **`TagDecoder`** (internal, sealed, immutable): the `PerceptronModel`, the `Lexicon`, the temperature and scale as
  `double` and the feature groups of the model (`PerceptronModel.FeatureGroups`, which the decoder takes from the
  weights file and never from its caller). It holds no per-call state; thread safety follows from that and from the
  workspace rule below.
- **`DecoderWorkspace`** (internal): reusable arrays for one thread: the folded and lower-cased characters of the
  sentence, per token the start, length, facts, hashes, shape bits and tag-independent buckets, the bucket array of the
  token being scored (`BucketCapacity`, 40, so that group features fit), the integer scores (17), the real scores (17)
  and the lexicon scratch buffer; for the feature groups also the cached lexicon facts of the tokens, the token
  classes, the clause state and the tags of the first pass. It grows geometrically to the largest sentence seen and is
  kept in a `[ThreadStatic]` field of the decoder; it is used by one thread at a time. The decoder resolves a
  caller-supplied constraint list before it takes the workspace and runs no caller code while it holds it, so a list
  that tags other text from its indexer (a nested call) cannot corrupt it.
- **`ScoreSlice`** (internal, sealed): an `IReadOnlyList<TagScore>` over a segment of the flat result array
  (`ArraySegment<T>` was not used because it exposes a writable `IList<T>` indexer). It is the type behind
  `TaggedToken.Alternatives`.
- **Result arrays**: one `TaggedToken[]` and, when alternatives are requested, one flat `TagScore[count * slots]` where
  `slots = min(maxAlternatives, 16)`; token _i_ owns slots `i * slots` to `i * slots + alternativeCount - 1`.

Scoring model: `score(tag) = (sum of int16 weights of the token's buckets for that tag) * Scale + constraint
adjustment`, a `double`. The integer sum is exact; only the final multiplication and the softmax use floating point.

#### Key Methods

- **`Decode(Token[] tokens, IReadOnlyList<TokenConstraint>? constraints, int maxAlternatives)`**: returns one
  `TaggedToken` per token.
  1. Throws `ArgumentNullException` for `null` tokens.
  2. Snapshots the constraints before the workspace is taken: `null` stays `null`; a `TokenConstraint[]` is used as is
     (an array indexer runs no user code, so there is no copy); any other list is copied once into a new array (one
     allocation per call, outside the per-token loop). `Count` is read exactly once and that many items are copied.
     Throws `ArgumentException` when that count differs from the token count; an empty array returns an empty result.
     The decode then reads the array, never the caller's list.
  3. Gets the workspace, configures it with the groups of the model and the lexicon (`DecoderWorkspace.Configure`),
     folds every token (`FeatureExtractor.SetToken`), calls `Finish` once, and sizes the lexicon scratch buffer. With the
     lexicon-reading groups `Finish` has looked every token up once; the decoder reuses those lookups.
  4. When the model has the right-preview group, runs the **first pass**: the same loop as below with the preview
     features inactive, constraints applied, and no probabilities or alternatives; the chosen tags are stored in the
     workspace (`Preview`) and the second pass is started (`FeatureExtractor.StartPass`). A model without that group has
     only the pass below, which is then the whole decode.
  5. Starts the history with `StartTag1` and `StartTag2` and calls `DecodeToken` for each token in order; the chosen tag
     becomes `previous1`, the old `previous1` becomes `previous2`, and `FeatureExtractor.Advance` records the decision
     for the clause state. This is the pass that produces the result (in a preview model it is the second pass).
  6. Creates each `TaggedToken` with the original token text and offset, the tag, the confidence, a window of the flat
     array, `IsConstrained` (the constraint is not `None`) and `IsKnownWord` (the lexicon fact that `DecodeToken`
     returned).
- **`DecodeToken`**: the work for one token.
  1. Clears the 17 integer scores, calls `FeatureExtractor.Extract`, and adds the weights of every bucket with
     `PerceptronModel.AddScores`.
  2. Computes the allowed tags: the lexicon lookup of the lower-cased folded token (or the cached lookup) and
     `Lexicon.AllowedTags` with the token facts. The candidates are the allowed set intersected with the constraint's
     allowed set; when that is empty, the constraint's allowed set alone (the constraint wins over the dictionary). The
     result of the lookup (whether the lexicon has an entry) is also returned through the `known` out parameter, so the
     known-word flag costs no further lookup; the first pass of a preview model discards it.
  3. For each candidate computes the real score and tracks the best; the lowest tag number wins a tie because the scan is
     in tag order and replaces the best only on a strictly greater score. In the first pass of a preview model the
     method returns here: only the tag is needed.
  4. Softmax over the candidates: `exp((score - best) / temperature)` divided by the sum, which is stable because the
     best term is 1. The confidence is the best candidate's share.
  5. Alternatives: repeatedly selects the most probable remaining candidate (lower tag number on a tie), up to
     `min(slots, candidates - 1)`, without sorting or allocating.
  6. Returns the tag.
- **`AllowedTags(workspace, index, out bool known)`**: the lexicon lookup with the workspace buffers and the shape facts,
  or the cached lookup of the groups; it returns the allowed tags and, through `known`, whether the lexicon has an entry
  for the token.

After the call: every result tag is a candidate; the confidence and the alternative probabilities are in (0, 1] and add
up to 1 when all candidates are listed; a token with one candidate has confidence 1 and no alternatives. A token's
`IsKnownWord` is exactly the lexicon membership of its folded, lower-cased spelling: letter case, shape rules, a
constraint and the feature groups do not change it. The `TaggedToken` struct has the size of 48 bytes on a 64-bit
process, with and without the flag (the flag sits in the padding), so the allocation budget of the result array holds.

Complexity: 22 bucket lookups per token without groups (a bit test and about 2.7 additions each on a realistic model)
and one lexicon lookup (about 14 block-head comparisons and at most 15 decoded spellings). The groups add up to 18
bucket lookups and, for the inflection group, up to three more lexicon lookups for a token that ends in `ing`, `ed` or
`s`; a preview model decodes every token twice. The embedded model (clause cues) adds seven bucket lookups per token,
the class of the two neighbors from the cached lexicon lookup of the token (the decoder looks every token up once for
the restriction anyway) and a two-bit clause state per token. Without groups nothing of this runs, and the decode is
the one-pass decode that the model had before the groups existed. Measured speed is in the verification design.

#### Error Handling

Argument errors are reported as described. Scores and probabilities cannot be `NaN` because the temperature of a loaded
model is finite and greater than zero and the adjustments of a constraint are finite. The decoder catches nothing; an
exception inside `Decode` (for example an out-of-memory condition) leaves the thread's workspace in a state that the
next call resets with `Begin`.

#### Dependencies

- **PerceptronModel** (`AddScores`, `HashBits`, `Temperature`, `Scale`, `FeatureGroups`) and **Lexicon** (`TryGetTags`,
  `AllowedTags`, `ScratchSize`) from ModelStorage.
- **FeatureExtractor** (including `Advance` and `StartPass`), **TokenConstraint**, the result types (`TaggedToken`,
  `TagScore`) and **Token** (Tokenization).
- **Upos** (`UposSet` for candidate sets).

#### Callers

- **Tagger** (every `Tag` overload).
- The test suite, which also runs the decoder directly with synthetic models.
