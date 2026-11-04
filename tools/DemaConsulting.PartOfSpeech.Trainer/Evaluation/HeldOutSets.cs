using System.Globalization;
using System.Text.RegularExpressions;

namespace DemaConsulting.PartOfSpeech.Trainer;

/// <summary>
///     The SHA-256 hashes of the committed data files, from <c>data/MANIFEST.tsv</c>.
/// </summary>
internal static class DataManifest
{
    /// <summary>
    ///     Reads the manifest.
    /// </summary>
    /// <param name="repositoryRoot">The full path of the repository root.</param>
    /// <returns>The lower-case SHA-256 by path below the data folder, with forward slashes.</returns>
    /// <exception cref="InvalidDataException">The manifest has a row that is not path, size and hash.</exception>
    public static Dictionary<string, string> Load(string repositoryRoot)
    {
        ArgumentNullException.ThrowIfNull(repositoryRoot);
        var path = Path.Combine(repositoryRoot, "data", "MANIFEST.tsv");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(path))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var cells = line.Split('\t');
            if (cells.Length < 3 || cells[2].Length != 64)
            {
                throw new InvalidDataException($"{path} has a row that is not a path, a size and a SHA-256: '{line}'.");
            }

            result[cells[0]] = cells[2].ToLowerInvariant();
        }

        return result;
    }
}

/// <summary>
///     One sentence of the convention-mapped table of the external treebank: the tokens with the mapped and the raw tag.
/// </summary>
internal sealed class UdSentence
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="UdSentence"/> class.
    /// </summary>
    /// <param name="sentenceId">The sentence id of the treebank.</param>
    /// <param name="documentId">The document of the sentence (the sentence id without its trailing number).</param>
    /// <param name="tokens">The words.</param>
    /// <param name="mapped">The tag of every word after the convention mapping.</param>
    /// <param name="raw">The tag of every word as the treebank gives it.</param>
    /// <param name="rules">The mapping rule that was applied to every word (<c>_</c> for none).</param>
    public UdSentence(string sentenceId, string documentId, string[] tokens, Upos[] mapped, Upos[] raw, string[] rules)
    {
        SentenceId = sentenceId;
        DocumentId = documentId;
        Tokens = tokens;
        Mapped = mapped;
        Raw = raw;
        Rules = rules;
    }

    /// <summary>
    ///     Gets the sentence id of the treebank.
    /// </summary>
    public string SentenceId { get; }

    /// <summary>
    ///     Gets the document of the sentence.
    /// </summary>
    public string DocumentId { get; }

    /// <summary>
    ///     Gets the words.
    /// </summary>
    public string[] Tokens { get; }

    /// <summary>
    ///     Gets the tag of every word after the convention mapping.
    /// </summary>
    public Upos[] Mapped { get; }

    /// <summary>
    ///     Gets the tag of every word as the treebank gives it.
    /// </summary>
    public Upos[] Raw { get; }

    /// <summary>
    ///     Gets the mapping rule that was applied to every word.
    /// </summary>
    public string[] Rules { get; }
}

