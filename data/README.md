<!-- markdownlint-disable MD013 MD060 -->

# Data

Training data, evaluation data and the lexicon of `DemaConsulting.PartOfSpeech`: a part-of-speech tagger for .NET with the
17 Universal POS tags. Everything here is open data with its license, provenance and production recipe recorded. The scripts
that produce and check it are in [`tools/data/`](../tools/data/README.md); the tag set, the annotation conventions, the file
formats and every mapping table are in [CONVENTIONS.md](CONVENTIONS.md) (normative). Nothing in this folder is consumed by
the library at run time yet; later phases embed compact binary forms generated from it.

## Contents

| Folder | What | Sentences | Tokens | Size | License |
| --- | --- | ---: | ---: | ---: | --- |
| [open/](open/README.md) | open corpus, train/dev/test, UPOS | 14,980 | 261,641 | 10.3 MB | CC0-1.0 annotations; PD/CC0 sentences |
| [masc/](masc/README.md) | MASC written genres, Penn trees mapped to UPOS | 22,933 | 386,837 | 11.6 MB | CC-BY-3.0-US |
| [silver/](silver/README.md) | BERT silver: Tatoeba and Gutenberg text, model labels mapped to UPOS | 13,973 | 242,710 | 8.5 MB | CC0-1.0 labels; CC0/PD sentences |
| [eval/shapes/](eval/shapes/README.md) | noun/verb shape probe (evaluation) | 560 | | 0.06 MB | CC0-1.0 |
| [eval/gold-v2/](eval/gold-v2/README.md) | single-target gold tokens (evaluation) | 5,072 | | 2.4 MB | CC0-1.0 labels; PD sentences |
| [eval/ste-challenge/](eval/ste-challenge/README.md) | simplified-technical-English challenge set (evaluation) | 32 | 272 | 0.01 MB | CC0-1.0 |
| [eval/ud-ewt/](eval/ud-ewt/README.md) | UD English-EWT: fetch documentation only, **no data** | | | | CC BY-SA 4.0, never committed |
| [lexicon/](lexicon/README.md) | spelling to UPOS tags lexicon, closed-class list, coverage report | | | 4.5 MB | CC-BY-4.0 and CC-BY-3.0-US (attribution), others |

The training corpora (open train, MASC, silver) hold 209,224 + 386,837 + 242,710 tokens (silver: 232,318 labeled, 10,392
masked). Total size of `data/`: 37.4 MB; the largest file is `masc/masc.tsv` (11.6 MB). Attribution for MASC and the Open English
WordNet lexicon sources is in `masc/ATTRIBUTION.md` and in `THIRD-PARTY-NOTICES.md` at the repository root.

## Rules of This Folder

- **Open licenses only.** CC0, public domain, CC BY (with attribution) and project licenses. No share-alike and no
  non-commercial data. `tools/data/check_licenses.py` scans the source tables and the folder tree; it also refuses
  `*.conllu` files and any path that names SemCor, Brown or UD English-EWT (except the fetch-only `eval/ud-ewt/README.md`).
- **Model-written labels are labeled as such.** The open corpus, the shape probe, gold v2 and the silver data have tags that
  language models wrote; no human reviewed them. Each README says so and gives the owner's CC0 dedication of labels and
  selection (not a claim on the underlying sentences). Nothing here is legal advice.
- **Every file is reproducible.** Inputs are pinned by SHA-256 (WordSense commit
  `6530de1928baf30f9a14446a54bfb5dd5f67b41c`, the MASC archive, UD files); outputs are sorted, UTF-8 without BOM, LF, NFC.
  `MANIFEST.tsv` lists the size, SHA-256 and record counts of every data file (Markdown files excluded).
- **US English; evaluation data is never trained on.**
- **Not used:** SemCor and the Brown corpus (restricted or fair-use-only terms), FAA handbook text (US federal work, but the
  project owner excluded it from the open corpus and gold v2; it is available only behind an off-by-default flag and
  is never committed), UD English-EWT (CC BY-SA 4.0, evaluation only and fetched, not committed).

