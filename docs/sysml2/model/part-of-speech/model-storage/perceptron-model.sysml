package PartOfSpeech {
    private import SoftwareItemMetadata::*;

    part def PerceptronModel {
        doc /* Reads, validates and scores with the averaged-perceptron weights of the weights file (including the feature group mask of the header), writes them, and gives their deterministic uncompressed image. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.PartOfSpeech/ModelStorage/PerceptronModel.cs", "src/DemaConsulting.PartOfSpeech/ModelStorage/PerceptronModelWriter.cs");
            testFiles = ("test/DemaConsulting.PartOfSpeech.Tests/ModelStorage/PerceptronModelTests.cs");
            designFiles = ("docs/design/part-of-speech/model-storage/perceptron-model.md");
            verificationFiles = ("docs/verification/part-of-speech/model-storage/perceptron-model.md");
            requirementFiles = ("docs/reqstream/part-of-speech/model-storage/perceptron-model.yaml");
        }
    }
}
