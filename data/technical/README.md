<!-- markdownlint-disable MD013 MD060 -->
<!-- cspell:ignore deverbal -->

# Technical-Text Training Sources

License: CC0-1.0

This folder holds the four training sources of the technical-text shapes of beta.3 (deverbal compound heads, capitalized defined terms,
capitalized step imperatives and base-form ambiguity in a clause), their dropped-sentence and excluded-document records and the shared
`term-conflict-ids.tsv`. The four sources are built from new US federal documents by `tools/data/technical_data.py`; the data commit
that registers them completes this README (provenance, counts, the labeling and decision rule, the hand check, the leakage and license
checks, the lexicon coverage and the known limits, in the layout of [`data/compound/README.md`](../compound/README.md)). At this
commit the folder holds only the sample of ids below.

## Divergence from UD Practice

The labeling convention has a section "Capitalized Defined Terms" ([`data/CONVENTIONS.md`](../CONVENTIONS.md)): a capitalized term made of
ordinary English words that names a kind, mode, setting, position, state, screen, document type or component class is NOUN (ADJ for an
adjectival modifier) and not PROPN; PROPN stays for a name of one particular entity. The practice of UD English-EWT, of the open corpus
and of the other corpora is not uniform for such terms, so the existing labels disagree with the rule by an amount that this section
counts. **Nothing is relabeled**: the counts only describe the distance between the rule and the labels that exist.

**The detector** (`tools/data/term_divergence.py`, function `defined_term_conflicts`, frozen at commit `67d7dc2` and not changed
afterwards) looks at runs of 1 to 4 consecutive Title Case tokens (`[A-Z][a-z]+`) inside a sentence (not at its first position, not after
`:` or a quote) in which every word is an ordinary English word of the lexicon (NOUN or ADJ, not only PROPN, no title word). A run whose
last word is an organization or place head (`Agency`, `Laboratory`, `River`, ...) is **org-like**: it is a name under the rule and never a
conflict. Class **A** (cue based, high precision) holds the runs with a right cue (a lower-case kind noun such as `mode` or `screen` after
the run), a document-type head (`Schematic`, `Checklist`, ...) or a left verb cue (`select`, `see`, ...); class **B** (broad) holds the
other runs of two or more words. For each run the tag of its last word is counted: PROPN is a **conflict** with the rule, NOUN and ADJ are
**agreement**, any other tag is **other** (in `silver` the tag `_` of a masked token).

**Training sources** (`python tools/data/term_divergence.py count-training`; the counts are of runs, not of sentences):

| Source | Sentences | A runs | A PROPN | A NOUN | A ADJ | A other | B runs | B PROPN | B NOUN | B ADJ | B other | Org-like runs | Org-like PROPN |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `open` (train) | 11,990 | 26 | 21 | 2 | 3 | 0 | 858 | 796 | 44 | 16 | 2 | 263 | 261 |
| `masc` | 22,829 | 41 | 20 | 14 | 7 | 0 | 1,710 | 1,107 | 538 | 44 | 21 | 395 | 353 |
| `silver` | 13,971 | 14 | 0 | 0 | 8 | 6 | 405 | 1 | 0 | 9 | 395 | 86 | 0 |
| `imperative` | 900 | 1 | 0 | 0 | 0 | 1 | 2 | 1 | 1 | 0 | 0 | 3 | 3 |
| `compound` | 742 | 5 | 3 | 1 | 1 | 0 | 40 | 37 | 3 | 0 | 0 | 10 | 10 |
| `compound2` | 1,100 | 5 | 2 | 3 | 0 | 0 | 33 | 31 | 2 | 0 | 0 | 19 | 19 |
| `headings` | 374 | 3 | 0 | 3 | 0 | 0 | 101 | 6 | 94 | 0 | 1 | 8 | 7 |
| `headings2` | 247 | 7 | 0 | 7 | 0 | 0 | 82 | 2 | 78 | 0 | 2 | 2 | 0 |

The `silver` rows are almost all `_`: the silver derivation masks a token for which the two off-the-shelf taggers gave no usable answer
(`data/silver/README.md`), so most capitalized runs there carry no training signal.

**Sample of conflicting runs** (`term-conflict-ids.tsv`, 156 rows, ids only): per source and class a seeded sample of at most 25 of the
runs counted as conflicts (class A and B, never org-like), ordered by `rank_of(20261110, "term-conflict", source, sentence id)`. The columns
are `source`, `class`, `sent_id` and the zero-based `token_index` of the last word of the run. The rows are 21, 20, 3, 2 of class A in
`open`, `masc`, `compound`, `compound2` and 25 of class B in each of `open`, `masc`, `compound` and `compound2`, 6 in `headings`, 2 in
`headings2` and 1 each in `imperative` and `silver`.

**Held-out sets** (`python tools/data/term_divergence.py count-held-out`, the only code path that opens a held-out file; it prints
integers only, no list option exists, and nobody opened these files in any other way):

| Set | Sentences | A runs | A PROPN | A NOUN | A ADJ | A other | B runs | B PROPN | B NOUN | B ADJ | B other | Org-like runs | Org-like PROPN |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `open/dev.tsv` | 1,489 | 1 | 0 | 1 | 0 | 0 | 101 | 93 | 8 | 0 | 0 | 27 | 27 |
| `open/test.tsv` | 1,501 | 2 | 2 | 0 | 0 | 0 | 117 | 102 | 8 | 7 | 0 | 29 | 29 |
| UD English-EWT dev (raw UPOS) | 2,001 | 3 | 1 | 0 | 2 | 0 | 114 | 84 | 24 | 5 | 1 | 29 | 23 |
| UD English-EWT test (raw UPOS) | 2,077 | 8 | 4 | 3 | 1 | 0 | 148 | 100 | 41 | 5 | 2 | 13 | 11 |

**What the numbers mean.** The rule is rare in running text: the cue-based class A has between 1 and 41 runs per source, and class B
holds about 7 runs per 100 sentences in `open` and `masc` and 6 to 8 per 100 sentences in the held-out sets. Where such a run exists,
the existing labels mostly say PROPN: 93% of the class B runs of `open` (train), 92% and 87% of those of `open` dev and test, 65% of those of
`masc`, and 74% and 68% of those of UD English-EWT dev and test; for class A the numbers are too small to give a share. The heading
sources (`headings`, `headings2`) agree with the rule (NOUN) because they were labeled by the heading rules of PR #19 and #20. A model
trained on `open`, `masc` and the UD practice therefore learns PROPN for a Title Case word in the middle of a sentence far more often
than the rule asks, and the held-out figures of those sets would count a NOUN answer for a defined term as an error.

**What the numbers do not mean.** They are neither an error rate of the corpora nor a count of wrong labels: class B is a broad,
position-and-lexicon selection (a Title Case work title, a person name made of ordinary words or a heading is in it too, and for those
PROPN may be right), and a verdict says only that the tag of the last word differs from what the rule asks for a defined term. The
detector reads no meaning. The held-out counts were used for no choice (no threshold, selection or relabeling), and a change of the
detector after a count would be a new, disclosed version.