## Regenerating

From the repository root, with Python 3.10 or later (standard library only):

```text
python tools/data/import_open.py   --source-dir <WordSense checkout at the pinned commit>   # open/
python tools/data/import_eval.py   --source-dir <WordSense checkout at the pinned commit>   # eval/
python tools/data/fetch_ud_ewt.py fetch                                                     # cache for the leakage filter
python tools/data/import_masc.py   [--archive penn-treebank.tgz]                            # masc/
python tools/data/build_lexicon.py [--wordsense-dir <checkout>]                             # lexicon/lexicon.tsv
python tools/data/check_lexicon.py                                                          # lexicon/coverage.tsv, COVERAGE.md
python tools/data/import_silver.py derive                                                   # silver/silver.tsv
python tools/data/check_leakage.py --write-report                                           # leakage-report.tsv
python tools/data/make_manifest.py                                                          # MANIFEST.tsv
```

`--source-dir` and `--wordsense-dir` are optional: without them the pinned files are downloaded into the git-ignored
`tools/data/cache/`. The silver labeling itself (`import_silver.py export-spike`) is a documented one-off that needs the
earlier experiment's model output (`silver/README.md`).

## Leakage Check

No training document and no training sentence may appear in an evaluation set. Training sets: open train, MASC, silver.
Evaluation sets: open dev and test, gold v2 (dev and test), the shape probe, the STE challenge set, and UD English-EWT
train, dev and test when the git-ignored cache is present.

1. **Documents:** the source ids of a training set are disjoint from the document ids of every evaluation set (open
   `source_id`, gold v2 `doc_id`). Gutenberg books are `gutenberg-<number>` and Tatoeba sentences `tatoeba-<number>` in
   every file, so one canonical id exists per document.
2. **Sentences:** key = the lower-case alphanumerics of the sentence; keys of at least 25 characters are compared and no
   training key may equal an evaluation key. Shorter exact duplicates (`Thank you.`) legitimately recur and are only counted.
3. **Near duplicates** (reported, not gated): an evaluation sentence of at least 10 words whose word 6-grams are at least 80
   percent contained in the training 6-grams of a set.

**Result** (`python tools/data/check_leakage.py`; the committed `leakage-report.tsv` has every pair; UD English-EWT was
fetched and included in this run): **0 document overlaps and 0 exact sentence overlaps** for every pair of training and
evaluation set. Informational: 2, 4 and 7 near-duplicate sentences of open dev, open test and gold v2 against open train,
and 1 gold v2 sentence against silver; all are agency boilerplate or repeated phrases (`Please let us know if these
examples and explanations have helped you, Ali.`, standard NWS and USGS announcement sentences) and not shared documents.
Short exact duplicates against UD English-EWT (MASC email 33 dev, 55 test, 208 train; silver 1 dev, 3 train) are generic
short sentences.

**Removals** (training side only; evaluation sets are never changed): MASC 5 documents (4 email documents overlap UD
English-EWT train, 1 UD English-EWT dev), listed in `masc/excluded-documents.tsv`; silver 27 Tatoeba sentences whose id is
the id of an open dev or test sentence (found by the document rule; their short texts escape the 25-character key), listed
in `silver/excluded-documents.tsv`, plus the 1,840 documents the selection run of the earlier experiment removed against
the WordSense evaluation sets (informational). The open corpus is split by document hash, so open train has no overlap with
open dev and test by construction (confirmed).

**Limitations:** paraphrases and partial overlaps are not detected; Tatoeba near-duplicates can sit in different open splits
by design; the UD check applies only when the cache is present, so a run without it reports `UD English-EWT checked: no`.

## Downstream Notes

- The tokenizer of phase 2 must emit the corpus tokenization (clitics split: `do` + `n't`, `John` + `'s`).
- The lexicon is 4.5 MB of text; do not embed it raw in the package without compressing or front-coding it.
- Data statistics above describe the committed files; `MANIFEST.tsv` is the authority.
