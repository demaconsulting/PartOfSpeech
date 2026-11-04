using System.Text;

namespace DemaConsulting.PartOfSpeech.Trainer;

/// <summary>
///     One training sentence: its tokens and, for every token, the gold tag or the marker that the token gives no training signal.
/// </summary>
internal sealed class TrainingSentence
{
    /// <summary>
    ///     The value of <see cref="Gold"/> for a token without a training signal.
    /// </summary>
    public const sbyte NoSignal = -1;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TrainingSentence"/> class.
    /// </summary>
    /// <param name="sentenceId">The sentence id of the corpus.</param>
    /// <param name="sourceId">The id of the document that the sentence belongs to.</param>
    /// <param name="tokens">The tokens as written.</param>
    /// <param name="gold">The gold tag ordinal of every token, or <see cref="NoSignal"/>.</param>
    public TrainingSentence(string sentenceId, string sourceId, string[] tokens, sbyte[] gold)
    {
        SentenceId = sentenceId;
        SourceId = sourceId;
        Tokens = tokens;
        Gold = gold;
    }

    /// <summary>
    ///     Gets the sentence id of the corpus.
    /// </summary>
    public string SentenceId { get; }

    /// <summary>
    ///     Gets the id of the document that the sentence belongs to.
    /// </summary>
    public string SourceId { get; }

    /// <summary>
    ///     Gets the tokens as written in the corpus.
    /// </summary>
    public string[] Tokens { get; }

    /// <summary>
    ///     Gets the gold tag ordinal of every token, or <see cref="NoSignal"/> for a masked token.
    /// </summary>
    public sbyte[] Gold { get; }
}

/// <summary>
///     Reads the committed corpora (the shared block format) into training sentences and applies the masking rules.
/// </summary>
/// <remarks>
///     <para>
///         A corpus file is a header of <c># key = value</c> comments, then one block per sentence: comment lines
///         (<c># sent_id</c>, <c># source_id</c>) and one tab-separated row per token (form, tag, status and an optional
///         source tag), blocks separated by an empty line. A comment starts with a hash and a space, so the token
///         <c>#</c> (which has a tab after the hash) is a row, not a comment.
///     </para>
///     <para>
///         A token gives no training signal when its tag is <c>_</c> or its status is <c>masked</c> (the silver labeler
///         declined), when its status is <c>disputed</c> and the source masks those, or when a silver probability limit
///         is set and the token is below it. Such a token still counts as a step of the averaging and still enters the
///         history, because the runtime decoder would also tag it; only the weight update is skipped.
///     </para>
///     <para>
///         The class is stateless and thread-safe. Files are refused when they are held-out evaluation sets (see
///         <see cref="DataGuard"/>).
///     </para>
/// </remarks>
internal static class CorpusReader
{
    /// <summary>
    ///     Reads a corpus file.
    /// </summary>
    /// <param name="repositoryRoot">The full path of the repository root; the file must lie below its <c>data</c> folder.</param>
    /// <param name="path">The full path of the file; it must not be a held-out evaluation set.</param>
    /// <param name="source">The masking and sampling settings of the source.</param>
    /// <returns>The sentences in file order, limited by the sampling settings.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="path"/> names a held-out evaluation set or lies outside the data folder.</exception>
    /// <exception cref="InvalidDataException">The file does not follow the corpus format.</exception>
    public static List<TrainingSentence> ReadFile(string repositoryRoot, string path, SourceConfig source)
    {
        DataGuard.EnsureTrainable(repositoryRoot, path);
        return Parse(File.ReadLines(path, Encoding.UTF8), source, path);
    }

    /// <summary>
    ///     Reads every source of a configuration and repeats the sentences of each source by its weight.
    /// </summary>
    /// <remarks>
    ///     A weight of 2 puts every sentence of the source into the epoch twice (the same object, so the allowed tags
    ///     are computed once); the shuffle then mixes the repeats with everything else. A weight of 0 leaves the source
    ///     out without reading it.
    /// </remarks>
    /// <param name="repositoryRoot">The full path of the repository root; the source paths are relative to it.</param>
    /// <param name="config">The configuration.</param>
    /// <returns>The sentences of one epoch, in source order.</returns>
    /// <exception cref="InvalidOperationException">A source is a held-out evaluation set.</exception>
    /// <exception cref="InvalidDataException">A file does not follow the corpus format.</exception>
    public static List<TrainingSentence> LoadSources(string repositoryRoot, TrainingConfig config)
    {
        ArgumentNullException.ThrowIfNull(repositoryRoot);
        ArgumentNullException.ThrowIfNull(config);
        var result = new List<TrainingSentence>();
        foreach (var source in config.Sources.Where(s => s.Weight > 0))
        {
            var path = Path.Combine(repositoryRoot, source.Path.Replace('/', Path.DirectorySeparatorChar));
            var sentences = ReadFile(repositoryRoot, path, source);
            for (var repeat = 0; repeat < source.Weight; repeat++)
            {
                result.AddRange(sentences);
            }
        }

        return result;
    }

    /// <summary>
    ///     Parses the lines of a corpus.
    /// </summary>
    /// <param name="lines">The lines, without line terminators.</param>
    /// <param name="source">The masking and sampling settings of the source.</param>
    /// <param name="origin">The name of the origin for error messages.</param>
    /// <returns>The sentences in file order, limited by the sampling settings.</returns>
    /// <exception cref="InvalidDataException">A row is malformed or a tag is unknown.</exception>
    public static List<TrainingSentence> Parse(IEnumerable<string> lines, SourceConfig source, string origin)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(source);
        var result = new List<TrainingSentence>();
        string sentenceId = string.Empty;
        string sourceId = string.Empty;
        var forms = new List<string>();
        var gold = new List<sbyte>();
        var lineNumber = 0;

