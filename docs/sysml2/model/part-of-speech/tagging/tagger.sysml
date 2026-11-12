package PartOfSpeech {
    private import SoftwareItemMetadata::*;

    part def Tagger {
        doc /* The public entry point: loads a model or uses the embedded one (Tagger.Default), tags text, sentences and token lists, and returns immutable results with options; a result token carries its confidence, ranked alternatives, a margin and a known-word flag. */

        metadata info : SoftwareInfo {
            description = "The public entry point: loads a model or uses the embedded one (Tagger.Default), tags text, sentences and token lists, and returns immutable results with options; a result token carries its confidence, ranked alternatives, a margin and a known-word flag.";
            sourceFiles = ("src/DemaConsulting.PartOfSpeech/Tagging/Tagger.cs", "src/DemaConsulting.PartOfSpeech/Tagging/TaggerOptions.cs", "src/DemaConsulting.PartOfSpeech/Tagging/TaggedSentence.cs", "src/DemaConsulting.PartOfSpeech/Tagging/TaggedToken.cs", "src/DemaConsulting.PartOfSpeech/Tagging/TagScore.cs");
            testFiles = ("test/DemaConsulting.PartOfSpeech.Tests/Tagging/TaggerTests.cs");
            designFiles = ("docs/design/part-of-speech/tagging/tagger.md");
            verificationFiles = ("docs/verification/part-of-speech/tagging/tagger.md");
            requirementFiles = ("docs/reqstream/part-of-speech/tagging/tagger.yaml");
        }
    }
}
