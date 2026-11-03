package PartOfSpeech {
    doc
    /*
     * PartOfSpeech is a .NET library for part-of-speech analysis of text, built on a template that demonstrates
     * DEMA Consulting best practices for structuring, documenting, and testing a DotNet library. The repository also
     * holds open data for the 17 Universal POS tags (training corpora, evaluation sets and a lexicon) and the
     * third-party notice; these data assets are files, not software items, and are not modeled here.
     */

    part def PartOfSpeechSystem {
        doc /* The Part Of Speech system as a whole, including its system-level data and licensing requirements. */

        comment testRef /* Test: test/DemaConsulting.PartOfSpeech.Tests/PartOfSpeechTests.cs, test/DemaConsulting.PartOfSpeech.Tests/PartOfSpeechDataTests.cs, test/DemaConsulting.PartOfSpeech.Tests/DataFiles.cs */
        comment designRef /* Design: docs/design/part-of-speech.md */
        comment verificationRef /* Verification: docs/verification/part-of-speech.md */
        comment reqRef /* Requirements: docs/reqstream/part-of-speech.yaml */

        part demo : Demo;
    }
}
