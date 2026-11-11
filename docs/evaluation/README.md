# Evaluation of the Embedded Model

This folder is the record of how the embedded model of `DemaConsulting.PartOfSpeech` was measured and which
confidence thresholds the library recommends. It is plain Markdown and JSON, read on GitHub; it is not part of the
generated documents. The figures live in the two generated reports; this page explains them, says how to reproduce
them and states what they do not show.

> **Status of this record.** The record below (selection, held-out results and the hashes of the inputs) describes the
> embedded model. That model is the **compound model**: the imperative model (itself the clause-cue model, the NO-ING
> model trained on the corpora after their labels were corrected and without the 878 adjudicated `prenominal-ing`
> corrections, with one switchable group of seven context features, the **clause cues**, plus the 900-sentence imperative
> source) plus two more training sources of US federal-government text, sentences with a noun compound whose head can also
> be a verb (the **compound sources**, 742 and 1,100 sentences, weight 1; see the trainer run log, sections "Imperative
> training data" and "Follow-up"). A pre-registered re-test compared four candidate arms with the imperative model on
> development data only (eight training seeds each) and selected this one by a rule that was fixed before any arm was
> trained; its two thresholds were selected on development data only (and **both changed**: strict 0.90 and balanced 0.80,
> where the previous model had 0.95 and 0.75), and it was then evaluated on held-out data. **That is a disclosed fifth look
> at these held-out sets** (six models of the family have now been scored on them, and the previous model, the imperative
> model, has its own record in the git history): they are no longer unseen, nothing was selected on this result, and the
> previous model's figures are shown beside the new ones below, with the results as produced. **What can be compared with
> the earlier records:** the labels of UD English-EWT, gold v2, the shape probe and the challenge set never changed (the
> correction tooling does not cover them), and the labels of the open test split are the same as in the previous record,
> so all five are fair comparisons between the previous model and this one; against the two oldest records the open test
> split is not comparable (its labels changed). The evidence is limited in the ways the run log states: the held-out
> figures come
> from one training seed and the intervals overlap on every set, so no difference is established by a single row. On
> held-out data the compound model has more harmful errors than the previous model on the open test split and gold v2,
> fewer on the treebank, the same number on the shape probe and is lower in accuracy on gold v2 and the challenge set (see
> _Earlier Models_ below); the pre-registered primary measure (the pooled compound and heading development tranches)
> favors the compound sources on the eight-seed means, which is development evidence on the frames the data were built
> from and is not repeated on the held-out sets. The trainer run log has the complete side-by-side tables, the gates and
> the decisions.

| File | What it is |
| --- | --- |
| [confidence-thresholds.md](confidence-thresholds.md) | Selection of the two recommended thresholds on development data only: curves, reliability table, the rule, the bootstrap intervals. It reads no held-out file, so it cannot show the held-out outcome: the strict point meets its target of 5.0 by the point estimates on the open test split (3.8; the 95% interval [1.8, 6.4] includes 5.0) and on gold v2 test (3.7), and exceeds it on the shape probe (11.9; see _Reading the Results_ below and [test-results.md](test-results.md)). |
| [operating-points.json](operating-points.json) | The selection as data. The `evaluate` command refuses to read a held-out file without it. |
| [test-results.md](test-results.md) | The one held-out run of the embedded model, every set and view, and the hashes of the inputs. |
| [test-results.json](test-results.json) | The same results as data; it is also the marker that the run was done. |
| [disclosure.md](disclosure.md) | The one paragraph that both generated reports carry after their introduction (here: the disclosed fifth look). The `thresholds` and `evaluate` commands read it; a missing or blank file means no notice. It describes the embedded model and this evaluation only, so it must be revised or removed before an evaluation of a different model is rendered. |

## How the Evaluation Was Done

1. **The model was frozen first.** Every setting of the model was chosen on development data only: open dev, gold v2
   dev, shapes-dev (standard and hard tranches) and UD-EWT dev. After the labels of the corpora were corrected, the model
   was trained again with those settings unchanged and only the softmax temperature was refit on development data; a
   pre-registered follow-up then removed the `prenominal-ing` corrections from the training data on the strength of
   development data alone, a second pre-registered experiment then added the clause-cue feature group on the strength
   of development data alone (the shapes development set, open and gold v2 development, UD English-EWT development; five
   seeds per variant), a third added the imperative training source in the same way, and the follow-up re-test (eight
   seeds per arm on an enlarged development measure) added the two compound training sources. Each time the model was trained
   again with the same seed and the temperature refit (it did not change in the last step either). An earlier
   pre-registered experiment had tested compound heads and Title Case headings as training sources and one more feature
   group (title-case line) on development data only; its rule selected no arm, so nothing changed and it read no held-out
   set (it adds no look). The
   weights, the configuration and the temperature did not change afterwards. The run log of the trainer,
   [RUN-LOG.md](../../tools/DemaConsulting.PartOfSpeech.Trainer/RUN-LOG.md), has the experiments, the gates of the
   retraining, the follow-ups and the decisions.
2. **The protocol was written before any number existed.** The section "Phase 5 protocol" of the run log fixes the
   definitions, the grid of thresholds, the rule that selects them, the sets and the stop conditions. It was committed
   before the first curve was computed, and it applied unchanged to every later model.
3. **The thresholds were selected on development data only** and committed with the selection artifact
   ([operating-points.json](operating-points.json)) before the held-out run.
4. **The held-out sets were evaluated once for this model.** The `evaluate` command is the only code that opens a
   held-out file, only with a valid selection for the frozen model, and it refuses to run when `test-results.json`
   exists (the previous record was deleted on purpose, in a commit of its own, before the run). The results were
   committed as produced, from one run, with no later change of the report. The same sets had
   been evaluated before for earlier models of the family, so this run is a fifth look.

The evaluation sets are described in [data/eval](../../data/eval/README.md). In short:

- **Open test split** and **gold v2 test split** are the general measures. Gold v2 has one labeled target word per
  sentence, from documents that no training set shares.
- **Shape probe** is 560 short sentences in seven noun and verb shapes (for example the first word of an instruction, the
  head of a noun compound). It is a stress test for short, technical and imperative text, not a sample of ordinary text.
- **Challenge set** is 32 simplified-technical-English sentences. It is a smoke test and never part of a headline
  number.
- **UD English-EWT test** is an external treebank with human labels. It is evaluated locally only and none of its text
  is stored in this repository (see _Reproduce the Results_).

The tags of the open corpus, gold v2, the shape probe and the challenge set were written by language models and no human
reviewed them. A set that shares the labelers' habits with the training labels measures agreement with those habits.
The UD treebank is the only set with human labels, and it follows other conventions (see _Mapping to the Library
Conventions_).

## Definitions

- A **decision** is a token whose correct tag is a noun or a verb and for which both were candidates of the tagger. The
  gold v2 class Noun includes proper nouns.
- A decision is **answered** at a threshold when the confidence of the chosen tag is at least the threshold.
  **Coverage** is the share of the decisions that is answered.
- A **harmful error** is a noun that is tagged as a verb or a verb that is tagged as a noun and answered. This is the
  mistake that changes the meaning of a sentence for a consumer, and so the one the thresholds are chosen for.
- Without a threshold the figure is **harmful errors per 1,000 decisions** at the reference point 0.45 of the reports,
  where about 99% of the decisions are answered. At a recommended threshold it is **per 1,000 answered decisions**.

The reports define the remaining columns (ECE, NLL, the intervals). Every set and view also has a table of precision
and recall by tag with 95% intervals; a precision or recall that is undefined (a tag that was never chosen, or that
has no gold token) is shown as 0. `test-results.json` holds the same figures for every set. The intervals of the small
sets (the challenge set, the `to` and `not` targets, single tags with few tokens) are wide and should not be read as
measurements.

## Reading the Results

What the held-out run shows, in plain terms (the tables are in [test-results.md](test-results.md)):

- **The accuracy generalizes.** The accuracy on the open test split is the accuracy on its development split within
  the bootstrap interval, and the model is well calibrated on it. Gold v2, which has other kinds of documents, is a
  little lower.
- **Without a threshold the harmful rate is not low.** On the open test split 20.0 noun-verb decisions in 1,000 are
  harmful errors, on gold v2 29.9 in 1,000, and on the shape probe 48.6 in 1,000.
- **A threshold fixes most of that at a price in coverage.** The strict threshold 0.90 cuts the open test split to
  3.8 harmful errors in 1,000 answered decisions and answers 89% of the decisions. The balanced threshold 0.80 gives
  7.1 in 1,000 and answers 93%. On gold v2 the figures are 3.7 (86% coverage) and 9.4 (91% coverage). They were chosen
  on development data and then measured on test; the test figures are the ones to quote. The strict target of at most
  5.0 in 1,000 answered was met on development data (4.7), by the point estimate on the open test split (3.8; the 95%
  interval [1.8, 6.4] includes 5.0, so the target is not established there) and on gold v2 test (3 harmful errors in 806
  answered decisions; interval [0.0, 8.1]), and it is
  **exceeded on the shape probe (11.9, five harmful errors of 419 answered decisions; interval [2.4, 22.0])**. The
  balanced target of at most 10.0 was met on development data (8.3), on the open test split (7.1; interval [4.1, 10.0])
  and on gold v2 (9.4; interval [3.5, 16.0]) and is exceeded on the gold v2 full-agreement view (10.7), so quote the
  held-out figures next to the development ones.
- **Consumers who need a low harmful rate must use a threshold.** A rule checker, a style checker or any consumer that
  acts on a noun or verb reading by itself should use the strict threshold and treat every token below it as unknown.
  A consumer that can tolerate more errors, or that shows the tag to a person, can use the balanced threshold. The
  threshold applies to the confidence of the chosen tag; a token with a single allowed tag has confidence 1.0 and is not
  a decision. The threshold lowers the harmful rate of the answered tokens; it does not make the unanswered tokens
  right.
- **The UD treebank is lower than the open test split** after the convention mapping, and lower again against the raw
  treebank tags. The model is trained on other corpora with other conventions, and the treebank is web text, e-mail
  and reviews. The harmful rate on the treebank is in the same range as on the open test split.

## Limitations

These are findings of the run. They are reported and the model was not changed in response.

1. **The shape probe shows a real weakness, though a smaller one than in the oldest records.** The protocol allowed at
   most 28 harmful errors at 0.45 on the probe (5% of the 560 rows); 27 of its 551 answered noun-verb decisions are
   harmful, the same number as for the previous model, so that stop condition did not fire (it fired in the records
   before the previous model's, with 37 and more), and the margin is one error. The errors are concentrated in the head
   of a noun compound (14), a noun that starts a sentence (8) and a noun in a measurement (4); the verb that starts an
   instruction has 1. The
   strict threshold reduces the figure to 5 harmful errors of 419 answered decisions but then answers only 75% of the
   decisions; the balanced threshold still leaves 8 of 458 (82% coverage). The probe is built to be hard, so its rates
   are not a
   forecast for ordinary text, but short technical and imperative text is a use that the library targets: use the strict
   threshold there, or give the tagger a glossary or constraints for the words that matter. It is a limitation of the
   model, and the thresholds and the model were not tuned on it.
2. **The two stop conditions that fired are views, not model defects.** The protocol limits the share of tokens whose
   correct tag is outside the tagger's candidates to 1%. It fired for the eight `to` and `not` targets of gold v2 and for
   the raw view of the treebank. In both the cause is a convention difference, not an error of the tagger: see the next
   two points. The protocol text applies the limit to every view and was not changed after the result; the run log
   records both findings as fired.
3. **Gold v2 `to` and `not`.** The WordSense class set of gold v2 has no particle class, so it labels `not` as Adverb and
   the infinitive `to` as Preposition or Other, while this library tags `not` and the infinitive marker `to` as PART.
   Half of the eight targets fall outside the candidates for that reason. They are reported separately and are not in
   the gold v2 headline row, as in the development evaluation.
4. **The challenge set is a smoke test.** It has 32 sentences. The tagger made no harmful error on it. Its misses are
   mostly capitalized component names (`Theta Arm`, `Gantry Robot`) that the model tags PROPN, where the set's guideline
   says NOUN or ADJ by function, and a few single tokens (`is`, `cracked`, `robot`, and symbols). A glossary that lists
   the component names fixes
   the first kind. The set cannot support a figure for technical text.
5. **The labels are model-written.** For the open corpus, gold v2, the probe and the challenge set, a language model or
   two wrote the reference tags. The agreement of the tagger with them is not the same as accuracy against a human
   reference, which only the treebank offers. The gold v2 development split is small, and the thresholds were chosen
   mostly on the open development split (see the intervals in [confidence-thresholds.md](confidence-thresholds.md)).
6. **One run.** Every figure comes from one run on one corpus of each kind. The bootstrap intervals describe the noise of
   the sample, not a change of genre. The throughput line of the report is a timing of the machine of the run, not a
   result of the model.
7. **Tokens that a glossary constrains** are not measured here. A hard glossary entry gives confidence 1.0 whether or not
   the entry is right.

The earlier model was also compared with earlier WordSense taggers outside this repository; that comparison is not part
of this record, was not repeated for the embedded model and no figure of it is published here.

## Earlier Models

The embedded model is the sixth model with a held-out record: a model trained before the labels of the training corpora
were corrected (the earlier model), the first model after the corrections (the first cleanup model, which kept the
`prenominal-ing` corrections), the same data without them (the NO-ING model), the NO-ING model plus the clause-cue
feature group (the clause-cue model), that model plus the imperative source (the imperative model, the previous model)
and the embedded one (the previous model's data and settings plus the two compound sources). The first five have their
records in the git history, and the trainer run log has every figure side by side, sections "Corpus cleanup C",
"Context-feature experiment", "Imperative training data" and "Follow-up". The open
test split is not a fair comparison with the two oldest records, because its labels changed twice (the corrections, and
then 17 withdrawn test rows); the other four held-out sets have the same labels in all records, and the open test split
has the same labels in the last four. Key figures (accuracy in percent and harmful errors in 1,000 decisions at the
reference point 0.45; "earl." is the earlier model, "first" the first cleanup model, "NO-ING" the NO-ING model, "cue"
the clause-cue model, "imp." the imperative (previous) model and "emb." the embedded one; the thresholds were 0.95 and
0.80 for the earlier and the NO-ING model, 0.90 and 0.85 for the first cleanup model, 0.95 and 0.75 for the clause-cue
and the imperative model and **0.90 and 0.80 for the embedded model**).
Accuracy in percent:

| Set | earl. | first | NO-ING | cue | imp. | emb. |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Open test split (labels changed) | 96.10 | 96.46 | 96.53 | 96.78 | 96.75 | 96.73 |
| Gold v2 test split | 94.57 | 94.34 | 94.37 | 94.77 | 94.87 | 94.57 |
| Shape probe | 89.11 | 87.50 | 89.64 | 91.25 | 93.21 | 93.39 |
| Challenge set | 87.13 | 87.50 | 88.60 | 86.76 | 89.34 | 88.97 |
| UD English-EWT, mapped | 92.30 | 92.97 | 92.85 | 93.19 | 93.03 | 93.05 |

Harmful errors in 1,000 decisions:

| Set | earl. | first | NO-ING | cue | imp. | emb. |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Open test split (labels changed) | 20.6 | 20.6 | 18.7 | 16.8 | 18.4 | 20.0 |
| Gold v2 test split | 24.6 | 31.0 | 29.9 | 24.6 | 26.7 | 29.9 |
| Shape probe | 86.3 | 93.5 | 86.3 | 66.5 | 48.6 | 48.6 |
| Challenge set | 0.0 | 0.0 | 0.0 | 0.0 | 0.0 | 0.0 |
| UD English-EWT, mapped | 16.5 | 18.8 | 17.9 | 15.9 | 18.6 | 16.6 |

Against the previous model the embedded model is **worse on the open test split and gold v2, better on the treebank and
the same on the shape probe**, as produced. The open test split has 88 harmful errors against 81 (20.0 against 18.4 in
1,000) and a 0.02 points lower accuracy (96.73% against 96.75%); gold v2 has 28 against 25 (29.9 against 26.7) and a 0.30
points lower accuracy (94.57% against 94.87%); the gold v2 full-agreement view has 20 against 17 (28.1 against 23.8). The
treebank has 73 against 82 (16.6 against 18.6 on the mapped view, 18.2 against 20.5 on the raw view) and a 0.02 points
higher accuracy (93.05% against 93.03%). Shape probe: 27 harmful errors at 0.45 in both (48.6 in 1,000), accuracy 93.39%
against 93.21% (+0.18 points), a lower calibration error (2.34% against 2.66%) and log-loss (0.1682 against 0.1841); the
head of a noun compound, the shape that the compound sources target, is **unchanged** (78.75% and 14 harmful errors in
both), the verb that starts an instruction is right in 98.75% of its 80 rows against 95.0% (1 harmful error against 3),
and the noun that starts a sentence (`n-subject-initial`, the shape that the imperative adoption named as weaker) is
86.25% with 8 harmful errors against 90.0% and 6. Challenge set: accuracy 88.97% against 89.34% (one token of 272; a
smoke test), no harmful error in either. The calibration error is higher on the open test split (0.27% against 0.25%),
gold v2 (2.14% against 1.95%) and the challenge set (7.16% against 6.50%) and equal on the treebank, and the log-loss is
lower on the open test split (0.1053 against 0.1068) and the shape probe and higher on gold v2 (0.2371 against 0.2307),
the challenge set and (by 0.0002) the treebank. At the recommended thresholds, which changed with the model, the strict
point (0.90) answers 89.4% of the open test split with 3.8 harmful errors in 1,000 answered decisions (the previous model
at 0.95: 83.7% and 2.4), the balanced point (0.80) 93.3% with 7.1 (the previous model at 0.75: 94.7% and 7.5), and on gold
v2 the strict point has 3 harmful errors in 806 answered decisions (the previous model had 1 in 759) and the balanced
point 9.4 in 1,000 (12.7); on the shape probe the strict point has 11.9 (the previous model 8.1) and the balanced point
17.5 (24.7). The two points are not the same operating points as the previous model's, so these rows compare two models
at two different thresholds each. Against the clause-cue model the harmful rate on the open test split is higher (20.0
against 16.8), on gold v2 higher (29.9 against 24.6) and on the treebank slightly higher (16.6 against 15.9), and that
of the shape probe is lower. The intervals of the records overlap on every set (the sets are small, and the differences
are a few errors per set), so no one set proves a difference, and the figures come from one training seed. The
development evidence is of another kind: the eight-seed means of the pre-registered re-test (the pooled compound and
heading development tranches: harmful errors per 1,000 decisions 193.1 for the embedded model against 238.8 for the
previous one, a Welch interval over training seeds of the difference of [-60.12, -31.37]; the hard tranche 159.7 against
178.8), measured on frames that the compound data were built from, while at the shipped seed the development open dev
harmful count was higher than the previous model's (96 against 91), and the gold v2 development count (11 against 14)
and the UD English-EWT development count (86 against 94) were lower. Held-out data agrees with development on the
direction of
the open split (more harmful errors) and of the treebank (fewer), does not repeat the lower gold v2 count, and does not
show the development gain on the shape of the compound head.

## Mapping to the Library Conventions

The tagger and the open corpus use the 17 Universal POS tags with four documented differences from Universal
Dependencies. [data/CONVENTIONS.md](../../data/CONVENTIONS.md) is the normative description. How each set is compared:

| Set | Reference tags | How the predictions are compared |
| --- | --- | --- |
| Open test, challenge set | 17 UPOS tags of the open corpus | Directly, tag against tag. |
| Gold v2 | 11 classes of one target word | The tag maps to a class (NOUN and PROPN to Noun, and so on). |
| Shape probe | Noun or Verb of the braced word | The tag maps to the gold v2 classes. |
| UD English-EWT, mapped (primary) | UPOS tags after four rules | Directly, tag against tag. |
| UD English-EWT, raw | UPOS tags as published | Directly; the library does not produce some of them. |

The four rules that map the treebank tags before scoring:

| Treebank reference | Mapped to |
| --- | --- |
| A possessive determiner (`PRON`, feature `Poss=Yes`, relation `nmod:poss`) | DET |
| The copula `be` (`AUX`, relation `cop`) | VERB |
| A phrasal-verb particle (`ADP`, relation `compound:prt`) | ADV |
| A preposition before a gerund or noun phrase (`SCONJ`, rules in the conventions) | ADP |

The report states how many words the mapping changed. Both views are reported so that the effect of the mapping is
visible.

## Development versus Held-Out Sets

The sets above are held-out evaluation sets: nothing was chosen on them. One more set exists for the opposite purpose, the
[shapes development set](../../data/eval/shapes-dev/README.md): 700 real sentences from US federal-government documents,
labeled by two blind models, with a judged word in each (600 target items, 120 for each of five hard shapes, and 100
controls). It is the set on which variants of the tagger are compared and settings are selected (the clause cues of the
embedded model were selected on its hard tranche; see the section "Context-feature experiment" of the run log), so a
figure from it is optimistic by construction and is never reported as a held-out result.

- **Five shapes, not seven.** The shape probe has seven shape ids. The development set uses the four that the limitations
  above name as the weak ones (`n-compound-head`, `n-subject-initial`, `v-imperative-initial`, `n-measure`) and a fifth,
  `v-after-modal` (a base verb after a modal or `to`), which is frequent in manuals and procedures. `n-obj-light` and
  `n-modifier` are not covered. The definitions are the probe's; no probe sentence is used or imitated, and a leakage check
  keeps the two sets apart.
- **Selection only here.** The trainer reads the file only through its own development guard (`eval-shapes`). It is never
  trained on, and the shape probe stays held-out evaluation only: it is not tuned on, and a variant is judged on it only
  after the settings were fixed on the development sets.
- **The baseline** of the model before the clause cues, on the development set and per shape, is in the
  [run log](../../tools/DemaConsulting.PartOfSpeech.Trainer/RUN-LOG.md) (section "Shapes development set: baseline of
  the shipped model"), and the section "Context-feature experiment" has every variant, the embedded model among them,
  against it. The standard tranche leaves little room to show a gain (the run log has the headroom per shape and
  explains why the set was not made harder), so a hard tranche was added, described below.
- **The hard tranche** (`hard.tsv`, [README](../../data/eval/shapes-dev/README.md#hard-tranche): 460 sentences of the
  contested cases of four shapes, chosen by a pre-registered rule that uses another model family's numbers and no
  PartOfSpeech output) has its own shipped-model baseline and headroom in the
  [run log](../../tools/DemaConsulting.PartOfSpeech.Trainer/RUN-LOG.md) (section "Shapes development set: hard tranche
  baseline of the shipped model"). It is for development only, like the standard tranche. Selection used numeric output
  of the WordSense Basic classifier (the same owner; nothing copied).
- **Three single-shape tranches** (`compound-head.tsv`, `headings.tsv`, `named-state.tsv`,
  [README](../../data/eval/shapes-dev/README.md#new-tranches)) measure shapes that the first two files do not: the
  head of a noun compound outside the frame of `n-compound-head`, Title Case heading lines (scored by exact tag) and
  state names after a preposition. They are also for development only, were built by a pre-registered rule that uses
  no PartOfSpeech output, and
  have a shipped-model baseline (including the wrong answers at a confidence of at least 0.95) in the
  [run log](../../tools/DemaConsulting.PartOfSpeech.Trainer/RUN-LOG.md) (section "Shapes development set: new tranches
  baseline of the shipped model").
- **Two follow-up tranches** (`compound-head-2.tsv`, `headings-2.tsv`,
  [README](../../data/eval/shapes-dev/README.md#follow-up-tranches)) add 255 and 129 items of the shapes of `compound-head`
  and `headings` from other documents, so that the compound and heading measure is bigger (310 compound targets and
  204 heading targets in all). They are for development only, were built by a pre-registered rule that uses no
  PartOfSpeech output, and have
  a shipped-model baseline in the
  [run log](../../tools/DemaConsulting.PartOfSpeech.Trainer/RUN-LOG.md) (section "Follow-up: baseline of the shipped
  model on the new tranches"). They are not held-out figures.

## Reproduce the Results

Run everything from the repository root with the .NET SDK. The commands are slow and heavy; run them at below-normal
priority and one at a time.

The thresholds read the two development files only:

```text
dotnet run -c Release --project tools/DemaConsulting.PartOfSpeech.Trainer -- thresholds
```

It writes `confidence-thresholds.md` and `operating-points.json`, and refuses to write when the 0.45 row of the curve
does not reproduce the development figures of the frozen model.

The development set of the shapes is scored (and prints Markdown, with no run-table row) with:

```text
dotnet run -c Release --project tools/DemaConsulting.PartOfSpeech.Trainer -- eval-shapes [--weights FILE] [--label TEXT]
```

The UD English-EWT run is local. The treebank is licensed CC BY-SA 4.0, so it is never committed: the files go into a
git-ignored cache and only aggregate figures and tag-level confusion enter a report. You need Python 3.10 or later (no
packages) and network access to GitHub:

```text
python tools/data/fetch_ud_ewt.py fetch
python tools/data/fetch_ud_ewt.py map
```

`fetch` downloads the pinned files (commit `4a4d77f599ea53cc405f85d0cec4b2f14f81d42b` of
`UniversalDependencies/UD_English-EWT`) into `tools/data/cache/ud-ewt/` and refuses any file whose SHA-256 differs from
[the pins](../../tools/data/pins/ud-ewt.tsv). `map` applies the four rules above and writes the mapped tables next to the
files. The cache never leaves your machine; see [data/eval/ud-ewt](../../data/eval/ud-ewt/README.md). `--ud-cache DIR` of
`evaluate` names another cache folder.

The held-out run checks every input and then runs. The committed tree already contains `test-results.json`, and
`evaluate` refuses to run when it exists (and while its lock file `test-results.json.running` exists), so reproduce in a
separate clone or worktree in which you delete
`docs/evaluation/test-results.md` and `test-results.json`:

```text
dotnet run -c Release --project tools/DemaConsulting.PartOfSpeech.Trainer -- evaluate --preflight
dotnet run -c Release --project tools/DemaConsulting.PartOfSpeech.Trainer -- evaluate --dump-dir <folder outside the repository>
```

`--preflight` parses every held-out input and prints counts only. `--dump-dir` also writes the per-token records of the
run, which can hold treebank text, so the folder must be outside the repository. The evaluation of the frozen model is
deterministic; the new files equal the committed ones except for the throughput line.

## Consistency Checks

Tests keep the copies of the figures honest. They check that the results name the embedded model and the committed
selection, that the input hashes equal the data manifest, that the thresholds and figures in the package documentation
and the user guide equal these files, that the two tables of the root README (the held-out results and the recommended
thresholds) equal these files, and that no treebank sentence or sentence id is in this folder.
