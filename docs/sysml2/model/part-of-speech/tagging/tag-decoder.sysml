package PartOfSpeech {
    part def TagDecoder {
        doc /* Tags a sentence greedily from left to right: scores the features, restricts the candidates and computes confidences and alternatives. */

        comment sourceRef /* Source: src/DemaConsulting.PartOfSpeech/Tagging/TagDecoder.cs, src/DemaConsulting.PartOfSpeech/Tagging/DecoderWorkspace.cs, src/DemaConsulting.PartOfSpeech/Tagging/ScoreSlice.cs */
        comment testRef /* Test: test/DemaConsulting.PartOfSpeech.Tests/Tagging/TagDecoderTests.cs */
        comment designRef /* Design: docs/design/part-of-speech/tagging/tag-decoder.md */
        comment verificationRef /* Verification: docs/verification/part-of-speech/tagging/tag-decoder.md */
        comment reqRef /* Requirements: docs/reqstream/part-of-speech/tagging/tag-decoder.yaml */
    }
}
