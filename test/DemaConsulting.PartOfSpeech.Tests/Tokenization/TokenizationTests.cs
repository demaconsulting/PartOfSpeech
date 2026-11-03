namespace DemaConsulting.PartOfSpeech.Tests.Tokenization;

/// <summary>
///     Subsystem tests of Tokenization: sentences and tokens of text, the open-corpus conformance gate and the sentence boundary measure.
/// </summary>
public class TokenizationTests
{
    /// <summary>
    ///     The minimum share of open-corpus sentences that the tokenizer must reproduce exactly (the measured value is above 99.9 percent).
    /// </summary>
    private const double ConformanceGate = 0.998;

    /// <summary>
    ///     The minimum recall and precision of the sentence boundary measure (the measured values are above 99.5 percent).
    /// </summary>
    private const double BoundaryGate = 0.985;

    /// <summary>
    ///     Reads the open corpus sentences of the given files.
    /// </summary>
    private static List<CorpusSentence> OpenCorpus(params string[] files) =>
        files.SelectMany(file => DataFiles.ReadCorpus(DataFiles.Data("open/" + file))).ToList();

    /// <summary>
    ///     Proves that a multi-sentence text becomes sentences with tokens.
    /// </summary>
    [Fact]
    public void Tokenization_Text_SplitsIntoSentencesAndTokens()
    {
        // Arrange: two sentences with a contraction, a title and quotes
        const string text = "Mr. Smith didn't go. \"Why not?\" she asked.";

        // Act: tokenize
        var sentences = Tokenizer.Tokenize(text);

        // Assert: the expected sentences and tokens
        Assert.Equal(new[] { "Mr. Smith didn't go.", "\"Why not?\" she asked." }, sentences.Select(s => s.Text));
        Assert.Equal(
            new[] { "Mr.", "Smith", "did", "n't", "go", "." },
            sentences[0].Tokens.Select(t => t.Text));
        Assert.Equal(
            new[] { "\"", "Why", "not", "?", "\"", "she", "asked", "." },
            sentences[1].Tokens.Select(t => t.Text));
    }

    /// <summary>
    ///     Proves that the tokenizer reproduces the gold tokenization of the open corpus (14,980 sentences) at the conformance gate.
    /// </summary>
    [Fact]
    public void Tokenization_OpenCorpus_ReproducesGoldTokenization()
    {
        // Arrange: all sentences of the three open splits (# text is the original sentence, the rows are the gold tokens)
        var sentences = OpenCorpus("train.tsv", "dev.tsv", "test.tsv");
        var mismatches = new List<string>();

        // Act: tokenize every sentence text at the word level and compare with the gold tokens
        foreach (var sentence in sentences)
        {
            var actual = Tokenizer.TokenizeWords(sentence.Text).Select(t => t.Text);
            if (!actual.SequenceEqual(sentence.Tokens.Select(t => t[0])))
            {
                mismatches.Add(sentence.SentenceId);
            }
        }

        // Assert: at least the gate (also report the achieved value in the output)
        var exact = (sentences.Count - mismatches.Count) / (double)sentences.Count;
        TestContext.Current.SendDiagnosticMessage(
            $"Tokenizer conformance: {sentences.Count - mismatches.Count} of {sentences.Count} open-corpus sentences exact ({exact:P3}); " +
            $"mismatches: {string.Join(", ", mismatches.Take(20))}");
        Assert.Equal(14980, sentences.Count);
        Assert.True(exact >= ConformanceGate, $"only {exact:P3} of the sentences were reproduced exactly");
    }

    /// <summary>
    ///     Proves that on every open-corpus text the offsets index the text and the tokens rejoin to the text without white space.
    /// </summary>
    [Fact]
    public void Tokenization_OpenCorpus_OffsetsRoundTrip()
    {
        // Arrange: all open sentences
        var sentences = OpenCorpus("train.tsv", "dev.tsv", "test.tsv");
        var failures = new List<string>();

        // Act: tokenize with the public API and check the offsets and the concatenation
        foreach (var sentence in sentences)
        {
            var tokens = Tokenizer.Tokenize(sentence.Text).SelectMany(s => s.Tokens).ToList();
            var offsetsOk = tokens.All(t => string.CompareOrdinal(sentence.Text, t.Start, t.Text, 0, t.Length) == 0);
            var rejoined = string.Concat(tokens.Select(t => t.Text));
            var expected = new string(sentence.Text.Where(c => !char.IsWhiteSpace(c)).ToArray());
            if (!offsetsOk || rejoined != expected)
            {
                failures.Add(sentence.SentenceId);
            }
        }

        // Assert: no failure at all (the property must hold for every text, not for a share)
        Assert.Empty(failures.Take(10));
    }

    /// <summary>
    ///     Proves that the sentence splitter recovers the boundaries between consecutive sentences of the same development and test documents
    ///     that were joined with a space, with recall and precision at the gate.
    /// </summary>
    [Fact]
    public void Tokenization_JoinedDocuments_RecoverSentenceBoundaries()
    {
        // Arrange: group the dev and test sentences by document and join each document with single spaces
        var documents = OpenCorpus("dev.tsv", "test.tsv").GroupBy(s => s.SourceId).Where(g => g.Count() > 1);
        var goldTerminal = 0;
        var recovered = 0;
        var produced = 0;
        var correct = 0;

        // Act: split the joined text and compare boundary positions (end offsets) with the gold boundaries
        foreach (var document in documents)
        {
            var sentences = document.ToList();
            var joined = string.Join(" ", sentences.Select(s => s.Text));
            var gold = new HashSet<int>();
            var position = 0;
            for (var i = 0; i < sentences.Count - 1; i++)
            {
                position += sentences[i].Text.Length;
                gold.Add(position);
                position++;
            }

            var found = new HashSet<int>();
            var split = Tokenizer.Tokenize(joined);
            for (var i = 0; i < split.Count - 1; i++)
            {
                found.Add(split[i].Start + split[i].Length);
            }

            // Recall counts the gold boundaries that follow terminal punctuation (the others are corpus artifacts)
            foreach (var end in gold.Where(e => ".!?\"')".IndexOf(joined[e - 1]) >= 0))
            {
                goldTerminal++;
                recovered += found.Contains(end) ? 1 : 0;
            }

            produced += found.Count;
            correct += found.Count(gold.Contains);
        }

        // Assert: both measures reach the gate (precision is a lower bound: gold sentences can hold several sentences)
        var recall = recovered / (double)goldTerminal;
        var precision = correct / (double)produced;
        TestContext.Current.SendDiagnosticMessage(
            $"Sentence boundaries (dev and test documents): gold={goldTerminal}, recovered={recovered}, recall={recall:P2}, produced={produced}, correct={correct}, precision={precision:P2}");
        Assert.True(goldTerminal > 1000);
        Assert.True(recall >= BoundaryGate, $"recall {recall:P2}");
        Assert.True(precision >= BoundaryGate, $"precision {precision:P2}");
    }
}
