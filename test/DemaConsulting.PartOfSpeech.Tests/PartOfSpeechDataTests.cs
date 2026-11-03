using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DemaConsulting.PartOfSpeech.Tests;

/// <summary>
///     System-level tests of the committed data and licensing artifacts: the third-party notice and its packaging, the
///     licenses and provenance of the data, the Universal POS tags, the separation of training and evaluation data, and
///     the lexicon. The tests read the files of the working copy and need no network.
/// </summary>
public class PartOfSpeechDataTests
{
    /// <summary>
    ///     The number of problems that an assertion shows, so that a broken file does not flood the test output.
    /// </summary>
    private const int ShownProblems = 20;

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
        "open", "masc", "silver", "eval/shapes", "eval/gold-v2", "eval/ste-challenge", "lexicon"
    };

    /// <summary>
    ///     The tables that record the license basis of every source document, relative to the data folder.
    /// </summary>
    private static readonly string[] SourceTables =
    {
        "open/sources.tsv", "eval/gold-v2/sources.tsv", "silver/sources.tsv"
    };

    /// <summary>
    ///     The license identifiers that a data folder may declare.
    /// </summary>
    private static readonly string[] AllowedLicenses = { "CC0-1.0", "CC-BY-3.0-US", "CC-BY-4.0", "Public-Domain", "MIT" };

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
    ///     Text that must not appear in the license basis of any source document.
    /// </summary>
    private static readonly Regex RestrictedLicenseText = new(
        @"share-?alike|by-sa|by-nc|by-nd|non-?commercial|no-?derivatives?|cc by|wikipedia|\.org/licenses/by",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    ///     A path of a restricted corpus: SemCor, the Brown corpus or the English Web Treebank.
    /// </summary>
    private static readonly Regex RestrictedCorpusPath = new("semcor|brown|ewt", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    ///     Proves that the third-party notice attributes MASC and Open English WordNet with title, creator, source link,
    ///     license link and the statement of conversion, and reproduces the Princeton WordNet license.
    /// </summary>
    [Fact]
    public void PartOfSpeech_ThirdPartyNotices_Content_AttributesMascAndOpenEnglishWordNet()
    {
        // Arrange: read the notice with whitespace collapsed, so that line wrapping does not matter
        var notices = Collapse(File.ReadAllText(DataFiles.Repository("THIRD-PARTY-NOTICES.md")));
        var required = new[]
        {
            // MASC: title, creator, source link, license link, citation, conversion
            "The Manually Annotated Sub-Corpus (MASC)",
            "Nancy Ide",
            "Collin Baker",
            "Christiane Fellbaum",
            "Rebecca Passonneau",
            "https://anc.org/data/masc/",
            "Creative Commons Attribution 3.0 United States",
            "https://creativecommons.org/licenses/by/3.0/us/",
            "https://aclanthology.org/L08-1105/",
            "converted to Universal POS (UPOS) tags",
            // Open English WordNet: title, creator, source link, license link, conversion
            "Open English WordNet 2025 Edition",
            "John P. McCrae",
            "https://en-word.net",
            "https://github.com/globalwordnet/english-wordnet",
            "Creative Commons Attribution 4.0 International",
            "https://creativecommons.org/licenses/by/4.0/",
            "maps a lower-case spelling to the UPOS tags it may carry",
            // Princeton WordNet license
            "WordNet 3.1 Copyright 2011 by Princeton University. All rights reserved.",
            "THIS SOFTWARE AND DATABASE IS PROVIDED \"AS IS\" AND PRINCETON UNIVERSITY MAKES NO REPRESENTATIONS",
            "Title to copyright in this software, database and any associated documentation shall at all times remain " +
            "with Princeton University"
        };

        // Act: find the required elements that the notice lacks
        var missing = required.Where(fragment => notices.IndexOf(Collapse(fragment), StringComparison.Ordinal) < 0);

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
    ///     Proves that every data folder declares an allowed license and that no source document is recorded under a
    ///     share-alike, non-commercial or other restricted license.
    /// </summary>
    [Fact]
    public void PartOfSpeech_DataLicensing_Folders_DeclareAllowedLicensesOnly()
    {
        // Arrange: collect the problems of the license declarations and of the source tables
        var problems = new List<string>();

        // Act: check every data README and every source table
        foreach (var folder in LicensedFolders)
        {
            problems.AddRange(LicenseProblems(folder));
        }

        foreach (var table in SourceTables)
        {
            problems.AddRange(RestrictedTextProblems(table));
        }

        // Assert: no problem was found
        Assert.Empty(problems);
    }

    /// <summary>
    ///     Proves that no UD English-EWT, SemCor or Brown corpus file and no CoNLL-U file is committed.
    /// </summary>
    [Fact]
    public void PartOfSpeech_DataLicensing_Tree_ContainsNoRestrictedCorpusFiles()
    {
        // Arrange: list the files of the repository, without dependencies, build output and the git-ignored download cache
        var files = DataFiles.EnumerateFiles(DataFiles.RepositoryRoot, IsIgnoredFolder).ToList();

        // Act: find CoNLL-U files anywhere and restricted corpus names below the data folder
        var problems = files
            .Where(file => file.EndsWith(".conllu", StringComparison.OrdinalIgnoreCase))
            .Select(file => "CoNLL-U file: " + DataFiles.RelativePath(file))
            .Concat(files
                .Select(DataFiles.RelativePath)
                .Where(path => path.StartsWith("data/", StringComparison.Ordinal) && path != "data/eval/ud-ewt/README.md")
                .Where(path => RestrictedCorpusPath.IsMatch(path))
                .Select(path => "restricted corpus name: " + path))
            .ToList();

        // Assert: nothing restricted is committed, and the UD English-EWT folder holds only its documentation
        Assert.Empty(problems);
        Assert.Equal(
            new[] { "data/eval/ud-ewt/README.md" },
            files.Select(DataFiles.RelativePath).Where(path => path.StartsWith("data/eval/ud-ewt/", StringComparison.Ordinal)));
    }

    /// <summary>
    ///     Proves that every data folder has a README with a license declaration and a record of its source, and that the
    ///     sources of the corpora are listed.
    /// </summary>
    [Fact]
    public void PartOfSpeech_DataProvenance_Folders_HaveReadmeLicenseAndSource()
    {
        // Arrange: collect the problems
        var problems = new List<string>();

        // Act: check the README of every data folder and the source tables
        foreach (var folder in LicensedFolders)
        {
            var readme = DataFiles.Data(folder + "/README.md");
            if (!File.Exists(readme))
            {
                problems.Add("missing README: data/" + folder);
                continue;
            }

            var text = File.ReadAllText(readme);
            if (!LicenseLine.IsMatch(text))
            {
                problems.Add("no license line: data/" + folder);
            }

            if (text.IndexOf("Source", StringComparison.Ordinal) < 0 && text.IndexOf("Produced", StringComparison.Ordinal) < 0)
            {
                problems.Add("no record of the source: data/" + folder);
            }
        }

        problems.AddRange(SourceTables.Where(table => !File.Exists(DataFiles.Data(table))).Select(table => "missing: data/" + table));
        problems.AddRange(new[] { "README.md", "CONVENTIONS.md" }.Where(name => !File.Exists(DataFiles.Data(name))).Select(name => "missing: data/" + name));

        // Assert: no problem was found
        Assert.Empty(problems);
    }

    /// <summary>
    ///     Proves that the checksum manifest lists exactly the committed data files and that each file has the recorded
    ///     size and SHA-256.
    /// </summary>
    [Fact]
    public void PartOfSpeech_DataProvenance_Manifest_ChecksumsMatchCommittedFiles()
    {
        // Arrange: read the manifest (path, bytes, sha256, lines, sentences) and list the data files it covers
        var manifest = File.ReadLines(DataFiles.Data("MANIFEST.tsv"))
            .Where(line => line.Length > 0 && !DataFiles.IsComment(line))
            .Select(line => line.Split('\t'))
            .ToList();
        var dataRoot = DataFiles.Data(string.Empty).TrimEnd('/', '\\');
        var onDisk = DataFiles.EnumerateFiles(dataRoot, _ => false)
            .Select(file => DataFiles.RelativePath(file).Substring("data/".Length))
            .Where(path => path != "MANIFEST.tsv" && !path.EndsWith(".md", StringComparison.Ordinal) && path != "silver/silver-faa.tsv")
            .ToList();

        // Act: compare the file list, the sizes and the hashes
        var problems = onDisk.Except(manifest.Select(row => row[0])).Select(path => "not in the manifest: " + path)
            .Concat(manifest.Select(row => row[0]).Except(onDisk).Select(path => "missing file: " + path))
            .Concat(manifest.Where(row => onDisk.Contains(row[0])).SelectMany(ChecksumProblems))
            .ToList();

        // Assert: the manifest is complete and every file matches
        Assert.NotEmpty(manifest);
        Assert.Empty(problems);
    }

    /// <summary>
    ///     Proves that the training corpora and the token-level evaluation corpora use only the 17 Universal POS tags
    ///     (the placeholder only for masked tokens) and that every sentence is the concatenation of its tokens.
    /// </summary>
    [Fact]
    public void PartOfSpeech_TrainingData_Corpora_UseOnlySeventeenUposTags()
    {
        // Arrange: the corpora and a place for the problems
        var problems = new List<string>();
        var sentences = 0;

        // Act: check every token of every sentence
        foreach (var corpus in TrainingCorpora.Concat(EvaluationCorpora))
        {
            foreach (var sentence in DataFiles.ReadCorpus(DataFiles.Data(corpus)))
            {
                sentences++;
                problems.AddRange(TokenProblems(corpus, sentence));
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
    ///     Proves that no training document and no training sentence of at least 25 letters and digits appears in an
    ///     evaluation set.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Leakage_TrainingVersusEvaluation_NoSharedDocumentOrSentence()
    {
        // Arrange: the documents and sentence keys of all evaluation sets
        var evaluation = EvaluationItems();
        var documents = new HashSet<string>(evaluation.Select(item => item.Document), StringComparer.Ordinal);
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in evaluation)
        {
            var key = DataFiles.SentenceKey(item.Text);
            if (key.Length >= DataFiles.MinimumKeyLength)
            {
                keys[key] = item.Set;
            }
        }

        // Act: compare every training sentence with the evaluation sets
        var problems = new List<string>();
        foreach (var corpus in TrainingCorpora)
        {
            foreach (var sentence in DataFiles.ReadCorpus(DataFiles.Data(corpus)))
            {
                if (documents.Contains(sentence.SourceId))
                {
                    problems.Add($"{corpus}: document {sentence.SourceId} is also an evaluation document");
                }

                if (keys.TryGetValue(DataFiles.SentenceKey(sentence.Text), out var set))
                {
                    problems.Add($"{corpus}: sentence {sentence.SentenceId} is also in {set}");
                }
            }
        }

        // Assert: the evaluation sets are not empty and nothing is shared
        Assert.True(evaluation.Count > 5000);
        Assert.True(keys.Count > 5000);
        Assert.Empty(problems.Take(ShownProblems));
    }

    /// <summary>
    ///     Proves that the sentence key of the leakage rule keeps only the lower-case letters and digits of a sentence.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Leakage_SentenceKey_KeepsLowerCaseLettersAndDigits()
    {
        // Arrange: a sentence with capitals, punctuation, spaces and digits
        const string sentence = "X, Y! 4 2.";

        // Act: compute the key
        var key = DataFiles.SentenceKey(sentence);

        // Assert: only the lower-case letters and digits remain
        Assert.Equal("xy42", key);
    }

    /// <summary>
    ///     Proves that the committed leakage report records no document overlap and no exact sentence overlap for any
    ///     pair of training and evaluation set.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Leakage_Report_RecordsNoOverlaps()
    {
        // Arrange: read the report (training set, evaluation set, sentences, document overlaps, exact overlaps, ...)
        var rows = File.ReadLines(DataFiles.Data("leakage-report.tsv"))
            .Where(line => line.Length > 0 && !DataFiles.IsComment(line))
            .Select(line => line.Split('\t'))
            .ToList();

        // Act: find the pairs that report an overlap, and the training sets that the report covers
        var overlaps = rows.Where(row => row[3] != "0" || row[4] != "0").Select(row => row[0] + " vs " + row[1]).ToList();
        var trainingSets = rows.Select(row => row[0]).Distinct().OrderBy(name => name, StringComparer.Ordinal).ToList();

        // Assert: every training set is reported against every committed evaluation set, and no pair overlaps
        Assert.Equal(new[] { "masc", "open-train", "silver" }, trainingSets);
        foreach (var training in trainingSets)
        {
            var evaluationSets = rows.Where(row => row[0] == training).Select(row => row[1]).Distinct(StringComparer.Ordinal);
            var missingSets = new[] { "gold-v2", "open-dev", "open-test", "shapes-probe", "ste-challenge" }.Except(evaluationSets);
            Assert.Empty(missingSets);
        }
        Assert.All(rows, row => Assert.Equal(7, row.Length));
        Assert.Empty(overlaps);
    }

    /// <summary>
    ///     Proves that the lexicon allows the gold tag for at least 99.9 percent of the non-disputed tokens of the open
    ///     training and development sets.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Lexicon_Coverage_TrainAndDev_MeetsThreshold()
    {
        // Arrange: read the lexicon
        var lexicon = DataFiles.ReadLexicon(DataFiles.Data("lexicon/lexicon.tsv"));

        foreach (var corpus in new[] { "open/train.tsv", "open/dev.tsv" })
        {
            // Act: count the non-disputed tokens whose gold tag the lexicon allows
            long tokens = 0;
            long covered = 0;
            foreach (var token in DataFiles.ReadCorpus(DataFiles.Data(corpus)).SelectMany(sentence => sentence.Tokens))
            {
                if (token[2] == "disputed")
                {
                    continue;
                }

                tokens++;
                if (DataFiles.IsTagAllowed(token[0], token[1], lexicon))
                {
                    covered++;
                }
            }

            // Assert: the coverage is at least 99.9 percent
            Assert.True(tokens > 20000, corpus);
            Assert.True(covered * 1000 >= tokens * 999, $"{corpus}: {covered} of {tokens} tokens are covered");
        }
    }

    /// <summary>
    ///     Proves that the lexicon rows are sorted by UTF-8 bytes, unique and valid, and that the counts in its header are
    ///     correct.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Lexicon_Format_RowsAreSortedUniqueAndValid()
    {
        // Arrange: the lexicon lines and a place for the problems
        var lines = File.ReadLines(DataFiles.Data("lexicon/lexicon.tsv")).ToList();
        var problems = new List<string>();
        string? previous = null;
        var spellings = 0;
        var entries = 0;

        // Act: check every row
        foreach (var line in lines.Where(line => line.Length > 0 && !DataFiles.IsComment(line)))
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
            spellings++;
            entries += tags.Length;
        }

        var header = Regex.Match(string.Join("\n", lines.Where(DataFiles.IsComment)), @"spellings=(\d+), tag entries=(\d+)");

        // Assert: the rows are valid and the header counts match
        Assert.Empty(problems.Take(ShownProblems));
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
    ///     Collapses every run of white space to one space.
    /// </summary>
    private static string Collapse(string text) => Regex.Replace(text, @"\s+", " ");

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
    ///     Finds the rows of a source table that mention a restricted license.
    /// </summary>
    private static IEnumerable<string> RestrictedTextProblems(string table)
    {
        var path = DataFiles.Data(table);
        if (!File.Exists(path))
        {
            return new[] { "missing: data/" + table };
        }

        return File.ReadLines(path)
            .Where(line => line.Length > 0 && line[0] != '#' && RestrictedLicenseText.IsMatch(line))
            .Select(line => $"data/{table}: restricted license text in row: {line.Substring(0, Math.Min(80, line.Length))}");
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
    ///     Finds the problems of the tokens of one sentence of a token-level corpus.
    /// </summary>
    private static IEnumerable<string> TokenProblems(string corpus, CorpusSentence sentence)
    {
        foreach (var token in sentence.Tokens)
        {
            var valid = token.Length >= 3 &&
                        (DataFiles.UniversalTags.Contains(token[1]) || (token[1] == "_" && token[2] == "masked"));
            if (!valid)
            {
                yield return $"{corpus}: {sentence.SentenceId}: invalid token row '{string.Join("|", token)}'";
            }
        }

        var rejoined = string.Concat(sentence.Tokens.Select(token => token[0]));
        if (rejoined != sentence.Text.Replace(" ", string.Empty))
        {
            yield return $"{corpus}: {sentence.SentenceId}: tokens do not rejoin to the text";
        }
    }

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
