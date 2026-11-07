<!-- markdownlint-disable MD013 MD060 -->
<!-- cspell:ignore usfs usbm ncwm -->

# Shapes Development Set

License: CC0-1.0

700 real sentences from 62 US federal-government documents, each with one judged word that is both a noun and a verb in
the lexicon, in one of five hard sentence shapes: 600 target items (120 for each shape) and 100 controls (the same spelling
in the other class). Every token has a Universal POS tag from two blind model labelers. It is the **development** set for
the sentence-context work: use it to select settings and to compare variants. **Development only**: never train on it and
never report it as a held-out figure (the [shape probe](../shapes/README.md) stays the held-out evaluation).

## Source and Provenance

- **Source:** the sentences are text of 62 documents that are US federal government works (17 U.S.C. 105): Army, Navy,
  Bureau of Mines, Department of Agriculture, Forest Service and NASA publications, quoted per document in
  `sources.tsv`. No document is in any other file of this repository, and no sentence is in a training or evaluation set.
  Nothing was written for this set: all 700 sentences are real text (0 authored). Two documents that were first used were
  dropped later (see [Excluded Sources](#excluded-sources)).
- **How produced:** `python tools/data/shapes_dev.py build --shape-lemma-cap n-measure=12` at commit
  `43300948ecc5c76360e9284c951b119e8a63ff36` of this repository (the labeling rounds ran at `57420f577b9f81ea25304475af85ee9bdae12f94`). The
  builder cleans the text, pre-selects candidates by
  position and lexicon only (never by class), exports blind batches, applies the decision rule below to the two labeler
  outputs and writes `dev.tsv`, `sources.tsv` and `excluded-sources.tsv`. The work folder (candidates, batches, labeler
  answers) is not committed; `dev.tsv` is never edited by hand.
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

The core of the rule (rules 1 and 2 and the treatment of disputed tokens) was committed in the docstring of `tools/data/shapes_dev.py`
(commit `88bafc2`) before any label existed; rules 3 to 5 and the cleaning filters came later (see the honest record of the order
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

Honest record of the order: rules 1 and 2 and the treatment of disputed tokens were committed before any label was seen.
Rules 3, 4 and 5 and the cleaning filters were added later, after labels of an earlier round had been seen, because the
frame-mismatch and gerund cases showed up there. They only remove items; no label was changed, and every change is in
the history of `shapes_dev.py`.

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
format that carries no exclusions. The sentences that were removed (26 targets and 6 controls) were replaced from the other,
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
A harder tranche, chosen by model-independent criteria (for example disagreement between an HMM tagger and the voters, or
the ambiguity of the lexicon entry), may follow.

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