        // Finishes the block that was collected so far; a block without rows (the file header) yields nothing
        void Flush()
        {
            if (forms.Count > 0 && Selected(sentenceId, source))
            {
                result.Add(new TrainingSentence(sentenceId, sourceId, forms.ToArray(), gold.ToArray()));
            }

            sentenceId = string.Empty;
            sourceId = string.Empty;
            forms.Clear();
            gold.Clear();
        }

        foreach (var line in lines)
        {
            lineNumber++;
            if (line.Length == 0)
            {
                Flush();
                if (source.MaxSentences > 0 && result.Count >= source.MaxSentences)
                {
                    return result;
                }

                continue;
            }

            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                ReadComment(line, ref sentenceId, ref sourceId);
                continue;
            }

            var cells = line.Split('\t');
            if (cells.Length < 3)
            {
                throw new InvalidDataException($"{origin}, line {lineNumber}: a token row needs at least form, tag and status.");
            }

            forms.Add(cells[0]);
            gold.Add(ReadGold(cells, source, origin, lineNumber));
        }

        Flush();
        if (source.MaxSentences > 0 && result.Count > source.MaxSentences)
        {
            result.RemoveRange(source.MaxSentences, result.Count - source.MaxSentences);
        }

        return result;
    }

    /// <summary>
    ///     Decides whether the sampling settings select a sentence.
    /// </summary>
    /// <remarks>
    ///     The decision is the FNV-1a hash of the sentence id modulo 1000 against the permille, so a subset does not
    ///     depend on file order or platform and shrinking the share keeps a prefix of the previous selection.
    /// </remarks>
    /// <param name="sentenceId">The sentence id.</param>
    /// <param name="source">The sampling settings.</param>
    /// <returns><c>true</c> if the sentence is used.</returns>
    public static bool Selected(string sentenceId, SourceConfig source)
    {
        if (source.SamplePermille >= 1000)
        {
            return true;
        }

        var bytes = Encoding.UTF8.GetBytes(sentenceId);
        return BinaryFormat.Fnv1a(bytes, 0, bytes.Length) % 1000 < (uint)source.SamplePermille;
    }

    /// <summary>
    ///     Reads a <c># key = value</c> comment and keeps the two ids that the reader needs.
    /// </summary>
    /// <param name="line">The comment line.</param>
    /// <param name="sentenceId">Receives the sentence id when the comment is <c># sent_id</c>.</param>
    /// <param name="sourceId">Receives the document id when the comment is <c># source_id</c>.</param>
    private static void ReadComment(string line, ref string sentenceId, ref string sourceId)
    {
        const string SentenceKey = "# sent_id = ";
        const string SourceKey = "# source_id = ";
        if (line.StartsWith(SentenceKey, StringComparison.Ordinal))
        {
            sentenceId = line.Substring(SentenceKey.Length);
        }
        else if (line.StartsWith(SourceKey, StringComparison.Ordinal))
        {
            sourceId = line.Substring(SourceKey.Length);
        }
    }

    /// <summary>
    ///     Converts the tag, status and source tag of a row into a gold ordinal or the no-signal marker.
    /// </summary>
    /// <param name="cells">The cells of the row.</param>
    /// <param name="source">The masking settings.</param>
    /// <param name="origin">The origin for error messages.</param>
    /// <param name="lineNumber">The line number for error messages.</param>
    /// <returns>The gold tag ordinal or <see cref="TrainingSentence.NoSignal"/>.</returns>
    private static sbyte ReadGold(string[] cells, SourceConfig source, string origin, int lineNumber)
    {
        var tag = cells[1];
        var status = cells[2];
        if (tag == "_" || status == "masked")
        {
            return TrainingSentence.NoSignal;
        }

        if (!UposExtensions.TryParse(tag, out var upos))
        {
            throw new InvalidDataException($"{origin}, line {lineNumber}: unknown tag '{tag}'.");
        }

        if (status == "disputed" && source.MaskDisputed)
        {
            return TrainingSentence.NoSignal;
        }

        if (source.MinSilverProbability > 0 && cells.Length > 3 && TryReadProbability(cells[3], out var probability) && probability < source.MinSilverProbability)
        {
            return TrainingSentence.NoSignal;
        }

        return (sbyte)(int)upos;
    }

    /// <summary>
    ///     Reads the probability in percent from a silver source tag such as <c>V:99</c> or <c>N:73@s</c>.
    /// </summary>
    /// <param name="sourceTag">The source tag.</param>
    /// <param name="probability">Receives the percentage.</param>
    /// <returns><c>true</c> if the tag carries a probability.</returns>
    private static bool TryReadProbability(string sourceTag, out int probability)
    {
        probability = 0;
        var colon = sourceTag.IndexOf(':');
        if (colon < 0)
        {
            return false;
        }

        var end = colon + 1;
        while (end < sourceTag.Length && sourceTag[end] >= '0' && sourceTag[end] <= '9')
        {
            end++;
        }

        return end > colon + 1 && int.TryParse(sourceTag.Substring(colon + 1, end - colon - 1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out probability);
    }
}
