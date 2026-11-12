package PartOfSpeech {
    private import SoftwareItemMetadata::*;

    part def Tokenization {
        doc /* Splits English text into sentences and tokens in the tokenization convention of the training data. */

        metadata info : SoftwareInfo {
            testFiles = ("test/DemaConsulting.PartOfSpeech.Tests/Tokenization/TokenizationTests.cs");
            designFiles = ("docs/design/part-of-speech/tokenization.md");
            verificationFiles = ("docs/verification/part-of-speech/tokenization.md");
            requirementFiles = ("docs/reqstream/part-of-speech/tokenization.yaml");
        }

        part tokenizer : Tokenizer;
        part sentenceSplitter : SentenceSplitter;
    }
}
