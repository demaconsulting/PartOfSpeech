### FeatureExtractor

![Tagging Structure](TaggingView.svg)

#### Purpose

`FeatureExtractor` is the single definition of the features that the perceptron scores: it turns the tokens of a
sentence and the two previous tags into hashed feature buckets. Decoding and the trainer both use it unchanged (the
trainer therefore trains for exactly the features that the decoder scores), because a model is only valid for the exact
feature set it was trained with. The 22 base templates can be extended by five switchable **feature groups** (context
features, `FeatureGroups`); the groups of a model are stored in its weights file and, with no group, the extractor
produces exactly the 22 base templates. The embedded model enables one group, `ClauseCues` (the choice is described in
the system design under _Context Features_). It is an internal static class (with the internal enumerations `FeatureKind`,
`FeatureGroups` and `TokenClass`) and has no state of its own; the state that the groups need lives in the caller's
`DecoderWorkspace`.

#### Data Model

- **`FeatureKind`**: the templates in bucket order, whose numeric values seed the hash. The 22 base templates are:
  - 0 to 3: Bias, Word (folded, cased), Lower (folded, lower-cased) and Shape;
  - 4 to 7: the prefixes of 1, 2, 3 and 4 characters of the lower-cased token;
  - 8 to 11: the suffixes of 1, 2, 3 and 4 characters of the lower-cased token;
  - 12: Initial, set when the token is the first one that contains a letter or a digit;
  - 13 to 16: Prev1, Prev2, Next1 and Next2, the lower-cased neighbors with boundary markers;
  - 17: PrevNext, the pair of the nearest neighbors;
  - 18 to 21: PrevTag1, PrevTag2, PrevTagPair and PrevTagLower, the previous tags alone, as a pair and with the word.

  The members from 22 on belong to the groups and are only ever appended, so the buckets of the base templates never
  move (see "Feature groups").
- **`FeatureGroups`** (flags, `uint`): `None` (0), `PositionCues` (1), `ClauseCues` (2), `RightPreview` (4),
  `Inflection` (8) and `TitleCaseLine` (16). Bit 32 and every higher bit are reserved for later groups and are rejected
  by the weights loader (`KnownGroupsMask` is 31).
- **Constants**: `FeatureSetVersion` (1; stored in a weights file without groups), `GroupsFeatureSetVersion` (2; stored
  in a weights file with at least one group; `FeatureSetVersionFor(groups)` gives the one that applies), `MaxFeatures`
  (22, the most features of a token without groups), `BucketCapacity` (42, the size of a bucket buffer that may hold
  group features), `StaticFeatureCount` (18, the features that do not depend on tags, the base stride of
  `DecoderWorkspace.StaticBuckets`; `StaticStrideFor(groups)` gives the stride with groups), `StartTag1` (17) and
  `StartTag2` (18) as the "previous tag" values before the first token, `ClassBoundary` (13, the class value before the
  first and after the last token), the token-fact bits `FlagLetterOrDigit`, `FlagDigit`, `FlagFirstUpper` and the shape
  bits `ShapeUpper`, `ShapeAllUpper`, `ShapeDigit`, `ShapeHyphen`, `ShapeApostrophe`, `ShapePunctuation`.
- **Shape** of a folded token: upper bit when the first character is an upper-case letter; all-upper when the token has
  more than one character, an upper-case letter and no lower-case letter; digit, hyphen and apostrophe bits when it
  contains them; punctuation bit when it has no letter and no digit; none of these is the plain lower shape.
- **Prefixes and suffixes** exist only when the token has at least _k_ characters, so a token has 15 to 22 features
  without groups.
- **Neighbor markers**: the lower-cased neighbors before the first and after the last token are the fixed words `-S-`,
  `-S2-`, `-E-` and `-E2-`.
- **Fold**: NFC normalization, U+2018, U+2019, U+201B and U+02BC to `'`, U+201C, U+201D and U+201E to `"`, U+2013,
  U+2014 and U+2015 to `--`, U+2026 to `...`.
- **Hash**: each string is hashed with FNV-1a over its UTF-16 code units; a feature bucket is a Murmur3-style mix of the
  template number and up to two values, masked to `2^hashBits - 1`. Tag features hash the tag number (0 to 18); group
  features hash small integer codes (class numbers, distances, flags) as their values.

