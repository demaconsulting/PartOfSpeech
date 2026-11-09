package PartOfSpeech {
    private import SoftwareItemMetadata::*;

    part def TagDecoder {
        doc /* Tags a sentence greedily from left to right (in two passes for a model with the right preview group): scores the features of the groups that the weights name (including the clause state that it carries from the tags chosen so far), restricts the candidates and computes confidences and alternatives. */

        metadata info : SoftwareInfo {
            description = "Tags a sentence greedily from left to right (in two passes for a model with the right preview group): scores the features of the groups that the weights name (including the clause state that it carries from the tags chosen so far), restricts the candidates and computes confidences and alternatives.";
            sourceFiles = ("src/DemaConsulting.PartOfSpeech/Tagging/TagDecoder.cs", "src/DemaConsulting.PartOfSpeech/Tagging/DecoderWorkspace.cs", "src/DemaConsulting.PartOfSpeech/Tagging/ScoreSlice.cs");
            testFiles = ("test/DemaConsulting.PartOfSpeech.Tests/Tagging/TagDecoderTests.cs");
            designFiles = ("docs/design/part-of-speech/tagging/tag-decoder.md");
            verificationFiles = ("docs/verification/part-of-speech/tagging/tag-decoder.md");
            requirementFiles = ("docs/reqstream/part-of-speech/tagging/tag-decoder.yaml");
        }
    }
}
