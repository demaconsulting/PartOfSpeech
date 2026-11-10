<!-- markdownlint-disable MD013 MD060 -->

# Data

Training data, evaluation data and the lexicon of `DemaConsulting.PartOfSpeech`: a part-of-speech tagger for .NET with the
17 Universal POS tags. Everything here is open data with its license, provenance and production recipe recorded. The scripts
that produce and check it are in [`tools/data/`](../tools/data/README.md); the tag set, the annotation conventions, the file
formats and every mapping table are in [CONVENTIONS.md](CONVENTIONS.md) (normative). At run time the library uses only the
lexicon and the trained weights, which are embedded in the package in compact binary forms: the lexicon is generated from
`lexicon/lexicon.tsv` (see [lexicon/README.md](lexicon/README.md), "Embedded Form"), and the weights
(`src/DemaConsulting.PartOfSpeech/Resources/weights.bin`, 521,695 bytes compressed, 1,157,499 bytes uncompressed) are
trained by the [Trainer tool](../tools/DemaConsulting.PartOfSpeech.Trainer/README.md) from the open train, MASC, silver and
imperative corpora with settings that are chosen on development data only: open dev, gold v2 dev, shapes-dev (standard, hard and the three single-shape tranches) and UD-EWT dev (the run log is
[RUN-LOG.md](../tools/DemaConsulting.PartOfSpeech.Trainer/RUN-LOG.md)). The corpora and the evaluation sets are not
shipped. How the model scores on the evaluation sets, the confidence thresholds that were chosen on the development
sets, and how to reproduce the evaluation (including the local UD English-EWT run) are in the
[evaluation record](../docs/evaluation/README.md).

## Contents

| Folder | What | Sentences | Tokens | Size | License |
| --- | --- | ---: | ---: | ---: | --- |
| [open/](open/README.md) | open corpus, train/dev/test, UPOS | 14,980 | 261,641 | 10.3 MB | CC0-1.0 annotations; PD/CC0 sentences |
| [masc/](masc/README.md) | MASC written genres, Penn trees mapped to UPOS | 22,829 | 386,445 | 11.6 MB | CC-BY-3.0-US |
| [silver/](silver/README.md) | BERT silver: Tatoeba and Gutenberg text, model labels mapped to UPOS | 13,971 | 242,699 | 8.5 MB | CC0-1.0 labels; CC0/PD sentences |
| [imperative/](imperative/README.md) | imperative-initial sentences from US federal instructional text, two-labeler agreement (training; weight 1 in `training.json`) | 900 | 11,946 | 0.5 MB | CC0-1.0 labels; US federal-work sentences |
| [compound/](compound/README.md) | sentences with a noun compound whose head can also be a verb, from US federal text, two-labeler agreement (training; weight 0 in `training.json`) | 742 | 14,692 | 0.6 MB | CC0-1.0 labels; US federal-work sentences |
| [headings/](headings/README.md) | Title Case heading, caption and step-title lines from US federal text, two-labeler agreement (training; weight 0 in `training.json`) | 375 | 1,668 | 0.2 MB | CC0-1.0 labels; US federal-work lines |
| [eval/shapes/](eval/shapes/README.md) | noun/verb shape probe (evaluation) | 560 | | 0.06 MB | CC0-1.0 |
| [eval/shapes-dev/](eval/shapes-dev/README.md) | hard-shape development set from US federal-government text (development only): standard tranche `dev.tsv` (700 sentences, 12,279 tokens), hard tranche `hard.tsv` (460 sentences, 7,936 tokens) and three single-shape tranches `compound-head.tsv` (120 sentences, 2,148 tokens), `headings.tsv` (115 heading lines, 507 tokens) and `named-state.tsv` (112 sentences, 1,970 tokens) | 1,507 | 24,840 | 1.2 MB | CC0-1.0 labels; US federal-work sentences |
| [eval/gold-v2/](eval/gold-v2/README.md) | single-target gold tokens (evaluation) | 5,072 | | 2.4 MB | CC0-1.0 labels; PD sentences |
| [eval/ste-challenge/](eval/ste-challenge/README.md) | simplified-technical-English challenge set (evaluation) | 32 | 272 | 0.01 MB | CC0-1.0 |
| [eval/ud-ewt/](eval/ud-ewt/README.md) | UD English-EWT: fetch documentation only, **no data** | | | | CC BY-SA 4.0, never committed |
| [lexicon/](lexicon/README.md) | spelling to UPOS tags lexicon, closed-class list, coverage report | | | 4.5 MB | CC-BY-4.0 and CC-BY-3.0-US (attribution), others |
| [corrections/](corrections/README.md) | label corrections for open, MASC and silver (mechanical rows, adjudicated rows, candidates, exceptions); **applied** to the corpora | | | 2.7 MB | CC0-1.0 labels; MASC token forms and excerpts CC-BY-3.0-US |