All working memory lives in the caller's `DecoderWorkspace` (folded characters, per-token hashes, shape bits and the
tag-independent buckets, plus for the groups the lexicon facts of the tokens, the token classes, the clause state and
the preview tags), which the decoder keeps per thread.

##### Feature groups

The groups are defined in the protocol section of the trainer run log. Every group adds features behind the base
ones; the order of the buckets is: the base buckets that do not depend on tags, then those of `PositionCues`,
`ClauseCues`, `Inflection` and `TitleCaseLine`, then the four tag features, then the history features of `ClauseCues`
and `Inflection`, then those of `RightPreview`. With the 22 base templates, at most 18 + 5 + 5 + 1 + 2 tag-independent
and 4 + 2 + 1 + 4 tag-dependent features give the capacity of 42.

- **Token class** (position and clause cues): the first matching rule, from the lexicon tag set _T_ of the token (empty
  for a spelling that is not in the lexicon) and its lower-cased folded form, gives `Punct` (no letter and no digit),
  `Modal` (can, could, may, might, must, shall, should, will, would, cannot), `To` (`to`), `Num` (only digits, or NUM in
  _T_), `Det`, `Pron`, `Adp` and `Conj` (the tag DET, PRON, ADP or CCONJ or SCONJ in _T_ and none of NOUN, VERB, ADJ),
  `NounVerb` (NOUN and VERB), `NounOnly`, `VerbOnly`, `AdjAdv` (ADJ or ADV) and `Other` (everything else, including
  unknown spellings). The possessive words are determiners in the lexicon (`my`, `his`), so they are `Det`. A token is
  **function-only** when _T_ is not empty and lists only DET, PRON, ADP, CCONJ, SCONJ, PART, AUX, PUNCT or SYM.
- **`PositionCues`** (5 static features): `PosIdx` (the token index capped at 3), `FirstContent` (the index of the
  first token that is not function-only, capped at 2, with whether this token is it; unknown spellings count as
  content), `EndPos` (the sentence terminal `.`, `!`, `?`, `:` or another token or none, a single final closing quote
  or bracket skipped, with `PosIdx`), `LenBucket` (the sentence length bucket 1-3, 4-6, 7-10, 11-15, 16-25 or 26 and
  more) and `LenPos` (the length bucket with `PosIdx`).
- **`ClauseCues`** (5 static and 2 history features): `PrevClass1` and `NextClass1` (the class of the neighbor, or the
  boundary marker), `PrevNextClass` (the pair), `DetDist` (the distance 1 to 3 to the nearest preceding `Det` or `Num`
  token, 0 for none) and `ModalDist` (the distance 1 or 2 to the nearest preceding `Modal` or `To` token, 0 for none);
  the history features `ClauseState` (bit 0: a subject-like tag DET, PRON, NOUN, PROPN or NUM was decided since the last
  clause boundary; bit 1: a verb tag VERB or AUX was; a token that is exactly `,`, `;`, `:`, `(` or `--` after folding,
  or a decided SCONJ or CCONJ, clears both bits) and `ClauseStatePrev` (the state with the previous tag). The cue of an
  imperative (`Check the valve`: the verb is followed by a determiner) is `NextClass1`.
- **`Inflection`** (1 static and 1 history feature, only for a token that ends in `ing`, `ed` or `s`): `StemKind` is
  the suffix kind (1 for `ing`, 2 for `ed`, 3 for `s`, `es` and `ies`) with whether some stem candidate lists VERB and
  whether some lists NOUN in the lexicon; `StemKindPrev` adds the previous tag. The stem candidates have at least two
  characters and need at most three lookups: for `ing` and `ed` the stem, the stem with `e` restored and the stem with
  a doubled final consonant undone (`ied` to `y` for `ed`); for `s` the token without `s` (not after `ss`), without
  `es`, and with `ies` replaced by `y`. So `avoid` + `ing` has a verb stem, while `br` + `ing` and `th` + `ing` have
  none; `mak` + `e`, `run` + `n` + `ing` and `stop` + `p` + `ed` find `make`, `run` and `stop`.
- **`RightPreview`** (4 features, only in the second pass): the tags of a first decoding pass for the next two tokens,
  with `StartTag1` for the end of the sentence and `StartTag2` beyond it: `Rt1`, `Rt2`, `RtPair` and `RtPrev` (the
  previous tag of this pass with `Rt1`). The first pass runs without them, so it is the decode of the same weights
  without the group.
