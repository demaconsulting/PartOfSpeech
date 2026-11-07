<!-- markdownlint-disable MD013 MD060 -->
<!-- cspell:ignore usfs usbm -->

# Shapes Development Set

License: CC0-1.0

700 real sentences from 64 US federal-government documents, each with one judged word that is both a noun and a verb in
the lexicon, in one of five hard sentence shapes: 600 target items (120 for each shape) and 100 controls (the same spelling
in the other class). Every token has a Universal POS tag from two blind model labelers. It is the **development** set for
the sentence-context work: use it to select settings and to compare variants. **Development only**: never train on it and
never report it as a held-out figure (the [shape probe](../shapes/README.md) stays the held-out evaluation).

## Source and Provenance

- **Source:** the sentences are text of 64 documents that are US federal government works (17 U.S.C. 105): Army, Navy,
  Bureau of Mines, Department of Agriculture, Forest Service, NASA and NIST publications, quoted per document in
  `sources.tsv`. No document is in any other file of this repository, and no sentence is in a training or evaluation set.
  Nothing was written for this set: all 700 sentences are real text (0 authored).
- **How produced:** `python tools/data/shapes_dev.py build --shape-lemma-cap n-measure=12` at commit
  `57420f577b9f81ea25304475af85ee9bdae12f94` of this repository. The builder cleans the text, pre-selects candidates by
  position and lexicon only (never by class), exports blind batches, applies the decision rule below to the two labeler
  outputs and writes `dev.tsv` and `sources.tsv`. The work folder (candidates, batches, labeler answers) is not committed;
  `dev.tsv` is never edited by hand.
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
| `sources.tsv` | the 64 documents: family, title, URL, author, year, license basis (the quoted statement), terms URL, retrieval date, hashes, sentences used |
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
| `n-subject-initial` | 120 | 0 | 13 | 0 |
| `v-imperative-initial` | 120 | 0 | 35 | 0 |
| `n-measure` | 120 | 0 | 15 | 0 |
| `v-after-modal` | 120 | 0 | 24 | 0 |

700 sentences, 12,305 tokens, 90 disputed tokens (none of them a judged word), 64 source documents. At most one judged
word per sentence; at most four target items per lemma, and 12 for `n-measure`, whose pool of real measure frames is thin
(`--shape-lemma-cap n-measure=12`). The authored share is capped at 30 percent for each shape and for the controls, and real
text comes first; real text filled every shape, so nothing was authored and the cap was never used.

Candidates that the decision rule dropped (counted per shape):

| Shape | Dropped: target disagreement | Dropped: class mismatch | Dropped: frame mismatch |
| --- | ---: | ---: | ---: |
| `n-compound-head` | 4 | 140 | 144 |
| `n-subject-initial` | 5 | 185 | 22 |
| `v-imperative-initial` | 9 | 81 | 27 |
| `n-measure` | 0 | 10 | 0 |
| `v-after-modal` | 2 | 49 | 2 |

## Labeling and Decision Rule

The rule is **pre-registered** in the docstring of `tools/data/shapes_dev.py` (the commit that added the builder came before
any label existed); this section restates it. Both labelers tag every token of a sentence blind: they see no shape, no
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

- **Final data:** 59 correct, 0 incorrect and 1 unclear (a garbled title-like fragment) of 60 target tokens: **precision
  59/60 = 98.3 percent** counting the unclear one as a miss, 100 percent of the decided items. The gate was 95 percent.
- **Leniency control:** 4 control items whose tag had been swapped to the wrong class were slipped into the sample unmarked;
  the reviewer judged all 4 incorrect (0 of 4 accepted), so it was not accepting every item.
- **Earlier pass on the same data, before the nominal-gerund rule:** 57 correct, 0 incorrect and 3 unclear, all three
  nominal gerunds. That pass motivated rule 5, which removed such items. (A first pass on the superseded round-1 data,
  59/60, is not evidence for the final set.)

## Sources

| Family | Documents | Sentences | Basis and origin |
| --- | ---: | ---: | --- |
| `usda` | 27 | 166 | Department of Agriculture farm, home and garden publications (scans on the Internet Archive); 17 U.S.C. 105 |
| `army` | 9 | 186 | War Department and Department of the Army field manuals (scans on the Internet Archive); 17 U.S.C. 105 |
| `nasa` | 13 | 98 | NASA handbooks and Apollo experience reports (NASA Technical Reports Server); 17 U.S.C. 105 and the NASA media usage statement |
| `usfs` | 8 | 92 | Forest Service bulletins and handbooks (scans on the Internet Archive); 17 U.S.C. 105 |
| `navy` | 3 | 79 | Navy training and medical manuals (scans on the Internet Archive); 17 U.S.C. 105 |
| `usbm` | 2 | 47 | Bureau of Mines first-aid instructions (scans on the Internet Archive); 17 U.S.C. 105 |
| `nist` | 2 | 32 | NIST Handbooks 44 and 130, 2024 editions (NIST publication server); 17 U.S.C. 105 and the NIST copyright statement |

Publication years are 1917 to 2024; every document was retrieved on 2026-11-06 and hashed (`raw_sha256` of the download,
`text_sha256` of the extracted sentences). **NIST caveat:** the text of both Handbooks was adopted by the National
Conference on Weights and Measures. The NIST-authored parts of Handbooks 44 and 130 are federal works under 17 U.S.C. 105, but
a part that the Conference wrote and NIST republished might not be; the 32 sentences were selected without checking
authorship. **Flagged for owner confirmation**: the owner decides whether the two documents stay.

## Noise Filter

The builder keeps a real sentence only when it passes a cleanliness filter (the rules are listed in the `shapes_dev.py`
docstring): it starts with a capital letter and ends in `.`, `!` or `?`; uses only ASCII letters, digits and a small set of
punctuation; has no slash or doubled punctuation junk, broken hyphen word, stray apostrophe, lone letter, unbalanced bracket or
quote, list item or citation, repeated word or trigram; is not all capitals or a title; is not a short caption; and has
no lower-case word that appears in no word list (the spellings of the open and MASC corpora and the lexicon, read as a
spelling list only), which removes most OCR errors. The length is 3 to 30 tokens.

## Known Limits

- **OCR residue:** most documents are scans with OCR text. The filter removes most errors, but a few sentences keep a
  garbled word or an odd line join (the one unclear item of the hand check is of that kind).
- **Nominal gerunds** are not decided by the conventions, so the noun shapes exclude possible nominal gerund targets
  (rule 5); the set says nothing about them. Gerund-initial instructions are excluded from `v-imperative-initial` (rule 4).
- **Controls are real text** and few (13 to 35 for each shape) because a control must pair with a lemma of a kept target.
- **`n-measure` is thin:** few real measure frames survive the position filter, so the shape takes at most 12 targets per
  lemma (4 for the others) and favors units and quantity nouns.
- **Narrow domain:** federal publications on agriculture, forestry, first aid, ordnance, aerospace and weights and measures;
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
