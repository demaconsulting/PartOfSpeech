<!-- markdownlint-disable MD013 MD060 -->

# Shape Probe

License: CC0-1.0

560 sentences in which a word that is both a noun and a verb has one of seven common sentence shapes. It measures how well
a tagger tells a noun use from a verb use of the same spelling where the sentence is short, technical or imperative
(`Make a quick repair.`, `Close the valve.`). **Evaluation only**: no training step reads it and no setting is chosen on it.

## Source and Provenance

- **Source:** the shape probe of the WordSense project (`corpus/shapes/probe.tsv`, commit
  `6530de1928baf30f9a14446a54bfb5dd5f67b41c`, SHA-256 `5d1ebe3a7fd64c20aeb1388c95c2fff2d97792f9761375bed8b52740e0d89105`),
  copied unchanged by `python tools/data/import_eval.py`.
- **How produced (by the WordSense project):** the sentences are project-authored: a language model (`gpt-5.6-luna`)
  wrote one sentence for a requested lemma, part of speech and shape. Two independent
  labelers (`gpt-6.1-sol` and `claude-sonnet-5.5`) labeled the part of speech of the braced word without seeing the
  intended one. A sentence was kept only when both gave the same part of speech with high confidence, and that is the gold
  of the row. The set was drawn from the kept sentences with a fixed seed: 80 for each shape, at most one for each lemma
  and shape.
- **License:** the sentences are project-authored content dedicated to the public domain under CC0 1.0 by the project
  owner. The labels are model-written and **not human-reviewed**; the owner's position on the rights in model outputs is
  stated in the repository README and is not legal advice.

## File

`probe.tsv` has a header row and the columns `id`, `shape`, `lemma`, `gold`, `target_index`, `sentence`. The sentence is
the words separated by single spaces (punctuation as separate words) with the target word in braces; `target_index` is
the zero-based index of the braced word; `gold` is `Noun` for a noun shape and `Verb` for a verb shape.

| Shape | Gold | The target word is |
| --- | --- | --- |
| `n-obj-light` | Noun | the object of a light verb (do, make, run, perform, take, give, have) after a determiner |
| `n-compound-head` | Noun | the head of a noun compound, after a modifier noun |
| `n-modifier` | Noun | a noun that modifies the noun after it; the lemma has no adjective sense in the lexicon |
| `n-subject-initial` | Noun | the first word of the sentence and its subject, with no article |
| `n-measure` | Noun | a noun in a measurement or quantity frame with a number |
| `v-imperative-initial` | Verb | the first word of an instruction, in the base form |
| `v-after-modal` | Verb | in the base form after a modal or the infinitive marker `to` |

## Evaluation

Report, for each shape, the share of rows where the tagger's tag for the braced word maps to the gold class (NOUN or
PROPN for Noun, VERB for Verb; `../../CONVENTIONS.md`). The harmful error is a gold noun tagged VERB or the reverse.