The training corpora (open train, MASC, silver) hold 209,224 + 386,445 + 242,699 tokens (silver: 232,307 labeled, 10,392
masked); the fourth training source, `imperative/`, holds 11,946 tokens and is configured with weight 1, so it is part of the
mix that trained the shipped weights. The fifth and sixth, `compound/` (14,692 tokens) and `headings/` (1,668 tokens), are configured
with weight 0: they are committed training data that are not part of the shipped mix. Total size of `data/`: 43.7 MB; the largest file is `masc/masc.tsv` (11.6 MB). Attribution for MASC and the Open English
WordNet lexicon sources is in `masc/ATTRIBUTION.md` and in `THIRD-PARTY-NOTICES.md` at the repository root.

## Rules of This Folder

- **Open licenses only.** CC0, public domain, CC BY (with attribution) and project licenses. No share-alike and no
  non-commercial data. `tools/data/check_licenses.py` scans the source tables and the folder tree; it also refuses
  `*.conllu` files and any path that names SemCor, Brown or UD English-EWT (except the fetch-only `eval/ud-ewt/README.md`).
- **Model-written labels are labeled as such.** The open corpus, the shape probe, the shapes development set, gold v2, the
  silver data, the imperative source, the compound source and the headings source have tags that language models wrote; no human reviewed them. Each README says so and gives the owner's CC0
  dedication of labels and selection (not a claim on the underlying sentences). Nothing here is legal advice.
- **Every file is reproducible.** Inputs are pinned by SHA-256 (WordSense commit
  `6530de1928baf30f9a14446a54bfb5dd5f67b41c`, the MASC archive, UD files); outputs are sorted, UTF-8 without BOM, LF, NFC.
  `MANIFEST.tsv` lists the size, SHA-256 and record counts of every data file (Markdown files excluded).
- **US English; evaluation data is never trained on.** The shapes development set is the one evaluation file that may be used
  to select settings (development only, through its own guard in the trainer); it is never trained on and never a held-out figure.
- **Not used:** SemCor and the Brown corpus (restricted or fair-use-only terms), FAA handbook text (US federal work, but the
  project owner excluded it from the open corpus and gold v2; it is available only behind an off-by-default flag and
  is never committed), UD English-EWT (CC BY-SA 4.0, evaluation only and fetched, not committed).

## Regenerating

From the repository root, with Python 3.10 or later (standard library only):

```text
python tools/data/import_open.py   --source-dir <WordSense checkout at the pinned commit>   # open/
python tools/data/import_eval.py   --source-dir <WordSense checkout at the pinned commit>   # eval/ (not eval/shapes-dev/: see its README; built by tools/data/shapes_dev.py)
python tools/data/fetch_ud_ewt.py fetch                                                     # cache for the leakage filter and the local UD evaluation (docs/evaluation/README.md)
python tools/data/import_masc.py   [--archive penn-treebank.tgz]                            # masc/
python tools/data/build_lexicon.py [--wordsense-dir <checkout>]                             # lexicon/lexicon.tsv (the silver derivation reads it; rebuilt after the corrections)
python tools/data/import_silver.py derive                                                   # silver/silver.tsv (the derivation; the corrections come after it)
python tools/data/make_corrections.py                                                       # corrections/{mechanical,candidates,exceptions}.tsv; regenerates only in the pending state (--check is state-aware)
python tools/data/corrections.py validate                                                   # the correction tables (adjudicated.tsv included) against the corpora (pending or applied)
python tools/data/corrections.py apply --write                                              # the corrections onto open, MASC and silver (a second apply is refused)
python tools/data/imperative_data.py --work <work-folder> build                             # imperative/ (needs the work folder of the labeling: see its README; not part of the corrections)
python tools/data/compound_heading_data.py --work <work-folder> build --source compound|headings   # compound/ and headings/ (the work folder of the labeling: see their READMEs for the options; not part of the corrections)
python tools/data/build_lexicon.py [--wordsense-dir <checkout>]                             # lexicon/lexicon.tsv again, from the corrected corpora
python tools/data/check_lexicon.py                                                          # lexicon/coverage.tsv, COVERAGE.md
python tools/data/check_leakage.py --write-report                                           # leakage-report.tsv, training-leakage-report.tsv
python tools/data/check_conventions.py                                                      # linter, report only (--tier fix --enforce: fail on any fix-tier violation, as lint.ps1 does)
python tools/data/make_manifest.py                                                          # MANIFEST.tsv
```

