<!-- markdownlint-disable MD013 MD060 -->
<!-- cspell:ignore usfs usbm usbr usgs wxb nbs docstrings -->

# Heading Training Source

License: CC0-1.0

375 real Title Case heading, caption and step-title lines from 90 US federal-government documents (`Fuel Filter Replacement`,
`Pacific Northwest Region`), each with a judged word in one of three groups: a common noun, an initial imperative verb or a true proper
noun. Every token has a Universal POS tag on which two blind model labelers agree. It is a **training** source: it shows that the capital
letters of a Title Case line do not make its words proper nouns, which the sentence-shaped training corpora rarely show, and it is disjoint
from every evaluation set. It is registered in the trainer configuration with weight 0, so it is not part of the mix that trained the
embedded model (`--source headings:1` adds it to a training run).

## Source and Provenance

- **Source:** the lines are text of 90 documents that are US federal government works (17 U.S.C. 105): Department of Agriculture, Forest
  Service, Bureau of Reclamation, Public Health Service, Bureau of Land Management, Bureau of Mines, Geological Survey, War Department and
  Army, Navy, Bureau of Standards and Weather Bureau publications, quoted per document in `sources.tsv`. No document is in any other
  source table of this repository, none is in an evaluation set, and nothing was written for this source: all lines are real text (0
  authored).
- **How produced:** `python tools/data/compound_heading_data.py --work <work-folder> build --source headings --allow-short` with the
  `--omit-document` options listed in `excluded-documents.tsv`, at the builder and the rule of commit `e776f5693739900d91e63fd2a12451da1f4ba90b`
  (the builder, its tests and the pre-registered rule, committed before any label of this source existed; the option `--omit-document` was
  added after the labeling and is the only change of the builder since). `--allow-short` is the waiver that the rule registers for a stratum floor
  that the labelers' agreement does not fill (see "Counts"). The labeling used the unchanged commands of `tools/data/shapes_dev.py` (`export`,
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
  author is not used (`excluded-documents.tsv`).

`headings.tsv` has SHA-256 `5931072928829e1911468173a181761ba001c4d7f16a8492391404395649124c` and `sources.tsv`
`428a73f0581ec168cdef63d885cd6e001e925813b0a0d0dd1148f79636935aff`; [MANIFEST.tsv](../MANIFEST.tsv) lists the size, hash and record
counts of every file of this folder.

## Files

| File | Content |
| --- | --- |
| `headings.tsv` | the 375 lines in the corpus block format of [CONVENTIONS.md](../CONVENTIONS.md) ("Other Formats" describes the header and the extra comments); ids `headings-NNNN` |
| `sources.tsv` | the 90 documents that the lines come from (only used documents): family, title, URL, author, year, license basis (the quoted statement), terms URL, retrieval date, hashes, lines used |
| `excluded-documents.tsv` | documents left out: the 7 whose record lists a state or local government body as an author (the rule 1 check after the build) |
| `dropped-sentences.tsv` | counts of candidate lines dropped alone because they equal a sentence of a training or evaluation set or of the shapes development set (counts only, no text) |

Each block has the comments `sent_id`, `source_id`, `text`, `word`, `lemma` and `stratum`, then one row `form`, `upos`, `status` for each
token. `word` is the zero-based token index of the judged word, `lemma` its lower-case form, and `stratum` is `noun`, `imperative` or
`proper`. Every status is `unanimous` (no token is `disputed` or masked), with the flags that both labelers agreed on. The unit is a line,
not a sentence: it has no final period, and a numbering or label in front of it is not part of the text.

## Counts

375 lines (1,668 tokens, 2 to 10 tokens each) with 273 distinct lemmas of the judged word in 90 documents. At most 4 lines per lemma and at most
12 per document (the cap is 25). The judged word is tagged exactly as the convention says:

| Stratum | Lines | Judged word |
| --- | ---: | --- |
| `noun` | 315 | a Title Case common noun that ends its noun phrase: NOUN |
| `proper` | 52 | a word that names one particular entity: PROPN |
| `imperative` | 8 | the first word of an instruction line, a base verb: VERB |

The registered floors are 150 noun, 100 imperative and 40 proper lines. The imperative stratum is below its floor (8 lines): 346 of the 356
initial-imperative candidates were read by both labelers as a noun, an adjective or a proper noun, and the rule drops a line whose agreed tag is not the one of the convention for its stratum. Those lines are
dropped and counted, never kept with another tag and never corrected by hand. **The source therefore has almost no imperative training
mass.**

## Labeling and Decision Rule

The rule was fixed before any label of this source was seen (the commit above); the docstring of `tools/data/compound_heading_data.py` is its
normative text.

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
   government body as an author. Their lines are removed (`--omit-document`: after the caps, nothing is added), and the documents are listed
   in `excluded-documents.tsv`.

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
| removed with the 7 documents (rule 7) | 33 |
| **in `headings.tsv`** | **375** |

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
| `nbs` | 2 | 6 | National Bureau of Standards publications (Internet Archive); 17 U.S.C. 105 |
| `wxb` | 1 | 1 | Weather Bureau publication (Internet Archive); 17 U.S.C. 105 |

`sources.tsv` lists only these used documents. Publication years are 1890 to 2008 (83 of the documents are from 1940 or later; older scans hold
few stand-alone heading lines); every document was retrieved on 2026-11-09 and hashed (`raw_sha256` of the download, `text_sha256` of the
extracted text). The same pool of documents feeds [the compound source](../compound/README.md): 73 documents contribute to both, and no
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
| word tokens whose gold tag the lexicon lists for the spelling (tokens without a letter or digit are not counted) | 1,512 | 1,569 | 96.37 percent |
| distinct (lower-cased spelling, tag) pairs the lexicon lists | 898 | 951 | 94.43 percent |

Of the 53 unlisted pairs, 29 are spellings that the lexicon does not contain at all and 24 are spellings that the lexicon lists with other tags
(for example `forests/PROPN`). 15 of the 53 contain a digit (numbers, ZIP codes, report numbers); the other 38 are hyphenated compounds
(`data-base/NOUN`), proper names (`Galena/PROPN`) and OCR residue. By tag: PROPN 27, NUM 13, NOUN 6, ADJ 5, X 2. In training with the
`history` restriction a gold tag that the lexicon misses is allowed, so these pairs do not block training.

## Known Limits

- **Model labels, no human review.** Two models agree on every token, which removes the lines where they differ, not the errors that they
  share. The model review of 45 lines found 1 wrong token of 219; the residual error of the 375 lines is not measured by a human.
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
the shipped mix; `train --check` reproduces the shipped weights); `--source headings:1` adds it to a training run. The trainer refuses the
evaluation files by name and admits this file because it is a training source under `data/`. Do not evaluate on it: its lines are selected by
a frame and labeled by models, so a score on them says nothing about held-out text.
