<!-- markdownlint-disable MD013 MD060 -->

# Simplified Technical English Challenge Set

License: CC0-1.0

32 sentences (272 tokens) written by the WordSense project: 30 imperative maintenance instructions with component names
(`Theta Arm`, `Gantry Robot`, `Z-Axis Carriage`), part numbers (`P/N 4471-20`), units (`N·m`, `psi`), negated imperatives
and warnings, plus two `Mount ... home` sentences that exercise the verb and adverb readings of `mount` and `home`.
It is a smoke test for the style of text that Simplified Technical English consumers (such as Ste100Mark) feed to a
tagger. **Evaluation only**: report it separately and never merge it into a headline number; 32 sentences are not a
measurement of accuracy on technical text.

## Source and Provenance

- **Source:** `corpus/open/targeted/data/test.tsv` of the WordSense project (commit
  `6530de1928baf30f9a14446a54bfb5dd5f67b41c`, SHA-256 `3f551971491dfa494d2d2a65dd0370fd2a92825ad42a471c428ea9e9c8650d45`),
  projected to `form upos status` in the shared corpus format by `python tools/data/import_eval.py`. The sentence
  comments `# selection` and `# query` are kept.
- **Sentences:** written by the project; the project owner dedicates them to the public domain under CC0 1.0
  (document `wordsense-ste-probe`).
- **Tags:** the UPOS tags follow the open corpus guideline and were annotated independently by two annotators that agreed
  on every token (status `unanimous` means two annotators agreed). Component names are NOUN or ADJ by function, never
  PROPN; the tags are model-written and not human-reviewed.
