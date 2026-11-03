using System.Security.Cryptography;
using System.Text;

namespace DemaConsulting.PartOfSpeech.Tests;

/// <summary>
///     One sentence of a committed token-level corpus (the shared corpus block format).
/// </summary>
internal sealed class CorpusSentence
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="CorpusSentence"/> class.
    /// </summary>
    /// <param name="sentenceId">The value of the <c># sent_id</c> comment.</param>
    /// <param name="sourceId">The value of the <c># source_id</c> comment (the document id).</param>
    /// <param name="text">The value of the <c># text</c> comment.</param>
    /// <param name="tokens">The token rows: form, tag, status and optional source tag.</param>
    public CorpusSentence(string sentenceId, string sourceId, string text, IReadOnlyList<string[]> tokens)
    {
        SentenceId = sentenceId;
        SourceId = sourceId;
        Text = text;
        Tokens = tokens;
    }

    /// <summary>
    ///     Gets the sentence id.
    /// </summary>
    public string SentenceId { get; }

    /// <summary>
    ///     Gets the id of the document that the sentence comes from.
    /// </summary>
    public string SourceId { get; }

    /// <summary>
    ///     Gets the sentence text.
    /// </summary>
    public string Text { get; }

    /// <summary>
    ///     Gets the token rows (tab-separated cells).
    /// </summary>
    public IReadOnlyList<string[]> Tokens { get; }
}

/// <summary>
///     A committed tab-separated table with named columns (gold v2, shape probe and the like).
/// </summary>
internal sealed class DataTable
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="DataTable"/> class.
    /// </summary>
    /// <param name="columns">The column names.</param>
    /// <param name="rows">The rows (tab-separated cells).</param>
    public DataTable(string[] columns, IReadOnlyList<string[]> rows)
    {
        Columns = columns;
        Rows = rows;
    }

    /// <summary>
    ///     Gets the column names.
    /// </summary>
    public string[] Columns { get; }

    /// <summary>
    ///     Gets the rows.
    /// </summary>
    public IReadOnlyList<string[]> Rows { get; }

    /// <summary>
    ///     Gets the value of one column in a row.
    /// </summary>
    /// <param name="row">A row of <see cref="Rows"/>.</param>
    /// <param name="name">The column name.</param>
    /// <returns>The value of the column.</returns>
    public string Cell(string[] row, string name)
    {
        var index = Array.IndexOf(Columns, name);
        if (index < 0)
        {
            throw new InvalidDataException($"Column '{name}' is missing; the columns are: {string.Join(", ", Columns)}");
        }

        return row[index];
    }

    /// <summary>
    ///     Gets the values of one column.
    /// </summary>
    /// <param name="name">The column name.</param>
    /// <returns>The value of the column in every row.</returns>
    public IEnumerable<string> Column(string name)
    {
        var index = Array.IndexOf(Columns, name);
        if (index < 0)
        {
            throw new InvalidDataException($"Column '{name}' is missing; the columns are: {string.Join(", ", Columns)}");
        }

        return Rows.Select(row => row[index]);
    }
}

/// <summary>
///     Helpers that locate and read the committed data files that the data and licensing tests verify.
/// </summary>
internal static class DataFiles
{
    /// <summary>
    ///     The 17 Universal POS tags.
    /// </summary>
    public static readonly IReadOnlyList<string> UniversalTags = new[]
    {
        "ADJ", "ADP", "ADV", "AUX", "CCONJ", "DET", "INTJ", "NOUN", "NUM", "PART", "PRON", "PROPN", "PUNCT", "SCONJ",
        "SYM", "VERB", "X"
    };

