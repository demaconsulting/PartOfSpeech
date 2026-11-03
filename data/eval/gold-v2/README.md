<!-- markdownlint-disable MD013 MD060 -->

# Gold v2

License: CC0-1.0

5,072 single-target gold tokens from 1,442 public-domain documents: for each sentence one target word with its gold
part of speech (and lemma, features, tense and sense, which this project does not use). A document-disjoint test of
tagger accuracy on real text. **Evaluation only**; the test split is reported, never tuned on.

## Source and Provenance

- **Source:** the second all-words gold set of the WordSense project (`corpus/allwords/v2`, commit
  `6530de1928baf30f9a14446a54bfb5dd5f67b41c`), files `dev.tsv`, `test.tsv` and `sources.tsv` copied unchanged by
  `python tools/data/import_eval.py`, with the SHA-256 of each input pinned in `tools/data/pins/wordsense-inputs.tsv`.
  The raw labeler answers (`labels.tsv`) and the sampling manifest are not imported.
- **Labels:** written by two language models, with a third adjudicating every disagreement. **No human reviewed a label**
  and no token was dropped. The project owner dedicates the labels and the selection to the public domain under CC0 1.0.
- **Sentences:** public domain in the United States only. They come from documents that appear nowhere else in the
  WordSense repository: VOA Learning English, USGS, National Park Service and NOAA National Weather Service pages (US
  federal works or explicit public-domain statements, quoted per document in `sources.tsv`) and 97 Project Gutenberg
  books (author dead by 1930, landing page "Public domain in the USA"). There is no FAA, NASA or NOS text, no Tatoeba, no
  CC BY or CC BY-SA text and no Universal Dependencies text (a scan of `sources.tsv` finds no such license row).

## Files

| File | Content |
| --- | --- |
| `dev.tsv` | 2,025 labeled tokens of the development split (586 documents) |
| `test.tsv` | 3,047 labeled tokens of the test split (856 documents); the two splits share no document |
| `sources.tsv` | the 1,442 documents: family, title, URL, author, year, license basis (the quoted statement), terms URL, retrieval date, hashes |

A row of `dev.tsv` and `test.tsv` has 16 tab-separated columns (named in the `# columns` comment line):
`id doc_id source terms_url sent_id target form strata pos lemma features tense sense status agreement sentence`.
`pos` is one of Noun, Verb, Adjective, Adverb, Pronoun, Preposition, Determiner, Conjunction, Auxiliary, Interjection
or Other; `target` is the zero-based index of the target token in `sentence`; `status` is `agreed` or `adjudicated`
and `agreement` is `full`, `analysis`, `pos` or `none` (use them to report on the cleaner subset).

## Evaluation

Tag the sentence, take the tag of the token at `target` and map it with the UPOS-to-class table in
[../../CONVENTIONS.md](../../CONVENTIONS.md). The WordSense class set has no particle class: targets that are `to` or
`not` are convention differences and must be reported separately. The sentence column holds the words separated by
single spaces, as a tagger receives them.
