using System.Text;

namespace DemaConsulting.PartOfSpeech.Trainer;

/// <summary>
///     Keeps the trainer away from the data that must stay unseen: it admits exactly the two development files for
///     evaluation and refuses every held-out evaluation set for training.
/// </summary>
/// <remarks>
///     <para>
///         The selection of every setting uses development data only, and the held-out sets (the open test split, the
///         gold v2 test split, the shape probe, the challenge set and the external treebank) are evaluated once, in a
///         later phase. A rule in a document is easy to forget, so the rule is code: there is no path through the
///         trainer that opens one of those files. Evaluation inputs are an allow-list of two files; training inputs are
///         checked against a deny-list that also refuses the development files, because training on them would
///         invalidate the selection.
///     </para>
///     <para>
///         The held-out names are spelled only in this file; a test scans the other trainer sources for them. The
///         class is stateless and thread-safe.
///     </para>
/// </remarks>
internal static class DataGuard
{
    /// <summary>
    ///     The only files that evaluation may read, relative to the repository root with forward slashes.
    /// </summary>
    public static readonly IReadOnlyList<string> DevelopmentFiles = new[]
    {
        "data/open/dev.tsv",
        "data/eval/gold-v2/dev.tsv",
    };

    /// <summary>
    ///     The file names that no training source may have (held-out splits and the development splits).
    /// </summary>
    private static readonly string[] RefusedFileNames = { "test.tsv", "dev.tsv", "probe.tsv" };

    /// <summary>
    ///     The folder names below which no training source may be (evaluation data and external treebanks).
    /// </summary>
    private static readonly string[] RefusedFolderNames = { "eval", "shapes", "ste-challenge", "ud-ewt", "ewt", "en_ewt" };

    /// <summary>
    ///     Resolves an evaluation input, which must be one of the two development files.
    /// </summary>
    /// <param name="repositoryRoot">The full path of the repository root.</param>
    /// <param name="path">The path that was asked for, absolute or relative to the working directory.</param>
    /// <returns>The full path of the development file.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="path"/> is not one of <see cref="DevelopmentFiles"/>.</exception>
    public static string EnsureDevelopmentFile(string repositoryRoot, string path)
    {
        ArgumentNullException.ThrowIfNull(repositoryRoot);
        ArgumentNullException.ThrowIfNull(path);

        // Compare full paths, so that a relative spelling or a dotted detour cannot slip past the allow-list
        var full = Path.GetFullPath(path);
        foreach (var allowed in DevelopmentFiles)
        {
            var allowedFull = Path.GetFullPath(Path.Combine(repositoryRoot, allowed.Replace('/', Path.DirectorySeparatorChar)));
            if (string.Equals(full, allowedFull, StringComparison.OrdinalIgnoreCase))
            {
                return allowedFull;
            }
        }

        throw new InvalidOperationException(
            $"'{path}' is not a development file. Evaluation reads only {string.Join(" and ", DevelopmentFiles)}; held-out sets are evaluated once, in a later phase.");
    }

    /// <summary>
    ///     Checks that the header of a development file says it is a development split.
    /// </summary>
    /// <remarks>
    ///     This guards against a held-out file that was copied or renamed to a development path.
    /// </remarks>
    /// <param name="lines">The lines of the file; only the leading comment lines are inspected.</param>
    /// <param name="origin">The name of the file for the message.</param>
    /// <exception cref="InvalidDataException">The header does not announce a development split.</exception>
    public static void EnsureDevelopmentHeader(IEnumerable<string> lines, string origin)
    {
        foreach (var line in lines)
        {
            if (!line.StartsWith("#", StringComparison.Ordinal))
            {
                break;
            }

            if (line.StartsWith("# corpus", StringComparison.Ordinal) && line.Contains("dev split"))
            {
                return;
            }
        }

        throw new InvalidDataException($"{origin} does not announce itself as a development split in its header; refusing to read it.");
    }

