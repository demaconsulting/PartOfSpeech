package PartOfSpeech {
    private import SoftwareItemMetadata::*;

    part def Glossary {
        doc /* The immutable set of domain terms with the tags that their head words may take, the matcher that finds them in a sentence (longest term first, left to right) and the per-token hard or soft constraints that a match gives to the tagger. */

        metadata info : SoftwareInfo {
            description = "The immutable set of domain terms with the tags that their head words may take, the matcher that finds them in a sentence (longest term first, left to right) and the per-token hard or soft constraints that a match gives to the tagger.";
            sourceFiles = ("src/DemaConsulting.PartOfSpeech/Tagging/Glossary.cs", "src/DemaConsulting.PartOfSpeech/Tagging/GlossaryBuilder.cs", "src/DemaConsulting.PartOfSpeech/Tagging/GlossaryEntry.cs", "src/DemaConsulting.PartOfSpeech/Tagging/GlossaryOptions.cs", "src/DemaConsulting.PartOfSpeech/Tagging/GlossaryMatch.cs", "src/DemaConsulting.PartOfSpeech/Tagging/GlossaryIndex.cs", "src/DemaConsulting.PartOfSpeech/Tagging/GlossaryWorkspace.cs", "src/DemaConsulting.PartOfSpeech/Tagging/GlossaryInflector.cs", "src/DemaConsulting.PartOfSpeech/Tagging/GlossaryConstraints.cs");
            testFiles = ("test/DemaConsulting.PartOfSpeech.Tests/Tagging/GlossaryTests.cs", "test/DemaConsulting.PartOfSpeech.Tests/Tagging/GlossaryMatchingTests.cs", "test/DemaConsulting.PartOfSpeech.Tests/Tagging/GlossaryThroughputTests.cs");
            designFiles = ("docs/design/part-of-speech/tagging/glossary.md");
            verificationFiles = ("docs/verification/part-of-speech/tagging/glossary.md");
            requirementFiles = ("docs/reqstream/part-of-speech/tagging/glossary.yaml");
        }
    }
}
