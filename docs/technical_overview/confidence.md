# Confidence Calibration and Alternatives

Every tagged token carries a confidence and a ranked list of alternatives. This section shows how they are computed
from the scores, how the temperature that makes them meaningful is chosen, what the margin adds, and how the operating
thresholds are selected and should be read.

## From Scores to Probabilities

The scores of the perceptron rank the tags but have no meaning as numbers. To turn them into probabilities the decoder
applies a softmax with a temperature `T` to the real scores `s` of the candidate tags:

```text
p(tag) = exp(s(tag) / T) / sum over the candidates c of exp(s(c) / T)
```

The real score is the integer sum of the weights multiplied by the scale of the file, plus any adjustment from a soft
constraint. The softmax is computed relative to the best score, so the best candidate always contributes the term 1 and
the computation cannot overflow. The confidence of a token is the probability of the chosen tag, and the alternatives
are the other candidates in descending order of probability.

Two properties follow directly from the formula:

- The sum is over the candidates only, that is, over the tags that the lexicon, the shape rules and the constraints
  allow. A token with one candidate has confidence 1 and no alternatives, and a word that the lexicon does not know
  spreads its probability over up to 17 tags.
- The temperature never changes which tag is best, because it divides all scores by the same number. It only changes how
  fast the probability grows with the gap between scores.

A worked example with the temperature of the current embedded model, about 9.5, makes this concrete. Suppose that two
candidates, NOUN and VERB, have the real scores 14 and 11. The terms are 1 for NOUN and exp(-3/9.5), about 0.73, for
VERB, so the confidence of NOUN is 1 / 1.73, about 58%, and VERB has 42% as the alternative. To reach a confidence of
90% between two candidates the gap must be T times ln 9, about 21 score units, which is 167 integer units at the scale
of 1/8. With a temperature of 1 the same gap of 3 would give 95%. The temperature is therefore the exchange rate between
the units of the perceptron and the probabilities of the output, and the value that is stored in the weights file is what
makes the confidences comparable to the observed accuracy.

## Choosing the Temperature

The perceptron is trained to get the best tag right, not to give calibrated probabilities, and its scores come in
whatever scale the training produced. The library calibrates after the training with temperature scaling, a method with
one parameter that is known to work well for classifiers (see the third reference of this document). The trainer uses
the real decoder to score the open development split at each candidate temperature and chooses the temperature that
minimizes the negative log-likelihood, the mean of minus the logarithm of the probability that the model gave to the gold
tag among its candidates. A coarse grid in steps of a quarter octave from 0.125 to 16 finds the basin, and a fine grid
in steps of a thirty-second of an octave refines it. The result is rounded to three decimals and written to the header
of the weights file.

Minimizing the log-likelihood rewards probabilities that match the observed frequencies, and it punishes confident
mistakes heavily. The trainer also reports the expected calibration error, which groups the tokens into 15 equal-width
bins of confidence and compares the average confidence of each bin with its accuracy. The error is a diagnostic and not
the quantity that is minimized.

Temperature scaling has the following limits, which are worth knowing:

- One number serves every tag, genre and word. If the model is more overconfident on one kind of text than on
  another, no single temperature fits both. The run log records that the gold v2 development split is more overconfident
  than the open split that the temperature was fitted on.
- Calibration is measured on text like the development split. A different kind of text can shift it.
- The confidence is conditional on the candidate set and on the earlier tags. A confidence of 1 for a word with one
  candidate describes the restriction, not the model.

## Margin and Alternatives

The margin of a token is its confidence minus the probability of its best alternative. It measures how close the call
was. In the example above, NOUN has confidence 0.58 and VERB has 0.42, so the margin is 0.16: a small margin on a token
whose confidence is not very high is a sign that the tagger had a real second choice. The margin is exact when at least
one alternative is reported, and for a token with one candidate it is 1.

The alternatives show what the competing readings are, with their probabilities, so a consumer can look at them instead
of only discarding a token. The number of alternatives is a setting of the tagger; the confidence and the alternatives
together add up to 1 when all candidates are listed.

## Operating Thresholds

A consumer that acts on a tag needs a rule for when to trust it. The simplest rule is a threshold on the confidence: a
token is answered when its confidence is at least the threshold, and treated as unknown otherwise. The library
recommends two operating points, selected on development data by a rule that was written before the thresholds were
computed.

The mistake that the selection is about is the harmful error, a noun that is tagged as a verb or a verb that is tagged
as a noun. These are the errors that change the meaning of a sentence for a consumer such as a rule checker. The
decisions that are counted are the tokens whose correct tag is a noun or a verb and for which both were candidates.
Three quantities are used:

- The **coverage** at a threshold is the share of the decisions that are answered.
- The **harmful rate** at a threshold is the number of harmful errors among the answered decisions, per 1,000 answered.
- The **targets** are a limit on the harmful rate and a floor on the coverage.

The rule scans a fixed grid of thresholds from 0.45 up to 0.99 in ascending order and takes the first one that meets both
the limit and the floor on the pooled development decisions. The **strict** point has the harmful-rate limit of 5 in
1,000 and a coverage floor of 60%; the **balanced** point has the limit 10 in 1,000 and a floor of 85%. For the current
embedded model the selected thresholds are 0.90 for strict and 0.75 for balanced. Scanning in ascending order picks the
lowest threshold, and so the highest coverage, that still meets the target.

How to read a threshold:

- **A threshold is not an error rate.** A calibrated confidence of 0.90 says that the chosen tag is right with
  probability about 0.90 for tokens at that confidence. The tokens that pass the threshold are more confident than that
  on average, and the harmful rate counts only noun and verb swaps. The two numbers answer different questions.
- **A threshold trades coverage for reliability.** Raising it removes more of the doubtful decisions and answers fewer
  tokens. The tokens below it are not made right; they are only not acted upon.
- **The figures are measurements and have intervals.** The selection was made on development data, and the held-out
  evaluation then measured the same thresholds on other sets. The held-out rate can be above the target. The evaluation
  record has the numbers with their intervals, and they must be quoted from there.
- **The thresholds belong to the model.** Weights that you train or load need their own selection.
- **A confidence of 1 from a single candidate is not a measurement**, and neither is the confidence of a token that a hard
  glossary entry limits to one tag.

Technical text is harder. The evaluation record contains a development-only measurement on tokens of technical
documents, and a technical operating point derived from it. It is published as a measured fact with its caveats and not
as a recommended gate; a confident error on a term that the dictionary knows is better removed with a glossary than
with a higher threshold.
