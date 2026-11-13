<!-- markdownlint-disable MD013 MD060 -->
<!-- cspell:ignore airforce coastguard usbr usfs -->

# Technical-Text Training Sources

License: CC0-1.0

This folder holds four **training** sources of the technical-text shapes (deverbal compound heads, capitalized defined terms, capitalized
step imperatives and base-form ambiguity in a clause), built from US federal-government documents, together with their dropped-sentence
and excluded-document records and the shared `term-conflict-ids.tsv`. Each sentence has one judged word in the frame of its shape, and
every token has a Universal POS tag on which two blind model labelers agree. The four sources are **training** data: they hold the
patterns that the other training corpora hold rarely, and each has a contrast stratum in which the same kind of word has the other
reading. They are disjoint from every evaluation set and from every other training source, and share no sentence with each other. The
four sources have weight 0 in the trainer configuration (`training.json`): they are committed training data that are not part of the
shipped mix, and `--source deverbal:1` (and so on) puts one into an experimental run.

| Source | File | Sentences | Tokens | Lemmas | Documents | Strata (sentences) |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| `deverbal` | `deverbal.tsv` | 637 | 12,007 | 376 | 173 | `pred` 136, `num` 160, `ing` 146, `other` 100, contrast `verb` 95 |
| `terms` | `terms.tsv` | 373 | 7,472 | 241 | 102 | `noun` 226, `adj` 54, contrast `proper` 93 |
| `steps` | `steps.tsv` | 531 | 4,894 | 292 | 130 | `imperative` 372, contrast `noun-initial` 159 |
| `clauses` | `clauses.tsv` | 310 | 5,870 | 190 | 112 | `main` 187, `sub` 61, contrast `noun` 34, `adj` 28 |

The four files hold 1,851 sentences and 30,243 tokens. The sizes are the sizes of the files: the sentence counts above are what each source
contains. The record of the builds is in the trainer run log (sections "beta.3 phase 1").

## Source and Provenance

- **Source:** the sentences are text of US federal-government works (17 U.S.C. 105), retrieved from the NASA Technical Reports Server
  (NTRS), the USGS Publications Warehouse and the Internet Archive: publications of the National Aeronautics and Space Administration, the
  Geological Survey, the Army, Navy, Air Force, Coast Guard and Marine Corps, the Environmental Protection Agency, the Department of Energy,
  the Forest Service, the National Park Service, the Bureau of Reclamation, the National Bureau of Standards, NOAA and the Department of
  Agriculture (the families of the table under "Counts"). Every
  document is one row of the `*-sources.tsv` table of its source (URL, title, author, year, the quoted license basis, retrieval date and
  the SHA-256 of the download and of the extracted sentences). No document is in any other source table of this repository, none is in an
  evaluation set, none is in either `excluded-sources.tsv`, and nothing was written for these sources: all sentences are real text
  (0 authored). The documents were retrieved on 2026-11-11 and 2026-11-12; publication years are 1940 to 2025 (every document is from 1940
  or later).
- **Documents of the four sources:** `deverbal` 173, `terms` 102, `steps` 130 and `clauses` 112, 218 distinct documents in all. The four
  sources are cut from one pool of documents (the training side of one partition of title families, see below), so a document may feed
  more than one of them; the sentences are never shared. The development tranches of the same shapes
  (`data/eval/shapes-dev/`) are cut from the other side of the partition: no document is in both.
- **Frozen inputs:** the inputs of the builds are pinned by SHA-256 in `tools/data/technical_data.py` and checked before every candidate
  round: the draft document list `b48d463364275182f4e704625d069b9c9ef06f48ca068405925c06ab0be142ae`, the tokenized units
  `dfe0ba191496d53727c1dcea8d2d5e0841a7885a4ad9c489e7f8b81f6f7ef4aa`, the classifier mass of every sentence for the band of the development
  products (`hmm.tsv`) `43432b1a2522bf6e85b7a6e380dfaa4d94a5b26a066e94fa0b8c127c9bf602f7` and `data/CONVENTIONS.md`
  `42da154c60272dc9a105c5496fa3c8797cffa81cfb55d34505497b33520e5bb2`.
