### FeatureExtractor

![Tagging Structure](TaggingView.svg)

#### Purpose

`FeatureExtractor` is the single definition of the features that the perceptron scores: it turns the tokens of a
sentence and the two previous tags into hashed feature buckets. Decoding and the trainer both use it unchanged (the
trainer therefore trains for exactly the features that the decoder scores), because a model is only valid for the exact
feature set it was trained with. It is an internal
static class (with the internal enumeration `FeatureKind`) and has no state of its own.

#### Data Model

- **`FeatureKind`**: the 22 templates in bucket order, whose numeric values seed the hash:
  - 0 to 3: Bias, Word (folded, cased), Lower (folded, lower-cased) and Shape;
  - 4 to 7: the prefixes of 1, 2, 3 and 4 characters of the lower-cased token;
  - 8 to 11: the suffixes of 1, 2, 3 and 4 characters of the lower-cased token;
  - 12: Initial, set when the token is the first one that contains a letter or a digit;
  - 13 to 16: Prev1, Prev2, Next1 and Next2, the lower-cased neighbors with boundary markers;
  - 17: PrevNext, the pair of the nearest neighbors;
  - 18 to 21: PrevTag1, PrevTag2, PrevTagPair and PrevTagLower, the previous tags alone, as a pair and with the word.
- **Constants**: `FeatureSetVersion` (1; stored in every weights file), `MaxFeatures` (22), `StaticFeatureCount` (18, the
  features that do not depend on tags), `StartTag1` (17) and `StartTag2` (18) as the "previous tag" values before the
  first token, the token-fact bits `FlagLetterOrDigit`, `FlagDigit`, `FlagFirstUpper` and the shape bits `ShapeUpper`,
  `ShapeAllUpper`, `ShapeDigit`, `ShapeHyphen`, `ShapeApostrophe`, `ShapePunctuation`.
- **Shape** of a folded token: upper bit when the first character is an upper-case letter; all-upper when the token has
  more than one character, an upper-case letter and no lower-case letter; digit, hyphen and apostrophe bits when it
  contains them; punctuation bit when it has no letter and no digit; none of these is the plain lower shape.
- **Prefixes and suffixes** exist only when the token has at least _k_ characters, so a token has 15 to 22 features.
- **Neighbor markers**: the lower-cased neighbors before the first and after the last token are the fixed words `-S-`,
  `-S2-`, `-E-` and `-E2-`.
- **Fold**: NFC normalization, U+2018, U+2019, U+201B and U+02BC to `'`, U+201C, U+201D and U+201E to `"`, U+2013,
  U+2014 and U+2015 to `--`, U+2026 to `...`.
- **Hash**: each string is hashed with FNV-1a over its UTF-16 code units; a feature bucket is a Murmur3-style mix of the
  template number and up to two values, masked to `2^hashBits - 1`. Tag features hash the tag number (0 to 18).

All working memory lives in the caller's `DecoderWorkspace` (folded characters, per-token hashes, shape bits and the
tag-independent buckets), which the decoder keeps per thread.

#### Key Methods

- **`Prepare(workspace, tokens, hashBits)`**: `workspace.Begin`, `SetToken` for every token, then `Finish`.
- **`SetToken(workspace, index, text)`**: folds one token into the character buffer (NFC only when the token has
  non-ASCII characters, so pure ASCII allocates nothing), writes the lower-cased copy right behind it, and records the
  facts, the shape bits and the two hashes. Preconditions: tokens are set in order from 0.
- **`Finish(workspace, hashBits)`**: computes the 18 tag-independent buckets of every token, including the neighbors.
- **`Extract(workspace, index, previous1, previous2, hashBits, buckets)`**: copies the static buckets of the token and
  appends the four buckets that depend on the two previous tags; returns the number of buckets (15 to 22).
- **`Fold(string)`**: the readable twin of the character folding, for tests and tools.
- **`ReferenceBucket` and `ReferenceBuckets`**: compute the same buckets from readable strings in the way the feature
  set is defined. They are slow and allocate, and exist as the oracle against which the tests check the fast path (and as
  a debugging aid for trainers). Both paths must agree for every token.
- **`ShapeLetters`, `HashString`**: helpers shared with the tests.

#### Error Handling

The fast path validates nothing: its inputs come from the decoder, which has validated them, and it cannot fail for
any string. `string.Normalize` throws `ArgumentException` for an unpaired surrogate (not valid UTF-16), so `SetToken`
and `Fold` first replace each unpaired surrogate by U+FFFD (`NormalizeNonAscii`) and only then normalize a token that
contains a non-ASCII character; a valid surrogate pair is kept. This is a robustness rule without a deviation: it
changes the features only (two different unpaired surrogates give the same features), never the text, token text or
offsets of the caller, it costs nothing for pure ASCII tokens (no extra scan, no copy) and only a read-only scan for a
well-formed non-ASCII token, and it is deterministic. Should a runtime still reject a text (for example another invalid
code point on .NET Framework), the `ArgumentException` of `Normalize` is caught and the token is used un-normalized.
`ReferenceBucket` throws `ArgumentException` for a tag spelling that is neither a tag nor a start marker.
Nothing else is caught.

#### Dependencies

- **DecoderWorkspace** (same subsystem, its scratch memory), **Upos** (`UposExtensions.Count`, tag parsing for the
  reference path) and **BinaryFormat** (the FNV constants).
- Base class library only.

#### Callers

- **TagDecoder** (`SetToken`, `Finish`, `Extract`, constants) and **EmbeddedModel** and **Tagger**
  (`FeatureSetVersion`).
- The test suite and the trainer.
