# Evaluation of the Embedded Model

This folder is the record of how the embedded model of `DemaConsulting.PartOfSpeech` was measured and which
confidence thresholds the library recommends. It is plain Markdown and JSON, read on GitHub; it is not part of the
generated documents. The figures live in the two generated reports; this page explains them, says how to reproduce
them and states what they do not show.

> **Status of this record.** The record below (selection, held-out results and the hashes of the inputs) describes the
> embedded model. That model is the **clause-cue model**: the same data and settings as its predecessor (the NO-ING
> model, trained on the corpora after their labels were corrected and without the 878 adjudicated `prenominal-ing`
> corrections), with one switchable group of seven context features added to the feature extractor, the **clause cues**
> (the classes of the neighboring words, the distance to the nearest determiner or modal and the clause state; see the
> trainer run log, section "Context-feature experiment"). A pre-registered experiment compared nine variants with the
> shipped feature set on development data only (five training seeds each) and selected this group by a rule that was fixed
> before any variant was trained; its softmax temperature was refit on development data, its two thresholds were selected
> on development data only, and it was then evaluated on held-out data. **That is a disclosed third look at these held-out
> sets** (earlier models of the family were evaluated on them first, and the previous model has its own record in the git
> history): they are no longer unseen, nothing was selected on this result, and the previous model's figures are shown
> beside the new ones below. **What can be compared with the earlier records:** the labels of UD English-EWT, gold v2, the
> shape probe and the challenge set never changed (the correction tooling does not cover them), and the labels of the open
> test split are the same as in the previous record, so all five are fair comparisons between the previous model and this
> one; against the two oldest records the open test split is not comparable (its labels changed). The evidence is limited
> in the ways the run log states: the held-out figures come from one training seed and the intervals overlap on every set,
> so no difference is established by a single row; on the development sets the shipped seed had more harmful open and gold
> v2 errors than the previous model, while the pre-registered primary measure (the hard tranche of the shapes development
> set) favors the new feature group on the five-seed means. The trainer
> run log has the complete side-by-side tables, the gates and the decisions.

| File | What it is |
| --- | --- |
| [confidence-thresholds.md](confidence-thresholds.md) | Selection of the two recommended thresholds on development data only: curves, reliability table, the rule, the bootstrap intervals. It reads no held-out file, so it cannot show the held-out outcome: the strict point meets its target of 5.0 on the open test split (2.2) and on gold v2 test (0.0), and exceeds it on the shape probe (5.9; see _Reading the Results_ below and [test-results.md](test-results.md)). |
| [operating-points.json](operating-points.json) | The selection as data. The `evaluate` command refuses to read a held-out file without it. |
| [test-results.md](test-results.md) | The one held-out run of the embedded model, every set and view, and the hashes of the inputs. |
| [test-results.json](test-results.json) | The same results as data; it is also the marker that the run was done. |
| [disclosure.md](disclosure.md) | The one paragraph that both generated reports carry after their introduction (here: the disclosed third look). The `thresholds` and `evaluate` commands read it; a missing or blank file means no notice. It describes the embedded model and this evaluation only, so it must be revised or removed before an evaluation of a different model is rendered. |

## How the Evaluation Was Done

1. **The model was frozen first.** Every setting of the model was chosen on the two development sets (the open corpus
   development split and the development split of gold v2). After the labels of the corpora were corrected, the model
   was trained again with those settings unchanged and only the softmax temperature was refit on development data; a
   pre-registered follow-up then removed the `prenominal-ing` corrections from the training data on the strength of
   development data alone, and a second pre-registered experiment then added the clause-cue feature group on the strength
   of development data alone (the shapes development set, open and gold v2 development, UD English-EWT development; five
   seeds per variant). Each time the model was trained again with the same seed and the temperature refit. The
   weights, the configuration and the temperature did not change afterwards. The run log of the trainer,
   [RUN-LOG.md](../../tools/DemaConsulting.PartOfSpeech.Trainer/RUN-LOG.md), has the experiments, the gates of the
   retraining, the follow-up and the decisions.
2. **The protocol was written before any number existed.** The section "Phase 5 protocol" of the run log fixes the
   definitions, the grid of thresholds, the rule that selects them, the sets and the stop conditions. It was committed
   before the first curve was computed, and it applied unchanged to every later model.
3. **The thresholds were selected on development data only** and committed with the selection artifact
   ([operating-points.json](operating-points.json)) before the held-out run.
