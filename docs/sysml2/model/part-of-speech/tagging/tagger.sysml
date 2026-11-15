package PartOfSpeech {
    private import SoftwareItemMetadata::*;

    part def Tagger {
        doc /* The public entry point: loads a model or uses the embedded one (Tagger.Default), tags text, sentences and token lists, and returns immutable results with options; a result token carries its confidence, ranked alternatives, a margin and a known-word flag. It also looks up one word on its own (GetPossibleTags, IsFormOf): the tags the lexicon lists for it or the tags the tagger allows for it, the match kind (spelling, compound head or not recognized) and the analyses (base word, tag and features) of its forms. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.PartOfSpeech/Tagging/Tagger.cs", "src/DemaConsulting.PartOfSpeech/Tagging/TaggerOptions.cs", "src/DemaConsulting.PartOfSpeech/Tagging/TaggedSentence.cs", "src/DemaConsulting.PartOfSpeech/Tagging/TaggedToken.cs", "src/DemaConsulting.PartOfSpeech/Tagging/TagScore.cs", "src/DemaConsulting.PartOfSpeech/Tagging/PossibleTagsMode.cs", "src/DemaConsulting.PartOfSpeech/Tagging/WordMatch.cs", "src/DemaConsulting.PartOfSpeech/Tagging/WordAnalysis.cs", "src/DemaConsulting.PartOfSpeech/Tagging/WordTags.cs");
            testFiles = ("test/DemaConsulting.PartOfSpeech.Tests/Tagging/TaggerTests.cs");
            designFiles = ("docs/design/part-of-speech/tagging/tagger.md");
            verificationFiles = ("docs/verification/part-of-speech/tagging/tagger.md");
            requirementFiles = ("docs/reqstream/part-of-speech/tagging/tagger.yaml");
        }
    }
}
