<!-- markdownlint-disable MD013 MD060 -->

# Trainer

`DemaConsulting.PartOfSpeech.Trainer` is the build-time tool that trains the averaged-perceptron weights of the tagger
(`src/DemaConsulting.PartOfSpeech/Resources/weights.bin`) from the committed corpora, with the same
feature extractor, lexicon restriction and weights writer that the runtime uses. It is not part of the NuGet package.
This file describes how to run it; the algorithm is in `docs/design/part-of-speech/model-trainer.md`, and the unit's
requirements are in `docs/reqstream/part-of-speech/model-trainer.yaml`.

## Commands

Run every command from the repository root:

```text
dotnet run -c Release --project tools/DemaConsulting.PartOfSpeech.Trainer -- <command> [options]
```

| Command | Purpose |
| --- | --- |
| `train` | Train with `training.json` (plus experiment overrides) and write the weights file (default: the embedded resource). |
| `train --check` | Retrain with the committed configuration and compare the **uncompressed image** with the committed file and with `imageSha256`. Exit code 1 if stale. |
| `eval --weights FILE` | Score a weights file on the two development sets through the real decoder (`Tagger.Load`). |
| `eval-shapes [--weights FILE] [--label TEXT] [--tranche standard\|hard\|both]` | Score a weights file (default: the embedded model) on the hard-shape development set through the real decoder and print Markdown: per shape the target-token accuracy (with a 95% Wilson interval), harmful errors and harmful errors per 1,000 noun-verb decisions, and the control accuracy (the same spelling in the other class), plus a pooled row. The standard tranche `data/eval/shapes-dev/dev.tsv` (section unchanged), the hard tranche `hard.tsv` and the two pooled each get a section (default `both`; a missing hard file is noted, `--tranche hard` needs it). Reads the shapes development files only (their own guard); appends no run-log row. |
| `eval-arm --weights FILE --label TEXT --json FILE` | Dev-only scoring of a trained arm of the context-feature experiment (see _Context-feature experiment_ below): the standard and the hard tranche of the shapes development set (per shape and pooled, with per-item outcomes of the target items), open dev, gold v2 dev, the mapped development part of the external treebank (local cache `tools/data/cache/ud-ewt/ud_dev.tsv`, SHA-256 pinned; missing cache: exit 2 with the fetch recipe), the file sizes, the groups of the weights header and the single-thread throughput of the default tagger (one warm-up pass and five timed passes over the open dev and treebank dev tokens, median tokens per second). Writes one JSON result (schema 1) to `--json`. It has no data option and refuses a weights or result file inside `data/` or `tools/data/`; it cannot read a held-out set. |
| `shapes-tokenize --in FILE --out FILE` | Tokenize a text file, one sentence per line, with the library tokenizer: each output line is `sentence TAB sentences found TAB tokens` (tokens separated by single spaces). Reads and writes only the two files, and refuses a path inside the repository's `data/` folder (links resolved), so it cannot read training, development or held-out data; it is the tokenizer of `tools/data/shapes_dev.py`, so the development set follows the corpus tokenization by construction. |
| `calibrate --weights FILE` | Choose the softmax temperature that minimizes the log-likelihood on the open development split. |
| `compare --a FILE --b FILE` | Paired bootstrap by document (1,000 resamples, fixed seed) of the difference between two weights files. |
| `glossary-probe --weights FILE` | Simulated glossaries on the open development split: score boost versus hard constraint. |
| `glossary-experiment` | Dev-only glossary decision: trains the configured model and a model with trained glossary features, compares them with score boosts and prints the pre-registered decision (outcome: boost only, see `RUN-LOG.md`). |
| `glossary-eval [--seed N] [--resamples N]` | Dev-only evaluation of the `Glossary` API (section E9 of `RUN-LOG.md`): simulated glossaries (noun compounds, single words, inflection, acronym case, gold v2 head effect, history effect after a match, non-head rule) tagged through `Tagger.Tag(tokens, glossary)` with the embedded model; hard and soft mode against no glossary with 0, 10 and 30% wrong entries, paired bootstrap by document (default 1,000 resamples, seed 1). Prints Markdown tables, the parity check against the phase-3 path, the invariant that nothing before a match changes, and the two decisions: the soft strength by the wrong-entry rule that fixes the shipped default (the first-run rule that it replaced is printed after it as the historical decision) and the non-head rule. Reads the two development files only and takes no path; it appends no run-log row. |
| `thresholds [--seed N] [--resamples N]` | Dev-only confidence curve of the embedded model and the pre-registered operating points (`strict`, `balanced`; the protocol section of `RUN-LOG.md`). Writes `docs/evaluation/operating-points.json` and `docs/evaluation/confidence-thresholds.md`; refuses to write when the 0.45 row does not reproduce the frozen development figures. Reads the two development files only. |
| `evaluate [--preflight] [--dump-dir DIR] [--ud-cache DIR] [--seed N] [--resamples N]` | The **one** held-out evaluation of the embedded model, behind the committed `operating-points.json`; see _Held-out evaluation_ below. It has no weights, data path or threshold option. |