- **How produced:** `python tools/data/technical_data.py --work <work-folder> build --product deverbal|terms|steps|clauses --allow-short`
  at the builder and the pre-registered rule of the commits recorded in the trainer run log (section "beta.3 phase 1: pre-registered
  rule"; the normative text is the docstring of `technical_data.py` and, for the frames, of `technical_frames.py`). The labeling used the
  unchanged commands of `tools/data/shapes_dev.py` (`export`, `check-batches`, `validate-output`, `status`, `decide`) with the rounds
  `dvt` (`deverbal`), `dtt` (`terms`), `stt` (`steps`) and `clt` (`clauses`). The global `--work` argument (a folder outside the
  repository, required) comes before the command. The work folder is not committed; it holds the extracted text, the tokens of every
  unit, the candidates, the blind batches, the two labeler outputs and the private decisions. The scripts that found and fetched the
  documents are one-off helpers and are not committed; the record of what was fetched is the `*-sources.tsv` tables. The files are never
  edited by hand.
- **Labels:** written by two language models that followed [CONVENTIONS.md](../CONVENTIONS.md) and saw only a batch of opaque ids and
  tokens: `claude-opus-5.5` and `gpt-6.1-sol`. A sentence is kept only when the two agree on the tag of every token. **No human reviewed a
  label.** The labels and the selection are dedicated to the public domain under CC0 1.0 (the position on the rights in model outputs is
  stated in the repository README and is not legal advice).
- **Sentences:** public domain in the United States as works of the federal government; the basis of every document is quoted in its
  row of the sources table (the statute, the agency named in the record, the record's rights statement where it has one, and the check
  that the text carries no copyright, permission, third-party credit or distribution restriction). There is no FAA text, no Wikipedia, no
  share-alike or non-commercial text, no Universal Dependencies text and no document with a state, local, foreign, university,
  contractor or private creator, publisher or co-author. `check_licenses.py` scans the four tables.

## Files

| File | Content |
| --- | --- |
| `deverbal.tsv`, `terms.tsv`, `steps.tsv`, `clauses.tsv` | the sentences of the four sources in the corpus block format of [CONVENTIONS.md](../CONVENTIONS.md) ("Other Formats" describes the header and the extra comments); ids `deverbal-NNNN`, `terms-NNNN`, `steps-NNNN`, `clauses-NNNN` |
| `deverbal-sources.tsv`, `terms-sources.tsv`, `steps-sources.tsv`, `clauses-sources.tsv` | the documents that the sentences of each source come from (only used documents): family, title, URL, author, year, license basis, terms URL, retrieval date, hashes, sentences used |
| `deverbal-dropped-sentences.tsv`, `terms-dropped-sentences.tsv`, `steps-dropped-sentences.tsv`, `clauses-dropped-sentences.tsv` | counts of candidate sentences dropped alone because they equal, or nearly equal, a sentence of a training or evaluation set, or share a word 5-gram with the shape probe (counts only, no text): `deverbal` 1 (5-gram), `terms` 1 (5-gram), `steps` 98 equal to a set sentence, 33 equal to a shapes development sentence and 1 (5-gram), `clauses` 1 (5-gram) |
| `excluded-documents.tsv` | documents left out of the four builds: none (header only) |
| `term-conflict-ids.tsv` | ids only of conflicting capitalized runs in the older sources (see "Divergence from UD Practice") |

Each sentence block has the comments `sent_id`, `source_id`, `text`, `word`, `lemma` and `stratum`, then one row `form`, `upos`, `status`
for each token. `word` is the zero-based token index of the judged word, `lemma` its lower-case form and `stratum` the stratum below.
`terms.tsv` also has `term` (the lower-case words of the capitalized term) and `steps.tsv` has `frame` and `unit` (see the strata). Every
status is `unanimous`, with the flags `+foreign` and `+mentioned` where both labelers agreed on them. Sentences have 4 to 30 tokens (the
lines of `steps` from 2); at most 20 sentences come from one document (`clauses` 15).

## The Shapes and Strata

The judged word of a sentence is fixed by position and lexicon only (`tools/data/technical_frames.py` holds every frame, word list and
stratum); no model of this project takes part. Each source has the following strata, and a sentence is kept when the judged word has the
exact tag of its stratum.

- **`deverbal`** (S1): a noun-noun compound whose head is spelled like a verb (a zero-derived noun such as `check`, or an `-ing` gerund
  noun), the head lower case and ending its compound. The stratum is the position of the compound: `pred` (the governor is a form of *be*,
  *become* or *remain*), `num` (a numeral stands before the compound), `ing` (a gerund head in any other position) and `other` (a
  zero-derived head after a determiner, in subject or object position). The head is tagged NOUN (or PROPN, the class Noun). The contrast
  stratum `verb` has the lemma of a target used as a verb (after a modal or `to`, as the first word of an instruction, or as an `-ing`
  verb) and tagged VERB. 95 `verb` sentences of 637 (14.9 percent); at most 4 sentences per head lemma and 20 per document.
- **`terms`** (S2): a run of 1 to 4 consecutive Title Case ordinary words inside a sentence (not at its start, not after a colon or a
  quote, not followed by a number or a one-letter label). The judged word is the last word of the run for `noun` (tagged NOUN) and `proper`
  (tagged PROPN), and the first word for `adj` (a run of two or more words whose first word is an adjective, tagged ADJ). Strata `noun` and
  `adj` follow the convention "Capitalized Defined Terms" below; the contrast stratum `proper` holds runs with a word the lexicon lists only
  as a proper name, or that end in an organization or place head (names of one particular entity; 24.9 percent of the file). 326 distinct
  terms (at most 2 sentences per term), at most 4 per lemma.
- **`steps`** (S3): lines that start with a capitalized word in step lists, enumerated or bulleted items, short stand-alone lines (2 to 12
  words) and instruction sentences. Stratum `imperative`: the first word is a base-form verb tagged VERB, followed by an object or
  complement (the frame: `bare` lower-case object, `det`, `prep`, `num`, `pron`, `particle`, `adv`); 372 sentences: 158 sentence units, 126
  enumerated, 60 short stand-alone and 28 bulleted lines, frames `bare` 140, `det` 161, `prep` 33, `num` 24, `pron` 6, `particle` 6, `adv`
  2. The contrast stratum `noun-initial` (the first word is a noun, as in a caption or a table cell: a short line of 2 to 6 tokens that
  starts with a capitalized noun-capable word) has 159 sentences (29.9 percent of the file), all short stand-alone lines. At most 6
  imperatives per lemma.
- **`clauses`** (S4): a lower-case base-form word that can also be a noun or an adjective, right after a plural or coordinated subject.
  Strata `main` (a main-clause verb, 187) and `sub` (a subordinating conjunction stands within 5 tokens before the subject, 61) are tagged VERB; the
  contrast strata `noun` (34: the same subject frame, the word followed by a lower-case noun and tagged NOUN) and `adj` (28: a determiner,
  the word and a plural noun, tagged ADJ) are 20.0 percent of the file. 41 of the 248 targets (16.5 percent) have a direct object without
  a determiner after the verb (the flag `np`; at most 60 percent of the targets). At most 4 per lemma.

## Counts

| Measure | `deverbal` | `terms` | `steps` | `clauses` |
| --- | ---: | ---: | ---: | ---: |
| sentences | 637 | 373 | 531 | 310 |
| tokens | 12,007 | 7,472 | 4,894 | 5,870 |
| distinct judged lemmas | 376 | 241 | 292 | 190 |
| documents | 173 | 102 | 130 | 112 |
| most sentences per lemma (targets) | 4 | 4 | 6 | 4 |
| most sentences per document | 20 | 20 | 20 | 15 |

| Family | `deverbal` | `terms` | `steps` | `clauses` |
| --- | ---: | ---: | ---: | ---: |
| `nasa` (documents / sentences) | 85 / 213 | 39 / 93 | 58 / 127 | 40 / 64 |
| `navy` | 12 / 73 | 11 / 49 | 11 / 95 | 12 / 64 |
| `army` | 17 / 119 | 10 / 44 | 10 / 67 | 16 / 73 |
| `usgs` | 16 / 60 | 10 / 37 | 11 / 42 | 12 / 21 |
| `usfs` | 19 / 55 | 10 / 26 | 15 / 53 | 12 / 29 |
| `airforce` | 8 / 40 | 5 / 28 | 7 / 47 | 8 / 21 |
| `coastguard` | 2 / 24 | 2 / 11 | 2 / 40 | 2 / 14 |
| `epa` | 6 / 31 | 6 / 37 | 6 / 17 | 3 / 7 |
| `nps` | 2 / 5 | 3 / 28 | 2 / 7 | 1 / 2 |
| `marine` | 1 / 7 | 1 / 1 | 1 / 20 | 1 / 3 |
| `nbs` | 3 / 6 | 1 / 2 | 3 / 7 | 1 / 6 |
| `doe` | 1 / 3 | 2 / 9 | 2 / 2 | 2 / 4 |
| `noaa` | | 1 / 5 | 1 / 3 | |
| `usbr` | | 1 / 3 | 1 / 4 | 1 / 1 |
| `usda` | 1 / 1 | | | 1 / 1 |

## Labeling and Decision Rule

The docstring of `tools/data/technical_data.py` is the normative text of the rule, and the trainer run log, section
"beta.3 phase 1: pre-registered rule (before any fetch)", is its short form.

1. **Documents.** US federal authorship, with the public-domain basis checked one document at a time and written in the `license_basis`
   cell. No document is in any table of used sources (training sources, evaluation sets, the shapes development set, the imperative,
   compound and headings sources, and every earlier sourcing), in either `excluded-sources.tsv` or any `excluded-documents.tsv`; a
   document that shares 3 or more sentence keys with a training or evaluation set is rejected as a whole.
2. **Partition.** Every document gets a family key (the lower-case title without volume, edition, year and series number); a family is on
   the development side when a seeded rank of its key is below 0.40. All members of a family are on one side: the training sources hold
   the training side only and the development tranches the other.
3. **Candidates.** A sentence is a candidate when it passes the cleanliness filters of `shapes_dev.py` (one sentence, 3 to 30 tokens, no
   OCR junk, citation, enumeration, title or repetition noise; step units 2 to 30 tokens with the noise rules of the step frame), equals no
   training, evaluation or shapes development sentence, shares no word 5-gram with the held-out shape probe, and has the position and
   lexicon frame of its shape (one candidate per sentence, the rarest stratum first, in a seeded order). No model score takes part, and
   no band: the training candidates are not selected by any classifier.
4. **Labels.** Every candidate is tagged from scratch, token by token, by the two blind labelers through the batch commands of
   `shapes_dev.py`; the batch files carry an opaque id, a token index and the token, and the labelers see no shape, source, lemma or
   intended class.
5. **Kept sentences.** A candidate is kept when both labelers agree on every token (no disputed token), when the convention linter reports
   no fix-tier violation on it, when the judged word has the exact tag of its stratum (`deverbal`: the class Noun for `pred`, `num`, `ing`
   and `other`, VERB for `verb`) and when the frame still holds in the labeled sentence. Anything else is dropped and counted; no label is
   changed by hand.
6. **Caps.** In a seeded order the build takes a kept sentence unless its lemma already has 4 sentences (6 for `steps`), its document
   already has 20, its term already has 2 (`terms`), or its stratum is full (`deverbal`: `pred` 250, `num` 200, `ing` 300, `other` 200;
   `terms`: `noun` 550, `adj` 250; `clauses`: `main` 450, `sub` 300); the contrast shares are applied last (`verb` at most 15 percent,
   `proper` 15 to 25 percent, `noun-initial` at most 30 percent, `noun` and `adj` together at most 20 percent).
7. **Quota gate.** `build` refuses a product below its floors unless `--allow-short` is given and never waives a cap; it checks that
   every token is `unanimous`, that the judged word has its stratum's tag and that the tokens rejoin to the text. A data test asserts the
   format, the caps of item 6 and the sizes as ranges: `deverbal` 450 to 800 sentences with 146 to 300 of `ing`, `terms` 373 to 900,
   `steps` 531 to 1,000 and `clauses` 310 to 800.

Numbers of the builds (items with a decision that the build read, and what it dropped; counts only):

| Source | Items read | Dropped: class mismatch | disputed token | target disagreement | frame mismatch | caps (total or stratum, lemma, term, document) | **Sentences** |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `deverbal` | 1,115 | 291 | 113 | 8 | 21 | 8 + 33 + 4 = 45 | **637** |
| `terms` | 808 | 153 | 98 | 57 | 0 | 111 + 5 + 11 = 127 | **373** |
| `steps` | 1,500 | 828 | 41 | 31 | 0 | 17 + 52 = 69 | **531** |
| `clauses` | 860 | 385 | 66 | 6 | 7 | 68 + 18 = 86 | **310** |

The labeling record (refusals, repairs and the decisions per round) is in the trainer run log, section "beta.3 phase 1: labels decided".

## Hand Check

A third model, `claude-sonnet-5.5`, reviewed 60 random sentences of each source as built (one blind agent per source, drawn once with a fixed
seed: `deverbal` 20261205, `terms` 20261206, `steps` 20261207, `clauses` 20261208) against the conventions. It saw each sentence with every token
and its tag and judged the marked word (and marked any other wrong token). This is a **model review, not a human review**, and it is not
independent of the labelers' shared blind spots. The gate was a marked-word precision of at least 95 percent (3 incorrect of 60 passes, 4 fail)
and at most 1 of 4 swapped-tag control items accepted.

| Measure | `deverbal` | `terms` | `steps` | `clauses` |
| --- | ---: | ---: | ---: | ---: |
| items reviewed | 60 | 60 | 60 | 60 |
| marked word correct / incorrect / unclear | 59 / 0 / 1 | 59 / 0 / 1 | 60 / 0 / 0 | 59 / 0 / 1 |
| marked-word precision (gate at least 95%) | 100.0% | 100.0% | 100.0% | 100.0% |
| whole item correct (no other token marked wrong) | 98.3% | 96.7% | 100.0% | 98.3% |
| token precision (reported, not gated) | 100.00% (0 of 1,104 tokens wrong) | 99.92% (1 of 1,247) | 100.00% (0 of 515) | 100.00% (0 of 1,184) |
| swapped-tag controls accepted (gate: at most 1 of 4) | 0 of 4 | 0 of 4 | 0 of 4 | 0 of 4 |
| unanswered items | 0 | 0 | 0 | 0 |
| gate | passed | passed | passed | passed |

One sample per source was reviewed.

## Capitalized Defined Terms (the `terms` Convention)

[CONVENTIONS.md](../CONVENTIONS.md) has a section "Capitalized Defined Terms": a capitalized term made of ordinary English words that names a
kind, mode, setting, position, state, screen, document type, diagram type or component class is NOUN (ADJ for an adjectival modifier of a
following noun), and PROPN is reserved for a name of one particular entity (an organization, a coined product name, a place, a person, a named
program) or a coined non-English label. The labelers of `terms` and of the development tranche `defined-terms` read that section, and the
stratum `proper` of `terms` holds the names that the section keeps as PROPN. The linter does not enforce the section (no rule of
`convention_rules.py` addresses it); the section "Divergence from UD Practice" below counts how far the labels of the other corpora are from
it (the counts are those of the trainer run log, section "beta.3 phase 1: divergence of the Capitalized Defined Terms rule from the existing
labels"), and nothing in the other corpora was relabeled. Of the 373 sentences of `terms`, 226 have a NOUN term, 54 an ADJ term and 93 a PROPN
name.

## Leakage and License Checks

`python tools/data/check_leakage.py` compares each source with every evaluation set (open dev and test, gold v2, the shape probe, all the
tranches of the shapes development set including the four technical ones, the STE challenge set, and UD English-EWT train, dev and test when
the git-ignored cache is present; the probe is reached through its sentence column only) and with the other training sources (open train,
MASC, silver, imperative, compound, headings, compound2 and headings2). Result: **0 document overlaps, 0 exact sentence overlaps and 0 near
duplicates** for every pair of one of the four sources and an evaluation set, and for every pair of one of the four sources and a training
source outside the four. The four sources share documents with each other (one pool; `deverbal`/`terms` 82, `deverbal`/`steps` 105,
`deverbal`/`clauses` 93, `terms`/`steps` 71, `terms`/`clauses` 59, `steps`/`clauses` 69; reported, not gated) and no sentence and no near
duplicate; [leakage-report.tsv](../leakage-report.tsv) and [training-leakage-report.tsv](../training-leakage-report.tsv) have the counts.
`check_licenses.py` scans the four sources tables, the excluded-source records and this README. `check_conventions.py` lints the four
files (lint only: the corrections overlay never addresses them): the fix tier finds no violation, which the lint run enforces, and the
check tier reports 3 hits in `deverbal` (`be-participle-adj-not-gradable`, left as the labelers tagged them) and none in `terms`, `steps`
and `clauses`.

## Known Limits

- **Model labels, no human review.** Two models agree on every token, which removes the sentences where they differ, not the errors that
  they share. The model review (see Hand Check) is a sample, not a human review; the residual error of the sources is not measured by a
  human.
- **No hard-case selection.** The pool is every frame hit in a seeded order; no classifier score chooses it. The sentences are therefore
  easier on average than the development tranches, which kept the contested band of a classifier for `deverbal-heads`, `step-lines` and
  `clause-verbs`. The data teach the frame, not the hard cases.
- **Small sources.** The sources hold 310 to 637 sentences each, and 4,894 to 12,007 tokens. A model that trains on one of them at weight 1
  is trained on a small amount of such text next to 63,522 sentences of the shipped mix.
- **Contrast strata are small.** The `verb` stratum is 14.9 percent of `deverbal`, `proper` 24.9 percent of `terms`, `noun-initial` 29.9
  percent of `steps` and `noun` and `adj` 20.0 percent of `clauses`; a model can lean toward the target reading.
- **Few enumerated lines.** 154 of the 372 `steps` imperatives (41.4 percent) come from enumerated or bulleted lines, the others from short
  stand-alone lines and instruction sentences.
- **Register and era.** The text is technical and descriptive (manuals, reports, procedures), a mix of scans (OCR residue in some
  sentences) and born-digital documents; vocabulary and punctuation differ from web text. The noise filter removed the clear cases only.
- **Frame, not coverage.** Compounds with an unambiguous head, defined terms without capitals, step lines in ALL CAPS and clauses with a
  singular subject are not selected.
- **Caps shape the sample.** At most 4 sentences per lemma (6 for `steps`) and 20 per document, so frequent lemmas are under-represented
  relative to the text.
- **The `terms` convention differs from UD practice** (see below): a model trained on `terms` and on the older sources receives both
  conventions.
- **No evaluation use.** The sentences are not independent of the sourcing of the shapes development set (the same federal agencies), but
  no document, no sentence and no near duplicate is shared with it or with any other evaluation set.

## Use

Train only. `tools/DemaConsulting.PartOfSpeech.Trainer/training.json` lists the sources as `deverbal`, `terms`, `steps` and `clauses`, each
with `"weight": 0` (they are not in the mix of the shipped weights, and `train --check` reproduces the shipped weights without them);
`--source deverbal:1` leaves one in a run. The trainer refuses the evaluation files by name and admits these files because they are training
sources under `data/`. Do not evaluate on them: their sentences are selected by a frame and labeled by models, so a score on them says
nothing about held-out text.

## Divergence from UD Practice

The labeling convention has a section "Capitalized Defined Terms" ([`data/CONVENTIONS.md`](../CONVENTIONS.md)): a capitalized term made of
ordinary English words that names a kind, mode, setting, position, state, screen, document type or component class is NOUN (ADJ for an
adjectival modifier) and not PROPN; PROPN stays for a name of one particular entity. The practice of UD English-EWT, of the open corpus
and of the other corpora is not uniform for such terms, so the existing labels disagree with the rule by an amount that this section
counts. **Nothing is relabeled**: the counts only describe the distance between the rule and the labels that exist.

**The detector** (`tools/data/term_divergence.py`, function `defined_term_conflicts`) looks at runs of 1 to 4 consecutive Title Case tokens (`[A-Z][a-z]+`) inside a sentence (not at its first position, not after
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

**Held-out sets** (`python tools/data/term_divergence.py count-held-out`, the only command of this tool that opens a held-out file; it prints
integers only and has no option that lists a sentence, an id or a token):

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
