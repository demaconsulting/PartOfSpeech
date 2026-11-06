<!-- markdownlint-disable MD013 MD060 -->
<!-- cspell:ignore testdata -->

# Data Tools

Python 3.10 or later scripts (standard library only) that produce and check the files under [`data/`](../../data/README.md).
They are development-time tooling: nothing here is shipped in the NuGet package. One part runs in CI: the correction tools
and their tests are a gate of `lint.ps1`, which the quality-checks job of the workflow runs (see _Tests_ below); the
importers and the other checks are not run in CI.

## Why Python

- **Precedent:** the WordSense project's `tools/open-corpus` and the experiments that produced the data are Python; the
  importers here are mechanical ports of that code, which keeps the conversions auditable against their origin.
- **Standard library only** (`re`, `csv`, `tarfile`, `urllib`, `hashlib`, `unittest`): no `pip install`, no new entry in
  `pip-requirements.txt`, no version pin to maintain; Python is already provisioned in the repository (lint environment,
  GitHub Actions `setup-python`).
- **Reproducible:** every input is pinned by SHA-256, every output is deterministic (sorted, fixed seeds recorded, LF,
  UTF-8 without BOM, NFC), and a re-run is bit-identical. `data/MANIFEST.tsv` and the C# data tests re-verify the committed
  outputs on every platform, so the importers themselves do not need to run in CI.
- **Not shipped:** the trainer and tagger of the package stay C#; these scripts only prepare the data.

Python files are outside the repository's lint tooling (there is no Python linter configured), so
`python -m compileall -q tools/data` and the unit tests below are the quality gate for this folder. They are inside the
repository's review tooling: `.reviewmark.yaml` lists `tools/data/**/*.py` and this README in `needs-review`, and three
review-sets cover them, `PartOfSpeech-DataConversion` (the common helpers, the importers, the lexicon build and their
tests), `PartOfSpeech-DataChecks` (the leakage, lexicon and license checks, the manifest and the line-ending tool) and
`PartOfSpeech-DataCorrections` (the conventions linter, the generator and validator of the staged corrections, the adjudication
tool, and their tests and fixtures).

## Scripts

Run from the repository root. Scripts take their input locations as arguments or fetch pinned files into the git-ignored
`tools/data/cache/`; no script contains a user-specific absolute path.

