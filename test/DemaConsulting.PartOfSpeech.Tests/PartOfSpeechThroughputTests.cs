namespace DemaConsulting.PartOfSpeech.Tests;

/// <summary>
///     System-level throughput test of the library. It is in its own class, in the non-parallel throughput collection, so
///     that it does not run at the same time as the other test classes of the test host.
/// </summary>
[Collection(ThroughputCollection.Name)]
public class PartOfSpeechThroughputTests
{
    /// <summary>
    ///     Proves that tagging pre-tokenized text on one thread runs at more than 50,000 tokens per second with a model of the size that a real model has.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Performance_SingleThread_ExceedsFiftyThousandTokensPerSecond()
    {
        // Arrange: the synthetic model (150,000 rows, 2^21 buckets), the embedded lexicon and the development sentences as token lists
        var training = DataFiles.ReadCorpus(DataFiles.Data("open/train.tsv")).Take(4000).Select(s => s.Tokens.Select(t => t[0]).ToArray());
        var weights = SyntheticModel.Create(training);
        TestContext.Current.SendDiagnosticMessage($"Synthetic weights file: {weights.Length:N0} bytes (Deflate) for {SyntheticModel.RowCount:N0} rows in 2^{SyntheticModel.HashBits} buckets.");
        using var stream = new MemoryStream(weights);
        var tagger = Tagger.Load(stream);
        var sentences = DataFiles.ReadCorpus(DataFiles.Data("open/dev.tsv")).Select(s => (IReadOnlyList<string>)s.Tokens.Select(t => t[0]).ToList()).ToList();
        var tokenCount = sentences.Sum(s => s.Count);

        // Act: warm up, then time windows over all sentences until one exceeds the gate (or the time budget is used up)
        var rate = ThroughputMeter.BestRate(
            () =>
            {
                foreach (var sentence in sentences)
                {
                    tagger.Tag(sentence);
                }
            },
            tokenCount,
            ThroughputMeter.Gate,
            out var windows);

        // Assert: the gate is 50,000 tokens per second (several hundred thousand are expected) unless the library is instrumented for coverage
        TestContext.Current.SendDiagnosticMessage($"Tagger throughput (pre-tokenized, one thread): best {rate:N0} tokens/s over {tokenCount} tokens, {windows} timed window(s), instrumented: {(ThroughputMeter.IsInstrumented ? "yes" : "no")}, gate {ThroughputMeter.Gate:N0}.");
        Assert.True(rate > ThroughputMeter.Gate, ThroughputMeter.Describe(rate, windows));
    }

    /// <summary>
    ///     Proves that tagging pre-tokenized text with a glossary of 10,000 entries on one thread still runs at more than 50,000 tokens per second, and reports how much slower it is than tagging without one.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Performance_GlossaryTagging_ExceedsFiftyThousandTokensPerSecond()
    {
        // Arrange: the embedded model, a glossary of word pairs from the training text and the development sentences as token lists
        var training = DataFiles.ReadCorpus(DataFiles.Data("open/train.tsv")).Take(2000).Select(s => s.Tokens.Select(t => t[0]).ToArray()).ToList();
        var terms = training
            .SelectMany(tokens => tokens.Zip(tokens.Skip(1), (a, b) => (a, b)))
            .Where(pair => pair.a.All(char.IsLetter) && pair.b.All(char.IsLetter))
            .Select(pair => pair.a + " " + pair.b)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10000)
            .ToList();
        var glossary = Glossary.Create(terms.Select(term => new GlossaryEntry(term, Upos.Noun)));
        var tagger = Tagger.Default;
        var sentences = DataFiles.ReadCorpus(DataFiles.Data("open/dev.tsv")).Select(s => (IReadOnlyList<string>)s.Tokens.Select(t => t[0]).ToList()).ToList();
        var tokenCount = sentences.Sum(s => s.Count);

        // Act: the best rate with the glossary against the gate, and (a gate of zero ends the measurement after one window) the rate without it
        var withGlossary = ThroughputMeter.BestRate(() => sentences.ForEach(s => tagger.Tag(s, glossary)), tokenCount, ThroughputMeter.Gate, out var windows);
        var plain = ThroughputMeter.BestRate(() => sentences.ForEach(s => tagger.Tag(s)), tokenCount, 0, out _);

        // Assert: above the gate of the plain tagger (the overhead of the glossary is a diagnostic message, not a ratio that could be flaky)
        TestContext.Current.SendDiagnosticMessage($"Tagging {tokenCount} tokens, one thread: {plain:N0} tokens/s without a glossary, best {withGlossary:N0} tokens/s with {glossary.Count:N0} entries, {windows} timed window(s), instrumented: {(ThroughputMeter.IsInstrumented ? "yes" : "no")}, gate {ThroughputMeter.Gate:N0}.");
        Assert.True(withGlossary > ThroughputMeter.Gate, ThroughputMeter.Describe(withGlossary, windows));
    }
}
