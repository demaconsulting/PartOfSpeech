<!-- markdownlint-disable MD013 MD060 -->
<!-- cspell:ignore Prenominal Santorini lexicalized -->

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
the model label was not trustworthy, so the token carries no training signal). A status can carry flags
(`unanimous+mentioned`); a flag is a fact about the word and never changes its tag (see "Corpus Block Format").

## Annotation Conventions

All training data follows the conventions of the open English POS corpus (the guideline is
[GUIDELINE.md, version 4](https://github.com/demaconsulting/WordSense/blob/6530de1928baf30f9a14446a54bfb5dd5f67b41c/corpus/open/GUIDELINE.md),
section 4), extended by the sections below the table. They differ from Universal Dependencies in documented ways,
which is why UD English-EWT is mapped before it is scored (see below).

**Precedence.** Where these conventions are silent, UD English practice decides: the
[UD English guidelines](https://universaldependencies.org/en/index.html) and the labels of the English-EWT treebank
(read to learn the practice, never copied). An existing convention in the table or in the mapping tables wins over
UD where the two differ. A case that still has no clear answer after the tests of a section gets the status
`disputed`; nobody settles it silently.

The decisions that matter most:

| Topic | Convention |
| --- | --- |
| Possessive words | `my, your, his, her, its, our, their, whose` before a noun are DET (not PRON) |
| Phrasal-verb particles | `give up`, `turn it off`, `come back` are ADV (not ADP) |
| Prepositions before gerunds | `before giving`, `of smoking`, `without asking`, `for helping` are ADP; SCONJ only before a clause with its own subject |
| `be` | VERB when it is the main verb (copula, existence, location, identity); AUX only when it helps another verb (progressive, passive) |
| `have`, `do` | AUX only as helpers (perfect; question, negation, emphasis); VERB otherwise |
| Modals | AUX; `need` and `dare` are AUX only before a bare verb without `to` |
| `to` | PART only as the infinitive marker; ADP everywhere else, including before a gerund |
| `not`, `n't`, possessive `'s` | PART |
| Relative `that` | PRON when it is the subject or object of its clause; SCONJ when the clause is complete without it |
| `no`, `all` before an adjective or adverb | ADV (`no better`); `the` before a comparative is ADV (`the more, the merrier`) |
| Quantity words | `many, much, few, several, enough, more, most, less, such, other` before a noun are ADJ; DET is a closed list; more in "Degree and Quantity Words" |
| Participles | decided by the ordered tests in "Prenominal Modifiers and Predicate Participles" |
| Names | PROPN only for names; members of a people or religion are NOUN; language names are PROPN; more in "Names and Labels" |
| Numbers | cardinals NUM; ordinals ADJ; `one` is NUM when it counts and PRON when it stands for a noun; more in "Numbers and Dates" |
| Contraction stems | the stem gets the tag of the word it stands for: `ca` AUX (can), `wo` AUX (will), `ai` like `be` |

Two limitations apply to the open corpus itself and are inherited by everything trained on it: its annotations were
written by language models (about 95 to 96 percent agreement with UD English-EWT after the convention mapping below),
and 36 percent of its training tokens are `unchecked` (no second annotator) and 3.6 percent `disputed` (the two
annotators disagreed). Guidance from the corpus: mask or down-weight `disputed` tokens when training.

### Mentioned Words

A word that is talked about rather than used is *mentioned*: `using the verb snowball`, `it and that in English`,
`the word Eureka`, `Sierra is a Spanish word`. Source: [UD, "Using a word vs. mentioning it"](https://universaldependencies.org/u/overview/morphology.html)
and the `Mentioned=Yes` feature of [UD English-EWT, issue 446](https://github.com/UniversalDependencies/UD_English-EWT/issues/446).

- **Tag** a mentioned word with the tag it gets in ordinary use, never NOUN because it is mentioned and never X: `snowball`
  VERB, `it` PRON, `Eureka` INTJ, `Sierra` NOUN (a common Spanish word, not a name).
- **Flag** every token of the mentioned word or phrase `+mentioned`. The quotation marks and other punctuation are not flagged.
- **Foreign.** Words of another language carry the flag `+foreign`. A single foreign word that is mentioned for its form
  or meaning gets the category it has in its own language (`Sierra` NOUN with `+foreign+mentioned`; `Eureka` INTJ).
  Foreign text used as running text inside an English sentence stays X (existing practice, as in UD English-EWT), with
  `+foreign`. X is never given to a word only because it is mentioned.

Ordered tests; stop at the first that answers:

1. Is the word talked about as a linguistic item (a spelling, a pronunciation, a translation, a term, a word choice)
   rather than used to say something about the world? Cues: quotation marks or italics around a word or short phrase,
   `the word X`, `the verb X`, `X in English`, `X is a Spanish word`. If not, it is an ordinary use: tag it normally
   and stop. A quotation of real or hypothetical speech is not a mention.
2. Tag the word as if it were used in its ordinary sense.
3. Closed-class words named in a list (`so` and `neither`, `it` and `that`) get each its own most frequent use when the
   mention gives no syntax to test; do not give the pair one shared tag.
4. A foreign word or phrase mentioned for its form or meaning: tag each word by its category in the donor language and
   add `+foreign` (with `+mentioned`).
5. X only for foreign running text and material that cannot be analyzed (gibberish, fragments); a mentioned word that
   can be analyzed is never X.
6. Annotators still disagree (for example speech quotation against mention): `disputed`.

### Prenominal Modifiers and Predicate Participles

Applies to an `-ing` or `-ed` word, or a zero-derived noun, that modifies a noun or follows a linking verb. Sources:
[Santorini, Treebank tagging guidelines, section 4.1](https://catalog.ldc.upenn.edu/docs/LDC99T42/tagguid1.pdf) ("JJ or VBG", "NN or VBG"),
[UD, ADJ: Nouns vs. Adjectives](https://universaldependencies.org/u/pos/ADJ.html) and the UD English-EWT labels. Stop at
the first test that applies:

1. Names the type or purpose of the head (`N for X-ing`, `a kind of`) → NOUN: `operating modes`, `landing gear`,
   `cooling system`, `bleed valve`, `torque wrench`.
2. A gradable, fixed property → ADJ: `a very exciting match`. **Gradable** means the word accepts a degree word
   (`very`, `so`, `too`, `quite`, `extremely`, `a little`) or a comparison (`more`, `less`, `-er`) with its meaning unchanged:
   `very confused`, `I'm a little confused`, `more surprised` pass; `was killed`, `very killed` do not.
3. An ongoing action of the head (`N that is X-ing`) → VERB: `evolving knowledge`.
4. A predicate participle:
   - after `be`: a passive, VERB with AUX `be` (`was killed`, `has been shown`), unless the participle is gradable
     (test 2); then ADJ with the copula VERB `be` (`was very confused`, `is more surprised than`);
   - after `become`, `feel`, `look`, `remain`, `seem`, `sound`: an overt `by` or `from` agent → VERB
     (`I feel attacked by this`); otherwise ADJ (`she seemed pleased`, `it looks broken`).
5. A hyphenated lexicalized unit → the dictionary category of the whole unit, typically ADJ: `a high-water mark`.
6. A noun coerced under degree or comparison → ADJ for that token: `more twilight`, `very London`.
7. Otherwise → `disputed`.

### Degree and Quantity Words

Source: UD English-EWT labels; the closed DET list of the table is unchanged.

| Case | Convention | Examples |
| --- | --- | --- |
| `more`, `most`, `less`, `least`, `much`, `enough` | ADV when modifying an adjective, adverb or verb; ADJ when quantifying a noun or standing for one | `more serious`, `works less` ADV; `more time`, `for more`, `not enough` ADJ; `good enough` ADV |
| `more than`, `less than` before a quantity | `more`, `less` ADJ; `than` ADP | `more than half`, `less than 20` |
| `at least`, `at most` | ADP, ADJ | `at least 5` |
| `a little` | DET, ADJ, before a noun, adjective, adverb or verb, and alone | `a little leaky`, `a little time`, `budge a little`; bare `little` is ADJ before a noun or alone (`little to do`) and ADV before a verb or participle |
| `late`, `else` | `late` ADJ, ADV only when it modifies a verb; `else` ADV | `the late 1980s`, `arrived late`; `who else` |
| Superlatives | `the` before a superlative adjective is DET; `the most`, `the least` before an adjective or adverb is DET ADV | `the longest`, `the best option`, `the most serious` |
| Superlative standing for a noun | ADJ | `do your best`, `all the best` |
| Quantity word before `of` | never PRON: `all, any, both, each, either, some` DET; `many, much, several, few, more, most, less, enough` ADJ; `none` PRON; `plenty`, `lots` NOUN | `Most of them`, `some of the`, `a lot of` (`a` DET, `lot` NOUN) |
| `half` | NOUN as a head; DET before `the`, `a`, `an` or a possessive; ADJ after `a`/`an`; ADV before an adjective, adverb or preposition | `half of the`, `a week and a half`; `half the visitors`, `half an hour`; `a half century`; `half naked` |

### Numbers and Dates

Source: UD English-EWT labels.

| Case | Convention | Examples |
| --- | --- | --- |
| `one of` + noun phrase | `one` NUM (it counts), also before a pronoun | `one of the`, `one of them` |
| `some` before a number | DET; `million`, `billion`, `thousand` after a number are NUM | `some 15 million` |
| Decades | NOUN, also with a prefix | `1990s`, `'90s`, `mid-1800s`, `the late 1980s` |
| Roman numeral after a name | NUM (a written-out ordinal stays ADJ) | `Henry II`, `Henry the Eighth` |
| List labels | item label: digits and numerals NUM, letters X; speaker label of a dialogue: PROPN; the punctuation around it PUNCT | `1)` NUM, `(a)` X; `A:` and `B:` in a dialogue PROPN |

### Multiword Prepositions and Related Patterns

Source: [UD English, `fixed`](https://universaldependencies.org/en/dep/fixed.html) and the English-EWT labels. The
words of the expression keep the tag of their own category and the last word is ADP:

| Expression | Tags |
| --- | --- |
| `due to`, `prior to`, `such as` | ADJ ADP |
| `instead of`, `next to`, `rather than` | ADV ADP |
| `because of`, `along with`, `out of`, `according to` | ADP ADP |
| `in front of` | ADP NOUN ADP |
| `thanks to` | NOUN ADP |
| `as well as` | ADV ADV ADP |

- Before a clause with its own subject the last word is SCONJ (`because of what they received`); before a gerund it is
  ADP (existing convention): `instead of having`, `such as respecting`.
- `according` stays ADP, as in every participial preposition (`including`, `regarding`, `given`); UD tags it VERB.
- A phrasal-verb particle stays ADV even in front of a preposition: `run out of`, `get along with` are ADV ADP; the
  preposition `out of` (`out of the window`, `out of order`) is ADP ADP.
- `as … as possible`: the first `as` is ADV, the degree word ADV or ADJ by its own use, the second `as` SCONJ before a
  clause or an elliptical clause and ADP before a noun phrase, `possible` ADJ: `as soon as possible`, `as big as a house`.
- `for` + subject + `to`-infinitive: `for` is SCONJ, the subject NOUN or PRON, `to` PART (`easier for students to
  organize`, `waited for him to return`). `for` is ADP before a noun phrase with no infinitive predicate of its own
  (`ways to vote`) and before a gerund (`thanks for helping`).

### Names and Labels

Source: UD English-EWT labels; [UD, PROPN](https://universaldependencies.org/u/pos/PROPN.html).

- A title directly before a name is part of the name: PROPN (`President Bush`, `Archduke Franz Ferdinand`, `Dr. Lee`).
  A capitalized title that stands for one particular person is PROPN (`the Marshal`, `the President`); lowercase and
  vocative uses are NOUN (`the president of the club`, `Yes, sir`).
- A capitalized common noun that names one particular entity, or a personification used as a name, is PROPN:
  `the City` (of London), `Black Care`. Inside a name the nouns and adjectives are PROPN and the function words keep
  their own tag (`University of Houston`: PROPN ADP PROPN).
- `AM` and `FM` as radio bands are PROPN; `a.m.`, `p.m.` and `AM`, `PM` after a clock time are NOUN.
- A fiscal-year token such as `FY2020` is NOUN.
- A language name is PROPN (existing convention), also as a modifier that names the language of the head (`English
  conversation`, `a Spanish word`); it is ADJ when it means of the people or place (`English tea`, `Spanish soldiers`).

### The `get`-passive

`get` + a past participle that asserts an event: `get` is AUX and the participle VERB (`got fired`, `get married`,
`got promoted`). When the participle is a gradable state (test 2 above), `get` is the main VERB and the participle ADJ
(`get depressed`, `get used to`).

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
- **Status flags.** The status cell may continue with flags, each after a plus sign: `status[+foreign][+mentioned]`
  (`unanimous+mentioned`, `unchecked+foreign+mentioned`). `mentioned` marks a word that is talked about rather than used
  and `foreign` a word of another language (see "Mentioned Words"). A flag never changes the tag, and a flag is
  not a status: masking, `disputed` handling and the lexicon use the status in front of the first plus sign. A flag
  appears at most once, in the order above, so a cell has one spelling; an unknown, repeated or out-of-order flag
  is an error. The optional `source_tag` column is unaffected, so every file without flags stays valid. The open
  importer (flags come in the consensus column of the source), the silver derivation (it recomputes the status and keeps
  the flags), the lexicon and coverage tools, and the readers of the trainer and the tests accept and keep the flags;
  the MASC importer derives every row from Penn trees and has no source for a flag. No committed file carries a flag
  yet; the corpus labels are corrected in a later change, and the corrections are staged in `data/corrections/` (see "Other
  Formats").
- `source_tag` (MASC, silver) records what the tag was derived from, so the derivation is reproducible from the file.

### Other Formats

The shape probe and gold v2 keep the column sets of their source (see `data/eval/*/README.md`). `lexicon.tsv` is
`spelling TAB TAG TAG ...` (see `data/lexicon/README.md`). All TSV files may start with comment lines (hash and space). The
`MANIFEST.tsv` lists size and SHA-256 of every data file except Markdown.

**Corrections overlay** (`data/corrections/`, specified in full in its `README.md`). Corrections to the labels of the open
corpus, MASC and the silver data are rows of three tables, not edits of the corpus files, until a later change applies them.
Each table starts with comment lines (`# corpus`, `# license`, `# kind`, `# columns`, `# generator`, `# input`), then one
row per line, sorted by the key `(source, split, sent_id, token_index)`, with these columns:

- `mechanical.tsv` (and `llm.tsv` for the adjudicated cases), kind `corrections`: `source, split, source_id, sent_id,
  token_index, token, old_tag, new_tag, old_status, new_status, provenance`
- `candidates.tsv`, kind `candidates` (cases that need a judgment; never a change): `source, split, source_id, sent_id,
  token_index, span_length, token, current_tag, current_status, source_tag, candidate_rule, hint, context`
- `exceptions.tsv`, kind `exceptions` (reviewed, keep as is): `source, split, source_id, sent_id, token_index, token, rule,
  reason, provenance`

Normative rules: `source` is `open`, `masc` or `silver`; `split` is `train`, `dev` or `test` for `open` and `-` otherwise;
`token_index` is the 0-based position among the token rows of the sentence and `token` is its form; tags are among the 17
and never the masked placeholder; `old_status` and `new_status` are whole status cells; a row changes the tag or the status
(the base status may change only to `disputed` and only for an `llm:` row; a `rule:` row keeps its status cell); the
provenance is `rule:<id>` (the id of a rule of the catalog in `data/corrections/README.md`) or `llm:<model>:<run>`; a token
has at most one correction row across all correction tables. A correction table is valid against the corpora when every row
matches them in the pending state (`old_tag`, `old_status`) or every row matches them in the applied state (`new_tag`,
`new_status`); a mixture is an error. The rules of the catalog implement the text of "Annotation Conventions"; where the
text and a rule disagree, this document governs and the rule is changed.

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

The held-out evaluation scores the mapped tags (the primary view) and the raw tags (a second view); how each evaluation
set is compared with the library's tags, and how to run the treebank evaluation locally, is in
[docs/evaluation/README.md](../docs/evaluation/README.md). This section stays the normative description of the rules.

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
separately from the headline number; they are convention differences, not tagger errors. The held-out report does so
(see [docs/evaluation/README.md](../docs/evaluation/README.md)).
