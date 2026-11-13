<!-- cspell:ignore selftest -->

# Independent audit of the technical-mix run inputs

`audit_runs.py` is a small, standard-library-only script that checks the inputs of the beta.3 phase 2
technical-mix experiment (E2): the 48 result files and the weights files that `beta3-tech/run_arms.ps1` wrote. It is
**not** part of the pre-registered, hash-pinned set in `beta3-tech/`; no frozen-hash table covers this folder. It does
not change any rule, number or decision of the experiment; it only reads the files the analyzer reads and fails closed.

## Purpose

The frozen analyzer `beta3-tech/analyze_tech.py` (which must not be edited) validates some of these inputs less
strictly than this audit does:

- the byte-identity check of the BASE weights file is skipped when `BASE-s20260101.bin` is missing;
- a result without `arm`, `seed` or `sources` is not rejected, and the count ranges are validated only weakly.

The audit makes those checks strict, independently of the analyzer. A harness that follows this one must make them
fail closed inside its own analyzer.

## What it checks

Every check prints one `PASS` or `FAIL` line. The exit code is 0 only when every check passes, 1 otherwise (and 2 for
a usage error).

- `files`: all 48 result files `<arm>-s<seed>.json` (6 arms x 8 seeds of `arms.json`) exist and parse; no other
  result file is present.
- `fields`: every result has `arm`, `seed`, `sources` and `inputs.commit`; `arm` and `seed` agree with the file name.
- `grid`: the `(arm, seed)` pairs inside the results are unique and complete.
- `sources`: the `sources` of each result equal those of `arms.json` for its arm, as sets of `NAME:WEIGHT` (BASE has
  none).
- `features`: the `features` of each result equal those of `arms.json` for its arm.
- `provenance`: one repository commit in all results, clean tree, driver and inputs agree, identical input hashes.
- `counts`: `decisions` is above 0 for every shape and pooled block; `harmful` is at most `wrong` and at most
  `decisions`; `wrong` is at most `answers` for every calibration point; the dev-set token blocks agree; pooled
  blocks equal the sum of their parts.
- `tranches`: the tranche totals equal the sizes pinned in `shipped_figures.json` (S1 450, S2 285, S3 236, S4 446;
  technical 1,417) and each equals its target plus control items.
- `bins`: every `<arm>-s<seed>.bin` exists and hashes to the `weights.sha256` recorded in its result.
- `base-shipped`: `BASE-s20260101.bin`, its recorded hash, the shipped main weights and the pinned value are one
  hash.
- `shipped-pin`: the shipped main weights (`git show b68307f:src/DemaConsulting.PartOfSpeech/Resources/weights.bin`)
  hash to `f384a877756dd2a646ce0e087ad034014f3bdfd2395b5f3751af152d2208ddad`.

On "wrong": in a shape block, `wrong` is `targetItems - targetCorrect`. It is not required to be at most `decisions`
there, because the defined-terms block has target items (adjectives) that are never decided; the rule
`wrong <= decisions` is applied to the `answers` of the calibration thresholds and curve points, where the schema
defines wrong over decided items.

## Usage

```text
python -B tools/DemaConsulting.PartOfSpeech.Trainer/beta3-tech-audit/audit_runs.py --runs <runs>
python -B tools/DemaConsulting.PartOfSpeech.Trainer/beta3-tech-audit/audit_runs.py --selftest
```

`--runs` is the folder that holds the result and weights files of the driver; it has no default. `--repo`, `--arms`
and `--figures` default to this checkout and to the files in `beta3-tech/`. The shipped weights are read from git
(`git show`), not from the working tree, whose `weights.bin` is the shipped TECH model. The self-test option
accepts `--selftest-dir <folder on C:>` for its temporary copies.

The self-test builds a synthetic, passing set of 48 results in a temporary folder, proves that it passes, and then
proves that every tampered copy fails the check it targets: a missing result file, an extra result file, a missing
`arm`, `seed`, `sources` or `inputs.commit`, a duplicate and a missing `(arm, seed)`, wrong sources or features, a
dirty tree, mixed commits, `decisions` 0, `wrong` above `answers`, `harmful` above `wrong` or `decisions`, a pooled
block that differs from its parts, a tranche total that differs from its size, a missing BASE weights file, a
missing other weights file, an altered BASE weights file, a recorded hash that differs from the shipped hash, a
shipped hash that differs from the pinned value, an unreadable shipped file, and an empty folder.

The record of the run of this audit against the real inputs is in `RUN-LOG.md`, section "beta.3 phase 2: independent
audit of the run inputs".
