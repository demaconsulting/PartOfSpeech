# Training Data and Held-Out Evaluation Methodology

A model can only be trusted as far as its evaluation can be trusted. This section describes, at a high level, what the
model is trained on, how its settings are chosen without looking at the data that is used to judge it, how the final
judgment is made, and how the central metric, the harmful error, is defined. The figures themselves are in the
evaluation record of the repository and are not repeated here.

## Training Data

The tagger is trained on sentences whose tokens carry one of the 17 Universal POS tags. The sources are chosen to cover
ordinary text and the kinds of text that the library is meant for:

- **A general open corpus** with a train split, and **MASC**, a corpus of written American English genres whose parse
  trees are mapped to the tag set.
- **A silver corpus** of openly licensed or public-domain sentences whose labels were written by a model and mapped to
  the tag set by documented rules. Tokens whose label probability is below a configured minimum are masked, which means
  that the trainer decodes them but does not learn from them.
- **Targeted sources** of sentences from US federal-government technical and instructional text, for shapes that the
  general data cover poorly: sentences that start with an imperative verb, noun compounds whose head can also be a
  verb, and several kinds of technical lines such as step instructions and defined terms. Two models labeled each
  sentence without seeing each other's labels, and the sentences were kept where their labels agreed.

Each source has an integer weight, the number of times its sentences enter every epoch. A source can be committed and
still have weight 0, which keeps it out of the mix; the configuration of the trainer says which sources are in the mix of
the embedded model. The lexicon is built separately, from dictionary tables and from the tags that appear in the
training corpora.

The labels of the silver, open, imperative, compound and technical sources were written by language models, not by
people, and no human reviewed them. The labels of MASC are different: they come from manually annotated and validated
Penn Treebank trees that are mapped to the tag set. The corpora went through a conventions linter and a correction
process in which two blind taggers adjudicate the disputed cases. This makes the labels consistent, but it also means
that the model learns the habits of its labelers, and that agreement with the model-written labels is not the same as
accuracy against a human reference. The only evaluation set with human labels is the
external UD English-EWT treebank, which follows other conventions; it is fetched on demand and never stored in the
repository.

## Selecting Settings on Development Data Only

Every setting of the model is chosen on development data and never on the held-out sets. Development data are
the dev splits of the open corpus and of a single-target gold set, a set of hard noun and verb shapes drawn from US
federal documents (in several tranches), and the development part of the treebank. The trainer reads these files only
through guards that refuse held-out files, and its tests check that.

The selection follows a discipline that is meant to prevent fooling oneself:

- **A rule before the experiment.** The run log states the selection rule, the metrics and the order of the steps before
  the first result exists: the primary measure, an accuracy floor, what counts as a tie, and the preference for the
  smaller model in a tie. A later experiment, such as a new training source or a new feature group, is
  pre-registered in the same way, with its arms, its seeds, its guards and the hashes of its scripts, before any arm is
  trained.
- **Ties are decided by a paired bootstrap by document.** Two options are tied when the 95% interval of the difference
  contains zero for both accuracy and the harmful rate. Resampling whole documents, not sentences, respects that the
  sentences of one document are not independent. Among tied options the smaller, simpler one wins.
- **A noise floor.** The baseline is trained with several seeds, and differences that are smaller than the spread
  between seeds are treated as ties. Later experiments train every arm with five or eight seeds.
- **A coordinate descent.** The early settings were chosen one after another: the restriction mode, the data mix, the
  number of epochs, the table size, the pruning, the quantization factor and the temperature. The interactions between
  the steps were not explored, and the run log says so.

Some development sets were built for the weak spots of the model and are therefore optimistic by construction: a
figure from them is never reported as a held-out result. The items of the harder tranches were selected by pre-registered
rules that use the output of another model family and never the output of this tagger, so that the set does not simply
reflect the mistakes that this tagger already makes.

![Data flow: training data train the model, development data select and freeze it, and held-out data evaluate it once
through a gate; leakage checks cover all three](technical-overview-evaluation.svg)

## The Held-Out Gate

The held-out sets are the open test split, the test split of the single-target gold set, a shape probe of short sentences
in seven noun and verb shapes, a small challenge set of simplified technical English, and the treebank test split. They
are opened by exactly one command of the trainer, which has no option for weights, data path or threshold.

The command enforces the discipline by construction. It refuses to read a held-out file unless a selection artifact is
committed that was made for the hash of the exact model that is about to be scored, and unless the development files of
that selection are the ones that are committed. It refuses to run again when the results already exist. The results are
committed as they were produced, from one run, and the report carries a notice about how many times these sets have
been looked at. This matters: a held-out set that has been used to compare several models is no longer unseen, and the
record discloses each further look instead of hiding it.

The protocol also fixes stop conditions in advance, for example a limit on the harmful errors of the shape probe at a
low confidence and a limit on the share of tokens whose correct tag is outside the candidates of the tagger. When a
condition fires, it is reported as fired and not adjusted afterwards. Some of the conditions that have fired are
convention differences and not model defects, and the record explains each one.

## Leakage Checks

A model that has seen the answers cannot be evaluated. The data tools therefore check, with counts only, that no
training document and no training sentence appears in any evaluation set. Two rules are applied: the document
identifiers of a training set must be disjoint from those of every evaluation set, and no sentence of a training set may
equal a sentence of an evaluation set once the case and punctuation are removed, whatever the length. Near duplicates,
sentences of at least 10 words of which at least 80% of the six-word sequences occur in a training set, are reported for
every set and are failures for the targeted training sources. When an overlap is found, the training data are
removed, never the evaluation data. The same checks compare the development tranches with the held-out sets, and the
targeted training sources with each other. The held-out sentences are read by the data tools only as hashed keys, and
the treebank is checked locally with counts only.

## The Harmful-Error Metric

Accuracy treats all mistakes alike, but a consumer such as a rule checker does not. Tagging a noun as an adjective
usually changes nothing for a rule that asks whether a word is a verb; tagging a noun as a verb, or the reverse, changes
the reading of the sentence. The harmful error is defined for this purpose:

- A **decision** is a token whose correct tag is a noun or a verb and for which both of those tags were candidates of the
  tagger.
- A **harmful error** is a decision in which the tagger chose the other of the two tags. At a threshold, only the answered
  decisions count.
- The **rate** is expressed per 1,000 decisions, or per 1,000 answered decisions at a threshold. Without a threshold, the
  reports use a reference point of 0.45, where nearly all decisions are answered.

The metric is deliberately narrow, and the evaluation record reports the usual measures as well: accuracy, the
log-likelihood, the expected calibration error and the precision and recall of every tag, with 95% intervals. Because
the intervals are bootstrap intervals by document, they describe the noise of the sample and not a change of genre.

## Reproducibility

The training is deterministic, so the evaluation can be repeated. The corpora, the configuration, the lexicon and the
code determine every weight, and the continuous integration of the repository retrains the model on Windows, Linux and
macOS and compares the uncompressed result with the same pinned hash. Anyone who checks out the repository can
reproduce the weights and, with the commands that are documented in the evaluation record, the results.
