### Tokenizer

![Tokenization Structure](TokenizationView.svg)

#### Purpose

`Tokenizer` splits text into tokens that follow the tokenization convention of the training data, and creates sentences
from tokens that a caller already has. It also defines the two result types `Token` and `Sentence`. It is a static,
stateless class.

#### Data Model

- **`Token`** is a read-only struct: `Text` (exactly the source characters, never normalized), `Start` (offset in the
  source string) and `Length` (equal to `Text.Length`). Equality is by text and start offset. The public constructor
  `Token(string text, int start)` lets callers build tokens for their own tests; the tagger accepts token lists, not
  `Token` values.
- **`Sentence`** is an immutable class: `Text` (the source from the first to the last token character), `Start`,
  `Length` and `Tokens` (a read-only list). Invariants: tokens are in source order, do not overlap and contain no white
  space; `Tokens[i].Start` indexes the source string that the sentence belongs to, not `Text`. Its constructor is
  internal; callers obtain sentences from `Tokenize` and `CreateSentence`.
- **Rule data**: two fixed sets of abbreviations without the period. The title set (`mr mrs ms dr st jr sr no prof capt
  gen col sgt lt rev hon mt ft gov sen rep pres cmdr adm maj cpl pvt supt messrs`) keeps the period only when the word
  starts with a capital letter. The second set (`etc inc co corp ltd vs v e.g i.e ft`) keeps it in any letter case.

#### Key Methods

- **`Tokenize(string text)`**: validates the argument, calls `TokenizeWords` and then
  `SentenceSplitter.Split`. Preconditions: `text` is not `null`. Postcondition: the concatenation of all tokens equals
  the text with white space removed, and every token equals the source at its offset.
- **`TokenizeWords(string text)`** (internal): cuts the text at white space (`char.IsWhiteSpace`) into chunks and calls
  `TokenizeChunk` for each, telling it whether the chunk is the last one of the text. Surrogate pairs are never cut
  because the rules only look at characters that are not surrogates.
- **`TokenizeChunk`**: peels leading punctuation (one token per character: the brackets `(`, `[`, `{` and `<`, the
  double quote, the backtick, the guillemets and typographic quotes, `$`, `£`, `€`, and an apostrophe unless it starts
  a clitic form), peels trailing punctuation from the end, and passes the remaining core to `SplitCore`. The trailing
  tokens are emitted after the core in text order.
- **`KeepsPeriod`**: decides whether one trailing period belongs to its word (see the rules below).
- **`PeelDotRun`**: handles a run of two or more periods at the end of a chunk.
- **`SplitCore`**: keeps a URL or an e-mail address whole, splits at a double hyphen (`--`, `---`) and at the dash and
  ellipsis characters U+2013, U+2014, U+2015 and U+2026, and hands each word piece to `SplitWord`.
- **`SplitWord` and `ContractionSplit`**: split one piece into a stem and a contraction part when the piece is `cannot`,
  `shan't`, `can't`, `won't`, `ain't`, a letter stem plus `n't`, or a stem of letters and apostrophes plus `'s 'm 're
  've 'll 'd`; the case of both parts is kept and all apostrophe forms (U+0027, U+2018, U+2019, U+201B, U+02BC) are
  accepted while the token keeps the original character.
- **`CreateSentence(IReadOnlyList<string> tokens)`**: validates every token (not `null`, not empty, no white space),
  joins them with single spaces and returns a sentence with `Start` 0 whose token offsets index the joined string.

The word rules, as implemented (the rule table in `data/CONVENTIONS.md` is the normative copy):

1. Leading brackets, quotes, `` ` ``, `$`, `£` and `€` are separate tokens; a leading apostrophe stays attached when it
   begins `'s 'm 're 've 'll 'd 'em 'tis 'twas`.
2. Trailing `, ; : ! ? ) ] } " ' % >`, the backtick and the typographic quotes are separate tokens, repeatedly.
3. A single trailing period stays attached to a title or abbreviation of the two sets (not at the end of the text), to
   a dotted acronym (`U.S.`, `p.m.`), and to a single letter other than `I` (`J.`) unless it is the last chunk or a
   closing quote or bracket follows (`"a."` keeps it, `"I."` keeps it); otherwise it is a separate token.
4. A run of periods: a run that is the whole chunk is one token per period; a run of three after a word is one `...`
   token; a run of four is `.` followed by `...`; a longer run is one token.
5. A URL (`http://`, `https://`, `www.`) or an e-mail address is one token.
6. `--` and the dash characters split a chunk and are tokens of their own; a single hyphen stays inside the word.
7. Contractions as described under `SplitWord`; `o'clock`, `gonna` and names such as `D'Artagnan` stay whole because
   they match no contraction pattern; a stem with digits or periods stays whole.
8. Everything else, including numbers, times, slash and ampersand words, is one token.

#### Error Handling

`Tokenize` throws `ArgumentNullException` for `null`. `CreateSentence` throws `ArgumentNullException` for a null list
and `ArgumentException` (naming the token index) for a null, empty or white-space-containing token. No text makes
`Tokenize` fail: unknown constructs fall through to rule 8. Nothing is caught.

#### Dependencies

- **SentenceSplitter** (this subsystem) for `Tokenize`.
- **Base class library** only; no OTS item and no shared package.

#### Callers

- **Tagger** (Tagging) calls `Tokenize` for text input and `CreateSentence` for token lists.
- Library consumers call both public methods directly.
- The test suite calls `TokenizeWords` for the corpus conformance measure.
