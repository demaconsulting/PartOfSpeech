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
