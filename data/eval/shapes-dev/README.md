<!-- markdownlint-disable MD013 MD060 -->
<!-- cspell:ignore usfs usbm ncwm nltk fcda usbr -->

# Shapes Development Set

License: CC0-1.0

700 real sentences from 62 US federal-government documents, each with one judged word that is both a noun and a verb in
the lexicon, in one of five hard sentence shapes: 600 target items (120 for each shape) and 100 controls (the same spelling
in the other class). Every token has a Universal POS tag from two blind model labelers. It is the **development** set for
the sentence-context work: use it to select settings and to compare variants. **Development only**: never train on it and
never report it as a held-out figure (the [shape probe](../shapes/README.md) stays the held-out evaluation).

A second file, `hard.tsv`, is the **hard tranche**: 460 real sentences (400 target items and 60 controls) of the contested cases
of four of the five shapes, built by a pre-registered, model-independent rule (see [Hard Tranche](#hard-tranche)). Everything
above and below this paragraph that gives a count describes the standard tranche (`dev.tsv`) unless it says otherwise.

## Source and Provenance

- **Source:** the sentences are text of 62 documents that are US federal government works (17 U.S.C. 105): Army, Navy,
  Bureau of Mines, Department of Agriculture, Forest Service and NASA publications, quoted per document in
  `sources.tsv`. No document is in any other file of this repository except `hard-sources.tsv` (the hard tranche
  shares part of these documents on purpose; no sentence is shared), and no sentence is in a training or evaluation set.
  Nothing was written for this set: all 700 sentences are real text (0 authored). Two documents that were first used were
  dropped later (see [Excluded Sources](#excluded-sources)).
- **How produced:** `python tools/data/shapes_dev.py --work <work-folder> build --shape-lemma-cap n-measure=12`, with the builder as
  merged by PR #10 (merge commit `17a9cf9` on main; the current standard profile rebuilds the committed files byte for byte, see
  `StandardRebuildTests`).
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
| `excluded-sources.tsv` | the documents that were dropped after they were sourced: `source_id`, `url`, `reason`, `decided` (see [Excluded Sources](#excluded-sources)) |
| `leakage-report.tsv` | counts of the set against every other evaluation set (documents, exact sentences, near duplicates, shared probe word 5-grams); no sentence text |
| `hard.tsv` | the 460 sentences of the hard tranche, same block format (see [Hard Tranche](#hard-tranche)); ids `shapes-dev-hard-NNNN` |
| `hard-sources.tsv` | the 99 documents of the hard tranche, same columns as `sources.tsv` |
| `hard-leakage-report.tsv` | counts of the hard tranche against every other evaluation set, the standard tranche included (shared documents are reported, not gated) |

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

The core of the rule (rules 1 and 2 and the treatment of disputed tokens) was written in the docstring of `tools/data/shapes_dev.py`
before any label existed; rules 3 to 5 and the cleaning filters came later (see the honest record of the order
below); this section restates the whole rule. Both labelers tag every token of a sentence blind: they see no shape, no
source, no intended class and no other labeler's answer, only the tokens under an opaque id. The labeling ran in three rounds
of candidates: round 1 was **superseded** and archived (OCR noise and weak shape fidelity; its pool had only 57 controls),
round 2 rebuilt the pool with the cleaning filter, and round 3 added compound-head candidates only. Sentences are never labeled twice.

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

Honest record of the order: rules 1 and 2 and the treatment of disputed tokens were written before any label was seen.
Rules 3, 4 and 5 and the cleaning filters were added later, after labels of an earlier round had been seen, because the
frame-mismatch and gerund cases showed up there. They only remove items; no label was changed, and every change is in
the history of `shapes_dev.py` (the PR that added the standard tranche is #10, merge commit `17a9cf9` on main).

## Hand Check

A third model, `claude-sonnet-5.5`, reviewed a stratified random sample of the final data (12 target tokens for each shape,
60 in all, drawn with a fixed seed) against the conventions. It judged each shown tag `correct`, `incorrect` or `unclear`.
This is a **model review, not a human review**, and it is not independent of the labelers' shared blind spots.

- **Final figure (pass D, the data as committed, after the exclusion):** a fresh sample (seed 13) of the 700 sentences: 60
  correct, 0 incorrect and 0 unclear of 60 target tokens, **precision 60/60 = 100 percent**. The gate was 95 percent.
- **Leniency control (pass D):** 4 control items whose tag had been swapped to the wrong class were slipped into the
  sample unmarked; the reviewer judged all 4 incorrect (0 of 4 accepted), so it was not accepting every item.

Earlier passes, kept as history (none of them describes the committed data):

| Pass | Data | Correct | Incorrect | Unclear |
| --- | --- | ---: | ---: | ---: |
| A | superseded round-1 data | 59 | 0 | 1 |
| B | before the nominal-gerund rule (rule 5) | 57 | 0 | 3 |
| C | the data that still contained the two NIST documents | 59 | 0 | 1 |
| **D** | **the committed data (final)** | **60** | **0** | **0** |

Pass B found three nominal gerunds, which motivated rule 5. Pass C's one unclear item was a garbled title-like fragment.
The replacement sentences of the rebuilt set were not in any earlier sample, which is why pass D drew a fresh sample.

## Excluded Sources

Two documents were used first and then **dropped by the project owner's decision**: NIST Handbook 44 and NIST Handbook 130
(2024 editions). Their text consists of recommendations of the National Conference on Weights and Measures (NCWM) that
NIST only publishes, so it is not clearly authored by the US government (17 U.S.C. 105). The set contains no sentence of
them, and `excluded-sources.tsv` records their ids, URLs, the reason and the date of the decision (2026-11-06); it is
generated by `shapes_dev.py build` from the work folder, because `sources.tsv` lists only the used documents and has a fixed
format that carries no exclusions. The record is fail-closed: `build` reads it itself (its rows are kept) and refuses a chosen
sentence or used source row whose id or normalized URL it lists, and `check_licenses.py` fails when it is missing, empty (zero bytes),
without its header or malformed, or when `sources.tsv` or the `source_id` of any `dev.tsv` sentence matches it. A record that holds only
the header is valid and means that nothing is excluded. The builder commands `exclusions`, `candidates`, `decide` and `build` (both tranches)
refuse when the record is missing or invalid; the optional work-folder table only adds rows. The sentences that were removed (26 targets and 6 controls) were replaced from the other,
already blind-labeled documents of the same pool, by the same method and decision rule; no new labeling was needed, and no
label was changed.

## Sources

| Family | Documents | Sentences | Basis and origin |
| --- | ---: | ---: | --- |
| `usda` | 27 | 174 | Department of Agriculture farm, home and garden publications (scans on the Internet Archive); 17 U.S.C. 105 |
| `army` | 9 | 194 | War Department and Department of the Army field manuals (scans on the Internet Archive); 17 U.S.C. 105 |
| `nasa` | 13 | 104 | NASA handbooks and Apollo experience reports (NASA Technical Reports Server); 17 U.S.C. 105 and the NASA media usage statement |
| `usfs` | 8 | 97 | Forest Service bulletins and handbooks (scans on the Internet Archive); 17 U.S.C. 105 |
| `navy` | 3 | 81 | Navy training and medical manuals (scans on the Internet Archive); 17 U.S.C. 105 |
| `usbm` | 2 | 50 | Bureau of Mines first-aid instructions (scans on the Internet Archive); 17 U.S.C. 105 |

`sources.tsv` lists only these used documents. Publication years are 1917 to 2007; every document was retrieved on 2026-11-06
and hashed (`raw_sha256` of the download, `text_sha256` of the extracted sentences).

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

The set was not made harder on purpose: choosing items by the errors of the shipped model would bias it against that model.
A second, harder tranche is chosen by model-independent criteria and is described in [Hard Tranche](#hard-tranche); its
selection rule is pre-registered there and its headroom is stated there too.

## Hard Tranche

A second development file, `hard.tsv`, holds the contested cases of four shapes (`n-compound-head`, `n-subject-initial`,
`v-imperative-initial`, `v-after-modal`; `n-measure` has no hard cases and stays at zero items). It is built from the same kind of
federal documents, labeled by the same two blind model labelers, and is for development only, like `dev.tsv`.

**The pre-registered selection rule (the inputs table and the rule below) was written before any label of the tranche existed**,
and before the candidates were generated. The frozen pre-registration is recorded in the RUN-LOG section "Shapes development set: hard
tranche, pre-registered selection rule" (`tools/DemaConsulting.PartOfSpeech.Trainer/RUN-LOG.md`): the rule core was written before any
label; the later additions and the one amendment (the `n-subject-initial` cap, rule 9, before labeling) are recorded in that section.
The sections "Rounds Actually Run", "Outcome" and the ones after it were added after
labeling; they do not change the rule.

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
   `hmm.tsv`. No PartOfSpeech tagger, weight file, prediction, error or score is read, by the tool or by the harness; a data test
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
   band and very confident disagreement (q < 0.10) is outside it. The band and the budgets were chosen after looking at how the
   standard tranche's labels distribute over the Basic classifier's q (yield only), never at a PartOfSpeech result; they are
   fixed before any label of this tranche exists. This was the project owner's decision.
6. **Controls.** Real sentences of the other class for the lemmas of the chosen targets of the shape (hard or not; no band),
   at most 2 per lemma and 50 candidates per shape, labeled blind like the targets.
7. **Gold.** Unchanged: two blind labelers (`claude-opus-5.5`, `gpt-6.1-sol`), agreement, target disagreement and class mismatch
   dropped, and the frame, base-form and nominal-gerund rules of the standard tranche. q and the classifier's answer are never
   shown to a labeler and never change a label.
8. **No authored sentences.** `n-measure` is omitted (no hard cases); zero items is valid and appears as `-` in reports.
9. **Quota gate** (floor and cap per shape): `n-compound-head` 80 and 100, `v-imperative-initial` 70 and 100, `v-after-modal` 80
   and 100, `n-subject-initial` 40 and 100, `n-measure` 0 and 0; controls 50 and 60; at most 4 items per lemma (controls 2).
   Below a floor `build` refuses unless `--allow-short`; a miss is disclosed and never padded. Amendment before any label
   existed: the `n-subject-initial` cap was raised from 60 to 100 after the new documents enlarged the pool (846 candidates
   against the 254 expected); written before labeling started.
10. **No PartOfSpeech model in the selection.** The voters (spaCy, NLTK) are not part of the rule; they may be used afterwards
    as descriptive diagnostics only.
11. **Hand check.** 60 targets (15 per shape, at least half with q < 0.5) by `claude-sonnet-5.5` (a model review, not a human
    one), with a leniency control; the gate is 95% precision.
12. **Leftovers are not reused.** Labeled candidates of the standard tranche that are not in `dev.tsv` are not reused, because
    their labels were seen.

### Rounds Actually Run

- **Main round:** all 2,258 candidate sentences (2,320 candidates: 400, 846, 574 and 300 targets for `n-compound-head`,
  `n-subject-initial`, `v-imperative-initial` and `v-after-modal`, plus 50 control candidates per shape; 16 blind batches).
- **Round `h2`:** the next 200 `v-after-modal` candidates (no controls), run because that shape kept only 77 targets, below its
  floor of 80. This is the round that rule 4 provides for.
- **Round `h3`:** a controls-only round, 149 sentences (152 control candidates, 38 per shape), run because the controls of the
  first two rounds were 45, below the floor of 50. The pre-registration did not name this round. It applies rule 6 (the other-class
  sentences of the chosen target lemmas, at most 2 per lemma) and the controls floor of rule 9, it adds no target, and it was
  prepared and exported before any `h3` label existed.

No other round ran, and no candidate was relabeled or added after the labels were seen.

### Outcome

Both blind labelers tagged every sentence of the three rounds; the build (`python tools/data/shapes_dev.py --work <work-folder>
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
`3014efd2044702c82c02c3557bb47f56ed985537f0d3154393455b110b176a8c` (inputs table above). The voters were not run (no diagnostic).
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

## Known Limits

- **OCR residue:** most documents are scans with OCR text. The filter removes most errors, but a few sentences keep a
  garbled word or an odd line join (the one unclear item of hand check pass C was of that kind).
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
