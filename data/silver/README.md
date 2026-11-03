<!-- markdownlint-disable MD013 MD060 -->

# BERT Silver

License: CC0-1.0

Sentences from CC0 and public-domain text, labeled by a BERT model and mapped to UPOS by documented rules. Silver means
"labeled by a model, not by people": it widens the training text beyond the open corpus (conversational Tatoeba
sentences and 19th and early 20th century fiction and non-fiction) and carries a per-token `status` and the raw model
label, so each rule can be tuned or removed in phase 3.

## Content and License

| Family | Sentences | Tokens | Documents | License basis |
| --- | ---: | ---: | ---: | --- |
| Tatoeba | 4,973 | 54,862 | 4,973 (one per sentence) | CC0 1.0: every sentence is in the official Tatoeba CC0 export |
| Project Gutenberg | 8,998 | 187,837 | 94 books | public domain in the United States; only the body text is used |
| **Total** | **13,971** | **242,699** | **5,067** | |

Status of the tokens: 232,307 `silver` (a UPOS tag derived from the model label) and 10,392 `masked` (no training
signal; the tag is `_`). Tokens are counted after the contraction split of the corpus tokenization.

- **Sentences:** CC0 (Tatoeba) or public domain in the United States (Gutenberg) per `sources.tsv`. The Gutenberg
  selection rule: English, a single named author born 1790 or later and dead by 1930, no translator, a landing page that
  states "Public domain in the USA", no year after 1930 in the front matter, header, footer and every paragraph that
  mentions Gutenberg, an e-book or a transcriber removed, and a book that no other part of the WordSense repository uses.
  Only the book text is used, with no Project Gutenberg license text or trademark. Public-domain status is a US
  statement only.
- **Labels and selection:** CC0 1.0 (project owner). The labels are model output; the owner's position on the rights in
  model outputs is stated in the repository README and is not legal advice.
- **No** FAA, NASA or NOS text, no CC BY or CC BY-SA text, no UD text. The spike that selected the text also drew FAA
  maintenance handbooks (US federal work); they are **not committed** because the project owner excluded FAA text from
  the open corpus and from gold v2 and has not approved it here. `import_silver.py export-spike --include-faa` writes
  them to the git-ignored `silver-faa.tsv` for local experiments only.

## Files

| File | Content |
| --- | --- |
| `silver.tsv` | the sentences in the shared corpus format: `form`, `upos`, `status`, `source_tag`; `# text` is the tokens joined by single spaces |
| `sources.tsv` | one row per document: id, family, title, author, URL, license basis, sentences |
| `excluded-documents.tsv` | documents left out because of overlap with evaluation sets or the open training split (ids only) |
| `dropped-sentences.tsv` | the 2 short sentences (key shorter than 25 characters) left out because the sentence equals an evaluation sentence (ids only) |

## How It Was Produced

1. **Text selection (one-off, done in the spike):** Tatoeba sentences from the CC0 export (seed 20261102) and 94
   Gutenberg books of up to 130 sentences each, filtered to 4 to 40 tokens, a capitalized start, a sentence-final mark,
   at most 20 percent digits, no URLs or markup, and no abbreviation periods inside the sentence. Duplicates and
   sentences that match the evaluation data of the WordSense repository were removed (1,840 documents; listed as
   `spike-selection` in `excluded-documents.tsv`, informational).
2. **Model labels (one-off, not reproducible here):** every token was labeled by the `BertBaseCased` model of the WordSense
   project (BERT base cased, Apache-2.0, int8 ONNX export, model file SHA-256
   `dcc10a647f1520c00e07fd2ee2b3ad383db94657bc97cba3e1915d27e98a962d`, with a frozen part-of-speech head fused with the
   Basic tagger; WordSense commit `6530de1928baf30f9a14446a54bfb5dd5f67b41c`). The model assigns one of the 13 WordSense
   classes and a probability. The labeling stage needs WordSense and its model, so it cannot be rerun in this repository;
   the committed `source_tag` column (class letter and probability) makes everything after it reproducible. The model
   labels were read from the committed per-token output of the earlier spike; BERT was not rerun.
3. **Contraction split and UPOS derivation:** `python tools/data/import_silver.py export-spike --spike-dir SPIKE_DIR`
   (one-off) re-splits the contractions with the corpus tokenization (`can't` becomes `ca` and `n't`) and applies the
   table in [../CONVENTIONS.md](../CONVENTIONS.md); `python tools/data/import_silver.py derive` recomputes `upos` and
   `status` from `source_tag` and the lexicon (deterministic, bit-identical); `verify` checks the file against the rules.
   `verify-sources` (needs network, optional) checks the Tatoeba ids against the CC0 export and the Gutenberg landing pages.
4. **Leakage:** no silver sentence equals a sentence of an evaluation set (open dev and test, gold v2, the shape probe, the
   STE challenge set, UD English-EWT), whatever its length, and no silver document id is an evaluation document id. A
   document that shares a sentence of 25 or more key characters or a document id with an evaluation set was removed: 27
   Tatoeba sentences have the id of an open dev or test sentence. A shorter equal sentence is dropped alone (the sentences of
   a book are independent): 2 Gutenberg sentences equal to a UD English-EWT sentence, 11 tokens
   (`dropped-sentences.tsv`). No silver sentence equals an open training sentence. See `excluded-documents.tsv` and the
   Leakage section of [../README.md](../README.md).

## Caveats

- Silver is **not gold**. Systematic model errors become training signal; the status and `source_tag` columns exist so
  phase 3 can mask or down-weight classes and re-measure on the open dev split before keeping silver in the mix.
- Tokens of capitalized words at the start of a sentence whose lower-case form is also a common noun (`Tom`, `Will`) are
  masked, so names are under-represented. The helper-versus-main-verb distinction of `be` and `have` cannot be decided
  per token, so those stems and the clitics `'s`, `'m`, `'re` are masked.
- The class set of the model has no PROPN, NUM, SYM, X or CCONJ/SCONJ split; those tags come from the rules.
- Tatoeba sentences are short and conversational; Gutenberg text is old (archaic words and capitalization).
