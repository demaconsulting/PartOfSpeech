package PartOfSpeech {
    part def Glossary {
        doc /* The immutable set of domain terms with the tags that their head words may take, the matcher that finds them in a sentence (longest term first, left to right) and the per-token hard or soft constraints that a match gives to the tagger. */

        comment sourceRef /* Source: src/DemaConsulting.PartOfSpeech/Tagging/Glossary.cs, src/DemaConsulting.PartOfSpeech/Tagging/GlossaryBuilder.cs, src/DemaConsulting.PartOfSpeech/Tagging/GlossaryEntry.cs, src/DemaConsulting.PartOfSpeech/Tagging/GlossaryOptions.cs, src/DemaConsulting.PartOfSpeech/Tagging/GlossaryMatch.cs, src/DemaConsulting.PartOfSpeech/Tagging/GlossaryIndex.cs, src/DemaConsulting.PartOfSpeech/Tagging/GlossaryWorkspace.cs, src/DemaConsulting.PartOfSpeech/Tagging/GlossaryInflector.cs, src/DemaConsulting.PartOfSpeech/Tagging/GlossaryConstraints.cs */
        comment testRef /* Test: test/DemaConsulting.PartOfSpeech.Tests/Tagging/GlossaryTests.cs, test/DemaConsulting.PartOfSpeech.Tests/Tagging/GlossaryMatchingTests.cs, test/DemaConsulting.PartOfSpeech.Tests/Tagging/GlossaryThroughputTests.cs */
        comment designRef /* Design: docs/design/part-of-speech/tagging/glossary.md */
        comment verificationRef /* Verification: docs/verification/part-of-speech/tagging/glossary.md */
        comment reqRef /* Requirements: docs/reqstream/part-of-speech/tagging/glossary.yaml */
    }
}
