### Tagger

![Tagging Structure](TaggingView.svg)

#### Purpose

`Tagger` is the public entry point of the library. It loads a model, tags a text, a sentence or a list of tokens, looks
up a single word on its own, and returns immutable results with tags, confidences and ranked alternatives. Its source
files also hold the options and the result types: `Tagger.cs`, `TaggerOptions.cs`, `TaggedSentence.cs`,
`TaggedToken.cs`, `TagScore.cs`, `PossibleTagsMode.cs`, `WordMatch.cs`, `WordAnalysis.cs` and `WordTags.cs`.

#### Data Model

- **`Tagger`** (public, sealed, immutable): the `TagDecoder`, the maximum number of alternatives and a
  `ConditionalWeakTable` that maps a `Glossary` to its `GlossaryConstraints` compiled for this tagger's temperature
  (created lazily and thread-safe; the table does not keep a glossary alive). The static
  `Default` is a `Lazy<Tagger>` with `ExecutionAndPublication`. `Options` returns a new `TaggerOptions` copy.
- **`TaggerOptions`** (public, sealed): `MaxAlternatives` (init-only, default 3, valid range 0 to 16 given by the
  constant `MaxAlternativesLimit`). It is validated when a tagger is created, not when it is set.
- **`TaggedSentence`** (public, sealed): `Text`, `Start`, `Length` (copied from the `Sentence` that was tagged, so the
  offsets index the same source string), `Tokens` (a read-only list) and `GlossaryMatches` (a read-only list in text
  order; empty without a glossary). Its constructor is internal.