4. **The held-out sets were evaluated once for this model.** The `evaluate` command is the only code that opens a
   held-out file, only with a valid selection for the frozen model, and it refuses to run when `test-results.json`
   exists (the previous record was deleted on purpose, in a commit of its own, before the run). The results were
   committed as produced, from one run, with no later change of the report. The same sets had
   been evaluated before for earlier models of the family, so this run is a third look.

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
- **Without a threshold the harmful rate is not low.** On the open test split 16.8 noun-verb decisions in 1,000 are
  harmful errors, on gold v2 24.6 in 1,000, and on the shape probe 66.5 in 1,000.
- **A threshold fixes most of that at a price in coverage.** The strict threshold 0.95 cuts the open test split to
  2.2 harmful errors in 1,000 answered decisions and answers 84% of the decisions. The balanced threshold 0.75 gives
  8.5 in 1,000 and answers 94%. On gold v2 the figures are 0.0 (81% coverage) and 10.4 (92% coverage). They were chosen
  on development data and then measured on test; the test figures are the ones to quote. The strict target of at most
  5.0 in 1,000 answered was met on development data (3.2), on the open test split (2.2; the 95% interval [0.6, 4.3]
  stays below 5.0) and on gold v2 test (no harmful error in 761 answered decisions), and it is **exceeded on the shape
  probe (5.9, two harmful errors in 340 answered decisions; interval [0.0, 14.8])**. The balanced target of at most 10.0
  was met on development data (9.9) and on the open test split (8.5) and is slightly exceeded on both gold v2 views (10.4
  and 10.6), so quote the held-out figures next to the development ones.
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

1. **The shape probe shows a real weakness.** Three stop conditions of the protocol fired. The one that concerns the
   behavior of the model is the shape probe: 37 of its 546 answered noun-verb decisions are harmful at 0.45, and the
   protocol allowed at most 28 (5% of the 560 rows). The errors are concentrated in four shapes: the head of a noun
   compound, a noun that starts a sentence, the verb that starts an instruction, and a noun in a measurement. The
   strict threshold reduces the figure to 2 harmful errors of 340 answered decisions but then answers only 61% of the
   decisions; the balanced threshold still leaves 10 of 456. The probe is built to be hard, so its rates are not a
   forecast for ordinary text, but short technical and imperative text is a use that the library targets: use the strict
   threshold there, or give the tagger a glossary or constraints for the words that matter. It is a limitation of the
   model, and the thresholds and the model were not tuned on it.
2. **The other two stop conditions are views, not model defects.** The protocol limits the share of tokens whose correct
   tag is outside the tagger's candidates to 1%. It fired for the eight `to` and `not` targets of gold v2 and for the
   raw view of the treebank. In both the cause is a convention difference, not an error of the tagger: see the next two
   points. The protocol text applies the limit to every view and was not changed after the result; the run log records
   both findings as fired.
3. **Gold v2 `to` and `not`.** The WordSense class set of gold v2 has no particle class, so it labels `not` as Adverb and
   the infinitive `to` as Preposition or Other, while this library tags `not` and the infinitive marker `to` as PART.
   Half of the eight targets fall outside the candidates for that reason. They are reported separately and are not in
   the gold v2 headline row, as in the development evaluation.
4. **The challenge set is a smoke test.** It has 32 sentences. The tagger made no harmful error on it. Its misses are
   mostly capitalized component names (`Theta Arm`, `Gantry Robot`) that the model tags PROPN, where the set's guideline
   says NOUN or ADJ by function, and the two `Mount ... home` sentences. A glossary that lists the component names fixes
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

