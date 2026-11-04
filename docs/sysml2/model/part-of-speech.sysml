package PartOfSpeech {
    doc
    /*
     * PartOfSpeech is a .NET library for part-of-speech analysis of English text: it splits text into sentences and
     * tokens and tags every token with one of the 17 Universal POS tags, with a calibrated confidence, ranked
     * alternatives and optional per-token constraints. The package embeds a compiled lexicon and trained weights; the
     * weights are produced by the build-time ModelTrainer unit. The repository also holds open data for the 17 Universal
     * POS tags (training corpora, evaluation sets and the lexicon source), the package license and the third-party
     * notice; these data assets are files, not software items, and are not modeled here.
     */

    part def PartOfSpeechSystem {
        doc /* The Part Of Speech system as a whole, including its system-level data, licensing and embedded model requirements. */

        comment testRef /* Test: test/DemaConsulting.PartOfSpeech.Tests/PartOfSpeechTests.cs, test/DemaConsulting.PartOfSpeech.Tests/PartOfSpeechDataTests.cs, test/DemaConsulting.PartOfSpeech.Tests/DataFiles.cs */
        comment designRef /* Design: docs/design/part-of-speech.md */
        comment verificationRef /* Verification: docs/verification/part-of-speech.md */
        comment reqRef /* Requirements: docs/reqstream/part-of-speech.yaml */

        part upos : Upos;
        part modelTrainer : ModelTrainer;
        part tokenization : Tokenization;
        part modelStorage : ModelStorage;
        part tagging : Tagging;
    }
}
