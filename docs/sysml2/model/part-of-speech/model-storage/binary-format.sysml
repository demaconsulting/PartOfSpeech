package PartOfSpeech {
    part def BinaryFormat {
        doc /* The primitives that both file formats share: little-endian fields, varints, checksum, header validation, Deflate and population count. */

        comment sourceRef /* Source: src/DemaConsulting.PartOfSpeech/ModelStorage/BinaryFormat.cs */
        comment testRef /* Test: test/DemaConsulting.PartOfSpeech.Tests/ModelStorage/BinaryFormatTests.cs */
        comment designRef /* Design: docs/design/part-of-speech/model-storage/binary-format.md */
        comment verificationRef /* Verification: docs/verification/part-of-speech/model-storage/binary-format.md */
        comment reqRef /* Requirements: docs/reqstream/part-of-speech/model-storage/binary-format.yaml */
    }
}
