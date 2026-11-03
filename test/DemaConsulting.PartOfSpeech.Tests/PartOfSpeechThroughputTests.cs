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

        // Act: warm up, then take the best of several timed passes over all sentences
        var rate = ThroughputMeter.BestRate(
            () =>
            {
                foreach (var sentence in sentences)
                {
                    tagger.Tag(sentence);
                }
            },
            tokenCount);

        // Assert: the gate is 50,000 tokens per second (several hundred thousand are expected); the measured rate goes to the output
        TestContext.Current.SendDiagnosticMessage($"Tagger throughput (pre-tokenized, one thread): {rate:N0} tokens/s over {tokenCount} tokens, best of {ThroughputMeter.Passes} passes.");
        Assert.True(rate > 50000, $"{rate:N0} tokens per second");
    }
}
