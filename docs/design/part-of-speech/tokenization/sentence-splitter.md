### SentenceSplitter

![Tokenization Structure](TokenizationView.svg)

#### Purpose

`SentenceSplitter` groups the tokens of a text into sentences. The rules are simple and deterministic, and they are
tuned to the open corpus, where the boundary benchmark joins consecutive sentences of one document and measures how many
boundaries are recovered. It is an internal static class with no state.

#### Data Model

- **Input**: the source string and the token list of the whole text, in source order.
- **Output**: a list of `Sentence` objects; every token belongs to exactly one sentence and the sentences keep the
  token order.
- **`NeverFinal`**: a case-insensitive set of abbreviations that are always followed by a name or a number and
  therefore never end a sentence (`mr. mrs. ms. dr. st. jr. sr. vs. v. prof. capt. gen. col. sgt. lt. rev. hon. mt. no.
  e.g. i.e. gov. sen. rep. pres. cmdr. adm. maj. cpl. pvt. supt. messrs. ft.`).
- **`SentenceStarters`**: a fixed set of capitalized words that typically start a sentence (`The`, `He`, `In`, `But` and
  similar); a dotted acronym such as `U.S.` ends a sentence only before one of them.

#### Key Methods

- **`Split(string source, IReadOnlyList<Token> tokens)`**: single left-to-right pass over the tokens. For each token it
  first checks for a blank line to the next token, then asks `IsBoundaryAfter`. A boundary closes the current sentence;
  closing quotes and brackets that touch the terminal token are included in it.
- **`HasBlankLine`**: counts line breaks in the white space between two tokens (a CRLF pair counts once, a paragraph
  separator U+2029 counts as a blank line); two or more breaks end the sentence, one does not.
- **`IsBoundaryAfter`**: a token ends a sentence when it is terminal punctuation (`.`, `!`, `?`, `...`, the ellipsis
  character, or a combination such as `?!`) or an abbreviation token that ends in a period, and the next token (after
  any touching closers) starts a sentence or the text ends. An ellipsis followed by a lower-case word continues the
  sentence.
- **`StartsSentence`**: an upper-case letter, a digit, or an opening quote or bracket (including `¿` and `¡`).
- **`EndsAfterAbbreviation`**: titles of `NeverFinal` never end a sentence; a single quoted initial (`"I."`) ends it
  before a capitalized word; a dotted acronym ends it only before a word of `SentenceStarters`; other abbreviations
  (`etc.`, `Inc.`) end it before a capitalized word.
- **`IsCloser`**: a one-character closer (`) ] } " ' ” ’ »`) that touches the previous token (no white space) closes a
  quotation; one that is separated by white space is an opening quote of the next sentence. This needs no quote
  parity state.

#### Error Handling

The splitter validates nothing because it is internal and called only with the output of `TokenizeWords`; an empty token
list gives an empty result. There are no exceptions to propagate. Because the lists of starter words and abbreviations
were tuned while looking at the open-corpus documents, the measured recall and precision are optimistic; precision is a
lower bound because a gold sentence can contain several real sentences.

#### Dependencies

- `Token` and `Sentence` (Tokenizer unit, same subsystem).
- Base class library only.

#### Callers

- **Tokenizer** (`Tokenize`) is the only caller.
