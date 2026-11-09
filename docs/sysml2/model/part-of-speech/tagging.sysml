package PartOfSpeech {
    private import SoftwareItemMetadata::*;

    part def Tagging {
        doc /* Assigns a Universal POS tag to every token with a confidence and alternatives, using features, weights, the lexicon, caller constraints and glossary terms. */

        metadata info : SoftwareInfo {
            description = "Assigns a Universal POS tag to every token with a confidence and alternatives, using features, weights, the lexicon, caller constraints and glossary terms.";
            testFiles = ("test/DemaConsulting.PartOfSpeech.Tests/Tagging/TaggingTests.cs");
            designFiles = ("docs/design/part-of-speech/tagging.md");
            verificationFiles = ("docs/verification/part-of-speech/tagging.md");
            requirementFiles = ("docs/reqstream/part-of-speech/tagging.yaml");
        }

        part featureExtractor : FeatureExtractor;
        part tagDecoder : TagDecoder;
        part tokenConstraint : TokenConstraint;
        part glossary : Glossary;
        part tagger : Tagger;
    }
}
