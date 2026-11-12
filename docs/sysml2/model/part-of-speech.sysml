package PartOfSpeech {
    private import SoftwareItemMetadata::*;

    doc
    /*
     * PartOfSpeech is a .NET library for part-of-speech analysis of English text: it splits text into sentences and
     * tokens and tags every token with one of the 17 Universal POS tags, with a calibrated confidence, ranked
     * alternatives and optional per-token constraints. The package embeds a compiled lexicon and trained weights; the
     * weights are produced by the build-time ModelTrainer unit, which also runs the one held-out evaluation of the frozen
     * model; its results, the selection of the two recommended confidence thresholds and their explanation are the
     * evaluation record in docs/evaluation, whose figures the packed API reference quotes. The repository also holds open
     * data for the 17 Universal POS tags (training corpora, evaluation sets and the lexicon source), the package license
     * and the third-party notice; these data assets and the evaluation record are files, not software items, and are not
     * modeled here.
     */

    part def PartOfSpeechSystem {
        doc /* The Part Of Speech system as a whole, including its system-level data, licensing, embedded model, evaluation record and confidence guidance requirements. */

        metadata info : SoftwareInfo {
            testFiles = ("test/DemaConsulting.PartOfSpeech.Tests/PartOfSpeechTests.cs", "test/DemaConsulting.PartOfSpeech.Tests/PartOfSpeechDataTests.cs", "test/DemaConsulting.PartOfSpeech.Tests/PartOfSpeechEvaluationTests.cs", "test/DemaConsulting.PartOfSpeech.Tests/DataFiles.cs");
            designFiles = ("docs/design/part-of-speech.md");
            verificationFiles = ("docs/verification/part-of-speech.md");
            requirementFiles = ("docs/reqstream/part-of-speech.yaml");
        }

        part upos : Upos;
        part modelTrainer : ModelTrainer;
        part tokenization : Tokenization;
        part modelStorage : ModelStorage;
        part tagging : Tagging;
    }
}
