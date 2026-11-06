<!-- markdownlint-disable MD013 MD060 -->

# Open Corpus

License: CC0-1.0

A UPOS-only copy of the **open English POS corpus** of the
[WordSense project](https://github.com/demaconsulting/WordSense/tree/6530de1928baf30f9a14446a54bfb5dd5f67b41c/corpus/open):
14,980 sentences from 5,310 public-domain or CC0 documents, split by document into train, dev and test. It is the primary
training and evaluation corpus of this library.

## License and Provenance

- **Annotations and selection: CC0 1.0** (the project owner's decision; `LICENSE` is the CC0 1.0 legal code).
- **Sentences: public domain in the United States or CC0 per source.** The statement used for every document is in
  `sources.tsv` (column `license_basis`, with the quoted statement and the terms URL). The corpus as a whole is not CC0;
  no claim is made on the sentences.
- **Source:** WordSense commit `6530de1928baf30f9a14446a54bfb5dd5f67b41c`, folder `corpus/open/`, files
  `representative/data/{train,dev,test}.tsv`, `sources.tsv`, `LICENSE`. The SHA-256 of every input is pinned in
  `tools/data/pins/wordsense-inputs.tsv` and recorded in the header of each file.
- **How produced:** `python tools/data/import_open.py [--source-dir PATH_TO_WORDSENSE_CHECKOUT]` projects the columns
  `id form lemma upos collapsed feats consensus` to `form upos status` (the consensus column) in the shared corpus format
  (`../CONVENTIONS.md`) and copies `sources.tsv` and `LICENSE` unchanged. The re-run is bit-identical.
- **Who annotated:** the tags were written by language models (gpt-6.1-sol on every token), not by people, and no tag
  was edited by hand. For 10,449 of the 14,980 sentences a second annotator (gemma4:26b) was asked to flag disagreement.
  Nobody human reviewed the tags. About 95 to 96 percent of the tags agree with UD English-EWT after the convention
  mapping of `../CONVENTIONS.md`.
- **Independence from UD:** no UD sentence, tag or guideline text was used as annotation input; the sentence texts of
  UD English-EWT train and dev served only as a negative filter and the UD dev file only to measure accuracy
  (see the corpus README in the WordSense repository, section 2).

## Files

| File | Sentences | Tokens | Documents | Content |
| --- | ---: | ---: | ---: | --- |
| `train.tsv` | 11,990 | 209,224 | 4,291 | training split (hash buckets 0 to 79 of the document id) |
| `dev.tsv` | 1,489 | 26,232 | 502 | development split (buckets 80 to 89) |
| `test.tsv` | 1,501 | 26,185 | 517 | test split (buckets 90 to 99); report only, never tune on it |
| `sources.tsv` | | | 5,311 | one row per source document: family, title, URL, author, year, license basis, terms URL, hashes (the 5,310 documents of the splits plus the project-written STE challenge document) |
| `LICENSE` | | | | CC0 1.0 Universal legal code |

The split rule is `int(SHA-256(source_id)[:8], 16) % 100`, so a document is on one side only. Documents by source
family (train, dev, test): Tatoeba 3,035 / 357 / 358 sentences (one document each), Project Gutenberg 136 / 21 / 18 books,
VOA Learning English 306 / 34 / 39 articles, USGS 361 / 39 / 43 pages, National Park Service 288 / 36 / 39 pages,
NOAA National Weather Service 165 / 15 / 20 pages.

Token statuses in the training split: 126,880 `unanimous`, 74,216 `unchecked` (no second annotator), 8,128 `disputed`.
Dev: 14,846 / 10,445 / 941. Test: 15,450 / 9,747 / 988. Status flags: 291 `foreign` and 496 `mentioned` in the training split,
94 and 48 in dev, 95 and 85 in test. Tag counts of the training split: NOUN 40,235, VERB 26,771, PUNCT 26,309, DET 21,597, ADP 20,667,
ADJ 15,772, PRON 12,875, PROPN 10,437, ADV 9,719, AUX 7,195, CCONJ 6,895, PART 5,011, SCONJ 2,736, NUM 2,320, X 362,
INTJ 246, SYM 77. These are the counts after the staged corrections were applied (`../corrections/README.md`).

## Use

- Train on `train.tsv`; choose settings on `dev.tsv`; report `test.tsv` once.
- Mask or down-weight `disputed` tokens in training (the corpus guidance); the lexicon build ignores them.
- Conventions (possessives are DET, particles are ADV, `be` is VERB unless it helps another verb, ...): `../CONVENTIONS.md`.
- Limitations: model-written annotations without human review; 36 percent of the training tokens are `unchecked`;
  Tatoeba sentences are short and conversational; the text is US-centered and mostly news, reference and fiction.
