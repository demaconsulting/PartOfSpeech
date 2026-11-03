### TokenConstraint

![Tagging Structure](TaggingView.svg)

#### Purpose

`TokenConstraint` is the immutable value with which a caller restricts or steers the tag of one token. It has a hard
form (an allowed set of tags) and a soft form (a boost or a penalty on the score of one tag) that can be combined, and
its default value is the neutral constraint.

#### Data Model

- **`_allowedMask`** (`int`): the allowed tags as a bit mask where zero means all 17, so that `default` is neutral. A
  non-zero mask has at least one of the low 17 bits set.
- **`_adjustments`** (`double[17]?`): the score adjustment of every tag, or `null` when there is none. Boosts are
  positive, penalties negative, in raw score units (the decoder adds them to the scaled model score, so they are divided
  by the temperature in the softmax like the model's own scores). The array is owned by the constraint and never
  exposed or shared between constraints.
- **Properties**: `Allowed` (a `UposSet`; all tags when unrestricted), `IsNone` (no restriction and no adjustment;
  `Only(UposSet.All)` therefore equals `None`) and the static `None`. `HasAdjustments` and `AdjustmentOf(tag)` are
  internal.
- Immutability: every operation returns a new value; `And` allocates a new adjustment array, so a constraint never
  changes after creation and is thread-safe.

#### Key Methods

- **`Only(UposSet)` and `Only(params Upos[])`**: a hard constraint. Throws `ArgumentException` for an empty set and
  `ArgumentNullException` for a null array.
- **`Prefer(Upos tag, double boost)` and `Penalize(Upos tag, double penalty)`**: a soft constraint that adds a
  positive amount to, or subtracts it from, the score of one tag. Throws `ArgumentOutOfRangeException` when the tag is
  not one of the 17 or the amount is `NaN`, infinite, zero or negative.
- **`And(TokenConstraint other)`**: intersects the allowed sets and adds the adjustments of every tag. Throws
  `ArgumentException` when the allowed sets have no tag in common, because a contradiction is reported and not guessed
  away, and also when the sum of the adjustments of a tag is `NaN` or infinite: each factory accepts only finite
  amounts, but two finite amounts such as `double.MaxValue` can still overflow when added, and an infinite adjustment
  would turn the softmax confidences into `NaN`. The result shares no array with either operand. Canonical form: when
  the sums of all 17 tags are zero (for example `Prefer(Noun, 1).And(Penalize(Noun, 1))`), the result stores no
  adjustment array at all, exactly like a constraint that never had adjustments; `Only` likewise stores a full allowed
  set as mask zero. Every constraint therefore has one representation, and `IsNone`, `Equals`, `GetHashCode`,
  `ToString` and `TaggedToken.IsConstrained` (which is `!IsNone`) always agree.
- **`Equals`, `==`, `!=`, `GetHashCode`, `ToString`**: equality by allowed set and adjustment values (a missing array
  equals an array of zeros); `ToString` gives `None`, `Only(NOUN|VERB)` or `Only(NOUN|VERB)+adjusted`.

Semantics inside the tagger (implemented by `TagDecoder`, described here because callers rely on them): the effective
tags of a token are the constraint's allowed set intersected with what the lexicon and the shape rules allow; if that is
empty, the constraint's allowed set alone applies. Soft adjustments change the scores of the effective tags before the
choice and before the softmax. The chosen tag enters the history of the following tokens, so a constraint also
influences its neighbors. `TaggedToken.IsConstrained` is true for every token whose constraint is not `None`.

#### Error Handling

Invalid arguments throw the standard argument exceptions at creation time, so that an invalid constraint can never
reach the decoder. A combination that contradicts itself, or whose adjustment sum is not finite, throws
`ArgumentException` from `And`. Nothing is caught.

#### Dependencies

- **Upos** (`UposSet`, `UposExtensions.Count`). Base class library only.

#### Callers

- Library consumers create constraints and pass them to `Tagger.Tag`.
- **TagDecoder** reads `Allowed`, `IsNone` and `AdjustmentOf`.
