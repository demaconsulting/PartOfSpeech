using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DemaConsulting.PartOfSpeech.Trainer;

namespace DemaConsulting.PartOfSpeech.Tests;

/// <summary>
///     System-level tests of the committed data and licensing artifacts: the third-party notice and its packaging, the
///     licenses and provenance of the data, the staged corpus corrections, the Universal POS tags, the separation of
///     training and evaluation data, and the lexicon. The tests read the files of the working copy and need no network.
///     Each test proves one obligation of one requirement; a test that finds no file or no row fails instead of passing
///     on nothing.
/// </summary>
public class PartOfSpeechDataTests
{
    /// <summary>
    ///     The number of problems that an assertion shows, so that a broken file does not flood the test output.
    /// </summary>
    private const int ShownProblems = 20;

    /// <summary>
    ///     The training configuration of the trainer tool, relative to the repository root.
    /// </summary>
    private const string TrainingConfigPath = "tools/DemaConsulting.PartOfSpeech.Trainer/training.json";

    /// <summary>
    ///     The file name of the combined license of the package.
    /// </summary>
    private const string PackageLicenseName = "PACKAGE-LICENSE.md";

    /// <summary>
    ///     The license class that the MASC source table records for every document: the license of the whole corpus.
    /// </summary>
    private const string MascLicenseBasis = "CC BY 3.0 US";

    /// <summary>
    ///     The training corpora, relative to the data folder.
    /// </summary>
    private static readonly string[] TrainingCorpora = { "open/train.tsv", "masc/masc.tsv", "silver/silver.tsv" };

    /// <summary>
    ///     The token-level evaluation corpora, relative to the data folder.
    /// </summary>
    private static readonly string[] EvaluationCorpora =
    {
        "open/dev.tsv", "open/test.tsv", "eval/ste-challenge/test.tsv"
    };

    /// <summary>
    ///     The data folders that hold data and therefore declare a license, relative to the data folder.
    /// </summary>
    private static readonly string[] LicensedFolders =
    {
        "open", "masc", "silver", "eval/shapes", "eval/gold-v2", "eval/ste-challenge", "lexicon", "corrections"
    };

    /// <summary>
    ///     The tables that record the origin and license basis of every source document, relative to the data folder,
    ///     with the name of the column that holds the document id.
    /// </summary>
    private static readonly (string Table, string IdColumn)[] SourceTables =
    {
        ("open/sources.tsv", "source_id"), ("eval/gold-v2/sources.tsv", "doc_id"), ("silver/sources.tsv", "source_id")
    };

    /// <summary>
    ///     The statements that every data README makes, per folder: the headings and bold fields that record where the
    ///     data comes from, under which license, and how it was produced. The folder of UD English-EWT holds no data; its
    ///     README records where the treebank is fetched from and under which license it stays out of the repository.
    /// </summary>
    private static readonly Dictionary<string, string[]> ReadmeStatements = new(StringComparer.Ordinal)
    {
        ["open"] = new[] { "## License and Provenance", "**Source:**", "**How produced:**", "`sources.tsv`" },
        ["masc"] = new[] { "## Source", "**Corpus:**", "**Input file:**", "**How produced:**", "`sources.tsv`" },
        ["silver"] = new[] { "## Content and License", "## How It Was Produced", "`sources.tsv`" },
        ["eval/shapes"] = new[]
        {
            "## Source and Provenance", "**Source:**", "**How produced (by the WordSense project):**", "**License:**"
        },
        ["eval/gold-v2"] = new[] { "## Source and Provenance", "**Source:**", "**Labels:**", "**Sentences:**", "`sources.tsv`" },
        ["eval/ste-challenge"] = new[] { "## Source and Provenance", "**Source:**", "**Sentences:**", "**Tags:**" },
        ["lexicon"] = new[] { "## Sources", "## How It Is Built" },
        ["eval/ud-ewt"] = new[] { "## Fetch and Map", "**Source:**", "CC BY-SA 4.0" }
    };

    /// <summary>
    ///     The license identifiers that a data folder may declare.
    /// </summary>
    private static readonly string[] AllowedLicenses = { "CC0-1.0", "CC-BY-3.0-US", "CC-BY-4.0", "Public-Domain", "MIT" };

    /// <summary>
    ///     The beginnings of the license basis of a source document: the license classes that a source table may record.
    /// </summary>
    private static readonly string[] AllowedLicenseBases =
    {
        "Tatoeba sentence licensed CC0 1.0", "CC0-1.0", "US federal government work", "US public domain",
        "Written by the project"
    };

    /// <summary>
    ///     The coarse classes of the single-target evaluation sets.
    /// </summary>
    private static readonly string[] CoarseClasses =
    {
        "Noun", "Verb", "Adjective", "Adverb", "Pronoun", "Preposition", "Determiner", "Conjunction", "Auxiliary",
        "Interjection", "Other"
    };

    /// <summary>
    ///     A line of a data README that declares the license of its folder.
    /// </summary>
    private static readonly Regex LicenseLine = new(@"^License: (\S+)\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>
    ///     A pinned identifier of an input: a Git commit (40 hexadecimal digits) or a SHA-256 (64 hexadecimal digits).
    /// </summary>
    private static readonly Regex PinnedIdentifier = new(@"\b(?:[0-9a-f]{40}|[0-9a-f]{64})\b", RegexOptions.CultureInvariant);

    /// <summary>
    ///     A reference to a script of the data tools, as the READMEs give it in the command that produces the data.
    /// </summary>
    private static readonly Regex ScriptReference = new(@"tools/data/([a-z_]+)\.py", RegexOptions.CultureInvariant);

    /// <summary>
    ///     The id of a MASC document: written genre and file path.
    /// </summary>
    private static readonly Regex MascDocument = new(@"^masc:written/([a-z-]+)/\S+$", RegexOptions.CultureInvariant);

    /// <summary>
    ///     Text that must not appear in the license basis of any source document.
    /// </summary>
    private static readonly Regex RestrictedLicenseText = new(
        @"share-?alike|by-sa|by-nc|by-nd|non-?commercial|no-?derivatives?|cc by|wikipedia|\.org/licenses/by",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    ///     A path of a SemCor or Brown corpus file.
    /// </summary>
    private static readonly Regex RestrictedCorpusPath = new("semcor|brown", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    ///     Proves that the third-party notice attributes MASC with its title, creators, source link, license link and
    ///     citation.
    /// </summary>
    [Fact]
    public void PartOfSpeech_ThirdPartyNotices_Masc_IsAttributed()
    {
        // Arrange: the MASC section of the notice and the elements that an attribution must have
        var section = NoticeSection("## The Manually Annotated Sub-Corpus (MASC)");
        var required = new[]
        {
            "The Manually Annotated Sub-Corpus (MASC)",
            "Nancy Ide",
            "Collin Baker",
            "Christiane Fellbaum",
            "Rebecca Passonneau",
            "https://anc.org/data/masc/",
            "Creative Commons Attribution 3.0 United States",
            "https://creativecommons.org/licenses/by/3.0/us/",
            "https://aclanthology.org/L08-1105/"
        };

        // Act: find the elements that the section lacks
        var missing = MissingFragments(section, required);

        // Assert: nothing is missing
        Assert.Empty(missing);
    }

    /// <summary>
    ///     Proves that the third-party notice attributes Open English WordNet with its title, creator, source links and
    ///     license link.
    /// </summary>
    [Fact]
    public void PartOfSpeech_ThirdPartyNotices_OpenEnglishWordNet_IsAttributed()
    {
        // Arrange: the Open English WordNet section of the notice and the elements that an attribution must have
        var section = NoticeSection("## Open English WordNet");
        var required = new[]
        {
            "Open English WordNet 2025 Edition",
            "John P. McCrae",
            "https://en-word.net",
            "https://github.com/globalwordnet/english-wordnet",
            "Creative Commons Attribution 4.0 International",
            "https://creativecommons.org/licenses/by/4.0/"
        };

        // Act: find the elements that the section lacks
        var missing = MissingFragments(section, required);

        // Assert: nothing is missing
        Assert.Empty(missing);
    }

    /// <summary>
    ///     Proves that the third-party notice states that the MASC data and the Open English WordNet data were converted
    ///     by this project.
    /// </summary>
    [Fact]
    public void PartOfSpeech_ThirdPartyNotices_Changes_AreStated()
    {
        // Arrange: the statement of change that each of the two sections must make
        var mascSection = NoticeSection("## The Manually Annotated Sub-Corpus (MASC)");
        var wordNetSection = NoticeSection("## Open English WordNet");

        // Act: find the statements that the sections lack
        var missing = MissingFragments(mascSection, new[] { "converted to Universal POS (UPOS) tags" })
            .Concat(MissingFragments(wordNetSection, new[] { "maps a lower-case spelling to the UPOS tags it may carry" }))
            .ToList();

        // Assert: both conversions are stated
        Assert.Empty(missing);
    }

    /// <summary>
    ///     Proves that the third-party notice reproduces the Princeton WordNet license that Open English WordNet carries.
    /// </summary>
    [Fact]
    public void PartOfSpeech_ThirdPartyNotices_PrincetonLicense_IsReproduced()
    {
        // Arrange: the Princeton WordNet section of the notice and the parts of the license that must be reproduced
        var section = NoticeSection("### Princeton WordNet");
        var required = new[]
        {
            "WordNet 3.1 Copyright 2011 by Princeton University. All rights reserved.",
            "THIS SOFTWARE AND DATABASE IS PROVIDED \"AS IS\" AND PRINCETON UNIVERSITY MAKES NO REPRESENTATIONS",
            "Title to copyright in this software, database and any associated documentation shall at all times remain " +
            "with Princeton University"
        };

        // Act: find the parts that the section lacks
        var missing = MissingFragments(section, required);

        // Assert: nothing is missing
        Assert.Empty(missing);
    }

    /// <summary>
    ///     Proves that the project file packs the third-party notice at the root of the NuGet package.
    /// </summary>
    [Fact]
    public void PartOfSpeech_ThirdPartyNotices_Packaging_NoticeIsPackedAtPackageRoot()
    {
        // Arrange: load the project file of the library
        var projectFile = DataFiles.Repository("src/DemaConsulting.PartOfSpeech/DemaConsulting.PartOfSpeech.csproj");
        var project = XDocument.Load(projectFile);

        // Act: find the items that include the notice file
        var items = project.Descendants("None")
            .Where(item => ((string?)item.Attribute("Include") ?? string.Empty).EndsWith("THIRD-PARTY-NOTICES.md", StringComparison.Ordinal))
            .ToList();

        // Assert: exactly one item packs the existing notice file at the package root
        var item = Assert.Single(items);
        var include = ((string?)item.Attribute("Include"))!.Replace('\\', '/');
        Assert.True(File.Exists(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectFile)!, include))));
        Assert.Equal("true", (string?)item.Attribute("Pack"));
        Assert.Equal("/", ((string?)item.Attribute("PackagePath"))!.Replace('\\', '/'));
    }

