package PartOfSpeech {
    doc
    /*
     * PartOfSpeech is a .NET library template demonstrating DEMA Consulting
     * best practices for structuring, documenting, and testing a DotNet library.
     */

    part def PartOfSpeechSystem {
        doc /* The Part Of Speech system as a whole. */

        comment testRef /* Test: test/DemaConsulting.PartOfSpeech.Tests/PartOfSpeechTests.cs */
        comment designRef /* Design: docs/design/part-of-speech.md */
        comment verificationRef /* Verification: docs/verification/part-of-speech.md */
        comment reqRef /* Requirements: docs/reqstream/part-of-speech.yaml */

        part demo : Demo;
    }
}