`--source-dir` and `--wordsense-dir` are optional: without them the pinned files are downloaded into the git-ignored
`tools/data/cache/`. The silver labeling itself (`import_silver.py export-spike`) is a documented one-off that needs the
earlier experiment's model output (`silver/README.md`). The importers write the uncorrected files (the pending state);
the corrections are applied after them, and every artifact that depends on the labels (the lexicon, then the weights) is
built from the corrected files.

## Leakage Check

No training document and no training sentence may appear in an evaluation set. Training sets: open train, MASC, silver, imperative, compound, headings.
Evaluation sets: open dev and test, gold v2 (dev and test), the shape probe, the shapes development set, the STE challenge
set, and UD English-EWT train, dev and test when the git-ignored cache is present.

1. **Documents:** the source ids of a training set are disjoint from the document ids of every evaluation set (open
   `source_id`, gold v2 `doc_id`). Gutenberg books are `gutenberg-<number>` and Tatoeba sentences `tatoeba-<number>` in
   every file, so one canonical id exists per document.
2. **Sentences:** key = the lower-case alphanumerics of the sentence; every key is compared, whatever its length, and no
   training key may equal an evaluation key. There is no exemption for short sentences: `Thank you.` and `What should
   I do ?` count like any other sentence. The remedy depends on the length of the key. A match of 25 or more
   characters shows that two corpora share source text, so the whole training document is excluded. A shorter match is a
   sentence that recurs in unrelated texts, so only that sentence is dropped from the training data (the sentences of a
   treebank file or of a book are independent; a Tatoeba document is a single sentence); the dropped sentence ids
   are listed in `dropped-sentences.tsv` of the training folder. The evaluation sets are never changed.
3. **Near duplicates** (gated for the imperative, compound and headings sources, reported for the other training sets): an evaluation sentence of at
   least 10 words whose word 6-grams are at least 80 percent contained in the training 6-grams of a set. Any near duplicate
   of the imperative, compound or headings source fails the check (`NEAR_GATED_TRAINING` in `check_leakage.py`); the older sources share agency
   boilerplate with the evaluation sets, so their near duplicates are only counted.
