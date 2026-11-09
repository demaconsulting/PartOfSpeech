package PartOfSpeech {
    private import SoftwareItemMetadata::*;

    part def Tokenizer {
        doc /* Splits text into tokens by the rules of the training data, and creates sentences from tokens that a caller supplies. */

        metadata info : SoftwareInfo {
            description = "Splits text into tokens by the rules of the training data, and creates sentences from tokens that a caller supplies.";
            sourceFiles = ("src/DemaConsulting.PartOfSpeech/Tokenization/Tokenizer.cs", "src/DemaConsulting.PartOfSpeech/Tokenization/Token.cs", "src/DemaConsulting.PartOfSpeech/Tokenization/Sentence.cs");
            testFiles = ("test/DemaConsulting.PartOfSpeech.Tests/Tokenization/TokenizerTests.cs");
            designFiles = ("docs/design/part-of-speech/tokenization/tokenizer.md");
            verificationFiles = ("docs/verification/part-of-speech/tokenization/tokenizer.md");
            requirementFiles = ("docs/reqstream/part-of-speech/tokenization/tokenizer.yaml");
        }
    }
}
