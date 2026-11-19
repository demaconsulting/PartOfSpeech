# Greedy Decoding with Lexicon Restriction

Decoding turns the scores of the model into one tag per token. This section describes how the decoder moves through a
sentence, how the lexicon and the shape rules restrict the candidates, what happens with words that the lexicon does not
know, and why the search is greedy and not exhaustive.

## Left to Right

The decoder handles one sentence at a time and one token at a time, from the first token to the last. For each token it
does the following:

1. Computes the features of the token. Most of them depend only on the sentence; a few depend on the previous tags,
   which the decoder has already decided.
2. Adds up the weights of the features to get one integer score for each of the 17 tags.
3. Determines which tags are allowed for this token (see below).
4. Chooses the allowed tag with the highest real score. A tie goes to the tag with the lowest tag number, so the result
   never depends on anything but the input.
5. Passes the chosen tag on as the previous tag of the next token.

The first token starts from two fixed start markers in place of previous tags, so no information crosses a sentence
boundary. The figure below shows the restriction and the decision for one token.

![One decoding step: the 17 tags are shown as chips, only the lexicon tags NOUN and VERB of the example word are
candidates, and the decision and the softmax use the candidates only; three boxes list the rules that decide which tags
are candidates](technical-overview-decoding.svg)

Because the decision for a token is final, the choice made for one token becomes evidence for the next. That is what
makes the tag history useful: after a determiner, a noun is likely, and the model can use that. It is also the main risk
of the approach, which the last part of this section discusses.

## The Lexicon Restriction

The lexicon is a table from a lower-cased, folded spelling to the set of tags that the spelling is known to carry. It is
built from dictionary tables and from the tags that are attested for the word in the training corpora, and it is
embedded in the package. The restriction uses it like this:

- **A known word may only take a tag that the lexicon lists for it**, plus the tags that its shape allows (below). The
  word `the` cannot become a verb, however the weights score the verb.
- **An unknown word may take any of the 17 tags.** The lexicon has no evidence either way, so the model alone decides,
  and the probability is spread over many tags. The known-word flag of a token reports which case applied.

The restriction is valuable for three reasons. It removes mistakes that no dictionary supports. It concentrates the
confidence on the tags that remain, so the confidences are more meaningful. And it makes the decoder easier to
understand, because a known word has only the few tags that the lexicon lists for it. Its cost is that the lexicon is
not perfect: it contains some annotation noise, so a tag that is in the allowed set is not necessarily right, and a word
that the lexicon misses a tag for cannot receive that tag. The trainer measures the share of gold tags that fall outside
the candidates, and the evaluation record reports it.

## Shape Rules

A dictionary lists words, not every token, so the allowed tags are extended by what the shape of the token permits. The
rules apply only to known words; an unknown word already has all tags.

| Shape of the token | Tags added to the lexicon tags |
| --- | --- |
| No letter and no digit (punctuation) | PUNCT, SYM, X |
| Contains a digit | NUM, SYM, X, NOUN, PROPN, ADJ |
| Starts with an upper-case letter | PROPN, X |

So a capitalized word that the lexicon knows only as a noun can also be a proper noun, and a known token that contains a
digit, such as `3rd`, can be a number or a noun whatever the lexicon lists for it. The rules are code, in one place, and
the word lookup of the
library uses the same code, so the tags that it reports for a word are the tags that the decoder would allow.

## Constraints

A caller or a glossary can add a constraint to a token. The decoder intersects the allowed tags with the allowed set of
the constraint, and when nothing is left, the constraint alone applies, because a curated list is more authoritative
than a dictionary. A soft constraint adds an amount to the score of a tag before the choice. The Glossaries and
Constraints section describes the interaction in detail.

## Why Greedy

A greedy decoder commits to the best tag at each token and never goes back. Exhaustive alternatives exist: Viterbi
search finds the best sequence of tags for models whose history is short, and beam search keeps several partial
sequences alive. The repository does not record an experiment that compares them with the greedy decoder, so the
reasons below are the properties of the design and not the result of a measurement.

- **Training and decoding agree.** The perceptron is trained with its own earlier guesses as history (see the Averaged
  Perceptron section), so the weights have been corrected for exactly the situations that a greedy decoder creates.
  The training is a plain loop over tokens with no search inside it.
- **The history is richer than two tags.** The clause cues keep a state that depends on the tags decided since the last
  clause boundary, and the optional right preview uses the result of a first pass. Exact dynamic programming over such a
  history needs a larger state, and the training would have to change as well.
- **Speed and simplicity.** The decoder does one feature extraction and one scoring per token, reuses its working memory
  between calls and is deterministic, which matters for a library that is embedded in other programs. The lexicon
  restriction already removes part of the choice for known words, so the search that greedy decoding gives up is
  smaller.
- **A local confidence.** A greedy decoder gives a clean per-token distribution: the softmax over the candidates, given
  the tags chosen so far.

The costs are real. An early mistake cannot be corrected by later evidence, and the later tokens are then scored with
a wrong history. The confidence of a token is conditional on the earlier decisions being right, and does not average
over the uncertainty of the earlier tokens as a forward-backward computation would. The model can look to the right
only through the words of the neighbors, or through the optional right preview, which approximates the missing look
ahead with a first pass at the price of decoding every token twice. The embedded model does not use the preview; it was
one of the groups that the selection rule did not choose.
