using System.Text;

namespace DemaConsulting.PartOfSpeech.Trainer;

/// <summary>
///     Keeps the trainer away from the data that must stay unseen: evaluation reads exactly the two development files
///     until a validated development selection exists, and every held-out evaluation set is refused for training.
/// </summary>
/// <remarks>
///     <para>
///         The selection of every setting uses development data only. The held-out sets (the open test split, the gold v2
///         test split, the shape probe, the challenge set and the external treebank) are evaluated once, by the
///         <c>evaluate</c> command, and only behind a validated development selection: a rule in a document is easy to
///         forget, so the rule is code. The development readers accept an allow-list of two files; the held-out readers
///         take a <see cref="SelectedOperatingPoints"/> as a required parameter, and that object exists only for the frozen
///         model, the committed development files and the pre-registered rule. Training inputs are checked against a
///         deny-list that refuses the held-out and the development files, because training on them would invalidate the
///         selection and the evaluation.
///     </para>
///     <para>
///         The held-out names are spelled only in this file; a test scans the other trainer sources for them. The
///         class is stateless and thread-safe.
///     </para>
/// </remarks>
internal static class DataGuard
{
    /// <summary>
    ///     The only files that development evaluation may read, relative to the repository root with forward slashes.
    /// </summary>
    public static readonly IReadOnlyList<string> DevelopmentFiles = new[]
    {
        "data/open/dev.tsv",
        "data/eval/gold-v2/dev.tsv",
    };

    /// <summary>
    ///     The held-out files of the repository that the <c>evaluate</c> command may read, in the order of <see cref="HeldOutSet"/>.
    /// </summary>
    public static readonly IReadOnlyList<string> HeldOutFiles = new[]
    {
        "data/open/test.tsv",
        "data/eval/gold-v2/test.tsv",
        "data/eval/shapes/probe.tsv",
        "data/eval/ste-challenge/test.tsv",
    };

    /// <summary>
    ///     The default location of the cache of the external treebank, relative to the repository root.
    /// </summary>
    public const string UdCacheFolder = "tools/data/cache/ud-ewt";

    /// <summary>
    ///     The name of the pinned treebank file that the mapped table is made from.
    /// </summary>
    public const string UdSourceFile = "en_ewt-ud-test.conllu";

    /// <summary>
    ///     The name of the convention-mapped table of the treebank in the cache.
    /// </summary>
    public const string UdMappedFile = "ud_test.tsv";

    /// <summary>
    ///     The file of the pinned hashes of the treebank, relative to the repository root.
    /// </summary>
    public const string UdPinsFile = "tools/data/pins/ud-ewt.tsv";

    /// <summary>
    ///     The file names that no training source may have (held-out splits and the development splits).
    /// </summary>
    private static readonly string[] RefusedFileNames = { "test.tsv", "dev.tsv", "probe.tsv" };

    /// <summary>
    ///     The folder names below which no training source may be (evaluation data and external treebanks).
    /// </summary>
    private static readonly string[] RefusedFolderNames = { "eval", "shapes", "ste-challenge", "ud-ewt", "ewt", "en_ewt" };

    /// <summary>
    ///     Resolves a held-out evaluation input of the repository; the caller must hold a validated development selection.
    /// </summary>
    /// <param name="repositoryRoot">The full path of the repository root.</param>
    /// <param name="set">The held-out set.</param>
    /// <param name="selection">The validated development selection (proof that the selection was made before this read).</param>
    /// <returns>The full path of the held-out file.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <c>null</c>; there is no way to open a held-out file without a selection.</exception>
    public static string EnsureHeldOutFile(string repositoryRoot, HeldOutSet set, SelectedOperatingPoints selection)
    {
        ArgumentNullException.ThrowIfNull(repositoryRoot);
        ArgumentNullException.ThrowIfNull(selection);
        return Path.GetFullPath(Path.Combine(repositoryRoot, HeldOutFiles[(int)set].Replace('/', Path.DirectorySeparatorChar)));
    }

