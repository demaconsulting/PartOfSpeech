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

The audit makes those checks strict, independently of the analyzer, and it is itself fail closed: a missing field,
series, row or file is an error, never a default or an empty set (see "Fail-closed rules"). A harness that follows
this one must make these checks fail closed inside its own analyzer.

## What it checks

Every check prints one `PASS` or `FAIL` line. The exit code is 0 only when every check passes, 1 otherwise (and 2 for
a usage error).

- `files`: the runs folder holds exactly the 48 result files `<arm>-s<seed>.json` (6 arms x 8 seeds of `arms.json`),
  their 48 weights files `<arm>-s<seed>.bin` and, optionally, the allowlisted files below; every other entry (a file
  with any name or extension, a directory, a link) is an error. Every result and `arms.json` parse as strict JSON (no
  `NaN` or `Infinity`, no duplicate object key), and each arm of `arms.json` has a string `id`, a `features` list and
  a `sources` object.
- `fields`: every result has `arm`, `seed`, `sources` and `inputs.commit`; `arm` and `seed` agree with the file name.
- `grid`: the `(arm, seed)` pairs inside the results are unique and complete.
- `sources`: the `sources` of each result equal those of `arms.json` for its arm, as sets of `NAME:WEIGHT` (BASE has
  none).
- `features`: the `features` of each result equal those of `arms.json` for its arm.
- `provenance`: `inputs` has exactly the sixteen expected keys (the fourteen `*Sha256` fields of the development files
  listed below, `commit` and `dirty`), no more and no fewer; every hash is a 64-digit lowercase hex string and equals
  the frozen value (so the hashes are identical in all runs); `inputs.dirty` and `driver.dirty` are `false`;
  `driver.commit` equals `inputs.commit`; there is exactly one repository commit in all results.
- `counts`: each result has exactly the 49 expected shape blocks (no more, no fewer; in the four sections `shapes`,
  `shapesNew`, `shapesFollowup` and `shapesTechnical`), `decisions` above 0 for every shape and pooled block, `harmful`
  at most `wrong` and at most `decisions`; the three dev-set token blocks (`openDev`, `goldDev`, `udDev`) are complete
  with accuracy in 0..100; the token counts and the item counts of every shape block are the same in all runs (the
  data fixes them); pooled blocks equal the sum of their parts; the calibration (see "Calibration schema") is
  complete.
- `tranches`: the tranche totals equal the sizes pinned in `shipped_figures.json` (S1 450, S2 285, S3 236, S4 446;
  technical 1,417) and each equals its target plus control items.
- `bins`: every `<arm>-s<seed>.bin` exists and hashes to the `weights.sha256` recorded in its result (a lowercase hex
  string); exactly 48 weights files are checked.
- `base-shipped`: `BASE-s20260101.bin`, its recorded hash, the shipped main weights and the pinned value are one
  hash.
- `shipped-pin`: the shipped main weights (`git show b68307f:src/DemaConsulting.PartOfSpeech/Resources/weights.bin`)
  hash to `f384a877756dd2a646ce0e087ad034014f3bdfd2395b5f3751af152d2208ddad`.

### Input-hash schema

The expected `inputs` object of every result has exactly these keys: `commit`, `dirty` and the fourteen hashes below
(copied, read only, from `FROZEN_INPUTS` of the frozen analyzer and the E2 pre-registration in `RUN-LOG.md`; the
audit hard-codes the names and the values):

`devSha256`, `hardSha256`, `udDevSha256`, `openDevSha256`, `goldDevSha256`, `compoundHeadSha256`, `headingsSha256`,
`namedStateSha256`, `compoundHead2Sha256`, `headings2Sha256`, `deverbalHeadsSha256`, `definedTermsSha256`,
`stepLinesSha256`, `clauseVerbsSha256`.

### Calibration schema

Under `shapesTechnical.calibration` every result must have the sets and series below, with the stated number of rows,
the stated thresholds in this order, and every field of every row. A missing series, row or field, a wrong threshold,
a non-integer count, or a boolean, null, NaN or text where a number belongs is an error.

| Set | Series |
| --- | --- |
| `technical` | `thresholds` (2 rows), `curve` (15 rows), `bins` (7 rows) |
| `allSingle` | `thresholds` (2 rows), `bins` (7 rows) |
| `nounVerbPooled` | `curve` (15 rows) and `decisions` (an integer above 0) |
| `tranches.deverbalHeads` | `thresholds` (2 rows), `bins` (7 rows) |
| `tranches.definedTerms` | `thresholds` (2 rows), `bins` (7 rows) |
| `tranches.stepLines` | `thresholds` (2 rows), `bins` (7 rows) |
| `tranches.clauseVerbs` | `thresholds` (2 rows), `bins` (7 rows) |