/// <summary>
///     Parses the lines of the held-out sets. The parsers never open a file; the guarded readers are <see cref="HeldOutReader"/>.
/// </summary>
/// <remarks>
///     Stateless and thread-safe.
/// </remarks>
internal static class HeldOutSets
{
    /// <summary>
    ///     The pattern of the trailing sentence number of a treebank sentence id.
    /// </summary>
    private static readonly Regex SentenceNumber = new("-[0-9]+$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    ///     Parses a corpus held-out set (open test split, challenge set): the shared block format, every token judged.
    /// </summary>
    /// <param name="lines">The lines.</param>
    /// <param name="origin">The origin for error messages.</param>
    /// <returns>The sentences with the gold tag of every token.</returns>
    public static List<TrainingSentence> ParseCorpus(IEnumerable<string> lines, string origin) =>
        CorpusReader.Parse(lines, new SourceConfig { Name = "held-out", MaskDisputed = false }, origin);

    /// <summary>
    ///     Parses the shape probe: a header row and one row for each sentence with the target word in braces.
    /// </summary>
    /// <param name="lines">The lines.</param>
    /// <param name="origin">The origin for error messages.</param>
    /// <returns>One target for each row; the shape is its group and the row id its document.</returns>
    /// <exception cref="InvalidDataException">A row is malformed, its braces do not mark exactly the target index or its gold is not Noun or Verb.</exception>
    public static List<GoldTarget> ParseProbe(IEnumerable<string> lines, string origin)
    {
        ArgumentNullException.ThrowIfNull(lines);
        string[]? columns = null;
        var result = new List<GoldTarget>();
        foreach (var line in lines)
        {
            if (line.Length == 0 || line.StartsWith("# ", StringComparison.Ordinal))
            {
                continue;
            }

            var cells = line.Split('\t');
            if (columns == null)
            {
                columns = cells;
                var missing = new[] { "id", "shape", "gold", "target_index", "sentence" }.FirstOrDefault(required => Array.IndexOf(columns, required) < 0);
                if (missing != null)
                {
                    throw new InvalidDataException($"{origin}: the column '{missing}' is missing.");
                }

                continue;
            }

            if (cells.Length != columns.Length)
            {
                throw new InvalidDataException($"{origin}: a row has {cells.Length} cells but the table has {columns.Length} columns.");
            }

            string Get(string name) => cells[Array.IndexOf(columns, name)];
            var id = Get("id");
            var tokens = Get("sentence").Split(' ');
            if (!int.TryParse(Get("target_index"), NumberStyles.None, CultureInfo.InvariantCulture, out var target) || target >= tokens.Length)
            {
                throw new InvalidDataException($"{origin}: the target index of row {id} is not an index of its sentence.");
            }

            // Exactly one word is braced and it is the target; the braces are not part of the word
            var braced = tokens.Select((t, i) => (t, i)).Where(p => p.t.Length >= 2 && p.t[0] == '{' && p.t[p.t.Length - 1] == '}').ToList();
            if (braced.Count != 1 || braced[0].i != target)
            {
                throw new InvalidDataException($"{origin}: the braces of row {id} do not mark exactly the word at the target index.");
            }

            tokens[target] = tokens[target].Substring(1, tokens[target].Length - 2);
            var gold = Get("gold");
            if (gold != "Noun" && gold != "Verb")
            {
                throw new InvalidDataException($"{origin}: the gold of row {id} is not Noun or Verb.");
            }

            result.Add(new GoldTarget(id, "probe:" + id, tokens, target, gold, string.Empty, string.Empty, string.Empty, Get("shape")));
        }

        return result;
    }

    /// <summary>
    ///     Parses the convention-mapped table of the external treebank.
    /// </summary>
    /// <param name="lines">The lines.</param>
    /// <param name="origin">The origin for error messages.</param>
    /// <returns>The sentences.</returns>
    /// <exception cref="InvalidDataException">A row is malformed, a tag is not a UPOS tag or a sentence has no id.</exception>
    public static List<UdSentence> ParseUd(IEnumerable<string> lines, string origin)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var result = new List<UdSentence>();
        string? id = null;
        var forms = new List<string>();
        var mapped = new List<Upos>();
        var raw = new List<Upos>();
        var rules = new List<string>();

        void Flush()
        {
            if (id != null && forms.Count > 0)
            {
                result.Add(new UdSentence(id, SentenceNumber.Replace(id, string.Empty), forms.ToArray(), mapped.ToArray(), raw.ToArray(), rules.ToArray()));
            }

            id = null;
            forms.Clear();
            mapped.Clear();
            raw.Clear();
            rules.Clear();
        }

        foreach (var line in lines)
        {
            // A comment starts with a hash and a space (a token that is a hash is followed by a tab)
            if (line.StartsWith("# sent_id = ", StringComparison.Ordinal))
            {
                Flush();
                id = line.Substring("# sent_id = ".Length);
                continue;
            }

            if (line.Length == 0)
            {
                Flush();
                continue;
            }

            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                continue;
            }

            var cells = line.Split('\t');
            if (id == null || cells.Length != 4 || !UposExtensions.TryParse(cells[1], out var mappedTag) || !UposExtensions.TryParse(cells[2], out var rawTag))
            {
                throw new InvalidDataException($"{origin}: a token row is malformed or outside a sentence.");
            }

            forms.Add(cells[0]);
            mapped.Add(mappedTag);
            raw.Add(rawTag);
            rules.Add(cells[3]);
        }

        Flush();
        return result;
    }
}

/// <summary>
///     One documented size of a held-out set and the size that was found.
/// </summary>
internal sealed class HeldOutCount
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="HeldOutCount"/> class.
    /// </summary>
    /// <param name="name">What is counted.</param>
    /// <param name="actual">The count that was found.</param>
    /// <param name="expected">The documented count.</param>
    public HeldOutCount(string name, long actual, long expected)
    {
        Name = name;
        Actual = actual;
        Expected = expected;
    }

    /// <summary>
    ///     Gets what is counted.
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     Gets the count that was found.
    /// </summary>
    public long Actual { get; }

    /// <summary>
    ///     Gets the documented count.
    /// </summary>
    public long Expected { get; }

    /// <summary>
    ///     Gets a value indicating whether the count equals the documented one.
    /// </summary>
    public bool Matches => Actual == Expected;
}

