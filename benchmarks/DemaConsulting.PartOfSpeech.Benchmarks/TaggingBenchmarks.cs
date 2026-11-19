using BenchmarkDotNet.Attributes;

namespace DemaConsulting.PartOfSpeech.Benchmarks;

/// <summary>
///     Benchmarks of tagging the pre-tokenized sentences of the open development set on one thread, without and with a
///     glossary of 10,000 entries, and of finding the terms of that glossary alone.
/// </summary>
/// <remarks>
///     The design targets are more than 50,000 tokens per second for tagging, with and without the glossary, and more
///     than ten times that for finding the terms; the <c>Tokens/s</c> column states the measured rate and the
///     <c>Allocated</c> column the memory of one run, which is the memory of the results.
/// </remarks>
[MemoryDiagnoser]
[Config(typeof(TokensPerSecondConfig))]
public class TaggingBenchmarks
{
    /// <summary>
    ///     The tagger with the embedded model.
    /// </summary>
    private readonly Tagger _tagger = Tagger.Default;

    /// <summary>
    ///     The pre-tokenized sentences.
    /// </summary>
    private IReadOnlyList<IReadOnlyList<string>> _sentences = [];

    /// <summary>
    ///     The same sentences as <see cref="Sentence"/> objects, for the glossary matching.
    /// </summary>
    private List<Sentence> _tokenizedSentences = [];

    /// <summary>
    ///     The glossary of 10,000 entries.
    /// </summary>
    private Glossary _glossary = Glossary.Create([]);

    /// <summary>
    ///     Loads the sentences and builds the glossary.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _sentences = BenchmarkData.Sentences;
        _tokenizedSentences = _sentences.Select(Tokenizer.CreateSentence).ToList();
        _glossary = BenchmarkData.CreateGlossary();
    }

    /// <summary>
    ///     Tags every sentence without a glossary.
    /// </summary>
    /// <returns>The number of tagged tokens, so that the work is not optimized away.</returns>
    [Benchmark(Baseline = true)]
    public int Tag()
    {
        var count = 0;
        foreach (var sentence in _sentences)
        {
            count += _tagger.Tag(sentence).Tokens.Count;
        }

        return count;
    }

    /// <summary>
    ///     Tags every sentence with the glossary of 10,000 entries.
    /// </summary>
    /// <returns>The number of tagged tokens, so that the work is not optimized away.</returns>
    [Benchmark]
    public int TagWithGlossary()
    {
        var count = 0;
        foreach (var sentence in _sentences)
        {
            count += _tagger.Tag(sentence, _glossary).Tokens.Count;
        }

        return count;
    }

    /// <summary>
    ///     Finds the terms of the glossary of 10,000 entries in every sentence.
    /// </summary>
    /// <returns>The number of matches, so that the work is not optimized away.</returns>
    [Benchmark]
    public int FindMatches()
    {
        var count = 0;
        foreach (var sentence in _tokenizedSentences)
        {
            count += _glossary.FindMatches(sentence).Count;
        }

        return count;
    }
}
