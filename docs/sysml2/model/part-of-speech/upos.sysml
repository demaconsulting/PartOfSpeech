package PartOfSpeech {
    private import SoftwareItemMetadata::*;

    part def Upos {
        doc /* The tag vocabulary: the 17 Universal POS tags, their text forms and an immutable set of tags. */

        metadata info : SoftwareInfo {
            description = "The tag vocabulary: the 17 Universal POS tags, their text forms and an immutable set of tags.";
            sourceFiles = ("src/DemaConsulting.PartOfSpeech/Upos.cs", "src/DemaConsulting.PartOfSpeech/UposExtensions.cs", "src/DemaConsulting.PartOfSpeech/UposSet.cs");
            testFiles = ("test/DemaConsulting.PartOfSpeech.Tests/UposTests.cs");
            designFiles = ("docs/design/part-of-speech/upos.md");
            verificationFiles = ("docs/verification/part-of-speech/upos.md");
            requirementFiles = ("docs/reqstream/part-of-speech/upos.yaml");
        }
    }
}