| Script | Purpose | Notes |
| --- | --- | --- |
| `common.py` | shared helpers: paths, the 17 tags, corpus block reader/writer, status-and-flags cells (`parse_status`, `base_status`, `format_status`), LF/UTF-8 writer, SHA-256, pinned download, sentence keys, clitic splitter, lexicon reader and token-shape rules | unit tested |
| `import_open.py` | open corpus to `data/open/` (UPOS projection, `sources.tsv`, `LICENSE`) | `--source-dir <WordSense checkout>` or download |
| `import_eval.py` | shape probe, gold v2 and the STE challenge set to `data/eval/` | same |
| `import_masc.py` | MASC Penn trees to `data/masc/` (`ptb_to_upos`, leakage exclusion, mapping audit) | `--archive`, `--exclude-genre`; writes `sources.tsv`, `excluded-documents.tsv` and `dropped-sentences.tsv` |
| `import_silver.py` | `export-spike` (one-off), `derive`, `verify`, `verify-sources` | silver data to `data/silver/`; FAA only behind `--include-faa`, never committed; run after `build_lexicon.py`; `verify` describes the derivation, so it passes only on the pre-correction file (after `corrections.py apply --write --reverse`) |
| `fetch_ud_ewt.py` | `fetch` the pinned UD English-EWT files into the cache; `map` writes the convention-mapped tables (cache only) | evaluation only; nothing is ever committed |
| `build_lexicon.py` | build `data/lexicon/lexicon.tsv`; `--write-pins` writes the form pin manifest | `--wordsense-dir`, `--k`, `--masc-k` |
| `check_lexicon.py` | coverage report (`coverage.tsv`, `COVERAGE.md`), gate G1, `--sweep` | exit code 1 if the gate fails |
| `check_leakage.py` | training versus evaluation overlap (documents, sentences of any length, near duplicates) | exit code 1 on any overlap; `--write-report` |
| `check_licenses.py` | scan for share-alike or non-commercial text, restricted corpus names, `*.conllu`; the MASC source table for the corpus-wide CC BY 3.0 US class; README license lines; the staged corrections (`data/corrections`): CC0-1.0 declaration, MASC attribution link, every row's document listed with an allowed license | exit code 1 on a finding |
| `make_manifest.py` | write or `--verify` `data/MANIFEST.tsv` | Markdown files excluded |
| `normalize_eol.py` | convert CRLF to LF in `data/` and `tools/data/` text files | run before `make_manifest.py` after editing on Windows |
| `leakage.py` | library: the evaluation sets and keys used by the importers and the leakage check; the rule that a long match excludes the document and a short match drops the sentence | |
| `corrections.py` | the staged corrections in `data/corrections/`: table formats, row and table validation (`mechanical.tsv`, `adjudicated.tsv`; an adjudicated row replaces the mechanical row of its token; `withdrawn-<detector>.tsv` holds adjudicated rows taken out on purpose, never applied), `validate` against the corpora (pending or applied state), `apply [--write] [--reverse]` (edits corpus lines in place), `withdraw --detector ID --reason TEXT [--write]` (pending state only: the adjudicated rows of one detector move to `withdrawn-ID.tsv`), `show` | exit code 1 on an error; `apply` and `withdraw` write only with `--write`; the committed corpora are in the applied state, so a second `apply` is refused (reverse, withdraw, apply again) |
| `convention_rules.py` | library: the rule registry of the conventions (mechanical `fix` rules with their guards, and `check` rules), shared by the linter and the generator | the catalog is in [`data/corrections/README.md`](../../data/corrections/README.md#rule-catalog) |
| `convention_candidates.py` | library: the detectors of the cases that need a judgment (prenominal participles, mentioned words, foreign text, proper-noun boundaries, ...) | feeds `candidates.tsv` |
| `check_conventions.py` | the conventions linter: counts per rule and source, `--apply` (lint as after the corrections; a no-op in the applied state), `--list RULE`, `--tsv FILE`, `--tier fix` or `--tier check`, `--enforce` | report only (exit code 0) without `--enforce`; with `--enforce` exit code 1 on a violation of the selected tiers; the STE challenge set is linted but never counts |
| `make_corrections.py` | generate `data/corrections/{mechanical,candidates,exceptions}.tsv` from the rules; `--check` (compare with the committed files, and validate `adjudicated.tsv` when it exists), `sample` (seeded hand-check sample), `fixed-point`, `catalog` | deterministic; refuses candidates above 5 MB; never writes `adjudicated.tsv` |
| `adjudicate.py` | the adjudication of the candidates by two blind language-model taggers, everything around the models: `select` (sentences, bands and the 14,000 cap, volumes first), `export` (blind three-column batches, private key, state table), `check-batches`, `validate-output` (the taggers' outputs), `status`, `decide [--drop CLASS,...]` (decision rule, writes `adjudicated.tsv` and the private reports), `sample` and `review-result` (the precision gate of a third reviewer model) | needs a work folder outside the repository (`--work`, with the flag files of the off-the-shelf taggers); never calls a model; exit code 1 on an invalid batch, output or table; the method is in [`data/corrections/README.md`](../../data/corrections/README.md#adjudication) |

Pins (`pins/`): `wordsense-inputs.tsv` (open corpus and evaluation inputs), `wordsense-form.manifest.tsv` (151 form
tables), `masc.tsv` (the MASC archive), `ud-ewt.tsv` (the UD files). All WordSense inputs are at commit
`6530de1928baf30f9a14446a54bfb5dd5f67b41c`.

## Tests

```text
python -m compileall -q tools/data
python -m unittest discover -s tools/data/tests
```

The unit tests cover the shared helpers, the Penn Treebank mapping, the silver derivation, the lexicon rules (including
the homograph-number parse of entry ids), sanity checks of the committed data (tag set, manifest, lexicon gate) and the
correction tools: the overlay (`test_corrections_overlay.py`), the rules and their guards on the fixture sentences of
`convention-cases.tsv` (`test_corrections_rules.py`), the linter (`test_corrections_linter.py`), the generator
(`test_corrections_generator.py`, including the check that the committed tables are valid against the committed corpora) and
the adjudication tool (`test_corrections_adjudication.py`: cell escaping, blind batches, bands and cap, the output validator, the
decision rule, the table, the sample and the gate, on the miniature data folder).
`corrections-cases.tsv` and `candidate-cases.tsv` hold the row cases that both this validator and the C# data tests are held
to (as `status-cases.tsv` does for the status cell); `corrections_testdata.py` builds the miniature data folder of the tests.

**CI gate.** No workflow job runs the full suite (some tests read the git-ignored download cache). The quality-checks job
runs `lint.ps1`, and its Python step (the `[PROJECT-SPECIFIC]` extension point) runs, in about one minute:

```text
python -m compileall -q tools/data
python tools/data/tests/run_trx.py --out artifacts/python-data-tools.trx --pattern "test_corrections_*.py"
python tools/data/corrections.py validate
python tools/data/make_corrections.py --check
python tools/data/check_conventions.py --tier fix --enforce
python tools/data/check_conventions.py --tier check
```

A failing step fails the lint run. The linter enforces the fix tier (a violation of a `fix` rule fails the run; the
evaluation-only STE set is shown and never counts) and reports the check tier, which needs judgment, without failing on it.
`make_corrections.py --check` is state-aware: the corrections are applied, so it reverses them in a temporary copy before it
regenerates the tables. `run_trx.py` runs
the same discovery as `python -m unittest discover -s tools/data/tests -p "test_corrections_*.py"` and also writes the results as a
TRX file; the quality-checks job uploads `artifacts/`, and ReqStream reads the file with the C# results as the evidence of the
`PartOfSpeech-Adjudication-*` requirements (the requirement lists the test method names). The other requirements evidence stays
in C# (`PartOfSpeech_DataCorrections_*` in `PartOfSpeechDataTests.cs`), which reads the
committed tables and corpora. A C# port of the rules was rejected: it would duplicate the detectors and let two
implementations of a heuristic drift.

## Machine Etiquette

The scripts are light (the heaviest, `import_masc.py`, takes seconds; the correction tools and their tests that read the
corpora take up to a minute). On a shared workstation run them at below-normal priority and one at a time.
