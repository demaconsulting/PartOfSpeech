## Tokenization Verification

This document describes the subsystem-level verification strategy for the Tokenization subsystem (`Tokenizer` and
`SentenceSplitter` working together).

### Verification Approach

The subsystem is verified by integration tests in `Tokenization/TokenizationTests.cs` that call the public
`Tokenizer.Tokenize` and the internal `Tokenizer.TokenizeWords` with real data. Nothing is mocked: both units are real and
the only input is text. The decisive evidence is the open corpus (`data/open/`), the only committed corpus that has both
the original text of each sentence and its gold tokens, so that the convention of the training data can be measured:

- **Conformance**: for each of the 14,980 sentences of the open corpus the tokens from `TokenizeWords` are compared with
  the gold tokens; the share of sentences that match exactly must reach the gate of 99.8 percent. The measured value is
  99.907 percent (14,966 sentences); the 14 differences are documented in _Tokenizer Verification_.
- **Offsets**: for every open-corpus text, the tokens must be exactly the source slices at their offsets, and
  concatenating them must give the text without white space.
- **Sentence boundaries**: consecutive sentences of the same development and test documents are joined with a space,
  and the splitter must find the boundaries again. Recall is measured over the gold boundaries that follow terminal
  punctuation, precision over the boundaries the splitter reports; both must reach 98.5 percent. Measured: recall
  100.00 percent, precision 99.95 percent. Precision is a lower bound (a gold sentence can contain several real
  sentences), and the splitter lists were tuned while looking at these documents, so both values are optimistic;
  they prove the rules work on real text, not that they generalize to every genre.

### Test Environment

No setup beyond the standard test runner. The tests read `data/open/*.tsv` from the working copy through the helper
`DataFiles.cs`, which finds the repository root by walking up from the test binaries to the solution file; no network is
needed.

### Acceptance Criteria

All tests pass. The conformance test fails below 99.8 percent exact sentences, the offset test fails on the first
sentence whose tokens are not source slices or do not rejoin to the text, and the boundary test fails when recall or
precision is below 98.5 percent or when the sample has fewer than 1,000 boundaries.

### Test Scenarios

- **Text splits into sentences and tokens**: `Tokenization_Text_SplitsIntoSentencesAndTokens` tokenizes a text of two
  sentences ("Mr. Smith didn't go." and a quoted question) and asserts the sentence texts and the tokens of each.
- **Gold tokenization is reproduced**: `Tokenization_OpenCorpus_ReproducesGoldTokenization` asserts 14,980 sentences and
  at least 99.8 percent exact matches (the percentage is written to the test output on failure).
- **Offsets round trip on the whole corpus**: `Tokenization_OpenCorpus_OffsetsRoundTrip` asserts, for every sentence, that
  tokens index the text and rejoin to it, with no exceptions allowed.
- **Sentence boundaries are recovered**: `Tokenization_JoinedDocuments_RecoverSentenceBoundaries` asserts recall and
  precision of at least 98.5 percent on the joined documents.
