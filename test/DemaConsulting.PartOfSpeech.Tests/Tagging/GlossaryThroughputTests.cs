namespace DemaConsulting.PartOfSpeech.Tests.Tagging;

/// <summary>
///     Throughput test of the matching of a <see cref="Glossary"/>. It is in its own class, in the non-parallel throughput
///     collection, so that it does not run at the same time as the other test classes of the test host.
/// </summary>
[Collection(ThroughputCollection.Name)]
public class GlossaryThroughputTests
{
    /// <summary>
    ///     Proves that finding the terms of a glossary of 10,000 entries in running text on one thread runs at more than ten times the tagging gate.
    /// </summary>
    [Fact]
    public void Glossary_FindMatches_TenThousandEntries_ExceedsTenTimesTheTaggingGate()
    {
        // Arrange: 10,000 distinct terms (word pairs from the training text), and the development sentences as sentences
        var training = DataFiles.ReadCorpus(DataFiles.Data("open/train.tsv")).Take(4000).Select(s => s.Tokens.Select(t => t[0]).ToArray()).ToList();
        var terms = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tokens in training)
        {
            for (var i = 0; i + 1 < tokens.Length && terms.Count < 10000; i++)
            {
                var pair = tokens[i] + " " + tokens[i + 1];
                if (tokens[i].All(char.IsLetter) && tokens[i + 1].All(char.IsLetter) && seen.Add(pair))
                {
                    terms.Add(pair);
                }
            }
        }

        var glossary = Glossary.Create(terms.Select(term => new GlossaryEntry(term, Upos.Noun)));
        var sentences = DataFiles.ReadCorpus(DataFiles.Data("open/dev.tsv")).Select(s => Tokenizer.CreateSentence(s.Tokens.Select(t => t[0]).ToList())).ToList();
        var tokenCount = sentences.Sum(s => s.Tokens.Count);
        var matchCount = sentences.Sum(s => glossary.FindMatches(s).Count);

        // Act: warm up, then time windows over all sentences until one exceeds the gate (or the time budget is used up)
        var gate = 10 * ThroughputMeter.Gate;
        var rate = ThroughputMeter.BestRate(
            () =>
            {
                foreach (var sentence in sentences)
                {
                    glossary.FindMatches(sentence);
                }
            },
            tokenCount,
            gate,
            out var windows);

        // Assert: ten times the tagging gate (millions of tokens per second are expected), on a glossary that really matches
        TestContext.Current.SendDiagnosticMessage($"Glossary matching throughput ({glossary.Count:N0} entries, {matchCount:N0} matches, one thread): best {rate:N0} tokens/s over {tokenCount} tokens, {windows} timed window(s), instrumented: {(ThroughputMeter.IsInstrumented ? "yes" : "no")}, gate {gate:N0}.");
        Assert.True(matchCount > 0, "the glossary must match something in the sentences");
        Assert.True(rate > gate, ThroughputMeter.Describe(rate, windows));
    }
}
