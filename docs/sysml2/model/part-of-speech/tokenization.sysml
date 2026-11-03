package PartOfSpeech {
    part def Tokenization {
        doc /* Splits English text into sentences and tokens in the tokenization convention of the training data. */

        comment testRef /* Test: test/DemaConsulting.PartOfSpeech.Tests/Tokenization/TokenizationTests.cs */
        comment designRef /* Design: docs/design/part-of-speech/tokenization.md */
        comment verificationRef /* Verification: docs/verification/part-of-speech/tokenization.md */
        comment reqRef /* Requirements: docs/reqstream/part-of-speech/tokenization.yaml */

        part tokenizer : Tokenizer;
        part sentenceSplitter : SentenceSplitter;
    }
}
