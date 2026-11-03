## Tokenization

![Tokenization Structure](TokenizationView.svg)

### Overview

Tokenization turns a string of English text into sentences and tokens, and it is the only way text enters the tagger.
It reproduces the tokenization convention of the training data (the _Tokenization Convention_ of
`data/CONVENTIONS.md`), because the tagger learned from text tokenized that way and a different convention would show
it tokens it has never seen.

The subsystem owns the public types `Token` and `Sentence` and contains two units:

- **Tokenizer** (`Tokenizer.cs`, `Token.cs`, `Sentence.cs`): splits text into tokens and builds sentences from tokens.
- **SentenceSplitter** (`SentenceSplitter.cs`): groups the tokens of a text into sentences.

The subsystem does not tag, does not normalize characters (tokens are slices of the source string) and does not look
at the model or the lexicon. It has no state, so every member is safe for concurrent use.

### Interfaces

Exposed (public API, namespace `DemaConsulting.PartOfSpeech`):

- `Tokenizer.Tokenize(string text)`: returns the sentences of the text in order, each with its tokens; an empty or
  white-space-only text gives an empty list; `null` throws `ArgumentNullException`.
- `Tokenizer.CreateSentence(IReadOnlyList<string> tokens)`: builds one sentence from tokens that the caller produced.
- `Token` (text, start offset, length) and `Sentence` (text, start offset, length, tokens), both immutable.

Consumed: nothing from other subsystems. `Tagger` (Tagging) calls both public methods; the test suite and the
conformance tests call the internal `Tokenizer.TokenizeWords`, which is the word-level part without sentence splitting.

### Design

Tokenization runs in two passes over the same string. `Tokenizer.TokenizeWords` walks the text once, cuts it into
white-space delimited chunks and applies the word rules of the convention to each chunk, producing a list of tokens
with offsets. `SentenceSplitter.Split` then walks that token list once and decides after which tokens a sentence ends,
using the token texts, the capitalization of the next token and the white space between tokens (for blank lines).

The two units share one invariant: a sentence is a contiguous range of the token list, and its `Start` and `Length`
span from the first to the last token character of the original string. Every token offset indexes the whole source
string, not the sentence, so a result can always be located in the text that the caller supplied. For pre-tokenized
input, `Tokenizer.CreateSentence` creates the same type by joining the tokens with single spaces into a synthetic source
string; its sentence starts at 0 and its token offsets index that string.

The rules of the two units, the corpus conformance gate (at least 99.8 percent of the 14,980 open-corpus sentences
tokenized exactly like their gold tokens) and the boundary gate (at least 98.5 percent recall and precision on joined
consecutive sentences) are described in _Tokenizer Design_, _SentenceSplitter Design_ and the Tokenization verification
design.
