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
| `calibrate --weights FILE` | Choose the softmax temperature that minimizes the log-likelihood on the open development split. |
| `compare --a FILE --b FILE` | Paired bootstrap by document (1,000 resamples, fixed seed) of the difference between two weights files. |
| `glossary-probe --weights FILE` | Simulated glossaries on the open development split: score boost versus hard constraint. |
| `glossary-experiment` | Dev-only glossary decision: trains the configured model and a model with trained glossary features, compares them with score boosts and prints the pre-registered decision (outcome: boost only, see `RUN-LOG.md`). |

Exit codes: 0 success, 1 stale weights (`train --check`), 2 usage or data error.

### Options of `train`

| Option | Meaning |
| --- | --- |
| `--config FILE` | Configuration file (default `tools/DemaConsulting.PartOfSpeech.Trainer/training.json`). |
| `--output FILE` | Where to write the (compressed) weights file. |
| `--update-config` | Record the SHA-256 of the uncompressed image in the configuration file. |
| `--check` / `--weights FILE` | Verify instead of write; `--weights` names the committed file when it is not the embedded resource. |
| `--eval`, `--label TEXT`, `--log FILE` | Evaluate the result on the development sets; label the run; append a row to a run-log table. |
| `--calibrate` | Experiments only: fit the softmax temperature of the run on the open development split before scoring and writing, so confidence-based metrics compare fairly. |
| `--checkpoints 4,6,8` | Also evaluate the averaged weights after these epochs (one run gives the epoch curve). |
| `--epochs --hash-bits --min-count --prune --scale --seed --temperature` | Override one setting of the configuration. |
| `--restrict none\|history\|full`, `--history predicted\|gold` | Restriction mode (A, B, C) and history mode. |
| `--source NAME:WEIGHT` | Set the weight of a source (0 leaves it out); repeatable. |
| `--max-sentences N`, `--sample-permille N` | Limit or subsample every source (deterministic by sentence id). |
| `--mask-disputed true\|false`, `--silver-min N` | Masking of `disputed` tokens and of silver tokens below N percent. |

Overrides are for experiments; they are rejected together with `--check` and `--update-config`.

## Configuration

`training.json` names the corpora (`open`, `masc`, `silver`; each with an integer `weight`, the number of times its
sentences enter every epoch) and every training setting. It contains no floating-point training setting except the
softmax `temperature`, which is chosen once by `calibrate` and only written into the file header. The weight scale is
`1 / quantScale` with `quantScale` a power of two, so it is exact in the file.

## Determinism contract

The training is integer arithmetic (weights `int`, time-weighted sums `long`), the shuffle uses an own SplitMix64
generator, and the writer sorts the buckets, so the **uncompressed** image is byte-identical on every operating system
and framework for the same corpora, configuration and library. The Deflate bytes of the committed file depend on the
zlib of the runtime, so `--check` compares uncompressed images only. The tests train a small subset and compare the
SHA-256 of the image with a pinned value on every target framework.

## Development data only

Every setting is selected on the two development sets (`data/open/dev.tsv`, `data/eval/gold-v2/dev.tsv`). The tool
has no code path that reads any other evaluation file: evaluation accepts exactly those two paths, and training refuses
held-out and development files (`Evaluation/DevSets.cs`, tested).

## Current model

`training.json` selects: sources open (x2), MASC and silver (labeled tokens with probability of at least 80%); restriction
mode `history` with predicted history; 6 epochs; 19 hash bits; magnitude pruning at 8 quantized units; `quantScale` 8;
temperature 6.727. The file `weights.bin` has 119,632 rows and 290,862 weights, is 1,111,898 bytes uncompressed and
492,971 bytes compressed, and the library embeds it (`Tagger.Default`). `train --check` takes about 15 seconds and runs
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
