package PartOfSpeech {
    private import SoftwareItemMetadata::*;

    part def EmbeddedModel {
        doc /* Gives lazy, thread-safe access to the lexicon and the trained weights that are embedded in the assembly. */

        metadata info : SoftwareInfo {
            description = "Gives lazy, thread-safe access to the lexicon and the trained weights that are embedded in the assembly.";
            sourceFiles = ("src/DemaConsulting.PartOfSpeech/ModelStorage/EmbeddedModel.cs");
            testFiles = ("test/DemaConsulting.PartOfSpeech.Tests/ModelStorage/EmbeddedModelTests.cs");
            designFiles = ("docs/design/part-of-speech/model-storage/embedded-model.md");
            verificationFiles = ("docs/verification/part-of-speech/model-storage/embedded-model.md");
            requirementFiles = ("docs/reqstream/part-of-speech/model-storage/embedded-model.yaml");
        }
    }
}
