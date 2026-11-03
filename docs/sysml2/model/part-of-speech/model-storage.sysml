package PartOfSpeech {
    part def ModelStorage {
        doc /* Holds the binary formats of the weights and the lexicon, their readers and writers, and the lexicon that is embedded in the package. */

        comment testRef /* Test: test/DemaConsulting.PartOfSpeech.Tests/ModelStorage/ModelStorageTests.cs */
        comment designRef /* Design: docs/design/part-of-speech/model-storage.md */
        comment verificationRef /* Verification: docs/verification/part-of-speech/model-storage.md */
        comment reqRef /* Requirements: docs/reqstream/part-of-speech/model-storage.yaml */

        part binaryFormat : BinaryFormat;
        part lexicon : Lexicon;
        part perceptronModel : PerceptronModel;
        part embeddedModel : EmbeddedModel;
    }
}
