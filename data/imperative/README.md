<!-- markdownlint-disable MD013 MD060 -->
<!-- cspell:ignore usfs usbm -->

# Imperative Training Source

License: CC0-1.0

900 real sentences from 122 US federal-government instructional documents, each starting with an imperative verb in its base form
(`Check the oil level.`, `Remove the cover and clean the contacts.`). Every token has a Universal POS tag on which two blind model
labelers agree. It is a **training** source: it holds the sentence type that is rare in the other training corpora and frequent in
manuals, and it is disjoint from every evaluation set. It is registered in the trainer configuration with weight 1, so it is
part of the mix that trained the embedded model (`--source imperative:0` trains without it).

## Source and Provenance

- **Source:** the sentences are text of 122 documents that are US federal government works (17 U.S.C. 105): War Department and Army
  field and technical manuals, Navy training manuals, Department of Agriculture bulletins, Forest Service, Bureau of Mines and Public
  Health Service publications, quoted per document in `sources.tsv`. No document is in any other source table of this repository,
  none is in an evaluation set, and nothing was written for this source: all 900 sentences are real text (0 authored).
- **How produced:** `python tools/data/imperative_data.py --work <work-folder> build` at commit
  `100e747cb0b31deff4f10d89b426001bf8c95f16` (the builder and the rule below, committed before any label of this source existed),
  after the unchanged blind-labeling commands of `tools/data/shapes_dev.py` (`export`, `check-batches`, `validate-output`, `decide`).
  The global `--work` argument (a folder outside the repository, required) comes before the command; the other options keep their
  defaults (`--lemma-cap 6`, `--document-cap 30`, `--max 900`, `--min 600`, `--out-dir` the repository's `data/imperative`). The work
  folder is not committed. It holds the extracted text of the sources (`sources-draft.tsv`, `text/<source_id>.txt`, one sentence per
  line), the tokens of every sentence (from `shapes_dev.py tokenize`), the candidates, the blind batches, the two labeler outputs and the
  private decisions (`private/`); the layout is in the docstring of `shapes_dev.py` and of `imperative_data.py`. The scripts that found and
  fetched the documents are one-off helpers and are not committed; the record of what was fetched is `sources.tsv` (URL, retrieval date,
  SHA-256 of the download and of the extracted sentences of every document). `imperative.tsv` is never edited by hand.
- **Labels:** written by two language models that followed [CONVENTIONS.md](../CONVENTIONS.md) and saw only a batch of opaque ids and
  tokens: `claude-opus-5.5` and `gpt-6.1-sol`. A sentence is kept only when the two agree on the tag of every token. **No human reviewed a
  label.** The project owner dedicates the labels and the selection to the public domain under CC0 1.0 (the owner's position on the rights in
  model outputs is stated in the repository README and is not legal advice).
- **Sentences:** public domain in the United States as works of the federal government; the basis of every document is quoted in
  `sources.tsv` (the statute, the Public Domain Mark of the Internet Archive record, and the check that the text carries no copyright,
  permission or third-party notice). There is no FAA text, no Wikipedia, no share-alike or non-commercial text and no Universal
  Dependencies text (`check_licenses.py` scans `sources.tsv`).

`imperative.tsv` has SHA-256 `218246df0c4ce739e6631a066e5296194d6773e09fc849cd64c1eb93516185ad` and `sources.tsv`
`46ed6556224f2adcd73da7f8c8b026597a3a72f85dc7f6f8284231f003699df5`; [MANIFEST.tsv](../MANIFEST.tsv) lists the size, hash and record
counts of every file of this folder.

## Files

| File | Content |
| --- | --- |
| `imperative.tsv` | the 900 sentences in the corpus block format of [CONVENTIONS.md](../CONVENTIONS.md) ("Other Formats" describes the header); ids `imperative-NNNN` |
| `sources.tsv` | the 122 documents that the sentences come from (only used documents): family, title, URL, author, year, license basis (the quoted statement), terms URL, retrieval date, hashes, sentences used |
| `excluded-documents.tsv` | documents left out because they reprint or share sentences with another set (header only: none was found) |
| `dropped-sentences.tsv` | counts of candidate sentences dropped alone because they equal, or nearly equal, a sentence of a training or evaluation set (counts only, no text) |

Each sentence block has the comments `sent_id`, `source_id` and `text`, then one row `form`, `upos`, `status` for each token. Every status
is `unanimous` (no token is `disputed` or masked, and no flag is set). The tokens are the library tokenizer's: they come from
`shapes_dev.py tokenize`, which runs the library tokenizer.

## Counts

900 sentences, 11,946 tokens, 351 distinct lemmas of the first word, 122 documents. At most 6 sentences per lemma (the most frequent
lemmas: `assist`, `attach`, `bend`, `close`, `cover`, `follow`, `get` and `give`, 6 each) and at most 30 sentences per document.
Token 0 of every sentence is tagged VERB and is the base form of the verb; a data test asserts this, the quotas and the format.

## Labeling and Decision Rule

The rule was fixed before any label of this source was seen (the commit above); the docstring of `tools/data/imperative_data.py` is its
normative text. Later commits changed only the wording of the source families in the docstring and made the fix-tier lint of `build`
take the resources of its participle rules from the correctable corpora only (as `check_conventions.py` does); the output of `build`
is byte-identical to the committed files.

1. **Documents.** US federal authorship, with the public-domain basis checked one document at a time and written in the `license_basis`
   cell. The id, the normalized URL and the Internet Archive identifier of a document are in no table of used sources (training and
   evaluation sources and the shapes development set); a document that shares 3 or more sentence keys with a training or evaluation set,
   with the shapes development set or with the full text of a document of that set is rejected as a whole (0 of 171 were).
2. **Candidates.** A sentence is a candidate when it passes the cleanliness filters of `shapes_dev.py` (3 to 30 tokens, one sentence, no
   OCR junk, citation, enumeration, title or repetition noise), equals no training, evaluation or shapes development sentence, shares no
   word 5-gram with the held-out shape probe, is not a near duplicate of a shapes development sentence (at least 10 words, at least 80
   percent of the word 6-grams), and has the `v-imperative-initial` frame of `shapes_dev.shape_hits`: token 0 is capitalized, is listed in the
   lexicon as both noun and verb, is its own base form, is no determiner, preposition or auxiliary, and the next token is a determiner,
   preposition, object pronoun, a following adverb or a quantity word. At most 9 candidates per lemma and 2,300 in all, in a seeded order
   that no model score influences.
3. **Labels.** Every candidate is tagged from scratch, token by token, by the two blind labelers through the batch commands of
   `shapes_dev.py`; the batch files carry an opaque id, a token index and the token, and the labelers see no shape, source, lemma or intended
   class.
4. **Kept sentences.** A candidate is kept when both labelers give token 0 the same tag, VERB, and its lower-cased form equals its base form
   (the decision of `shapes_dev.decide_item`), when every other token is agreed too (no disputed token), and when the convention linter
   reports no fix-tier violation on it. A first word that both labelers read as a noun is dropped. No label is changed by hand.
5. **Caps.** In a seeded order the build takes a kept sentence unless its lemma already has 6 sentences, its document already has 30, or 900
   are taken.
6. **Quota gate.** `build` refuses (exit code 1, nothing written) unless there are 600 to 900 sentences, at most 6 per lemma, at most 30
   per document, token 0 is VERB in the base form in every sentence, every token is `unanimous` and the tokens rejoin to the text.

Order of events (all on 2026-11-08): the documents were sourced and the builder with this rule was committed (`100e747`, 15:29); the
2,300 candidates were exported in 16 blind batches and both labelers tagged every batch; the decision and the build followed; the
hand check below was run on the built data; then the checks, the leakage report and this registration. Every candidate was
labeled before the build; the caps, the quota gate and the hand check were applied to the output of the labelers and changed no label.

Numbers of the run:

| Stage | Sentences |
| --- | ---: |
| sentences of the sourced text (171 documents) | 125,902 |
| clean sentences kept for the frame | 96,344 |
| frame hits (705 lemmas) | 6,753 |
| frame hits whose first word is not its base form (gerund, past or third person form; not counted above) | 903 |
| candidates after the lemma cap (9) and the total cap (2,300), 641 lemmas | 2,300 |
| kept by the decision (12 target disagreement and 693 class mismatch dropped) | 1,595 |
| dropped for a disputed token (168 disputed non-target tokens in the kept items) | 143 |
| dropped for a fix-tier violation of the linter | 1 |
| dropped by the lemma cap (6), the document cap (30) and the total cap (900) | 58, 131 and 362 |
| **in `imperative.tsv`** | **900** |

Candidate sentences dropped alone because of an overlap (`dropped-sentences.tsv`): 1 equals a sentence of a set and 4 are near
duplicates of a shapes development sentence; 1 more shares a word 5-gram with the held-out probe. Of the 171 sourced documents, 49
contribute no sentence (their candidates were cut by a cap or by the decision).

**Refusals of one labeler.** `claude-opus-5.5` had no refusals. `gpt-6.1-sol` returned empty refusals (`cannot assist`, with no labels) for
several requests, and those batches were requested again from fresh agents: batch b001 twice (one earlier output set was invalid and
partial and was set aside, unused), b004 three times, b006 twice, b007 three times, b008 twice, b010 four times and b012 twice. Every batch
ended with one valid output per labeler, and each labeler's accepted output for a batch is the only labeling used. Which request
produced the accepted output does not enter the data.

## Hand Check

A third model, `claude-sonnet-5.5`, reviewed 60 random sentences of the final data (drawn with the fixed seed 20261110) against the
conventions. It saw each sentence with every token and its tag, and judged the first word (and marked any other wrong token). This is a
**model review, not a human review**, and it is not independent of the labelers' shared blind spots.

- **Target token:** 60 correct, 0 incorrect and 0 unclear of 60, **precision 60/60 = 100.0 percent**. The gate was 95 percent (3 incorrect of
  60 is exactly 95.0 percent and passes; 4 do not).
- **Whole sentence:** 59 of 60 sentences (98.3 percent) have no other token marked wrong; **token precision 99.87 percent** (1 token wrong of
  761). These figures are reported, not gated.
- **Leniency control:** 4 control items whose tag had been swapped to the wrong class were slipped into the review unmarked; the reviewer
  judged all 4 incorrect (0 of 4 accepted), so it was not accepting every item.

One sample was drawn and reviewed once; it was not drawn again.

## Sources

| Family | Documents | Sentences | Basis and origin |
| --- | ---: | ---: | --- |
| `army` | 69 | 643 | War Department and Department of the Army field and technical manuals (scans and electronic copies on the Internet Archive); 17 U.S.C. 105 |
| `usda` | 36 | 166 | Department of Agriculture farm, home and food bulletins and leaflets (Internet Archive); 17 U.S.C. 105 |
| `navy` | 12 | 68 | Navy training and drill manuals and handbooks (Internet Archive); 17 U.S.C. 105 |
| `usfs` | 3 | 11 | Forest Service publications (Internet Archive); 17 U.S.C. 105 |
| `usbm` | 1 | 7 | Bureau of Mines ground-control instructions (Internet Archive); 17 U.S.C. 105 |
| `phs` | 1 | 5 | Public Health Service handbook (Internet Archive); 17 U.S.C. 105 |

`sources.tsv` lists only these used documents. Publication years are 1889 to 2011 (most are from 1940 to 1969); every document was
retrieved on 2026-11-08 and hashed (`raw_sha256` of the download, `text_sha256` of the extracted sentences).

## Leakage and License Checks

`python tools/data/check_leakage.py` compares this source with every evaluation set (open dev and test, gold v2, the shape probe, both
tranches of the shapes development set, the STE challenge set, and UD English-EWT train, dev and test when the git-ignored cache is
present; the probe is reached through its sentence column only). Result: **0 document overlaps, 0 exact sentence overlaps and 0 near
duplicates** for every pair; [leakage-report.tsv](../leakage-report.tsv) has the counts. `check_licenses.py` scans `sources.tsv` and
this README. `check_conventions.py` lints `imperative.tsv` (lint only: the corrections overlay never addresses it): the fix tier finds no
violation, which the lint run enforces, and the check tier reports none.

## Lexicon Coverage

The lexicon is not built from this source, and the file does not change it (`lexicon/lexicon.tsv` is unchanged). Measured against
`lexicon/lexicon.tsv` (`python tools/data/imperative_data.py --work <work-folder> report` prints the figures):

| Measure | Listed | Of | Share |
| --- | ---: | ---: | ---: |
| word tokens whose gold tag the lexicon lists for the spelling (tokens without a letter or digit are not counted) | 10,394 | 10,621 | 97.86 percent |
| distinct (lower-cased spelling, tag) pairs the lexicon lists | 2,679 | 2,836 | 94.46 percent |

All 900 first words are listed with VERB. Of the 157 unlisted pairs, 141 are spellings that the lexicon does not contain at all and 16 are
spellings that the lexicon lists with other tags (for example `facts/PROPN`). 96 of the 157 contain a digit (numerals such as `1,000`
and measure compounds such as `10-inch/ADJ`); the other 61 are hyphenated compounds (`cut-off/NOUN`, `crew-served/ADJ`), proper names
(`Agenda/PROPN`) and OCR residue. By tag: NUM 66, PROPN 33, NOUN 27, ADJ 24, X 5, VERB 2.
In training with the `history` restriction a gold tag that the lexicon misses is allowed, so these pairs do not block training.

## Known Limits

- **Model labels, no human review.** Two models agree on every token, which removes the sentences where they differ, not the errors that
  they share. The model review of 60 sentences found 1 wrong token of 761; the residual error of the 900 sentences is not measured by a
  human.
- **VERB first words only.** Only sentences whose first word both labelers read as a verb in the base form are kept: 693 of the 2,300
  candidates (30 percent) were read as nouns or another class by at least one labeler and were dropped. The source therefore teaches
  "an ambiguous word that starts a sentence before a determiner or preposition is a verb" and shows no counter-example; a model that
  trains on it can lean toward the verb reading for noun-initial sentences.
- **Register and era.** The text is instructional (manuals, bulletins, leaflets), 71 percent from the Army, and mostly scans from the
  1940s to the 1960s with a few modern manuals; vocabulary and punctuation differ from web text. OCR residue (broken words, garbled
  numerals) remains in some sentences; the noise filter removed the clear cases only.
- **Frame, not coverage.** The sentences have the `v-imperative-initial` frame of the shapes development set (a lexicon-ambiguous first
  word before a determiner, preposition, object pronoun, adverb or quantity word). Imperatives that start with an unambiguous verb
  (`Stand ...`) or are followed by another word class are not selected.
- **Caps shape the sample.** At most 6 sentences per lemma and 30 per document, so the frequent verbs (`check`, `use`, `see`) are under-represented
  relative to the text.
- **No evaluation use.** The sentences are not independent of the sourcing of the shapes development set (same families of federal text), but
  no document, no sentence and no near duplicate is shared with it or with any other evaluation set.

## Use

Train only. `tools/DemaConsulting.PartOfSpeech.Trainer/training.json` lists the source as `imperative` with `"weight": 1` (part of the
shipped mix; `train --check` reproduces the shipped weights); `--source imperative:0` leaves it out of a training run. The trainer refuses the
evaluation files by name and admits this file because it is a training source under `data/`. Do not evaluate on it: its sentences are
selected by a frame and labeled by models, so a score on them says nothing about held-out text.
