<!-- markdownlint-disable MD013 MD060 -->

# UD English-EWT (Fetch Only, No Data)

**No UD English-EWT data is committed anywhere in this repository.** This folder holds only this documentation. The
treebank is licensed under CC BY-SA 4.0 (share-alike), which this project does not take into its data; it is used for
**evaluation only**, is fetched on demand into a git-ignored cache, and nothing derived from it (not even sentence-key
hashes) is committed or trained on. A `.gitignore` rule for `*.conllu` and for `tools/data/cache/` makes an accidental
commit impossible.

## Fetch and Map

```text
python tools/data/fetch_ud_ewt.py fetch                      # download and verify the pinned files
python tools/data/fetch_ud_ewt.py map                        # write the convention-mapped dev and test tables
```

- **Source:** <https://github.com/UniversalDependencies/UD_English-EWT>, pinned to commit
  `4a4d77f599ea53cc405f85d0cec4b2f14f81d42b` (the same files WordSense pins).
- **Cache:** `tools/data/cache/ud-ewt/` (git-ignored). The script verifies the SHA-256 of every file and refuses a
  mismatch: `en_ewt-ud-dev.conllu` `39239e0a...90f8`, `en_ewt-ud-test.conllu` `fa024f43...494a`, `en_ewt-ud-train.conllu`
  `d68e0612...2143` (full hashes in `tools/data/pins/ud-ewt.tsv`). The train file is fetched only for the leakage check.
- **Convention map:** the corpus conventions differ from UD in four documented ways (possessive determiners, copula `be`,
  phrasal-verb particles, prepositions before gerunds), so the reference tags are mapped before scoring a tagger. The
  rules are in [../../CONVENTIONS.md](../../CONVENTIONS.md); the mapped tables (`ud_dev.tsv`, `ud_test.tsv`) keep the raw
  tag and the rule next to the mapped tag and never leave the cache.

## Use in This Project

- **Evaluation:** the mapped `test` table is scored once by the `evaluate` command of the trainer, for the frozen model,
  as a local run; the mapped `dev` table is not used to choose anything. The step-by-step recipe, the pinned files and
  how the results are read are in the [evaluation record](../../../docs/evaluation/README.md). Only aggregate figures
  and tag-level confusion are committed (to `docs/evaluation/`); no UD word, sentence or sentence id is.
- **Leakage:** `tools/data/check_leakage.py` adds the sentence keys of the train, dev and test files to the evaluation
  universe when the cache is present, and `import_masc.py` and `import_silver.py` drop any training document (long
  matches) or sentence (short matches) that equals a sentence of them (only document and sentence ids are recorded).
- **Not used for:** training, lexicon building, tuning of rules on its test split, or any committed file.
