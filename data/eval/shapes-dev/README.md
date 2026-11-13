<!-- markdownlint-disable MD013 MD060 -->
<!-- cspell:ignore usfs usbm ncwm nltk fcda usbr safehandlingofbo airforce Jaccard coastguard usace -->

# Shapes Development Set

License: CC0-1.0

700 real sentences from 62 US federal-government documents, each with one judged word that is both a noun and a verb in
the lexicon, in one of five hard sentence shapes: 600 target items (120 for each shape) and 100 controls (the same spelling
in the other class). Every token has a Universal POS tag from two blind model labelers. It is the **development** set for
the sentence-context work: use it to select settings and to compare variants. **Development only**: never train on it and
never report it as a held-out figure (the [shape probe](../shapes/README.md) stays the held-out evaluation).

A second file, `hard.tsv`, is the **hard tranche**: 460 real sentences (400 target items and 60 controls) of the contested cases
of four of the five shapes, built by a model-independent rule (see [Hard Tranche](#hard-tranche)). Three more
files, `compound-head.tsv`, `headings.tsv` and `named-state.tsv`, are the **new tranches**: one shape each that the first two
files do not measure (see [New Tranches](#new-tranches)). Two further files, `compound-head-2.tsv` and `headings-2.tsv`, are the
**follow-up tranches**: 255 and 129 more items of the shapes of `compound-head` and `headings`, from new documents (see
[Follow-up Tranches](#follow-up-tranches)). Four further files, `deverbal-heads.tsv`, `defined-terms.tsv`, `step-lines.tsv` and
`clause-verbs.tsv`, are the **technical tranches**: 450, 285, 236 and 446 sentences of four shapes of technical English, from further new
documents (see [Technical Tranches](#technical-tranches)). Everything above and below this paragraph that gives a count
describes the standard tranche (`dev.tsv`) unless it says otherwise.

## Source and Provenance

- **Source:** the sentences are text of 62 documents that are US federal government works (17 U.S.C. 105): Army, Navy,
  Bureau of Mines, Department of Agriculture, Forest Service and NASA publications, quoted per document in
  `sources.tsv`. No document is in any other file of this repository except `hard-sources.tsv` (the hard tranche
  shares part of these documents on purpose; no sentence is shared), and no sentence is in a training or evaluation set.
  Nothing is written for this set: all 700 sentences are real text (0 authored). Two documents are excluded (see
  [Excluded Sources](#excluded-sources)).
- **How produced:** `python tools/data/shapes_dev.py --work <work-folder> build --shape-lemma-cap n-measure=12`. The current standard
  profile rebuilds the committed files byte for byte (see `StandardRebuildTests`).
  The global `--work` argument (a folder outside the repository, required) comes before the command; `--shape-lemma-cap
  n-measure=12` is the cap that the `candidates` runs also used, and the other options keep their defaults (`--per-shape 120`,
  `--lemma-cap 4`, `--controls 100`, `--share 0.30`, `--out-dir` the repository's `data/eval/shapes-dev`). The work folder is not
  committed. It holds the extracted text of the sources (`sources-draft.tsv`, `text/<source_id>.txt`), the tokens of every
  sentence (`tokens.tsv`, from `shapes_dev.py tokenize`), the candidate rounds with their blind batches and the two labeler
  outputs (`private/`, `batches/`, `rounds/`), and `excluded-sources.tsv`; the layout is in the docstring of `shapes_dev.py`. The
  builder cleans the text, pre-selects candidates by
  position and lexicon only (never by class), exports blind batches, applies the decision rule below to the two labeler
  outputs and writes `dev.tsv`, `sources.tsv` and `excluded-sources.tsv`. `dev.tsv` is never edited by hand. A rebuild into another
  `--out-dir` from the same work folder is byte-identical to the committed files.
- **Quota gate:** `build` exits with code 1 and writes nothing unless every shape has exactly 120 targets, there are exactly 100
  controls, no lemma exceeds its cap (4 targets, 12 for `n-measure`, 2 controls) and no shape and not the controls has more than
  30% authored sentences. `--allow-short` builds anyway and lists the misses as warnings; the committed build did not need it.
  The data tests assert the same quotas on the committed `dev.tsv`.
- **Held-out isolation:** the builder sees the held-out sets (the shape probe included) only as one opaque set of hashes,
  `leakage.held_out_overlap_keys()`: hashed sentence keys of the evaluation sets and hashed word 5-grams of the probe sentences.
  The probe is read through its `sentence` column only (the braces around the target word are removed); its labels, shapes, lemmas
  and target indexes are never read or parsed. The set is used only to exclude a candidate that equals a held-out sentence or shares
  a probe 5-gram, never to choose, weight or frame a candidate. The filter removes nothing from the work folder: none of its 24,315
  sentences equals a training or evaluation sentence or shares a probe 5-gram (no authored sentence was written).
- **Excluded documents:** the builder and `check_licenses.py` read `excluded-sources.tsv` and refuse any used document, by id or by
  normalized URL (see [Excluded Sources](#excluded-sources)).
- **Process record:** this README states what the data is. The order of decisions, the labeling rounds, the top-up rounds, the build
  commits and the hand-check passes are in the trainer
  [run log](../../../tools/DemaConsulting.PartOfSpeech.Trainer/RUN-LOG.md), sections "Shapes development set: standard tranche, build
  record", "Shapes development set: hard tranche, pre-registered selection rule" and "Shapes development set: new tranches,
  pre-registered selection rule".
- **Labels:** written by two language models that followed [CONVENTIONS.md](../../CONVENTIONS.md) and saw only a batch of
  opaque ids and tokens: `claude-opus-5.5` (the first labeler, whose tag breaks ties) and `gpt-6.1-sol`. **No human
  reviewed a label.** The project owner dedicates the labels and the selection to the public domain under CC0 1.0 (the
  owner's position on the rights in model outputs is stated in the repository README and is not legal advice).
- **Sentences:** public domain in the United States as works of the federal government; the basis of every document is
  quoted in `sources.tsv`. There is no FAA text, no Wikipedia, no share-alike or non-commercial text and no Universal
  Dependencies text (the license check scans `sources.tsv`).

## Files

| File | Content |
| --- | --- |
| `dev.tsv` | the 700 sentences in the corpus block format of [CONVENTIONS.md](../../CONVENTIONS.md) ("Other Formats" describes the extra comments) |
| `sources.tsv` | the 62 documents that the sentences come from (only used documents): family, title, URL, author, year, license basis (the quoted statement), terms URL, retrieval date, hashes, sentences used |
| `excluded-sources.tsv` | the documents that are excluded from the set: `source_id`, `url`, `reason`, `decided` (see [Excluded Sources](#excluded-sources)) |
| `leakage-report.tsv` | counts of the set against every other evaluation set (documents, exact sentences, near duplicates, shared probe word 5-grams); no sentence text |
| `hard.tsv` | the 460 sentences of the hard tranche, same block format (see [Hard Tranche](#hard-tranche)); ids `shapes-dev-hard-NNNN` |
| `hard-sources.tsv` | the 99 documents of the hard tranche, same columns as `sources.tsv` |
| `hard-leakage-report.tsv` | counts of the hard tranche against every other evaluation set, the standard tranche included (shared documents are reported, not gated) |
| `compound-head.tsv` | the 120 items of the `compound-head` tranche (100 targets, 20 controls), same block format; ids `shapes-dev-compound-head-NNNN` |
| `headings.tsv` | the 115 heading lines of the `headings` tranche (95 targets, 20 controls), same block format plus `# kind`; ids `shapes-dev-headings-NNNN` |
| `named-state.tsv` | the 112 sentences of the `named-state` tranche (100 targets, 12 controls), same block format; ids `shapes-dev-named-state-NNNN` |
| `<tranche>-sources.tsv` | the documents of a new tranche (74, 51 and 59), same columns as `sources.tsv` |
| `<tranche>-leakage-report.tsv` | counts of a new tranche against every other evaluation set, the standard and the hard tranche included, and against the other new tranches (shared documents among the tranches of one generation are reported, not gated; a document shared between the first generation and the follow-up fails) |
| `compound-head-2.tsv` | the 255 items of the `compound-head-2` tranche (210 targets, 45 controls), same block format as `compound-head.tsv`; ids `shapes-dev-compound-head-2-NNNN` |
| `headings-2.tsv` | the 129 heading lines of the `headings-2` tranche (109 targets, 20 controls), same block format plus `# kind` as `headings.tsv`; ids `shapes-dev-headings-2-NNNN` |
| `compound-head-2-sources.tsv`, `headings-2-sources.tsv` | the documents of the follow-up tranches (89 and 35), same columns as `sources.tsv` |
| `compound-head-2-leakage-report.tsv`, `headings-2-leakage-report.tsv` | counts of a follow-up tranche against every other evaluation set; a shared document is allowed only with the other follow-up tranche (none with the first-generation tranches) |
| `deverbal-heads.tsv`, `defined-terms.tsv`, `step-lines.tsv`, `clause-verbs.tsv` | the 450, 285, 236 and 446 sentences of the four technical tranches, same block format as `compound-head.tsv` (comments `shape`, `role`, `target`, `lemma`, `origin`, `kind`, and where the shape has them `term`, `frame` and `unit`); ids `shapes-dev-deverbal-heads-NNNN`, `shapes-dev-defined-terms-NNNN`, `shapes-dev-step-lines-NNNN`, `shapes-dev-clause-verbs-NNNN` |
| `deverbal-heads-sources.tsv`, `defined-terms-sources.tsv`, `step-lines-sources.tsv`, `clause-verbs-sources.tsv` | the documents of the technical tranches (156, 95, 97 and 139), same columns as `sources.tsv` |
| `deverbal-heads-leakage-report.tsv`, `defined-terms-leakage-report.tsv`, `step-lines-leakage-report.tsv`, `clause-verbs-leakage-report.tsv` | counts of a technical tranche against every other evaluation set; a shared document is allowed only with another technical tranche (none with any earlier tranche) |

Each sentence block has the comments `sent_id`, `source_id`, `text`, `shape`, `role` (`target` or `control`), `target` (the
zero-based index of the judged word), `lemma` and `origin` (`real` or `authored`), then one row `form`, `upos`, `status`
for each token. The header announces a dev split. The tokens are the library tokenizer's: a test asserts that
`Tokenizer.Tokenize` reproduces every stored sentence.

## Shapes

The shape ids and definitions are those of the [shape probe](../shapes/README.md) (definitions only; no probe sentence is
used). The probe has seven shapes; this set uses five: the four that the [evaluation record](../../../docs/evaluation/README.md)
names as the weak ones (the head of a noun compound, a noun that starts a sentence, the verb that starts an instruction and
a noun in a measurement) plus `v-after-modal`, because a modal or `to` before a base verb is frequent in manuals and
procedures. `n-obj-light` and `n-modifier`, the two least frequent shapes in procedural text, are not covered.

| Shape | Gold | The judged word is |
| --- | --- | --- |
| `n-compound-head` | Noun | the head of a noun compound, after a modifier noun |
| `n-subject-initial` | Noun | the first word of the sentence and its subject, with no article |
| `n-measure` | Noun | a noun in a measurement or quantity frame with a number |
| `v-imperative-initial` | Verb | the first word of an instruction, in the base form |
| `v-after-modal` | Verb | in the base form after a modal or the infinitive marker `to` |

A control has the same lemma as a target of the shape, in a real sentence where the word has the other class (a verb for a
noun shape, a noun for a verb shape); it checks that a variant does not simply favor the shape's class.

## Counts

Target items per shape: 120, of which 0 authored. Controls: 100 in all, 0 authored.

| Shape | Target items | Authored | Controls | Authored |
| --- | ---: | ---: | ---: | ---: |
| `n-compound-head` | 120 | 0 | 13 | 0 |
| `n-subject-initial` | 120 | 0 | 12 | 0 |
| `v-imperative-initial` | 120 | 0 | 31 | 0 |
| `n-measure` | 120 | 0 | 14 | 0 |
| `v-after-modal` | 120 | 0 | 30 | 0 |

700 sentences, 12,279 tokens, 90 disputed tokens (none of them a judged word), 62 source documents. At most one judged
word per sentence; at most four target items per lemma, and 12 for `n-measure`, whose pool of real measure frames is thin
(`--shape-lemma-cap n-measure=12`). The authored share is capped at 30 percent for each shape and for the controls, and real
text comes first; real text filled every shape, so nothing was authored and the cap was never used.

Candidates that the decision rule dropped (counted per shape; candidates of the two [excluded](#excluded-sources)
documents are in the second table instead, whatever their labels were):

| Shape | Dropped: target disagreement | Dropped: class mismatch | Dropped: frame mismatch |
| --- | ---: | ---: | ---: |
| `n-compound-head` | 4 | 133 | 133 |
| `n-subject-initial` | 5 | 183 | 22 |
| `v-imperative-initial` | 9 | 76 | 27 |
| `n-measure` | 0 | 9 | 0 |
| `v-after-modal` | 2 | 47 | 2 |

| Shape | Excluded-source candidates, targets | Excluded-source candidates, controls |
| --- | ---: | ---: |
| `n-compound-head` | 30 | 8 |
| `n-subject-initial` | 13 | 4 |
| `v-imperative-initial` | 7 | 8 |
| `n-measure` | 4 | 2 |
| `v-after-modal` | 14 | 5 |

## Labeling and Decision Rule

This section states the whole rule as applied to the committed data. Rules 1 and 2 and the treatment of
disputed tokens are the labeling rules; rules 3 to 5 and the cleaning filters only remove items
(they change no label). Both labelers tag every token of a sentence blind: they see no shape, no
source, no intended class and no other labeler's answer, only the tokens under an opaque id. Sentences are never labeled twice.

For the judged word (the target):

1. **Target disagreement:** the labelers give different tags: drop the sentence.
2. **Class mismatch:** they agree on a tag whose class is not the intended one (the shape's class for a target item, the
   other class for a control): drop the sentence.
3. **Frame validation:** they agree on the intended class, but the word is outside the frame of its shape, judged by the
   tags both gave the neighbors: `n-compound-head` needs a noun before the word; `n-measure` needs a number before it and
   no noun or adjective-plus-noun after it; `n-subject-initial` and `v-imperative-initial` need the word at position 0;
   `v-after-modal` needs an auxiliary or the particle `to` before it. Drop the sentence.
4. **Base form:** `v-imperative-initial` and `v-after-modal` need the base form (the lower-cased surface form equals its
   base form); a gerund, past form or third person form is dropped.
5. **Nominal gerund:** the noun shapes drop a target whose lower-cased form ends in `ing` and has a different verb stem (a
   possible nominal gerund, which the conventions do not decide).

Otherwise the item is kept. For every other token: the same tag gives that tag with status `unanimous`; different tags give
status `disputed` and the tag of the first labeler (a fixed tie-break); flags are kept only where both agree. The judged
word is never disputed. Labeler confidence is recorded in the work folder and not used.

## Hand Check

A third model, `claude-sonnet-5.5`, reviewed a stratified random sample of the data (12 target tokens for each shape,
60 in all, drawn with a fixed seed) against the conventions. It judged each shown tag `correct`, `incorrect` or `unclear`.
This is a **model review, not a human review**, and it is not independent of the labelers' shared blind spots.

- **Result:** a sample (seed 13) of the 700 sentences: 60
  correct, 0 incorrect and 0 unclear of 60 target tokens, **precision 60/60 = 100 percent**. The gate was 95 percent.
- **Leniency control:** 4 control items whose tag had been swapped to the wrong class were slipped into the
  sample unmarked; the reviewer judged all 4 incorrect (0 of 4 accepted), so it was not accepting every item.

## Excluded Sources

Two documents are **excluded by the project owner's decision**: NIST Handbook 44 and NIST Handbook 130
(2024 editions). Their text consists of recommendations of the National Conference on Weights and Measures (NCWM) that
NIST only publishes, so it is not clearly authored by the US government (17 U.S.C. 105). The set contains no sentence of
them, and `excluded-sources.tsv` records their ids, URLs, the reason and the date of the decision (2026-11-06); it is
generated by `shapes_dev.py build` from the work folder, because `sources.tsv` lists only the used documents and has a fixed
format that carries no exclusions. The record is fail-closed: `build` reads it itself (its rows are kept) and refuses a chosen
sentence or used source row whose id or normalized URL it lists, and `check_licenses.py` fails when it is missing, empty (zero bytes),
without its header or malformed, or when `sources.tsv` or the `source_id` of any `dev.tsv` sentence matches it. A record that holds only
the header is valid and means that nothing is excluded. The builder commands `exclusions`, `candidates`, `decide`, `sample`, `build` and `report` (both tranches)
refuse when the record is missing or invalid; the optional work-folder table only adds rows, and every command uses the union of both tables (by id and normalized URL, through one loader), so a document listed by URL under another id is not collected either, and one id with two different URLs in the two tables is refused by every command.

The record also lists eight documents of training sources, not of this set. `nbs-safehandlingofbo65unit` (National Bureau of Standards Handbook 65) was
removed from `data/headings/` by the project owner on 2026-11-10 because its record lists a private non-profit organization as a co-author and its
public-domain basis is uncertain. Seven more documents (one state-agency report and six `usda-` and `usfs-` documents) are listed because their
records name a state or local government body as an author, so a federal-only basis is not verifiable; these seven rows were recorded by the implementing agent
(not decided by the project owner; the date in the `decided` column is the date the row was recorded, whereas the NBS row and the two NIST rows are owner decisions)
(they are also in `data/compound/excluded-documents.tsv`
and `data/headings/excluded-documents.tsv`). The shapes development set has no text of any of them; `check_licenses.py` and the builders refuse them in the
source tables and sentences of the training sources `data/compound/` and `data/headings/` (both sources of each folder) and of every tranche of this set.

The record has 15 more rows for documents of the technical-text tranches and sources: six documents whose records name a state or local government body as a creator or co-creator or that reproduce text written by one,
one document whose creator is a foreign government body, five documents with a contractor author, one document whose creator is a volunteer civilian organization, one
whose text is the terms of a private company and one with a university professor as editor. None of them is used by any tranche or training source, and the check refuses
all 25 rows in every source table and every sentence of `data/technical/` and of this set.

## Sources

| Family | Documents | Sentences | Basis and origin |
| --- | ---: | ---: | --- |
| `usda` | 27 | 174 | Department of Agriculture farm, home and garden publications (scans on the Internet Archive); 17 U.S.C. 105 |
| `army` | 9 | 194 | War Department and Department of the Army field manuals (scans on the Internet Archive); 17 U.S.C. 105 |
| `nasa` | 13 | 104 | NASA handbooks and Apollo experience reports (NASA Technical Reports Server); 17 U.S.C. 105 and the NASA media usage statement |
| `usfs` | 8 | 97 | Forest Service bulletins and handbooks (scans on the Internet Archive); 17 U.S.C. 105 |
| `navy` | 3 | 81 | Navy training and medical manuals (scans on the Internet Archive); 17 U.S.C. 105 |
| `usbm` | 2 | 50 | Bureau of Mines first-aid instructions (scans on the Internet Archive); 17 U.S.C. 105 |

`sources.tsv` lists only these used documents. Publication years are 1917 to 2007; every document is hashed
(`raw_sha256` of the download, `text_sha256` of the extracted sentences) and carries its retrieval date in `sources.tsv`.

## Noise Filter

The builder keeps a real sentence only when it passes a cleanliness filter (the rules are listed in the `shapes_dev.py`
docstring): it starts with a capital letter and ends in `.`, `!` or `?`; uses only ASCII letters, digits and a small set of
punctuation; has no slash or doubled punctuation junk, broken hyphen word, stray apostrophe, lone letter, unbalanced bracket or
quote, list item or citation, repeated word or trigram; is not all capitals or a title; is not a short caption; and has
no lower-case word that appears in no word list (the spellings of the open and MASC corpora and the lexicon, read as a
spelling list only), which removes most OCR errors. The length is 3 to 30 tokens.

## Headroom

The shipped model already scores high here (the baseline is in the trainer
[run log](../../../tools/DemaConsulting.PartOfSpeech.Trainer/RUN-LOG.md), section "Shapes development set: baseline of the
shipped model", which is the source of these figures). The headroom (100% minus the target accuracy) and the target errors
left to gain are:

| Shape | Headroom | Target errors |
| --- | ---: | ---: |
| `n-compound-head` | 6.7% | 8 |
| `n-subject-initial` | 4.2% | 5 |
| `v-imperative-initial` | 5.0% | 6 |
| `n-measure` | 0.8% | 1 |
| `v-after-modal` | 1.7% | 2 |
| all shapes | 3.7% | 22 |

The set is not made harder on purpose: choosing items by the errors of the shipped model would bias it against that model.
The hard tranche is chosen by model-independent criteria and is described in [Hard Tranche](#hard-tranche); its
selection rule and its headroom are stated there.

## Hard Tranche

A second development file, `hard.tsv`, holds the contested cases of four shapes (`n-compound-head`, `n-subject-initial`,
`v-imperative-initial`, `v-after-modal`; `n-measure` has no hard cases and stays at zero items). It is built from the same kind of
federal documents, labeled by the same two blind model labelers, and is for development only, like `dev.tsv`.

**The selection rule (the inputs table and the rule below)** uses no label of the tranche and no PartOfSpeech result: it selects
the candidates that are then labeled. The caps and floors are in rule 9. Rule 4 provides a top-up
round `h2`, and the floor of rule 9 is served by a further round `h3`. The
order, the commits and the round sizes are in the run log.

Inputs of the selection:

| Input | Value |
| --- | --- |
| Text | 123 federal documents, 60,297 sentences (63 documents of the standard tranche's work folder without the two excluded NIST handbooks, plus 60 newly sourced documents) |
| Classifier of the selection signal | the WordSense Basic classifier (HMM tagger plus cue model), a different model family from another project |
| WordSense commit | `6530de1928baf30f9a14446a54bfb5dd5f67b41c` (read only; never modified) |
| `DemaConsulting.WordSense.dll` SHA-256 | `b96e0055664896983e311f626d41e5251a2dcf1a236d86f6ec50beaae6a746bf` |
| `hmm.tsv` SHA-256 (the classifier's noun and verb mass for every token of the text) | `3014efd2044702c82c02c3557bb47f56ed985537f0d3154393455b110b176a8c` |
| `tokens.tsv` SHA-256 (the harness input) | `197d68f161bb1e05357caa74ed0a53a85c38a675e491c82f2365450b9294c328` |

The rule (`python tools/data/shapes_dev.py --tranche hard`; the docstring of the tool has the same text):

1. **Inputs.** Real text of the sourced documents, library tokens, the lexicon, the committed `dev.tsv` (for exclusion) and
   `hmm.tsv` (the tool refuses any `hmm.tsv` whose SHA-256 differs from the pinned value above, so edited mass cells cannot enter a
   selection). No PartOfSpeech tagger, weight file, prediction, error or score is read, by the tool or by the harness; a data test
   asserts it.
2. **Candidate filters.** Unchanged from the standard tranche: cleanliness, length 3 to 30 tokens, duplicates, the held-out
   filter (only through `leakage.held_out_overlap_keys`), excluded sources and document key hits. In addition, a candidate is not
   a standard-tranche sentence: its sentence key is not in `dev.tsv`, and a sentence of at least 10 words does not have 80% or more
   of its word 6-grams in the standard sentences of the same document.
3. **Frame.** The unchanged `shape_hits` position and lexicon frame of each shape (`n-compound-head` always strict). The lexicon
   ambiguity of the target word (listed as noun and verb, not a function word) is part of the frame.
4. **Contested band.** q is the mass that the WordSense Basic classifier gives the shape's class (Noun for `n-*`, Verb for
   `v-*`) at the target. A candidate is eligible if 0.10 <= q < 0.97. The part with q < 0.5 is pure disagreement with the shape's
   class and is reported separately. Candidates are ordered by q ascending (hardest first), ties by the seeded rank, at most 4
   per lemma. Budgets of written candidates: `n-compound-head` 400, `v-after-modal` 300, `v-imperative-initial` and
   `n-subject-initial` all. A second round (`h2`) takes the next block only for a shape that ends below its floor.
5. **Why a band and not pure disagreement.** On the 1,825 labeled candidates of the standard tranche, where the Basic
   classifier disagrees with the shape's class, the two blind labelers agree with the shape's class in only 1% to 21% of the
   cases (`n-compound-head` 8%, `n-subject-initial` 1%, `v-imperative-initial` 5%, `v-after-modal` 21%). Very confident
   disagreements are mostly not true instances of the shape (an imperative that looks like a subject-initial noun, an adjective
   before the head, and so on), and filling a shape from them would need on the order of 1,500 labeled sentences per shape. The
   real pools of candidates with q < 0.5 are 668, 275, 220 and 189 (in the order above). So disagreement (q < 0.5) is inside the
   band and very confident disagreement (q < 0.10) is outside it. The band and the budgets come from how the
   standard tranche's labels distribute over the Basic classifier's q (yield only), never from a PartOfSpeech result.
6. **Controls.** Real sentences of the other class for the lemmas of the chosen targets of the shape (hard or not; no band),
   at most 2 per lemma and 50 candidates per shape, labeled blind like the targets.
7. **Gold.** Unchanged: two blind labelers (`claude-opus-5.5`, `gpt-6.1-sol`), agreement, target disagreement and class mismatch
   dropped, and the frame, base-form and nominal-gerund rules of the standard tranche. q and the classifier's answer are never
   shown to a labeler and never change a label.
8. **No authored sentences.** `n-measure` is omitted (no hard cases); zero items is valid and appears as `-` in reports.
9. **Quota gate** (floor and cap per shape): `n-compound-head` 80 and 100, `v-imperative-initial` 70 and 100, `v-after-modal` 80
   and 100, `n-subject-initial` 40 and 100, `n-measure` 0 and 0; controls 50 and 60; at most 4 items per lemma (controls 2).
   Below a floor `build` refuses unless `--allow-short`; a miss is disclosed and never padded.
10. **No PartOfSpeech model in the selection.** The voters (spaCy, NLTK) are not part of the rule; they may be used afterwards
    as descriptive diagnostics only.
11. **Hand check.** 60 targets (15 per shape, at least half with q < 0.5) by `claude-sonnet-5.5` (a model review, not a human
    one), with a leniency control; the gate is 95% precision.
12. **Leftovers are not reused.** Labeled candidates of the standard tranche that are not in `dev.tsv` are not reused, because
    their labels were seen.

### Outcome

Both blind labelers tagged every candidate sentence (the main round and the two top-up rounds); the build (`python tools/data/shapes_dev.py --work <work-folder>
--tranche hard build`; the defaults are the floors, caps and lemma caps of rule 9) wrote 460 sentences, 7,936
tokens and no authored sentence (79 tokens that are not a judged word are disputed between the labelers; the judged word never
is). `n-measure` has zero items.

| Shape | Target items | Controls | Authored | Targets with q < 0.5 |
| --- | ---: | ---: | ---: | ---: |
| `n-compound-head` | 100 | 15 | 0 | 100 of 100 |
| `n-subject-initial` | 100 | 15 | 0 | 24 of 100 |
| `v-imperative-initial` | 100 | 15 | 0 | 65 of 100 |
| `n-measure` | 0 | 0 | 0 | - |
| `v-after-modal` | 100 | 15 | 0 | 71 of 100 |
| all shapes | 400 | 60 | 0 | 260 of 400 |

Dropped sentences (targets and controls of all rounds): the two labelers disagreed on the judged word, agreed on the other
class (class mismatch), or the sentence did not fit the frame after labeling (frame mismatch; the base-form and nominal-gerund
rules of the standard tranche).

| Shape | Target disagreement | Class mismatch | Frame mismatch |
| --- | ---: | ---: | ---: |
| `n-compound-head` | 0 | 274 | 19 |
| `n-subject-initial` | 25 | 389 | 121 |
| `v-imperative-initial` | 14 | 226 | 35 |
| `n-measure` | 0 | 0 | 0 |
| `v-after-modal` | 8 | 331 | 8 |

The q bands of the labeled targets, of the targets both labelers kept, and of the built items (hardest first, so a shape with a
larger kept pool than its cap loses its least contested items):

| Shape | q band | Labeled | Kept by both labelers | Built |
| --- | --- | ---: | ---: | ---: |
| `n-compound-head` | [0.10, 0.30) | 298 | 82 | 82 |
| `n-compound-head` | [0.30, 0.50) | 102 | 34 | 18 |
| `n-subject-initial` | [0.10, 0.30) | 161 | 10 | 10 |
| `n-subject-initial` | [0.30, 0.50) | 106 | 14 | 14 |
| `n-subject-initial` | [0.50, 0.70) | 106 | 29 | 29 |
| `n-subject-initial` | [0.70, 0.90) | 219 | 93 | 47 |
| `n-subject-initial` | [0.90, 0.97) | 254 | 174 | 0 |
| `v-imperative-initial` | [0.10, 0.30) | 131 | 40 | 38 |
| `v-imperative-initial` | [0.30, 0.50) | 70 | 28 | 27 |
| `v-imperative-initial` | [0.50, 0.70) | 95 | 55 | 35 |
| `v-imperative-initial` | [0.70, 0.90) | 134 | 88 | 0 |
| `v-imperative-initial` | [0.90, 0.97) | 144 | 98 | 0 |
| `v-after-modal` | [0.10, 0.30) | 166 | 30 | 27 |
| `v-after-modal` | [0.30, 0.50) | 133 | 45 | 44 |
| `v-after-modal` | [0.50, 0.70) | 156 | 73 | 29 |
| `v-after-modal` | [0.70, 0.90) | 45 | 19 | 0 |
| `v-after-modal` | [0.90, 0.97) | 0 | 0 | 0 |

(`n-compound-head` has no row above q 0.5 because 400 candidates, its budget, were all below 0.4.) The kept pools before the cap
were 116 (`n-compound-head`), 320 (`n-subject-initial`), 309 (`v-imperative-initial`) and 167 (`v-after-modal`), against
a cap of 100 each: `n-compound-head` lost 16 items at the cap, `n-subject-initial` 220, `v-imperative-initial` 209 and
`v-after-modal` 67. The controls are 15 per shape (the quota split of 60).

**Documents:** 99 documents (460 sentences), 43 of them also in the standard tranche (a shared document is allowed; no sentence is
shared) and 56 only here (308 sentences). Families by documents: usda 27, army 16, nasa 14, navy 11, usfs 9, usbm 6, phs 5, fcda 4,
usbr 4, noaa 2, nist 1; `phs`, `fcda`, `usbr`, `noaa` and `nist` are new families against the standard tranche. The
license basis of every document is quoted in `hard-sources.tsv`; the two excluded NIST handbooks are not used.

**Selection harness:** the WordSense Basic classifier numbers come from the harness `tools/data/hmm-tags` (a standalone program
outside the solution) run on the pinned DLL: WordSense commit `6530de1928baf30f9a14446a54bfb5dd5f67b41c`,
`DemaConsulting.WordSense.dll` SHA-256 `b96e0055664896983e311f626d41e5251a2dcf1a236d86f6ec50beaae6a746bf`, `hmm.tsv` SHA-256
`3014efd2044702c82c02c3557bb47f56ed985537f0d3154393455b110b176a8c` (inputs table above). The voters are not run (no diagnostic).
q is in no committed file.

### Hand Check of the Hard Tranche

`claude-sonnet-5.5` (a model review, not a human review) judged 60 target items shown without q: 15 per
shape, at least half with q < 0.5, drawn from the 400 target items of the capped build selection (all 60 sampled targets and the 4 control sentences are in `hard.tsv`). Result: 58 correct, 0 incorrect and 2
unclear, so the precision is 58 of 60 = 96.7% counted strictly (100% of the decided items). The two unclear items are
`n-subject-initial` fragments that read either as an imperative or as a noun-compound heading. The leniency control (4 items whose
tag was swapped on purpose) was judged incorrect 4 times of 4, so the reviewer did not accept wrong tags. The gate (95%) passed.

### Headroom of the Hard Tranche

The shipped model is measured on it in the trainer [run log](../../../tools/DemaConsulting.PartOfSpeech.Trainer/RUN-LOG.md),
section "Shapes development set: hard tranche baseline of the shipped model". The headroom (100% minus the target accuracy) per
shape and over the four shapes, and the comparison with the standard tranche, are in its subsection
[Headroom of the hard tranche](../../../tools/DemaConsulting.PartOfSpeech.Trainer/RUN-LOG.md#headroom-of-the-hard-tranche); the
figures are kept there only.

### Limits of the Hard Tranche

- **Less contested than asked for in `n-subject-initial`:** most items are in the q 0.5 to 0.9 range (76 of 100) and only 24 have
  q < 0.5, so this shape is not a pure-disagreement set; `n-compound-head` (100 of 100) and `v-after-modal` (71) are, and
  `v-imperative-initial` has 65. The very confident disagreements (q < 0.10) are outside the band by rule 5.
- **No `n-measure`:** zero items; the Basic classifier is confident on 154 of 171 measure frames, so there are no hard cases.
- **Another model family, not independent of the conventions:** the WordSense Basic classifier is a different model family from
  the PartOfSpeech perceptron, but both follow public part-of-speech conventions, so the tranche is "contested for the Basic
  classifier", not "intrinsically hard".
- **Selection and labels interact:** the band keeps a sentence only if both labelers also agree on its class, and the large
  class-mismatch drops show how many contested candidates are not true instances of the shape.
- **Hand check:** a model review of 60 tokens of the 400 built target items (15 per shape); two unclear items; no human reviewed
  any label.
- **Controls:** 60 real sentences, 15 per shape, chosen for lemmas of kept targets.
- **Same text family as the standard tranche:** 43 documents are shared, so the two tranches are one family, not two independent
  samples; the narrow-domain limit below applies to both.

## New Tranches

Three more development files hold shapes that `dev.tsv` and `hard.tsv` do not measure: `compound-head.tsv`, `headings.tsv` and
`named-state.tsv`. Each is a real-text tranche for development only, built by a rule that is independent of the
PartOfSpeech model (`python tools/data/shapes_dev_extra.py`), and each gold tag is the agreement of two blind model labelers. The
rule is stated once below ("The Rule"), as applied to the committed data.

- **Label-independent rule:** the rule and its inputs use no label and no candidate result of the three tranches.
- **Top-up rounds:** two exist: round `h2` (more `headings` imperative candidates) and round `h3` (more `compound-head` controls; no
  target).

| Tranche | File | Shape id | The judged word is | Gold |
| --- | --- | --- | --- | --- |
| `compound-head` | `compound-head.tsv` | `n-compound-head-ambiguous` | the head (the last noun) of a noun compound, in a spelling that the lexicon lists as noun and verb, in the cases that the frame of `n-compound-head` does not hold | Noun (NOUN or PROPN); controls: the same lemma as a verb |
| `headings` | `headings.tsv` | `heading-titlecase` | a capitalized word of a Title Case heading line: a common noun (kind `noun`), the initial imperative (kind `imperative`) or a true name (kind `proper`, a control) | exactly NOUN, VERB and PROPN |
| `named-state` | `named-state.tsv` | `n-named-state` | a capitalized or ALL-CAPS state, position or setting name after a preposition (`set to OFF`, `moved to STANDBY`) | Noun (NOUN or PROPN); controls: the same lemma as a verb |

### The Shape of `compound-head`

`n-compound-head` (standard and hard tranche) tests the **head** of a noun compound: its frame (`shape_hits`) wants a determiner,
a preposition, a conjunction, an auxiliary or a mark two tokens before the head, a strict noun modifier right before it and a
verb-like word, a mark or the end after it. The shape of this tranche is the **complement** of that frame at the target token, so
the two shapes do not overlap at a token: compounds of three or more nouns, a compound after an adjective or another noun-adjective
chain, a compound at the start of the sentence (head at token 1), a head that an adverb or a mark ends, and `-ing` heads that the
lexicon lists as nouns (`finding`, `setting`). The head is lower case and listed as NOUN and VERB (`check`, `split`, `stand`,
`reach`, `register`, `estimate`); the word before it is a noun modifier (`noun_like_modifier`, strict); the next token is the end,
a mark or not a noun-capable word. The nominal-gerund rule of the older noun shapes is not applied, `-ing` heads are at most 25% of the
targets and are counted separately. The modifier position of a compound (`bleed valve`) is not a shape of this set.

### The Shape of `headings`

A heading, caption, table header or step title is a **line**, not a sentence: 2 to 10 tokens, usually without final punctuation, in
Title Case, with the numbering or label (`2.1`, `Step 3.`, `Table 2.`) removed. Lines come from the OCR text of the documents (`heading_lines`: a
line after a blank line whose next non-blank line is a paragraph line; no running header, table-of-contents line, dot leader or trailing page
number or page range such as `4-8`) and pass a heading filter (`heading_noise_reason`: ASCII, no junk, balanced brackets, not ALL CAPS, every content word capitalized, no
broken last word, every word in a word list, one sentence by the library tokenizer, no training or evaluation line). Three kinds of judged
word (position and lexicon only; the capitalization is the stress, so there is no band):

- **noun** (target, gold NOUN): a non-initial capitalized token whose lower-case spelling the lexicon lists as noun and verb and
  that ends its noun phrase (the next token is the end, a mark, a preposition or a conjunction), as `Check` in `Engine Oil Check`;
  a common noun that is tagged PROPN is wrong for this shape;
- **imperative** (target, gold VERB): token 0, capitalized, listed as noun and verb, in its base form, followed by a determiner, an
  object pronoun, an adverb or particle, or a capitalized noun-capable word (`Remove the Cover`, `Check Fluid Level`); the first word may
  also be a base form that the lexicon lists as a verb only (`Remove`);
- **proper** (control, gold PROPN): a non-initial capitalized token whose lower-case spelling the lexicon lists as a proper noun only.

### The Shape of `named-state`

The judged word is a single capitalized or ALL-CAPS token (2 to 12 letters; an ordinary word when capitalized, a Roman numeral never) that is
not the first token and stands directly after `to`, `in`, `at`, `from`, `into` or `on` (a determiner may be between), with a device cue word
(`set`, `switch`, `selector`, `position`, `mode`, `setting`, `move`, `turn`, `lever`, `knob`, `valve`, `control`, ... and their inflected forms) among the 6 tokens before
the preposition, and that is not the modifier of a following noun (`set to OFF` and `moved to STANDBY` qualify; `to the OFF position` does not).
There is no band: the pool of real frames is thin and a band would shrink it further. A control is the same lemma as a verb after a modal or `to`.

### Conventions for These Tranches

[CONVENTIONS.md](../../CONVENTIONS.md) has two rules for these tranches (they apply to new data; no existing corpus is relabeled):

- Title Case or ALL-CAPS capitalization of a heading is typographic and never changes a tag: a common noun is NOUN, a base verb that starts an
  instruction is VERB, a function word keeps its tag, and a word is PROPN only when it names one particular entity.
- A state, position or setting name (`OFF`, `AUTO`, `STANDBY`, `Closed`) after a preposition is NOUN when it is an ordinary English
  word (the capitals mark a printed label, not a name), and PROPN only for a coined or arbitrary label that is no English word or for the name of
  one particular entity; a letter or number label is outside the frame.

### Inputs of the Selection

| Input | Value |
| --- | --- |
| Text | 288 US federal documents (Army and Army Air Forces 123, Department of Agriculture 104, Forest Service 48, Navy 11, NASA 2), 163,727 sentences and 139,208 candidate heading lines, none of them in a training source or in any evaluation set other than these tranches (`dev.tsv`, `hard.tsv` and `imperative.tsv` included), and none by id, URL or archive.org identifier in the source table of any of those; the documents that the three new tranches use come from this pool and may be shared among those tranches (see "Documents") |
| Selection signal of `compound-head` | the WordSense Basic classifier (HMM tagger plus cue model), as in the hard tranche; no signal for the other two tranches |
| WordSense commit | `6530de1928baf30f9a14446a54bfb5dd5f67b41c` (read only) |
| `DemaConsulting.WordSense.dll` SHA-256 | `b96e0055664896983e311f626d41e5251a2dcf1a236d86f6ec50beaae6a746bf` |
| `hmm.tsv` SHA-256 | `f20d0952ab09b838c32fa56d4c14f45743db60d74db0fefaabea5150499ed6f1` |
| `tokens.tsv` SHA-256 (harness input, 289,335 units) | `5b800937b439b53a54c76d270c23ca510a26967716c45f748aa31cd658a8b055` |

The tool refuses any `hmm.tsv` or `tokens.tsv` whose bytes differ from the two pinned values, so edited mass cells cannot enter a selection.

### The Rule

1. **Candidates.** A unit (a sentence of 3 to 30 tokens, or a heading line of 2 to 10 tokens) passes the cleanliness filter of `shapes_dev` (headings:
   the heading filter above), has no duplicate, equals no training or evaluation sentence (the held-out sets enter only through
   `leakage.held_out_overlap_keys`), shares no probe word 5-gram, and is not a sentence of `dev.tsv`, `hard.tsv` or `imperative.tsv` (exact key, or a
   sentence of at least 10 words with at least 80% of its word 6-grams in those files). A document with 3 or more sentences of that kind, or whose id, URL or
   archive.org identifier is already used or excluded, is rejected as a whole.
2. **`compound-head`.** Frame as above; the contested band of the Basic classifier (q is the mass of the class Noun at the target; eligible if 0.10 <= q < 0.97;
   order q ascending, ties by the seeded rank), at most 4 per lemma, 600 candidates. Controls: the same lemma as a verb after a modal or `to`, or as the first word of
   an instruction; no band; at most 2 per lemma; two rounds of 50 control candidates each: the first for the lemmas of the candidate targets, the second (round `h3`, controls only) for the lemmas of the
   targets that the build takes (real verb-use sentences that are not candidates of the first round, at most the room left under 2 per lemma, seeded order); round `h3` adds no target.
3. **`headings`.** The frames above; 400 candidates per target kind, at most 4 per lemma and 20 per document, ordered by strong imperative frames first and by the seeded rank within them;
   60 `proper` controls, at most 2 per lemma. The imperative candidates are taken in two rounds: the second (round `h2`) takes units that are not candidates of the first, with the frame widened
   so that the first word may also be a base form that the lexicon lists as a verb and not as a noun, proper noun, adjective or auxiliary (`Remove the Cover`); budget 400 per round,
   at most 4 per lemma and 20 per document; the targets of both rounds count together at the build.
4. **`named-state`.** Every frame hit in the seeded order (budget 500 candidates), at most 4 per lemma. Controls: 50 candidates, at most 2 per lemma.
5. **Gold.** Two blind labelers (`claude-opus-5.5`, whose tag breaks ties for tokens other than the judged word, and `gpt-6.1-sol`) tag every token of every unit and see only an
   opaque id, an index and the token. A target disagreement drops the item; so does an agreed tag other than the gold of the kind (the exact tag for `headings`, the class for the
   others); frame rules after labeling (`compound-head`: a noun before the head; `named-state`: a preposition, with an optional determiner, before the word; `headings`: the imperative is
   token 0 and in its base form, the noun is never token 0). No label is edited by hand. No authored sentence exists in any tranche.
6. **Quota gate** (floor and cap): `compound-head` targets 80 and 100, controls 15 and 20; `headings` noun targets 40 and 50, imperative targets 40 and 50, controls 12 and 20;
   `named-state` targets 40 and 100, controls 12 and 20; at most 4 targets per lemma (both target kinds of `headings` together; controls 2) and at most 12 `headings` targets per document.
   Below a floor `build` refuses unless `--allow-short`; a miss is stated and never padded. A lemma over its cap refuses the build in every case: `--allow-short` does not waive it.
7. **Hand check.** 60 targets (20 `compound-head`, 10 heading nouns, 10 heading imperatives, 20 `named-state`; fewer where a tranche has fewer) and 4 swapped-tag controls are
   shown to a third model (`claude-sonnet-5.5`; a model review, not a human review); the gate is a precision of 95%.
8. **No PartOfSpeech model in the selection** (a data test asserts it).

### Build

The files are written by `python tools/data/shapes_dev_extra.py --work <work-folder> build`. The work folder (documents, tokens, `hmm.tsv`, candidate rounds, blind batches and both
labelers' outputs) is outside the repository and not committed; a rebuild from it into another `--out-dir` is byte-identical to the committed files. No label is edited by hand.
A rerun of `candidates` from the committed repository reproduces each round's `candidates.tsv` and its report byte for byte, because the tool's used-document index and sentence
reference hold only the standard and hard tranches, the imperative source and the training and evaluation sets, never the three
tranches it builds; the tranches are kept disjoint from one another by `check_leakage.py` (no shared sentence), the builders of the standard and hard tranches (`shapes_dev.py`) also leave out the sentences of the three newer tranches, and the imperative builder keeps its source disjoint from all of them.

### Counts of the New Tranches

| Tranche | Labeled | Kept by both labelers | Target disagreement | Class mismatch | Frame mismatch | Built |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| `compound-head` targets | 600 | 202 | 3 | 349 | 46 | 100 |
| `compound-head` controls | 100 | 88 | 0 | 12 | 0 | 20 |
| `headings` noun targets | 400 | 362 | 2 | 36 | 0 | 50 |
| `headings` imperative targets | 555 | 47 | 10 | 498 | 0 | 45 |
| `headings` proper controls | 46 | 45 | 0 | 1 | 0 | 20 |
| `named-state` targets | 153 | 137 | 9 | 2 | 5 | 100 |
| `named-state` controls | 43 | 21 | 0 | 22 | 0 | 12 |

A class mismatch is an agreed tag outside the gold of the shape (for `headings` the exact tag: a noun target tagged PROPN, an imperative target tagged NOUN). Built means
the items that the caps of the rule take from the kept ones (4 per lemma, 2 per control lemma, 12 `headings` targets per document, the cap of each stratum); a `headings` line that ends in a
page number or a page range (`4-8`) is not taken (9 of the kept items are such lines).

| Tranche | Sentences | Tokens | Disputed tokens (judged word never) | Target lemmas | Documents | Years |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `compound-head` | 120 | 2,148 | 24 | 80 | 74 | 1899 to 2008 |
| `headings` | 115 | 507 | 5 | 70 | 51 | 1918 to 2017 |
| `named-state` | 112 | 1,970 | 9 | 66 | 59 | 1917 to 2008 |

- **`compound-head`:** all 100 targets are in the band with q below 0.30, 10 of them are `-ing` heads, and all
  tags are NOUN; the 20 controls are tagged VERB.
- **`headings`:** 50 noun targets (NOUN), 45 imperative targets (VERB; 28 words that the lexicon lists as noun and verb, and 17 that it lists as verb only), 20 `proper`
  controls (PROPN).
- **`named-state`:** 100 targets (54 tagged NOUN, 46 PROPN; 58 in ALL CAPS, 42 capitalized) and 12 controls (VERB).

### Documents

| Tranche | Documents | Families by documents (sentences) |
| --- | ---: | --- |
| `compound-head` | 74 | army 40 (62), usfs 15 (27), usda 14 (19), navy 5 (12) |
| `headings` | 51 | usda 24 (47), army 14 (38), usfs 8 (13), navy 5 (17) |
| `named-state` | 59 | army 28 (61), usda 14 (21), usfs 12 (21), navy 4 (7), nasa 1 (2) |

The sourced documents are all US federal government works (17 U.S.C. 105): Army, War Department and Army Air Forces manuals, Department of Agriculture and Forest Service bulletins, Navy
manuals and one NASA handbook (NTRS rights record `GOV_PUBLIC_USE_PERMITTED`, no third-party material); the basis of every document is quoted in its `<tranche>-sources.tsv`. No FAA text,
nothing in `excluded-sources.tsv`, and no document of a training source or of any other evaluation set (`dev.tsv`, `hard.tsv` and `imperative.tsv` included): the leakage reports show 0 shared documents,
0 exact sentences and 0 near duplicates against all of them. The three new tranches may share documents with each other (20 between `compound-head` and `headings`, 23 between
`compound-head` and `named-state`, 20 between `headings` and `named-state`) and share no sentence.

### Hand Check of the New Tranches

`claude-sonnet-5.5` (a model review, not a human review) judged 60 target items of the built data (20 `compound-head`, 10 heading nouns, 10 heading imperatives, 20 `named-state`) and 4
control items whose tag had been swapped on purpose. Result: 57 correct, 1 incorrect and 2 unclear, so the precision is 57 of 60 = 95.0% counted strictly (57 of 58 = 98.3% of the decided items).
The gate (95%) passed. All three items that were not judged correct are `named-state` abbreviations whose tag (NOUN or PROPN) the conventions leave open (three equipment or label abbreviations), and
the other 57 are correct in all four strata (`compound-head` 20 of 20, heading nouns 10 of 10, heading imperatives 10 of 10, `named-state` 17 of 20). The leniency control was judged
incorrect 4 times of 4. The reviewer did not read the mapping tables at the end of the conventions.

### Headroom of the New Tranches

The shipped model is measured on them in the trainer [run log](../../../tools/DemaConsulting.PartOfSpeech.Trainer/RUN-LOG.md), section "Shapes development set: new tranches baseline of the
shipped model"; the figures are kept there only.

### Limits of the New Tranches

- **`named-state` is not only device states:** the frame is position and lexicon only, so besides state names (`OFF`, `HIGH`, `IDLE`, `AUTO`, `Closed`) it admits a capitalized word that names a
  month, a place or an organization after a preposition (`March`, `China`, `Department`). Both are Noun and the class-level score treats them alike; the ALL-CAPS share (counted above) is the closest
  to a printed label. The pool of real frames is thin (the run log has its size), so the tranche has no band and takes at most 4 per lemma; its 12 controls are the fewest because a state
  name used as a verb after `to` or a modal is rare.
- **`headings` imperatives:** 28 of the 45 imperative targets are noun-and-verb words (the hard case) and 17 are verbs only; the two groups are not scored apart in the trainer report.
  The labelers read most `Check Fluid Level`-like frames as compound nouns (the class mismatch column), so the imperative targets are the lines where both read a verb.
- **`headings` OCR residue:** one heading line is OCR junk that the line filter does not see (`Fig, No, Title Page`, gold NOUN, kept as built). No heading line ends in a
  page number or a page range (the filter in "The Shape of `headings`"). The hand check of 20 heading items (10 nouns, 10 imperatives) does not include two of the 50 noun targets
  (`shapes-dev-headings-0031`, `shapes-dev-headings-0035`).
- **`compound-head` is the hardest block:** the band takes q ascending, so every target has q below 0.30 for the Basic classifier; it is "contested for the Basic classifier", not intrinsically hard.
- **Same text family:** the tranches are cut from technical manuals and bulletins; the `headings` lines come from OCR and keep a few odd line joins that the filter could not see.
- **Model labels:** two models, no human review; the hand check is a model review of 60 items.

## Follow-up Tranches

Two more development files, `compound-head-2.tsv` and `headings-2.tsv`, extend the tranches `compound-head` and `headings` with items from **new**
US federal documents (no document of any earlier table of this repository, and no document of the training sources; the two files may share documents with each other). Each is for development only,
and each gold tag is the agreement of two blind model labelers. They have the shapes, the block format and the conventions of the new tranches above
(`n-compound-head-ambiguous` and `heading-titlecase`; ids `shapes-dev-compound-head-2-NNNN` and `shapes-dev-headings-2-NNNN`; the same comments
`shape`, `role`, `target`, `lemma`, `origin` and, for headings, `kind`). They are built by `python tools/data/followup_data.py` (the normative text of the rule is
its docstring; the trainer run log, section "Follow-up", records the rule, its pins and its amendments) and no PartOfSpeech model takes part in the selection.

| Tranche | File | Items | Targets | Controls | Documents | Head or word lemmas | Tokens |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| `compound-head-2` | `compound-head-2.tsv` | 255 | 210 (gold NOUN) | 45 (the same lemma as a verb, gold VERB) | 89 | 157 | 4,576 |
| `headings-2` | `headings-2.tsv` | 129 | 109: `imperative` 59 (gold VERB), `noun` 50 (gold NOUN) | 20: `proper` (gold PROPN) | 35 | 85 | 678 |

`compound-head.tsv` and `compound-head-2.tsv` together have 310 targets and 65 controls; `headings.tsv` and `headings-2.tsv` together have 204 targets
(`imperative` 104 with the 45 of `headings.tsv`, `noun` 100 with its 50) and 40 controls. The two follow-up tranches share 30 documents and no sentence;
each shares no document with any other tranche, no document with a training source, and no sentence or near duplicate with any set (see "Leakage and
license checks" below). The judged word of an item is never `disputed`; other tokens may be: 34 sentences of `compound-head-2` (43 tokens) and 15 lines of
`headings-2` (16 tokens) have a disputed non-target token (the tag in the file is the first labeler's, status `disputed`).

### `compound-head-2`

- **The judged word** is the head (the last noun) of a noun compound in a spelling that the lexicon lists as noun and verb, in the frame of
  `n-compound-head-ambiguous` (`shapes_dev_extra.compound_ambiguous_hit`, as `compound-head`). 210 targets over 157 head lemmas in 79 documents; 26 of the
  targets have an `-ing` head (36 of the 310 targets of both files, 11.6 percent; the registered limit is 25 percent). The controls are 45
  sentences over 32 lemmas of the targets in which the same spelling is a verb.
- **Selection signal:** the contested band of the pinned WordSense Basic classifier, `0.10 <= q < 0.97` of the noun-versus-verb mass of the head
  (computed once from a fixed table, `hmm.tsv`; no PartOfSpeech model), taken with the smallest `q` first. The development side of the pool had
  17,502 frame hits (6,229 in the band) in 104,010 clean sentences; 800 target candidates over 431 lemmas and 80 control candidates were labeled;
  265 targets were kept by the decision rule and 210 by the caps.
- **Caps:** at most 4 targets per head lemma counted over both files (so the 100 targets of `compound-head` count), 150 to 210 targets (210, the cap),
  at most 2 controls per lemma, 30 to 45 controls (45, the cap; 47 were eligible), `-ing` heads at most 25 percent of the 310 targets. 57 items
  (55 targets and 2 controls) were dropped by the caps.
- **Controls are 17.6 percent of the new items** (45 of 255), below the requested share of about 20 percent: the cap of 45 binds, not the supply
  (47 controls were eligible). With the 20 controls of `compound-head.tsv` the two files have 65 controls of 375 items (17.3 percent).

| Family | Documents | Items |
| --- | ---: | ---: |
| `army` | 44 | 155 |
| `usfs` | 12 | 39 |
| `navy` | 8 | 17 |
| `usda` | 9 | 13 |
| `marine` | 2 | 7 |
| `airforce` | 2 | 6 |
| `other` | 3 | 5 |
| `nbs` | 2 | 4 |
| `phs` | 4 | 4 |
| `interior` | 1 | 3 |
| `usbr` | 2 | 2 |

### `headings-2`

- **The judged word** is a capitalized word of a Title Case line: `imperative` (token 0, the base-form verb that starts an instruction, gold VERB; frame
  of `heading-titlecase` strength 0: token 1 is a determiner, an object pronoun, an adverb or a particle), `noun` (a non-initial common noun that ends its
  noun phrase, gold NOUN) or `proper` (a control: a word that names one particular entity, gold PROPN). The lines come from heading lines, table-of-contents
  lines and labeled step titles (`Task 3`, `Step 2.1`; the label is removed); ALL-CAPS and sentence-case step titles are not in scope. A line is dropped when
  its lower-case token set has Jaccard similarity 0.8 or more with any training heading line (old, new, or of the training side of the pool). The
  tranche has 27 imperative lemmas (59 lines in 8 documents), 44 noun lemmas, at most 6 lines per lemma over imperative and noun and at most 20 per document.
- **Selection signal:** position and lexicon only; no band and no model.
- **The imperative kind is short of the requested target: 59 lines against about 100** (the registered floor is 80, the cap 130; the tranche is built
  with the registered waiver `--allow-short`). The supply of real Title Case instruction lines in the development documents is the limit, not a
  labeling rule: 60 candidates were labeled and 59 kept (1 dropped as a class mismatch). The `noun` kind (floor 30, cap 50) and the `proper` controls
  (floor 12, cap 20) are at their caps (109 and 24 were kept by the decision rule).

| Family | Documents | Lines |
| --- | ---: | ---: |
| `army` | 20 | 88 |
| `usfs` | 5 | 26 |
| `usbr` | 2 | 3 |
| `marine` | 1 | 3 |
| `interior` | 1 | 2 |
| `other` | 1 | 2 |
| `usda` | 2 | 2 |
| `airforce` | 1 | 1 |
| `navy` | 1 | 1 |
| `phs` | 1 | 1 |

### Source and Basis

The 89 and 35 documents are US federal government works (17 U.S.C. 105), one `license_basis` cell per document in `compound-head-2-sources.tsv` and
`headings-2-sources.tsv` (the agency named in the record, the record's rights statement where it has one, and the check that the text carries no copyright,
permission, third-party credit or distribution restriction), from the Internet Archive and hashed (the retrieval date of each document is in its row of the sources table). Publication years are 1881 to
2017 (74 of the 89 documents are from 1940 or later) for `compound-head-2` and 1944 to 2013 (all 35 from 1940 or later) for `headings-2`. No document is in
a table of used sources, in `excluded-sources.tsv` or in any `excluded-documents.tsv`; no FAA text, no Wikipedia, no share-alike or non-commercial text,
no Universal Dependencies text, no sentence supplied by any consumer, and no document with a non-federal creator, publisher or co-author.

### Inputs of the Follow-up Selection

| Input | Value |
| --- | --- |
| Documents | 416 US federal documents of the follow-up (147 on the development side, 269 on the training side; whole title families are on one side); `sources-draft.tsv` SHA-256 `77e6a5250877694ae67e36a5a133d6cff6f8e245864ed1434f9fab6114d2f2c2` |
| `tokens.tsv` SHA-256 (the units of the draft: 767,460) | `4387679a975ee8ca005f8d9267ab38e2bc414ee888d907701e25cff2cd406e98` |
| WordSense commit and `DemaConsulting.WordSense.dll` SHA-256 | `6530de1928baf30f9a14446a54bfb5dd5f67b41c`, `b96e0055664896983e311f626d41e5251a2dcf1a236d86f6ec50beaae6a746bf` |
| `hmm.tsv` SHA-256 (the Basic classifier's noun and verb mass for every token of the text; `compound-head-2` only) | `42c7588b9c01632a96749a1a14248e8bdf2cb1be64999e177bd657f288276edb` |

### Hand Check of the Follow-up Tranches

`claude-sonnet-5.5` (a model review, not a human review) judged 60 items of each tranche as built and 4 control items whose tag had been swapped on
purpose, once, from one sample drawn with a fixed seed (`compound-head-2` 20261121: 60 targets; `headings-2` 20261122: 30 imperative, 15 noun, 15 proper).

| Measure | `compound-head-2` | `headings-2` |
| --- | ---: | ---: |
| judged word correct | 60 of 60 (100.0%) | 60 of 60 (100.0%) |
| whole item correct (no other token marked wrong) | 59 of 60 (98.3%) | 59 of 60 (98.3%) |
| token precision (reported, not gated) | 99.91% (1 wrong of 1,066) | 99.70% (1 wrong of 333) |
| controls accepted | 0 of 4 | 0 of 4 |
| gate (judged word at least 95%, at most 1 control accepted) | passed | passed |

### Leakage and License Checks

`python tools/data/check_leakage.py --write-report` writes `compound-head-2-leakage-report.tsv` and `headings-2-leakage-report.tsv` (counts only) and adds
the two tranches to the rows of the other reports. Result for both tranches: **0 shared documents, 0 exact sentences and 0 near duplicates** against
open dev and test, gold v2, the STE challenge set, UD English-EWT, the standard and the hard tranche and the other three new tranches, and 0 shared
word 5-grams with the shape probe; against each other the two tranches share 30 documents (reported, not gated) and no sentence. Every training source
(including `compound2` and `headings2`) shares no document, exact sentence or near duplicate with them (rows of `leakage-report.tsv`).
`check_licenses.py` scans both source tables (restricted text, FAA material and `excluded-sources.tsv`).

### Limits of the Follow-up Tranches

- **`headings-2` imperatives are few:** 59 lines in 8 documents against the requested about 100; a per-kind figure of this kind has a wide uncertainty.
- **`compound-head-2` is contested for the Basic classifier**, like `compound-head`: every target has a small `q`; it is not intrinsically hard.
- **Controls are 17.6 percent** (45 of 255) against the requested about 20 percent, bound by the cap.
- **Same register:** technical manuals and reports (Army 155 of the 255 `compound-head-2` items and 88 of the 129 `headings-2` lines), mostly OCR text.
- **Model labels:** two models, no human review; the hand check is a model review of 60 items per tranche.

## Technical Tranches

Four more development files, `deverbal-heads.tsv`, `defined-terms.tsv`, `step-lines.tsv` and `clause-verbs.tsv`, hold real sentences from **new**
US federal documents (no document of any earlier table of this repository, of the training sources or of any other tranche except among
themselves: the four may share a document). Each is for development only, and each gold tag is the agreement of two blind model labelers.
They have the block format and the conventions of the new tranches above (ids `shapes-dev-<name>-NNNN`; the comments `shape`, `role`,
`target`, `lemma`, `origin` and `kind`). Each is one shape of technical English, defined by position and lexicon only. They are built by
`python tools/data/technical_data.py` (the normative text is its docstring; the trainer run log, sections "beta.3 phase 1", records the
rule and its application), and no PartOfSpeech model takes part in
the selection. The four training sources of the same shapes are in [`data/technical/`](../../technical/README.md), cut from the other side of
one partition of title families; no document is on both sides.

| Tranche | File | Sentences | Targets | Controls | Scoring | Documents | Lemmas | Tokens |
| --- | --- | ---: | --- | --- | --- | ---: | ---: | ---: |
| `deverbal-heads` | `deverbal-heads.tsv` | 450 | 360 (gold NOUN): `pred` 62, `num` 60, `ing` 140, `other` 98 | 90: `verb` (the same lemma as a verb, gold VERB) | class (Noun or Verb) | 156 | 246 | 8,762 |
| `defined-terms` | `defined-terms.tsv` | 285 | 215: `noun` 174 (gold NOUN), `adj` 41 (gold ADJ) | 70: `proper` (gold PROPN) | exact tag | 95 | 200 | 5,580 |
| `step-lines` | `step-lines.tsv` | 236 | 166: `imperative` (gold VERB) | 70: `noun` (gold NOUN) | exact tag | 97 | 164 | 1,906 |
| `clause-verbs` | `clause-verbs.tsv` | 446 | 356: `main` 288, `sub` 68 (gold VERB) | 90: `noun` 60 (gold NOUN), `adj` 30 (gold ADJ) | exact tag | 139 | 264 | 7,947 |

The four tranches have 1,417 sentences and 24,195 tokens: 1,097 targets and 320 controls, in 217 distinct documents. Each tranche IS the size in the
table (for example, `step-lines.tsv` has 166 imperative targets and 70 noun controls). The judged word of an item is never `disputed`. The scoring
column says how a gold tag is compared: `deverbal-heads` scores the class of the judged word (a NOUN or PROPN answer is Noun, a VERB answer is Verb),
the other three score the exact tag.

### The Shapes

- **`deverbal-heads` (S1)**: the judged word is the head of a noun-noun compound whose head is spelled like a verb (a zero-derived noun such as `check`
  or an `-ing` gerund noun), lower case, ending its compound. Kind `pred` (the governor is a form of *be*, *become* or *remain*), `num` (a numeral
  stands before the compound), `ing` (a gerund head in any other position) and `other` (a zero-derived head after a determiner, in subject or object
  position) are targets with gold NOUN; kind `verb` is a control, a lemma of a target used as a verb in a similar frame (gold VERB). At most 4 targets
  per head lemma (counted over the union with the targets of `compound-head.tsv` and `compound-head-2.tsv`), 2 controls per lemma and 15 items per
  document (12 at most are present).
- **`defined-terms` (S2)**: the judged word is part of a capitalized term, a run of 1 to 4 Title Case ordinary words inside a sentence: the last word
  for kind `noun` (gold NOUN) and `proper` (a control, gold PROPN: a name of one particular entity), the first word for kind `adj` (gold ADJ). The
  comment `term` holds the lower-case words of the term. The labeling convention is the section "Capitalized Defined Terms" of
  [CONVENTIONS.md](../../CONVENTIONS.md): a term that names a kind, mode, setting, position, state, screen, document type or component class is NOUN
  (ADJ for a modifier), and PROPN is for a name. 244 distinct terms (at most 2 items per term), at most 4 items per lemma and 15 per document.
- **`step-lines` (S3)**: the judged word is token 0 of a step line, a list item, a short stand-alone line or an instruction sentence that starts with a
  capitalized word. Kind `imperative` (gold VERB; the comments `frame` and `unit` say what follows the verb and where the line comes from: 166 targets,
  of which 78 are sentence units, 50 enumerated lines, 23 short stand-alone lines and 15 bulleted lines, and 120 have a bare lower-case object); kind
  `noun` (a control, gold NOUN: a short caption-like line of 2 to 6 tokens that starts with a capitalized noun-capable word, 70 lines). At most 6
  targets per lemma, 2 controls per lemma, 30 items per title family and 15 per document.
- **`clause-verbs` (S4)**: the judged word is a lower-case base-form word that can also be a noun or an adjective, right after a plural or coordinated
  subject. Kind `main` (a main-clause verb) and `sub` (a subordinating conjunction stands within 5 tokens before the subject) are targets with gold VERB; kind
  `noun` (the same frame, the word followed by a lower-case noun, gold NOUN) and `adj` (a determiner, the word and a plural noun, gold ADJ) are
  controls. 87 of the 356 targets (24.4 percent; at most 60 percent are allowed) have a direct object without a determiner after the verb. At most 4
  targets per lemma, 2 controls per lemma and 15 per document.

### Selection Signal

- **`deverbal-heads` and `step-lines`:** the contested band of the pinned WordSense Basic classifier, `0.10 <= q < 0.97` of the noun-versus-verb mass of the
  judged word (class Noun for `deverbal-heads`, class Verb for `step-lines`; read from a fixed table, `hmm.tsv`; no PartOfSpeech model), taken with
  the smallest `q` first.
- **`clause-verbs`:** the same mass for class Verb with the lower bound only, `0.10 <= q`, smallest `q` first.
- **`defined-terms` and every control:** position and lexicon only; no band and no model.

The tranches are therefore, for three of the four shapes, the contested part of the Basic classifier's mass and not an intrinsic measure of difficulty.

### Documents of the Technical Tranches

| Family | `deverbal-heads` | `defined-terms` | `step-lines` | `clause-verbs` |
| --- | ---: | ---: | ---: | ---: |
| `nasa` (documents / sentences) | 67 / 155 | 30 / 90 | 33 / 56 | 52 / 144 |
| `army` | 21 / 91 | 11 / 20 | 12 / 22 | 22 / 84 |
| `navy` | 14 / 47 | 11 / 18 | 11 / 34 | 11 / 52 |
| `usgs` | 10 / 15 | 11 / 32 | 9 / 16 | 12 / 20 |
| `epa` | 10 / 37 | 8 / 33 | 9 / 36 | 9 / 43 |
| `nps` | 9 / 30 | 11 / 45 | 5 / 17 | 10 / 33 |
| `usfs` | 6 / 14 | 4 / 16 | 7 / 26 | 7 / 18 |
| `airforce` | 5 / 16 | 1 / 1 | | 3 / 7 |
| `doe` | 5 / 28 | 4 / 20 | 6 / 16 | 5 / 31 |
| `nbs` | 3 / 4 | | 2 / 2 | 2 / 4 |
| `noaa` | 2 / 5 | 1 / 2 | 1 / 1 | |
| `coastguard` | 1 / 2 | 1 / 1 | 1 / 9 | 1 / 2 |
| `phs` | 1 / 1 | 1 / 5 | 1 / 1 | 2 / 4 |
| `usace` | 1 / 4 | 1 / 2 | | 2 / 3 |
| `usbr` | 1 / 1 | | | 1 / 1 |

The documents are US federal government works (17 U.S.C. 105), one `license_basis` cell per document in the `<tranche>-sources.tsv` tables (the agency
named in the record, the record's rights statement where it has one, and the check that the text carries no copyright, permission, third-party credit or
distribution restriction), taken from the NASA Technical Reports Server (59, 24, 26 and 44 documents), the USGS Publications Warehouse (8, 10, 8 and 11) and the
Internet Archive (89, 61, 63 and 84) and hashed (the retrieval date of each document is in its row of the sources table). Publication years are 1942 to 2024 (`deverbal-heads`), 1946 to 2024 (`defined-terms`),
1945 to 2024 (`step-lines`) and 1942 to 2024 (`clause-verbs`); every document is from 1940 or later. No document is in a table of used sources, in either
`excluded-sources.tsv` or in any `excluded-documents.tsv`; there is no FAA text, no Wikipedia, no share-alike or non-commercial text, no Universal Dependencies
text, no sentence supplied by any consumer, and no document with a non-federal creator, publisher or co-author. The tranches share documents with each other
(`deverbal-heads`/`defined-terms` 68, `deverbal-heads`/`step-lines` 71, `deverbal-heads`/`clause-verbs` 106, `defined-terms`/`step-lines` 48,
`defined-terms`/`clause-verbs` 71, `step-lines`/`clause-verbs` 63; reported, not gated) and never a sentence.

### Inputs of the Technical Selection

| Input | Value |
| --- | --- |
| Documents | 572 US federal documents sourced for the technical-text data (303 on the development side, 269 on the training side; whole title families are on one side); `sources-draft.tsv` SHA-256 `b48d463364275182f4e704625d069b9c9ef06f48ca068405925c06ab0be142ae`; the tranches use 217 of the development documents |
| `tokens.tsv` SHA-256 (the units of the draft: 459,396, with the step units) | `dfe0ba191496d53727c1dcea8d2d5e0841a7885a4ad9c489e7f8b81f6f7ef4aa` |
| WordSense commit and `DemaConsulting.WordSense.dll` SHA-256 | `6530de1928baf30f9a14446a54bfb5dd5f67b41c`, `b96e0055664896983e311f626d41e5251a2dcf1a236d86f6ec50beaae6a746bf` |
| `hmm.tsv` SHA-256 (the Basic classifier's noun and verb mass for every token of the text; `deverbal-heads`, `step-lines`, `clause-verbs`) | `43432b1a2522bf6e85b7a6e380dfaa4d94a5b26a066e94fa0b8c127c9bf602f7` |
| `data/CONVENTIONS.md` SHA-256 as the labelers read it | `42da154c60272dc9a105c5496fa3c8797cffa81cfb55d34505497b33520e5bb2` |

### Hand Check of the Technical Tranches

`claude-sonnet-5.5` (a model review, not a human review) judges a sample of 60 items of each tranche and 4 control items whose tag is swapped on
purpose (the samples are drawn with the fixed seeds `deverbal-heads` 20261201, `defined-terms` 20261202, `step-lines` 20261203 and `clause-verbs`
20261204; one blind reviewer per tranche). The judged word agrees with the label for 100% of the judged items in every tranche; the trainer run log,
section "beta.3 phase 1: hand checks and registration", has the record.

| Measure | `deverbal-heads` | `defined-terms` | `step-lines` | `clause-verbs` |
| --- | ---: | ---: | ---: | ---: |
| judged word correct / incorrect / unclear | 60 / 0 / 0 | 60 / 0 / 0 | 57 / 0 / 3 | 60 / 0 / 0 |
| marked-word precision (gate at least 95%) | 100.0% | 100.0% | 100.0% | 100.0% |
| whole item correct (no other token marked wrong) | 96.7% | 95.0% | 93.3% | 98.3% |
| token precision (reported, not gated) | 99.83% (2 wrong of 1,160) | 99.74% (3 of 1,157) | 99.63% (2 of 539) | 99.91% (1 of 1,147) |
| controls accepted | 0 of 4 | 0 of 4 | 0 of 4 | 0 of 4 |
| gate (judged word at least 95%, at most 1 control accepted, no unanswered item) | passed | passed | passed | passed |

### Leakage and License Checks of the Technical Tranches

`python tools/data/check_leakage.py --write-report` writes `deverbal-heads-leakage-report.tsv`, `defined-terms-leakage-report.tsv`,
`step-lines-leakage-report.tsv` and `clause-verbs-leakage-report.tsv` (counts only) and adds the four tranches to the rows of the other reports. Result for
all four tranches: **0 shared documents, 0 exact sentences and 0 near duplicates** against open dev and test, gold v2, the STE challenge set, UD English-EWT,
the standard and the hard tranche and the five other single-shape tranches, and 0 shared word 5-grams with the shape probe; against each other the four tranches
share documents (reported, not gated) and no sentence and no near duplicate. Every training source (including `deverbal`, `terms`, `steps` and `clauses`)
shares no document, exact sentence or near duplicate with them (rows of `leakage-report.tsv`). `check_licenses.py` scans the four source tables (restricted
text, FAA material and `excluded-sources.tsv`).

### Limits of the Technical Tranches

- **Three of the four are contested for the Basic classifier:** every target of `deverbal-heads`, `step-lines` and `clause-verbs` has a small `q`
  (`clause-verbs` has no upper bound on `q`); they are not intrinsically hard.
- **Per-kind figures are small:** `step-lines` has 166 imperative targets (65 from enumerated or bulleted lines, 39.2 percent), 70 `noun` controls and 97
  documents; `defined-terms` has 41 `adj` targets; `clause-verbs` has 68 `sub` targets and 30 `adj` controls. A per-kind figure of this kind has a wide
  uncertainty.
- **Same register:** technical manuals, procedures and reports (NASA documents give 155 of the 450 `deverbal-heads` sentences and 144 of the 446
  `clause-verbs` sentences), with OCR residue in some sentences.
- **Convention-dependent gold:** the gold of `defined-terms` follows the section "Capitalized Defined Terms" of the conventions, which differs from the
  practice of UD English-EWT and of the open corpus for such terms (see [`data/technical/README.md`](../../technical/README.md)); a score on it measures
  agreement with that section.
- **Model labels:** two models, no human review; the hand check is a model review of 60 items per tranche.

## Known Limits

- **OCR residue:** most documents are scans with OCR text. The filter removes most errors, but a few sentences keep a
  garbled word or an odd line join.
- **Nominal gerunds** are not decided by the conventions, so the noun shapes exclude possible nominal gerund targets
  (rule 5); the set says nothing about them. Gerund-initial instructions are excluded from `v-imperative-initial` (rule 4).
- **Controls are real text** and few (12 to 31 for each shape) because a control must pair with a lemma of a kept target.
- **`n-measure` is thin:** few real measure frames survive the position filter, so the shape takes at most 12 targets per
  lemma (4 for the others) and favors units and quantity nouns.
- **Number-word vocabulary of the `n-measure` pre-selection:** the candidate filter knows the number words one to twelve,
  fifteen, twenty, thirty to ninety, hundred, thousand and million; it omits thirteen, fourteen and sixteen to nineteen.
  This limits only the candidate pre-selection, not the gold labels: frames with an omitted number word (`sixteen pounds`)
  were not pre-selected as `n-measure` candidates. The labeled candidates were generated with this vocabulary, so it is
  kept as is; a changed vocabulary would change a rerun of `candidates` and the set would no longer be reproducible from it.
- **Narrow domain:** federal publications on agriculture, forestry, first aid, ordnance and aerospace;
  the sentence length and vocabulary are those of technical prose, not of dialogue or news.
- **Model labels:** two models, no human review; the hand check is a model review of 60 tokens.

## Use

Select settings here and compare variants; this is the dev set for the sentence-context work (sentence-level context
features, a two-pass decoder and word clusters). Never train on it, never add it to a corpus, and never report a figure from
it as a held-out result: the settings are chosen on it, so the figure is optimistic. The held-out measurement of a
variant is the shape probe. The trainer reads the file only through the development guard
(`eval-shapes`, see the [Trainer README](../../../tools/DemaConsulting.PartOfSpeech.Trainer/README.md)); the
[evaluation record](../../../docs/evaluation/README.md) explains the split between development and held-out sets, and the
baseline of the shipped model is in the trainer run log. `python tools/data/check_leakage.py --write-report` keeps the set
disjoint from the training sets and the other evaluation sets (and from the probe's word 5-grams).
