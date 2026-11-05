using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

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
    ///     Gets the status of a corpus status cell without its flags (<c>disputed+mentioned</c> gives <c>disputed</c>).
    /// </summary>
    /// <remarks>
    ///     Plain test support for the data files; the parser of the trainer is not used here, so that a unit test of
    ///     another unit does not depend on the trainer.
    /// </remarks>
    /// <param name="cell">The status cell.</param>
    /// <returns>The text before the first plus sign.</returns>
    public static string BaseStatus(string cell)
    {
        var plus = cell.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? cell : cell.Substring(0, plus);
    }

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

/// <summary>
///     Helpers of the data tests of the staged corpus corrections (<c>data/corrections</c>): the column lists, the
///     validation of one row and of a whole table, and the lookup of the addressed tokens in the committed corpora.
/// </summary>
/// <remarks>
///     This is the second implementation of the validation that <c>tools/data/corrections.py</c> performs; both are
///     held to the shared cases of <c>tools/data/tests/corrections-cases.tsv</c>. The corpus files are read with the
///     plain readers of <see cref="DataFiles"/>, so a test of the data does not depend on the trainer.
/// </remarks>
internal static class CorrectionTables
{
    /// <summary>
    ///     The names of the tables of corrections, relative to <c>data/corrections</c>; <c>llm.tsv</c> exists only after
    ///     the adjudication of the candidates.
    /// </summary>
    public static readonly IReadOnlyList<string> CorrectionTableNames = new[] { "mechanical.tsv", "llm.tsv" };

    /// <summary>
    ///     The columns of a table of corrections.
    /// </summary>
    public static readonly IReadOnlyList<string> CorrectionColumns = new[]
    {
        "source", "split", "source_id", "sent_id", "token_index", "token", "old_tag", "new_tag", "old_status",
        "new_status", "provenance"
    };

    /// <summary>
    ///     The columns of the table of candidates.
    /// </summary>
    public static readonly IReadOnlyList<string> CandidateColumns = new[]
    {
        "source", "split", "source_id", "sent_id", "token_index", "span_length", "token", "current_tag",
        "current_status", "source_tag", "candidate_rule", "hint", "context"
    };

    /// <summary>
    ///     The columns of the table of exceptions.
    /// </summary>
    public static readonly IReadOnlyList<string> ExceptionColumns = new[]
    {
        "source", "split", "source_id", "sent_id", "token_index", "token", "rule", "reason", "provenance"
    };

    /// <summary>
    ///     The corpus file of every addressable source and split, relative to the data folder.
    /// </summary>
    private static readonly Dictionary<(string Source, string Split), string> CorpusFiles = new()
    {
        [("open", "train")] = "open/train.tsv",
        [("open", "dev")] = "open/dev.tsv",
        [("open", "test")] = "open/test.tsv",
        [("masc", "-")] = "masc/masc.tsv",
        [("silver", "-")] = "silver/silver.tsv"
    };

    /// <summary>
    ///     The base statuses of the status cell of a token row.
    /// </summary>
    private static readonly string[] StatusBases = { "unanimous", "disputed", "unchecked", "ptb", "silver", "masked" };

    /// <summary>
    ///     The flags of the status cell, in their canonical order.
    /// </summary>
    private static readonly string[] StatusFlags = { "foreign", "mentioned" };

    /// <summary>
    ///     The provenance of a row: a rule of the catalog, or a model and a run of the adjudication.
    /// </summary>
    private static readonly Regex ProvenancePattern = new(
        @"^(?:rule:[a-z0-9]+(?:-[a-z0-9]+)*|llm:[^:\s]+:[^:\s]+)$", RegexOptions.CultureInvariant);

    /// <summary>
    ///     A non-negative integer without leading zeros.
    /// </summary>
    private static readonly Regex IndexPattern = new(@"^(?:0|[1-9][0-9]*)$", RegexOptions.CultureInvariant);

    /// <summary>
    ///     A positive integer without leading zeros.
    /// </summary>
    private static readonly Regex SpanPattern = new(@"^[1-9][0-9]*$", RegexOptions.CultureInvariant);

