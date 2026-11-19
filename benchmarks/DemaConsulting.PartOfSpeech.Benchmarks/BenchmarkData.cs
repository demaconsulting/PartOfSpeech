namespace DemaConsulting.PartOfSpeech.Benchmarks;

/// <summary>
///     The input of the benchmarks: the pre-tokenized sentences of the open development set and a glossary of 10,000 word
///     pairs from the open training set.
/// </summary>
internal static class BenchmarkData
{
    /// <summary>
    ///     The number of glossary entries of the glossary benchmarks.
    /// </summary>
    public const int GlossaryEntries = 10000;

    /// <summary>
    ///     The sentences of the open development set, each as its list of tokens.
    /// </summary>
    private static readonly Lazy<IReadOnlyList<IReadOnlyList<string>>> LazySentences =
        new(() => ReadCorpus(Path.Combine(RepositoryRoot(), "data", "open", "dev.tsv")));

    /// <summary>
    ///     Gets the sentences of the open development set, each as its list of tokens.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> Sentences => LazySentences.Value;

    /// <summary>
    ///     Gets the number of tokens of <see cref="Sentences"/>, which one run of a tagging benchmark processes.
    /// </summary>
    public static int TokenCount => Sentences.Sum(sentence => sentence.Count);

    /// <summary>
    ///     Creates a glossary of <see cref="GlossaryEntries"/> distinct pairs of adjacent words of the open training set.
    /// </summary>
    /// <returns>The glossary.</returns>
    public static Glossary CreateGlossary()
    {
        var terms = ReadCorpus(Path.Combine(RepositoryRoot(), "data", "open", "train.tsv"))
            .SelectMany(tokens => tokens.Zip(tokens.Skip(1), (first, second) => (First: first, Second: second)))
            .Where(pair => pair.First.All(char.IsLetter) && pair.Second.All(char.IsLetter))
            .Select(pair => pair.First + " " + pair.Second)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(GlossaryEntries);
        return Glossary.Create(terms.Select(term => new GlossaryEntry(term, Upos.Noun)));
    }

    /// <summary>
    ///     Reads the token forms of a corpus file: one token per line with the form in the first column, sentences
    ///     separated by blank lines, and comment lines that start with <c>#</c>.
    /// </summary>
    /// <param name="path">The path of the corpus file.</param>
    /// <returns>The sentences, each as its list of tokens.</returns>
    private static List<IReadOnlyList<string>> ReadCorpus(string path)
    {
        var sentences = new List<IReadOnlyList<string>>();
        var current = new List<string>();
        foreach (var line in File.ReadLines(path))
        {
            if (line.Length == 0)
            {
                if (current.Count > 0)
                {
                    sentences.Add(current);
                    current = new List<string>();
                }

                continue;
            }

            if (!line.StartsWith('#'))
            {
                current.Add(line.Split('\t')[0]);
            }
        }

        if (current.Count > 0)
        {
            sentences.Add(current);
        }

        return sentences;
    }

    /// <summary>
    ///     Finds the root of the repository by walking up from the working folder and the folder of the assembly to the
    ///     folder that holds the solution file.
    /// </summary>
    /// <returns>The path of the repository root.</returns>
    private static string RepositoryRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var folder = new DirectoryInfo(start); folder != null; folder = folder.Parent)
            {
                if (File.Exists(Path.Combine(folder.FullName, "PartOfSpeech.slnx")))
                {
                    return folder.FullName;
                }
            }
        }

        throw new InvalidOperationException("The benchmarks must run inside a checkout of the repository (PartOfSpeech.slnx not found).");
    }
}
