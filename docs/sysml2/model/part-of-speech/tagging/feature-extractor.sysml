package PartOfSpeech {
    private import SoftwareItemMetadata::*;

    part def FeatureExtractor {
        doc /* The single definition of the hashed features of a token, shared by decoding and the trainer: 22 base templates plus four switchable feature groups (position, clause, right preview, inflection) that a model names in its weights file; the embedded model enables the clause-cue group. */

        metadata info : SoftwareInfo {
            description = "The single definition of the hashed features of a token, shared by decoding and the trainer: 22 base templates plus four switchable feature groups (position, clause, right preview, inflection) that a model names in its weights file; the embedded model enables the clause-cue group.";
            sourceFiles = ("src/DemaConsulting.PartOfSpeech/Tagging/FeatureExtractor.cs");
            testFiles = ("test/DemaConsulting.PartOfSpeech.Tests/Tagging/FeatureExtractorTests.cs");
            designFiles = ("docs/design/part-of-speech/tagging/feature-extractor.md");
            verificationFiles = ("docs/verification/part-of-speech/tagging/feature-extractor.md");
            requirementFiles = ("docs/reqstream/part-of-speech/tagging/feature-extractor.yaml");
        }
    }
}
