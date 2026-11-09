package PartOfSpeech {
    private import SoftwareItemMetadata::*;

    part def SentenceSplitter {
        doc /* Groups the tokens of a text into sentences by terminal punctuation, abbreviations, capitalization and blank lines. */

        metadata info : SoftwareInfo {
            description = "Groups the tokens of a text into sentences by terminal punctuation, abbreviations, capitalization and blank lines.";
            sourceFiles = ("src/DemaConsulting.PartOfSpeech/Tokenization/SentenceSplitter.cs");
            testFiles = ("test/DemaConsulting.PartOfSpeech.Tests/Tokenization/SentenceSplitterTests.cs");
            designFiles = ("docs/design/part-of-speech/tokenization/sentence-splitter.md");
            verificationFiles = ("docs/verification/part-of-speech/tokenization/sentence-splitter.md");
            requirementFiles = ("docs/reqstream/part-of-speech/tokenization/sentence-splitter.yaml");
        }
    }
}
