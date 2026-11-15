### TagDecoder

![Tagging Structure](TaggingView.svg)

#### Purpose

`TagDecoder` tags the tokens of one sentence with a greedy left-to-right decoder over the averaged-perceptron scores:
it scores the features, restricts the candidates with the lexicon and the constraints, chooses the best candidate, and
converts the scores into a calibrated confidence and ranked alternatives. A model with the right-preview feature group is
decoded in two passes (see `Decode`). It also answers the context-free lookup of one word (`GetPossibleTags`,
`IsFormOf`) with the same folding, the same lexicon read and the same shape rules as the decode. The source files are
`TagDecoder.cs`, the per-thread scratch memory `DecoderWorkspace.cs` and the list `ScoreSlice.cs`.

#### Data Model

- **`TagDecoder`** (internal, sealed, immutable): the `PerceptronModel`, the `Lexicon`, the temperature and scale as
  `double` and the feature groups of the model (`PerceptronModel.FeatureGroups`, which the decoder takes from the
  weights file and never from its caller). It holds no per-call state; thread safety follows from that and from the
  workspace rule below.
- **`DecoderWorkspace`** (internal): reusable arrays for one thread: the folded and lower-cased characters of the
  sentence, per token the start, length, facts, hashes, shape bits and tag-independent buckets, the bucket array of the
  token being scored (`BucketCapacity`, 40, so that group features fit), the integer scores (17), the real scores (17)
  and the lexicon scratch buffer; for the feature groups also the cached lexicon facts of the tokens, the token
  classes, the clause state and the tags of the first pass; and `LemmaBytes`, a 255-byte buffer (the longest base word
  that the lexicon format allows, so it never grows) in which `IsFormOf` encodes the base word it compares. It grows
  geometrically to the largest sentence seen and is
  kept in a `[ThreadStatic]` field of the decoder; it is used by one thread at a time. The decoder resolves a
  caller-supplied constraint list before it takes the workspace and runs no caller code while it holds it, so a list
  that tags other text from its indexer (a nested call) cannot corrupt it. The word lookup uses the same workspace as a
  one- or two-token sentence: it calls `Configure` first (which clears the cached lexicon facts of a sentence decoded
  earlier on the thread) and `Begin`, so a lookup between two decodes leaves no state behind that a decode reads.
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
  for the token. It reads the lexicon through `Lexicon.TryGetTags` and turns the facts into allowed tags with
  `ApplyShapeRules`.
- **`ApplyShapeRules(flags, known, tags)`**: the one place that turns the folded token's shape flags (letter or digit,
  digit, first upper-case letter, recorded by `FeatureExtractor.SetFolded`) and the lexicon tags into the allowed tags
  (`Lexicon.AllowedTags`). The decode and the word lookup both call it, so the tags of the lookup in the
  `WithShapeRules` mode are the decoder's allowed tags by construction, not by a copy of the rules.
- **`GetPossibleTags(string word, PossibleTagsMode mode)`** (and the `ReadOnlySpan<char>` twin on .NET 8 and later): the
  lookup of one word, called by `Tagger` after it validated the arguments. Non-ASCII text is NFC-normalized first
  (`NormalizeForLookup`, which returns an ASCII string as it is and otherwise calls `FeatureExtractor.NormalizeNonAscii`;
  a span with a character above U+007F is copied to a string first) so that the length test sees the real length. A
  word longer than the longest spelling goes to `LookupOverlong`; any other word is folded into the thread's workspace
  with `FeatureExtractor.SetFolded` (the folding core of `SetToken`, which takes a span or a string that is already
  normalized and gives identical characters, flags, shape and hashes), the scratch buffer is sized, and `Find` returns
  the match. The tags come from the whole word only: `LexiconOnly` gives the lexicon tags of the whole word (empty
  unless it is a spelling), `WithShapeRules` gives `ApplyShapeRules` of the whole word with `known` true only for a
  spelling (so all 17 tags for a not-recognized word or a compound head). The result (`WordTags`) keeps the lexicon, the
  ordinal of the matched spelling and `Lexicon.AnalysedTags` of it, so that no analysis is decoded unless asked for.
- **`Find(workspace, index, out ordinal, out keyLength, out wholeTags)`**: the whole spelling with `Lexicon.TryFindKey`;
  if it is not one, the compound rule on the folded, lower-cased characters: one scan that returns not recognized for a
  leading, doubled or trailing hyphen-minus (and for no hyphen at all), then `TryFindKey` of the last part only, at its
  offset in the same buffer and with the same scratch (no copy, no recursion). It returns `Spelling`, `CompoundHead` or
  `NotRecognized`; the key of the matched spelling stays UTF-8 encoded in the scratch buffer.
- **`LookupOverlong(text, mode)` and `OverlongHeadStart(text)`**: the same compound rule for a word longer than the longest
  spelling, which is never folded as a whole. `OverlongHeadStart` scans the normalized characters as written once (the
  hyphen-minus is the only hyphen; `FeatureExtractor.FoldsToDoubleHyphen` marks the three dash characters that fold to
  `--`, which leave an empty part exactly as in the folded word, and a test proves the helper agrees with the folding for
  every UTF-16 code unit) and returns where the last part starts, or -1 when there is no hyphen, a part is empty, or the
  last part is itself longer than the longest spelling (folding never shortens it, so it cannot be a spelling). Only
  then `LookupOverlong` folds the last part alone into the workspace (`SetFolded` of a span slice on .NET 8 and later,
  `SetFoldedPart` of the string and an offset on `netstandard2.0`, so nothing is copied) and looks it up as a whole with
  `TryFindKey`: found gives `CompoundHead` with that spelling's analyses, otherwise `NotRecognized`; the tags are those
  of any word that is not a spelling. The workspace therefore grows at most to what a word that fits needs, whatever the
  length of the whole word, and the cost beyond a normal lookup is one scan of the characters. A differential test looks
  up generated words (hyphens, doubled hyphens, dash characters, ellipsis, case, accents, prefixes that make the word
  too long) in a lexicon whose longest spelling is short and in one that also lists a 200-character spelling, and requires
  identical results.
