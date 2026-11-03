namespace DemaConsulting.PartOfSpeech.LexiconCompiler;

/// <summary>
///     Command-line entry point of the lexicon compiler.
/// </summary>
/// <remarks>
///     <para>
///         Usage: <c>LexiconCompiler [--input data/lexicon/lexicon.tsv] [--output src/DemaConsulting.PartOfSpeech/Resources/lexicon.bin] [--check]</c>.
///         Without <c>--check</c> the tool compiles the table and writes the binary. With <c>--check</c> it compiles in
///         memory and compares the <em>uncompressed</em> image (Deflate output differs between runtimes) with the
///         committed binary; the exit code is 1 when they differ or the binary is missing.
///     </para>
///     <para>
///         The program only parses arguments and does I/O: all logic is in <c>LexiconWriter</c> and <c>Lexicon</c> of the
///         library, so the tests exercise exactly the code that produces the committed file.
///     </para>
/// </remarks>
internal static class Program
{
    /// <summary>
    ///     The repository-relative default location of the source table.
    /// </summary>
    private const string DefaultInput = "data/lexicon/lexicon.tsv";

    /// <summary>
    ///     The repository-relative default location of the compiled lexicon.
    /// </summary>
    private const string DefaultOutput = "src/DemaConsulting.PartOfSpeech/Resources/lexicon.bin";

    /// <summary>
    ///     Runs the tool.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>0 on success, 1 when the check fails, 2 for a usage or data error.</returns>
    internal static int Main(string[] args)
    {
        try
        {
            // Parse the three options; anything else is a usage error
            string? input = null;
            string? output = null;
            var check = false;
            var index = 0;
            while (index < args.Length)
            {
                var argument = args[index++];
                switch (argument)
                {
                    case "--input" when index < args.Length:
                        input = args[index++];
                        break;
                    case "--output" when index < args.Length:
                        output = args[index++];
                        break;
                    case "--check":
                        check = true;
                        break;
                    default:
                        Console.Error.WriteLine($"Unknown or incomplete argument '{argument}'. Usage: LexiconCompiler [--input FILE] [--output FILE] [--check]");
                        return 2;
                }
            }

            input ??= Path.Combine(FindRepositoryRoot(), DefaultInput.Replace('/', Path.DirectorySeparatorChar));
            output ??= Path.Combine(FindRepositoryRoot(), DefaultOutput.Replace('/', Path.DirectorySeparatorChar));

            // Compile the table (the writer validates every format rule and reports the offending line)
            var entries = LexiconWriter.FromTsv(File.ReadLines(input));
            return check ? Check(entries, output) : Write(entries, output);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }

    /// <summary>
    ///     Compiles the lexicon and writes the binary.
    /// </summary>
    /// <param name="entries">The spellings and tags.</param>
    /// <param name="output">The file to write.</param>
    /// <returns>0.</returns>
    private static int Write(IReadOnlyList<KeyValuePair<string, UposSet>> entries, string output)
    {
        var file = LexiconWriter.Compile(entries, true);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllBytes(output, file);
        Console.WriteLine($"Wrote {output}: {file.Length} bytes, {entries.Count} spellings.");
        return 0;
    }

    /// <summary>
    ///     Compares the committed binary with a fresh compilation.
    /// </summary>
    /// <param name="entries">The spellings and tags.</param>
    /// <param name="committed">The committed binary.</param>
    /// <returns>0 when the binary is current, 1 otherwise.</returns>
    private static int Check(IReadOnlyList<KeyValuePair<string, UposSet>> entries, string committed)
    {
        // The command that fixes a stale or missing binary is part of the message so the failure is actionable
        const string Regenerate = "dotnet run --project tools/LexiconCompiler";
        if (!File.Exists(committed))
        {
            Console.Error.WriteLine($"The compiled lexicon {committed} does not exist. Regenerate it with: {Regenerate}");
            return 1;
        }

        // Compare uncompressed images: the Deflate bytes depend on the runtime, the payload does not
        var expected = LexiconWriter.Compile(entries, false);
        var actual = Lexicon.ToUncompressedImage(File.ReadAllBytes(committed));
        if (!expected.AsSpan().SequenceEqual(actual))
        {
            Console.Error.WriteLine($"The compiled lexicon {committed} does not match the source table. Regenerate it with: {Regenerate}");
            return 1;
        }

        Console.WriteLine($"The compiled lexicon {committed} is current ({entries.Count} spellings).");
        return 0;
    }

    /// <summary>
    ///     Finds the repository root: the nearest folder above the tool that holds the solution file.
    /// </summary>
    /// <returns>The full path of the repository root.</returns>
    /// <exception cref="DirectoryNotFoundException">No parent folder holds <c>PartOfSpeech.slnx</c>.</exception>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PartOfSpeech.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("PartOfSpeech.slnx was not found above " + AppContext.BaseDirectory);
    }
}
