# Introduction

This document explains how the Part Of Speech library decides which tag belongs to each word of an English sentence.
It is a theory-of-operation document: it describes the ideas and the mathematics behind the tagger, why each technique
was chosen, and what each choice costs. It is written for technical users and reviewers who want to understand the
behavior of the tagger well enough to judge where it can be trusted.

The figure below shows the path that one sentence takes through the library. The numbered steps are the subject of the
sections that follow: tokenization, features, scoring with an averaged perceptron, restriction of the candidate tags and
the decision with its confidence.

![Pipeline: text is split into tokens, each token gets hashed features, the weights give a score per tag, the lexicon
and constraints restrict the candidates, and a softmax gives the tag with its confidence and
alternatives](technical-overview-pipeline.svg)

## Purpose

The purpose of this document is to make the behavior of the tagger understandable and predictable. The library tags
every token with one of the 17 Universal POS tags, with a confidence and ranked alternatives. A consumer such as a
rule checker acts on those numbers, so it needs to know what a score is, what a confidence means, why the tagger
sometimes returns a confident wrong answer, and how a glossary changes the result.

This document is informational. It is not a requirement, a design specification or a compliance record. The structure
of the code is in the Part Of Speech Design document, the API and its usage are in the Part Of Speech User Guide, and
the measured results are in the evaluation record of the repository. This document does not repeat them. Where a number
is needed, it is either an example that is marked as such or a statement about the current embedded model that the
evaluation record and the trainer configuration keep up to date.

## Scope

This document covers:

- How text becomes tokens and sentences, and why the conventions must match the training data
- How a token is described to the model with hashed features and switchable feature groups
- How the averaged perceptron scores tags, how it is trained and why it is averaged
- How the decoder chooses tags from left to right with the help of the lexicon
- How scores become a calibrated confidence, a margin, ranked alternatives and operating thresholds
- How the training data are used and how the model is selected and evaluated without looking at held-out data
- How glossaries and constraints interact with decoding and confidences
- The limitations and known failure modes

This document does not cover the public API, the binary file formats, the build and release process, or the licenses of
the data. It describes the embedded model and the default configuration; a weights file that you train or load yourself
follows the same theory but has its own numbers.

## References

- [REF-1] Michael Collins, *Discriminative Training Methods for Hidden Markov Models: Theory and Experiments with
  Perceptron Algorithms*, Proceedings of the Conference on Empirical Methods in Natural Language Processing, 2002
  (the averaged perceptron for tagging)
- [REF-2] Weinberger et al., *Feature Hashing for Large Scale Multitask Learning*, Proceedings of the International
  Conference on Machine Learning, 2009 (the hashing trick)
- [REF-3] Guo et al., *On Calibration of Modern Neural Networks*, Proceedings of the International Conference on Machine
  Learning, 2017 (temperature scaling and expected calibration error)
- [REF-4] Universal Dependencies, *Universal POS Tags*, <https://universaldependencies.org/u/pos/> (the 17 tags)
- [REF-5] Part Of Speech Design, DEMA Consulting (code structure and algorithms of each software item)
- [REF-6] Part Of Speech User Guide, DEMA Consulting (usage, confidences, thresholds, constraints and glossaries)
- [REF-7] Evaluation of the Embedded Model, the evaluation record in the repository (selection of thresholds and the
  held-out results)
