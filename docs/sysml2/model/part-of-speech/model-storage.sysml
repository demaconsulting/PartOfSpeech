package PartOfSpeech {
    private import SoftwareItemMetadata::*;

    part def ModelStorage {
        doc /* Holds the binary formats of the weights and the lexicon, their readers and writers, and the lexicon and the trained weights that are embedded in the package. */

        metadata info : SoftwareInfo {
            description = "Holds the binary formats of the weights and the lexicon, their readers and writers, and the lexicon and the trained weights that are embedded in the package.";
            testFiles = ("test/DemaConsulting.PartOfSpeech.Tests/ModelStorage/ModelStorageTests.cs");
            designFiles = ("docs/design/part-of-speech/model-storage.md");
            verificationFiles = ("docs/verification/part-of-speech/model-storage.md");
            requirementFiles = ("docs/reqstream/part-of-speech/model-storage.yaml");
        }

        part binaryFormat : BinaryFormat;
        part lexicon : Lexicon;
        part perceptronModel : PerceptronModel;
        part embeddedModel : EmbeddedModel;
    }
}
