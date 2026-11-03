## Upos

![Part Of Speech Structure](PartOfSpeechView.svg)

### Purpose

`Upos` is the tag vocabulary of the library: the 17 Universal POS tags as an enumeration, their text forms, and a small
immutable set type for combining tags. It is the one unit that sits directly under the system and is used by every
subsystem. Its source files are `Upos.cs` (the enumeration), `UposExtensions.cs` (text conversion and the tag count) and
`UposSet.cs` (the set type).

### Data Model

- **`Upos`** is an enumeration over `byte` with 17 members in alphabetical order of the Universal POS names, with the
  values 0 to 16: `Adj`, `Adp`, `Adv`, `Aux`, `Cconj`, `Det`, `Intj`, `Noun`, `Num`, `Part`, `Pron`, `Propn`, `Punct`,
  `Sconj`, `Sym`, `Verb`, `X`. The order is part of the binary formats: the tag number is the column of the weights
  and the bit of the lexicon masks, so the order never changes without a new format version.
- **`UposExtensions.Count`** is the constant 17.
- **`UposSet`** is a read-only struct over one `int` mask. Bit _i_ is set when the tag with value _i_ is in the set;
  only the low 17 bits are ever set. Invariants: the default value is the empty set; two sets are equal exactly when
  their masks are equal; enumeration is in ascending tag order. `UposSet.All` has all 17 bits set. The mask is
  exposed to the library through an internal property and an internal `FromMask` factory.

### Key Methods

- **`UposExtensions.ToTagString(Upos)`**: returns the Universal POS name in upper case (`NOUN`, `CCONJ`) by a lookup
  in a table of 17 names. Throws `ArgumentOutOfRangeException` for a value that is not one of the 17 members.
- **`UposExtensions.TryParse(string?, out Upos)`**: ordinal, exact comparison with the 17 names, with no trimming and no
  case folding. Returns `false` (and `Adj` in the output) for `null` or any other text.
- **`UposSet.Of(params Upos[])`**: builds a set from the tags. Throws `ArgumentNullException` for `null` and
  `ArgumentOutOfRangeException` for a value that is not a tag.
- **`Contains`, `Add`, `Remove`**: bit test, set and clear on the mask; `Add` and `Remove` return a new set.
  `Contains` returns `false` for a value that is not a tag; `Add` and `Remove` throw `ArgumentOutOfRangeException`
  for one.
- **`Union`, `Intersect`**: bitwise or and bitwise and of the masks; pure functions.
- **`Count`**: the population count of the mask, from 0 to 17.
- **`GetEnumerator`, `ToString`**: ascending tag order; `ToString` joins the names with a vertical bar (for example
  `NOUN|VERB`) and gives an empty string for the empty set.

### Error Handling

All methods validate their arguments and throw the standard argument exceptions; none returns an error code except
`TryParse`. There is no internal state, so no operation can fail half way. Nothing is caught.

### Dependencies

None. `Upos` uses only the base class library.

### Callers

Every other unit: `Lexicon` (masks and allowed tags), `PerceptronModel` (tag columns), `FeatureExtractor` (previous tags),
`TagDecoder` (candidates), `TokenConstraint` (allowed set and adjustments), `Tagger` and the result types, and the
library consumers.
