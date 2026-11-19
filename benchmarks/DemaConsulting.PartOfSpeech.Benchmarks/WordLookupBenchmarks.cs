// cspell:ignore zzqxv
using BenchmarkDotNet.Attributes;

namespace DemaConsulting.PartOfSpeech.Benchmarks;

/// <summary>
///     Benchmarks of the word lookup, the form question and the margin of a tagged token.
/// </summary>
/// <remarks>
///     The design target is that these calls allocate nothing for ASCII text: the <c>Allocated</c> column must show no
///     bytes (<c>-</c>).
/// </remarks>
[MemoryDiagnoser]
public class WordLookupBenchmarks
{
    /// <summary>
    ///     Known, capitalized, unknown, digit, punctuation, upper-case and hyphenated words, and compounds that are longer
    ///     than the longest spelling of the lexicon.
    /// </summary>
    private static readonly string[] AsciiWords =
    [
        "valve", "Valve", "zzqxv", "1st", "--", "TESTS", "tests", "well-tested", "built-in", "pre-flight", "-ed",
        "state-of-the-art-style", new string('a', 300) + "-tested", new string('a', 300) + "-zzqxv",
    ];

    /// <summary>
    ///     A compound whose whole is longer than any spelling and whose last part is a spelling.
    /// </summary>
    private static readonly string LongCompound = new string('a', 300) + "-tested";

    /// <summary>
    ///     The tagger with the embedded model.
    /// </summary>
    private readonly Tagger _tagger = Tagger.Default;

    /// <summary>
    ///     The tokens of a tagged sentence, for the margin benchmark.
    /// </summary>
    private IReadOnlyList<TaggedToken> _tokens = [];

    /// <summary>
    ///     Tags the sentence of the margin benchmark.
    /// </summary>
    [GlobalSetup]
    public void Setup() =>
        _tokens = _tagger.Tag(["Check", "the", "bleed", "valve", "before", "the", "test", "."]).Tokens;

    /// <summary>
    ///     Looks up every word in both modes.
    /// </summary>
    /// <returns>A checksum of the results, so that the work is not optimized away.</returns>
    [Benchmark]
    public int GetPossibleTags()
    {
        var sum = 0;
        foreach (var word in AsciiWords)
        {
            sum += _tagger.GetPossibleTags(word).Tags.Count;
            sum += _tagger.GetPossibleTags(word, PossibleTagsMode.WithShapeRules).Tags.Count;
        }

        return sum;
    }

    /// <summary>
    ///     Asks the form question for true and false cases, a compound head and an over-long compound.
    /// </summary>
    /// <returns>The number of true answers, so that the work is not optimized away.</returns>
    [Benchmark]
    public int IsFormOf()
    {
        var sum = 0;
        sum += _tagger.IsFormOf("tests", "test", Upos.Verb) ? 1 : 0;
        sum += _tagger.IsFormOf("TESTS", "Test", Upos.Noun) ? 1 : 0;
        sum += _tagger.IsFormOf("well-tested", "test", Upos.Verb) ? 1 : 0;
        sum += _tagger.IsFormOf(LongCompound, "test", Upos.Verb) ? 1 : 0;
        sum += _tagger.IsFormOf("tests", "test", Upos.Adj) ? 1 : 0;
        sum += _tagger.IsFormOf("zzqxv", "zzqxv", Upos.Noun) ? 1 : 0;
        return sum;
    }

    /// <summary>
    ///     Reads the margin and the known-word flag of every token of a tagged sentence.
    /// </summary>
    /// <returns>The sum of the margins, so that the work is not optimized away.</returns>
    [Benchmark]
    public double Margin()
    {
        var sum = 0.0;
        for (var i = 0; i < _tokens.Count; i++)
        {
            var token = _tokens[i];
            sum += token.Margin + (token.IsKnownWord ? 1 : 0);
        }

        return sum;
    }
}