The embedded model is the fourth model with a held-out record: a model trained before the labels of the training corpora
were corrected (the earlier model), the first model after the corrections (the first cleanup model, which kept the
`prenominal-ing` corrections), the same data without them (the previous model, the NO-ING model) and the embedded one
(the previous model's data and settings plus the clause-cue feature group). The first three have their records in
the git history, and the trainer run log has every figure side by side, sections "Corpus cleanup C" and
"Context-feature experiment". The open test split is not a fair comparison with the two oldest records, because its
labels changed twice (the corrections, and then 17 withdrawn test rows); the other four held-out sets have the same
labels in all records, and the open test split has the same labels in the last two. Key figures (accuracy in percent and
harmful errors in 1,000 decisions at the reference point 0.45; "Acc." is accuracy and "Harm." harmful errors, "earl." the
earlier model, "first" the first cleanup model, "prev." the previous (NO-ING) model and "emb." the embedded one; the
thresholds were 0.95 and 0.80 for the earlier and the previous model, 0.90 and 0.85 for the first cleanup
model and 0.95 and 0.75 for the embedded model):

| Set | Acc. earl. | Acc. first | Acc. prev. | Acc. emb. | Harm. earl. | Harm. first | Harm. prev. | Harm. emb. |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Open test split (labels changed) | 96.10 | 96.46 | 96.53 | 96.78 | 20.6 | 20.6 | 18.7 | 16.8 |
| Gold v2 test split | 94.57 | 94.34 | 94.37 | 94.77 | 24.6 | 31.0 | 29.9 | 24.6 |
| Shape probe | 89.11 | 87.50 | 89.64 | 91.25 | 86.3 | 93.5 | 86.3 | 66.5 |
| Challenge set | 87.13 | 87.50 | 88.60 | 86.76 | 0.0 | 0.0 | 0.0 | 0.0 |
| UD English-EWT, mapped | 92.30 | 92.97 | 92.85 | 93.19 | 16.5 | 18.8 | 17.9 | 15.9 |

Against the previous model the embedded model has a higher accuracy on the open test split, gold v2, the shape probe
and the treebank (+0.25, +0.40, +1.61 and +0.34 points) and fewer harmful noun-verb errors on all four (74 against 82
on the open test split, 23 against 28 on gold v2, 37 against 48 on the shape probe, 70 against 79 on the treebank), a
lower log-loss on each of them and a lower gold v2 calibration error; it has a lower accuracy on the challenge set
(-1.84 points, five of 272 tokens; a smoke test) and a slightly higher open test calibration error (0.23% against
0.20%). Against the earlier model, before the corrections, the harmful rate on gold v2 is now equal and those of the
shape probe and the treebank are lower (the open test split is not comparable). The intervals of the records overlap
on every set (the sets are small, and the differences are a few errors per set), so no one set proves a difference,
and the figures come from one training seed. The development evidence is of another kind: the five-seed means of the
pre-registered experiment (a Welch interval over training seeds that excludes 0 for the primary measure, the hard
tranche of the shapes development set, and a descriptive item bootstrap whose upper end is close to 0), while at the
shipped seed the development open and gold v2 harmful counts were higher than the previous model's (100 against 90
and 16 against 11). The shape probe still fires its stop condition. The recommended thresholds are the strict 0.95 of
the earlier models and a lower balanced 0.75 (the earlier and the previous model had 0.80), selected on development
data by the same rule; the balanced point answers more (94% against 93% on the open test split) at a slightly higher
harmful rate per answered decision (8.5 against 7.3).

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
controls). It is the set on which variants of the tagger (sentence-level context features, a two-pass decoder, word
clusters) are compared and settings are selected, so a figure from it is optimistic by construction and is never
reported as a held-out result.

- **Five shapes, not seven.** The shape probe has seven shape ids. The development set uses the four that the limitations
  above name as the weak ones (`n-compound-head`, `n-subject-initial`, `v-imperative-initial`, `n-measure`) and a fifth,
  `v-after-modal` (a base verb after a modal or `to`), which is frequent in manuals and procedures. `n-obj-light` and
  `n-modifier` are not covered. The definitions are the probe's; no probe sentence is used or imitated, and a leakage check
  keeps the two sets apart.
- **Selection only here.** The trainer reads the file only through its own development guard (`eval-shapes`). It is never
  trained on, and the shape probe stays held-out evaluation only: it is not tuned on, and a variant is judged on it only
  after the settings were fixed on the development sets.
- **The baseline** of the shipped model on the development set, per shape, is in the
  [run log](../../tools/DemaConsulting.PartOfSpeech.Trainer/RUN-LOG.md) (section "Shapes development set: baseline of the
  shipped model"); every variant is compared with it. The baseline there is the one of the previous model; the embedded
  model scores 96.8% on the standard tranche at its seed and 91.25% on the held-out probe, so the room to show a gain
  is small; the run log has the headroom per shape and explains
  why the set was not made harder.
- **The hard tranche** (`hard.tsv`, [README](../../data/eval/shapes-dev/README.md#hard-tranche): 460 sentences of the
  contested cases of four shapes, chosen by a pre-registered rule that uses another model family's numbers and no
  PartOfSpeech output) has its own shipped-model baseline and headroom in the
  [run log](../../tools/DemaConsulting.PartOfSpeech.Trainer/RUN-LOG.md) (section "Shapes development set: hard tranche
  baseline of the shipped model"). It is for development only, like the standard tranche. Selection used numeric output
  of the WordSense Basic classifier (the same owner; nothing copied).

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
and the user guide equal these files, and that no treebank sentence or sentence id is in this folder. The root README
repeats the two thresholds with their development and open test figures; no test compares those copies, so check them
when the record is reissued.