    /// <summary>
    ///     Proves that every data folder declares a license and that the license is one of the allowed set.
    /// </summary>
    [Fact]
    public void PartOfSpeech_DataLicensing_Folders_DeclareAllowedLicenses()
    {
        // Arrange: collect the problems of the license declarations
        var problems = new List<string>();

        // Act: check the README of every data folder
        foreach (var folder in LicensedFolders)
        {
            problems.AddRange(LicenseProblems(folder));
        }

        // Assert: every folder was checked and no problem was found
        Assert.Equal(8, LicensedFolders.Length);
        Assert.Empty(problems);
    }

    /// <summary>
    ///     Proves that the staged correction tables are covered by the license gate: each table declares CC0-1.0 for its
    ///     labels and names the MASC license and attribution, the README of the folder links the attribution file, every
    ///     row belongs to a document that a source table lists (a MASC row only with the CC BY 3.0 US class, because its
    ///     token and context excerpt are MASC text), and no label, id, reason or provenance cell names a restricted license.
    /// </summary>
    [Fact]
    public void PartOfSpeech_DataLicensing_Corrections_AreCoveredByTheLicenseGate()
    {
        // Arrange: the documents that each source table lists (MASC: only those with the corpus license class), the README
        var problems = new List<string>();
        var mascSources = DataFiles.ReadTable(DataFiles.Data("masc/sources.tsv"));
        var listed = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["open"] = SourceIds("open/sources.tsv", "source_id"),
            ["silver"] = SourceIds("silver/sources.tsv", "source_id"),
            ["masc"] = new HashSet<string>(
                mascSources.Rows.Where(row => mascSources.Cell(row, "license_basis") == MascLicenseBasis).Select(row => mascSources.Cell(row, "source_id")),
                StringComparer.Ordinal)
        };
        var readme = File.ReadAllText(DataFiles.Data("corrections/README.md"));
        var textColumns = new[] { "token", "hint", "context" };
        var names = new[] { "mechanical.tsv", "candidates.tsv", "exceptions.tsv", "llm.tsv" }
            .Where(name => File.Exists(CorrectionTables.FilePath(name))).ToList();
        var rows = 0;

        // Act: check the attribution, then the license line and every row of each table
        if (!readme.Contains("](../masc/ATTRIBUTION.md)", StringComparison.Ordinal) || !File.Exists(DataFiles.Data("masc/ATTRIBUTION.md")))
        {
            problems.Add("data/corrections/README.md: does not link the existing MASC attribution ../masc/ATTRIBUTION.md");
        }

        foreach (var name in names)
        {
            var declarations = File.ReadLines(CorrectionTables.FilePath(name)).Where(line => line.StartsWith("# license = ", StringComparison.Ordinal)).ToList();
            if (declarations.Count != 1 || !declarations[0].Contains("CC0-1.0", StringComparison.Ordinal)
                || !declarations[0].Contains(MascLicenseBasis, StringComparison.Ordinal)
                || !declarations[0].Contains("masc/ATTRIBUTION.md", StringComparison.Ordinal))
            {
                problems.Add($"data/corrections/{name}: needs one '# license' line with CC0-1.0, {MascLicenseBasis} and masc/ATTRIBUTION.md");
            }

            var table = DataFiles.ReadTable(CorrectionTables.FilePath(name));
            rows += table.Rows.Count;
            foreach (var row in table.Rows)
            {
                var source = table.Cell(row, "source");
                var document = table.Cell(row, "source_id");
                if (!listed.TryGetValue(source, out var documents) || !documents.Contains(document))
                {
                    problems.Add($"data/corrections/{name}: the document {document} is not listed with an allowed license for source {source}");
                }

                problems.AddRange(table.Columns
                    .Where(column => Array.IndexOf(textColumns, column) < 0 && RestrictedLicenseText.IsMatch(table.Cell(row, column)))
                    .Select(column => $"data/corrections/{name}: column {column} names a restricted license for {document}"));
            }
        }

