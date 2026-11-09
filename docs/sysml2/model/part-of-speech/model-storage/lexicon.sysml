package PartOfSpeech {
    private import SoftwareItemMetadata::*;

    part def Lexicon {
        doc /* Reads, searches and writes the compiled word lexicon and applies the token-shape rules that give the allowed tags of a token. */

        metadata info : SoftwareInfo {
            description = "Reads, searches and writes the compiled word lexicon and applies the token-shape rules that give the allowed tags of a token.";
            sourceFiles = ("src/DemaConsulting.PartOfSpeech/ModelStorage/Lexicon.cs", "src/DemaConsulting.PartOfSpeech/ModelStorage/LexiconWriter.cs");
            testFiles = ("test/DemaConsulting.PartOfSpeech.Tests/ModelStorage/LexiconTests.cs");
            designFiles = ("docs/design/part-of-speech/model-storage/lexicon.md");
            verificationFiles = ("docs/verification/part-of-speech/model-storage/lexicon.md");
            requirementFiles = ("docs/reqstream/part-of-speech/model-storage/lexicon.yaml");
        }
    }
}
