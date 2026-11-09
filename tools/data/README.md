<!-- markdownlint-disable MD013 MD060 -->
<!-- cspell:ignore testdata ngrams stdlib -->

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
| `check_leakage.py` | training versus evaluation overlap for the four training sets (open train, MASC, silver, imperative: documents, sentences of any length, near duplicates); second section: the hard-shape development set versus every other evaluation set (documents, exact sentences, shared word 5-grams with the shape probe, near duplicates); third section (when `hard.tsv` exists): the hard tranche against the same sets and against the standard tranche (a shared document is allowed; an exact sentence and a same-document near duplicate fail); fourth section (each of `compound-head.tsv`, `headings.tsv` and `named-state.tsv` when it exists): a new tranche against the same sets, the standard and the hard tranche (no shared document, exact sentence or same-document near duplicate allowed) and the other new tranches (a shared document is reported, not gated) | exit code 1 on any overlap (a near duplicate counts only for the imperative source; the near duplicates of the other training sets are reported, not gated); `--write-report` writes `data/leakage-report.tsv`, `data/eval/shapes-dev/leakage-report.tsv`, `data/eval/shapes-dev/hard-leakage-report.tsv` and `data/eval/shapes-dev/<tranche>-leakage-report.tsv` (counts only; no probe text is ever printed) |
| `check_licenses.py` | scan for share-alike or non-commercial text, restricted corpus names, `*.conllu`; FAA material in the source tables of the hard-shape development set and of the imperative source (`imperative/sources.tsv`, which is scanned for restricted licenses too); its exclusion record `eval/shapes-dev/excluded-sources.tsv` (required; a missing, zero-byte, header-less or malformed file is a finding, a header-only file is valid; no used source row and no `dev.tsv`, `hard.tsv`, `compound-head.tsv`, `headings.tsv` or `named-state.tsv` document may match an excluded id or URL; the sources tables of the three newer tranches are scanned for FAA material and restricted licenses too); the MASC source table for the corpus-wide CC BY 3.0 US class; README license lines; the staged corrections (`data/corrections`): CC0-1.0 declaration, MASC attribution link, every row's document listed with an allowed license | exit code 1 on a finding |
| `make_manifest.py` | write or `--verify` `data/MANIFEST.tsv` | Markdown files excluded |
| `normalize_eol.py` | convert CRLF to LF in `data/` and `tools/data/` text files | run before `make_manifest.py` after editing on Windows |
| `leakage.py` | library: the evaluation sets (the hard-shape development set among them, required; its hard tranche `shapes-dev-hard` (`hard.tsv`) is required too, `HARD_TRANCHE_REQUIRED` is `True`; the three newer tranches `shapes-dev-compound-head`, `shapes-dev-headings` and `shapes-dev-named-state` are evaluation sets when their files exist) and keys used by the importers and the leakage check; the rule that a long match excludes the document and a short match drops the sentence; the hashed hard-shape exclusion keys (`held_out_overlap_keys`: hashed sentence keys of the evaluation sets, both tranches left out, and hashed word 5-grams of the probe's `sentence` column only, an opaque set for the builder of the development set) | |
| `corrections.py` | the staged corrections in `data/corrections/`: table formats, row and table validation (`mechanical.tsv`, `adjudicated.tsv`; an adjudicated row replaces the mechanical row of its token; `withdrawn-<detector>.tsv` holds adjudicated rows taken out on purpose, never applied), `validate` against the corpora (pending or applied state), `apply [--write] [--reverse]` (edits corpus lines in place), `withdraw --detector ID --reason TEXT [--write]` (pending state only: the adjudicated rows of one detector move to `withdrawn-ID.tsv`), `show` | exit code 1 on an error; `apply` and `withdraw` write only with `--write`; the committed corpora are in the applied state, so a second `apply` is refused (reverse, withdraw, apply again) |
| `convention_rules.py` | library: the rule registry of the conventions (mechanical `fix` rules with their guards, and `check` rules), shared by the linter and the generator | the catalog is in [`data/corrections/README.md`](../../data/corrections/README.md#rule-catalog) |
| `convention_candidates.py` | library: the detectors of the cases that need a judgment (prenominal participles, mentioned words, foreign text, proper-noun boundaries, ...) | feeds `candidates.tsv` |
| `check_conventions.py` | the conventions linter: counts per rule and source, `--apply` (lint as after the corrections; a no-op in the applied state), `--list RULE`, `--tsv FILE`, `--tier fix` or `--tier check`, `--enforce` | report only (exit code 0) without `--enforce`; with `--enforce` exit code 1 on a violation of the selected tiers; the STE challenge set is linted but never counts; the imperative training source is linted (own column, never corrected, not a source of the corpus resources of the rules) and counts, so its fix tier must stay at zero |
| `make_corrections.py` | generate `data/corrections/{mechanical,candidates,exceptions}.tsv` from the rules; `--check` (compare with the committed files, and validate `adjudicated.tsv` when it exists), `sample` (seeded hand-check sample), `fixed-point`, `catalog` | deterministic; refuses candidates above 5 MB; never writes `adjudicated.tsv` |
| `adjudicate.py` | the adjudication of the candidates by two blind language-model taggers, everything around the models: `select` (sentences, bands and the 14,000 cap, volumes first), `export` (blind three-column batches, private key, state table), `check-batches`, `validate-output` (the taggers' outputs), `status`, `decide [--drop CLASS,...]` (decision rule, writes `adjudicated.tsv` and the private reports), `sample` and `review-result` (the precision gate of a third reviewer model) | needs a work folder outside the repository (`--work`, with the flag files of the off-the-shelf taggers); never calls a model; exit code 1 on an invalid batch, output or table; the method is in [`data/corrections/README.md`](../../data/corrections/README.md#adjudication) |
| `shapes_dev.py` | the builder of the hard-shape development set `data/eval/shapes-dev/`, everything around the models: `exclusions` (documents that training or evaluation already used), `tokenize` (the trainer command `shapes-tokenize`), `candidates` (position and lexicon pre-selection, leakage and probe 5-gram filters), `export` / `check-batches` / `validate-output` / `status` (blind batches, reusing the `adjudicate.py` batch format), `decide` (the decision rule), `sample` and `review-result` (the model review, stop below 95% precision), `build` (`dev.tsv`, `sources.tsv`, `excluded-sources.tsv`), `report` (counts for the README). `--tranche hard` (before the command) is the profile of the hard tranche: `hard.tsv`, `hard-sources.tsv`; targets chosen by the contested band of the WordSense Basic classifier (`hmm.tsv` of the harness below), hardest first, standard-tranche sentences excluded, floors and caps instead of exact counts | needs a work folder outside the repository (`--work`, required and before the command: `sources-draft.tsv`, `text/<source_id>.txt`, `authored/<shape>.tsv`, optional `excluded-sources.tsv` for documents the owner dropped (optional only because the committed record is required by `exclusions`, `candidates`, `decide`, `sample`, `build` and `report`; one function, `load_exclusions`, reads and validates both tables and every command gets its exclusions only through it: a URL listed under another id is honored, one id with two URLs is refused), `hmm.tsv` for the hard tranche; the layout and the pre-registered rule of the hard tranche are in the script's docstring); stdlib only; never calls a model and reads no PartOfSpeech model file; sees the held-out sets only through `leakage.held_out_overlap_keys()` (an opaque set of hashes, probe sentence column only) as an exclusion filter and prints no probe text; `build` refuses a document listed in the committed `data/eval/shapes-dev/excluded-sources.tsv` and a missed quota (standard: 120 targets per shape, 100 controls, lemma caps, 30% authored share; hard: floors 80/40/70/0/80 and caps 100/100/100/0/100 per shape, controls 50 to 60) unless `--allow-short`; the trainer command `shapes-tokenize` needs the .NET SDK |
| `shapes_dev_extra.py` | the builder of the three newer tranches of the development set (`compound-head.tsv`, `headings.tsv`, `named-state.tsv`, with `<tranche>-sources.tsv`), everything around the models: `lines` (heading lines of the OCR text), `tokenize`, `preflight` (pool sizes, counts only), `candidates` (frames by position and lexicon, the contested band for `compound-head`, the top-up rounds `h2` and `h3`), `report`, `sample` and `review-result` (the model review of 60 targets, stop below 95% precision) and `build` (quota gates per stratum); the blind labeling is the unchanged `shapes_dev.py export / check-batches / validate-output / decide --round <tranche>` | needs a work folder outside the repository (`--work`, before the command; the layout of `shapes_dev.py` plus `raw/<source_id>.txt`, `lines/<source_id>.txt` and `rounds/<tranche>/`) and, for `compound-head`, the `hmm.tsv` of the harness below (pinned by SHA-256 in the script); stdlib only; imports `shapes_dev.py` and `imperative_data.py` read-only; never calls a model and reads no PartOfSpeech model file, and never reads a held-out set (a test scans its source); the pre-registered rule and its amendments are in the script's docstring |
| `imperative_data.py` | the builder of the training source `data/imperative/` (about 900 sentences that start with an imperative verb, from US federal instructional text), everything around the models: `exclusions` (documents that training, evaluation or the shapes development set already used; checks the draft and its public-domain basis), `candidates` (the `v-imperative-initial` frame of `shapes_dev.py`, leakage, probe 5-gram and shapes-dev sentence filters, lemma cap 9), `build` (kept = both blind labelers agree on every token and token 0 is a base-form VERB, no fix-tier violation, lemma cap 6, document cap 30, 600 to 900 sentences: `imperative.tsv`, `sources.tsv`, `excluded-documents.tsv`, `dropped-sentences.tsv`), `sample` and `review-result` (the model review of 60 sentences, stop below 95% precision), `report`; the blind labeling itself is the unchanged `shapes_dev.py export / check-batches / validate-output / decide` | needs a work folder outside the repository (`--work`, before the command; the layout of `shapes_dev.py`); stdlib only; imports `shapes_dev.py` read-only; never calls a model and reads no PartOfSpeech model file (a data test scans its source); the pre-registered rule is in the script's docstring |
| `hmm-tags/` | standalone C# console tool (not in the solution, so `build.ps1`, `lint.ps1` and CI never build it): `form\|POS\|pNoun\|pVerb` per token of the WordSense Basic classifier (HMM tagger plus cue model) for the selection of the hard tranche and of the `compound-head` tranche; see its [README](hmm-tags/README.md) | needs a pinned copy of `DemaConsulting.WordSense.dll` outside the repository (`-p:WordSenseDll=PATH`); the header of its output records the WordSense commit and the DLL SHA-256; no reference to the PartOfSpeech library |

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

**Developer-run tests.** `test_shapes_dev.py` tests the development-set builder on a miniature data and work folder (exclusions,
position heuristics, leakage and probe filters, blind export, the decision rule, selection caps, build, the review gate, and the
dev-versus-evaluation leakage check, plus a byte-identical regression of the standard profile); `test_shapes_dev_hard.py` tests the
hard tranche (the `hmm.tsv` reader, the contested band and its order, the standard-tranche exclusion, the floors and caps, the
build, the hand-check sample, and the leakage and license checks on `hard.tsv`); `test_shapes_dev_extra.py` tests the three newer tranches (the
complement frame of `n-compound-head`, the heading extraction and filter, the state-name frame, the exact-tag decision, the selection caps, the quota
gates, the hand-check sample, and the scans for a model and for a direct read of a held-out set). Their file names are not in the CI pattern below,
so they are developer gates, not requirement evidence (the requirements of the development set are evidenced by C# tests):

```text
python -m unittest discover -s tools/data/tests -p "test_shapes_dev*.py"
```

`SHAPES_DEV_STANDARD_WORK=W` (the work folder of the standard tranche) adds a test that rebuilds the committed `dev.tsv`,
`sources.tsv` and `excluded-sources.tsv` from it and compares the bytes.

`test_imperative_data.py` tests the builder of the imperative training source on the same miniature folders (the used-document index
and the draft checks, the candidate frame and its filters, the caps, the keep rule, the fix-tier drop, the quota gate, the review
sample and gate, the near-duplicate gate of `check_leakage.py` (an imperative near duplicate fails, another training source's does
not), and a scan that the tool reads no model); it is a developer gate too:

```text
python -m unittest discover -s tools/data/tests -p "test_imperative_data.py"
```

**CI gate.** No workflow job runs the full suite (some tests read the git-ignored download cache). The quality-checks job
runs `lint.ps1`, and its Python step (the `[PROJECT-SPECIFIC]` extension point) runs, in about one minute:

```text
python -m compileall -q tools/data
python tools/data/tests/run_trx.py --out artifacts/python-data-tools.trx --pattern "test_corrections_*.py"
python tools/data/corrections.py validate
python tools/data/make_corrections.py --check
python tools/data/check_conventions.py --tier fix --enforce --apply
python tools/data/check_conventions.py --tier check --apply
```

A failing step fails the lint run. The linter enforces the fix tier (a violation of a `fix` rule fails the run; the
evaluation-only STE set is shown and never counts; the imperative source counts) and reports the check tier, which needs judgment, without failing on it.
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
