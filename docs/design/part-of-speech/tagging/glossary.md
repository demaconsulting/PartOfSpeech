### Glossary

![Tagging Structure](TaggingView.svg)

#### Purpose

`Glossary` is the unit with which a caller hands the tagger a set of domain terms: single words and multiword terms,
each with the tags that its head word may take and an optional definition. It finds the terms in a sentence (longest
term first, left to right, no overlap) and turns each match into per-token constraints, so that the decoder, the
confidences and the tag history all follow the glossary. It is an immutable, thread-safe value that belongs to a call,
not to a tagger. The unit consists of `Glossary`, `GlossaryBuilder`, `GlossaryEntry`, `GlossaryOptions` (with
`GlossaryMode`) and `GlossaryMatch` as its public types, and the internal helpers `GlossaryIndex` (with `GlossaryForm`),
`GlossaryWorkspace`, `GlossaryInflector` and `GlossaryConstraints`.

#### Data Model

- **`GlossaryEntry`** (immutable): `Term` and the `Tokens` of the term as the tokenizer splits it in running text (the
  term is tokenized followed by a sentinel word that is dropped, so a final period of an abbreviation is kept as it is
  in the middle of a text; the form at the end of a text is kept as well when it differs); `Tags` (a non-empty
  `UposSet` for the head word); `Definition`; `HeadIndex` (the last token by default); `CaseSensitive`, `Mode` and
  `Inflect` (each `null` for the glossary default); the listed `Variants` with their tokens; and the optional explicit
  tags of non-head tokens (`GetTokenTags`). Every `With...` method returns a new entry.
