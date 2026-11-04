## Tagging Verification

This document describes the subsystem-level verification strategy for the Tagging subsystem (`FeatureExtractor`,
`TagDecoder`, `TokenConstraint`, `Glossary` and `Tagger` working together).

### Verification Approach

The subsystem is verified by integration tests in `Tagging/TaggingTests.cs` that drive the public `Tagger` with tiny
models. A **tiny trained model** is produced by `TestSupport/TinyTrainer.cs`: about 40 short fixture sentences are
tagged by a deterministic averaged perceptron (the algorithm of the spike: left to right, unrestricted greedy decoding
with the previous predicted tags as history, +1 for the gold tag and -1 for the guess on a mistake, weights averaged
over all steps, a fixed-seed shuffle). It uses only `FeatureExtractor` and `PerceptronModelWriter`, which is also the
proof that the trainer can reuse the extractor and the writer unchanged (the real trainer does, and its parity tests are
in _ModelTrainer Verification_). A lexicon of the fixture
words is compiled by `LexiconWriter`. **Hand-set tiny models** (`TestSupport/TinyModels.cs`, class `WeightsBuilder`)
say "the lower-case word `a` adds 5 to DET" through `FeatureExtractor.ReferenceBucket`, so a test controls every score
without knowing a hash. Nothing is mocked; every unit is real.

The subsystem tests prove behavior that no single unit can: that a model trained with the shared extractor tags its own
training sentences (the fixtures are separable and trained for 15 epochs, so a single wrong tag shows a defect in the
features, the scoring or the decoding), that the lexicon restriction holds on nonsense input, that tag history changes
later tags, that constraints and glossary entries propagate through the history, that a glossary entry corrects a word
that the model alone reads as a verb and reports its match on the tokens and on the sentence, and that results are
deterministic.

### Test Environment

No setup beyond the standard test runner: the tiny models are built in memory, no files and no network. The tests
run in Release or Debug configuration alike.

### Acceptance Criteria

All tests pass on every target framework. A test fails when a training sentence is tagged wrongly, a chosen or
alternative tag is outside the allowed set, the same word gets the same tag in contexts that differ in the preceding
tags, a constraint does not change the following token through the history, a glossary entry does not decide the tag of
its head word or does not change the following token, a match is not reported on its tokens or on the sentence, or two
runs (or two taggers loaded from the same bytes) differ in any tag, confidence or alternative.

### Test Scenarios

- **End to end on the training sentences**: `Tagging_TinyTrainedModel_TagsTrainingSentences` tags every fixture sentence
  from its tokens (more than 250 tokens) and asserts that no token receives a tag other than gold.
- **Lexicon restriction on unseen input**: `Tagging_TinyTrainedModel_LexiconRestrictionHolds` tags every fixture sentence
  in reverse word order with 16 alternatives, and asserts that the chosen tag together with the alternatives equals
  exactly the set of tags that the lexicon and the shape rules allow for the token.
- **History changes later tags**: `Tagging_Context_PrecedingTags_ChangeTagOfAmbiguousWord` tags "run" after a
  determiner (NOUN) and after a pronoun and a modal (VERB) and asserts that both have alternatives.
- **Constraints propagate**: `Tagging_Constraints_FirstTokenOnly_ChangesNextTokenTag` uses hand-set weights under
  which the word after DET is a NOUN and after ADJ a VERB, tags free and with the first token restricted to ADJ, and
  asserts DET NOUN versus ADJ VERB and the `IsConstrained` flags.
- **Glossary entries propagate**: `Tagging_Glossary_HardEntry_ChangesHeadAndNextTokenThroughHistory` uses the same
  hand-set weights idea with a glossary entry that says that the word is ADJ, and asserts DET NOUN without the glossary
  and ADJ VERB with it, the head with confidence 1 and the `IsConstrained` flags.
- **Glossary results**: `Tagging_Glossary_Results_ReportMatchesOnTokensAndSentence` uses hand-set weights under which
  the model alone reads the first word of the noun term `run mat` as a verb, and asserts that with the glossary both words
  are nouns, that the sentence has one match with the first token, the token count, the head token, the text and the
  definition, that only the head token is flagged as head, and that exactly the two tokens of the span carry the match.
- **Determinism**: `Tagging_Tag_SameInput_ProducesSameOutput` tags a text with an unknown word three times (twice with
  one tagger, once with a second tagger loaded from the same bytes) and asserts identical tags, offsets, confidences
  (round-trip format) and alternatives.