    /// <summary>
    ///     Checks that the header of a held-out file says it is the set that was asked for.
    /// </summary>
    /// <remarks>
    ///     This guards against a file that was copied or renamed to a held-out path, and a development split that was
    ///     mistaken for a held-out split.
    /// </remarks>
    /// <param name="lines">The lines of the file; only the leading lines are inspected.</param>
    /// <param name="set">The held-out set.</param>
    /// <param name="origin">The name of the file for the message.</param>
    /// <exception cref="InvalidDataException">The header does not announce the set.</exception>
    public static void EnsureHeldOutHeader(IEnumerable<string> lines, HeldOutSet set, string origin)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var marker = set switch
        {
            HeldOutSet.OpenTest => "test split",
            HeldOutSet.GoldTest => "test split",
            HeldOutSet.Challenge => "challenge set",
            _ => string.Empty,
        };
        foreach (var line in lines)
        {
            // The probe is a table with a header row; the corpora announce themselves in a comment
            if (set == HeldOutSet.Probe)
            {
                if (line.StartsWith("id\tshape\t", StringComparison.Ordinal))
                {
                    return;
                }

                break;
            }

            if (!line.StartsWith("#", StringComparison.Ordinal))
            {
                break;
            }

            if (line.StartsWith("# corpus", StringComparison.Ordinal) && line.Contains(marker))
            {
                return;
            }
        }

        throw new InvalidDataException($"{origin} does not announce itself as the expected held-out set in its header; refusing to read it.");
    }

    /// <summary>
    ///     Resolves a development evaluation input, which must be one of the two development files.
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
            $"'{path}' is not a development file. Development evaluation reads only {string.Join(" and ", DevelopmentFiles)}; held-out sets are read only by the evaluate command, behind a validated development selection.");
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
    ///     Checks that a folder for per-token output lies outside the repository, links resolved, so that held-out content can never land in the tree.
    /// </summary>
    /// <param name="repositoryRoot">The full path of the repository root.</param>
    /// <param name="directory">The folder, absolute or relative to the working directory.</param>
    /// <returns>The full path of the folder.</returns>
    /// <exception cref="InvalidOperationException">The folder is the repository root or lies inside it.</exception>
    public static string EnsureOutsideRepository(string repositoryRoot, string directory)
    {
        ArgumentNullException.ThrowIfNull(repositoryRoot);
        ArgumentNullException.ThrowIfNull(directory);
        var root = Canonicalize(repositoryRoot);
        var target = Canonicalize(directory);
        var same = string.Equals(root.TrimEnd(Path.DirectorySeparatorChar), target.TrimEnd(Path.DirectorySeparatorChar), Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        if (same || RelativeTo(root, target) != null || RelativeTo(Path.GetFullPath(repositoryRoot), Path.GetFullPath(directory)) != null)
        {
            throw new InvalidOperationException($"'{directory}' is inside the repository; the per-token dump holds held-out content and must be written outside it.");
        }

        return Path.GetFullPath(directory);
    }

    /// <summary>
    ///     Checks that the one held-out run has not been made yet: the results file must not exist.
    /// </summary>
    /// <param name="resultsPath">The full path of the results file.</param>
    /// <exception cref="InvalidOperationException">The results file exists.</exception>
    public static void EnsureNotEvaluated(string resultsPath)
    {
        ArgumentNullException.ThrowIfNull(resultsPath);
        if (File.Exists(resultsPath))
        {
            throw new InvalidOperationException($"{resultsPath} exists: the held-out evaluation was already run once. A repeat needs that file to be deleted on purpose (a visible change in the history) and a recorded reason.");
        }
    }

    /// <summary>
    ///     Checks that a path may be a training source: it must lie inside the <c>data</c> folder of the repository
    ///     once links are resolved, and it must be neither a held-out set nor a development file.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The check works on the canonical path: the path is made absolute (so <c>..</c> detours and rooted paths
    ///         are visible) and symbolic links and junctions are resolved where the runtime supports it, so that a
    ///         safe-looking name cannot point at held-out data. The path must be inside the data root both as written
    ///         and after link resolution; the file name and folder names are then checked below the data root, for both
    ///         spellings.
    ///     </para>
    ///     <para>
    ///         On .NET Framework (the test project compiles this file for it too) links are not resolved; the trainer
    ///         itself runs on a current .NET.
    ///     </para>
    /// </remarks>
    /// <param name="repositoryRoot">The full path of the repository root.</param>
    /// <param name="path">The path of the corpus, absolute or relative to the working directory.</param>
    /// <exception cref="InvalidOperationException">The path escapes the data root, names a held-out set, an evaluation folder or a development file, or goes through too many links.</exception>
    public static void EnsureTrainable(string repositoryRoot, string path)
    {
        ArgumentNullException.ThrowIfNull(repositoryRoot);
        ArgumentNullException.ThrowIfNull(path);

        var dataRoot = Path.Combine(repositoryRoot, "data");
        var written = RelativeTo(Path.GetFullPath(dataRoot), Path.GetFullPath(path));
        var resolved = RelativeTo(Canonicalize(dataRoot), Canonicalize(path));
        if (written == null || resolved == null)
        {
            throw new InvalidOperationException($"'{path}' is outside the data folder of the repository and cannot be used for training.");
        }

        if (IsRefused(written) || IsRefused(resolved))
        {
            throw new InvalidOperationException($"'{path}' is an evaluation or development file and cannot be used for training.");
        }
    }

    /// <summary>
    ///     Computes a path below a folder.
    /// </summary>
    /// <param name="root">The full path of the folder.</param>
    /// <param name="full">The full path to test.</param>
    /// <returns>The part of <paramref name="full"/> below <paramref name="root"/>, or <c>null</c> when it is not strictly below it.</returns>
    private static string? RelativeTo(string root, string full)
    {
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return full.Length > prefix.Length && full.StartsWith(prefix, comparison) ? full.Substring(prefix.Length) : null;
    }

    /// <summary>
    ///     Tells whether a path below the data folder names a refused file or lies below a refused folder.
    /// </summary>
    /// <param name="relative">The path below the data folder.</param>
    /// <returns><c>true</c> when the path is refused.</returns>
    private static bool IsRefused(string relative)
    {
        var segments = relative.Replace('\\', '/').Split('/');
        var name = segments[segments.Length - 1];
        var refused = Array.Exists(RefusedFileNames, f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase));
        for (var i = 0; i < segments.Length - 1 && !refused; i++)
        {
            refused = Array.Exists(RefusedFolderNames, f => string.Equals(f, segments[i], StringComparison.OrdinalIgnoreCase));
        }

        return refused;
    }

    /// <summary>
    ///     Makes a path absolute and resolves every symbolic link or junction on it (where the runtime supports links).
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The canonical full path; the path need not exist.</returns>
    /// <exception cref="InvalidOperationException">More than 40 links were followed (a link cycle).</exception>
    private static string Canonicalize(string path)
    {
        var full = Path.GetFullPath(path);
#if NET6_0_OR_GREATER
        const int MaxLinks = 40;
        var links = 0;
        var root = Path.GetPathRoot(full)!;
        var pending = new Queue<string>(Segments(full, root));
        var current = root;
        while (pending.Count > 0)
        {
            current = Path.Combine(current, pending.Dequeue());
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.LinkTarget == null)
            {
                continue;
            }

            if (++links > MaxLinks)
            {
                throw new InvalidOperationException($"'{path}' goes through too many links.");
            }

            // Continue below the final target, whose own parents may contain links again
            var target = Path.GetFullPath(info.ResolveLinkTarget(true)?.FullName ?? Path.Combine(Path.GetDirectoryName(current) ?? root, info.LinkTarget));
            root = Path.GetPathRoot(target)!;
            pending = new Queue<string>(Segments(target, root).Concat(pending.ToArray()));
            current = root;
        }

        return current;
