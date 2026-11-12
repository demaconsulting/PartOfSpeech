package PartOfSpeech {
    private import SoftwareItemMetadata::*;

    part def BinaryFormat {
        doc /* The primitives that both file formats share: little-endian fields, varints, checksum, header validation, Deflate and population count. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.PartOfSpeech/ModelStorage/BinaryFormat.cs");
            testFiles = ("test/DemaConsulting.PartOfSpeech.Tests/ModelStorage/BinaryFormatTests.cs");
            designFiles = ("docs/design/part-of-speech/model-storage/binary-format.md");
            verificationFiles = ("docs/verification/part-of-speech/model-storage/binary-format.md");
            requirementFiles = ("docs/reqstream/part-of-speech/model-storage/binary-format.yaml");
        }
    }
}