/// <summary>
///     All held-out inputs of the <c>evaluate</c> command, read through the guard and parsed.
/// </summary>
internal sealed class HeldOutData
{
    /// <summary>
    ///     Gets the sentences of the open test split.
    /// </summary>
    public List<TrainingSentence> OpenTest { get; init; } = new();

    /// <summary>
    ///     Gets the targets of the gold v2 test split.
    /// </summary>
    public List<GoldTarget> Gold { get; init; } = new();

    /// <summary>
    ///     Gets the rows of the shape probe.
    /// </summary>
    public List<GoldTarget> Probe { get; init; } = new();

    /// <summary>
    ///     Gets the sentences of the challenge set.
    /// </summary>
    public List<TrainingSentence> Challenge { get; init; } = new();

    /// <summary>
    ///     Gets the sentences of the external treebank.
    /// </summary>
    public List<UdSentence> Ud { get; init; } = new();

    /// <summary>
    ///     Gets the identification of every input: a name and the SHA-256 of the file (the source pin for the treebank).
    /// </summary>
    public List<KeyValuePair<string, string>> Inputs { get; init; } = new();

    /// <summary>
    ///     Reads and parses every held-out input.
    /// </summary>
    /// <param name="repositoryRoot">The full path of the repository root.</param>
    /// <param name="selection">The validated development selection (the guard requires it).</param>
    /// <param name="udCache">The cache folder of the external treebank, absolute or relative to the repository root.</param>
    /// <returns>The data.</returns>
    /// <exception cref="InvalidDataException">A file does not match its recorded hash or header, or is malformed.</exception>
    public static HeldOutData Load(string repositoryRoot, SelectedOperatingPoints selection, string udCache)
    {
        ArgumentNullException.ThrowIfNull(repositoryRoot);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(udCache);
        var open = HeldOutReader.Read(repositoryRoot, HeldOutSet.OpenTest, selection);
        var gold = HeldOutReader.Read(repositoryRoot, HeldOutSet.GoldTest, selection);
        var probe = HeldOutReader.Read(repositoryRoot, HeldOutSet.Probe, selection);
        var challenge = HeldOutReader.Read(repositoryRoot, HeldOutSet.Challenge, selection);
        var ud = HeldOutReader.ReadUd(repositoryRoot, udCache, selection);
        var data = new HeldOutData
        {
            OpenTest = HeldOutSets.ParseCorpus(open.Lines, open.Path),
            Gold = DevSets.ParseGold(gold.Lines, gold.Path),
            Probe = HeldOutSets.ParseProbe(probe.Lines, probe.Path),
            Challenge = HeldOutSets.ParseCorpus(challenge.Lines, challenge.Path),
            Ud = HeldOutSets.ParseUd(ud.Lines, ud.Path),
        };
        data.Inputs.Add(new("open-test", open.Sha256));
        data.Inputs.Add(new("gold-v2-test", gold.Sha256));
        data.Inputs.Add(new("shape-probe", probe.Sha256));
        data.Inputs.Add(new("challenge-set", challenge.Sha256));
        data.Inputs.Add(new("ud-test-source", ud.SourceSha256));
        data.Inputs.Add(new("ud-test-mapped", ud.Sha256));
        return data;
    }

    /// <summary>
    ///     Lists the counts of every set next to the documented ones. Only counts: no tag, form or prediction.
    /// </summary>
    /// <returns>The counts.</returns>
    public List<HeldOutCount> Counts() => new()
    {
        new("open test sentences", OpenTest.Count, 1501),
        new("open test tokens", OpenTest.Sum(s => (long)s.Tokens.Length), 26185),
        new("open test documents", OpenTest.Select(s => s.SourceId).Distinct(StringComparer.Ordinal).Count(), 517),
        new("gold v2 test targets", Gold.Count, 3047),
        new("gold v2 test documents", Gold.Select(g => g.DocumentId).Distinct(StringComparer.Ordinal).Count(), 856),
        new("shape probe rows", Probe.Count, 560),
        new("challenge set sentences", Challenge.Count, 32),
        new("challenge set tokens", Challenge.Sum(s => (long)s.Tokens.Length), 272),
        new("treebank sentences", Ud.Count, 2077),
        new("treebank words", Ud.Sum(s => (long)s.Tokens.Length), 25094),
    };
}