#else
        return full;
#endif
    }

#if NET6_0_OR_GREATER
    /// <summary>
    ///     Splits a full path below its root into its folder and file names.
    /// </summary>
    /// <param name="full">The full path.</param>
    /// <param name="root">The root of the path.</param>
    /// <returns>The names in order.</returns>
    private static string[] Segments(string full, string root) =>
        full.Substring(root.Length).Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
#endif
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
    /// <param name="lemma">The lemma of the target as the table gives it, or an empty string when the table has no lemma column or no lemma.</param>
    /// <param name="status">The adjudication status of the row (<c>agreed</c> or <c>adjudicated</c>), or an empty string.</param>
    /// <param name="agreement">The agreement of the labelers (<c>full</c>, <c>analysis</c>, <c>pos</c> or <c>none</c>), or an empty string.</param>
    /// <param name="group">The group of the row for per-group reporting (the shape of a probe row), or an empty string.</param>
    public GoldTarget(string id, string documentId, string[] tokens, int target, string goldClass, string lemma = "", string status = "", string agreement = "", string group = "")
    {
        Id = id;
        DocumentId = documentId;
        Tokens = tokens;
        Target = target;
        GoldClass = goldClass;
        Lemma = lemma;
        Status = status;
        Agreement = agreement;
        Group = group;
    }

    /// <summary>
    ///     Gets the adjudication status of the row, or an empty string.
    /// </summary>
    public string Status { get; }

    /// <summary>
    ///     Gets the agreement of the labelers, or an empty string.
    /// </summary>
    public string Agreement { get; }

    /// <summary>
    ///     Gets the group of the row for per-group reporting (the shape of a probe row), or an empty string.
    /// </summary>
    public string Group { get; }

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
    ///     Gets the lemma of the target (a user lists lemmas in a glossary); empty when the table gives none.
    /// </summary>
    public string Lemma { get; }

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
    /// <exception cref="InvalidDataException">A required column is missing, a row is malformed or the target index is negative or outside the sentence.</exception>
    public static List<GoldTarget> ParseGoldDev(IEnumerable<string> lines, string origin) => ParseGold(lines, origin);

    /// <summary>
    ///     Parses the lines of a gold v2 table (development or held-out): a <c># columns =</c> comment names the tab-separated columns.
    /// </summary>
    /// <param name="lines">The lines.</param>
    /// <param name="origin">The origin for error messages.</param>
    /// <returns>The targets in file order.</returns>
    /// <exception cref="InvalidDataException">A required column is missing, a row is malformed or the target index is negative or outside the sentence.</exception>
    public static List<GoldTarget> ParseGold(IEnumerable<string> lines, string origin)
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
            var targetText = Cell(cells, columns, "target", origin);
            if (!int.TryParse(targetText, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var target))
            {
                throw new InvalidDataException($"{origin}: the target of row {Cell(cells, columns, "id", origin)} is not an integer.");
            }

            if (target < 0 || target >= tokens.Length)
            {
                throw new InvalidDataException($"{origin}: the target of row {Cell(cells, columns, "id", origin)} is outside its sentence (index {target} of {tokens.Length} tokens; it must not be negative).");
            }

            result.Add(new GoldTarget(
                Cell(cells, columns, "id", origin),
                Cell(cells, columns, "doc_id", origin),
                tokens,
                target,
                Cell(cells, columns, "pos", origin),
                OptionalCell(cells, columns, "lemma"),
                OptionalCell(cells, columns, "status"),
                OptionalCell(cells, columns, "agreement")));
        }

        return result;
    }

    /// <summary>
    ///     Gets the value of a named column of a row, or an empty string when the table has no such column or the cell holds the placeholder <c>-</c>.
    /// </summary>
    /// <param name="cells">The cells of the row.</param>
    /// <param name="columns">The column names.</param>
    /// <param name="name">The column to read.</param>
    /// <returns>The cell, or an empty string.</returns>
    private static string OptionalCell(string[] cells, string[] columns, string name)
    {
        var index = Array.IndexOf(columns, name);
        return index >= 0 && cells[index] != "-" ? cells[index] : string.Empty;
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

/// <summary>
///     The held-out evaluation sets of the repository.
/// </summary>
internal enum HeldOutSet
{
    /// <summary>
    ///     The test split of the open corpus.
    /// </summary>
    OpenTest = 0,

    /// <summary>
    ///     The test split of gold v2.
    /// </summary>
    GoldTest = 1,

    /// <summary>
    ///     The noun-verb shape probe.
    /// </summary>
    Probe = 2,

    /// <summary>
    ///     The simplified-technical-English challenge set.
    /// </summary>
    Challenge = 3,
}

/// <summary>
///     The content of a held-out file together with the hashes that identify it.
/// </summary>
internal sealed class HeldOutFile
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="HeldOutFile"/> class.
    /// </summary>
    /// <param name="path">The full path of the file.</param>
    /// <param name="sha256">The SHA-256 of the file.</param>
    /// <param name="lines">The lines of the file.</param>
    /// <param name="sourceSha256">The SHA-256 of the pinned source the file was made from, or an empty string.</param>
    public HeldOutFile(string path, string sha256, IReadOnlyList<string> lines, string sourceSha256 = "")
    {
        Path = path;
        Sha256 = sha256;
        Lines = lines;
        SourceSha256 = sourceSha256;
    }

    /// <summary>
    ///     Gets the full path of the file.
    /// </summary>
    public string Path { get; }

    /// <summary>
    ///     Gets the SHA-256 of the file.
    /// </summary>
    public string Sha256 { get; }

    /// <summary>
    ///     Gets the lines of the file.
    /// </summary>
    public IReadOnlyList<string> Lines { get; }

    /// <summary>
    ///     Gets the SHA-256 of the pinned source file the file was made from, or an empty string.
    /// </summary>
    public string SourceSha256 { get; }
}