    /// <summary>
    ///     The id of a rule or of a candidate class: lower-case words joined by hyphens.
    /// </summary>
    private static readonly Regex RuleIdPattern = new(@"^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant);

    /// <summary>
    ///     Gets the full path of a table of the corrections folder.
    /// </summary>
    /// <param name="name">The file name, for example <c>mechanical.tsv</c>.</param>
    /// <returns>The full path.</returns>
    public static string FilePath(string name) => DataFiles.Data("corrections/" + name);

    /// <summary>
    ///     Finds the first error of one corrections row, with the same checks in the same order as the Python validator.
    /// </summary>
    /// <param name="table">The table that holds the row (it may have more columns than a corrections table).</param>
    /// <param name="row">The row.</param>
    /// <returns>The error code, or <c>null</c> when the row is valid on its own.</returns>
    public static string? RowError(DataTable table, string[] row) =>
        AddressError(table, row) ??
        TokenError(table, row) ??
        TagError(table, row) ??
        StatusError(table, row);

    /// <summary>
    ///     Finds the first problem of the address columns (source, split, token index) of a row of any overlay table.
    /// </summary>
    /// <param name="table">The table that holds the row.</param>
    /// <param name="row">The row.</param>
    /// <returns>The error code, or <c>null</c> when the address is legal.</returns>
    public static string? AddressError(DataTable table, string[] row)
    {
        var source = table.Cell(row, "source");
        var split = table.Cell(row, "split");
        if (source != "open" && source != "masc" && source != "silver")
        {
            return "bad-source";
        }

        var legal = source == "open" ? split is "train" or "dev" or "test" : split == "-";
        if (!legal)
        {
            return "bad-split";
        }

        return IndexPattern.IsMatch(table.Cell(row, "token_index")) ? null : "bad-index";
    }

    /// <summary>
    ///     Finds the problems of the whole table: the rows that are not valid on their own, rows out of order and
    ///     rows that share an address with an earlier row of the same table or of <paramref name="seen"/>.
    /// </summary>
    /// <param name="name">The name of the table for the messages.</param>
    /// <param name="kind">The kind of the table: <c>corrections</c>, <c>candidates</c> or <c>exceptions</c>.</param>
    /// <param name="table">The table.</param>
    /// <param name="seen">The keys of the rows of the tables of the same kind read before (updated).</param>
    /// <returns>The problems; empty when the table is well formed.</returns>
    public static IEnumerable<string> TableProblems(string name, string kind, DataTable table, HashSet<string> seen)
    {
        var columns = kind switch
        {
            "corrections" => CorrectionColumns,
            "candidates" => CandidateColumns,
            _ => ExceptionColumns
        };
        if (!columns.SequenceEqual(table.Columns))
        {
            yield return $"{name}: the columns are not {string.Join(", ", columns)}";
            yield break;
        }

        string[]? previous = null;
        foreach (var row in table.Rows)
        {
            var problem = RowProblem(kind, table, row);
            var where = $"{name}: {table.Cell(row, "sent_id")}#{table.Cell(row, "token_index")}";
            if (problem != null)
            {
                yield return $"{where}: {problem}";
                continue;
            }

            if (previous != null && CompareKeys(table, previous, row) > 0)
            {
                yield return where + ": out of order";
            }

            previous = row;
            if (!seen.Add(AddressKey(kind, table, row)))
            {
                yield return where + ": the address is used twice";
            }
        }
    }

    /// <summary>
    ///     Finds the sentences that the rows of the tables name, and reads them from the committed corpora.
    /// </summary>
    /// <param name="tables">The tables.</param>
    /// <returns>The sentences by source, split and sentence id; only the sentences that a row names are kept.</returns>
    public static Dictionary<(string Source, string Split), Dictionary<string, CorpusSentence>> ReadNamedSentences(
        IEnumerable<DataTable> tables)
    {
        // Collect the sentence ids per corpus so that the corpora are read once and only the named sentences are kept
        var wanted = new Dictionary<(string Source, string Split), HashSet<string>>();
        foreach (var table in tables)
        {
            foreach (var row in table.Rows)
            {
                var corpus = (table.Cell(row, "source"), table.Cell(row, "split"));
                if (!wanted.TryGetValue(corpus, out var ids))
                {
                    ids = new HashSet<string>(StringComparer.Ordinal);
                    wanted[corpus] = ids;
                }

                ids.Add(table.Cell(row, "sent_id"));
            }
        }

        var sentences = new Dictionary<(string Source, string Split), Dictionary<string, CorpusSentence>>();
        foreach (var entry in wanted)
        {
            var found = new Dictionary<string, CorpusSentence>(StringComparer.Ordinal);
            if (CorpusFiles.TryGetValue(entry.Key, out var file))
            {
                foreach (var sentence in DataFiles.ReadCorpus(DataFiles.Data(file)).Where(s => entry.Value.Contains(s.SentenceId)))
                {
                    found[sentence.SentenceId] = sentence;
                }
            }

            sentences[entry.Key] = found;
        }

        return sentences;
    }

    /// <summary>
    ///     Finds the token that a row addresses, and the problem of the address when there is none.
    /// </summary>
    /// <param name="sentences">The sentences that <see cref="ReadNamedSentences"/> returned.</param>
    /// <param name="table">The table that holds the row.</param>
    /// <param name="row">The row.</param>
    /// <param name="span">The number of tokens of the row that must exist, from the token index on.</param>
    /// <returns>The token row of the corpus, or <c>null</c> with the problem.</returns>
    public static (string[]? Token, string? Problem) FindToken(
        IReadOnlyDictionary<(string Source, string Split), Dictionary<string, CorpusSentence>> sentences,
        DataTable table,
        string[] row,
        int span)
    {
        var id = table.Cell(row, "sent_id");
        if (!sentences.TryGetValue((table.Cell(row, "source"), table.Cell(row, "split")), out var corpus) ||
            !corpus.TryGetValue(id, out var sentence))
        {
            return (null, $"{id}: no such sentence");
        }

        if (sentence.SourceId != table.Cell(row, "source_id"))
        {
            return (null, $"{id}: the document id is {sentence.SourceId}");
        }

        if (!int.TryParse(table.Cell(row, "token_index"), NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
            index > sentence.Tokens.Count - span)
        {
            return (null, $"{id}#{table.Cell(row, "token_index")}: outside the {sentence.Tokens.Count} tokens of the sentence");
        }

        var token = sentence.Tokens[index];
        return token[0] == table.Cell(row, "token") ? (token, null) : (null, $"{id}#{index}: the token is {token[0]}, not {table.Cell(row, "token")}");
    }

    /// <summary>
    ///     Determines whether a status cell is valid: a known status and the flags once each, in canonical order.
    /// </summary>
    /// <param name="cell">The cell.</param>
    /// <param name="status">The status without its flags.</param>
    /// <returns><c>true</c> for a valid cell.</returns>
    public static bool IsStatus(string cell, out string status)
    {
        var parts = cell.Split('+');
        status = parts[0];
        if (Array.IndexOf(StatusBases, status) < 0)
        {
            return false;
        }

        var previous = -1;
        foreach (var flag in parts.Skip(1))
        {
            var position = Array.IndexOf(StatusFlags, flag);
            if (position <= previous)
            {
                return false;
            }

            previous = position;
        }

        return true;
    }

    /// <summary>
    ///     Finds the problems of the token columns of a corrections row.
    /// </summary>
    private static string? TokenError(DataTable table, string[] row)
    {
        var token = table.Cell(row, "token");
        var empty = token.Length == 0 || table.Cell(row, "sent_id").Length == 0 || table.Cell(row, "source_id").Length == 0;
        return empty || token.Any(char.IsWhiteSpace) ? "bad-token" : null;
    }

    /// <summary>
    ///     Finds the problems of the tag columns of a corrections row.
    /// </summary>
    private static string? TagError(DataTable table, string[] row)
    {
        foreach (var column in new[] { "old_tag", "new_tag" })
        {
            var tag = table.Cell(row, column);
            if (tag == "_")
            {
                return "masked-tag";
            }

            if (!DataFiles.UniversalTags.Contains(tag))
            {
                return "bad-tag";
            }
        }

        return null;
    }

    /// <summary>
    ///     Finds the problems of the status columns and the provenance of a corrections row.
    /// </summary>
    private static string? StatusError(DataTable table, string[] row)
    {
        var oldStatus = table.Cell(row, "old_status");
        var newStatus = table.Cell(row, "new_status");
        var provenance = table.Cell(row, "provenance");
        if (!IsStatus(oldStatus, out var oldBase) || !IsStatus(newStatus, out var newBase))
        {
            return "bad-status";
        }

        if (table.Cell(row, "old_tag") == table.Cell(row, "new_tag") && oldStatus == newStatus)
        {
            return "no-change";
        }

        if (oldBase != newBase && !(provenance.StartsWith("llm:", StringComparison.Ordinal) && newBase == "disputed"))
        {
            return "status-base-change";
        }

        if (provenance.StartsWith("rule:", StringComparison.Ordinal) && oldStatus != newStatus)
        {
            return "mechanical-status-change";
        }

        return ProvenancePattern.IsMatch(provenance) ? null : "bad-provenance";
    }

    /// <summary>
    ///     Finds the problem of one row of a table of the given kind, other than its order and its uniqueness.
    /// </summary>
    private static string? RowProblem(string kind, DataTable table, string[] row)
    {
        if (kind == "corrections")
        {
            return RowError(table, row);
        }

        var problem = AddressError(table, row) ?? TokenError(table, row);
        if (problem != null)
        {
            return problem;
        }

        return kind == "candidates" ? CandidateProblem(table, row) : ExceptionProblem(table, row);
    }

    /// <summary>
    ///     Finds the problems of the columns that are specific to a candidate row.
    /// </summary>
    private static string? CandidateProblem(DataTable table, string[] row)
    {
        if (!SpanPattern.IsMatch(table.Cell(row, "span_length")))
        {
            return "bad-span";
        }

        if (!DataFiles.UniversalTags.Contains(table.Cell(row, "current_tag")) || !IsStatus(table.Cell(row, "current_status"), out _))
        {
            return "bad-current-state";
        }

        return RuleIdPattern.IsMatch(table.Cell(row, "candidate_rule")) && table.Cell(row, "hint").Length > 0 ? null : "bad-rule-or-hint";
    }

    /// <summary>
    ///     Finds the problems of the columns that are specific to an exception row.
    /// </summary>
    private static string? ExceptionProblem(DataTable table, string[] row) =>
        RuleIdPattern.IsMatch(table.Cell(row, "rule")) && table.Cell(row, "reason").Length > 0 && ProvenancePattern.IsMatch(table.Cell(row, "provenance"))
            ? null
            : "bad-rule-reason-or-provenance";

    /// <summary>
    ///     Gets the key that must be unique in a table: the address of the token, and for candidates and exceptions
    ///     also the rule.
    /// </summary>
    private static string AddressKey(string kind, DataTable table, string[] row)
    {
        var rule = kind switch
        {
            "candidates" => table.Cell(row, "candidate_rule"),
            "exceptions" => table.Cell(row, "rule"),
            _ => string.Empty
        };
        return string.Join("|", table.Cell(row, "source"), table.Cell(row, "split"), table.Cell(row, "sent_id"), table.Cell(row, "token_index"), rule);
    }

    /// <summary>
    ///     Compares the keys of two rows: source, split, sentence id by the bytes of its UTF-8 encoding, token index.
    /// </summary>
    private static int CompareKeys(DataTable table, string[] left, string[] right)
    {
        var order = string.CompareOrdinal(table.Cell(left, "source"), table.Cell(right, "source"));
        order = order != 0 ? order : string.CompareOrdinal(table.Cell(left, "split"), table.Cell(right, "split"));
        if (order != 0)
        {
            return order;
        }

        var x = Encoding.UTF8.GetBytes(table.Cell(left, "sent_id"));
        var y = Encoding.UTF8.GetBytes(table.Cell(right, "sent_id"));
        for (var i = 0; i < Math.Min(x.Length, y.Length); i++)
        {
            if (x[i] != y[i])
            {
                return x[i] < y[i] ? -1 : 1;
            }
        }

        order = x.Length.CompareTo(y.Length);
        return order != 0
            ? order
            : long.Parse(table.Cell(left, "token_index"), CultureInfo.InvariantCulture)
                .CompareTo(long.Parse(table.Cell(right, "token_index"), CultureInfo.InvariantCulture));
    }
}
