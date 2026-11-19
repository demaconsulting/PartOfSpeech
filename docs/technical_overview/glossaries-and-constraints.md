# Glossaries and Constraints

A general model does not know the vocabulary of a particular domain. It reads `bleed valve` as a verb and a noun
because `bleed` is also a verb, and it may tag a capitalized defined term as a proper noun. The library lets a caller
state what it knows, either for one token with a constraint or for a whole list of terms with a glossary. This section
explains how these hints interact with decoding and with the confidences.

## Constraints

A constraint is attached to one token. It has two independent parts that can be combined:

- A **hard** part, a set of allowed tags. The token may take only these tags, intersected with the tags that the lexicon
  and the shape rules allow. When the intersection is empty, the constraint alone applies, because the caller is more
  authoritative than a dictionary.
- A **soft** part, an amount that is added to or subtracted from the score of a tag. The amounts are in the units of the
  real scores, so the softmax divides them by the temperature just as it divides the scores of the model.

The decoder applies the constraint where it applies the lexicon restriction: after the weights are summed and before the
best tag is chosen and the softmax is computed. Nothing else in the decoder changes, which is why the confidences
follow the constraint without any special rule:

- A hard constraint that leaves one candidate gives that token confidence exactly 1 and no alternatives. This is the
  result of a restriction and not a measurement. It says nothing about whether the constraint is right.
- A hard constraint that leaves several candidates gives the softmax over the remaining tags.
- A soft constraint changes the scores, so it shows in the confidence and can change the winner, but only when the
  model's own preference is weaker than the adjustment.

Because the decoder works from left to right, the tag that a constraint produces becomes the previous-tag feature of the
following tokens. A constraint therefore affects its neighbors on the right. It cannot affect the tokens before it:
they are decoded before the constraint is reached, with no information from it. This holds for the embedded model; a
model with the optional right preview looks ahead with a first pass that already contains the constraints, so tokens on
the left can change as well.

## Hard Mode and Soft Mode

The two parts of a constraint give a glossary its two modes. In hard mode an entry limits the head word of a term to the
tags of the entry. This is the right choice for a curated list: every correct entry helps, and nothing disagrees. Its
cost is that a wrong entry forces a wrong tag everywhere the term occurs, with a confidence of 1, and the model cannot
object.

In soft mode the entry adds a preference to each tag of the entry. The size of the preference is the soft strength times
the temperature of the model, so the strength is measured in the unit of the log-odds, the quantity inside the softmax.
A preference of strength `k` multiplies the odds of the preferred tag against each other tag by `exp(k)`. The default
strength of the library is currently 2, so the odds change by a factor of about 7.4. Consider two candidates, VERB with
a probability of 80% and NOUN with 20%, and an entry that prefers NOUN. The odds of NOUN are 1 to 4; multiplied by 7.4
they are about 1.85 to 1, so NOUN becomes the answer with a probability of about 65%. If the model had been 95% sure of
VERB, the odds of NOUN would be about 1 to 19, and after the factor about 0.39 to 1: VERB stays the answer with a
probability of 72%. For two candidates the break-even point is a confidence of about 88%. A soft entry overrules a
doubtful model and yields to a confident one, which is the point of soft mode for a list that is not curated or may
contain mistakes. The default strength was chosen on development data with simulated glossaries, taking the cost of
wrong entries into account.

## Why Boosts and Not a Trained Feature

The obvious alternative is to give the model a feature that says what the glossary claims and to train it. The
repository tested this on development data, with the decision thresholds fixed before the experiment, and rejected it in
favor of score boosts. The boost needs no retraining, works for any glossary that a caller invents, and leaves the
feature set and the weights file unchanged. The run log has the protocol and the measurements.

## Matching Terms

A glossary is a set of entries, and each entry is a term of one or more words with the tags that its head word takes.
Matching works on tokens, not on characters:

- Terms are tokenized with the same rules as running text, and the tokens are folded and lower-cased, so a term matches
  in any letter case by default. An entry can be made case sensitive, which is needed for an acronym such as `IT`.
- At each position the longest term wins, matches never overlap, and a term never matches across two sentences.
- An index over the tokens of all the terms makes the cost of matching depend on the length of the longest term, not on
  the number of entries.

The tags of the entry apply to the **head word** of the term, which is the last word for a noun compound such as
`bleed valve`. For a phrasal verb whose later words are particles, such as `shut down`, the head is the first word.
The other words of a noun term are limited so that they cannot be verbs or auxiliaries, and the particle of a two-word
phrasal verb is limited to adposition, adverb and particle. Every other word is left to the model.

Text has plurals and verb forms and a glossary lists base forms, so the regular forms of the head word match too, with
the tags that fit the form: a plural is a noun, a past form can be a verb or an adjective, and a present participle can
be a verb, an adjective or a noun. Irregular forms are listed by the caller as variants. Only the head word is
inflected.

## Interaction with the Model

A glossary is turned into constraints for each matched token, and then everything above applies. A few consequences are
worth stating.

- **The known-word flag does not change.** A glossary changes no lexicon fact, so a token that the lexicon does not
  know is still reported as unknown even when a glossary entry tags it with confidence 1.
- **The caller wins a conflict.** When a caller passes its own constraints as well as a glossary, the two are combined
  per token, and where their allowed sets have no tag in common, the caller's constraint is used.
- **Wrong entries are the main risk.** In hard mode, a wrong entry is tagged wrongly wherever it occurs and with full
  confidence. A glossary should be curated, or used in soft mode.
- **A glossary is better than a threshold for a known term.** A confident error on a term that the dictionary lists with
  the wrong reading is not removed by raising the confidence threshold, because the confidence is high. The glossary
  fixes the term wherever it occurs.
- **Glossary effects are not part of the held-out measurement.** The reported figures describe the tagger without a
  glossary, and the effect of glossaries was measured separately, on development data, with simulated entries.