/// <summary>
///     Opens the held-out files, and only for a caller that holds a validated development selection.
/// </summary>
/// <remarks>
///     <para>
///         Every read verifies the SHA-256 of the file against the data manifest (the repository sets) or the pins of the
///         treebank, and the header of the file, before a line reaches a parser. The held-out names are spelled in this
///         file and nowhere else in the trainer. Stateless and thread-safe.
///     </para>
/// </remarks>
internal static class HeldOutReader
{
    /// <summary>
    ///     Reads a held-out file of the repository.
    /// </summary>
    /// <param name="repositoryRoot">The full path of the repository root.</param>
    /// <param name="set">The set.</param>
    /// <param name="selection">The validated development selection.</param>
    /// <returns>The file.</returns>
    /// <exception cref="InvalidDataException">The hash differs from the data manifest or the header is not the one of the set.</exception>
    public static HeldOutFile Read(string repositoryRoot, HeldOutSet set, SelectedOperatingPoints selection)
    {
        var path = DataGuard.EnsureHeldOutFile(repositoryRoot, set, selection);
        var bytes = File.ReadAllBytes(path);
        var sha = ModelBuilder.Sha256Hex(bytes);
        var relative = DataGuard.HeldOutFiles[(int)set].Substring("data/".Length);
        var expected = DataManifest.Load(repositoryRoot).TryGetValue(relative, out var recorded) ? recorded : string.Empty;
        if (!string.Equals(sha, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"{path} does not match the SHA-256 of the data manifest; refusing to evaluate on a changed file.");
        }

        var lines = Split(bytes);
        DataGuard.EnsureHeldOutHeader(lines.Take(20), set, path);
        return new HeldOutFile(path, sha, lines);
    }

