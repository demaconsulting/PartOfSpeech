# Averaged Perceptron

The scoring model of the tagger is an averaged perceptron. This section defines the score, explains the update rule
that trains the weights and the averaging that makes them reliable, and describes how the training is made exact and
reproducible with integer arithmetic.

## Scoring

For a token, the feature extractor produces a set of present features, each addressed by a bucket. The weight table
holds one integer weight for each bucket and tag. The score of a tag is the sum of the weights of the present
features for that tag:

```text
score(tag) = sum over the present features f of  weight[f][tag]
```

The tag with the highest score is the best guess of the model. The scores are not probabilities and have no fixed
scale; they only rank the tags. The conversion to a confidence is a separate step (see the Confidence Calibration and
Alternatives section).

The weights are stored as 16-bit integers with a scale factor in the file header. At run time the decoder adds the
integer weights, which is exact, and multiplies the sum by the scale to get the real score. In the embedded model the
scale is 1/8, so an integer score of 24 is a real score of 3.

## Training: the Perceptron Update

The perceptron learns from mistakes. Training reads sentences in which the tokens carry gold tags, and it processes the
tokens in order. For each token it computes the scores with the current weights, takes the best tag as its guess and
compares it with the gold tag. If the guess is right, nothing changes. If the guess is wrong, the weights of the present
features are corrected:

```text
weight[f][gold]  = weight[f][gold]  + 1
weight[f][guess] = weight[f][guess] - 1      for every present feature f
```

The training loop is shown in the figure below. Read the left part as the loop over tokens and the right part as the
two things that happen on a mistake.

![Training loop: features, scores and a guess for each token; a wrong guess raises the weights of the gold tag, lowers
those of the guessed tag and records the update time; at the end the weights are averaged, quantized and
pruned](technical-overview-training.svg)

A small example shows why this works. Suppose that the token `book` has three present features, and that earlier
training taught all three of them to favor the noun reading: each has the weight 1 for NOUN and 0 for VERB (the other
tags are left out of this example). The scores
are 3 for NOUN and 0 for VERB. The gold tag in this sentence is VERB, so the guess is wrong, and the update changes
each of the three features: NOUN goes from 1 to 0 and VERB goes from 0 to 1. The scores are now 0 for NOUN and 3 for
VERB. The same context would now be tagged correctly, and the update was exactly large enough to do it. Features that
are shared with other tokens (a common suffix, for example) are changed too, which is why the order of the sentences,
many passes over the data and averaging matter.

The perceptron is a good fit for this tagger for four reasons. It is simple, so the training code is short and can be
checked. It handles a very large number of sparse features, because a mistake touches only the features that were
present. It needs no probabilities during training, so the confidence can be calibrated afterwards without touching the
weights. And its updates are integers, which allows exact arithmetic. Its main weakness is that the raw weights depend
on the order of the training examples, which the averaging addresses.

## Averaging

After the last mistake, the weights reflect the last few sentences that the model saw. A feature that was pushed up by
an early example and pushed down by a late one ends at some value that depends on where the training stopped. The
standard remedy is to use the average of the weights over all steps of the training instead of the final weights.
The average changes slowly and is not dominated by the end of the training, and it behaves like an ensemble of all
the intermediate models. Averaging is the standard refinement of the perceptron for tagging (see the first reference of
this document), and it generalizes better than the plain perceptron because it is less sensitive to the order of the
examples.

Computing the average naively would require adding the whole weight table at every step, which is far too slow. The
trick is to keep two numbers per weight: the current weight `W` and a running sum `U` of the step numbers at which it
was updated. Let `t` be the number of the current step (every token of every epoch is one step, whether or not it
causes an update) and `T` the total number of steps. When an update adds `d` to a weight at step `t`, the trainer
also adds `t * d` to `U`. At the end, the average of the weight over all steps is:

```text
averaged weight = W - U / T
```