- **`GlossaryOptions`**: `CaseSensitive` (default false), `Mode` (`Hard` by default), `SoftStrength` (in units of the
  model temperature; finite and greater than zero; the default is the constant `DefaultSoftStrength`, whose choice is
  documented in section E9 of the trainer's `RUN-LOG.md`) and `Inflect` (default true). A glossary keeps a validated copy.
- **`GlossaryBuilder`**: a list of entries that implements `IEnumerable<GlossaryEntry>` so that it works with a
  collection initializer; it is not thread-safe, and `Build` takes a snapshot.
- **`Glossary`**: the array of entries in the order added (exposed as a read-only view), the options copy and the
  `GlossaryIndex`. `Glossary.Empty` is the glossary without entries.
- **`GlossaryIndex`**: a trie over the folded, lower-cased tokens of every spelling, frozen into flat arrays. An edge is
  found in an open-addressing table keyed by the parent node and a 64-bit hash of the token; the stored key is compared
  with the characters of the token, so a hash collision never gives a wrong match. A node that ends a spelling points to
  a chain of `GlossaryForm` objects (at most one case-insensitive form and any number of case-sensitive forms that
  differ in letter case). A form records its entry, the offset of the head token inside the span, the tags of the head
  (the tags of the entry, or the derived tags of an inflected form), its rank (term, listed variant, generated form), the
  case rule and the exact and lower-case tokens. Equal spellings collapse into one form: the better rank wins, then the
  earlier entry, and the generated forms of the same entry unite their tags.
- **`GlossaryWorkspace`**: per-thread scratch memory (the folded characters of the sentence, token hashes and offsets,
  and the matches as integer arrays) that grows to the largest sentence and is reused.
- **`GlossaryConstraints`**: the constraints of a glossary compiled for one tagger: the head constraint of every distinct
  tag set in both modes, and the constraints of the non-head tokens of every entry. It is built once per glossary and
  tagger, because the soft boost is `SoftStrength` times the temperature of the tagger's model, and it is immutable.
- **`GlossaryMatch`** (immutable): the `Entry`, `FirstToken`, `TokenCount`, `HeadToken`, `Start` and `Length` in the
  source string, `Text` (the span as written), the effective `Mode` and `IsVariant` (an inflected form or a listed variant
  was matched). Internal members carry the entry index, the tag-set number and the start of the head token.

Invariants: an entry always has at least one tag and valid indexes; a glossary never contains two entries with the same
tokens under the same effective case rule; the index and the compiled constraints never change after creation.

#### Key Methods

- **Entry construction and `With...`**: validates the term (not null, at least one token with a letter or a digit), the
  tags, the head and token indexes, the mode and the variants (each variant must give the same number of tokens as
  the term). `WithHead` moves the head word; a phrasal verb entry such as `shut down` should use `WithHead(0)`, because
  its head is the first word, the verb tags belong to it and its inflected forms (`shuts`, `shutting`) are the forms of
  the first word.
- **`Glossary.Create(entries, options)`** (and `GlossaryBuilder.Build`): copies and validates the options, copies the
  entries, rejects `null` items and duplicates (the identity of an entry is its lower-case tokens, or its exact tokens
  when it is case-sensitive, under the effective case rule), collects the forms and builds the index. The forms of an
  entry are its term (and its end-of-text form), its variants, and, when inflection is on, the forms that
  `GlossaryInflector.Inflect` generates for the head word only: for a noun the plural (an acronym of capitals takes a
  lower-case `s`), for a verb the third person, the past form and the present participle, with the tags that fit the
  form (a plural is a noun, a past form a verb or an adjective, a present participle a verb, an adjective or a noun);
  proper nouns, adjectives and other tags are not inflected. Irregular forms and the doubling of longer words are not
  generated; they are listed with `WithVariants`.
- **`GlossaryIndex.Find(tokens, workspace)`**: folds every token once into the workspace (NFC, typographic quotes,
  dashes and the ellipsis mapped to ASCII, plus the lower-case form) and hashes it; then, from the left, walks the trie
  from the current token as deep as the tokens allow and takes the longest spelling whose case rule is satisfied (a
  case-sensitive form beats a case-insensitive form of the same length), records it, and continues after it. Tokens whose
  characters are ASCII, Latin-1 or the folded typographic characters are processed without allocation; any other token
  takes a slower path through `FeatureExtractor.Fold` that allocates.
- **`Glossary.FindMatches(sentence)`** and the internal `Match`: run `Find` with the workspace of the current thread and
  copy the result into `GlossaryMatch` objects before returning, so no caller code runs while the workspace holds
  results. A glossary without entries and a sentence without tokens give a shared empty list.
- **`GlossaryConstraints.Compile(glossary, temperature)`**: for every distinct head tag set builds the hard head
  constraint (`TokenConstraint.Only(tags)`) and the soft head constraint (`Prefer` of every tag with the boost, combined
  with `And`); for every entry builds the non-head table in the effective mode of the entry. The rules for a non-head
  token are, in this order: explicit tags (`WithTokenTags`) are treated like head tags; a token of a noun term (tags
  within `NOUN` and `PROPN`) must not be a verb or an auxiliary (hard: all tags except those two; soft: a penalty for
  both); the second token of a term of exactly two tokens whose tags include `VERB` and whose head is the first token is
  limited to adposition, adverb and particle (hard) or boosted for them (soft); every other non-head token (such as `it`
  in `take it apart`) is left to the model. The last rule is the one switch `NonNominalRule`, set on development data (see
  section E9 of the trainer's `RUN-LOG.md`) for the measured shape only: two-token phrasal verbs whose head is the first
  token. The boost is clamped to a large finite value so that a huge strength cannot overflow.
- **`GlossaryConstraints.Build(matches, tokenCount, constraints)`**: fills a constraint array of the sentence from the
  matches (shared structs, so nothing is allocated per token) and merges the caller's list token by token: no glossary
  constraint leaves the caller's, no caller constraint leaves the glossary's, two constraints are combined with `And`,
  and when their allowed sets have no tag in common, or the sum of their adjustments is not finite, the caller's
  constraint wins. A caller list of the wrong length throws `ArgumentException`.
- **`Tagger.Tag(..., glossary, constraints)`** (the three overloads, in unit `Tagger`): find the matches; without a match
  use the plain decoding path; otherwise get the compiled constraints of the glossary from a per-tagger
  `ConditionalWeakTable`, build the constraint array, decode, and attach the match to every token of its span
  (`TaggedToken.WithGlossaryMatch`) and the list to the sentence. The decoder is not changed by the glossary: its allowed
  tags, adjustments, confidence rules and history already do what the constraints ask. In particular a hard constraint
  with one tag gives confidence exactly 1 and no alternatives, and the tokens before the first match are decoded with
  nothing that depends on the glossary, so they equal the result without a glossary.

#### Error Handling

All validation happens when an entry, builder or glossary is created, so an invalid glossary can never reach the
matcher: a null term, entry, entry list or variant throws `ArgumentNullException`; an empty term or a term without a
letter or a digit, an empty tag set, a head or token index that is the head itself, a variant with another token count
and a duplicate entry throw `ArgumentException`; an undefined tag or mode, an index out of range and a soft strength
that is not finite and greater than zero throw `ArgumentOutOfRangeException`. `FindMatches` and the tagging overloads
throw `ArgumentNullException` for a null sentence, text, token list or glossary, and `ArgumentException` for a
constraint list of the wrong length or an invalid token (as the other overloads do). Nothing is caught except the
exception of `And` inside the merge of constraints, which is turned into "the caller's constraint wins".

#### Dependencies

- **Tokenizer** (`Tokenizer.TokenizeWords` for terms and the sentence tokens), **Sentence** and **Token** from Tokenization.
- **FeatureExtractor** (`Fold`, for tokens with unusual characters) and **TokenConstraint** (`Only`, `Prefer`,
  `Penalize`, `And`).
- **Upos** and **UposSet**.
- **Tagger**, **TagDecoder**, **TaggedToken** and **TaggedSentence** carry the integration: the tagger compiles the
  constraints for the temperature of its decoder, the decoder applies them unchanged, and the result types hold the
  matches. The unit tests therefore drive the glossary through a real tagger with tiny models.
- Base class library only (`ConditionalWeakTable` and `[ThreadStatic]` exist on every target framework).

#### Callers

- Library consumers create entries, builders and glossaries and pass a glossary to `Tagger.Tag`, or call `FindMatches` to
  detect terms and read definitions.
- **Tagger** calls `Match` and `GlossaryConstraints`.
- **ModelTrainer** uses the real glossary path in its glossary evaluation (see _ModelTrainer Design_).