4. **Shapes development set against every other evaluation set:** the same document and sentence rules apply, and no
   sentence may share a word 5-gram with the shape probe (the probe's short sentences escape the near-duplicate rule). The
   counts are in `eval/shapes-dev/leakage-report.tsv`; no evaluation text is printed or stored. The builder of the set applies
   the same exclusion while it builds, and sees the held-out sets only through `leakage.held_out_overlap_keys()`, an opaque set of
   hashes of sentence keys and probe 5-grams (the probe's `sentence` column only, never its labels, shapes, lemmas or targets),
   used to exclude overlaps and never to select, weight or frame a candidate. The hard tranche (`eval/shapes-dev/hard.tsv`) is
   an evaluation set of its own in this check (label `shapes-dev-hard`, counts in `eval/shapes-dev/hard-leakage-report.tsv`) and
   is also checked against the standard tranche: the two tranches are one family and may share a source document (reported, not
   gated), but no sentence of one equals a sentence of the other, and no same-document near duplicate is allowed. The three newer
   tranches (`compound-head.tsv`, `headings.tsv`, `named-state.tsv`) are evaluation sets of their own too (labels `shapes-dev-compound-head`,
   `shapes-dev-headings`, `shapes-dev-named-state`, counts in `eval/shapes-dev/<tranche>-leakage-report.tsv`) and are checked against the standard
   and the hard tranche with no shared document allowed; among themselves a shared document is reported, not gated. The selection of
   the hard tranche and of `compound-head` used numeric output of the WordSense Basic classifier (same owner, run unmodified); nothing of WordSense text,
   labels or data is copied.

**Result** (`python tools/data/check_leakage.py`; the committed `leakage-report.tsv` has every pair; UD English-EWT was
fetched and included in this run): **0 document overlaps and 0 exact sentence overlaps** for every pair of training and
evaluation set, short sentences included, and none between the shapes development set and the other evaluation sets (the imperative, compound and headings sources included, with 0 near duplicates too), and none between `compound` or `headings` and any other training set (the two share documents by design, one pool, and no sentence; `training-leakage-report.tsv`). Informational: 2, 4 and 7 near-duplicate sentences of open dev, open test and
gold v2 against open train, and 1 gold v2 sentence against silver; all are agency boilerplate or repeated phrases
(`Please let us know if these examples and explanations have helped you, Ali.`, standard NWS and USGS announcement
sentences) and not shared documents.

**Removals** (training side only; evaluation sets are never changed): MASC 5 documents (4 email documents overlap UD
English-EWT train, 1 UD English-EWT dev), listed in `masc/excluded-documents.tsv`, and 104 short sentences (2 to 19 key
characters, such as `Thank you .`; 36 equal to a UD English-EWT dev sentence, 36 test, 31 train, and 1 gold v2 sentence,
`What should I do ?`), listed in `masc/dropped-sentences.tsv` (392 tokens); silver 27 Tatoeba sentences whose id is
the id of an open dev or test sentence (found by the document rule), listed in `silver/excluded-documents.tsv`, 2 short
Gutenberg sentences equal to UD English-EWT dev and train sentences (11 tokens), listed in `silver/dropped-sentences.tsv`,
plus the 1,840 documents the selection run of the earlier
experiment removed against the WordSense evaluation sets (informational); imperative no document and 5 candidate sentences before labeling (1 equal to a set
sentence and 4 near duplicates of shapes development sentences, counted in `imperative/dropped-sentences.tsv`); compound no document and 5 candidate sentences before labeling (3 equal to and 2 near duplicates of
shapes development sentences, `compound/dropped-sentences.tsv`) and headings no document and 118 candidate lines before labeling (equal to shapes development sentences,
`headings/dropped-sentences.tsv`). The open corpus is split by document hash, so
open train has no overlap with open dev and test by construction (confirmed; short sentences included).

**Limitations:** paraphrases and partial overlaps are not detected; Tatoeba near-duplicates can sit in different open splits
by design; the UD check applies only when the cache is present, so a run without it reports `UD English-EWT checked: no`.

## Downstream Notes

- **Corrections.** [corrections/](corrections/README.md) holds the label corrections that follow
  [CONVENTIONS.md](CONVENTIONS.md) as small tables (one row per token: address, old and new tag and status, provenance).
  They are **applied**: `open/`, `masc/` and `silver/` carry the corrected labels (8,407 effective rows; 878 adjudicated
  `prenominal-ing` rows are withdrawn and kept as provenance only, see
  [corrections/README.md](corrections/README.md#withdrawn-rows)), and the lexicon and
  the weights are built from them. The tables stay as the provenance of every change; a second application is refused and
  the `--check` of the generator reverses the rows in a temporary copy to prove that the tables are still the generator's
  output. `mechanical.tsv`, `candidates.tsv` and `exceptions.tsv` are
  generated (`make_corrections.py`); `adjudicated.tsv` holds the decisions of two blind language-model taggers on the
  judgment cases (`adjudicate.py`; the method is in [corrections/README.md](corrections/README.md#adjudication), and no human
  reviewed the rows), and an adjudicated row replaces the mechanical row of the same token. All tables are run
  through the tests and the lint run, and are valid against the corpora both before and after the application. The fix tier
  of the conventions linter finds no violation in the corrected files (`lint.ps1` enforces that); the check tier is report only.
- **Status flags.** A token's status cell can carry flags after plus signs (`unanimous+mentioned`, `silver+foreign+mentioned`;
  the flags are `foreign` and `mentioned`, once each, in that order). They record facts about the word and never change
  the tag; the status in front of the first plus sign drives masking, `disputed` handling and the lexicon. The open, MASC and
  silver files carry flags after the corrections (counts in the README of each folder); the other data files carry none. The
  format is specified in [CONVENTIONS.md](CONVENTIONS.md), "Corpus
  Block Format", and the data tools and the trainer's corpus reader accept and keep them.
- The tokenizer of the library emits the corpus tokenization (clitics split: `do` + `n't`, `John` + `'s`); its rules are
  in the "Tokenizer Rules" section of [CONVENTIONS.md](CONVENTIONS.md) and the test suite checks them against the open
  corpus.
- The lexicon is 4.5 MB of text; the package embeds a front-coded, Deflate-compressed binary form of about 0.64 MB
  instead (`src/DemaConsulting.PartOfSpeech/Resources/lexicon.bin`, built by `tools/LexiconCompiler`).
- Data statistics above describe the committed files; `MANIFEST.tsv` is the authority.