Two examples show that this is the mean of the values that the weight held. Suppose a weight is raised by 1 at step 2 of
a run of 10 steps. It is 0 for the first two steps (the update happens after the scores of step 2 are computed) and 1
for the other eight. Here `W` is 1 and `U` is 2, so the average is 1 - 2/10 = 0.8, which is 8 steps at 1 out of 10.
Suppose it is raised at step 2 and lowered again at step 9. It is 1 for steps 3 to 9, that is 7 steps. Here `W` is 0 and
`U` is 2 - 9 = -7, so the average is 0 + 7/10 = 0.7.

## Integer and Deterministic Training

The trainer uses integer arithmetic throughout. The current weights are integers, the sums `U` are 64-bit integers and
the step counter is an integer. A mistake adds or subtracts 1, so no floating-point value ever enters the weights. The
final average is quantized to the 16-bit integers of the file with exact integer arithmetic:

```text
stored weight = round( S * (W * T - U) / T )     clamped to the range -32767 to 32767
```

where `S` is the quantization factor, a power of two (8 in the current configuration), rounding is half away from zero,
and the file records the scale `1/S` so that the decoder can undo it. The same example weight of 0.8 becomes
round(8 * 0.8) = round(6.4) = 6, which is a real weight of 0.75. The sums are checked for overflow, so a run that is
too long for 64 bits fails with a message instead of silently writing wrong weights.

After the quantization the trainer prunes the weights of small magnitude: a weight is dropped when its absolute value,
whether the weight is positive or negative, is at most the pruning threshold, which in the current configuration is 8
units of 1/8, that is a real weight of 1.0 or less in magnitude. The example weight of 6 is therefore dropped. Pruning
makes the file much smaller, because most weights are small
and rows that end up empty are not stored. On the development data, a pruning threshold of 1.0 in real units was
statistically tied with no pruning, and the trainer configuration uses the largest threshold that was tied. Larger
thresholds lost accuracy. The same selection chose the quantization factor: a factor of 4 lost accuracy, and larger
factors than 8 added size without a gain.

Everything else in the training is deterministic as well. The order of the sentences in each epoch is a shuffle driven
by a small fixed random number generator that is part of the trainer, seeded from the configuration, so it does not
depend on the platform. The weight table is written in bucket order. The result is that the uncompressed weights file is
byte-for-byte the same on every operating system and framework for the same corpora, configuration and library code.
The configuration records the SHA-256 hash of that file, and the trainer can retrain and compare the result with the
committed weights to prove that they are current. The compressed bytes can differ between runtimes, which is why the
comparison uses the uncompressed image.

## Training Under the Conditions of Use

At run time the decoder never sees gold tags. The previous-tag features of a token come from the tags that the decoder
itself chose. If training used the gold tags as history, the model would learn on conditions it never meets, and its
first mistake at run time would put it in a situation that it was not trained for. The trainer therefore uses the
model's own guesses as history, exactly as the decoder does. On the development data this predicted history was better
than gold history.

The lexicon restriction (see the Greedy Decoding with Lexicon Restriction section) raises a related question: should the
training see it? The trainer has three modes. In the first, training ignores the restriction. In the second, the update
compares the gold tag with the best of all 17 tags, but the history that is passed to the next token is the best tag
that the lexicon allows, which is what the decoder will write. In the third, the guess is restricted for the update as
well. The second mode is used. It was as accurate as the first on the development data and smaller, and its history
matches the decoder. The third was clearly worse. The likely cause, which the run log records without having
investigated it further, is that an update that only competes against the allowed tags never meets the competitors that
the token-shape rules add at run time.

Two more details of the training data matter for the update. A source has an integer weight, which is the number of
times its sentences are repeated in every epoch, so a weight of 2 makes a source count double. A token without a gold
tag, for example a silver token whose label has too low a probability, is decoded and counts as a step of the averaging,
but it causes no update.
