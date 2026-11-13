<!-- markdownlint-disable MD013 MD060 -->
<!-- cspell:ignore usfs usbm usbr usgs wxb nbs docstrings airforce coastguard -->

# Heading Training Sources

License: CC0-1.0

This folder holds two training sources of the same kind, built from different documents. **`headings`** (`headings.tsv`): 374 real Title
Case heading, caption and step-title lines from 89 US federal-government documents. **`headings2`** (`headings-2.tsv`): 247 real lines from 59
further US federal-government documents, with a stratum of initial imperatives (see [Second Source: `headings-2.tsv`](#second-source-headings-2tsv));
the two share no document and no line. The lines of `headings` (`Fuel Filter Replacement`,
`Pacific Northwest Region`) each have a judged word in one of three groups: a common noun, an initial imperative verb or a true proper
noun. Every token has a Universal POS tag on which two blind model labelers agree. They are **training** sources: they show that the capital
letters of a Title Case line do not make its words proper nouns, which the sentence-shaped training corpora rarely show, and they are disjoint
from every evaluation set. They are registered in the trainer configuration with weight 0, so they are not part of the mix that trained the
embedded model (`--source headings:1` and `--source headings2:1` add them to a training run). The sections up to "Known Limits" describe
`headings`; the section "Second Source" describes `headings2`.

## Source and Provenance

- **Source:** the lines are text of 89 documents that are US federal government works (17 U.S.C. 105): Department of Agriculture, Forest
  Service, Bureau of Reclamation, Public Health Service, Bureau of Land Management, Bureau of Mines, Geological Survey, War Department and
  Army, Navy, Bureau of Standards and Weather Bureau publications, quoted per document in `sources.tsv`. No document is in any other
  source table of this repository, none is in an evaluation set, and nothing is written for this source: all lines are real text (0
  authored).
- **How produced:** `python tools/data/compound_heading_data.py --work <work-folder> build --source headings --allow-short` with the
  `--omit-document` options for the documents listed in `excluded-documents.tsv`, at the builder and the rule of commit `e776f5693739900d91e63fd2a12451da1f4ba90b`
  (the builder, its tests and the rule; the option `--omit-document`, with an optional
  reason per document, is the only part of the builder that is not at that commit). `--allow-short` is the waiver that the rule registers for a stratum floor
  that the labelers' agreement does not fill (see "Counts"). The labeling uses the commands of `tools/data/shapes_dev.py` (`export`,
  `check-batches`, `validate-output`, `status`, `decide`) with `--round headings`. The global `--work` argument (a folder outside the
  repository, required) comes before the command; the other options keep their defaults (`--seed` 20261110, the caps below). The work
  folder is not committed. It holds the extracted text of the sources (`sources-draft.tsv`, `raw/<source_id>.txt` with the OCR line
  structure, `lines/<source_id>.txt` with the heading lines found by `shapes_dev_extra.py lines`), the tokens of every line (from
  `shapes_dev_extra.py tokenize`, which runs the library tokenizer), the candidates, the blind batches, the two labeler outputs and the
  private decisions; the layout is in the docstrings of `shapes_dev.py` and `compound_heading_data.py`. The scripts that found and fetched the
  documents are one-off helpers and are not committed; the record of what was fetched is `sources.tsv` (URL, retrieval date, SHA-256 of
  the download and of the extracted sentences of every document). `headings.tsv` is never edited by hand.
- **Labels:** written by two language models that followed [CONVENTIONS.md](../CONVENTIONS.md) (including its rule for headings, captions
  and step titles) and saw only a batch of opaque ids and tokens: `claude-opus-5.5` and `gpt-6.1-sol`. A line is kept only when the two agree on
  the tag of every token. **No human reviewed a label.** The project owner dedicates the labels and the selection to the public domain under
  CC0 1.0 (the owner's position on the rights in model outputs is stated in the repository README and is not legal advice).
- **Sentences:** public domain in the United States as works of the federal government; the basis of every document is quoted in
  `sources.tsv` (the statute, the Public Domain Mark or the rights statement of the Internet Archive record, and the check that the text
  carries no copyright, permission or third-party notice). There is no FAA text, no Wikipedia, no share-alike or non-commercial text and no
  Universal Dependencies text (`check_licenses.py` scans `sources.tsv`). A document whose record lists a state or local government body as an
  author is not used, and neither is the National Bureau of Standards Handbook 65 (*Safe handling of bodies containing radioactive isotopes*,
  1958): its author field also lists a private non-profit organization, its public-domain basis is uncertain, and the project owner removed it
  (`excluded-documents.tsv`, and `data/eval/shapes-dev/excluded-sources.tsv`, which every data check reads).

`headings.tsv` has SHA-256 `cae45ff12cb4e7e9531a007beb8278b4014a8bdab9ae3e52b06ba46df4d48cde` and `sources.tsv`
`26ca020469ee3a396ba71bd7845030a516e612c44e887933b1fe8450445fbad7`; [MANIFEST.tsv](../MANIFEST.tsv) lists the size, hash and record
counts of every file of this folder.

## Files

| File | Content |
| --- | --- |
| `headings.tsv` | the 374 lines in the corpus block format of [CONVENTIONS.md](../CONVENTIONS.md) ("Other Formats" describes the header and the extra comments); ids `headings-NNNN` |
| `sources.tsv` | the 89 documents that the lines come from (only used documents): family, title, URL, author, year, license basis (the quoted statement), terms URL, retrieval date, hashes, lines used |
| `excluded-documents.tsv` | documents left out: the 7 whose record lists a state or local government body as an author, and 1 with a private non-profit co-author and an uncertain public-domain basis (removed by the project owner; the rule 1 check after the build) |
| `dropped-sentences.tsv` | counts of candidate lines dropped alone because they equal a sentence of a training or evaluation set or of the shapes development set (counts only, no text) |
| `headings-2.tsv`, `headings-2-sources.tsv`, `headings-2-excluded-documents.tsv`, `headings-2-dropped-sentences.tsv` | the same four files for the second source `headings2` (see [Second Source](#second-source-headings-2tsv)); ids `headings2-NNNN` |

Each block has the comments `sent_id`, `source_id`, `text`, `word`, `lemma` and `stratum`, then one row `form`, `upos`, `status` for each
token. `word` is the zero-based token index of the judged word, `lemma` its lower-case form, and `stratum` is `noun`, `imperative` or
`proper`. Every status is `unanimous` (no token is `disputed` or masked), with the flags that both labelers agreed on. The unit is a line,
not a sentence: it has no final period, and a numbering or label in front of it is not part of the text.

## Counts

374 lines (1,664 tokens, 2 to 10 tokens each) with 272 distinct lemmas of the judged word in 89 documents. At most 4 lines per lemma and at most
12 per document (the cap is 25). The judged word is tagged exactly as the convention says:

| Stratum | Lines | Judged word |
| --- | ---: | --- |
| `noun` | 314 | a Title Case common noun that ends its noun phrase: NOUN |
| `proper` | 52 | a word that names one particular entity: PROPN |
| `imperative` | 8 | the first word of an instruction line, a base verb: VERB |

The registered floors are 150 noun, 100 imperative and 40 proper lines. The imperative stratum is below its floor (8 lines): 346 of the 356
initial-imperative candidates were read by both labelers as a noun, an adjective or a proper noun, and the rule drops a line whose agreed tag is not the one of the convention for its stratum. Those lines are
dropped and counted, never kept with another tag and never corrected by hand. **The source therefore has almost no imperative training
mass.**

## Labeling and Decision Rule

The docstring of `tools/data/compound_heading_data.py` is the normative text of the rule.

1. **Documents.** US federal authorship, with the public-domain basis checked one document at a time and written in the `license_basis`
   cell. The id, the normalized URL and the Internet Archive identifier of a document are in no table of used sources (training and
   evaluation sources, the shapes development set, the imperative source and every earlier sourcing); a document that shares 3 or more
   sentence keys with a training or evaluation set, with the shapes development set or with the full text of an earlier sourcing is rejected
   as a whole.
2. **Candidates.** A line is a candidate when `shapes_dev_extra.heading_lines` finds it in the OCR text of a document (a line that stands alone
   above a paragraph) and it passes the heading filter `heading_noise_reason` (2 to 10 tokens, Title Case, no all-capital word, no unknown
   word, no OCR junk, and the corrected trailing page number and page range rule), equals no training, evaluation or shapes development
   sentence, shares no word 5-gram with the held-out shape probe, and has a frame hit: stratum `proper` (a non-initial capitalized word that the
   lexicon lists as a proper noun only), `imperative` (token 0 capitalized, listed as noun and verb, its own base form, followed by a
   determiner, an object pronoun, an adverb, a particle or a capitalized noun-capable word) or `noun` (a non-initial capitalized word listed as
   noun and verb that ends its noun phrase). A line with hits of several strata takes the rarest first (proper, imperative, noun). No model
   score takes part. At most 4 candidates per lemma over all strata, 25 per document and 1,500 in all.
3. **Labels.** Every candidate is tagged from scratch, token by token, by the two blind labelers through the batch commands of
   `shapes_dev.py`; the batch files carry an opaque id, a token index and the token, and the labelers see no stratum, source, lemma or intended
   class.
4. **Kept lines.** A candidate is kept when both labelers agree on every token (no disputed token), when the convention linter reports no
   fix-tier violation on it, and when the judged word has the tag of the convention on which both agree: an initial imperative is VERB, a
   noun word NOUN, a proper noun PROPN. A line on which both agree on another tag for the judged word is dropped as a class mismatch. No label
   is changed by hand.
5. **Caps.** In the order proper, imperative, noun and a seeded order inside a stratum the build takes a kept line unless its stratum is full
   (noun 340, imperative 200, proper 60), its lemma already has 4 lines or its document already has 25.
6. **Quota gate.** `build` refuses (exit code 1, nothing written) below the floors (150 noun, 100 imperative, 40 proper) unless `--allow-short`
   waives them, and always refuses a cap that is exceeded, a judged word without the tag of its stratum, a token that is not `unanimous` and tokens
   that do not rejoin to the text; a data test asserts the caps, the tags and the format. The floors of the noun and proper strata are
   met; the imperative floor is waived.
7. **Documents removed after the build.** A license check of the author field of the records found 7 documents that list a state or local
   government body as an author, and the project owner removed a further document whose record lists a private non-profit organization as a
   co-author (its public-domain basis is uncertain). Their lines (33 and 1) are removed (`--omit-document`: after the caps, nothing is added), and
   the documents are listed in `excluded-documents.tsv`.

Numbers of the run:

| Stage | Lines |
| --- | ---: |
| stand-alone lines of the OCR text found by `heading_lines` (224 documents of the shared pool) | 195,739 |
| clean heading lines kept for the frame | 2,976 |
| frame hits: noun 1,424, imperative 505, proper 100 | 2,029 |
| candidates after the lemma, document and total caps, 560 lemmas in 124 documents: noun 560, imperative 356, proper 96 | 1,012 |
| dropped: judged word read as another class by both labelers (class mismatch) | 427 |
| dropped: the labelers disagree on the judged word (target disagreement) | 12 |
| dropped: a disputed token elsewhere in the line | 31 |
| dropped by the caps: noun 108, proper 26 | 134 |
| removed with the 8 documents (rule 7) | 34 |
| **in `headings.tsv`** | **374** |

The class mismatches are 346 imperative lines (the first word read as NOUN by both 275 times, as ADJ 44 and as PROPN 27), 80 noun lines (the
noun word read as PROPN 65 times, VERB 9, ADJ 4 and ADV 2) and 1 proper line. Candidate lines dropped alone because of an overlap
(`dropped-sentences.tsv`): 118 equal a sentence of the shapes development set.

**Refusals.** None. Each of the 7 batches was tagged by both labelers (14 runs), every run produced a valid output at the first request, and
no batch was skipped or requested again.

## Hand Check

A third model, `claude-sonnet-5.5`, reviewed 48 random lines of the data as built before rule 7 (drawn with the fixed seed 20261112) against
the conventions. The registered plan is 60 lines (noun 25, imperative 20, proper 15); the imperative stratum has 8 lines, so all 8 were
reviewed and the sample has 48. It saw each line with every token and its tag, and judged the marked word (and marked any other wrong token).
This is a **model review, not a human review**, and it is not independent of the labelers' shared blind spots. 3 of the 48 lines belong to
documents removed by rule 7; the figures below are those of the 45 that remain.

- **Judged word:** 45 correct, 0 incorrect and 0 unclear of 45, **precision 45/45 = 100.0 percent**. The gate was 95 percent.
- **Whole line:** 44 of 45 lines (97.8 percent) have no other token marked wrong; **token precision 99.54 percent** (1 token wrong of 219).
  These figures are reported, not gated.
- **Leniency control:** 4 control items whose judged word had been swapped to the wrong class were slipped into the review unmarked; the
  reviewer judged all 4 incorrect (0 of 4 accepted), so it was not accepting every item.

One sample was drawn and reviewed once; it was not drawn again.

## Sources

| Family | Documents | Lines | Basis and origin |
| --- | ---: | ---: | --- |
| `usda` | 25 | 105 | Department of Agriculture bulletins, circulars and reports (Internet Archive); 17 U.S.C. 105 |
| `usfs` | 15 | 84 | Forest Service publications (Internet Archive); 17 U.S.C. 105 |
| `usbr` | 10 | 52 | Bureau of Reclamation and related Interior study reports (Internet Archive); 17 U.S.C. 105 |
| `other` | 7 | 29 | Bureau of Land Management, Bureau of Fisheries and National Park Service publications (Internet Archive); 17 U.S.C. 105 |
| `phs` | 12 | 25 | Public Health Service and Children's Bureau publications (Internet Archive); 17 U.S.C. 105 |
| `usbm` | 3 | 24 | Bureau of Mines publications (Internet Archive); 17 U.S.C. 105 |
| `army` | 5 | 18 | War Department and Department of the Army publications (Internet Archive); 17 U.S.C. 105 |
| `usgs` | 6 | 17 | Geological Survey publications (Internet Archive); 17 U.S.C. 105 |
| `navy` | 4 | 14 | Navy and Marine Corps publications (Internet Archive); 17 U.S.C. 105 |
| `nbs` | 1 | 5 | National Bureau of Standards publications (Internet Archive); 17 U.S.C. 105 |
| `wxb` | 1 | 1 | Weather Bureau publication (Internet Archive); 17 U.S.C. 105 |

`sources.tsv` lists only these used documents. Publication years are 1890 to 2008 (83 of the documents are from 1940 or later; older scans hold
few stand-alone heading lines); every document is hashed (`raw_sha256` of the download, `text_sha256` of the
extracted text) and carries its retrieval date in `sources.tsv`. The same pool of documents feeds [the compound source](../compound/README.md): 73 documents contribute to both, and no
sentence is in both.

## Leakage and License Checks

`python tools/data/check_leakage.py` compares this source with every evaluation set (open dev and test, gold v2, the shape probe, all five
tranches of the shapes development set, the STE challenge set, and UD English-EWT train, dev and test when the git-ignored cache is present;
the probe is reached through its sentence column only) and with the other training sources (open train, MASC, silver, imperative and
compound). Result: **0 document overlaps, 0 exact sentence overlaps and 0 near duplicates** for every pair, except that this source and the
compound source share documents by design (reported, not gated; their sentences never match); [leakage-report.tsv](../leakage-report.tsv)
and [training-leakage-report.tsv](../training-leakage-report.tsv) have the counts. Near duplicates are defined for sentences of at least 10
words, so a heading line of 2 to 10 words cannot be one; the exact-sentence rule (the lower-case alphanumerics of the line) applies to every
line. `check_licenses.py` scans `sources.tsv`, the excluded-source record and this README. `check_conventions.py` lints `headings.tsv` (lint
only: the corrections overlay never addresses it): the fix tier finds no violation, which the lint run enforces, and the check tier
reports none.

## Lexicon Coverage

The lexicon is not built from this source, and the file does not change it (`lexicon/lexicon.tsv` is unchanged). Measured against
`lexicon/lexicon.tsv` (`python tools/data/compound_heading_data.py --work <work-folder> report --source headings` prints the figures):

| Measure | Listed | Of | Share |
| --- | ---: | ---: | ---: |
| word tokens whose gold tag the lexicon lists for the spelling (tokens without a letter or digit are not counted) | 1,508 | 1,565 | 96.36 percent |
| distinct (lower-cased spelling, tag) pairs the lexicon lists | 895 | 948 | 94.41 percent |

Of the 53 unlisted pairs, 29 are spellings that the lexicon does not contain at all and 24 are spellings that the lexicon lists with other tags
(for example `forests/PROPN`). 15 of the 53 contain a digit (numbers, ZIP codes, report numbers); the other 38 are hyphenated compounds
(`data-base/NOUN`), proper names (`Galena/PROPN`) and OCR residue. By tag: PROPN 27, NUM 13, NOUN 6, ADJ 5, X 2. In training with the
`history` restriction a gold tag that the lexicon misses is allowed, so these pairs do not block training.

## Known Limits

- **Model labels, no human review.** Two models agree on every token, which removes the lines where they differ, not the errors that they
  share. The model review (see Hand Check) is a sample, not a human review; the residual error of the source is not measured by a human.
- **No imperative training mass.** 8 lines have an initial imperative judged as VERB. The labelers read the first word of almost every
  imperative-shaped heading as a noun, an adjective or a proper noun, and the class rule dropped those lines. The source therefore teaches
  the noun reading of a Title Case word and the proper-noun boundary, and next to nothing about a Title Case instruction (`Check Fluid
  Level`).
- **Short units.** A line has 2 to 10 tokens (4.4 on average), so the source adds few tokens to a training run, and the context that a line
  gives is small.
- **Register and era.** The text is the headings of bulletins, reports and manuals, mostly from scans from 1940 to 1990; vocabulary and
  punctuation differ from web text, and OCR residue remains in some lines (the noise filter removed the clear cases only).
- **Frame, not coverage.** The lines have a frame hit of `shapes_dev_extra.heading_hits` (a capitalized word that the lexicon lists as noun and
  verb, or as a proper noun only); headings made only of unambiguous words are not selected.
- **Caps shape the sample.** At most 4 lines per lemma and 12 to 25 per document, and the noun and proper strata are capped (340 and 60), so
  frequent heading words are under-represented relative to the text.
- **No evaluation use.** The lines are not independent of the sourcing of the shapes development set (same families of federal text), but no
  document, no line and no near duplicate is shared with it or with any other evaluation set.

## Use

Train only. `tools/DemaConsulting.PartOfSpeech.Trainer/training.json` lists the source as `headings` with `"weight": 0` (committed, not part of
the shipped mix; `train --check` reproduces the shipped weights); `--source headings:1` adds it to a training run (the trainer run log, section
"Compound and heading training data: results", has the measured effect of the source at weight 1). The trainer refuses the
evaluation files by name and admits this file because it is a training source under `data/`. Do not evaluate on it: its lines are selected by
a frame and labeled by models, so a score on them says nothing about held-out text.

## Second Source: `headings-2.tsv`

**What it is.** `headings2` has 247 real Title Case lines (1,160 tokens, 2 to 10 tokens each) from 59 US federal-government documents, in two
strata, every token tagged by two blind model labelers who agree on it (every status is `unanimous`):

| Stratum | Lines | Judged word (token 0 of the line) |
| --- | ---: | --- |
| `imperative` | 165 | the first word of an instruction line, a base verb: VERB (`Splint A Suspected Fracture`, `Locate a Target by Shift from a Known Point`) |
| `initial-noun` | 82 | the first word of a noun-phrase heading whose first word can also be a verb: NOUN (the contrast stratum: the same kind of first word as a noun) |

There are 144 distinct lemmas of the judged word (76 in the imperative stratum, 69 in the initial-noun stratum), at most 6 lines per lemma
and at most 24 per document (the cap is 25); the imperative lines come from 30 of the 59 documents. The block format is that of `headings.tsv`
(comments `sent_id`, `source_id`, `text`, `word`, `lemma`, `stratum`); the stratum names are `imperative` and `initial-noun`. The units are lines,
not sentences: no final period, and a number or label in front of a step title (`Task 3`, `Step 2.1`) is not part of the text.

**The imperative stratum is short of the requested target, plainly stated.** The request was about 300 imperative lines; the file has 165 (the
registered floor is 150, the cap 400). The supply of real Title Case instruction lines in the new documents, not a labeling rule, is the limit:
the strong frame found 193 candidate lines on the training side. Of the 165 lines, **41 come from the `initial-noun` frame** (the first word is
a capitalized noun-capable word followed by another capitalized noun-capable word, the shape that the labelers read as a noun, an adjective or a name
in 398 of 439 cases) **and are in the imperative stratum because both labelers tagged token 0 as VERB**; the other 124 come from the strong
imperative frame. The 41 lines are kept as the rule's code assigns strata (the stratum of a kept line is decided by the agreed tag of the judged
word, whatever frame the candidate came from); this reading is a decision of the implementing agent, not of the project owner. Without them the
imperative stratum would be 124 lines and the initial-noun stratum at most 62. The `initial-noun` stratum is capped at half of the kept imperative
lines (82); 235 further lines were dropped at that cap.

`headings-2.tsv` has SHA-256 `3f1fca247aa625c6f46269f5db5034540e4685fc55918dff5068da6ba591fca5` and `headings-2-sources.tsv`
`accd6790b8226e962d8d192b7658375f1bafb7e1721a11779ee55e05024b682d`; [MANIFEST.tsv](../MANIFEST.tsv) lists the size, hash and record counts of every file of this folder.

**Files.**

| File | Content |
| --- | --- |
| `headings-2.tsv` | the 247 lines in the corpus block format of [CONVENTIONS.md](../CONVENTIONS.md); ids `headings2-NNNN` |
| `headings-2-sources.tsv` | the 59 documents that the lines come from: family, title, URL, author, year, license basis (the quoted statement), terms URL, retrieval date, hashes, lines used |
| `headings-2-excluded-documents.tsv` | documents left out after the build: none (header only) |
| `headings-2-dropped-sentences.tsv` | counts of candidate lines dropped alone because of an overlap with a set (3 equal a set sentence, 20 equal a shapes development sentence, 1 is a near duplicate of one; counts only, no text) |

**Source and basis.** The lines are text of 59 documents that are US federal government works (17 U.S.C. 105), one `license_basis` cell per
document in `headings-2-sources.tsv`, from the Internet Archive and hashed (the retrieval date is in the sources table); publication years are 1923 to 2025 (57 of the
documents are from 1940 or later). The basis checks and exclusions are those of the second compound source (see
[its README](../compound/README.md#second-source-compound-2tsv)): no FAA text, no Wikipedia, no share-alike or non-commercial text, no Universal
Dependencies text, no sentence supplied by any consumer, no document with a non-federal creator, publisher or co-author, and no document in a table
of used sources or in either excluded-sources record. The same pool of training-side documents feeds `compound2`: 57 documents contribute to
both, and no line is a sentence of the other.

| Family | Documents | Lines | Origin |
| --- | ---: | ---: | --- |
| `army` | 37 | 192 | War Department and Department of the Army manuals (soldier's manuals, field manuals, training publications) |
| `usda` | 8 | 23 | Department of Agriculture publications |
| `phs` | 2 | 11 | Public Health Service publications |
| `airforce` | 1 | 5 | Air Force manuals |
| `navy` | 3 | 5 | Navy publications |
| `marine` | 2 | 3 | Marine Corps publications |
| `usfs` | 3 | 3 | Forest Service publications |
| `other` | 1 | 3 | other federal bodies |
| `coastguard` | 1 | 1 | Coast Guard publication |
| `usbr` | 1 | 1 | Bureau of Reclamation publications |

**Selection and labeling rule.** The normative text is the docstring of `tools/data/followup_data.py` (the trainer run log, section
"Follow-up", records the rule, its pins and its amendments). In short:

1. **Lines** come from three places in the OCR text: a heading line that stands alone above a paragraph, a table-of-contents line (the dot
   leaders and the page number are removed), and a labeled step title (`Task`, `Step`, `Procedure`, `Operation`, `Exercise` or `Job` plus a
   number or letter, removed with the label); then the unchanged noise filter of the headings tranches (2 to 10 tokens, Title Case, no
   all-capital word, no unknown word, no page number) and the equal and near-duplicate filters against every set. A title that stands in the table
   of contents, on its task page and in a cross reference is one line. ALL-CAPS and sentence-case step titles are not in scope.
2. **Frames** use position and lexicon only. `imperative`: token 0 is capitalized, is its own base form, is listed with VERB (a verb-only word, or
   noun and verb), and token 1 is a determiner, an object pronoun, an adverb of the shared list or a particle (`up down out off back away`).
   `initial-noun`: token 0 is capitalized, is its own base form, is listed as noun and verb, and token 1 is a capitalized noun-capable word
   (`Mass Characteristics`). A line is one candidate.
3. **Documents** are on the training side (whole title families, assigned by a seeded hash before any label); no document of the file is in a
   development tranche.
4. **Labels:** every candidate is tagged from scratch, token by token, by `claude-opus-5.5` and `gpt-6.1-sol`, which see opaque ids and tokens
   only. **No human reviewed a label.**
5. **Kept lines:** both labelers agree on every token, the convention linter reports no fix-tier violation, and token 0 has the tag of its
   stratum (VERB in the base form for `imperative`, NOUN for `initial-noun`); any other combination is dropped and counted, never edited by hand.
6. **Caps** in a seeded order: 6 lines per lemma, 25 per document, `imperative` 150 to 400 lines, `initial-noun` at most half of the kept
   imperative lines. `build` refuses below a floor unless `--allow-short` (the miss is disclosed, never padded).

| Stage | Lines |
| --- | ---: |
| candidates labeled (imperative frame 137, initial-noun frame 450) | 587 |
| dropped: the labelers disagree on token 0 (target disagreement) | 11 |
| dropped: token 0 read as another class (class mismatch) | 75 |
| dropped: a disputed token elsewhere in the line | 19 |
| dropped by the `initial-noun` cap (half of the kept imperative lines) | 235 |
| **in `headings-2.tsv`** | **247** (imperative 165, initial-noun 82) |

**Hand check (model review, not a human review).** One sample of 60 lines (40 `imperative`, 20 `initial-noun`; seed 20261124) plus 4 control
items whose tag had been swapped on purpose was drawn once from the data as built and reviewed once by `claude-sonnet-5.5` against
[CONVENTIONS.md](../CONVENTIONS.md): the judged word is correct in 60 of 60 (100.0 percent; the gate is 95 percent), 60 of 60 lines (100.0
percent) have no other token marked wrong, token precision is 100.00 percent (0 tokens wrong of 295; reported, not gated), and 0 of the 4
controls were accepted (the gate allows 1). It is not independent of the labelers' shared blind spots.

**Leakage and license checks.** `python tools/data/check_leakage.py` finds **0 document overlaps, 0 exact sentence overlaps and 0 near
duplicates** against every evaluation set (open dev and test, gold v2, the shape probe, all seven tranches of the shapes development set, the
STE challenge set, UD English-EWT) and against open train, MASC, silver, imperative, `compound` and `headings`; it shares 57 documents (and no
line) with `compound2` (reported, not gated). [training-leakage-report.tsv](../training-leakage-report.tsv) and
[leakage-report.tsv](../leakage-report.tsv) have the counts. `check_licenses.py` scans `headings-2-sources.tsv`. `check_conventions.py` lints
`headings-2.tsv` (lint only; its own column `headings2`): 0 violations at the fix tier and 0 at the check tier. `lexicon/lexicon.tsv` is unchanged.

**Known limits.**

- **Imperative mass is short.** 165 imperative lines against the requested about 300; 41 of them (24.8 percent) come from the `initial-noun`
  frame, whose lines the labelers mostly read as nouns, adjectives or names. 158 of the 165 are from Army documents (usda 5, phs 1, marine 1);
  30 documents supply the imperative lines.
- **Strata are not independent samples of the text.** The `initial-noun` stratum is a contrast set that the rule caps at half of the imperative
  lines; it teaches the noun reading of the same kind of first word and is not a random sample of noun headings.
- **Model labels, no human review.** Two models agree on every token; the review above is a sample of 60.
- **Short units.** A line has 2 to 10 tokens (4.7 on average), so the source adds few tokens (1,160) to a training run.
- **Register.** Step titles and table-of-contents lines of manuals, mostly Army publications (192 of 247 lines, 158 of the 165 imperative lines); vocabulary and punctuation
  differ from web text; OCR residue remains in some lines.
- **Frame, not coverage.** Only the two frames above are selected; ALL-CAPS and sentence-case step titles, and instruction lines whose second token is
  something else, are not.
- **Caps shape the sample:** at most 6 lines per lemma and 25 per document.

**Use.** Train only. `training.json` lists the source as `headings2` with `"weight": 0` (not part of the shipped mix; `train --check` reproduces the
shipped weights); `--source headings2:1` adds it to a training run. The trainer refuses the evaluation files by name and admits this file because it
is a training source under `data/`. Do not evaluate on it.
