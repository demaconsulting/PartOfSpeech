package PartOfSpeech {
    private import SoftwareItemMetadata::*;

    part def TokenConstraint {
        doc /* The immutable value with which a caller restricts the tags of one token or adds a preference to the score of a tag. */

        metadata info : SoftwareInfo {
            description = "The immutable value with which a caller restricts the tags of one token or adds a preference to the score of a tag.";
            sourceFiles = ("src/DemaConsulting.PartOfSpeech/Tagging/TokenConstraint.cs");
            testFiles = ("test/DemaConsulting.PartOfSpeech.Tests/Tagging/TokenConstraintTests.cs");
            designFiles = ("docs/design/part-of-speech/tagging/token-constraint.md");
            verificationFiles = ("docs/verification/part-of-speech/tagging/token-constraint.md");
            requirementFiles = ("docs/reqstream/part-of-speech/tagging/token-constraint.yaml");
        }
    }
}