        // Assert: the three tables were scanned, they carry rows, and no problem was found
        Assert.True(names.Count >= 3);
        Assert.True(rows > 10000);
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that no source document is recorded under a share-alike, non-commercial, no-derivatives, Wikipedia or
    ///     CC BY license.
    /// </summary>
    [Fact]
    public void PartOfSpeech_DataLicensing_SourceTables_RecordNoRestrictedLicense()
    {
        // Arrange: read the license basis of every source document
        var rows = 0;
        var problems = new List<string>();

        // Act: find the license bases that name a restricted license
        foreach (var (table, idColumn) in SourceTables)
        {
            var data = DataFiles.ReadTable(DataFiles.Data(table));
            rows += data.Rows.Count;
            problems.AddRange(data.Rows
                .Where(row => RestrictedLicenseText.IsMatch(data.Cell(row, "license_basis")))
                .Select(row => $"data/{table}: restricted license for {data.Cell(row, idColumn)}"));
        }

        // Assert: the tables are not empty and no document has a restricted license
        Assert.True(rows > 10000);
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that no UD English-EWT file is committed: no CoNLL-U file anywhere, no path below the data folder that
    ///     names the treebank, and nothing but the documentation in its folder.
    /// </summary>
    [Fact]
    public void PartOfSpeech_DataLicensing_UdEwt_IsNotCommitted()
    {
        // Arrange: list the files of the repository, without dependencies, build output and the git-ignored download cache
        var files = DataFiles.EnumerateFiles(DataFiles.RepositoryRoot, IsIgnoredFolder).ToList();
        var paths = files.Select(DataFiles.RelativePath).ToList();

        // Act: find CoNLL-U files anywhere and treebank names below the data folder
        var problems = paths
            .Where(path => path.EndsWith(".conllu", StringComparison.OrdinalIgnoreCase))
            .Select(path => "CoNLL-U file: " + path)
            .Concat(paths
                .Where(path => path.StartsWith("data/", StringComparison.Ordinal) && path != "data/eval/ud-ewt/README.md")
                .Where(path => path.IndexOf("ewt", StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(path => "treebank name: " + path))
            .ToList();

        // Assert: the repository was listed, nothing of the treebank is committed, and its folder holds only its README
        Assert.Contains("data/README.md", paths);
        Assert.Empty(problems);
        Assert.Equal(
            new[] { "data/eval/ud-ewt/README.md" },
            paths.Where(path => path.StartsWith("data/eval/ud-ewt/", StringComparison.Ordinal)));
    }

    /// <summary>
    ///     Proves that no SemCor or Brown corpus file is committed.
    /// </summary>
    [Fact]
    public void PartOfSpeech_DataLicensing_RestrictedCorpora_AreNotCommitted()
    {
        // Arrange: list the files below the data folder
        var paths = DataFiles.EnumerateFiles(DataFiles.Data(string.Empty).TrimEnd('/', '\\'), IsIgnoredFolder)
            .Select(DataFiles.RelativePath)
            .ToList();

        // Act: find the paths that name SemCor or the Brown corpus
        var problems = paths.Where(path => RestrictedCorpusPath.IsMatch(path)).ToList();

        // Assert: the data folder was listed and no restricted corpus is committed
        Assert.Contains("data/MANIFEST.tsv", paths);
        Assert.Empty(problems);
    }

    /// <summary>
    ///     Proves that the README of every data folder records the source of the data, its license and how it was
    ///     produced: the statements of the folder, a pinned input identifier, and a script of the data tools that exists.
    /// </summary>
    [Fact]
    public void PartOfSpeech_DataProvenance_Readmes_RecordSourceAndProduction()
    {
        // Arrange: collect the problems
        var problems = new List<string>();

        // Act: check the README of every data folder
        foreach (var entry in ReadmeStatements)
        {
            var readme = DataFiles.Data(entry.Key + "/README.md");
            if (!File.Exists(readme))
            {
                problems.Add("missing README: data/" + entry.Key);
                continue;
            }

            var text = File.ReadAllText(readme);
            problems.AddRange(entry.Value
                .Where(statement => text.IndexOf(statement, StringComparison.Ordinal) < 0)
                .Select(statement => $"data/{entry.Key}: README lacks '{statement}'"));
            if (!PinnedIdentifier.IsMatch(text))
            {
                problems.Add($"data/{entry.Key}: README names no pinned commit or SHA-256");
            }

            var scripts = ScriptReference.Matches(text).Cast<Match>().Select(match => match.Groups[1].Value).Distinct().ToList();
            if (scripts.Count == 0)
            {
                problems.Add($"data/{entry.Key}: README names no script that produces the data");
            }

            problems.AddRange(scripts
                .Where(script => !File.Exists(DataFiles.Repository("tools/data/" + script + ".py")))
                .Select(script => $"data/{entry.Key}: README names a missing script {script}.py"));
        }

        problems.AddRange(new[] { "README.md", "CONVENTIONS.md" }
            .Where(name => !File.Exists(DataFiles.Data(name)))
            .Select(name => "missing: data/" + name));

        // Assert: every folder was checked and no problem was found
        Assert.Equal(8, ReadmeStatements.Count);
        Assert.Empty(problems);
    }

    /// <summary>
    ///     Proves that every source document of the open corpus, the evaluation sets and the silver and MASC training data
    ///     appears in the source list of its folder (and that every MASC source row has sentences in <c>masc.tsv</c>).
    /// </summary>
    [Fact]
    public void PartOfSpeech_DataProvenance_Sources_ListEveryDocument()
    {
        // Arrange: the documents of the data and the documents that the source tables list
        var openListed = SourceIds("open/sources.tsv", "source_id");
        var goldListed = SourceIds("eval/gold-v2/sources.tsv", "doc_id");
        var silverListed = SourceIds("silver/sources.tsv", "source_id");
        var openDocuments = new[] { "open/train.tsv", "open/dev.tsv", "open/test.tsv", "eval/ste-challenge/test.tsv" }
            .SelectMany(corpus => DataFiles.ReadCorpus(DataFiles.Data(corpus)).Select(sentence => sentence.SourceId))
            .ToList();
        var goldDocuments = new[] { "dev.tsv", "test.tsv" }
            .SelectMany(name => DataFiles.ReadTable(DataFiles.Data("eval/gold-v2/" + name)).Column("doc_id"))
            .ToList();
        var silverDocuments = DataFiles.ReadCorpus(DataFiles.Data("silver/silver.tsv")).Select(sentence => sentence.SourceId).ToList();
        var mascDocuments = DataFiles.ReadCorpus(DataFiles.Data("masc/masc.tsv")).Select(sentence => sentence.SourceId).Distinct().ToList();
        var mascListed = SourceIds("masc/sources.tsv", "source_id");

        // Act: find the documents that no source list names
        var problems = MissingDocuments("open", openDocuments, openListed)
            .Concat(MissingDocuments("eval/gold-v2", goldDocuments, goldListed))
            .Concat(MissingDocuments("silver", silverDocuments, silverListed))
            .Concat(MissingDocuments("masc", mascDocuments, mascListed))
            .Concat(mascListed.Except(mascDocuments).Select(document => "masc: listed but not in masc.tsv: " + document))
            .ToList();

        // Assert: the data and the lists are not empty and every document is listed
        Assert.True(openDocuments.Distinct().Count() > 5000);
        Assert.True(goldDocuments.Distinct().Count() > 1000);
        Assert.True(silverDocuments.Distinct().Count() > 5000);
        Assert.True(mascDocuments.Count > 300);
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that every row of the source tables names a license class of the allowed set, a well-formed URL (empty
    ///     only for the document that the project wrote itself), and a positive sentence count; for MASC, the corpus-wide
    ///     CC BY 3.0 US class and counts that equal those of <c>masc.tsv</c>.
    /// </summary>
    [Fact]
    public void PartOfSpeech_DataProvenance_Sources_RowsRecordLicenseAndUrl()
    {
        // Arrange: read the source tables
        var problems = new List<string>();
        var rows = 0;

        // Act: check the license class, the URLs and the sentence count of every row; MASC records one license for the
        // whole corpus, so its table is checked for that class and for counts that match masc.tsv
        foreach (var (table, idColumn) in SourceTables)
        {
            var data = DataFiles.ReadTable(DataFiles.Data(table));
            rows += data.Rows.Count;
            foreach (var row in data.Rows)
            {
                problems.AddRange(SourceRowProblems(table, data, row, idColumn));
            }
        }

        rows += DataFiles.ReadTable(DataFiles.Data("masc/sources.tsv")).Rows.Count;
        problems.AddRange(MascSourceProblems());
        // Assert: the tables are not empty and every row is valid
        Assert.True(rows > 10000);
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the checksum manifest lists exactly the committed data files (Markdown files and the manifest
    ///     itself excluded).
    /// </summary>
    [Fact]
    public void PartOfSpeech_DataProvenance_Manifest_ListsExactlyTheCommittedFiles()
    {
        // Arrange: read the manifest and list the data files it covers
        var manifest = ReadManifest();
        var onDisk = ManifestedFilesOnDisk();

        // Act: compare the file lists
        var problems = onDisk.Except(manifest.Select(row => row[0])).Select(path => "not in the manifest: " + path)
            .Concat(manifest.Select(row => row[0]).Except(onDisk).Select(path => "missing file: " + path))
            .ToList();

        // Assert: the manifest is not empty and complete
        Assert.True(manifest.Count > 20);
        Assert.Empty(problems);
    }

    /// <summary>
    ///     Proves that each committed data file has the size and the SHA-256 that the checksum manifest records.
    /// </summary>
    [Fact]
    public void PartOfSpeech_DataProvenance_Manifest_ChecksumsMatchCommittedFiles()
    {
        // Arrange: read the manifest (path, bytes, sha256, lines, sentences) and list the data files it covers
        var manifest = ReadManifest();
        var onDisk = ManifestedFilesOnDisk();

        // Act: compare the sizes and the hashes of the files that exist
        var problems = manifest.Where(row => onDisk.Contains(row[0])).SelectMany(ChecksumProblems).ToList();

        // Assert: the manifest is not empty and every file matches
        Assert.True(manifest.Count > 20);
        Assert.Empty(problems);
    }

    /// <summary>
    ///     Proves that the staged correction tables (<c>data/corrections</c>) have the documented columns, that every
    ///     row is valid on its own (legal address, tags of the 17, status cells with canonical flags, a change, a
    ///     provenance), that the rows are sorted, and that no token has two correction rows.
    /// </summary>
    [Fact]
    public void PartOfSpeech_DataCorrections_Tables_AreWellFormed()
    {
        // Arrange: the tables of corrections that exist (the mechanical one is required), the candidates and the exceptions
        var names = CorrectionTables.CorrectionTableNames.Where(name => File.Exists(CorrectionTables.FilePath(name))).ToList();
        Assert.Contains("mechanical.tsv", names);
        var corrections = names.Select(name => (Name: name, Table: DataFiles.ReadTable(CorrectionTables.FilePath(name)))).ToList();
        var candidates = DataFiles.ReadTable(CorrectionTables.FilePath("candidates.tsv"));
        var exceptions = DataFiles.ReadTable(CorrectionTables.FilePath("exceptions.tsv"));

        // Act: validate each table; the addresses of the corrections must be unique across the tables of corrections
        var addresses = new HashSet<string>(StringComparer.Ordinal);
        var problems = corrections.SelectMany(entry => CorrectionTables.TableProblems(entry.Name, "corrections", entry.Table, addresses))
            .Concat(CorrectionTables.TableProblems("candidates.tsv", "candidates", candidates, new HashSet<string>(StringComparer.Ordinal)))
            .Concat(CorrectionTables.TableProblems("exceptions.tsv", "exceptions", exceptions, new HashSet<string>(StringComparer.Ordinal)))
            .ToList();

        // Assert: the tables that carry the staged work are not empty and no problem was found
        Assert.True(corrections.First(entry => entry.Name == "mechanical.tsv").Table.Rows.Count > 0);
        Assert.True(candidates.Rows.Count > 0);
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the row validation of the data tests gives the verdicts of the shared cases
    ///     (<c>tools/data/tests/corrections-cases.tsv</c>), which the Python validator of the overlay is also held to.
    /// </summary>
    [Fact]
    public void PartOfSpeech_DataCorrections_RowCases_MatchSharedFixture()
    {
        // Arrange: the shared cases (one corrections row and the expected verdict each)
        var cases = DataFiles.ReadTable(DataFiles.Repository("tools/data/tests/corrections-cases.tsv"));

        // Act: validate every case row and compare the verdict
        var problems = cases.Rows
            .Select(row => (Case: cases.Cell(row, "case_id"), Expected: cases.Cell(row, "result"), Actual: CorrectionTables.RowError(cases, row) ?? "valid"))
            .Where(verdict => verdict.Expected != verdict.Actual)
            .Select(verdict => $"{verdict.Case}: expected {verdict.Expected}, got {verdict.Actual}")
            .ToList();

        // Assert: the cases are not empty, cover valid and invalid rows, and every verdict agrees
        Assert.True(cases.Rows.Count > 20);
        Assert.Contains("valid", cases.Column("result"));
        Assert.Contains("no-change", cases.Column("result"));
        Assert.Empty(problems);
    }

    /// <summary>
    ///     Proves that the field validation of the candidate rows gives the verdicts of the shared cases
    ///     (<c>tools/data/tests/candidate-cases.tsv</c>), which the Python validator of the overlay is also held to; the
    ///     syntax of the fields is validated whether the corrections are pending or applied.
    /// </summary>
    [Fact]
    public void PartOfSpeech_DataCorrections_CandidateCases_MatchSharedFixture()
    {
        // Arrange: the shared cases (one candidate row and the expected verdict each)
        var cases = DataFiles.ReadTable(DataFiles.Repository("tools/data/tests/candidate-cases.tsv"));

        // Act: validate every case row and compare the verdict
        var problems = cases.Rows
            .Select(row => (Case: cases.Cell(row, "case_id"), Expected: cases.Cell(row, "result"), Actual: CorrectionTables.CandidateError(cases, row) ?? "valid"))
            .Where(verdict => verdict.Expected != verdict.Actual)
            .Select(verdict => $"{verdict.Case}: expected {verdict.Expected}, got {verdict.Actual}")
            .ToList();

        // Assert: the cases are not empty, cover valid rows and every error code, and every verdict agrees
        Assert.True(cases.Rows.Count > 25);
        var results = cases.Column("result").ToList();
        foreach (var code in new[] { "valid", "bad-source", "bad-split", "bad-index", "bad-token", "bad-span", "bad-current-state", "bad-rule-or-hint" })
        {
            Assert.Contains(code, results);
        }

        Assert.Empty(problems);
    }

    /// <summary>
    ///     Proves that every correction row and every exception row addresses an existing token of a committed corpus
    ///     (sentence id, document id, token index and form), and that the correction rows match the corpora either all
    ///     in the pending state (old tag and status) or all in the applied state (new tag and status).
    /// </summary>
    [Fact]
    public void PartOfSpeech_DataCorrections_Rows_MatchCommittedCorpora()
    {
        // Arrange: the correction tables and the exceptions, and the sentences that their rows name
        var corrections = CorrectionTables.CorrectionTableNames
            .Where(name => File.Exists(CorrectionTables.FilePath(name)))
            .Select(name => DataFiles.ReadTable(CorrectionTables.FilePath(name)))
            .ToList();
        var exceptions = DataFiles.ReadTable(CorrectionTables.FilePath("exceptions.tsv"));
        var sentences = CorrectionTables.ReadNamedSentences(corrections.Concat(new[] { exceptions }));
        var problems = new List<string>();
        var pending = 0;
        var applied = 0;

        // Act: find the token of every row and the state of the corpus at that token
        foreach (var table in corrections)
        {
            foreach (var row in table.Rows)
            {
                var (token, problem) = CorrectionTables.FindToken(sentences, table, row, 1);
                var state = token == null ? null : CorrectionState(table, row, token);
                pending += state == "pending" ? 1 : 0;
                applied += state == "applied" ? 1 : 0;
                if (state == null)
                {
                    problems.Add(problem ?? $"{table.Cell(row, "sent_id")}#{table.Cell(row, "token_index")}: the corpus matches neither the old nor the new value");
                }
            }
        }

        problems.AddRange(exceptions.Rows.Select(row => CorrectionTables.FindToken(sentences, exceptions, row, 1).Problem).OfType<string>());

        // Assert: there are rows, all of one state, and every row addresses its token
        Assert.True(pending + applied > 0);
        Assert.True(pending == 0 || applied == 0, $"rows of both states: {pending} pending and {applied} applied");
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that every candidate row addresses an existing token of a committed corpus (sentence id, document id,
    ///     token index and form) and that its span stays inside the sentence.
    /// </summary>
    [Fact]
    public void PartOfSpeech_DataCorrections_Candidates_AddressExistingTokens()
    {
        // Arrange: the candidates and the sentences that they name
        var candidates = DataFiles.ReadTable(CorrectionTables.FilePath("candidates.tsv"));
        var sentences = CorrectionTables.ReadNamedSentences(new[] { candidates });

        // Act: find the token of every row, with room for the whole span
        var problems = candidates.Rows
            .Select(row => CorrectionTables.FindToken(sentences, candidates, row, int.Parse(candidates.Cell(row, "span_length"), CultureInfo.InvariantCulture)).Problem)
            .OfType<string>()
            .ToList();

        // Assert: there are candidates and every one addresses its token
        Assert.True(candidates.Rows.Count > 0);
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the training corpora and the token-level evaluation corpora use only the 17 Universal POS tags
    ///     (the placeholder only for masked tokens).
    /// </summary>
    [Fact]
    public void PartOfSpeech_TrainingData_Corpora_UseOnlySeventeenUposTags()
    {
        // Arrange: the corpora and a place for the problems
        var problems = new List<string>();
        var sentences = 0;

        // Act: check the tag of every token of every sentence
        foreach (var corpus in TrainingCorpora.Concat(EvaluationCorpora))
        {
            foreach (var sentence in DataFiles.ReadCorpus(DataFiles.Data(corpus)))
            {
                sentences++;
                problems.AddRange(TagProblems(corpus, sentence));
            }
        }

        // Assert: the corpora are not empty and no problem was found
        Assert.True(sentences > 1000);
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that in the training corpora and the token-level evaluation corpora the tokens of every sentence
    ///     concatenate to its text.
    /// </summary>
    [Fact]
    public void PartOfSpeech_TrainingData_Corpora_TokensRejoinToSentenceText()
    {
        // Arrange: the corpora and a place for the problems
        var problems = new List<string>();
        var sentences = 0;

        // Act: compare the tokens of every sentence with its text
        foreach (var corpus in TrainingCorpora.Concat(EvaluationCorpora))
        {
            foreach (var sentence in DataFiles.ReadCorpus(DataFiles.Data(corpus)))
            {
                sentences++;
                var rejoined = string.Concat(sentence.Tokens.Select(token => token[0]));
                if (rejoined != sentence.Text.Replace(" ", string.Empty))
                {
                    problems.Add($"{corpus}: {sentence.SentenceId}: tokens do not rejoin to the text");
                }
            }
        }

        // Assert: the corpora are not empty and no problem was found
        Assert.True(sentences > 1000);
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the single-target evaluation sets (gold v2 and the shape probe) use only the documented coarse
    ///     classes that the Universal POS tags map to.
    /// </summary>
    [Fact]
    public void PartOfSpeech_EvaluationData_SingleTargetSets_UseOnlyDocumentedClasses()
    {
        // Arrange: the class columns of the gold sets and of the probe
        var classes = new[] { "dev.tsv", "test.tsv" }
            .SelectMany(name => DataFiles.ReadTable(DataFiles.Data("eval/gold-v2/" + name)).Column("pos"))
            .Concat(DataFiles.ReadTable(DataFiles.Data("eval/shapes/probe.tsv")).Column("gold"))
            .ToList();

        // Act: find the values that are not a documented class
        var unknown = classes.Where(value => Array.IndexOf(CoarseClasses, value) < 0).Distinct().ToList();

        // Assert: every target is labeled with a documented class
        Assert.True(classes.Count > 1000);
        Assert.Empty(unknown);
    }

    /// <summary>
    ///     Proves that no training document is also an evaluation document: the source ids of the training corpora are
    ///     disjoint from the document ids of the evaluation sets.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Leakage_Documents_TrainingAndEvaluationAreDisjoint()
    {
        // Arrange: the document ids of all evaluation sets
        var evaluation = EvaluationItems();
        var documents = new HashSet<string>(evaluation.Select(item => item.Document), StringComparer.Ordinal);

        // Act: compare the document of every training sentence with the evaluation documents
        var problems = new List<string>();
        var sentences = 0;
        foreach (var corpus in TrainingCorpora)
        {
            foreach (var sentence in DataFiles.ReadCorpus(DataFiles.Data(corpus)))
            {
                sentences++;
                if (documents.Contains(sentence.SourceId))
                {
                    problems.Add($"{corpus}: document {sentence.SourceId} is also an evaluation document");
                }
            }
        }

        // Assert: the sets are not empty and nothing is shared
        Assert.True(documents.Count > 2000);
        Assert.True(sentences > 10000);
        Assert.Empty(problems.Distinct().Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that no training sentence, of any length, is also an evaluation sentence: the sentence keys (lower-case
    ///     letters and digits) of the training corpora are disjoint from those of the evaluation sets.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Leakage_Sentences_TrainingAndEvaluationAreDisjoint()
    {
        // Arrange: the sentence keys of all evaluation sets (a text without a letter or digit has no key)
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in EvaluationItems())
        {
            var key = DataFiles.SentenceKey(item.Text);
            if (key.Length > 0)
            {
                keys[key] = item.Set;
            }
        }

        // Act: compare every training sentence with the evaluation sentences
        var problems = new List<string>();
        var sentences = 0;
        foreach (var corpus in TrainingCorpora)
        {
            foreach (var sentence in DataFiles.ReadCorpus(DataFiles.Data(corpus)))
            {
                sentences++;
                if (keys.TryGetValue(DataFiles.SentenceKey(sentence.Text), out var set))
                {
                    problems.Add($"{corpus}: sentence {sentence.SentenceId} is also in {set}");
                }
            }
        }

        // Assert: the sets are not empty and nothing is shared
        Assert.True(keys.Count > 5000);
        Assert.True(sentences > 10000);
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the sentence key of the leakage rule keeps only the lower-case letters and digits of a sentence, so
    ///     that a short sentence has a key as well.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Leakage_SentenceKey_KeepsLowerCaseLettersAndDigits()
    {
        // Arrange: a sentence with capitals, punctuation, spaces and digits, and a short sentence
        const string sentence = "X, Y! 4 2.";
        const string shortSentence = "Thank you.";

        // Act: compute the keys
        var key = DataFiles.SentenceKey(sentence);
        var shortKey = DataFiles.SentenceKey(shortSentence);

        // Assert: only the lower-case letters and digits remain
        Assert.Equal("xy42", key);
        Assert.Equal("thankyou", shortKey);
    }

    /// <summary>
    ///     Proves that the committed leakage report covers every training set against every evaluation set (UD English-EWT
    ///     included) and records no document overlap and no exact sentence overlap for any pair.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Leakage_Report_RecordsNoOverlaps()
    {
        // Arrange: read the report (training set, evaluation set, sentences, document overlaps, exact overlaps, near duplicates)
        var rows = File.ReadLines(DataFiles.Data("leakage-report.tsv"))
            .Where(line => line.Length > 0 && !DataFiles.IsComment(line))
            .Select(line => line.Split('\t'))
            .ToList();

        // Act: find the pairs that report an overlap, and the training sets that the report covers
        var overlaps = rows.Where(row => row[3] != "0" || row[4] != "0").Select(row => row[0] + " vs " + row[1]).ToList();
        var trainingSets = rows.Select(row => row[0]).Distinct().OrderBy(name => name, StringComparer.Ordinal).ToList();

        // Assert: every training set is reported against every evaluation set, and no pair overlaps
        Assert.Equal(new[] { "masc", "open-train", "silver" }, trainingSets);
        foreach (var training in trainingSets)
        {
            var evaluationSets = rows.Where(row => row[0] == training).Select(row => row[1]).Distinct(StringComparer.Ordinal);
            var expected = new[]
            {
                "gold-v2", "open-dev", "open-test", "shapes-probe", "ste-challenge", "ud-ewt-dev", "ud-ewt-test", "ud-ewt-train"
            };
            Assert.Empty(expected.Except(evaluationSets));
        }

        Assert.All(rows, row => Assert.Equal(6, row.Length));
        Assert.Empty(overlaps);
    }

    /// <summary>
    ///     Proves that the lexicon allows the gold tag for at least 99.9 percent of the non-disputed tokens of the open
    ///     training set.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Lexicon_Coverage_Train_MeetsThreshold()
    {
        // Arrange: read the lexicon
        var lexicon = DataFiles.ReadLexicon(DataFiles.Data("lexicon/lexicon.tsv"));

        // Act: count the non-disputed tokens whose gold tag the lexicon allows
        var (tokens, covered) = MeasureCoverage("open/train.tsv", lexicon);

        // Assert: the coverage is at least 99.9 percent
        Assert.True(tokens > 100000, "open/train.tsv");
        Assert.True(covered * 1000 >= tokens * 999, $"open/train.tsv: {covered} of {tokens} tokens are covered");
    }

    /// <summary>
    ///     Proves that the lexicon allows the gold tag for at least 99.9 percent of the non-disputed tokens of the open
    ///     development set.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Lexicon_Coverage_Dev_MeetsThreshold()
    {
        // Arrange: read the lexicon
        var lexicon = DataFiles.ReadLexicon(DataFiles.Data("lexicon/lexicon.tsv"));

        // Act: count the non-disputed tokens whose gold tag the lexicon allows
        var (tokens, covered) = MeasureCoverage("open/dev.tsv", lexicon);

        // Assert: the coverage is at least 99.9 percent
        Assert.True(tokens > 20000, "open/dev.tsv");
        Assert.True(covered * 1000 >= tokens * 999, $"open/dev.tsv: {covered} of {tokens} tokens are covered");
    }

    /// <summary>
    ///     Proves that the lexicon rows are valid (a spelling and alphabetical Universal POS tags), sorted by UTF-8 bytes
    ///     and unique.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Lexicon_Format_RowsAreSortedUniqueAndValid()
    {
        // Arrange: the lexicon rows and a place for the problems
        var rows = LexiconRows();
        var problems = new List<string>();
        string? previous = null;

        // Act: check every row
        foreach (var line in rows)
        {
            var cells = line.Split('\t');
            var tags = cells.Length == 2 ? cells[1].Split(' ') : Array.Empty<string>();
            if (tags.Length == 0 || tags.Any(tag => !DataFiles.UniversalTags.Contains(tag)) ||
                !tags.SequenceEqual(tags.OrderBy(tag => tag, StringComparer.Ordinal).Distinct()))
            {
                problems.Add("invalid row: " + line);
            }
            else if (previous != null && CompareUtf8(previous, cells[0]) >= 0)
            {
                problems.Add("not sorted or not unique: " + cells[0]);
            }

            previous = cells[0];
        }

        // Assert: the lexicon is not empty and every row is valid
        Assert.True(rows.Count > 200000);
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the counts of spellings and tag entries in the header of the lexicon are the counts of its rows.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Lexicon_Format_HeaderCountsMatchRows()
    {
        // Arrange: the lexicon rows and the counts that the header states
        var lines = File.ReadLines(DataFiles.Data("lexicon/lexicon.tsv")).ToList();
        var rows = LexiconRows();
        var header = Regex.Match(string.Join("\n", lines.Where(DataFiles.IsComment)), @"spellings=(\d+), tag entries=(\d+)");

        // Act: count the spellings and the tag entries of the rows
        var spellings = rows.Count;
        var entries = rows.Sum(line => line.Substring(line.IndexOf('\t') + 1).Split(' ').Length);

        // Assert: the header states the counts of the rows
        Assert.True(header.Success);
        Assert.Equal(spellings, int.Parse(header.Groups[1].Value, CultureInfo.InvariantCulture));
        Assert.Equal(entries, int.Parse(header.Groups[2].Value, CultureInfo.InvariantCulture));
    }

    /// <summary>
    ///     Proves that the token-shape rules that decide which tags the coverage check allows work as documented.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Lexicon_TokenShapeRules_AllowTagsByShape()
    {
        // Arrange: a small lexicon
        var lexicon = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["the"] = new[] { "DET" },
            ["."] = new[] { "PUNCT" },
            ["3rd"] = new[] { "ADJ" }
        };

        // Act / Assert: an unknown spelling allows every tag
        Assert.True(DataFiles.UniversalTags.All(tag => DataFiles.IsTagAllowed("unlisted", tag, lexicon)));

        // A known spelling allows its tags, ignoring case, and no others
        Assert.True(DataFiles.IsTagAllowed("THE", "DET", lexicon) && !DataFiles.IsTagAllowed("the", "VERB", lexicon));

        // A capitalized token also allows PROPN and X
        Assert.True(DataFiles.IsTagAllowed("The", "PROPN", lexicon) && DataFiles.IsTagAllowed("The", "X", lexicon));

        // A token without letter or digit also allows SYM and X, but not NOUN
        Assert.True(DataFiles.IsTagAllowed(".", "SYM", lexicon) && !DataFiles.IsTagAllowed(".", "NOUN", lexicon));

        // A token with a digit also allows NUM, but not VERB
        Assert.True(DataFiles.IsTagAllowed("3rd", "NUM", lexicon) && !DataFiles.IsTagAllowed("3rd", "VERB", lexicon));
    }

    /// <summary>
    ///     Proves that the lexicon that is embedded in the assembly encodes exactly the spellings and tags of <c>data/lexicon/lexicon.tsv</c>.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Lexicon_Embedded_EncodesLexiconTsv()
    {
        // Arrange: the table as the data tests read it, and the lexicon that the library loads from its embedded resource
        var table = DataFiles.ReadLexicon(DataFiles.Data("lexicon/lexicon.tsv"));
        var embedded = EmbeddedModel.Lexicon;
        var scratch = new byte[embedded.ScratchSize];
        var problems = new List<string>();

        // Act: look every spelling of the table up in the embedded lexicon and compare the tags
        foreach (var row in table)
        {
            var key = row.Key.ToCharArray();
            if (!embedded.TryGetTags(key, 0, key.Length, scratch, out var tags) || !tags.Select(t => t.ToTagString()).SequenceEqual(row.Value))
            {
                problems.Add(row.Key);
            }
        }

        // Assert: nothing is missing or different, and the embedded lexicon has no extra spelling
        Assert.Empty(problems.Take(ShownProblems));
        Assert.Equal(table.Count, embedded.SpellingCount);
        Assert.Equal(table.Values.Sum(tags => tags.Length), embedded.EntryCount);
    }

    /// <summary>
    ///     Proves that the weights that are embedded in the assembly are exactly the model that the committed training
    ///     configuration describes: the same file as the committed one, the same hash bits, scale and temperature, and
    ///     the SHA-256 of the uncompressed image that the trainer recorded. A model that was edited or retrained
    ///     without the configuration (or the other way around) fails here in every normal test run; the full retraining
    ///     check is <c>train --check</c> in the build.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Model_EmbeddedWeights_MatchTrainingConfig()
    {
        // Arrange: the embedded resource, the committed file next to the project and the text of the configuration
        using var stream = EmbeddedModel.OpenResource(EmbeddedModel.WeightsResourceName);
        Assert.NotNull(stream);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var embedded = buffer.ToArray();
        var committed = File.ReadAllBytes(DataFiles.Repository("src/DemaConsulting.PartOfSpeech/Resources/weights.bin"));
        var config = File.ReadAllText(DataFiles.Repository(TrainingConfigPath));

        // Act: load the model and hash its uncompressed image
        var model = PerceptronModel.Load(embedded, FeatureExtractor.FeatureSetVersion);
        string hash;
        using (var sha = System.Security.Cryptography.SHA256.Create())
        {
            hash = BitConverter.ToString(sha.ComputeHash(PerceptronModel.ToUncompressedImage(embedded))).Replace("-", string.Empty).ToLowerInvariant();
        }

        // Assert: embedded equals committed, and the header and the image match the configuration
        Assert.Equal(committed, embedded);
        Assert.Equal(ConfigValue(config, "imageSha256"), hash);
        Assert.Equal(int.Parse(ConfigValue(config, "hashBits"), CultureInfo.InvariantCulture), model.HashBits);
        Assert.Equal(1f / int.Parse(ConfigValue(config, "quantScale"), CultureInfo.InvariantCulture), model.Scale);
        Assert.Equal((float)double.Parse(ConfigValue(config, "temperature"), CultureInfo.InvariantCulture), model.Temperature);
    }

    /// <summary>
    ///     Proves that the size figures that the documentation states for the embedded weights (compressed and
    ///     uncompressed bytes and, where a document states it, the number of rows) equal the embedded file. A retrained
    ///     model fails here and the message names the document that must be updated. The run log is excluded on purpose:
    ///     it records the figures of historical runs.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Model_EmbeddedWeights_SizeFiguresInDocs_AreCurrent()
    {
        // Arrange: the figures of the embedded file, formatted as the documents state them
        using var stream = EmbeddedModel.OpenResource(EmbeddedModel.WeightsResourceName);
        Assert.NotNull(stream);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var embedded = buffer.ToArray();
        var model = PerceptronModel.Load(embedded, FeatureExtractor.FeatureSetVersion);
        var compressed = embedded.Length.ToString("N0", CultureInfo.InvariantCulture);
        var uncompressed = PerceptronModel.ToUncompressedImage(embedded).Length.ToString("N0", CultureInfo.InvariantCulture);
        var rows = model.RowCount.ToString("N0", CultureInfo.InvariantCulture);
        var sizeDocuments = new[]
        {
            "README.md",
            "docs/user_guide/introduction.md",
            "data/README.md",
            "src/DemaConsulting.PartOfSpeech/NamespaceDoc.cs",
            "tools/DemaConsulting.PartOfSpeech.Trainer/README.md",
            "docs/design/part-of-speech.md",
            "docs/design/part-of-speech/model-storage.md",
            "docs/design/part-of-speech/model-storage/perceptron-model.md",
            "docs/design/part-of-speech/model-trainer.md",
            "docs/verification/part-of-speech.md"
        };
        var rowDocuments = new[]
        {
            "src/DemaConsulting.PartOfSpeech/NamespaceDoc.cs",
            "tools/DemaConsulting.PartOfSpeech.Trainer/README.md",
            "docs/design/part-of-speech/model-storage.md",
            "docs/design/part-of-speech/model-storage/perceptron-model.md",
            "docs/design/part-of-speech/model-trainer.md"
        };

        // Act: collect the figures that a document does not state
        var missing = new List<string>();
        foreach (var path in sizeDocuments)
        {
            var text = Collapse(File.ReadAllText(DataFiles.Repository(path)));
            missing.AddRange(MissingFragments(text, new[] { compressed, uncompressed }).Select(figure => $"{path}: {figure}"));
        }

        foreach (var path in rowDocuments)
        {
            var text = Collapse(File.ReadAllText(DataFiles.Repository(path)));
            missing.AddRange(MissingFragments(text, new[] { rows }).Select(figure => $"{path}: {figure} rows"));
        }

        // Assert: every document states the current figures
        Assert.True(missing.Count == 0, "Update the size figures of the embedded weights in: " + string.Join("; ", missing));
    }

    /// <summary>
    ///     Proves that the training configuration names only corpora that are committed in the repository, that are
    ///     training corpora (no development, held-out or restricted data) and whose data folders declare an allowed
    ///     license, and that the third-party notice names the corpora that the weights were trained on.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Model_TrainingConfig_UsesOnlyCommittedTrainingCorpora()
    {
        // Arrange: the corpus paths of the configuration
        var config = File.ReadAllText(DataFiles.Repository(TrainingConfigPath));
        var paths = Regex.Matches(config, "\"path\"\\s*:\\s*\"([^\"]+)\"").Cast<Match>().Select(m => m.Groups[1].Value).ToList();
        var notice = NoticeSection("## Embedded Weights");

        // Act: find the problems of each source
        var problems = new List<string>();
        foreach (var path in paths)
        {
            var relative = path.StartsWith("data/", StringComparison.Ordinal) ? path.Substring("data/".Length) : path;
            if (Array.IndexOf(TrainingCorpora, relative) < 0)
            {
                problems.Add("not a training corpus: " + path);
            }
            else if (!File.Exists(DataFiles.Repository(path)))
            {
                problems.Add("not committed: " + path);
            }

            if (RestrictedCorpusPath.IsMatch(path))
            {
                problems.Add("restricted corpus: " + path);
            }

            problems.AddRange(LicenseProblems(relative.Substring(0, relative.IndexOf('/'))));
        }

        // Assert: at least one source, no problem, and the notice says which corpora the weights come from
        Assert.NotEmpty(paths);
        Assert.Empty(problems.Take(ShownProblems));
        Assert.Empty(MissingFragments(notice, new[] { "MASC", "CC BY 3.0 US", "open corpus", "silver corpus" }));
    }

    /// <summary>
    ///     Proves that the project file names the combined package license file instead of a license expression, packs
    ///     the file at the package root, and that the file exists.
    /// </summary>
    [Fact]
    public void PartOfSpeech_PackageLicense_ProjectFile_NamesLicenseFileAtPackageRoot()
    {
        // Arrange: load the project file of the library
        var projectFile = DataFiles.Repository("src/DemaConsulting.PartOfSpeech/DemaConsulting.PartOfSpeech.csproj");
        var project = XDocument.Load(projectFile);

        // Act: find the license property and the items that include the license file
        var licenseFile = project.Descendants("PackageLicenseFile").Select(e => e.Value.Trim()).ToList();
        var expressions = project.Descendants("PackageLicenseExpression").ToList();
        var items = project.Descendants("None")
            .Where(item => ((string?)item.Attribute("Include") ?? string.Empty).EndsWith(PackageLicenseName, StringComparison.Ordinal))
            .ToList();

        // Assert: a license file and no expression (a package has one of them); the file is packed at the root
        Assert.Equal(new[] { PackageLicenseName }, licenseFile);
        Assert.Empty(expressions);
        var item = Assert.Single(items);
        var include = ((string?)item.Attribute("Include"))!.Replace('\\', '/');
        Assert.True(File.Exists(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectFile)!, include))));
        Assert.Equal("true", (string?)item.Attribute("Pack"));
        Assert.Equal("/", ((string?)item.Attribute("PackagePath"))!.Replace('\\', '/'));
    }

    /// <summary>
    ///     Proves that the package license starts with the SPDX expression of the whole package, contains the MIT license
    ///     text of the repository for the code, covers the two embedded data files with their licenses, and points to
    ///     the third-party notice.
    /// </summary>
    [Fact]
    public void PartOfSpeech_PackageLicense_File_CoversCodeAndEmbeddedData()
    {
        // Arrange: the package license and the license of the repository, with white space collapsed
        var text = Collapse(File.ReadAllText(DataFiles.Repository(PackageLicenseName)));
        var repositoryLicense = Collapse(File.ReadAllText(DataFiles.Repository("LICENSE"))).Trim();
        var required = new[]
        {
            "## Code: MIT License",
            "lexicon.bin",
            "weights.bin",
            "Open English WordNet",
            "Creative Commons Attribution 4.0 International",
            "https://creativecommons.org/licenses/by/4.0/",
            "Manually Annotated Sub-Corpus (MASC)",
            "Creative Commons Attribution 3.0 United States",
            "https://creativecommons.org/licenses/by/3.0/us/",
            "THIRD-PARTY-NOTICES.md"
        };

        // Act: find what the file lacks
        var missing = MissingFragments(text, required);

        // Assert: the SPDX line first, the MIT text of the repository, and every required statement
        Assert.StartsWith("SPDX-License-Identifier: MIT AND CC-BY-4.0 AND CC-BY-3.0-US ", text, StringComparison.Ordinal);
        Assert.Contains(repositoryLicense, text, StringComparison.Ordinal);
        Assert.Empty(missing);
    }

    /// <summary>
    ///     Proves that the third-party notice covers the embedded weights: the MASC section says that the weights were
    ///     learned from the converted MASC data and are provided under its license, and a section of their own names the
    ///     trainer and the corpora.
    /// </summary>
    [Fact]
    public void PartOfSpeech_ThirdPartyNotices_Weights_AreCovered()
    {
        // Arrange: the sections of the notice that must cover the weights
        var masc = NoticeSection("## The Manually Annotated Sub-Corpus (MASC)");
        var weights = NoticeSection("## Embedded Weights");

        // Act: find the statements that the sections lack
        var missing = MissingFragments(masc, new[] { "weights of the tagger (`weights.bin`) were learned in part from the converted data", "provided under the terms of this license" })
            .Concat(MissingFragments(weights, new[] { "Resources/weights.bin", "tools/DemaConsulting.PartOfSpeech.Trainer", "`training.json`", "`RUN-LOG.md`", "the attribution of MASC applies to the weights" }))
            .ToList();

        // Assert: nothing is missing
        Assert.Empty(missing);
    }

    /// <summary>
    ///     Gets the value of a top-level setting of the training configuration, without a JSON parser (which .NET
    ///     Framework does not have in the box).
    /// </summary>
    private static string ConfigValue(string config, string name)
    {
        var match = Regex.Match(config, "\"" + name + "\"\\s*:\\s*\"?([^\",\\s}]+)");
        Assert.True(match.Success, "training.json has no setting " + name);
        return match.Groups[1].Value;
    }

    /// <summary>
    ///     Collapses every run of white space to one space.
    /// </summary>
    private static string Collapse(string text) => Regex.Replace(text, @"\s+", " ");

    /// <summary>
    ///     Gets one section of the third-party notice, from its heading to the next heading of the same level, with white
    ///     space collapsed so that line wrapping does not matter.
    /// </summary>
    /// <param name="heading">The Markdown heading line, for example <c>## Open English WordNet</c>.</param>
    private static string NoticeSection(string heading)
    {
        var text = File.ReadAllText(DataFiles.Repository("THIRD-PARTY-NOTICES.md")).Replace("\r\n", "\n");
        var start = text.IndexOf("\n" + heading + "\n", StringComparison.Ordinal);
        Assert.True(start >= 0, "THIRD-PARTY-NOTICES.md has no section " + heading);
        var level = heading.IndexOf(' ');
        var next = Regex.Match(text.Substring(start + 1), @"\n#{1," + level.ToString(CultureInfo.InvariantCulture) + "} ");
        return Collapse(next.Success ? text.Substring(start, next.Index + 1) : text.Substring(start));
    }

    /// <summary>
    ///     Finds the required fragments that a text lacks, ignoring differences in white space.
    /// </summary>
    private static List<string> MissingFragments(string text, IEnumerable<string> required) =>
        required.Where(fragment => text.IndexOf(Collapse(fragment), StringComparison.Ordinal) < 0).ToList();

    /// <summary>
    ///     Determines whether a folder is left out of the search for restricted files: repository metadata, dependencies,
    ///     build output and the git-ignored download cache of the data tools.
    /// </summary>
    private static bool IsIgnoredFolder(string folder)
    {
        var ignored = new[] { ".git", "node_modules", ".venv", "bin", "obj", "__pycache__", "TestResults", "artifacts", ".agent-logs" };
        return Array.IndexOf(ignored, Path.GetFileName(folder)) >= 0 || DataFiles.RelativePath(folder) == "tools/data/cache";
    }

    /// <summary>
    ///     Finds the problems of the license declarations of a data README.
    /// </summary>
    private static IEnumerable<string> LicenseProblems(string folder)
    {
        var readme = DataFiles.Data(folder + "/README.md");
        if (!File.Exists(readme))
        {
            return new[] { "missing README: data/" + folder };
        }

        var licenses = LicenseLine.Matches(File.ReadAllText(readme)).Cast<Match>().Select(match => match.Groups[1].Value).ToList();
        if (licenses.Count == 0)
        {
            return new[] { "no license declared: data/" + folder };
        }

        return licenses.Where(license => Array.IndexOf(AllowedLicenses, license) < 0).Select(license => $"data/{folder}: license {license} is not allowed");
    }

    /// <summary>
    ///     Reads the document ids of a source table.
    /// </summary>
    private static HashSet<string> SourceIds(string table, string idColumn) =>
        new(DataFiles.ReadTable(DataFiles.Data(table)).Column(idColumn), StringComparer.Ordinal);

    /// <summary>
    ///     Finds the documents of a data set that its source list does not name.
    /// </summary>
    private static IEnumerable<string> MissingDocuments(string label, IEnumerable<string> documents, HashSet<string> listed) =>
        documents.Distinct().Where(document => !listed.Contains(document)).Select(document => $"{label}: not in the source list: {document}");

    /// <summary>
    ///     Finds the problems of the MASC source table: the id and genre of every row, the license class, and the
    ///     sentence and token counts, which must equal the counts of the document in <c>masc.tsv</c> (and so sum to the
    ///     totals of the file).
    /// </summary>
    private static IEnumerable<string> MascSourceProblems()
    {
        var counts = DataFiles.ReadCorpus(DataFiles.Data("masc/masc.tsv"))
            .GroupBy(sentence => sentence.SourceId)
            .ToDictionary(group => group.Key, group => (Sentences: group.Count(), Tokens: group.Sum(sentence => sentence.Tokens.Count)), StringComparer.Ordinal);
        var data = DataFiles.ReadTable(DataFiles.Data("masc/sources.tsv"));
        var sentenceTotal = 0;
        var tokenTotal = 0;
        foreach (var row in data.Rows)
        {
            var id = data.Cell(row, "source_id");
            var match = MascDocument.Match(id);
            if (!match.Success || match.Groups[1].Value != data.Cell(row, "genre"))
            {
                yield return $"data/masc/sources.tsv: {id}: the id is malformed or does not match the genre '{data.Cell(row, "genre")}'";
            }

            if (data.Cell(row, "license_basis") != MascLicenseBasis)
            {
                yield return $"data/masc/sources.tsv: {id}: license class is not {MascLicenseBasis}";
            }

            var sentences = int.TryParse(data.Cell(row, "sentences"), NumberStyles.None, CultureInfo.InvariantCulture, out var s) ? s : 0;
            var tokens = int.TryParse(data.Cell(row, "tokens"), NumberStyles.None, CultureInfo.InvariantCulture, out var t) ? t : 0;
            if (sentences <= 0 || tokens <= 0)
            {
                yield return $"data/masc/sources.tsv: {id}: the sentence or token count is not a positive number";
            }

            if (!counts.TryGetValue(id, out var actual) || actual.Sentences != sentences || actual.Tokens != tokens)
            {
                yield return $"data/masc/sources.tsv: {id}: the counts differ from masc.tsv";
            }

            sentenceTotal += sentences;
            tokenTotal += tokens;
        }

        if (sentenceTotal != counts.Values.Sum(count => count.Sentences) || tokenTotal != counts.Values.Sum(count => count.Tokens))
        {
            yield return "data/masc/sources.tsv: the counts do not sum to the totals of masc.tsv";
        }
    }

    /// <summary>
    ///     Finds the problems of one row of a source table.
    /// </summary>
    private static IEnumerable<string> SourceRowProblems(string table, DataTable data, string[] row, string idColumn)
    {
        var id = data.Cell(row, idColumn);
        var license = data.Cell(row, "license_basis");
        if (!AllowedLicenseBases.Any(basis => license.StartsWith(basis, StringComparison.Ordinal)))
        {
            yield return $"data/{table}: {id}: license class is not allowed: {license.Substring(0, Math.Min(60, license.Length))}";
        }

        var url = data.Cell(row, "url");
        if (url.Length == 0 ? data.Cell(row, "family") != "project-written" : !IsWellFormedUrl(url))
        {
            yield return $"data/{table}: {id}: url is empty or malformed: '{url}'";
        }

        if (Array.IndexOf(data.Columns, "terms_url") >= 0 && !IsWellFormedUrl(data.Cell(row, "terms_url")))
        {
            yield return $"data/{table}: {id}: terms_url is empty or malformed";
        }

        if (!int.TryParse(data.Cell(row, "sentences"), NumberStyles.None, CultureInfo.InvariantCulture, out var sentences) || sentences <= 0)
        {
            yield return $"data/{table}: {id}: the sentence count is not a positive number";
        }
    }

    /// <summary>
    ///     Determines whether a text is an absolute HTTPS URL without white space.
    /// </summary>
    private static bool IsWellFormedUrl(string text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && !text.Any(char.IsWhiteSpace);

    /// <summary>
    ///     Reads the rows of the checksum manifest: path, bytes, SHA-256, lines and sentences.
    /// </summary>
    private static List<string[]> ReadManifest() =>
        File.ReadLines(DataFiles.Data("MANIFEST.tsv"))
            .Where(line => line.Length > 0 && !DataFiles.IsComment(line))
            .Select(line => line.Split('\t'))
            .ToList();

    /// <summary>
    ///     Lists the data files that the manifest covers, relative to the data folder: all files except Markdown files,
    ///     the manifest itself and the git-ignored local FAA file.
    /// </summary>
    private static List<string> ManifestedFilesOnDisk()
    {
        var dataRoot = DataFiles.Data(string.Empty).TrimEnd('/', '\\');
        return DataFiles.EnumerateFiles(dataRoot, _ => false)
            .Select(file => DataFiles.RelativePath(file).Substring("data/".Length))
            .Where(path => path != "MANIFEST.tsv" && !path.EndsWith(".md", StringComparison.Ordinal) && path != "silver/silver-faa.tsv")
            .ToList();
    }

    /// <summary>
    ///     Finds the differences between a manifest row and the file it describes.
    /// </summary>
    private static IEnumerable<string> ChecksumProblems(string[] row)
    {
        var path = DataFiles.Data(row[0]);
        if (new FileInfo(path).Length.ToString(CultureInfo.InvariantCulture) != row[1])
        {
            yield return "size differs: " + row[0];
        }

        if (!string.Equals(DataFiles.Sha256Hex(path), row[2], StringComparison.Ordinal))
        {
            yield return "SHA-256 differs: " + row[0];
        }
    }

    /// <summary>
    ///     Finds the tokens of one sentence of a token-level corpus that do not carry a Universal POS tag.
    /// </summary>
    private static IEnumerable<string> TagProblems(string corpus, CorpusSentence sentence)
    {
        foreach (var token in sentence.Tokens)
        {
            var valid = token.Length >= 3 &&
                        CorpusStatus.TryParse(token[2], out var status, out _) &&
                        (DataFiles.UniversalTags.Contains(token[1]) || (token[1] == "_" && status == "masked"));
            if (!valid)
            {
                yield return $"{corpus}: {sentence.SentenceId}: invalid token row '{string.Join("|", token)}'";
            }
        }
    }

    /// <summary>
    ///     Counts the non-disputed tokens of a corpus and those whose gold tag the lexicon allows.
    /// </summary>
    private static (long Tokens, long Covered) MeasureCoverage(string corpus, IReadOnlyDictionary<string, string[]> lexicon)
    {
        long tokens = 0;
        long covered = 0;
        foreach (var token in DataFiles.ReadCorpus(DataFiles.Data(corpus)).SelectMany(sentence => sentence.Tokens))
        {
            if (CorpusStatus.BaseStatus(token[2]) == "disputed")
            {
                continue;
            }

            tokens++;
            if (DataFiles.IsTagAllowed(token[0], token[1], lexicon))
            {
                covered++;
            }
        }

        return (tokens, covered);
    }

    /// <summary>
    ///     Reads the rows of the lexicon (every line that is not empty and not a comment).
    /// </summary>
    private static List<string> LexiconRows() =>
        File.ReadLines(DataFiles.Data("lexicon/lexicon.tsv")).Where(line => line.Length > 0 && !DataFiles.IsComment(line)).ToList();

    /// <summary>
    ///     Reads the sets, documents and sentences of all evaluation sets.
    /// </summary>
    private static List<(string Set, string Document, string Text)> EvaluationItems()
    {
        var items = new List<(string Set, string Document, string Text)>();
        foreach (var corpus in EvaluationCorpora)
        {
            items.AddRange(DataFiles.ReadCorpus(DataFiles.Data(corpus)).Select(s => (corpus, s.SourceId, s.Text)));
        }

        foreach (var name in new[] { "dev.tsv", "test.tsv" })
        {
            var gold = DataFiles.ReadTable(DataFiles.Data("eval/gold-v2/" + name));
            items.AddRange(gold.Column("doc_id").Zip(gold.Column("sentence"), (doc, text) => ("gold-v2/" + name, doc, text)));
        }

        var probe = DataFiles.ReadTable(DataFiles.Data("eval/shapes/probe.tsv"));
        items.AddRange(probe.Column("id").Zip(probe.Column("sentence"), (id, text) => ("shapes/probe.tsv", id, text)));
        return items;
    }

    /// <summary>
    ///     Compares the corpus token of a correction row with the row: the state of the corpus at that token.
    /// </summary>
    /// <returns><c>pending</c> (the old value), <c>applied</c> (the new value) or <c>null</c> (neither).</returns>
    private static string? CorrectionState(DataTable table, string[] row, string[] token)
    {
        if (token[1] == table.Cell(row, "old_tag") && token[2] == table.Cell(row, "old_status"))
        {
            return "pending";
        }

        return token[1] == table.Cell(row, "new_tag") && token[2] == table.Cell(row, "new_status") ? "applied" : null;
    }

    /// <summary>
    ///     Compares two strings by the bytes of their UTF-8 encoding.
    /// </summary>
    private static int CompareUtf8(string left, string right)
    {
        var x = Encoding.UTF8.GetBytes(left);
        var y = Encoding.UTF8.GetBytes(right);
        for (var i = 0; i < Math.Min(x.Length, y.Length); i++)
        {
            if (x[i] != y[i])
            {
                return x[i] < y[i] ? -1 : 1;
            }
        }

        return x.Length.CompareTo(y.Length);
    }
}
