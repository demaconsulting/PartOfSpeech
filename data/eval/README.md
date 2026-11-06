<!-- markdownlint-disable MD013 MD060 -->

# Evaluation Data

Evaluation sets that no training step reads. Each folder has its own README with source, license, provenance and how it
was produced.

| Folder | What | Size | License |
| --- | --- | --- | --- |
| `shapes/` | noun versus verb shape probe | 560 sentences | CC0-1.0 (labels model-written) |
| `gold-v2/` | single-target gold tokens from public-domain documents | 5,072 tokens | CC0-1.0 labels, public-domain sentences |
| `ste-challenge/` | simplified-technical-English challenge set | 32 sentences, 272 tokens | CC0-1.0 |
| `ud-ewt/` | UD English-EWT: **fetch script and documentation only, no data** | | CC BY-SA 4.0, never committed |

The open corpus dev and test splits (`../open/`) are also evaluation data. The leakage rules that keep every training
document and sentence out of these sets are in [../README.md](../README.md).

## Results and Reproduction

Only the development splits (the open corpus and gold v2) were used to select the model and the confidence thresholds.
The test splits, the shape probe, the challenge set and the UD English-EWT test file were evaluated once for each of two
models of one family (the model of the first corpus cleanup, then the embedded model that the follow-up selected: a
disclosed second look), the last time for the frozen embedded model. The [evaluation record](../../docs/evaluation/README.md) explains how to read the results, lists the limits of
the measurement (for example the convention differences for `to` and `not` in gold v2 and the weakness that the shape
probe shows) and gives the commands that reproduce them, including the local UD English-EWT run. The figures are in
[confidence-thresholds.md](../../docs/evaluation/confidence-thresholds.md) and
[test-results.md](../../docs/evaluation/test-results.md). The `ud-ewt/` folder holds the fetch recipe only; no treebank
file or text is ever committed.