- **`TitleCaseLine`** (2 static features, tag-independent, no lexicon): a sentence is a **Title Case line** (a heading,
  a caption or a step title) when, among its word tokens with the first word token left out, there are at least two and
  at least 60% start with an upper-case letter (the integer test `5 * capitalized >= 3 * words`), and its last token is
  not `.`, `!` or `?`. A word token contains a letter and no digit, so numbers, numbered items (`3.`), punctuation and
  tokens such as `Part2` are not words; the first word token is left out so that the capital of the first word of any
  sentence is not counted. The two features of a token are `TitleLine` (the line flag with the capitalization of the
  token: the upper-first and all-upper shape bits) and `TitleLineFirst` (the line flag with whether the token is the
  first token of the sentence); on a line that is not Title Case both have one constant value for every token. The
  decision is made once per sentence in `Finish` from the token facts of the workspace and allocates nothing. A
  capital that starts a word of a heading therefore need not mean a proper name to the model.

#### Key Methods

- **`Prepare(workspace, tokens, hashBits)`**: `workspace.Begin`, `SetToken` for every token, then `Finish`.
- **`SetToken(workspace, index, text)`**: folds one token into the character buffer (NFC only when the token has
  non-ASCII characters, so pure ASCII allocates nothing), writes the lower-cased copy right behind it, and records the
  facts, the shape bits and the two hashes. Preconditions: tokens are set in order from 0.
- **`Finish(workspace, hashBits)`**: computes the 18 tag-independent buckets of every token, including the neighbors;
  when the workspace has groups it then looks the tokens up once in the workspace lexicon (position cues, clause cues
  and right preview) and appends the tag-independent buckets of the groups. It starts the first pass (clause state
  cleared, preview inactive).
- **`Extract(workspace, index, previous1, previous2, hashBits, buckets)`**: copies the static buckets of the token and
  appends the four buckets that depend on the two previous tags, then the history and preview buckets of the groups;
  returns the number of buckets (15 to 22 without groups, at most `BucketCapacity` with them).
- **`DecoderWorkspace.Configure(groups, lexicon)`**: selects the groups and the lexicon that they read, before
  `Prepare`; with `None` the workspace has no group features, and a workspace that was never configured
  is such a workspace.
- **`Advance(workspace, index, tag)`**: records the decision for a token as history for the following tokens (the
  clause state); the caller passes the tag that becomes `previous1` of the next token. A no-op without
  `ClauseCues`, so a caller may always call it.
- **`StartPass(workspace, preview)`**: clears the clause state and selects whether the preview features are active
  (only with `RightPreview`); the second pass of a preview decode calls it with `true` after
  `DecoderWorkspace.SetPreview` stored the tags of the first pass.
- **`Fold(string)`**: the readable twin of the character folding, for tests and tools.
- **`ReferenceBucket`, `ReferenceBuckets`, `ReferenceBucketOfCodes`, `ReferenceTokenClass` and `ReferenceStemValue`**:
  compute the same buckets from readable strings in the way the feature set is defined. They are slow and allocate,
  and exist as the oracle against which the tests check the fast path (and as a debugging aid for trainers). The
  overload of `ReferenceBuckets` with groups takes the lexicon, the tags decided so far and, for the second pass, the
  tags of the first pass. Both paths must agree for every token.
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
`ReferenceBucket` throws `ArgumentException` for a tag spelling that is neither a tag nor a start marker, and
`ReferenceBuckets` for a clause-cues call without the tags decided so far. `DecoderWorkspace.SetPreview` throws
`ArgumentException` for fewer tags than tokens. Nothing else is caught. A workspace without a lexicon treats every
token as not in the lexicon.

#### Dependencies

- **DecoderWorkspace** (same subsystem, its scratch memory), **Upos** (`UposExtensions.Count`, tag parsing for the
  reference path), **UposSet** and **Lexicon** (ModelStorage; the lookups of the groups) and **BinaryFormat** (the FNV
  constants).
- Base class library only.

#### Callers

- **TagDecoder** (`SetToken`, `Finish`, `Extract`, `Advance`, `StartPass`, constants) and **EmbeddedModel**,
  **Tagger** and **PerceptronModel** (`FeatureSetVersion`, `GroupsFeatureSetVersion`, `FeatureSetVersionFor`,
  `KnownGroupsMask`).
- The test suite and the trainer.