    /// <summary>
    ///     Reads the convention-mapped table of the external treebank from its cache, after verifying the pinned source file.
    /// </summary>
    /// <param name="repositoryRoot">The full path of the repository root.</param>
    /// <param name="cacheFolder">The cache folder, absolute or relative to the repository root.</param>
    /// <param name="selection">The validated development selection.</param>
    /// <returns>The mapped table.</returns>
    /// <exception cref="InvalidDataException">The cache or the pins are missing, or a hash differs from the pin.</exception>
    public static HeldOutFile ReadUd(string repositoryRoot, string cacheFolder, SelectedOperatingPoints selection)
    {
        ArgumentNullException.ThrowIfNull(repositoryRoot);
        ArgumentNullException.ThrowIfNull(selection);
        var folder = Path.GetFullPath(Path.Combine(repositoryRoot, cacheFolder.Replace('/', Path.DirectorySeparatorChar)));
        var sourcePath = Path.Combine(folder, UdSource);
        var mappedPath = Path.Combine(folder, DataGuard.UdMappedFile);
        const string Recipe = "Run: python tools/data/fetch_ud_ewt.py fetch, then python tools/data/fetch_ud_ewt.py map.";
        if (!File.Exists(sourcePath) || !File.Exists(mappedPath))
        {
            throw new InvalidDataException($"The treebank cache {folder} is missing the pinned source or the mapped table. {Recipe}");
        }

        // The pinned source decides which treebank text the mapped table may have been made from
        var pins = Path.Combine(repositoryRoot, DataGuard.UdPinsFile.Replace('/', Path.DirectorySeparatorChar));
        var pinned = File.ReadLines(pins)
            .Where(l => l.Length > 0 && l[0] != '#')
            .Select(l => l.Split('\t'))
            .Where(c => c.Length >= 2 && c[0] == UdSource)
            .Select(c => c[1])
            .FirstOrDefault() ?? string.Empty;
        var sourceSha = ModelBuilder.Sha256Hex(File.ReadAllBytes(sourcePath));
        if (!string.Equals(sourceSha, pinned, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"The cached {UdSource} does not match its pinned SHA-256. {Recipe}");
        }

        var bytes = File.ReadAllBytes(mappedPath);
        return new HeldOutFile(mappedPath, ModelBuilder.Sha256Hex(bytes), Split(bytes), sourceSha);
    }

    /// <summary>
    ///     Gets the name of the pinned treebank source file.
    /// </summary>
    private static string UdSource => DataGuard.UdSourceFile;

    /// <summary>
    ///     Splits the bytes of a UTF-8 text file into lines.
    /// </summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The lines without line terminators.</returns>
    private static List<string> Split(byte[] bytes)
    {
        var lines = new List<string>();
        using var reader = new StringReader(new UTF8Encoding(false).GetString(bytes));
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return lines;
    }
}
