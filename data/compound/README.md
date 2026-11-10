<!-- markdownlint-disable MD013 MD060 -->
<!-- cspell:ignore usfs usbm usbr usgs wxb nbs docstrings Lahontan airforce coastguard -->

# Compound-Head Training Sources

License: CC0-1.0

This folder holds two training sources of the same kind, built from different documents. **`compound`** (`compound.tsv`): 742 real
sentences from 154 US federal-government documents. **`compound2`** (`compound-2.tsv`): 1,100 real sentences from 209 further US
federal-government documents (see [Second Source: `compound-2.tsv`](#second-source-compound-2tsv)); the two share no document and no
sentence. Each sentence has a noun compound whose head can also be a verb
(`the fuel pump seal`, `the oil level check`). Every token has a Universal POS tag on which two blind model labelers agree. They are
**training** sources: they hold the noun + noun compounds with an ambiguous head (`[noun] check`, `[noun] split`, `[noun] stand`) that the
other training corpora hold rarely, and a counter-example group in which the same kind of word is a verb. They are disjoint from every
evaluation set. They are registered in the trainer configuration with weight 0, so they are not part of the mix that trained the embedded model
(`--source compound:1` and `--source compound2:1` add them to a training run). The sections up to "Known Limits" describe `compound`; the
section "Second Source" describes `compound2`.

## Source and Provenance

- **Source:** the sentences are text of 154 documents that are US federal government works (17 U.S.C. 105): Department of Agriculture,
  Forest Service, Geological Survey, War Department and Army, Navy, Bureau of Reclamation, Public Health Service, Bureau of Land
  Management, Weather Bureau, Bureau of Standards and Bureau of Mines publications, quoted per document in `sources.tsv`. No document is in
  any other source table of this repository, none is in an evaluation set, and nothing was written for this source: all sentences are real
  text (0 authored).
- **How produced:** `python tools/data/compound_heading_data.py --work <work-folder> build --source compound` with the `--omit-document` options
  listed in `excluded-documents.tsv`, at the builder and the rule of commit `e776f5693739900d91e63fd2a12451da1f4ba90b` (the builder, its tests and
  the pre-registered rule, committed before any label of this source existed; the option `--omit-document` (with an optional reason per document) was added after the labeling and is the
  only change of the builder since). The labeling used the unchanged commands of `tools/data/shapes_dev.py` (`export`, `check-batches`,
  `validate-output`, `status`, `decide`) with `--round compound`. The global `--work` argument (a folder outside the repository, required)
  comes before the command; the other options keep their defaults (`--seed` 20261110, the caps below). The work folder is not committed. It
  holds the extracted text of the sources (`sources-draft.tsv`, `text/<source_id>.txt`, one sentence per line, `raw/<source_id>.txt`), the
  tokens of every unit (from `shapes_dev_extra.py tokenize`, which runs the library tokenizer), the candidates, the blind batches, the two
  labeler outputs and the private decisions; the layout is in the docstrings of `shapes_dev.py` and `compound_heading_data.py`. The scripts
  that found and fetched the documents are one-off helpers and are not committed; the record of what was fetched is `sources.tsv` (URL,
  retrieval date, SHA-256 of the download and of the extracted sentences of every document). `compound.tsv` is never edited by hand.
- **Labels:** written by two language models that followed [CONVENTIONS.md](../CONVENTIONS.md) and saw only a batch of opaque ids and
  tokens: `claude-opus-5.5` and `gpt-6.1-sol`. A sentence is kept only when the two agree on the tag of every token. **No human reviewed a
  label.** The project owner dedicates the labels and the selection to the public domain under CC0 1.0 (the owner's position on the rights in
  model outputs is stated in the repository README and is not legal advice).
- **Sentences:** public domain in the United States as works of the federal government; the basis of every document is quoted in
  `sources.tsv` (the statute, the Public Domain Mark or the rights statement of the Internet Archive record, and the check that the text
  carries no copyright, permission or third-party notice). There is no FAA text, no Wikipedia, no share-alike or non-commercial text and no
  Universal Dependencies text (`check_licenses.py` scans `sources.tsv`). A document whose record lists a state or local government body as an
  author is not used (`excluded-documents.tsv`).

`compound.tsv` has SHA-256 `eb8c4708ba95fb370c885110e10eda0f1d826c5922876bd66e69389894bd5e3b` and `sources.tsv`
`8b861dd3ff2d5a3fd5e741f5fe2a906807f99afa562d7e294d4011d93ef15e02`; [MANIFEST.tsv](../MANIFEST.tsv) lists the size, hash and record
counts of every file of this folder.

## Files

| File | Content |
| --- | --- |
| `compound.tsv` | the 742 sentences in the corpus block format of [CONVENTIONS.md](../CONVENTIONS.md) ("Other Formats" describes the header and the extra comments); ids `compound-NNNN` |
| `sources.tsv` | the 154 documents that the sentences come from (only used documents): family, title, URL, author, year, license basis (the quoted statement), terms URL, retrieval date, hashes, sentences used |
| `excluded-documents.tsv` | documents left out: the 7 whose record lists a state or local government body as an author (the rule 1 check after the build) |
| `dropped-sentences.tsv` | counts of candidate sentences dropped alone because they equal, or nearly equal, a sentence of a training or evaluation set (counts only, no text) |
| `compound-2.tsv`, `compound-2-sources.tsv`, `compound-2-excluded-documents.tsv`, `compound-2-dropped-sentences.tsv` | the same four files for the second source `compound2` (see [Second Source](#second-source-compound-2tsv)); ids `compound2-NNNN` |

Each sentence block has the comments `sent_id`, `source_id`, `text`, `head`, `lemma` and `kind`, then one row `form`, `upos`, `status` for
each token. `head` is the zero-based token index of the head of the compound, `lemma` its lower-case form, and `kind` is `noun` (the head is
a noun) or `verb` (the head is a verb). Every status is `unanimous` (no token is `disputed` or masked), with the flags that both labelers
agreed on.

## Counts

742 sentences (14,692 tokens, 4 to 30 tokens each) with 462 distinct head lemmas in 154 documents. At most 4 sentences per head lemma and at
most 20 per document. Kind `noun` has 631 sentences (head tagged NOUN) and kind `verb` 111 (head tagged VERB after a noun that both labelers
agree on). The heads are all lower case and are words that `lexicon/lexicon.tsv` lists as both noun and verb; a data test asserts the quotas
and the format.

## Labeling and Decision Rule

The rule was fixed before any label of this source was seen (the commit above); the docstring of `tools/data/compound_heading_data.py` is its
normative text.

1. **Documents.** US federal authorship, with the public-domain basis checked one document at a time and written in the `license_basis`
   cell. The id, the normalized URL and the Internet Archive identifier of a document are in no table of used sources (training and
   evaluation sources, the shapes development set, the imperative source and every earlier sourcing); a document that shares 3 or more
   sentence keys with a training or evaluation set, with the shapes development set or with the full text of an earlier sourcing is rejected
   as a whole.
2. **Candidates.** A sentence is a candidate when it passes the cleanliness filters of `shapes_dev.py` (3 to 30 tokens, one sentence, no
   OCR junk, citation, enumeration, title or repetition noise), equals no training, evaluation, shapes development or imperative sentence, is no
   heading line of the shared pool, shares no word 5-gram with the held-out shape probe, is not a near duplicate of a shapes development
   sentence (at least 10 words, at least 80 percent of the word 6-grams), and has the frame of `n-compound-head-ambiguous`
   (`shapes_dev_extra.compound_ambiguous_hit`): the head is lower case, is listed in the lexicon as both noun and verb, stands right after a
   noun-like modifier and is the last noun of the compound. No model score takes part: the pool is every frame hit, taken in a seeded order. At
   most 6 candidates per head lemma and 1,500 in all.
3. **Labels.** Every candidate is tagged from scratch, token by token, by the two blind labelers through the batch commands of
   `shapes_dev.py`; the batch files carry an opaque id, a token index and the token, and the labelers see no shape, source, lemma or intended
   class.
4. **Kept sentences.** A candidate is kept when both labelers agree on every token (no disputed token), when the convention linter reports no
   fix-tier violation on it, and when the head is read as a noun (NOUN or PROPN) by both and the frame still holds in the labeled
   sentence (kind `noun`), or when both read the head as VERB and agree that a noun stands right before it (kind `verb`). Any other
   combination (an adjective, adverb or other class for the head, a modifier read as an adjective, a disagreement on the head) is dropped
   and counted. No label is changed by hand.
5. **Caps.** In a seeded order the build takes a kept sentence unless its head lemma already has 4 sentences, its document already has
   20, its kind is full (680 noun, 120 verb) or 800 are taken; the verb items stay at most 15 percent of the file.
6. **Quota gate.** `build` refuses (exit code 1, nothing written) unless there are 600 to 800 sentences with at least 200 head lemmas, at most 4
   per lemma and 20 per document, at most 15 percent verb items, the head is tagged as its kind says, every token is `unanimous` and the tokens
   rejoin to the text; a data test asserts this.
7. **Documents removed after the build.** A license check of the author field of the records found 7 documents that list a state or local
   government body as an author. Their items are removed (`--omit-document`: after the caps, nothing is added, the verb share is applied again
   to what is left), and the documents are listed in `excluded-documents.tsv`.

Numbers of the run:

| Stage | Sentences |
| --- | ---: |
| sentences of the sourced text (224 documents of the shared pool) | 176,759 |
| clean sentences kept for the frame | 126,725 |
| frame hits (1,935 head lemmas) | 17,783 |
| candidates after the lemma cap (6) and the total cap (1,500), 694 lemmas in 192 documents | 1,500 |
| dropped: head read as another class by both labelers (class mismatch) | 106 |
| dropped: the labelers disagree on the head (target disagreement) | 6 |
| dropped: a modifier or head read so that the frame no longer holds (frame mismatch) | 80 |
| dropped: a disputed token elsewhere in the sentence | 163 |
| dropped by the caps: lemma 55, document 6, noun 107, verb 177 | 345 |
| removed with the 7 documents (rule 7) | 54 |
| dropped by the verb share applied again | 4 |
| **in `compound.tsv`** | **742** |

Candidate sentences dropped alone because of an overlap (`dropped-sentences.tsv`): 3 equal a sentence of the shapes development set and 2 are
near duplicates of one.

**Refusals.** None. Each of the 10 batches was tagged by both labelers (20 runs), every run produced a valid output at the first request,
and no batch was skipped or requested again.

## Hand Check

A third model, `claude-sonnet-5.5`, reviewed 60 random sentences of the data as built before rule 7 (drawn with the fixed seed 20261111)
against the conventions. It saw each sentence with every token and its tag, and judged the head (and marked any other wrong token). This is a
**model review, not a human review**, and it is not independent of the labelers' shared blind spots. 2 of the 60 sentences belong to documents
removed by rule 7; the figures below are those of the 58 that remain.

- **Head:** 58 correct, 0 incorrect and 0 unclear of 58, **precision 58/58 = 100.0 percent**. The gate was 95 percent (3 incorrect of 60 is exactly
  95.0 percent and passes; 4 do not).
- **Whole sentence:** 56 of 58 sentences (96.6 percent) have no other token marked wrong; **token precision 99.75 percent** (3 tokens wrong of
  1,217). These figures are reported, not gated.
- **Leniency control:** 4 control items whose head tag had been swapped to the wrong class were slipped into the review unmarked; the reviewer
  judged all 4 incorrect (0 of 4 accepted), so it was not accepting every item.

One sample was drawn and reviewed once; it was not drawn again.

## Sources

| Family | Documents | Sentences | Basis and origin |
| --- | ---: | ---: | --- |
| `usda` | 38 | 139 | Department of Agriculture bulletins, circulars, leaflets and reports (Internet Archive); 17 U.S.C. 105 |
| `usfs` | 21 | 128 | Forest Service publications (Internet Archive); 17 U.S.C. 105 |
| `army` | 14 | 86 | War Department and Department of the Army manuals, bulletins and reports (Internet Archive); 17 U.S.C. 105 |
| `usgs` | 19 | 80 | Geological Survey bulletins, papers and reports (Internet Archive); 17 U.S.C. 105 |
| `usbr` | 12 | 70 | Bureau of Reclamation and related Interior study reports (Internet Archive); 17 U.S.C. 105 |
| `other` | 10 | 69 | Bureau of Land Management, Bureau of Fisheries, Office of Education, Bureau of Education and National Park Service publications (Internet Archive); 17 U.S.C. 105 |
| `navy` | 13 | 54 | Navy and Marine Corps manuals and reports (Internet Archive); 17 U.S.C. 105 |
| `phs` | 11 | 39 | Public Health Service and Children's Bureau publications (Internet Archive); 17 U.S.C. 105 |
| `usbm` | 5 | 36 | Bureau of Mines publications (Internet Archive); 17 U.S.C. 105 |
| `wxb` | 7 | 22 | Weather Bureau publications (Internet Archive); 17 U.S.C. 105 |
| `nbs` | 4 | 19 | National Bureau of Standards publications (Internet Archive); 17 U.S.C. 105 |

`sources.tsv` lists only these used documents. Publication years are 1862 to 2008 (115 of the documents are from 1940 or later); every
document was retrieved on 2026-11-09 and hashed (`raw_sha256` of the download, `text_sha256` of the extracted sentences). The same pool of
documents feeds [the headings source](../headings/README.md): 73 documents contribute to both, and no sentence is in both.

## Leakage and License Checks

`python tools/data/check_leakage.py` compares this source with every evaluation set (open dev and test, gold v2, the shape probe, all five
tranches of the shapes development set, the STE challenge set, and UD English-EWT train, dev and test when the git-ignored cache is present;
the probe is reached through its sentence column only) and with the other training sources (open train, MASC, silver, imperative and
headings). Result: **0 document overlaps, 0 exact sentence overlaps and 0 near duplicates** for every pair, except that this source and the
headings source share documents by design (reported, not gated; their sentences never match); [leakage-report.tsv](../leakage-report.tsv)
and [training-leakage-report.tsv](../training-leakage-report.tsv) have the counts. `check_licenses.py` scans `sources.tsv`, the excluded-source
record and this README. `check_conventions.py` lints `compound.tsv` (lint only: the corrections overlay never addresses it): the fix tier
finds no violation, which the lint run enforces, and the check tier reports 2 (one `be-participle-adj-not-gradable` and one
`language-name-token` hit, both left as the labelers tagged them).

## Lexicon Coverage

The lexicon is not built from this source, and the file does not change it (`lexicon/lexicon.tsv` is unchanged). Measured against
`lexicon/lexicon.tsv` (`python tools/data/compound_heading_data.py --work <work-folder> report --source compound` prints the figures):

| Measure | Listed | Of | Share |
| --- | ---: | ---: | ---: |
| word tokens whose gold tag the lexicon lists for the spelling (tokens without a letter or digit are not counted) | 12,823 | 13,265 | 96.67 percent |
| distinct (lower-cased spelling, tag) pairs the lexicon lists | 3,971 | 4,332 | 91.67 percent |

All 742 heads are listed with the tag they have here. Of the 361 unlisted pairs, 295 are spellings that the lexicon does not contain at all
and 66 are spellings that the lexicon lists with other tags. 158 of the 361 contain a digit (numerals such as `2.05` and measure compounds); the
other 203 are hyphenated compounds (`field-trip/NOUN`, `non-local/ADJ`), proper names (`Lahontan/PROPN`) and OCR residue. By tag: NUM 138,
PROPN 131, ADJ 50, NOUN 27, VERB 4, X 4, AUX 2, ADV 2, DET 1, CCONJ 1, ADP 1. In training with the `history` restriction a gold tag that the
lexicon misses is allowed, so these pairs do not block training.

## Known Limits

- **Model labels, no human review.** Two models agree on every token, which removes the sentences where they differ, not the errors that
  they share. The model review (see Hand Check) is a sample, not a human review; the residual error of the source is not measured by a
  human.
- **No hard-case selection.** The pool is every frame hit in a seeded order; no classifier score chooses it. The sentences are therefore
  easier on average than the development tranche `compound-head.tsv`, which kept only the contested band of a classifier. The data teach
  the frame, not the hard cases.
- **Few verb counter-examples.** 111 sentences (15.0 percent at most) use the head as a verb. A model that trains on the source can lean
  toward the noun reading of an ambiguous head after a noun.
- **Register and era.** The text is technical and descriptive (bulletins, reports, manuals), mostly scans from 1940 to 1990 with OCR
  residue in some sentences; vocabulary and punctuation differ from web text. The noise filter removed the clear cases only.
- **Frame, not coverage.** The sentences have the frame of `n-compound-head-ambiguous`; compounds with an unambiguous head, compounds with a
  participle or gerund modifier, and heads after an adjective are not selected.
- **Caps shape the sample.** At most 4 sentences per head lemma and 20 per document, so the frequent heads are under-represented relative to
  the text.
- **No evaluation use.** The sentences are not independent of the sourcing of the shapes development set (same families of federal text), but
  no document, no sentence and no near duplicate is shared with it or with any other evaluation set.

## Use

Train only. `tools/DemaConsulting.PartOfSpeech.Trainer/training.json` lists the source as `compound` with `"weight": 0` (committed, not part of
the shipped mix; `train --check` reproduces the shipped weights); `--source compound:1` adds it to a training run (the trainer run log, section
"Compound and heading training data: results", has the measured effect of the source at weight 1). The trainer refuses the
evaluation files by name and admits this file because it is a training source under `data/`. Do not evaluate on it: its sentences are selected
by a frame and labeled by models, so a score on them says nothing about held-out text.

## Second Source: `compound-2.tsv`

**What it is.** `compound2` has 1,100 real sentences (20,745 tokens, 4 to 30 tokens each) from 209 US federal-government documents, each
with a noun compound whose head can also be a verb, in the frame of `n-compound-head-ambiguous`; every token has a tag on which two blind
model labelers agree (every status is `unanimous`). It has the same format, the same head rule and the same two kinds as `compound.tsv`
(`head`, `lemma`, `kind` comments; kind `noun` 935 sentences, kind `verb` 165 = 15.0%), and it is built from documents that `compound.tsv`,
`headings.tsv`, the imperative source and every evaluation set do not use: together with `compound.tsv` it holds 742 + 1,100 = 1,842
sentences. There are 670 distinct head lemmas (133 of them with a verb sentence; 112 sentences have an `-ing` head), at most 4 sentences per
head lemma and at most 20 per document.

**Files.**

| File | Content |
| --- | --- |
| `compound-2.tsv` | the 1,100 sentences in the corpus block format of [CONVENTIONS.md](../CONVENTIONS.md); ids `compound2-NNNN` |
| `compound-2-sources.tsv` | the 209 documents that the sentences come from: family, title, URL, author, year, license basis (the quoted statement), terms URL, retrieval date, hashes, sentences used |
| `compound-2-excluded-documents.tsv` | documents left out after the build: none (header only) |
| `compound-2-dropped-sentences.tsv` | counts of candidate sentences dropped alone because of an overlap with a set (2 near duplicates of a shapes development sentence, 1 shared word 5-gram with the shape probe; counts only, no text) |

`compound-2.tsv` has SHA-256 `2a9a98492afb1248623b50111ca62d8abd57e4569b1a5e52b3cff544bc0746cc` and `compound-2-sources.tsv`
`9c7ec7014ff115e745501706126b4637382c058783fdea43021b9f6cbfbd0b4f`; [MANIFEST.tsv](../MANIFEST.tsv) lists the size, hash and record counts of every
file of this folder.

**Source and basis.** The sentences are text of 209 documents that are US federal government works (17 U.S.C. 105), one `license_basis` cell
per document in `compound-2-sources.tsv` (the agency named in the record, the record's rights statement where it has one, and the check that the text
carries no copyright, permission, third-party credit or distribution restriction); the documents were retrieved from the Internet Archive on
2026-11-10 and hashed (`raw_sha256` of the download, `text_sha256` of the extracted sentences). Publication years are 1852 to 2025 (182 of the
documents are from 1940 or later). There is no FAA text, no Wikipedia, no share-alike or non-commercial text, no Universal Dependencies text, no
sentence supplied by any consumer, and no document that lists a state, local, foreign, university, contractor or private creator, publisher or
co-author. No document is in a table of used sources or in either excluded-sources record (`check_licenses.py` scans the table).

| Family | Documents | Sentences | Origin |
| --- | ---: | ---: | --- |
| `army` | 111 | 612 | War Department and Department of the Army manuals, bulletins and reports |
| `usda` | 26 | 165 | Department of Agriculture publications |
| `navy` | 30 | 114 | Navy publications |
| `usfs` | 13 | 59 | Forest Service publications |
| `other` | 6 | 46 | other federal bodies |
| `marine` | 5 | 38 | Marine Corps publications |
| `airforce` | 2 | 24 | Air Force manuals |
| `usbr` | 9 | 22 | Bureau of Reclamation publications |
| `phs` | 6 | 17 | Public Health Service publications |
| `coastguard` | 1 | 3 | Coast Guard publication |

**Selection and labeling rule.** The normative text is the docstring of `tools/data/followup_data.py` (rules 1 to 10; the pre-registered rule
at commit `8c9f17fc5cb7e5c35f91827fc242980f407bd42d`, with the amendments recorded in the trainer run log, section "Follow-up"). In short:

1. **Documents** are assigned to the training side or the development side of the follow-up as whole title families (a seeded hash of the
   family key); `compound-2.tsv` uses documents of the training side only, so no document of it is in a development tranche.
2. **Candidates** are every frame hit of `compound_ambiguous_hit` in a clean sentence of the training side (224,925 clean sentences, 36,552
   frame hits over 2,396 head lemmas; no model score and no band), taken in a seeded order, at most 6 per head lemma and 3,400 in all: 3,400
   candidates over 1,219 head lemmas in 242 documents. A sentence that equals a training, evaluation or shapes development sentence, is a
   heading line of the shared pool or shares a word 5-gram with the held-out shape probe is not a candidate.
3. **Labels:** every candidate is tagged from scratch, token by token, by the two blind labelers (`claude-opus-5.5` and `gpt-6.1-sol`) of
   `compound.tsv`. **No human reviewed a label.**
4. **Kept sentences:** both labelers agree on every token, the convention linter reports no fix-tier violation, and the head is read as a noun
   (NOUN or PROPN) by both with the frame still holding (kind `noun`) or as VERB by both after a noun that both agree on (kind `verb`).
5. **Caps** in a seeded order: 4 per head lemma, 20 per document, at most 15 percent verb items, at most 1,100 sentences.
6. **Quota gate:** `build` refuses below 600 sentences or 200 head lemmas unless `--allow-short`; a data test asserts the quotas and the format.

| Stage | Items |
| --- | ---: |
| candidates labeled | 3,400 |
| dropped: the labelers disagree on the head (target disagreement) | 10 |
| dropped: head read as another class (class mismatch) | 211 |
| dropped: a modifier or head read so that the frame no longer holds (frame mismatch) | 364 |
| dropped: a disputed token elsewhere in the sentence | 387 |
| kept by the labeling rules | 2,428 |
| dropped by the caps: noun 791, verb 421, document 59, lemma 57 | 1,328 |
| **in `compound-2.tsv`** | **1,100** |

**Hand check (model review, not a human review).** One sample of 60 sentences (54 `noun`, 6 `verb`; seed 20261123) plus 4 control items whose
head tag had been swapped on purpose was drawn once from the data as built and reviewed once by `claude-sonnet-5.5` against
[CONVENTIONS.md](../CONVENTIONS.md): the head is correct in 60 of 60 (100.0 percent; the gate is 95 percent), 59 of 60 sentences (98.3 percent)
have no other token marked wrong, token precision is 99.91 percent (1 token wrong of 1,145; reported, not gated), and 0 of the 4 controls
were accepted (the gate allows 1). It is not independent of the labelers' shared blind spots.

**Leakage and license checks.** `python tools/data/check_leakage.py` finds **0 document overlaps, 0 exact sentence overlaps and 0 near
duplicates** against every evaluation set (open dev and test, gold v2, the shape probe, all seven tranches of the shapes development set,
the STE challenge set, UD English-EWT) and against open train, MASC, silver, imperative, `compound` and `headings`; it shares 57 documents (and no
sentence) with `headings2`, which is cut from the same pool of the training side (reported, not gated).
[training-leakage-report.tsv](../training-leakage-report.tsv) and [leakage-report.tsv](../leakage-report.tsv) have the counts. `check_licenses.py` scans
`compound-2-sources.tsv`. `check_conventions.py` lints `compound-2.tsv` (lint only; its own column `compound2`): the fix tier finds 0
violations, and the check tier reports 2 hits (one `be-participle-adj-not-gradable` and one `infinitive-to`, both left as the labelers tagged
them). `lexicon/lexicon.tsv` is unchanged.

**Known limits.**

- **Model labels, no human review.** Two models agree on every token, which removes the sentences where they differ, not the errors they share;
  the review above is a sample of 60.
- **No hard-case selection.** The pool is every frame hit in a seeded order, with no classifier score, so the sentences are easier on average
  than the development tranches `compound-head.tsv` and `compound-head-2.tsv` (which keep only the contested band of a classifier).
- **Few verb counter-examples.** 165 sentences (15.0 percent, the cap) use the head as a verb; 935 use it as a noun.
- **Register.** The text is technical and descriptive (manuals, bulletins, reports); 111 of the 209 documents (612 of the sentences) are Army
  publications; most documents are OCR scans with residue in some sentences.
- **Frame, not coverage.** Compounds with an unambiguous head, compounds with a participle or gerund modifier, and heads after an adjective are
  not selected.
- **Caps shape the sample:** at most 4 sentences per head lemma and 20 per document.

**Use.** Train only. `training.json` lists the source as `compound2` with `"weight": 0` (not part of the shipped mix; `train --check`
reproduces the shipped weights); `--source compound2:1` adds it to a training run. The trainer refuses the evaluation files by name and admits this
file because it is a training source under `data/`. Do not evaluate on it.
