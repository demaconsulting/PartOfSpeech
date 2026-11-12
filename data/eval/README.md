<!-- markdownlint-disable MD013 MD060 -->

# Evaluation Data

Evaluation sets that no training step reads. Each folder has its own README with source, license, provenance and how it
was produced.

| Folder | What | Size | License |
| --- | --- | --- | --- |
| `shapes/` | noun versus verb shape probe | 560 sentences | CC0-1.0 (labels model-written) |
| `shapes-dev/` | hard-shape **development** set: real US federal-government sentences, five shapes, controls; standard tranche `dev.tsv` and hard tranche `hard.tsv` (four shapes, contested cases, same federal documents and families), three single-shape tranches from new documents: `compound-head.tsv`, `headings.tsv` (heading lines, exact tags) and `named-state.tsv`, two follow-up tranches from further new documents: `compound-head-2.tsv` and `headings-2.tsv`, and four technical tranches from further new documents: `deverbal-heads.tsv` (class scoring), `defined-terms.tsv`, `step-lines.tsv` and `clause-verbs.tsv` (exact tags) | 700 + 460 + 120 + 115 + 112 + 255 + 129 + 450 + 285 + 236 + 446 sentences | CC0-1.0 labels (model-written), US federal-work sentences |
| `gold-v2/` | single-target gold tokens from public-domain documents | 5,072 tokens | CC0-1.0 labels, public-domain sentences |
| `ste-challenge/` | simplified-technical-English challenge set | 32 sentences, 272 tokens | CC0-1.0 |
| `ud-ewt/` | UD English-EWT: **fetch script and documentation only, no data** | | CC BY-SA 4.0, never committed |

The open corpus dev and test splits (`../open/`) are also evaluation data. The leakage rules that keep every training
document and sentence out of these sets are in [../README.md](../README.md).

**Development versus held-out.** `shapes-dev/` is the one set here that is for selection: variants of the tagger are compared
on it, so a figure from it is never a held-out result. The shape probe (`shapes/`) and the other sets stay evaluation only.
Nothing is trained on any of them.

## Results and Reproduction

Only the development splits (the open corpus and gold v2) and the development files of the hard-shape set and of the
treebank were used to select the model, its feature group and the confidence thresholds. The test splits, the shape
probe, the challenge set and the UD English-EWT test file were evaluated once for each frozen model of one family;
the last time for the embedded model, a disclosed look at sets that earlier models had been scored on. The
[evaluation record](../../docs/evaluation/README.md) explains how to read the results, lists the limits of
the measurement (for example the convention differences for `to` and `not` in gold v2 and the weakness that the shape
probe shows) and gives the commands that reproduce them, including the local UD English-EWT run. The figures are in
[confidence-thresholds.md](../../docs/evaluation/confidence-thresholds.md) and
[test-results.md](../../docs/evaluation/test-results.md). The `ud-ewt/` folder holds the fetch recipe only; no treebank
file or text is ever committed.
