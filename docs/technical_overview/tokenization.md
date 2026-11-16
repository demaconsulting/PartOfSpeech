# Tokenization and Sentence Splitting

A tagger does not see text; it sees a list of tokens, and it assigns one tag to each token. What counts as a token is
therefore part of the model. This section describes the conventions of the tokenizer and the sentence splitter, and
explains why they are fixed rules that must agree with the training data.

## Why the Convention Matters

The model learns from a corpus in which people or models have already cut the text into tokens and tagged each one. The
features that it learns (the spelling of a token, its suffixes, its neighbors) describe those tokens. If the library
cut a sentence differently at run time, the model would be asked about tokens that it never saw.

The contraction `don't` shows the effect. In the training convention it is two tokens, `do` and `n't`, and the model
has learned what each of them looks like and which tags it takes. A tokenizer that kept `don't` whole would
produce one token with the suffix `n't`, a shape that looks like an unusual word, and neighbors that have changed. The
weights are still the same, but they now apply to the wrong evidence. The same holds for every other convention:
whether a final period belongs to an abbreviation, whether a currency sign is a token, whether `well-known` is one word
or three.

For this reason the library does not try to be clever or linguistically ideal. It reproduces the convention of its
training data, and a test measures how often the tokenizer reproduces the gold tokens of the open training corpus. The
convention itself is written down in the data documentation of the repository, and the Part Of Speech User Guide gives
a short table of it.

## The Tokenization Rules

The tokenizer cuts the text at white space and then peels punctuation from the edges of each chunk. The rules are
ordinary code, not a trained model, so the result is deterministic and the cost is small. The main conventions are:

- Opening brackets, quotes and currency signs are separate tokens (a leading apostrophe stays with the word when it
  begins a clitic such as `'s` or `'em`), and so are closing brackets, quotes and the commas, colons, question marks
  and similar marks at the end of a chunk.
- A contraction is split into a stem and a clitic: `John's` becomes `John` and `'s`, `we're` becomes `we` and `'re`,
  and `can't` becomes `ca` and `n't`. Words that merely look similar, such as `o'clock` or `gonna`, stay whole.
- A single final period stays attached only where it belongs to the word: a dotted acronym such as `U.S.`, a title
  such as `Mr.` (only when it is written with a capital letter), an abbreviation from a fixed list such as `etc.` or
  `Inc.`, or a single-letter initial such as `J.`. Any other final period is its own token.
- The end of the text is an exception. In the last chunk of the text a title, a listed abbreviation and an initial lose
  their period, because there it is far more likely a full stop: a final `Mr.` becomes `Mr` and `.`. A dotted acronym
  keeps its period even there, and a single letter inside a closing quote or bracket (`"a."`) keeps it too. The pronoun
  `I` never keeps a period, unless a closing quote or bracket follows it.
- A single hyphen stays inside a word (`well-known`), while a double hyphen or a dash character is a separate token.
- Web addresses, e-mail addresses and numbers with internal punctuation (`1,000`, `7:00`) are single tokens.

Every token is a slice of the original text and keeps its offset. The tagger folds the text for its own features (see
the Features section), but the text and the offsets that it returns are always the ones of the caller.

## Sentence Splitting

The tags of a token depend on the previous tags, so the tagger needs to know where a sentence starts. The sentence
splitter makes one pass over the tokens. A sentence ends after terminal punctuation (a period, a question mark, an
exclamation mark or an ellipsis) when the next token starts a sentence, that is, when it is a capitalized word, a digit
or an opening quote. A closing quote or bracket that touches the terminal mark stays with its sentence. A blank line
always ends a sentence, and a single line break does not.

Abbreviations are the hard part, because a period after `Dr` and a period after `home` look the same. The splitter
handles them with rules and two small fixed lists. A title such as `Dr.` is on a list of words that are always followed
by a name or a number, so it never ends a sentence. A single-letter initial does not end a sentence either, except that
a quoted initial before a capitalized word does. A dotted acronym such as `U.S.` ends a sentence only before a word on
a list of capitalized words that commonly start a sentence. Any other abbreviation, such as `etc.`, ends a sentence
before a capitalized word.

The splitter is a rule set, not a learned model, for three reasons. It needs no data and no weights, so the tokenization
subsystem has no dependency on the model. It is fast and deterministic. And its mistakes are easy to explain. The price
is accuracy on text that the rules did not anticipate: the lists were tuned while looking at the documents of the open
corpus, so a measurement on that corpus is optimistic. Text with many unusual abbreviations, or with sentences that
start with a lower-case word, will be split in the wrong places. A wrong split matters for tagging because each
sentence is decoded on its own, starting from fixed start markers, so the model loses the context on the other side of
the wrong boundary.

## Pre-Tokenized Input

A caller that already has tokens can pass them to the tagger as one sentence. The tagger then trusts the tokens, and the
responsibility for the convention moves to the caller. Tokens that follow another convention, for example a contraction
that is kept whole, are still tagged, but they are less familiar to the model and to the lexicon, and the confidence
will usually show it. For best results, use the tokenizer of the library, or a tokenizer that follows the same
convention.