Exit codes: 0 success, 1 stale weights (`train --check`) or a stop condition fired (`evaluate`), 2 usage or data error.

### Options of `train`

| Option | Meaning |
| --- | --- |
| `--config FILE` | Configuration file (default `tools/DemaConsulting.PartOfSpeech.Trainer/training.json`). |
| `--output FILE` | Where to write the (compressed) weights file. Required with experiment overrides and `--calibrate`, so that the shipped weights are never overwritten by an experiment. |
| `--update-config` | Record the SHA-256 of the uncompressed image in the configuration file. |
| `--check` / `--weights FILE` | Verify instead of write; `--weights` names the committed file when it is not the embedded resource. |
| `--eval`, `--label TEXT`, `--log FILE` | Evaluate the result on the development sets; label the run; append a row to a run-log table. |
| `--calibrate` | Experiments only: fit the softmax temperature of the run on the open development split before scoring and writing, so confidence-based metrics compare fairly. |
| `--checkpoints 4,6,8` | Also evaluate the averaged weights after these epochs (one run gives the epoch curve). |
| `--epochs --hash-bits --min-count --prune --scale --seed --temperature` | Override one setting of the configuration. |
| `--restrict none\|history\|full`, `--history predicted\|gold` | Restriction mode (A, B, C) and history mode. |
| `--features none\|F1,F2,F3,F4\|positionCues,clauseCues,rightPreview,inflection` | Context feature groups of the model (experiment override; the groups are stored as a mask in the weights header; `none` is the base feature set, the one before the groups existed, and trains as it did then; the embedded model uses `clauseCues`). Names are accepted in any order and case, short or long. |
| `--preview-folds N` | Number of jackknife folds of the `rightPreview` group (2 to 10, default 5; experiment override). |
| `--source NAME:WEIGHT` | Set the weight of a source (0 leaves it out); repeatable. |
| `--max-sentences N`, `--sample-permille N` | Limit or subsample every source (deterministic by sentence id). |
| `--mask-disputed true\|false`, `--silver-min N` | Masking of `disputed` tokens and of silver tokens below N percent. |

Overrides and `--calibrate` are for experiments: they require `--output` (the run is refused with exit code 2 otherwise),
and they are rejected together with `--check` and `--update-config`.

## Configuration

`training.json` names the corpora (`open`, `masc`, `silver`; each with an integer `weight`, the number of times its
sentences enter every epoch) and every training setting. It contains no floating-point training setting except the
softmax `temperature`, which is chosen once by `calibrate` and only written into the file header. The weight scale is
`1 / quantScale` with `quantScale` a power of two, so it is exact in the file.

## Determinism contract

The training is integer arithmetic (weights `int`, time-weighted sums `long`), the shuffle uses an own SplitMix64
generator, and the writer sorts the buckets, so the **uncompressed** image is designed to be byte-identical on every
operating system and framework for the same corpora, configuration and library; CI verifies it on Windows, Linux and
macOS against the same pinned hash. The Deflate bytes of the committed file depend on the
zlib of the runtime, so `--check` compares uncompressed images only. The tests train a small subset and compare the
SHA-256 of the image with a pinned value on every target framework.

## Development data and the held-out gate

Every setting is selected on development data: the two development sets (`data/open/dev.tsv`,
`data/eval/gold-v2/dev.tsv`) and, for the feature group, also the hard-shape development set and the development part of
the external treebank (below). Every command
except `evaluate`, `eval-shapes` and `eval-arm` reads those two files and nothing else: evaluation accepts exactly those two paths, and
training accepts only files below the repository's `data` folder (after the path is made absolute and links are resolved) and refuses
held-out and development files there (`Evaluation/DevSets.cs`, tested). The held-out sets are read by `evaluate` only, and
only with a committed selection artifact that is valid for the frozen model.

The hard-shape development set has two files, the standard tranche (`data/eval/shapes-dev/dev.tsv`) and the hard tranche
(`data/eval/shapes-dev/hard.tsv`), and they are development files with a guard of their own:
`eval-shapes` reads them through `DataGuard.EnsureShapesDevelopmentFile` (an allow-list of exactly those two paths, links refused)
and a header that announces a `dev split`; the tranche of an item is the file it was read from, never a comment. The reader also
checks that the corpus header and the sentence ids belong to the tranche of the file (the hard tranche says `hard tranche` and its
ids start with `shapes-dev-hard-`; the standard tranche does neither), so a copy of one file at the path of the other is refused. They are not in
the two-file allow-list, so no other command can read them, they are not held-out files, so `evaluate` never reads them, and
training refuses them like every other development file (both lie below the `eval` folder). They are for selecting settings (for example the context-feature work),
never a held-out figure; the shape probe stays evaluation only.

### Context-feature experiment

