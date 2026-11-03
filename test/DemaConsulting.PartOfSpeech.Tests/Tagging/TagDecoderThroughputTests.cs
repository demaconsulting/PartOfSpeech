namespace DemaConsulting.PartOfSpeech.Tests.Tagging;

/// <summary>
///     Throughput test of <see cref="TagDecoder"/>. It is in its own class, in the non-parallel throughput collection, so
///     that it does not run at the same time as the other test classes of the test host.
/// </summary>
[Collection(ThroughputCollection.Name)]
public class TagDecoderThroughputTests
{
    /// <summary>
    ///     Proves that the decoder tags real text at more than 50,000 tokens per second on one thread with a model of realistic size.
    /// </summary>
    [Fact]
    public void TagDecoder_Decode_SyntheticModel_ExceedsFiftyThousandTokensPerSecond()
    {
        // Arrange: the synthetic model (150,000 rows in 2^21 buckets), the embedded lexicon and the development sentences
        var sentences = DataFiles.ReadCorpus(DataFiles.Data("open/dev.tsv")).Select(s => Tokens(s.Tokens.Select(t => t[0]).ToArray())).ToList();
        var training = DataFiles.ReadCorpus(DataFiles.Data("open/train.tsv")).Take(4000).Select(s => s.Tokens.Select(t => t[0]).ToArray());
        var decoder = new TagDecoder(
            PerceptronModel.Load(SyntheticModel.Create(training), FeatureExtractor.FeatureSetVersion),
            EmbeddedModel.Lexicon);
        var tokenCount = sentences.Sum(s => s.Length);

        // Act: warm up, then take the best of several timed passes over all sentences
        var rate = ThroughputMeter.BestRate(
            () =>
            {
                foreach (var sentence in sentences)
                {
                    decoder.Decode(sentence, null, 3);
                }
            },
            tokenCount);

        // Assert: well above the gate (the measured rate is reported in the test output)
        TestContext.Current.SendDiagnosticMessage($"Decoder throughput: {rate:N0} tokens/s over {tokenCount} tokens ({sentences.Count} sentences), best of {ThroughputMeter.Passes} passes.");
        Assert.True(rate > 50000, $"{rate:N0} tokens per second");
    }

    /// <summary>
    ///     Creates tokens with offsets for words separated by single spaces.
    /// </summary>
    private static Token[] Tokens(params string[] words)
    {
        var tokens = new Token[words.Length];
        var start = 0;
        for (var i = 0; i < words.Length; i++)
        {
            tokens[i] = new Token(words[i], start);
            start += words[i].Length + 1;
        }

        return tokens;
    }
}
