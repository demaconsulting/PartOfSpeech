# Evaluation of the Embedded Model

This folder is the record of how the embedded model of `DemaConsulting.PartOfSpeech` was measured and which
confidence thresholds the library recommends. It is plain Markdown and JSON, read on GitHub; it is not part of the
generated documents. The figures live in the two generated reports; this page explains them, says how to reproduce
them and states what they do not show.

> **Status of this record.** The record below (selection, held-out results and the hashes of the inputs) describes the
> embedded model. That model was trained on the corpora after their labels were corrected, its softmax temperature was
> refit on development data, its two thresholds were selected again on development data only, and it was then
> evaluated once on held-out data. An earlier model, trained before the corrections, had its own record, which this one
> replaces; it stays in the git history. **What can be compared with it:** the labels of the open test split were
> corrected, so its figures are not strictly comparable with the earlier record; the labels of UD English-EWT, gold v2,
> the shape probe and the challenge set did not change (the correction tooling does not cover them), so those four are
> the fair comparisons. The pre-registered development gate on harmful errors failed on its single seed, and the owner
> accepted the model after a development-only diagnosis. On three of the four fair comparison sets (gold v2, the shape
> probe and UD English-EWT) the harmful rates of the embedded model are higher than before; on the challenge set they
> are 0.0 in both records (see _Earlier Model_ below). The trainer run log, section "Corpus cleanup C", has the
> complete old-versus-new tables, the gates and the decision.

| File | What it is |
| --- | --- |
| [confidence-thresholds.md](confidence-thresholds.md) | Selection of the two recommended thresholds on development data only: curves, reliability table, the rule, the bootstrap intervals. It reads no held-out file, so it cannot show the held-out outcome: the strict point, within its target of 5.0 on development data, is above it on the open test split (see _Reading the Results_ below and [test-results.md](test-results.md)). |
| [operating-points.json](operating-points.json) | The selection as data. The `evaluate` command refuses to read a held-out file without it. |
| [test-results.md](test-results.md) | The one held-out run of the embedded model, every set and view, and the hashes of the inputs. |
| [test-results.json](test-results.json) | The same results as data; it is also the marker that the run was done. |

## How the Evaluation Was Done

1. **The model was frozen first.** Every setting of the model was chosen on the two development sets (the open corpus
   development split and the development split of gold v2). After the labels of the corpora were corrected, the model
   was trained again with those settings unchanged and only the softmax temperature was refit on development data. The
   weights, the configuration and the temperature did not change afterwards. The run log of the trainer,
   [RUN-LOG.md](../../tools/DemaConsulting.PartOfSpeech.Trainer/RUN-LOG.md), has the experiments, the gates of the
   retraining and the decision.
2. **The protocol was written before any number existed.** The section "Phase 5 protocol" of the run log fixes the
   definitions, the grid of thresholds, the rule that selects them, the sets and the stop conditions. It was committed
   before the first curve was computed, and it applied unchanged to the retrained model.
3. **The thresholds were selected on development data only** and committed with the selection artifact
   ([operating-points.json](operating-points.json)) before the held-out run.
4. **The held-out sets were evaluated once.** The `evaluate` command is the only code that opens a held-out file, only
   with a valid selection for the frozen model, and it refuses to run when `test-results.json` exists. The results were
   committed as produced, from one run, with no later change of the report.

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
- **Without a threshold the harmful rate is not low.** On the open test split 20.6 noun-verb decisions in 1,000 are
  harmful errors, on gold v2 31.0 in 1,000, and on the shape probe 93.5 in 1,000.
- **A threshold fixes most of that at a price in coverage.** The strict threshold 0.90 cuts the open test split to
  5.7 harmful errors in 1,000 answered decisions and answers 88% of the decisions. The balanced threshold 0.85 gives
  6.5 in 1,000 and answers 91%. On gold v2 the figures are 5.0 (86% coverage) and 9.6 (89% coverage). They were chosen
  on development data and then measured on test; the test figures are the ones to quote. The development figures are
  slightly optimistic by the selection: the strict target of at most 5.0 in 1,000 answered was met on development data
  (4.8) and is slightly exceeded on the open test split, where the 95% interval [3.0, 8.8] includes 5.0.
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
   behavior of the model is the shape probe: 52 of its 545 answered noun-verb decisions are harmful at 0.45, and the
   protocol allowed at most 28 (5% of the 560 rows). The errors are concentrated in four shapes: the head of a noun
   compound, a noun that starts a sentence, the verb that starts an instruction, and a noun in a measurement. The
   strict threshold reduces the figure to 7 harmful errors of 377 answered decisions but then answers only 68% of the
   decisions; the balanced threshold still leaves 11 of 412. The probe is built to be hard, so its rates are not a
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

## Earlier Model

The embedded model replaces a model that was trained before the labels of the training corpora were corrected and that
had its own held-out record (in the git history, and in the trainer run log with every figure side by side, section
"Corpus cleanup C"). The open test split is not a fair comparison, because its labels were corrected too; the other four
held-out sets have the same labels as before. Key figures, earlier model and embedded model (harmful errors in 1,000
decisions at the reference point 0.45; the thresholds of the earlier model were 0.95 and 0.80):

| Set | Accuracy (%), earlier | Accuracy (%), embedded | Harmful per 1,000, earlier | Harmful per 1,000, embedded |
| --- | ---: | ---: | ---: | ---: |
| Open test split (labels corrected, not comparable) | 96.10 | 96.46 | 20.6 | 20.6 |
| Gold v2 test split | 94.57 | 94.34 | 24.6 | 31.0 |
| Shape probe | 89.11 | 87.50 | 86.3 | 93.5 |
| Challenge set | 87.13 | 87.50 | 0.0 | 0.0 |
| UD English-EWT, mapped | 92.30 | 92.97 | 16.5 | 18.8 |

The embedded model agrees better with the human labels of the treebank and is better calibrated on it and on the open
test split, and it has more harmful noun-verb errors on gold v2, the shape probe and the treebank. The intervals of the
two records overlap on every set (the sets are small), so no one set proves a difference; the direction is the same on
all three. The recommended thresholds moved with the model (strict from 0.95 to 0.90, balanced from 0.80 to 0.85).

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

## Reproduce the Results

Run everything from the repository root with the .NET SDK. The commands are slow and heavy; run them at below-normal
priority and one at a time.

The thresholds read the two development files only:

```text
dotnet run -c Release --project tools/DemaConsulting.PartOfSpeech.Trainer -- thresholds
```

It writes `confidence-thresholds.md` and `operating-points.json`, and refuses to write when the 0.45 row of the curve
does not reproduce the development figures of the frozen model.

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