- **`TaggedToken`** (public, read-only struct): `Text`, `Start`, `Length`, `Tag` (`Upos`), `Confidence` (the probability
  of the tag among the candidates, calibrated by the model temperature), `Alternatives` (an `IReadOnlyList<TagScore>`
  backed by a `ScoreSlice` over the sentence's flat array; empty when only one tag was possible or none was requested)
  and `IsConstrained`; `IsKnownWord` (whether the lexicon has an entry for the folded, lower-cased spelling; a lexicon
  fact that no glossary or constraint changes, so the head of a hard glossary entry that the lexicon does not list is
  not known but constrained); `Margin` (the confidence minus the probability of the first alternative, read from the
  backing array without creating the `ScoreSlice`; with no alternative `max(0, 2 * Confidence - 1)`, the smallest value
  that the confidence allows, which is 1 for a token with one allowed tag; 0 for a default token); `GlossaryMatch` (the
  match whose span contains the token, or `null`) and `IsGlossaryHead` (true for the head token of its match).
  `ToString` gives `text/TAG`. The flag is a `bool` that sits in the padding of the struct, so the size is 48 bytes on a
  64-bit process (verified with `Unsafe.SizeOf`; a test guards it), and `WithGlossaryMatch` copies it unchanged.
- **`TagScore`** (public, read-only struct): `Tag` and `Probability`.
- **`PossibleTagsMode`** (public enum): `LexiconOnly` (0, the default of the lookup: the tags the lexicon lists for the
  folded, lower-cased word; empty when it is not a spelling) and `WithShapeRules` (1: exactly the tags the decoder
  allows for the word as a token on its own; all 17 when it is not a spelling).
- **`WordMatch`** (public enum): `NotRecognized` (0, also the default value), `Spelling` (1: the folded, lower-cased
  word is a lexicon spelling) and `CompoundHead` (2: a hyphenated word that is not a spelling whose last part is one).
- **`WordAnalysis`** (public, read-only struct, `IEquatable`): `Lemma` (the base word, lower case NFC; proper-noun
  capitalization is not kept), `Tag` (`Upos`), `Features` (UD notation, names ascending, joined by `|`, empty when the
  table records none; only `Degree`, `Number`, `Person`, `Tense` and `VerbForm` exist) and `GetFeature(string name)`
  (the value of one feature or `null`; case-sensitive; allocates). `ToString` gives `test VERB Person=3|Tense=Pres`.
  The default value has an empty `Lemma`. The constructor is internal.
- **`WordTags`** (public, read-only struct, `IEquatable`): `Tags` (`UposSet`, by mode, from the whole word only),
  `Match` (`WordMatch`), `IsKnownWord` (`Match == Spelling`, equal to `TaggedToken.IsKnownWord` of the same word),
  `TagsWithoutAnalysis` (`Tags` minus the tags that an analysis of the matched spelling names; allocation-free) and
  `GetAnalyses()` (the analyses of the whole spelling for `Spelling`, of the last part for `CompoundHead`; an empty list
  otherwise; it allocates one array and one string per base word, and the features strings are shared). It holds the
  `Lexicon` and the ordinal of the matched spelling, the tags that an analysis names and the tags, so the analyses are
  decoded only when asked for. Two results are equal when they describe the same entry of the same lexicon with the same
  tags and match; `default(WordTags)` is `NotRecognized` with no tags and equals the not-recognized result of
  `LexiconOnly`. The constructor is internal.
- Invariants: the alternatives of a token exclude its tag, are in descending probability, number at most
  `MaxAlternatives`, and together with the confidence sum to at most 1; the token text equals the source at its offset;
  `0 <= Margin <= Confidence` and `Margin >= 2 * Confidence - 1`, and the margin is exact when at least one
  alternative is reported. A token with one allowed tag has no alternatives and the confidence 1; a token with several
  allowed tags has `min(MaxAlternatives, allowed - 1)` alternatives, also when a soft constraint rounds its confidence
  to 1.0 (the alternatives then have the probability 0), so with `MaxAlternatives` of 1 or more `IsConstrained` with no
  alternatives identifies a token that a constraint forced to one tag.

#### Key Methods

- **`Load(Stream weights, TaggerOptions? options = null)`**: validates the arguments and options, loads the weights with
  `PerceptronModel.Load` (any supported feature set: with or without feature groups) and uses the embedded lexicon. The
  stream is read to its end and not closed.
- **`Load(Stream weights, Stream lexicon, TaggerOptions? options = null)`**: the same with a caller-supplied `POSL`
  lexicon. The lexicon is not only the source of the allowed tags: the feature groups of a weights file (clause cues,
  position, inflection) read it to compute their features, so a custom lexicon changes the features the model sees
  compared with the lexicon the weights were trained with.
- **`Tag(string text)`**: `Tokenizer.Tokenize`, then one `Tag(Sentence)` per sentence; an empty text gives an empty
  list.
- **`Tag(Sentence sentence, IReadOnlyList<TokenConstraint>? constraints = null)`**: `TagDecoder.Decode` over the
  sentence tokens, wrapped in a `TaggedSentence`.
- **`Tag(IReadOnlyList<string> tokens, IReadOnlyList<TokenConstraint>? constraints = null)`**:
  `Tokenizer.CreateSentence` and then the sentence overload; the tagged sentence's text is the tokens joined by single
  spaces.
- **`Tag(string, Glossary)`, `Tag(Sentence, Glossary, constraints = null)` and `Tag(IReadOnlyList<string>, Glossary,
  constraints = null)`**: the same three ways of tagging with a glossary (the design of the glossary steps is in
  *Glossary Design*). The sentence overload finds the matches; without a match it takes the plain path, otherwise it
  takes the compiled constraints of the glossary for this tagger, merges the caller's constraints, decodes, and attaches
  every match to the tokens of its span and to the sentence. The optional constraint parameter comes last and the text
  overload has none, so that an existing call such as `Tag(sentence, null)` keeps binding to the overload without a
  glossary.
- **`Default`**: the tagger over the embedded weights and lexicon:
  `new Tagger(EmbeddedModel.Weights, EmbeddedModel.Lexicon, default options)` created once by the `Lazy`
  (`ExecutionAndPublication`), so every caller, also concurrent first callers, receives the same instance. A missing
  resource (`InvalidOperationException`) or a damaged one (`InvalidDataException`) means a broken package; the `Lazy`
  caches the failure, so every access fails the same way.
- **`GetPossibleTags(string word, PossibleTagsMode mode = LexiconOnly)`** (and on .NET 8 and later the
  `ReadOnlySpan<char>` overload; `netstandard2.0` has no span without a package): the context-free lookup of one word in
  the lexicon of this tagger (so `Default` and `Load(weights, lexicon)` both work). It validates the arguments
  (`ValidateMode`, `ValidateWord`: not null, not empty, no `char.IsWhiteSpace` character, the same rule as a token of
  `Tag(IReadOnlyList<string>)` and shared with it through `Tokenizer.ContainsWhiteSpace`), then calls
  `TagDecoder.GetPossibleTags`. It never uses a sentence, the model or a glossary. The word is folded like a token of
  the tagger (the same `FeatureExtractor` code), so letter case, typographic quotes, dashes and the ellipsis and Unicode
  composition never change the result; position and capitalization in a sentence do not exist for it. The span overload
  looks up a span of ASCII characters without a copy; a span with a character above U+007F is copied to a string first.
- **`IsFormOf(string word, string lemma, Upos tag)`**: validates (null, empty, white space, undefined tag) and calls
  `TagDecoder.IsFormOf`; true when an analysis of the word has this base word and tag. Word and base word are folded
  like a token, so case never matters (`IsFormOf("TESTS", "Test", Upos.Noun)`). The word is found with the lookup rule,
  so a compound head counts (`IsFormOf("well-tested", "test", Upos.Verb)` is true because the head `tested` has the base
  word `test`; a caller who must exclude compounds checks `WordTags.Match`). It is false for an unrecognized word, for a
  tag that is attested without an analysis, and for a lexicon without analyses. Mode-independent.

**The word lookup rules.**

- *Tags.* `LexiconOnly` reports the lexicon tags of the whole folded word (empty when it is not a spelling).
  `WithShapeRules` reports `Lexicon.AllowedTags` of the whole token with the decoder's own fold and shape facts: all 17
  tags when the word is not a spelling, otherwise the lexicon tags extended by the shape rules. Both come from the whole
  word only, exactly what the tagger sees, and are independent of `Match`.
- *Unknown versus empty.* A recognized word never has empty `Tags`; `NotRecognized` is a value of `Match`. `LexiconOnly`
  gives empty tags for a not-recognized word and `WithShapeRules` gives all 17, and both give `Match == NotRecognized`,
  `IsKnownWord == false` and no analyses. Callers test `Match` or `IsKnownWord`, never `Tags.IsEmpty`.
- *Compound rule.* The whole word is looked up first; if it is a spelling the result is `Spelling` with the analyses of
  that spelling (`built-in` has its own analyses, not those of `in`). Otherwise, a hyphenated compound is a word that
  contains the hyphen-minus `-` with every part between hyphens non-empty (`well-tested`, `pre-flight`; not `-ed`,
  `well-` or `a--b`); its last part is looked up as a whole and nothing is split, stripped or recursed into. A last part
  that is a spelling gives `CompoundHead` with the analyses of that part (`well-tested` carries
  `test VERB Tense=Past|VerbForm=Part`, the lemma of the head, not `well-test`); otherwise the result is
  `NotRecognized`. Only the hyphen-minus counts: a character that folds to `--` (en dash, em dash, horizontal bar) makes
  an empty part, and the typographic hyphen U+2010 is no hyphen.
- *Compound versus tagger.* The tagger looks up only the whole token and has no hyphen rule: a hyphenated token that is
  not a spelling is unknown to it (all 17 tags, `IsKnownWord` false), and the hyphen only changes the shape bits of the
  model features. So for `CompoundHead` the `Tags` are the whole-token result (empty in `LexiconOnly`, all 17 in
  `WithShapeRules`), `IsKnownWord` is false, and the analyses are extra evidence that never narrows the tags and never
  changes the tagging. `TagsWithoutAnalysis` is `Tags` minus the head's tags. A whole spelling always wins over its
  head.
- *Over-long words.* A word with more characters than the longest spelling of the lexicon has bytes (122 in the embedded
  lexicon; fewer for a small custom lexicon) cannot be a spelling as a whole (folding never shortens a word and a
  character takes at least one byte), so only the whole-spelling lookup is skipped for it; the compound rule applies
  unchanged to its last part. `TagDecoder` scans the word once, without folding it, for the compound structure (leading,
  doubled or trailing hyphen, a character that folds to `--`), and folds only the last part, and only when that part
  does not have more characters than that. The thread's workspace therefore never grows beyond what a word of that many
  characters needs (a word with fewer characters is folded as a whole even if its characters take more bytes than the
  longest spelling has, for example 100 times `é`; that is bounded by the same size, so the length test is on
  characters, not on the UTF-8 length, which would need a further scan of every lookup), ASCII input allocates nothing,
  and the word is read a bounded number of times (one scan for the compound structure, then the fold of the last part).
  The tests verify the guarantees that they can observe: completion without an error and the bound of the buffer; they
  do not measure the running time or the allocated bytes, which the opt-in benchmarks measure. The result is the same as
  the result of a lexicon whose longest spelling is long enough to look the whole word up (a test compares both over
  generated words), and a test measures the buffer on a new thread after the worst folded word (an ellipsis for every
  byte of the longest spelling) and then looks up words of a million characters.
- *Phrases.* The lexicon has no multiword spellings (a test proves it for every spelling), so text with white space is
  refused with `ArgumentException` instead of being answered "unknown"; phrases and terms belong in a `Glossary`.
- *Custom lexicon.* A lexicon compiled without an analyses table gives tags and `Match` but no analyses;
  `TagsWithoutAnalysis` equals `Tags`, and `IsFormOf` is false.
- *Thread safety.* The lookup uses the decoder's `[ThreadStatic]` workspace, so any number of threads may look up words
  and tag at the same time; the workspace is `Configure`d for the tagger's lexicon and groups at the start of every call
  (which also clears cached lexicon facts of a sentence that was decoded before), so the interleaving of lookups and
  decodes, also of two taggers with different lexicons on one thread, does not leak state (tests interleave them, and
  run eight threads).
- *No retention.* The decoder erases its workspace (`DecoderWorkspace.Clear`) in a `finally` block at the end of
  every lookup and form question, so the folded word, its hashes, the lexicon key and the encoded base word do not
  stay in the thread's memory.
- *Allocation.* After the first call on a thread `GetPossibleTags` (both overloads, both modes) and `IsFormOf` allocate
  0 bytes for ASCII words, including compounds, malformed compounds, over-long words and a 1,000,000-character word
  (measured by the opt-in benchmarks, not asserted by tests). A word with a non-ASCII character that is already NFC
  allocates nothing; a decomposed word allocates the normalized string (32 bytes for `cafe` plus U+0301); a span with an
  NFC non-ASCII word allocates its `ToString` (32 bytes for `café`), a decomposed one 64 bytes. `GetAnalyses()`
  allocates its array and the base word strings (measured per call: 360 bytes for `tests`, 600 for `well` with five
  analyses, 368 for the head of `well-tested`, 0 for a word without analyses). Measured on .NET 10, Windows x64, during
  development; `Tag(...)` of a 25-token sentence allocates 4,872 bytes (the result arrays), which the lookup does not
  change.

#### Error Handling

- Creation: `ArgumentNullException` for a null stream; `ArgumentOutOfRangeException` when `MaxAlternatives` is outside 0
  to 16; `InvalidDataException` (from the readers) for a damaged file or one of another format or feature set version.
- Tagging: `ArgumentNullException` for a null text, sentence, token list or glossary; `ArgumentException` for an invalid
  token (from `CreateSentence`) or a constraint list whose length differs from the token count (from the decoder, or
  from the merge of the glossary constraints). No text makes
  tagging fail: a token with an unpaired surrogate (not valid UTF-16) is handled by `FeatureExtractor`, which counts it
  as U+FFFD for the features only, so the tokens, their text and offsets are those of the input.
- All of these are propagated to the caller; nothing is caught.
- Lookup: `ArgumentNullException` for a null `word` (and a null `lemma` of `IsFormOf`); `ArgumentException` for an empty
  word or base word or one with a white-space character; `ArgumentOutOfRangeException` for a mode or tag that is not a
  defined value. No other input fails: an unpaired surrogate counts as U+FFFD, an over-long word is answered, and a word
  that the lexicon does not know is a `NotRecognized` result, not an error.

#### Dependencies

- **Tokenizer** (Tokenization; `ContainsWhiteSpace` is the white-space rule that tokens and lookups share),
  **TagDecoder**, **TokenConstraint**, **Glossary** and **FeatureExtractor** (Tagging), **PerceptronModel**, **Lexicon**
  and **EmbeddedModel** (ModelStorage), **Upos**.
- Base class library only.

#### Callers

- Library consumers (the public API) and the test suite.