Every set also has `judged` (an integer above 0). The `thresholds` rows are at 0.90 and 0.95; the `curve` rows are at
0.45 to 0.95 in steps of 0.05 and then 0.96, 0.97, 0.98 and 0.99. A threshold row has `threshold`, `answers`, `wrong`,
`errorPer1k`, `intervalLowPer1k` and `intervalHighPer1k`; a curve row has these and `coverage`, `nounVerbAnswers`,
`harmful` and `harmfulPer1k`; a bin has `count`, `harmful`, `correctPercent`, `meanConfidence`, `low` and `high`.
Invariants per row: `answers` from 1 to `judged`; `wrong` at most `answers`; on the curve `harmful` at most `wrong` and
at most `nounVerbAnswers`, `nounVerbAnswers` at most `answers`, `coverage` from 0 to 1; per bin `harmful` at most
`count` and at most the wrong items of the bin; the bin counts sum to `judged`.

### Fail-closed rules

- No default fills a missing value, and no check passes because a list is empty. A check that finds no result at all
  fails, and a missing `inputs` or `driver` object, section, block, series, row or field is an error.
- A removed key is an error even when it is removed from every run (an identical set of fewer hashes is not accepted).
- Numbers must be real: a boolean is not an integer, and `NaN` and `Infinity` are refused (the file does not parse).
- A crashing check, or an input that cannot be loaded, is a failed check, not a traceback and not a pass.

### Allowlist of non-result files

The runs folder may contain, besides the 48 result files and the 48 weights files, only these files, which the driver
`beta3-tech/run_arms.ps1` and the analyzer `beta3-tech/analyze_tech.py` produce (each one is optional):

- `progress.log`, `build.txt` and `build.err.txt` (the driver);
- `<arm>-s<seed>.train.txt`, `.train.err.txt`, `.eval.txt` and `.eval.err.txt` for each of the 48 runs (the driver;
  standard output and error of the `train` and `eval-arm` commands);
- `report.md` and `decision.json` (the analyzer, run with `--out report.md --decision decision.json`).

Every allowlisted name must be a regular file. Anything else fails: a subdirectory, a link, a log of a `(arm, seed)`
that is not in the grid, an extra weights file, and the driver's transient files `<label>.json.part` and
`<label>.json.eval` (the driver removes them when a run completes, so one that is left is an unfinished run).

### Interpretation of "wrong"

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

## Self-test

The self-test builds a synthetic, passing set of 48 results (with the full schema above) in a temporary folder,
proves that it passes all 11 checks, and then proves that every tampered copy fails the check it targets. It has 341
tamper cases (one mutated copy each) in 68 groups, and one accept case; it takes about three minutes. The groups:

- results and fields: a missing or extra result file, a missing `arm`, `seed`, `sources` or `inputs.commit`, a
  duplicate and a missing `(arm, seed)`, wrong sources or features;
- provenance and the input schema: a dirty tree, mixed commits, each of the fourteen hashes and `dirty` removed from
  one run and from all runs, `inputs.commit` removed from all runs, `inputs` or `driver` (or a field of `driver`)
  removed, an extra `inputs` key, a malformed hash (short, uppercase, not hex, an integer; in one run and in all runs),
  a well-formed hash that differs from the frozen value;
- counts: `decisions` 0, a calibration row with `wrong` above `answers`, `harmful` above `wrong` or `decisions`, a
  pooled block that differs from its parts, a tranche total that differs from its size, every series of every set
  missing (one run and all runs), a whole set missing, the first, second or last row of every series missing, an extra
  row, a row that is not an object, a wrong threshold in every row of every grid, swapped rows, every field of every
  series kind missing, a null, text, boolean or fractional count, a missing `judged` or `decisions`, bins that do not
  sum to `judged`, `nounVerbAnswers` above `answers`, a missing shape block or section, an extra shape block, a
  missing dev-set block, accuracy above 100, a token or item count that differs between runs;
- strict JSON and `arms.json`: a `NaN` literal, a duplicate key, an arm without `features` or `sources`;
- the runs folder: an extra JSON file with a name that does not match the result pattern, an extra non-JSON file, an
  extra subdirectory, an allowlisted name that is a directory, a log of a run that is not in the grid, a missing
  result together with an extra file;
- weights: a missing BASE weights file, a missing other weights file, an altered BASE weights file (the
  `base-shipped` and `bins` checks), a recorded hash that differs from the shipped hash or is malformed, a shipped
  hash that differs from the pinned value, an unreadable shipped file, and an empty folder;
- accept: every allowlisted file present (the five named files and the four logs of each of the 48 runs) passes all
  checks.

The record of the runs of this audit against the real inputs is in `RUN-LOG.md`, section "beta.3 phase 2: independent
audit of the run inputs".
