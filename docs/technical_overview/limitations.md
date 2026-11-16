# Limitations and Known Failure Modes

The techniques in the previous sections have costs. This section collects them in one place, so that a reader can judge
where the tagger can be trusted and where it needs a threshold, a glossary or a second opinion. It states properties of
the design and findings that the evaluation record reports; the figures are in that record.

## Limits of the Decoder

- **Greedy decisions cannot be revised.** A wrong tag early in a sentence becomes the previous-tag evidence of the next
  tokens, and the error can spread. The tagger has no mechanism that goes back.
- **The context is local.** The model sees the token, the two words on each side, the two previous tags and, with the
  clause cues, a few cues about the structure of the clause. It has no parse tree and no knowledge of long-distance
  agreement. A sentence in which the decisive word is far away is harder.
- **Sentences are independent.** Each sentence is tagged alone. Nothing that was said in the previous sentence helps.
- **The confidence is conditional.** The confidence of a token assumes that the earlier tags were right. It does not
  carry the uncertainty of the earlier decisions.

## Limits of the Lexicon and the Features

- **The lexicon is not a gold standard.** It records the tags that a word has been seen with, including some annotation
  noise, so an allowed tag can be wrong. A tag that the lexicon and the shape rules do not allow can never be chosen
  for a known word, even when it is right in that sentence. The evaluation record shows cases of this: conventions
  of another data set that place a word in a class that this tagger does not allow.
- **Unknown words are decided by spelling and context.** A word that the lexicon does not know can take any tag, and the
  evidence is its suffixes, its shape and its neighbors. Its confidence is spread across several tags.
- **Hash collisions add noise.** Two features can share a weight row. The effect was small in the development
  experiments, but it cannot be removed, and the weights cannot be inspected by feature name.
- **The features are fixed.** There is no morphological analysis beyond prefixes, suffixes and the optional stem lookup
  of the inflection group, and no semantic information. Pruning also discards small weights on purpose; this was tied
  with no pruning on the development data and is not free in general.

## Limits of the Confidence

- **A single temperature.** One number calibrates every tag, word and genre. Text that differs from the development split
  can be more or less overconfident than the measurement suggests.
- **Confident errors exist.** The model gives a confidence of 0.95 or more to some wrong answers, in particular for
  technical text. A threshold removes many errors, but a threshold of 0.90 does not make the remaining answers
  correct.
- **A confidence of 1 can mean no choice.** A word with one allowed tag, or a token that a hard constraint limits to one
  tag, has confidence 1 whatever the model thinks.
- **Thresholds are measured on one model.** The recommended thresholds describe the embedded model on the sets of the
  evaluation record. Other weights need their own selection.

## Known Weak Spots

The evaluation record names the shapes in which the model errs most. They are the head of a noun compound whose head is
spelled like a verb (`the pressure check`), a noun that starts a sentence, a noun in a measurement, and the verb that
starts an instruction. They are the shapes of short technical and imperative text, which the library is meant for, so
the weak spots matter in practice: use the strict threshold there, or give the tagger a glossary or constraints for the
words that matter. Capitalized terms in running text, headings and step titles are another source of confident errors,
because a capital letter points to a proper noun in ordinary text and means nothing special at the start of a heading.
The context features of the model address some of these shapes, and development data show gains, but they do not remove
the weakness.

## Limits of the Evidence

- **Most labels are written by models.** The labels of MASC come from a treebank that its distributor describes as
  manually validated, but the other training sources and the evaluation sets other than the treebank carry model-written
  labels. Agreement with those reference
  tags is not the same as accuracy against a human reference. Only one evaluation set has human labels, and it follows
  other conventions, so its results are lower after the conventions are mapped.
- **The held-out sets have been seen before.** Several models of the family have been scored on them. Nothing is
  selected on those results, but the sets are no longer unseen, and the record states how many looks there have been.
- **One run and one seed.** The held-out figures come from one training run. The intervals describe the noise of the
  sample, not a change of genre or a different seed.
- **Development evidence can be optimistic.** The development sets for hard shapes were designed around the weak spots,
  and the feature groups were designed against the same shapes. A gain there is evidence, not a held-out result.
- **The selection was a coordinate descent.** The early settings were chosen one at a time, and the interactions
  between them were not explored.
- **Glossary effects are measured only on simulated entries**, and not on held-out data.

## Limits of the Scope

- **Language and text type.** The tagger is trained for English as written in US sources, with the 17 Universal POS
  tags and the conventions of its data, which differ from the Universal Dependencies treebanks in four documented
  respects. It is not meant for other languages, for speech transcripts or for text with unusual tokenization.
- **Tokenization and sentence splitting are rules.** They follow the training convention and are tuned on one corpus.
  Unusual abbreviations or formatting can split sentences in the wrong place, and tokens from another convention are
  less familiar to the model.
- **Glossaries need care.** In hard mode a wrong entry is wrong everywhere and with full confidence. Terms are matched by
  tokens, so spellings that are tokenized differently (`bleed-valve` and `bleed valve`) need separate entries. Only the
  regular forms of the head word are inflected, and the head word of an unusual term may need to be set by hand.
- **The tagger tags; it does not parse or reduce words to base forms.** The word lookup reports the base words and
  forms that the lexicon lists for a spelling, but the tagger does not use them to disambiguate.
