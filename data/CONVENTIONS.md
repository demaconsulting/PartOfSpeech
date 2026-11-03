<!-- markdownlint-disable MD013 MD060 -->

# Data Conventions

This document is normative for every file under `data/`: the tag set, the annotation conventions that all training
and evaluation data follow, the file formats, the tokenization convention, and every mapping table that was used to
bring a source into that tag set. The scripts in `tools/data/` implement these tables; if the two disagree, fix the
script or this document so that they agree again.

## Tag Set

The 17 Universal POS (UPOS) tags are the only tags used by training and evaluation corpora:

```text
ADJ ADP ADV AUX CCONJ DET INTJ NOUN NUM PART PRON PROPN PUNCT SCONJ SYM VERB X
```

The only other value that can appear in a tag column is `_`, and only in a row whose status is `masked` (silver data:
the model label was not trustworthy, so the token carries no training signal).

## Annotation Conventions

All training data follows the conventions of the open English POS corpus (the guideline is
[GUIDELINE.md, version 4](https://github.com/demaconsulting/WordSense/blob/6530de1928baf30f9a14446a54bfb5dd5f67b41c/corpus/open/GUIDELINE.md),
section 4). They differ from Universal Dependencies in documented ways, which is why UD English-EWT is mapped before it
is scored (see below). The decisions that matter most:

| Topic | Convention |
| --- | --- |
| Possessive words | `my, your, his, her, its, our, their, whose` before a noun are DET (not PRON) |
| Phrasal-verb particles | `give up`, `turn it off`, `come back` are ADV (not ADP) |
| Prepositions before gerunds | `before giving`, `of smoking`, `without asking` are ADP; SCONJ only before a clause with its own subject |
| `be` | VERB when it is the main verb (copula, existence, location, identity); AUX only when it helps another verb (progressive, passive) |
| `have`, `do` | AUX only as helpers (perfect; question, negation, emphasis); VERB otherwise |
| Modals | AUX; `need` and `dare` are AUX only before a bare verb without `to` |
| `to` | PART only as the infinitive marker; ADP everywhere else, including before a gerund |
| `not`, `n't`, possessive `'s` | PART |
| Relative `that` | PRON when it is the subject or object of its clause; SCONJ when the clause is complete without it |
| `no`, `all` before an adjective or adverb | ADV (`no better`); `the` before a comparative is ADV |
| Quantity words | `many, much, few, several, enough, more, most, less, such, other` before a noun are ADJ; DET is a closed list |
| Participles | VERB when an event is asserted; ADJ when a state is described (`a broken window`) |
| Names | PROPN only for names; members of a people or religion are NOUN; language names are PROPN |
| Numbers | cardinals NUM; ordinals ADJ; `one` is NUM when it counts and PRON when it stands for a noun |
| Contraction stems | the stem gets the tag of the word it stands for: `ca` AUX (can), `wo` AUX (will), `ai` like `be` |

Two limitations apply to the open corpus itself and are inherited by everything trained on it: its annotations were
written by language models (about 95 to 96 percent agreement with UD English-EWT after the convention mapping below),
and 36 percent of its training tokens are `unchecked` (no second annotator) and 3.6 percent `disputed` (the two
annotators disagreed). Guidance from the corpus: mask or down-weight `disputed` tokens when training.

## File Formats

### Corpus Block Format

The open corpus, MASC, the silver data and the STE challenge set share one format (UTF-8, no BOM, LF newlines, NFC,
final newline). A file starts with header comment lines, then a blank line, then one block per sentence:

```text
# corpus = ...
# license = ...
# columns = form, upos, status[, source_tag]

# sent_id = <unique id>
# source_id = <document id>
# text = <sentence>
<form> TAB <upos> TAB <status> [TAB <source_tag>]
<blank line between sentences>
```

- A comment line is a line that starts with a hash followed by a space. A token whose form is `#` is followed by a tab.
- `sent_id`, `source_id` and `text` are always the first three comment lines of a block; extra comment lines
  (`# genre = ...`, `# selection = ...`) may follow.
- `text` is the original sentence for the open corpus and the STE set. For MASC and silver it is the tokens joined by
  single spaces (the original text is not kept). The tokens of a sentence always concatenate to its text without spaces.
- `status` vocabulary: open and STE `unanimous | disputed | unchecked`; MASC `ptb`; silver `silver | masked`.
- `source_tag` (MASC, silver) records what the tag was derived from, so the derivation is reproducible from the file.

### Other Formats

The shape probe and gold v2 keep the column sets of their source (see `data/eval/*/README.md`). `lexicon.tsv` is
`spelling TAB TAG TAG ...` (see `data/lexicon/README.md`). All TSV files may start with comment lines (hash and space). The
`MANIFEST.tsv` lists size and SHA-256 of every data file except Markdown.

## Tokenization Convention

The convention of the open corpus (UD-like): contractions are split (`do` + `n't`, `ca` + `n't`, `I` + `'ve`,
`John` + `'s`, `can` + `not`), punctuation is separate, hyphenated compounds stay whole (`well-known`), `o'clock` stays
whole. MASC (Penn Treebank tokenization) follows it already. The data is **not** rejoined (the spike for the earlier
WordSense tagger rejoined clitics; do not). A tokenizer for this library must emit this convention.

### Tokenizer Rules

The `Tokenizer` of the library (`src/DemaConsulting.PartOfSpeech/Tokenization/Tokenizer.cs`) implements the rules below.
They were derived from the open corpus, where the `# text` line holds the original sentence next to the gold tokens, and
the test suite requires that at least 99.8 percent of the 14,980 open-corpus sentences are tokenized exactly like their
gold tokens (the measured value is 99.907 percent). Rules are applied to each white-space delimited chunk of the text
(any `char.IsWhiteSpace` character separates chunks). Tokens are slices of the source, so characters are never changed.

| Rule | Behavior | Examples |
| --- | --- | --- |
| Chunks | Tokens never contain white space; surrogate pairs stay whole | `a<TAB>b` gives `a` `b` |
| Leading punctuation | `( [ { <`, `"`, `` ` ``, `$`, `£`, `€`, guillemets and typographic quotes are tokens | `"Hi` `$700` |
| Leading apostrophe | Split off, unless it begins `'s 'm 're 've 'll 'd 'em 'tis 'twas` | `'Tis` stays whole |
| Trailing punctuation | `, ; : ! ? ) ] } " ' % >`, `` ` `` and typographic quotes are tokens, repeatedly | `73%` `hens'` |
| Periods | A period stays attached to a known abbreviation, a dotted acronym and a single initial | `Mr.` `U.S.` `p.m.` `J.` |
| Sentence-final period | Split from the last chunk of the text and from `I`; a quoted letter keeps it | `home.` `I.` `"a."` |
| Dot runs | A run that is a whole chunk is one token per dot; three dots after a word are one token | `...` `Wait` `...` |
| Four dots | A period followed by an ellipsis token | `men....` gives `men` `.` `...` |
| Inner dots | An ellipsis inside a word, or in front of it, stays attached | `as...as` `...a` |
| Dashes | `--` runs and U+2013, U+2014, U+2015 and U+2026 are tokens of their own, also inside a chunk | `forest--at` |
| Hyphen | A single hyphen stays inside the word | `well-known` `COVID-19` `-ing` |
| Negations | `n't` is split off a stem of letters; `ca`, `wo`, `sha`, `ai` and `can` + `not` are irregular | `do` `n't` |
| Clitics | `'s 'm 're 've 'll 'd` are split off a stem of letters and apostrophes; case is kept | `John` `'s` `CA` `N'T` |
| Apostrophe forms | U+0027, U+2018, U+2019, U+201B and U+02BC all count as an apostrophe; the token keeps its character | `don’t` |
| Whole words | A stem with digits or periods, and words such as `o'clock`, `gonna`, `D'Artagnan`, stay whole | `1900's` |
| Compounds | Slash and ampersand words, numbers, times, ordinals and decades stay whole | `and/or` `R&D` `7:00` |
| Standalone symbols | A chunk that is only `/`, `&`, `+`, `*` or `-` is a token | `/` |
| Addresses | URLs (`http://`, `https://`, `www.`) and e-mail addresses are single tokens, without trailing punctuation | `a@b.com` |

Abbreviations that keep their period (not at the end of the text): the titles `Mr Mrs Ms Dr St Jr Sr No Prof Capt Gen
Col Sgt Lt Rev Hon Mt Ft Gov Sen Rep Pres Cmdr Adm Maj Cpl Pvt Supt Messrs` when capitalized, and `etc Inc Co Corp Ltd vs v
e.g i.e ft` in any case. The corpus has no URL tokens and no typographic characters, so the rules for addresses and for
typographic punctuation are design decisions that only fixtures test. The corpus keeps `£9` and `£70` and U+2015 inside
words whole, which the rules split like `$` and the other dashes, and `no.` at the end of a sentence keeps its period,
which the rules split because it is a common sentence end; these are the documented residue of the 99.9 percent.

Sentence boundaries (`SentenceSplitter`): a sentence ends after `.`, `!`, `?`, `...` or the ellipsis character, and the
quotes and brackets that touch it, when the next token starts with an upper-case letter, a digit or an opening quote or
bracket, or the text ends. A dotted acronym ends a sentence only before a typical sentence starter. Titles and initials
never end a sentence. An ellipsis before a lower-case word does not end a sentence. A blank line (two line breaks)
always ends a sentence and a single line break does not.

## Mapping Tables

### Open Corpus

Identity projection of the `upos` and `consensus` columns; lemma, collapsed tag and features are dropped.

### MASC: Penn Treebank Tag to UPOS

`tools/data/import_masc.py` (function `ptb_to_upos`) walks the bracketed trees (traces `-NONE-` removed) and maps every
pre-terminal using its Penn tag and its place in the tree. Only written genres are used, sentences of 3 to 70 tokens.

| Penn tag (context) | UPOS |
| --- | --- |
| `$`, `#`, `SYM`; any token that is one of `% + = < > @ ~ ^ \|` | SYM |
| `.`, `,`, `:`, brackets (`-LRB-`, `-RRB-`, `-LSB-`, `-RSB-`, `-LCB-`, `-RCB-`), quotation marks, `HYPH`, `NFP` | PUNCT |
| `FW`, `XX`, `GW`, `ADD` (addresses, URLs), `AFX`, `LS` (list markers) | X |
| `NN*` or `JJ*` whose form is a number written with digits | NUM |
| `NNP`, `NNPS` | PROPN |
| `NN` that is one of `something anything nothing everything someone anyone everyone somebody anybody everybody nobody none one ones others`; `NNS` `others` | PRON |
| `NN` `today tomorrow yesterday tonight` under an `NP-TMP` | ADV |
| other `NN`, `NNS` | NOUN |
| `PRP`, `WP`, `EX` | PRON |
| `PRP$`, `WP$`, `PDT` | DET |
| `DT`: `a`, `an`, `the` | DET |
| `DT`: `all`, `no`, `the` inside an `ADVP` or `ADJP` | ADV |
| `WDT` alone in its `WHNP` (relative `that`, `which`) | PRON |
| `DT` alone in its `NP` (`That is true`) | PRON |
| other `DT`, `WDT` | DET |
| `IN` whose parent is `SBAR`, word not one of `of with by about for in on at from to into over under` | SCONJ |
| other `IN` | ADP |
| `CC` | CCONJ |
| `TO` under a `PP` | ADP |
| other `TO` (infinitive marker) | PART |
| `POS` | PART |
| `RB`, `RBR`, `RBS`: `not`, `n't` | PART |
| other `RB`, `RBR`, `RBS` | ADV |
| `WRB` `when whenever while where wherever once` opening an `SBAR` (via `WHADVP`) that the treebank marks adverbial (`-ADV -TMP -LOC -MNR -PRP -CND -DIR`), or whose word is `while once whenever wherever`, or an unmarked `SBAR` that is neither inside an `NP` (relative clause) nor the complement of a verb that takes an indirect question (`ask know wonder tell learn decide see ...`) | SCONJ |
| other `WRB` (`how`, `why`, questions, relative `when`) | ADV |
| `RP` (particle) | ADV |
| `JJ`, `JJR`, `JJS` | ADJ |
| `MD` | AUX |
| `VB*` of `be`, `have`, `do` (including `'s 'm 're 've 'd ai`) with a `VP` sibling (helper use) | AUX |
| `VBG`, `VBN` under a `PP` (`including`, `regarding`, `according`, `given`) | ADP |
| `VBG`, `VBN` under an `ADJP`, or in an `NP` before a noun | ADJ |
| other `VB*` | VERB |
| `UH` | INTJ |
| `CD` | NUM |
| anything else | X |

Known mapping noise (the Penn tags themselves are used as given): names inside names; nominal gerunds; `please`, `yes`,
`no`, `well` tagged `UH` become INTJ where the open corpus often has ADV; quantity words tagged `DT` before a noun are
DET where the open corpus has ADJ; `so` tagged `CC` is CCONJ; unmarked `SBAR` clauses of `when`/`where` can be
misjudged. `data/masc/mapping-audit.tsv` lists the Penn-by-UPOS counts and the 30 most frequent disagreements with
the open training split.

### BERT Silver: Model Class to UPOS

The model labels each token of the reference tokenization with one of its 13 classes (N R V J B P C I D A, infinitive
`to` T, punctuation, other) and a probability. `tools/data/import_silver.py` (function `derive_sentence`) derives
UPOS from the class letter, the probability and the token. Rules are applied in this order:

| Model class or token | UPOS | Status |
| --- | --- | --- |
| class probability below 50 percent | `_` | masked |
| the model gave no answer: capitalized and not the first word of the sentence | PROPN | silver |
| the model gave no answer: any other word | `_` | masked |
| token with no letter or digit: one of `% $ + = < > # @ &` | SYM | silver |
| token with no letter or digit: any other | PUNCT | silver |
| token with a digit: a number (`3`, `1,000`, `3.5`, `10:30`) | NUM | silver |
| token with a digit: anything else (`3rd`, `B2B`) | `_` | masked |
| N, lower case | NOUN | silver |
| N, capitalized, lower-case form is a lexicon NOUN (sentence-initial or archaic capital) | `_` | masked |
| N, capitalized, lower-case form is not a lexicon NOUN | PROPN | silver |
| R | PRON | silver |
| V | VERB | silver |
| J | ADJ | silver |
| B | ADV | silver |
| P | ADP | silver |
| C: `and or but nor` | CCONJ | silver |
| C: `for yet so either neither both plus` | `_` | masked |
| C: any other word | SCONJ | silver |
| I | INTJ | silver |
| D | DET | silver |
| A | AUX | silver |
| T (`to` before a verb or auxiliary) | PART | silver |
| O (`not`) | PART | silver |

Contractions are split by the corpus tokenization after labeling; the stem takes the label of the joined token:

| Piece | UPOS | Status |
| --- | --- | --- |
| `n't`, `not` (from `cannot`) | PART | silver |
| `'ve`, `'ll`, `'d` | AUX | silver |
| `'s`, `'m`, `'re` | `_` | masked (possessive, helper and main verb cannot be told apart without a parse) |
| stem of `n't`: `is are was were ai has have had am be` | `_` | masked (helper or main verb) |
| stem of `n't`: other word labeled A (`do`, `ca`, `wo`, `sha`, `could`) | AUX | silver |
| stem `that`, `what` labeled D before a clitic | PRON | silver |
| any other stem | by the class table above | silver |

### UD English-EWT to the Corpus Conventions (evaluation only)

Nothing from UD English-EWT is committed. `tools/data/fetch_ud_ewt.py map` applies four rules to the **reference** tags
before a tagger is scored against them (the raw tag is kept next to the mapped one), because the corpus conventions
differ from UD:

| Rule | UD reference | Mapped to |
| --- | --- | --- |
| Possessive determiner | PRON with `Poss=Yes` and relation `nmod:poss` | DET |
| Copula | AUX with lemma `be` and relation `cop` | VERB |
| Phrasal-verb particle | ADP with relation `compound:prt` | ADV |
| Preposition before a gerund or noun phrase | SCONJ when the lexicon has the word but no SCONJ analysis for it, or relation `mark` with a gerund head | ADP |

### Gold v2: UPOS to the Gold Classes

Gold v2 labels one target word per sentence with one of 11 values (the ten word classes of the WordSense lexicon and
`Other`). An evaluation maps a predicted UPOS tag to the class:

| UPOS | Gold v2 class |
| --- | --- |
| NOUN, PROPN | Noun |
| VERB | Verb |
| AUX | Auxiliary |
| ADJ | Adjective |
| ADV | Adverb |
| ADP | Preposition |
| CCONJ, SCONJ | Conjunction |
| DET | Determiner |
| PRON | Pronoun |
| INTJ | Interjection |
| PART, NUM, PUNCT, SYM, X | Other |

The WordSense class set has no particle class, so gold v2 labels `not` as Adverb (8 of 8 targets) and the infinitive
`to` as Preposition (5 targets) or Other (3 targets). Evaluation code must report targets whose form is `to` or `not`
separately from the headline number; they are convention differences, not tagger errors.