    /// <summary>
    ///     Checks that a path may be a training source: it must be neither a held-out set nor a development file.
    /// </summary>
    /// <param name="path">The path of the corpus.</param>
    /// <exception cref="InvalidOperationException">The path names a held-out set, an evaluation folder or a development file.</exception>
    public static void EnsureTrainable(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var segments = path.Replace('\\', '/').Split('/');
        var name = segments[segments.Length - 1];
        var refused = Array.Exists(RefusedFileNames, f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase));
        for (var i = 0; i < segments.Length - 1 && !refused; i++)
        {
            refused = Array.Exists(RefusedFolderNames, f => string.Equals(f, segments[i], StringComparison.OrdinalIgnoreCase));
        }

        if (refused)
        {
            throw new InvalidOperationException($"'{path}' is an evaluation or development file and cannot be used for training.");
        }
    }
}

/// <summary>
///     One target token of the gold v2 development set: a sentence, the index of the word to judge and its gold class.
/// </summary>
internal sealed class GoldTarget
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="GoldTarget"/> class.
    /// </summary>
    /// <param name="id">The id of the row.</param>
    /// <param name="documentId">The id of the document of the sentence.</param>
    /// <param name="tokens">The tokens of the sentence.</param>
    /// <param name="target">The index of the target token.</param>
    /// <param name="goldClass">The gold class name (Noun, Verb, and so on).</param>
    public GoldTarget(string id, string documentId, string[] tokens, int target, string goldClass)
    {
        Id = id;
        DocumentId = documentId;
        Tokens = tokens;
        Target = target;
        GoldClass = goldClass;
    }

    /// <summary>
    ///     Gets the id of the row.
    /// </summary>
    public string Id { get; }

    /// <summary>
    ///     Gets the id of the document of the sentence.
    /// </summary>
    public string DocumentId { get; }

    /// <summary>
    ///     Gets the tokens of the sentence.
    /// </summary>
    public string[] Tokens { get; }

    /// <summary>
    ///     Gets the index of the target token.
    /// </summary>
    public int Target { get; }

    /// <summary>
    ///     Gets the gold class of the target.
    /// </summary>
    public string GoldClass { get; }

    /// <summary>
    ///     Gets the target token as written.
    /// </summary>
    public string Form => Tokens[Target];

    /// <summary>
    ///     Gets a value indicating whether the target is <c>to</c> or <c>not</c>, whose gold class is a convention difference that is reported separately.
    /// </summary>
    public bool IsConventionWord =>
        string.Equals(Form, "to", StringComparison.OrdinalIgnoreCase) || string.Equals(Form, "not", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
///     Loads the development sets (the only evaluation inputs of the trainer) behind <see cref="DataGuard"/>.
/// </summary>
/// <remarks>
///     The loaders are split into a guarded file reader and a parser over lines so that tests can parse fixtures
///     without any file; the parsers still need no guard, because they never open a file. Stateless and thread-safe.
/// </remarks>
internal static class DevSets
{
    /// <summary>
    ///     The class that a UPOS tag has in the gold v2 set (the table of <c>data/CONVENTIONS.md</c>).
    /// </summary>
    /// <param name="tag">The tag.</param>
    /// <returns>The class name.</returns>
    public static string GoldClassOf(Upos tag)
    {
        switch (tag)
        {
            case Upos.Noun:
            case Upos.Propn:
                return "Noun";
            case Upos.Verb:
                return "Verb";
            case Upos.Aux:
                return "Auxiliary";
            case Upos.Adj:
                return "Adjective";
            case Upos.Adv:
                return "Adverb";
            case Upos.Adp:
                return "Preposition";
            case Upos.Cconj:
            case Upos.Sconj:
                return "Conjunction";
            case Upos.Det:
                return "Determiner";
            case Upos.Pron:
                return "Pronoun";
            case Upos.Intj:
                return "Interjection";
            default:
                return "Other";
        }
    }

    /// <summary>
    ///     Reads the open development split.
    /// </summary>
    /// <param name="repositoryRoot">The full path of the repository root.</param>
    /// <returns>The sentences with the gold tag of every token (disputed tokens included).</returns>
    /// <exception cref="InvalidOperationException">The file is not the development file (cannot happen for the fixed path).</exception>
    /// <exception cref="InvalidDataException">The file does not announce a development split or is malformed.</exception>
    public static List<TrainingSentence> ReadOpenDev(string repositoryRoot)
    {
        var path = DataGuard.EnsureDevelopmentFile(repositoryRoot, Path.Combine(repositoryRoot, "data", "open", "dev.tsv"));
        DataGuard.EnsureDevelopmentHeader(File.ReadLines(path, Encoding.UTF8).Take(20), path);
        return ParseOpenDev(File.ReadLines(path, Encoding.UTF8), path);
    }

    /// <summary>
    ///     Parses the lines of an open development split.
    /// </summary>
    /// <param name="lines">The lines.</param>
    /// <param name="origin">The origin for error messages.</param>
    /// <returns>The sentences; no token is masked, so every token is judged.</returns>
    public static List<TrainingSentence> ParseOpenDev(IEnumerable<string> lines, string origin) =>
        CorpusReader.Parse(lines, new SourceConfig { Name = "dev", MaskDisputed = false }, origin);

    /// <summary>
    ///     Reads the gold v2 development split.
    /// </summary>
    /// <param name="repositoryRoot">The full path of the repository root.</param>
    /// <returns>The targets in file order.</returns>
    /// <exception cref="InvalidDataException">The file does not announce a development split or is malformed.</exception>
    public static List<GoldTarget> ReadGoldDev(string repositoryRoot)
    {
        var path = DataGuard.EnsureDevelopmentFile(repositoryRoot, Path.Combine(repositoryRoot, "data", "eval", "gold-v2", "dev.tsv"));
        DataGuard.EnsureDevelopmentHeader(File.ReadLines(path, Encoding.UTF8).Take(20), path);
        return ParseGoldDev(File.ReadLines(path, Encoding.UTF8), path);
    }

    /// <summary>
    ///     Parses the lines of a gold v2 table: a <c># columns =</c> comment names the tab-separated columns.
    /// </summary>
    /// <param name="lines">The lines.</param>
    /// <param name="origin">The origin for error messages.</param>
    /// <returns>The targets in file order.</returns>
    /// <exception cref="InvalidDataException">A required column is missing, a row is malformed or the target index is outside the sentence.</exception>
    public static List<GoldTarget> ParseGoldDev(IEnumerable<string> lines, string origin)
    {
        ArgumentNullException.ThrowIfNull(lines);
        string[]? columns = null;
        var result = new List<GoldTarget>();
        foreach (var line in lines)
        {
            if (line.StartsWith("# columns = ", StringComparison.Ordinal))
            {
                columns = line.Substring("# columns = ".Length).Split(' ');
                continue;
            }

            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            if (columns == null)
            {
                throw new InvalidDataException(origin + ": the columns comment is missing.");
            }

            var cells = line.Split('\t');
            if (cells.Length != columns.Length)
            {
                throw new InvalidDataException($"{origin}: a row has {cells.Length} cells but the table has {columns.Length} columns.");
            }

            var tokens = Cell(cells, columns, "sentence", origin).Split(' ');
            if (!int.TryParse(Cell(cells, columns, "target", origin), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var target) || target >= tokens.Length)
            {
                throw new InvalidDataException($"{origin}: the target of row {Cell(cells, columns, "id", origin)} is outside its sentence.");
            }

            result.Add(new GoldTarget(
                Cell(cells, columns, "id", origin),
                Cell(cells, columns, "doc_id", origin),
                tokens,
                target,
                Cell(cells, columns, "pos", origin)));
        }

        return result;
    }

    /// <summary>
    ///     Gets the value of a named column of a row.
    /// </summary>
    /// <param name="cells">The cells of the row.</param>
    /// <param name="columns">The column names.</param>
    /// <param name="name">The column to read.</param>
    /// <param name="origin">The origin for the error message.</param>
    /// <returns>The cell.</returns>
    /// <exception cref="InvalidDataException">The table has no such column.</exception>
    private static string Cell(string[] cells, string[] columns, string name, string origin)
    {
        var index = Array.IndexOf(columns, name);
        return index >= 0 ? cells[index] : throw new InvalidDataException($"{origin}: the column '{name}' is missing.");
    }
}