The experiment on sentence-level context features (the groups F1 position cues, F2 clause cues, F3 right preview and F4
inflection of the shared feature extractor) is pre-registered in `RUN-LOG.md` and run by the harness in
`context-features/`: `arms.json` (the ten arms and the five seeds), `run_arms.ps1` (resumable and sequential: per arm and
seed `train --seed S --features G [--preview-folds 5] --calibrate --output FILE`, then `eval-arm`; below-normal priority,
at most 8 threads, at least 6 GB free on C:; the interface contract and the schema of the per-run JSON are at the top of
the script) and `analyze_arms.py` (the table, the guards and the selection rule; it checks its own logic on synthetic runs).
All three files are frozen by
the hashes in the run log. The rule selected F2, which the embedded model enables through `training.json`; the other
groups stay switches that are off (`RUN-LOG.md` has the results and the caveats, and `docs/design/part-of-speech/model-trainer.md`
the design). On a machine whose cores are saturated by other processes the below-normal priority can starve a run;
`-Runner FILE` replaces the `dotnet` command with a wrapper of your own (for example one that starts it at normal priority).
To run everything (writes below the given folder, which must be on C:):

```text
pwsh tools/DemaConsulting.PartOfSpeech.Trainer/context-features/run_arms.ps1 -Out <folder>
python tools/DemaConsulting.PartOfSpeech.Trainer/context-features/analyze_arms.py --runs <folder> --out <report.md>
```

The treebank development table that `eval-arm` reads comes from `python tools/data/fetch_ud_ewt.py fetch` and `map`.
A model that is trained with the `rightPreview` group first trains one model per fold (without that group, with the other
groups of the arm), decodes the held-out fold with each, and then trains once on both decoding modes; its `train` output
prints one line per fold and the time of the jackknife, and the `Trained ... in X s` line is the time of the final training
alone.

### Held-out evaluation

The protocol (definitions, selection rule, stop conditions) is the section "Phase 5 protocol" of `RUN-LOG.md`; it was
written before any number existed. The order is fixed by code:

1. **Select on development data.** `thresholds` writes `docs/evaluation/operating-points.json` and
   `docs/evaluation/confidence-thresholds.md`. Commit both before step 3.
2. **Fetch the external treebank (local only, never committed).** `python tools/data/fetch_ud_ewt.py fetch`, then
   `python tools/data/fetch_ud_ewt.py map` (pinned hashes are enforced; the cache is git-ignored). `evaluate` converts
   nothing itself: it reads the mapped table (the four convention rules of `data/CONVENTIONS.md`) from the cache and
   verifies the pinned source file and the pinned hash of the mapped table first. `--ud-cache DIR` names another cache folder.
3. **Pre-flight.** `evaluate --preflight` reads and parses every held-out input (verifying the hashes and headers) and
   prints counts only, next to the documented counts; no tag, prediction or metric. A difference stops the work.
4. **The one run.** `evaluate --dump-dir DIR` (a folder outside the repository, which also receives the per-token records
   of the run) writes `docs/evaluation/test-results.md` and `test-results.json`. It refuses to run when
   `test-results.json` exists; a repeat needs that committed file to be deleted on purpose and the reason recorded in
   `RUN-LOG.md`. The run is claimed atomically: immediately before scoring `evaluate` creates the lock file
   `docs/evaluation/test-results.json.running` exclusively (a second run is refused) and removes it once the results file
   is written. After an aborted run the lock stays and blocks every later run; delete it by hand only after an aborted
   run and record the deletion and its reason in `RUN-LOG.md`. The model, the configuration, the temperature and the
   thresholds never change in a repeat.

Run the commands at below-normal priority, one process at a time (see _Machine limits_). `build.ps1` and CI do not run
`thresholds` or `evaluate`.

## Current model

`training.json` selects: sources open (x2), MASC and silver (labeled tokens with probability of at least 80%); restriction
mode `history` with predicted history; 6 epochs; 19 hash bits; magnitude pruning at 8 quantized units; `quantScale` 8;
the feature group `clauseCues`; temperature 9.514. The file `weights.bin` has 121,033 rows and 301,571 weights, is
1,146,827 bytes uncompressed and 517,210 bytes compressed, and the library embeds it (`Tagger.Default`). `train --check`
takes about 15 seconds and runs
in `build.ps1` and in the CI build on all three operating systems. The glossary decision (boost only, no trained
feature) and every experiment are in `RUN-LOG.md`; `docs/design/part-of-speech/model-trainer.md` is the design and
`docs/verification/part-of-speech/model-trainer.md` the verification. To change the model: edit `training.json` (or run
experiments with the overrides), run `train --update-config`, then `train --check`, run the tests, and note the change in
`RUN-LOG.md`; the committed corpora, the lexicon and the extractor are inputs, so changing any of them also requires a new
training run and new pinned hashes in the tests.

## Machine limits

Training is single-threaded. Peak memory is about 0.5 GB at `hashBits` 21 and 1 GB at 22 (two dense tables of
`2^hashBits * 17` cells; the shipped model uses 19 bits and needs about 0.2 to 0.6 GB); do not use 23 or 24. On a shared
machine run it at below-normal priority with a restricted processor affinity, one process at a time.

## Run log

`RUN-LOG.md` records every experiment (a Markdown table that must be the last thing in the file). `--log RUN-LOG.md
--label TEXT` on `train` or `eval` appends a row with the development metrics of the run.
