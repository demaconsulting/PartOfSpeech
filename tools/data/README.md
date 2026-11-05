<!-- markdownlint-disable MD013 MD060 -->

# Data Tools

Python 3 scripts (standard library only) that produce and check the files under [`data/`](../../data/README.md). They are
development-time tooling: nothing here is shipped in the NuGet package, and no CI job depends on them.

## Why Python

- **Precedent:** the WordSense project's `tools/open-corpus` and the experiments that produced the data are Python; the
  importers here are mechanical ports of that code, which keeps the conversions auditable against their origin.
- **Standard library only** (`re`, `csv`, `tarfile`, `urllib`, `hashlib`, `unittest`): no `pip install`, no new entry in
  `pip-requirements.txt`, no version pin to maintain; Python is already provisioned in the repository (lint environment,
  GitHub Actions `setup-python`).
- **Reproducible:** every input is pinned by SHA-256, every output is deterministic (sorted, fixed seeds recorded, LF,
  UTF-8 without BOM, NFC), and a re-run is bit-identical. `data/MANIFEST.tsv` and the C# data tests re-verify the committed
  outputs on every platform, so the scripts themselves do not need to run in CI.
- **Not shipped:** the trainer and tagger of the package stay C#; these scripts only prepare the data.

Python files are outside the repository's lint tooling (there is no Python linter configured), so
`python -m py_compile tools/data/*.py` and the unit tests below are the quality gate for this folder. They are inside the
repository's review tooling: `.reviewmark.yaml` lists `tools/data/**/*.py` and this README in `needs-review`, and two
review-sets cover them, `PartOfSpeech-DataConversion` (the common helpers, the importers, the lexicon build and their
tests) and `PartOfSpeech-DataChecks` (the leakage, lexicon and license checks, the manifest and the line-ending tool).

## Scripts

Run from the repository root. Scripts take their input locations as arguments or fetch pinned files into the git-ignored
`tools/data/cache/`; no script contains a user-specific absolute path.

| Script | Purpose | Notes |
| --- | --- | --- |
| `common.py` | shared helpers: paths, the 17 tags, corpus block reader/writer, status-and-flags cells (`parse_status`, `base_status`, `format_status`), LF/UTF-8 writer, SHA-256, pinned download, sentence keys, clitic splitter, lexicon reader and token-shape rules | unit tested |
| `import_open.py` | open corpus to `data/open/` (UPOS projection, `sources.tsv`, `LICENSE`) | `--source-dir <WordSense checkout>` or download |
| `import_eval.py` | shape probe, gold v2 and the STE challenge set to `data/eval/` | same |
| `import_masc.py` | MASC Penn trees to `data/masc/` (`ptb_to_upos`, leakage exclusion, mapping audit) | `--archive`, `--exclude-genre`; writes `sources.tsv`, `excluded-documents.tsv` and `dropped-sentences.tsv` |
| `import_silver.py` | `export-spike` (one-off), `derive`, `verify`, `verify-sources` | silver data to `data/silver/`; FAA only behind `--include-faa`, never committed; run after `build_lexicon.py` |
| `fetch_ud_ewt.py` | `fetch` the pinned UD English-EWT files into the cache; `map` writes the convention-mapped tables (cache only) | evaluation only; nothing is ever committed |
| `build_lexicon.py` | build `data/lexicon/lexicon.tsv`; `--write-pins` writes the form pin manifest | `--wordsense-dir`, `--k`, `--masc-k` |
| `check_lexicon.py` | coverage report (`coverage.tsv`, `COVERAGE.md`), gate G1, `--sweep` | exit code 1 if the gate fails |
| `check_leakage.py` | training versus evaluation overlap (documents, sentences of any length, near duplicates) | exit code 1 on any overlap; `--write-report` |
| `check_licenses.py` | scan for share-alike or non-commercial text, restricted corpus names, `*.conllu`; the MASC source table for the corpus-wide CC BY 3.0 US class; README license lines | exit code 1 on a finding |
| `make_manifest.py` | write or `--verify` `data/MANIFEST.tsv` | Markdown files excluded |
| `normalize_eol.py` | convert CRLF to LF in `data/` and `tools/data/` text files | run before `make_manifest.py` after editing on Windows |
| `leakage.py` | library: the evaluation sets and keys used by the importers and the leakage check; the rule that a long match excludes the document and a short match drops the sentence | |

Pins (`pins/`): `wordsense-inputs.tsv` (open corpus and evaluation inputs), `wordsense-form.manifest.tsv` (151 form
tables), `masc.tsv` (the MASC archive), `ud-ewt.tsv` (the UD files). All WordSense inputs are at commit
`6530de1928baf30f9a14446a54bfb5dd5f67b41c`.

## Tests

```text
python -m compileall -q tools/data
python -m unittest discover -s tools/data/tests
```

The unit tests cover the shared helpers, the Penn Treebank mapping, the silver derivation, the lexicon rules (including
the homograph-number parse of entry ids) and sanity checks of the committed data (tag set, manifest, lexicon gate).

## Machine Etiquette

The scripts are light (the heaviest, `import_masc.py`, takes seconds). On a shared workstation run them at below-normal
priority and one at a time.
