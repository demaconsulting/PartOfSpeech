package PartOfSpeech {
    part def Tagging {
        doc /* Assigns a Universal POS tag to every token with a confidence and alternatives, using features, weights, the lexicon and caller constraints. */

        comment testRef /* Test: test/DemaConsulting.PartOfSpeech.Tests/Tagging/TaggingTests.cs */
        comment designRef /* Design: docs/design/part-of-speech/tagging.md */
        comment verificationRef /* Verification: docs/verification/part-of-speech/tagging.md */
        comment reqRef /* Requirements: docs/reqstream/part-of-speech/tagging.yaml */

        part featureExtractor : FeatureExtractor;
        part tagDecoder : TagDecoder;
        part tokenConstraint : TokenConstraint;
        part tagger : Tagger;
    }
}