    /// <summary>
    ///     Gets the repository root: the nearest folder above the test binaries that holds the solution file.
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">The solution file was not found in any parent folder.</exception>
    public static string RepositoryRoot
    {
        get
        {
            // Walk up from the test binaries until the solution file (or a .git entry) is found
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "PartOfSpeech.slnx")) ||
                    Directory.Exists(Path.Combine(directory.FullName, ".git")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Repository root not found above " + AppContext.BaseDirectory);
        }
    }

    /// <summary>
    ///     Gets the full path of a file below the repository root.
    /// </summary>
    /// <param name="relativePath">The path below the root, with forward slashes.</param>
    /// <returns>The full path.</returns>
    public static string Repository(string relativePath) =>
        Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>
    ///     Gets the full path of a file below the <c>data</c> folder.
    /// </summary>
    /// <param name="relativePath">The path below <c>data</c>, with forward slashes.</param>
    /// <returns>The full path.</returns>
    public static string Data(string relativePath) => Repository("data/" + relativePath);

    /// <summary>
    ///     Gets the repository-relative path of a file, with forward slashes.
    /// </summary>
    /// <param name="fullPath">The full path of a file below the repository root.</param>
    /// <returns>The relative path.</returns>
    public static string RelativePath(string fullPath) =>
        fullPath.Substring(RepositoryRoot.Length).TrimStart('/', '\\').Replace('\\', '/');

    /// <summary>
    ///     Determines whether a line is a comment of the data files (a hash followed by a space).
    /// </summary>
    /// <param name="line">The line.</param>
    /// <returns><c>true</c> for a comment line.</returns>
    public static bool IsComment(string line) => line.StartsWith("# ", StringComparison.Ordinal);

    /// <summary>
    ///     Reads a corpus in the shared block format: header comments, a blank line, then one block per sentence.
    /// </summary>
    /// <param name="path">The full path of the file.</param>
    /// <returns>The sentences in file order.</returns>
    public static IEnumerable<CorpusSentence> ReadCorpus(string path)
    {
        var comments = new Dictionary<string, string>(StringComparer.Ordinal);
        var tokens = new List<string[]>();
        foreach (var line in File.ReadLines(path).Concat(new[] { string.Empty }))
        {
            if (line.Length > 0)
            {
                AddBlockLine(line, comments, tokens);
                continue;
            }

            var sentence = ToSentence(path, comments, tokens);
            comments.Clear();
            tokens = new List<string[]>();
            if (sentence != null)
            {
                yield return sentence;
            }
        }
    }

    /// <summary>
    ///     Reads a tab-separated table whose columns come from a <c># columns =</c> comment (names separated by spaces,
    ///     optionally with a trailing comma) or from the first line.
    /// </summary>
    /// <param name="path">The full path of the file.</param>
    /// <returns>The table.</returns>
    /// <exception cref="InvalidDataException">A row has a different number of cells than the table has columns.</exception>
    public static DataTable ReadTable(string path)
    {
        string[]? columns = null;
        var rows = new List<string[]>();
        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith("# columns = ", StringComparison.Ordinal))
            {
                columns = line.Substring("# columns = ".Length).Split(' ').Select(name => name.TrimEnd(',')).ToArray();
            }
            else if (line.Length > 0 && line[0] != '#')
            {
                var cells = line.Split('\t');
                if (columns == null)
                {
                    columns = cells;
                }
                else if (cells.Length != columns.Length)
                {
                    throw new InvalidDataException($"{path}: a row has {cells.Length} cells, but the table has {columns.Length} columns");
                }
                else
                {
                    rows.Add(cells);
                }
            }
        }

        return new DataTable(columns ?? throw new InvalidDataException(path + ": no columns"), rows);
    }

    /// <summary>
    ///     Reads the lexicon: one lower-case spelling per line followed by its alphabetical UPOS tags.
    /// </summary>
    /// <param name="path">The full path of <c>lexicon.tsv</c>.</param>
    /// <returns>The tags of every spelling.</returns>
    public static Dictionary<string, string[]> ReadLexicon(string path)
    {
        var lexicon = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(path))
        {
            if (line.Length == 0 || IsComment(line))
            {
                continue;
            }

            var tab = line.IndexOf('\t');
            lexicon[line.Substring(0, tab)] = line.Substring(tab + 1).Split(' ');
        }

        return lexicon;
    }

    /// <summary>
    ///     Determines whether the lexicon and the token-shape rules allow a tag for a token.
    /// </summary>
    /// <remarks>
    ///     The rules are the normative ones of <c>data/lexicon/README.md</c>: a spelling that is not in the lexicon allows
    ///     all 17 tags; otherwise the lexicon tags are allowed, plus the tags that the shape of the token allows.
    /// </remarks>
    /// <param name="token">The token as written.</param>
    /// <param name="tag">The tag to test.</param>
    /// <param name="lexicon">The lexicon.</param>
    /// <returns><c>true</c> if the tag is allowed.</returns>
    public static bool IsTagAllowed(string token, string tag, IReadOnlyDictionary<string, string[]> lexicon)
    {
        if (!lexicon.TryGetValue(token.ToLowerInvariant(), out var tags))
        {
            return true;
        }

        return Array.IndexOf(tags, tag) >= 0 || ShapeAllows(token, tag);
    }

    /// <summary>
    ///     Computes the sentence key of the leakage rule: the lower-case letters and digits of the text.
    /// </summary>
    /// <param name="text">The sentence text.</param>
    /// <returns>The key.</returns>
    public static string SentenceKey(string text)
    {
        var key = new StringBuilder(text.Length);
        foreach (var ch in text.Normalize(NormalizationForm.FormC).ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                key.Append(ch);
            }
        }

        return key.ToString();
    }

    /// <summary>
    ///     Computes the lower-case hexadecimal SHA-256 of a file.
    /// </summary>
    /// <param name="path">The full path of the file.</param>
    /// <returns>The hash.</returns>
    public static string Sha256Hex(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
    }

    /// <summary>
    ///     Enumerates the files below a folder, in a stable order, skipping the folders that the filter selects.
    /// </summary>
    /// <param name="root">The full path of the folder.</param>
    /// <param name="skipFolder">Returns <c>true</c> for the full path of a folder that is not entered.</param>
    /// <returns>The full paths of the files.</returns>
    public static IEnumerable<string> EnumerateFiles(string root, Func<string, bool> skipFolder)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        foreach (var file in Directory.GetFiles(root).OrderBy(f => f, StringComparer.Ordinal))
        {
            yield return file;
        }

        foreach (var folder in Directory.GetDirectories(root).OrderBy(f => f, StringComparer.Ordinal))
        {
            if (skipFolder(folder))
            {
                continue;
            }

            foreach (var file in EnumerateFiles(folder, skipFolder))
            {
                yield return file;
            }
        }
    }

    /// <summary>
    ///     Adds one non-empty line of a corpus block to the comments or the token rows.
    /// </summary>
    private static void AddBlockLine(string line, Dictionary<string, string> comments, List<string[]> tokens)
    {
        if (!IsComment(line))
        {
            tokens.Add(line.Split('\t'));
            return;
        }

        var separator = line.IndexOf(" = ", StringComparison.Ordinal);
        if (separator > 0)
        {
            comments[line.Substring(2, separator - 2)] = line.Substring(separator + 3);
        }
    }

    /// <summary>
    ///     Converts a finished block to a sentence; a block without a sentence id is a header and yields nothing.
    /// </summary>
    private static CorpusSentence? ToSentence(string path, Dictionary<string, string> comments, List<string[]> tokens)
    {
        if (comments.Count == 0 && tokens.Count == 0)
        {
            return null;
        }

        if (!comments.TryGetValue("sent_id", out var sentenceId))
        {
            return tokens.Count == 0 ? null : throw new InvalidDataException(path + ": token rows without a sent_id");
        }

        comments.TryGetValue("source_id", out var sourceId);
        comments.TryGetValue("text", out var text);
        return new CorpusSentence(sentenceId, sourceId ?? string.Empty, text ?? string.Empty, tokens);
    }

    /// <summary>
    ///     Determines whether the shape of a token allows a tag that the lexicon does not list for it.
    /// </summary>
    private static bool ShapeAllows(string token, string tag)
    {
        if (!token.Any(char.IsLetterOrDigit))
        {
            return tag is "PUNCT" or "SYM" or "X";
        }

        if (token.Any(char.IsDigit) && tag is "NUM" or "SYM" or "X" or "NOUN" or "PROPN" or "ADJ")
        {
            return true;
        }

        return char.IsUpper(token[0]) && tag is "PROPN" or "X";
    }
}
