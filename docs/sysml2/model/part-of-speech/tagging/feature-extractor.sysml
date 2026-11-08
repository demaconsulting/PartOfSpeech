package PartOfSpeech {
    part def FeatureExtractor {
        doc /* The single definition of the hashed features of a token, shared by decoding and the trainer: 22 base templates plus four switchable feature groups (position, clause, right preview, inflection) that a model names in its weights file; the embedded model enables the clause-cue group. */

        comment sourceRef /* Source: src/DemaConsulting.PartOfSpeech/Tagging/FeatureExtractor.cs */
        comment testRef /* Test: test/DemaConsulting.PartOfSpeech.Tests/Tagging/FeatureExtractorTests.cs */
        comment designRef /* Design: docs/design/part-of-speech/tagging/feature-extractor.md */
        comment verificationRef /* Verification: docs/verification/part-of-speech/tagging/feature-extractor.md */
        comment reqRef /* Requirements: docs/reqstream/part-of-speech/tagging/feature-extractor.yaml */
    }
}
