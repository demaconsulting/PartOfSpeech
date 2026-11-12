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
///         forget, so the rule is code. The development readers accept an allow-list of two files; the files of the
///         hard-shape development set (its tranches) have their own guard (<see cref="EnsureShapesDevelopmentFile"/>) that only the
///         <c>eval-shapes</c> command uses. The held-out readers
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
    ///     The hard-shape development file, relative to the repository root with forward slashes. It is a third development
    ///     file with its own guard (<see cref="EnsureShapesDevelopmentFile"/>); it is deliberately not in
    ///     <see cref="DevelopmentFiles"/>, so no command that reads "the two development files" can reach it by accident, and
    ///     it is not a held-out file (<see cref="HeldOutFiles"/>), so <c>evaluate</c> never reads it.
    /// </summary>
    public const string ShapesDevelopmentFile = "data/eval/shapes-dev/dev.tsv";

    /// <summary>
    ///     The hard tranche of the hard-shape development set, relative to the repository root with forward slashes. It is
    ///     the second file of the same set (the standard tranche is <see cref="ShapesDevelopmentFile"/>), cut from the same
    ///     federal documents with a different, pre-registered selection, and it shares the guard of the first file
    ///     (<see cref="EnsureShapesDevelopmentFile"/>): it is not in <see cref="DevelopmentFiles"/> or <see cref="HeldOutFiles"/>,
    ///     and below the <c>eval</c> folder, so the training deny-list refuses it.
    /// </summary>
    public const string ShapesDevelopmentHardFile = "data/eval/shapes-dev/hard.tsv";

    /// <summary>
    ///     The compound-head tranche of the hard-shape development set (shape <c>n-compound-head-ambiguous</c>), relative to the
    ///     repository root with forward slashes. It shares the guard of the other tranches and, like them, is neither a
    ///     two-file development file nor a held-out file; it may be absent.
    /// </summary>
    public const string ShapesDevelopmentCompoundHeadFile = "data/eval/shapes-dev/compound-head.tsv";

    /// <summary>
    ///     The headings tranche of the hard-shape development set (shape <c>heading-titlecase</c>, scored by exact tag), relative to
    ///     the repository root with forward slashes. It shares the guard of the other tranches; it may be absent.
    /// </summary>
    public const string ShapesDevelopmentHeadingsFile = "data/eval/shapes-dev/headings.tsv";

    /// <summary>
    ///     The named-state tranche of the hard-shape development set (shape <c>n-named-state</c>), relative to the repository root
    ///     with forward slashes. It shares the guard of the other tranches; it may be absent.
    /// </summary>
    public const string ShapesDevelopmentNamedStateFile = "data/eval/shapes-dev/named-state.tsv";

    /// <summary>
    ///     The second compound-head tranche of the hard-shape development set (shape <c>n-compound-head-ambiguous</c>, the same shape as
    ///     <see cref="ShapesDevelopmentCompoundHeadFile"/>), relative to the repository root with forward slashes. It shares the guard
    ///     of the other tranches; it may be absent.
    /// </summary>
    public const string ShapesDevelopmentCompoundHead2File = "data/eval/shapes-dev/compound-head-2.tsv";

    /// <summary>
    ///     The second headings tranche of the hard-shape development set (shape <c>heading-titlecase</c>, scored by exact tag, the same
    ///     shape as <see cref="ShapesDevelopmentHeadingsFile"/>), relative to the repository root with forward slashes. It shares the
    ///     guard of the other tranches; it may be absent.
    /// </summary>
    public const string ShapesDevelopmentHeadings2File = "data/eval/shapes-dev/headings-2.tsv";

    /// <summary>
    ///     The deverbal-heads tranche of the hard-shape development set (shape <c>n-deverbal-head</c>, scored by class), relative to the
    ///     repository root with forward slashes. It shares the guard of the other tranches; it may be absent.
    /// </summary>
    public const string ShapesDevelopmentDeverbalHeadsFile = "data/eval/shapes-dev/deverbal-heads.tsv";

    /// <summary>
    ///     The defined-terms tranche of the hard-shape development set (shape <c>term-capitalized</c>, scored by exact tag), relative to
    ///     the repository root with forward slashes. It shares the guard of the other tranches; it may be absent.
    /// </summary>
    public const string ShapesDevelopmentDefinedTermsFile = "data/eval/shapes-dev/defined-terms.tsv";

    /// <summary>
    ///     The step-lines tranche of the hard-shape development set (shape <c>step-line</c>, scored by exact tag), relative to the
    ///     repository root with forward slashes. It shares the guard of the other tranches; it may be absent.
    /// </summary>
    public const string ShapesDevelopmentStepLinesFile = "data/eval/shapes-dev/step-lines.tsv";

    /// <summary>
    ///     The clause-verbs tranche of the hard-shape development set (shape <c>v-clause-ambiguous</c>, scored by exact tag), relative to
    ///     the repository root with forward slashes. It shares the guard of the other tranches; it may be absent.
    /// </summary>
    public const string ShapesDevelopmentClauseVerbsFile = "data/eval/shapes-dev/clause-verbs.tsv";

    /// <summary>
    ///     The files of the hard-shape development set that the shapes guard admits, in the order of <see cref="ShapesDevTranche"/>.
    ///     The sources files and the leakage reports of the tranches are not listed, so the guard refuses them.
    /// </summary>
    public static readonly IReadOnlyList<string> ShapesDevelopmentFiles = new[]
    {
        ShapesDevelopmentFile,
        ShapesDevelopmentHardFile,
        ShapesDevelopmentCompoundHeadFile,
        ShapesDevelopmentHeadingsFile,
        ShapesDevelopmentNamedStateFile,
        ShapesDevelopmentCompoundHead2File,
        ShapesDevelopmentHeadings2File,
        ShapesDevelopmentDeverbalHeadsFile,
        ShapesDevelopmentDefinedTermsFile,
        ShapesDevelopmentStepLinesFile,
        ShapesDevelopmentClauseVerbsFile,
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
    ///     The pinned SHA-256 of the convention-mapped table of the treebank, as recorded for the held-out run in
    ///     <c>docs/evaluation/test-results.json</c> (input <c>ud-test-mapped</c>). The cached table is verified against it
    ///     before a line is parsed, so a stale or locally edited cache cannot be scored.
    /// </summary>
    public const string UdMappedSha256 = "fdb535bdbbc4ece79826323e450fc30a686543358c0f0c958deb8289850f6695";

    /// <summary>
    ///     The name of the convention-mapped table of the development part of the external treebank in the cache; the dev-only
    ///     <c>eval-arm</c> command reads exactly this file and no other part of the treebank.
    /// </summary>
    public const string UdDevMappedFile = "ud_dev.tsv";

    /// <summary>
    ///     The pinned SHA-256 of the convention-mapped table of the development part of the external treebank (recorded in the run log
    ///     of the context-feature experiment). The table is verified against it before a line is parsed.
    /// </summary>
    public const string UdDevMappedSha256 = "0fc0b4a371ef6d0437e01e396762cdb600d0d7ff0d725ead00ac9f3c41f82037";

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
    /// <remarks>
    ///     Like <see cref="EnsureShapesDevelopmentFile"/>, the canonical path of the file (every symbolic link or junction
    ///     resolved, where the runtime supports links) must equal the canonical repository root plus the allowed relative file;
    ///     a link at or below the data folder is refused.
    /// </remarks>
    /// <exception cref="InvalidOperationException"><paramref name="path"/> is not one of <see cref="DevelopmentFiles"/>, or a link lies on its way.</exception>
    public static string EnsureDevelopmentFile(string repositoryRoot, string path)
    {
        ArgumentNullException.ThrowIfNull(repositoryRoot);
        ArgumentNullException.ThrowIfNull(path);

        // Compare full paths, so that a relative spelling or a dotted detour cannot slip past the allow-list
        var full = Path.GetFullPath(path);
        foreach (var allowed in DevelopmentFiles)
        {
            var relative = allowed.Replace('/', Path.DirectorySeparatorChar);
            var allowedFull = Path.GetFullPath(Path.Combine(repositoryRoot, relative));
            if (string.Equals(full, allowedFull, StringComparison.OrdinalIgnoreCase))
            {
                EnsureNoLink(repositoryRoot, relative, allowedFull, path, $"the development file {allowed}");
                return allowedFull;
            }
        }

        throw new InvalidOperationException(
            $"'{path}' is not a development file. Development evaluation reads only {string.Join(" and ", DevelopmentFiles)}; held-out sets are read only by the evaluate command, behind a validated development selection.");
    }

    /// <summary>
    ///     Resolves a hard-shape development file, which must be exactly one of <see cref="ShapesDevelopmentFiles"/>.
    /// </summary>
    /// <param name="repositoryRoot">The full path of the repository root.</param>
    /// <param name="path">The path that was asked for, absolute or relative to the working directory.</param>
    /// <returns>The full path of the hard-shape development file.</returns>
    /// <remarks>
    ///     The file and every folder on its way must be real: a symbolic link or junction that replaces any of them is refused
    ///     (where the runtime supports links), so that the written path cannot lead to other content.
    /// </remarks>
    /// <exception cref="InvalidOperationException"><paramref name="path"/> is not a file of the hard-shape development set, or a link lies on its way.</exception>
    public static string EnsureShapesDevelopmentFile(string repositoryRoot, string path)
    {
        ArgumentNullException.ThrowIfNull(repositoryRoot);
        ArgumentNullException.ThrowIfNull(path);
        var full = Path.GetFullPath(path);
        foreach (var allowed in ShapesDevelopmentFiles)
        {
            var relative = allowed.Replace('/', Path.DirectorySeparatorChar);
            var allowedFull = Path.GetFullPath(Path.Combine(repositoryRoot, relative));
            if (string.Equals(full, allowedFull, StringComparison.OrdinalIgnoreCase))
            {
                EnsureNoLink(repositoryRoot, relative, allowedFull, path, $"the shapes development file {allowed}");
                return allowedFull;
            }
        }

        throw new InvalidOperationException($"'{path}' is not a shapes development file; the shapes evaluation reads only {string.Join(", ", ShapesDevelopmentFiles)}.");
    }

    /// <summary>
    ///     Refuses a development file whose canonical path is not the canonical repository root plus the allowed relative file.
    /// </summary>
    /// <param name="repositoryRoot">The full path of the repository root.</param>
    /// <param name="relative">The allowed file, relative to the repository root.</param>
    /// <param name="allowedFull">The full path of the allowed file.</param>
    /// <param name="path">The path that was asked for, for the message.</param>
    /// <param name="description">The description of the file for the message.</param>
    /// <exception cref="InvalidOperationException">The file or a folder at or below the data folder is a link.</exception>
    private static void EnsureNoLink(string repositoryRoot, string relative, string allowedFull, string path, string description)
    {
        // Links in the repository root itself are tolerated; a link at or below the data folder moves the canonical path away from the expected one
        var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var expected = Path.Combine(Canonicalize(repositoryRoot), relative);
        if (!string.Equals(Canonicalize(allowedFull), expected, comparison))
        {
            throw new InvalidOperationException($"'{path}' goes through a link; {description} must be a real file below real folders.");
        }
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
    ///     Checks that a file given to a general-purpose command lies outside the data folder of the repository, links resolved, so
    ///     that the command can never read or overwrite training, development or held-out data.
    /// </summary>
    /// <param name="repositoryRoot">The full path of the repository root.</param>
    /// <param name="path">The file, absolute or relative to the working directory.</param>
    /// <returns>The full path of the file.</returns>
    /// <exception cref="InvalidOperationException">The path is the data folder or lies inside it, as written or after link resolution.</exception>
    public static string EnsureOutsideData(string repositoryRoot, string path)
    {
        ArgumentNullException.ThrowIfNull(repositoryRoot);
        ArgumentNullException.ThrowIfNull(path);
        var dataRoot = Path.Combine(repositoryRoot, "data");
        var same = string.Equals(
            Path.GetFullPath(dataRoot).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar),
            Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        if (same || RelativeTo(Canonicalize(dataRoot), Canonicalize(path)) != null || RelativeTo(Path.GetFullPath(dataRoot), Path.GetFullPath(path)) != null)
        {
            throw new InvalidOperationException($"'{path}' is inside the data folder of the repository; this command reads and writes files outside it only.");
        }

        return Path.GetFullPath(path);
    }

    /// <summary>
    ///     Checks that a file given to the dev-only <c>eval-arm</c> command (the weights to read, the result to write) lies neither in the
    ///     data folder of the repository nor in the data tooling folder below <c>tools</c> (which holds the treebank cache), links resolved,
    ///     so that the command can never read a held-out file as weights or overwrite one with its result.
    /// </summary>
    /// <param name="repositoryRoot">The full path of the repository root.</param>
    /// <param name="path">The file, absolute or relative to the working directory.</param>
    /// <returns>The full path of the file.</returns>
    /// <exception cref="InvalidOperationException">The path is inside one of the two folders, as written or after link resolution.</exception>
    public static string EnsureArmFile(string repositoryRoot, string path)
    {
        ArgumentNullException.ThrowIfNull(repositoryRoot);
        ArgumentNullException.ThrowIfNull(path);
        EnsureOutsideData(repositoryRoot, path);
        var toolsData = Path.Combine(repositoryRoot, "tools", "data");
        var same = string.Equals(
            Path.GetFullPath(toolsData).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar),
            Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        if (same || RelativeTo(Canonicalize(toolsData), Canonicalize(path)) != null || RelativeTo(Path.GetFullPath(toolsData), Path.GetFullPath(path)) != null)
        {
            throw new InvalidOperationException($"'{path}' is inside the data tooling folder of the repository; the evaluation of a trained arm reads and writes files outside it only.");
        }

        return Path.GetFullPath(path);
    }

    /// <summary>
    ///     Gets the path of the lock file of a held-out run: the results file name with the suffix <c>.running</c>, next to it.
    /// </summary>
    /// <param name="resultsPath">The full path of the results file.</param>
    /// <returns>The path of the lock file.</returns>
    public static string RunLockPath(string resultsPath)
    {
        ArgumentNullException.ThrowIfNull(resultsPath);
        return resultsPath + ".running";
    }

    /// <summary>
    ///     Checks that the one held-out run has not been made or started yet: neither the results file nor the lock file of a run may exist.
    /// </summary>
    /// <remarks>
    ///     This is a courtesy check for an early, clear message; the atomic guard is <see cref="AcquireRunLock"/>.
    /// </remarks>
    /// <param name="resultsPath">The full path of the results file.</param>
    /// <exception cref="InvalidOperationException">The results file or the lock file exists.</exception>
    public static void EnsureNotEvaluated(string resultsPath)
    {
        ArgumentNullException.ThrowIfNull(resultsPath);
        if (File.Exists(resultsPath))
        {
            throw new InvalidOperationException($"{resultsPath} exists: the held-out evaluation was already run once. A repeat needs that file to be deleted on purpose (a visible change in the history) and a recorded reason.");
        }

        var lockPath = RunLockPath(resultsPath);
        if (File.Exists(lockPath))
        {
            throw new InvalidOperationException($"{lockPath} exists: a held-out run was started and did not complete (or is still running). The lock is never removed automatically; after an aborted run it must be deleted by hand, and the deletion and its reason recorded in RUN-LOG.md.");
        }
    }

    /// <summary>
    ///     Acquires the exclusive run lock of the one held-out run by creating the lock file with <see cref="FileMode.CreateNew"/>.
    /// </summary>
    /// <remarks>
    ///     Creation is atomic, so of two concurrent runs exactly one gets the lock. The lock is acquired before the first
    ///     held-out token is scored and stays on disk until the results file is written; a stale lock of an aborted run blocks
    ///     every later run until it is deleted by hand.
    /// </remarks>
    /// <param name="resultsPath">The full path of the results file.</param>
    /// <returns>The held lock.</returns>
    /// <exception cref="InvalidOperationException">The run was already made or started, or another run holds the lock.</exception>
    public static RunLock AcquireRunLock(string resultsPath)
    {
        EnsureNotEvaluated(resultsPath);
        var lockPath = RunLockPath(resultsPath);
        FileStream stream;
        try
        {
            stream = new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }
        catch (IOException) when (File.Exists(lockPath))
        {
            throw new InvalidOperationException($"{lockPath} exists: another held-out run holds the lock or an aborted run left it. Delete it by hand only after an aborted run, and record the deletion and its reason in RUN-LOG.md.");
        }

        var note = new UTF8Encoding(false).GetBytes("held-out run started " + DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture) + "\n");
        stream.Write(note, 0, note.Length);
        stream.Flush();
        return new RunLock(stream, lockPath, resultsPath);
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
///     The exclusive lock of the one held-out run: a lock file next to the results file, created atomically.
/// </summary>
/// <remarks>
///     The lock file stays until <see cref="Complete"/> has written the results file. Disposing the lock without completing
///     it (an exception, a killed process) leaves the file behind on purpose: a run that started scoring and did not finish
///     must be looked at by a person, who deletes the lock by hand and records that in RUN-LOG.md.
/// </remarks>
internal sealed class RunLock : IDisposable
{
    /// <summary>
    ///     The open lock file.
    /// </summary>
    private readonly FileStream _stream;

    /// <summary>
    ///     The path of the results file.
    /// </summary>
    private readonly string _resultsPath;

    /// <summary>
    ///     Initializes a new instance of the <see cref="RunLock"/> class.
    /// </summary>
    /// <param name="stream">The open lock file.</param>
    /// <param name="lockPath">The path of the lock file.</param>
    /// <param name="resultsPath">The path of the results file.</param>
    internal RunLock(FileStream stream, string lockPath, string resultsPath)
    {
        _stream = stream;
        LockPath = lockPath;
        _resultsPath = resultsPath;
    }

    /// <summary>
    ///     Gets the path of the lock file.
    /// </summary>
    public string LockPath { get; }

    /// <summary>
    ///     Writes the results file exclusively (it must not exist) and then removes the lock file.
    /// </summary>
    /// <param name="resultsText">The text of the results file.</param>
    /// <exception cref="IOException">The results file exists or cannot be written; the lock file then stays.</exception>
    public void Complete(string resultsText)
    {
        ArgumentNullException.ThrowIfNull(resultsText);
        using (var results = new FileStream(_resultsPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            var bytes = new UTF8Encoding(false).GetBytes(resultsText);
            results.Write(bytes, 0, bytes.Length);
        }

        _stream.Dispose();
        File.Delete(LockPath);
    }

    /// <summary>
    ///     Closes the lock file without removing it.
    /// </summary>
    public void Dispose() => _stream.Dispose();
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
    ///     Gets the path of a tranche of the hard-shape development set, relative to the repository root with forward slashes.
    /// </summary>
    /// <param name="tranche">The tranche.</param>
    /// <returns>The relative path of its file.</returns>
    public static string ShapesDevFileOf(ShapesDevTranche tranche) => DataGuard.ShapesDevelopmentFiles[(int)tranche];

    /// <summary>
    ///     Tells whether the file of a tranche of the hard-shape development set exists (through the shapes guard).
    /// </summary>
    /// <param name="repositoryRoot">The full path of the repository root.</param>
    /// <param name="tranche">The tranche.</param>
    /// <returns><c>true</c> when the file exists.</returns>
    /// <exception cref="InvalidOperationException">A link lies on the way to the file.</exception>
    public static bool ShapesDevExists(string repositoryRoot, ShapesDevTranche tranche) =>
        File.Exists(DataGuard.EnsureShapesDevelopmentFile(repositoryRoot, Path.Combine(repositoryRoot, ShapesDevFileOf(tranche).Replace('/', Path.DirectorySeparatorChar))));

    /// <summary>
    ///     Gets the name of a tranche of the hard-shape development set, as the corpus comment and the command line spell it.
    /// </summary>
    /// <param name="tranche">The tranche.</param>
    /// <returns>The name: <c>standard</c>, <c>hard</c>, <c>compound-head</c>, <c>headings</c>, <c>named-state</c>, <c>compound-head-2</c>, <c>headings-2</c>, <c>deverbal-heads</c>, <c>defined-terms</c>, <c>step-lines</c> or <c>clause-verbs</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tranche"/> is not a tranche.</exception>
    public static string ShapesDevNameOf(ShapesDevTranche tranche) => tranche switch
    {
        ShapesDevTranche.Standard => "standard",
        ShapesDevTranche.Hard => "hard",
        ShapesDevTranche.CompoundHead => "compound-head",
        ShapesDevTranche.Headings => "headings",
        ShapesDevTranche.NamedState => "named-state",
        ShapesDevTranche.CompoundHead2 => "compound-head-2",
        ShapesDevTranche.Headings2 => "headings-2",
        ShapesDevTranche.DeverbalHeads => "deverbal-heads",
        ShapesDevTranche.DefinedTerms => "defined-terms",
        ShapesDevTranche.StepLines => "step-lines",
        ShapesDevTranche.ClauseVerbs => "clause-verbs",
        _ => throw new ArgumentOutOfRangeException(nameof(tranche)),
    };

    /// <summary>
    ///     Gets the phrase that the corpus comment of a tranche carries (<c>hard tranche</c>, <c>headings tranche</c>); the standard tranche has none.
    /// </summary>
    /// <param name="tranche">The tranche.</param>
    /// <returns>The marker, or <c>null</c> for the standard tranche.</returns>
    private static string? ShapesDevMarkerOf(ShapesDevTranche tranche) =>
        tranche == ShapesDevTranche.Standard ? null : ShapesDevNameOf(tranche) + " tranche";

    /// <summary>
    ///     Gets the prefix of the sentence ids of a tranche (<c>shapes-dev-hard-</c>, <c>shapes-dev-headings-</c>); the standard tranche has none.
    /// </summary>
    /// <param name="tranche">The tranche.</param>
    /// <returns>The prefix, or <c>null</c> for the standard tranche, whose ids are the ones that start with no other prefix.</returns>
    private static string? ShapesDevIdPrefixOf(ShapesDevTranche tranche) =>
        tranche == ShapesDevTranche.Standard ? null : "shapes-dev-" + ShapesDevNameOf(tranche) + "-";

    /// <summary>
    ///     The tranches that have a corpus marker and an id prefix, in the order of <see cref="ShapesDevTranche"/>.
    /// </summary>
    private static readonly ShapesDevTranche[] MarkedTranches =
    {
        ShapesDevTranche.Hard,
        ShapesDevTranche.CompoundHead,
        ShapesDevTranche.Headings,
        ShapesDevTranche.NamedState,
        ShapesDevTranche.CompoundHead2,
        ShapesDevTranche.Headings2,
        ShapesDevTranche.DeverbalHeads,
        ShapesDevTranche.DefinedTerms,
        ShapesDevTranche.StepLines,
        ShapesDevTranche.ClauseVerbs,
    };

    /// <summary>
    ///     Gets the tranche that the id of a sentence announces.
    /// </summary>
    /// <param name="id">The sentence id.</param>
    /// <returns>The tranche whose prefix the id starts with, and the standard tranche for every other id.</returns>
    /// <remarks>
    ///     The longest matching prefix decides: the prefix of the compound-head tranche (<c>shapes-dev-compound-head-</c>) is the start
    ///     of the prefix of the second compound-head tranche (<c>shapes-dev-compound-head-2-</c>), and so is it for the headings.
    /// </remarks>
    private static ShapesDevTranche ShapesDevTrancheOfId(string id)
    {
        // No marked tranche matches: default(ShapesDevTranche) is the standard tranche
        return MarkedTranches
            .Where(t => id.StartsWith(ShapesDevIdPrefixOf(t)!, StringComparison.Ordinal))
            .OrderByDescending(t => ShapesDevIdPrefixOf(t)!.Length)
            .FirstOrDefault();
    }

    /// <summary>
    ///     Reads the standard tranche of the hard-shape development set.
    /// </summary>
    /// <param name="repositoryRoot">The full path of the repository root.</param>
    /// <param name="sha256">Receives the SHA-256 of the file as 64 lower-case hexadecimal digits.</param>
    /// <returns>The items in file order.</returns>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="InvalidDataException">The file does not announce a development split or is malformed.</exception>
    public static List<ShapesDevItem> ReadShapesDev(string repositoryRoot, out string sha256) =>
        ReadShapesDev(repositoryRoot, ShapesDevTranche.Standard, out sha256);

    /// <summary>
    ///     Reads one tranche of the hard-shape development set; every item carries the tranche that the file stands for.
    /// </summary>
    /// <param name="repositoryRoot">The full path of the repository root.</param>
    /// <param name="tranche">The tranche: the file read is the one of the tranche, so the tranche of an item cannot be set by the file content.</param>
    /// <param name="sha256">Receives the SHA-256 of the file as 64 lower-case hexadecimal digits.</param>
    /// <returns>The items in file order.</returns>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="InvalidDataException">The file does not announce a development split or the tranche it is read as (corpus header, sentence id prefix), or is malformed.</exception>
    public static List<ShapesDevItem> ReadShapesDev(string repositoryRoot, ShapesDevTranche tranche, out string sha256)
    {
        var path = DataGuard.EnsureShapesDevelopmentFile(repositoryRoot, Path.Combine(repositoryRoot, ShapesDevFileOf(tranche).Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"The shapes development set {path} does not exist.", path);
        }

        var header = File.ReadLines(path, Encoding.UTF8).Take(20).ToList();
        DataGuard.EnsureDevelopmentHeader(header, path);
        EnsureShapesDevTrancheHeader(header, tranche, path);
        sha256 = ModelBuilder.Sha256Hex(File.ReadAllBytes(path));
        var items = ShapesDevSets.Parse(File.ReadLines(path, Encoding.UTF8), path, tranche);
        var bad = items.Find(item => ShapesDevTrancheOfId(item.Id) != tranche);
        if (bad != null)
        {
            var name = ShapesDevNameOf(tranche);
            var prefixRule = tranche is ShapesDevTranche.Standard or ShapesDevTranche.Hard
                ? $"hard ids start with '{ShapesDevIdPrefixOf(ShapesDevTranche.Hard)}'"
                : $"{name} ids start with '{ShapesDevIdPrefixOf(tranche)}'";
            throw new InvalidDataException($"{path}: the sentence id '{bad.Id}' does not belong to the {name} tranche ({prefixRule}); refusing to read it.");
        }

        return items;
    }

    /// <summary>
    ///     Checks that the corpus comment of a file of the hard-shape development set announces the tranche that the file stands for, so that
    ///     a copy of the file of one tranche at the path of another is refused instead of reported as the wrong tranche.
    /// </summary>
    /// <param name="lines">The leading lines of the file; only the leading comment lines are inspected.</param>
    /// <param name="tranche">The tranche the file is read as.</param>
    /// <param name="origin">The name of the file for the message.</param>
    /// <exception cref="InvalidDataException">The corpus comment does not match the tranche: a tranche other than the standard one must name its own marker and no other, and the standard tranche names none.</exception>
    private static void EnsureShapesDevTrancheHeader(IEnumerable<string> lines, ShapesDevTranche tranche, string origin)
    {
        var claims = new HashSet<ShapesDevTranche>();
        foreach (var line in lines)
        {
            if (!line.StartsWith("#", StringComparison.Ordinal))
            {
                break;
            }

            if (!line.StartsWith("# corpus", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var marked in MarkedTranches.Where(m => line.Contains(ShapesDevMarkerOf(m)!, StringComparison.Ordinal)))
            {
                claims.Add(marked);
            }
        }

        // Another tranche's marker is refused first, then a missing marker of the tranche itself
        var other = MarkedTranches.FirstOrDefault(marked => marked != tranche && claims.Contains(marked));
        if (other != default)
        {
            throw new InvalidDataException($"{origin} announces itself as the {ShapesDevNameOf(other)} tranche in its corpus header; refusing to read it as the {ShapesDevNameOf(tranche)} tranche.");
        }

        if (tranche != ShapesDevTranche.Standard && !claims.Contains(tranche))
        {
            throw new InvalidDataException($"{origin} does not announce itself as the {ShapesDevNameOf(tranche)} tranche ('{ShapesDevMarkerOf(tranche)}') in its corpus header; refusing to read it as the {ShapesDevNameOf(tranche)} tranche.");
        }
    }

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
    ///     Reads the convention-mapped table of the development part of the external treebank from its local cache.
    /// </summary>
    /// <remarks>
    ///     The file name is fixed (<see cref="DataGuard.UdDevMappedFile"/> in <see cref="DataGuard.UdCacheFolder"/>), there is no
    ///     path parameter, the SHA-256 of the table is verified against its pin before a line is parsed, and the corpus comment must
    ///     announce a development table. The test part of the treebank has another file name and is never opened here.
    /// </remarks>
    /// <param name="repositoryRoot">The full path of the repository root.</param>
    /// <param name="sha256">Receives the SHA-256 of the table as 64 lower-case hexadecimal digits.</param>
    /// <returns>The sentences.</returns>
    /// <exception cref="InvalidDataException">The cache is missing, the hash differs from the pin or the table does not announce a development part.</exception>
    public static List<UdSentence> ReadUdDev(string repositoryRoot, out string sha256)
    {
        ArgumentNullException.ThrowIfNull(repositoryRoot);
        var path = Path.GetFullPath(Path.Combine(repositoryRoot, DataGuard.UdCacheFolder.Replace('/', Path.DirectorySeparatorChar), DataGuard.UdDevMappedFile));
        const string Recipe = "Run: python tools/data/fetch_ud_ewt.py fetch, then python tools/data/fetch_ud_ewt.py map.";
        if (!File.Exists(path))
        {
            throw new InvalidDataException($"The development table {path} is missing from the treebank cache. {Recipe}");
        }

        var bytes = File.ReadAllBytes(path);
        sha256 = ModelBuilder.Sha256Hex(bytes);
        if (!string.Equals(sha256, DataGuard.UdDevMappedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"The cached {DataGuard.UdDevMappedFile} does not match its pinned SHA-256. {Recipe}");
        }

        var lines = new List<string>();
        using (var reader = new StringReader(new UTF8Encoding(false).GetString(bytes)))
        {
            while (reader.ReadLine() is { } line)
            {
                lines.Add(line);
            }
        }

        // The corpus comment of the cache file says which part of the treebank it holds
        var announced = lines.Take(5).FirstOrDefault(l => l.StartsWith("# cache file", StringComparison.Ordinal)) ?? string.Empty;
        if (!announced.Contains(" dev,", StringComparison.Ordinal) || announced.Contains(" test,", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{path} does not announce itself as the development part of the treebank in its header; refusing to read it.");
        }

        return HeldOutSets.ParseUd(lines, path);
    }

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
    public static HeldOutFile ReadUd(string repositoryRoot, string cacheFolder, SelectedOperatingPoints selection) =>
        ReadUd(repositoryRoot, cacheFolder, selection, DataGuard.UdMappedSha256);

    /// <summary>
    ///     Reads the convention-mapped table of the external treebank from its cache, after verifying the pinned source file
    ///     and the table itself against the given pin.
    /// </summary>
    /// <param name="repositoryRoot">The full path of the repository root.</param>
    /// <param name="cacheFolder">The cache folder, absolute or relative to the repository root.</param>
    /// <param name="selection">The validated development selection.</param>
    /// <param name="mappedSha256">The pinned SHA-256 of the mapped table (the run uses <see cref="DataGuard.UdMappedSha256"/>).</param>
    /// <returns>The mapped table.</returns>
    /// <exception cref="InvalidDataException">The cache or the pins are missing, or a hash differs from its pin.</exception>
    internal static HeldOutFile ReadUd(string repositoryRoot, string cacheFolder, SelectedOperatingPoints selection, string mappedSha256)
    {
        ArgumentNullException.ThrowIfNull(repositoryRoot);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(mappedSha256);
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

        // The mapped table is derived data in a local cache: it must be the exact table of the held-out run, not merely a table made from the pinned source
        var bytes = File.ReadAllBytes(mappedPath);
        var mappedSha = ModelBuilder.Sha256Hex(bytes);
        if (!string.Equals(mappedSha, mappedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"The cached {DataGuard.UdMappedFile} does not match its pinned SHA-256. {Recipe}");
        }

        return new HeldOutFile(mappedPath, mappedSha, Split(bytes), sourceSha);
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
