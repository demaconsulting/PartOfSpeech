# Evaluation of the Embedded Model

This folder is the record of how the embedded model of `DemaConsulting.PartOfSpeech` was measured and which
confidence thresholds the library recommends. It is plain Markdown and JSON, read on GitHub; it is not part of the
generated documents. The figures live in the two generated reports; this page explains them, says how to reproduce
them and states what they do not show.

> **Status of this record.** The record below (selection, held-out results and the hashes of the inputs) describes the
> model that was frozen before the labels of the training corpora were corrected. The embedded model has since been
> retrained on the corrected corpora and was checked on development data only: of the development gates that were fixed
> before the retraining, the agreement with the UD English-EWT development set improved and the convention linter is clean,
> but the harmful noun-verb errors per 1,000 decisions rose on both the open and the UD development sets, so the
> pre-registered stop rule applied and no held-out set was evaluated and no threshold was reselected. The record
> therefore does not describe the embedded model; the old-versus-new development comparison is in the trainer run log,
> section "Corpus cleanup C".

| File | What it is |
| --- | --- |
| [confidence-thresholds.md](confidence-thresholds.md) | Selection of the two recommended thresholds on development data: curves, reliability table, the rule, the bootstrap intervals. |
| [operating-points.json](operating-points.json) | The selection as data. The `evaluate` command refuses to read a held-out file without it. |
| [test-results.md](test-results.md) | The one held-out run of the frozen model, every set and view, and the hashes of the inputs. |
| [test-results.json](test-results.json) | The same results as data; it is also the marker that the run was done. |

## How the Evaluation Was Done

1. **The model was frozen first.** Every setting of the model was chosen on the two development sets (the open corpus
   development split and the development split of gold v2). The weights, the configuration and the temperature did not
   change afterwards. The run log of the trainer,
   [RUN-LOG.md](../../tools/DemaConsulting.PartOfSpeech.Trainer/RUN-LOG.md), has the experiments.
2. **The protocol was written before any number existed.** The section "Phase 5 protocol" of the run log fixes the
   definitions, the grid of thresholds, the rule that selects them, the sets and the stop conditions. It was committed
   before the first curve was computed.
3. **The thresholds were selected on development data only** and committed with the selection artifact
   ([operating-points.json](operating-points.json)).
4. **The held-out sets were evaluated once.** The `evaluate` command is the only code that opens a held-out file, only
   with a valid selection for the frozen model, and it refuses to run when `test-results.json` exists. The results were
   committed as produced. Two later changes to the report writer, each followed by a re-run with the same frozen model
   and inputs, changed the text and the JSON but no figure (errata recorded in the run log): the labels of one table, and
   the addition of the per-tag precision and recall, their intervals and the interval of the remaining measures for every
   set and view.

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
  harmful errors, on gold v2 24.6 in 1,000, and on the shape probe 86.3 in 1,000.
- **A threshold fixes most of that at a price in coverage.** The strict threshold 0.95 cuts the open test split to
  4.1 harmful errors in 1,000 answered decisions and answers 82% of the decisions. The balanced threshold 0.80 gives
  9.3 in 1,000 and answers 92%. On gold v2 the figures are 3.9 (81% coverage) and 10.4 (93% coverage). They were chosen
  on development data and then measured on test; the test figures are the ones to quote. The development figures are
  slightly optimistic by the selection.
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
   behavior of the model is the shape probe: 48 of its 548 answered noun-verb decisions are harmful at 0.45, and the
   protocol allowed at most 28 (5% of the 560 rows). The errors are concentrated in four shapes: the head of a noun
   compound, a noun that starts a sentence, the verb that starts an instruction, and a noun in a measurement. The
   strict threshold reduces the figure to 3 harmful errors of 331 answered decisions but then answers only 60% of the
   decisions; the balanced threshold still leaves 19 of 448. The probe is built to be hard, so its rates are not a
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

The same model was also compared with earlier WordSense taggers outside this repository; that comparison is not part of
this record and no figure of it is published here.

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
links to the results and states no figure.