- **`IsFormOf(string word, string lemma, Upos tag)`**: normalizes both texts, returns false when the base word is longer
  than 255 bytes, takes the last part with `OverlongHeadStart` when the word is longer than the longest spelling,
  folds the word (or its last part) as token 0 and the base word as token 1, finds the spelling (`Find`, or `TryFindKey`
  of the last part), encodes the lower-cased base word to UTF-8 in `workspace.LemmaBytes`, and asks
  `Lexicon.HasAnalysis`, which compares the bytes of each template of that tag with the key (still in the lexicon
  scratch) and the base word. It allocates nothing for ASCII text.

After the call: every result tag is a candidate; the confidence and the alternative probabilities are in [0, 1] and add
up to 1 when all candidates are listed; a token with one candidate has confidence 1 and no alternatives. A token with
several candidates has `min(MaxAlternatives, candidates - 1)` alternatives, also when a soft constraint rounds its
confidence to 1.0 (the alternatives then have the probability 0), so with `MaxAlternatives` of 1 or more the number of
alternatives, not the confidence, tells a token with one allowed tag. A token's
`IsKnownWord` is exactly the lexicon membership of its folded, lower-cased spelling: letter case, shape rules, a
constraint and the feature groups do not change it. The `TaggedToken` struct has the size of 48 bytes on a 64-bit
process, with and without the flag (the flag sits in the padding), so the allocation budget of the result array holds.

Complexity: 22 bucket lookups per token without groups (a bit test and about 2.7 additions each on a realistic model)
and one lexicon lookup (about 14 block-head comparisons and at most 15 decoded spellings). The groups add up to 18
bucket lookups and, for the inflection group, up to three more lexicon lookups for a token that ends in `ing`, `ed` or
`s`; a preview model decodes every token twice. The embedded model (clause cues) adds seven bucket lookups per token,
the class of the two neighbors from the cached lexicon lookup of the token (the decoder looks every token up once for
the restriction anyway) and a two-bit clause state per token. Without groups nothing of this runs, and the decode is
the one-pass decode of a model without groups. Measured speed is in the verification design.

**Word lookup parity.** The lookup in `WithShapeRules` mode returns exactly `{Tag} U Alternatives` of a decode of the same
word (with 16 alternatives and no constraint), at the first, a middle and the last position of a sentence, for a model
with and without feature groups, and its known flag equals the token's; the tests check this for every spelling of the
embedded lexicon, its title-case form, and 52 samples of other shapes (digits, punctuation, upper case, typographic
characters, decomposed text, hyphenated words, malformed compounds, unknown words, supplementary characters), about
525,000 words per model, with no difference. The `LexiconOnly` mode equals the lexicon tags. Some lexicon spellings are
not stable under the fold (for example a spelling with U+2015, which folds to `--`, or one with the modifier apostrophe
U+02BC, which folds to `'`): no input reaches them, because
every word is folded before the lookup, so the raw-parity check applies to the fold-stable spellings only. All 8,711
hyphenated spellings are found as `Spelling` and never as a compound head, and an unlisted prefix and a hyphen in
front of the last part of each give a compound head with that last part's analyses. The decode's hot path is the same
code as without the lookup
(`SetToken` normalizes and calls `SetFolded`; the allocation tests and the throughput gates of the decoder, the
tagger and the glossary pass unchanged on every target framework).

#### Error Handling

Argument errors are reported as described. Scores and probabilities cannot be `NaN` because the temperature of a loaded
model is finite and greater than zero and the adjustments of a constraint are finite. The decoder catches nothing; an
exception inside `Decode` (for example an out-of-memory condition) leaves the thread's workspace in a state that the
next call resets with `Begin`. The lookup (`GetPossibleTags`, `IsFormOf`) receives arguments that `Tagger` has validated
and fails for no text: a word that the lexicon does not know, an unpaired surrogate and a word of a million characters
are answered.

#### Dependencies

- **PerceptronModel** (`AddScores`, `HashBits`, `Temperature`, `Scale`, `FeatureGroups`) and **Lexicon** (`TryGetTags`,
  `TryFindKey`, `AllowedTags`, `AnalysedTags`, `HasAnalysis`, `ScratchSize`, `MaxSpellingBytes`) from ModelStorage.
- **FeatureExtractor** (including `Advance`, `StartPass`, `SetFolded`, `SetFoldedPart`, `NormalizeNonAscii` and
  `FoldsToDoubleHyphen`), **TokenConstraint**, the result types (`TaggedToken`, `TagScore`, `WordTags`, `WordMatch`,
  `PossibleTagsMode`) and **Token** (Tokenization).
- **Upos** (`UposSet` for candidate sets).

#### Callers

- **Tagger** (every `Tag` overload, `GetPossibleTags` and `IsFormOf`).
- The test suite, which also runs the decoder directly with synthetic models.
