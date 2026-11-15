using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     Compiles the lexicon source tables (<c>data/lexicon/lexicon.tsv</c> and <c>data/lexicon/analyses.tsv</c>) into the <c>POSL</c> binary format.
/// </summary>
/// <remarks>
///     The writer is build-time code: the <c>LexiconCompiler</c> tool and the tests use it, the runtime never does.
///     It lives next to the reader so that both sides of the format are reviewed together and the round-trip tests can
///     reach it. The output of <c>Compile</c> is deterministic for the uncompressed form; only the Deflate bytes
///     may differ between runtimes, which is why the tool compares uncompressed images. All members are stateless and
///     thread-safe.
/// </remarks>
internal static class LexiconWriter
{
    /// <summary>
    ///     The default number of spellings per block as a power of two (16 spellings).
    /// </summary>
    public const int DefaultBlockShift = 4;

    /// <summary>
    ///     Matches the counts that the generator records in the header comment of the source table.
    /// </summary>
    private static readonly Regex HeaderCounts = new(@"spellings=(\d+), tag entries=(\d+)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    ///     Matches the counts that the generator records in the header comment of the analyses table.
    /// </summary>
    private static readonly Regex AnalysesHeaderCounts = new(@"spellings=(\d+), analyses=(\d+)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>
    ///     Parses the lines of the source table and validates every rule that the binary format relies on.
    /// </summary>
    /// <remarks>
    ///     The rules are the ones of <c>data/lexicon/README.md</c>: one lower-case, NFC-normalized spelling without
    ///     white space per line, strictly ascending in UTF-8 byte order, followed by a tab and its tags in
    ///     alphabetical order. Comment lines start with a hash and a space. A header comment that records
    ///     <c>spellings=N, tag entries=M</c> is checked against the actual counts. Nothing is dropped silently: a
    ///     spelling that cannot be represented is reported.
    /// </remarks>
    /// <param name="lines">The lines of the table.</param>
    /// <returns>The spellings with their tags, in table order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="lines"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidDataException">A line is malformed, out of order or the header counts do not match.</exception>
    public static IReadOnlyList<KeyValuePair<string, UposSet>> FromTsv(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var entries = new List<KeyValuePair<string, UposSet>>();
        byte[]? previous = null;
        long declaredSpellings = -1;
        long declaredEntries = -1;
        long entryCount = 0;
        var lineNumber = 0;
        foreach (var line in lines)
        {
            lineNumber++;

            // Comment and blank lines carry no data; one comment may declare the expected counts
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                var match = HeaderCounts.Match(line);
                if (match.Success)
                {
                    declaredSpellings = long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                    declaredEntries = long.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                }

                continue;
            }

            var entry = ParseRow(line, lineNumber);

            // Strictly ascending UTF-8 byte order is what the binary search of the reader depends on
            var bytes = Encoding.UTF8.GetBytes(entry.Key);
            if (previous != null && BinaryFormat.Compare(previous, 0, previous.Length, bytes, 0, bytes.Length) >= 0)
            {
                throw new InvalidDataException($"Line {lineNumber}: '{entry.Key}' is not strictly after the previous spelling (unsorted or duplicate).");
            }

            previous = bytes;
            entries.Add(entry);
            entryCount += entry.Value.Count;
        }

        if ((declaredSpellings >= 0 && declaredSpellings != entries.Count) || (declaredEntries >= 0 && declaredEntries != entryCount))
        {
            throw new InvalidDataException(
                $"The header declares {declaredSpellings} spellings and {declaredEntries} tag entries but the table has {entries.Count} and {entryCount}.");
        }

        return entries;
    }

    /// <summary>
    ///     Encodes a lexicon without analyses in the <c>POSL</c> format.
    /// </summary>
    /// <remarks>
    ///     The file is a version 3 file whose analyses section is empty: the empty feature set, no templates, the empty
    ///     list, and every spelling pointing at the empty list. Custom lexicons and most tests use this overload.
    /// </remarks>
    /// <param name="entries">The spellings with their tags, strictly ascending in UTF-8 byte order, no tag set empty.</param>
    /// <param name="compress">Whether to store the payload as raw Deflate.</param>
    /// <param name="blockShift">The log2 of the number of spellings per block (1 to 8).</param>
    /// <returns>The file bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="blockShift"/> is outside 1 to 8.</exception>
    /// <exception cref="ArgumentException">An entry violates the format rules (empty or unsorted spelling, empty tag set, too many masks).</exception>
    public static byte[] Compile(IReadOnlyList<KeyValuePair<string, UposSet>> entries, bool compress, int blockShift = DefaultBlockShift) =>
        Compile(entries, Array.Empty<KeyValuePair<string, IReadOnlyList<Lexicon.Analysis>>>(), compress, blockShift);

    /// <summary>
    ///     Encodes a lexicon with its analyses in the <c>POSL</c> format.
    /// </summary>
    /// <remarks>
    ///     The output is deterministic: feature sets ascend, templates are ordered by descending use and then by their
    ///     content, lists appear in the order of the first spelling that needs them.
    /// </remarks>
    /// <param name="entries">The spellings with their tags, strictly ascending in UTF-8 byte order, no tag set empty.</param>
    /// <param name="analyses">
    ///     The analyses: spellings strictly ascending in UTF-8 byte order and all in <paramref name="entries"/>, each with at
    ///     least one and at most 255 analyses that are strictly ascending by tag, base word and features, with a tag of the spelling.
    /// </param>
    /// <param name="compress">Whether to store the payload as raw Deflate.</param>
    /// <param name="blockShift">The log2 of the number of spellings per block (1 to 8).</param>
    /// <returns>The file bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> or <paramref name="analyses"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="blockShift"/> is outside 1 to 8.</exception>
    /// <exception cref="ArgumentException">An entry or an analysis violates the format rules, or the analyses need more than 65,535 lists.</exception>
    public static byte[] Compile(
        IReadOnlyList<KeyValuePair<string, UposSet>> entries,
        IReadOnlyList<KeyValuePair<string, IReadOnlyList<Lexicon.Analysis>>> analyses,
        bool compress,
        int blockShift = DefaultBlockShift)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(analyses);
        if (blockShift < 1 || blockShift > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(blockShift), blockShift, "The block shift must be between 1 and 8.");
        }

        // Encode every spelling and build the palette of distinct masks (sorted, so the output is deterministic)
        var encoded = new byte[entries.Count][];
        var maskSet = new SortedSet<int>();
        long entryCount = 0;
        var longest = 0;
        for (var i = 0; i < entries.Count; i++)
        {
            encoded[i] = Encoding.UTF8.GetBytes(entries[i].Key);
            if (encoded[i].Length == 0 || entries[i].Value.IsEmpty)
            {
                throw new ArgumentException($"Entry {i} has an empty spelling or an empty tag set.", nameof(entries));
            }

            if (i > 0 && BinaryFormat.Compare(encoded[i - 1], 0, encoded[i - 1].Length, encoded[i], 0, encoded[i].Length) >= 0)
            {
                throw new ArgumentException($"Entry {i} ('{entries[i].Key}') is not strictly after the previous spelling.", nameof(entries));
            }

            maskSet.Add(entries[i].Value.Mask);
            entryCount += entries[i].Value.Count;
            longest = Math.Max(longest, encoded[i].Length);
        }

        if (maskSet.Count > ushort.MaxValue || longest > ushort.MaxValue)
        {
            throw new ArgumentException("The lexicon has too many distinct tag masks or a spelling that is too long.", nameof(entries));
        }

        var palette = maskSet.ToArray();
        var paletteIndex = new Dictionary<int, int>();
        for (var i = 0; i < palette.Length; i++)
        {
            paletteIndex[palette[i]] = i;
        }

        // Data region: block heads are stored whole, the other spellings front-coded against their predecessor
        var blockSize = 1 << blockShift;
        var blockCount = (entries.Count + blockSize - 1) >> blockShift;
        var blockOffsets = new uint[blockCount];
        using var data = new MemoryStream();
        for (var i = 0; i < entries.Count; i++)
        {
            var maskIndex = (uint)paletteIndex[entries[i].Value.Mask];
            if ((i & (blockSize - 1)) == 0)
            {
                blockOffsets[i >> blockShift] = (uint)data.Position;
                BinaryFormat.WriteVarint(data, (uint)encoded[i].Length);
                data.Write(encoded[i], 0, encoded[i].Length);
            }
            else
            {
                var shared = SharedPrefix(encoded[i - 1], encoded[i]);
                BinaryFormat.WriteVarint(data, (uint)shared);
                BinaryFormat.WriteVarint(data, (uint)(encoded[i].Length - shared));
                data.Write(encoded[i], shared, encoded[i].Length - shared);
            }

            BinaryFormat.WriteVarint(data, maskIndex);
        }

        // Payload: palette, block index, data, then the analyses section
        var section = EncodeAnalyses(entries, encoded, analyses);
        var dataStart = (palette.Length * 4) + (blockCount * 4);
        var payload = new byte[dataStart + (int)data.Length + section.Bytes.Length];
        for (var i = 0; i < palette.Length; i++)
        {
            BinaryFormat.WriteUInt32(payload, i * 4, (uint)palette[i]);
        }

        for (var i = 0; i < blockCount; i++)
        {
            BinaryFormat.WriteUInt32(payload, (palette.Length * 4) + (i * 4), blockOffsets[i]);
        }

        Buffer.BlockCopy(data.GetBuffer(), 0, payload, dataStart, (int)data.Length);
        Buffer.BlockCopy(section.Bytes, 0, payload, dataStart + (int)data.Length, section.Bytes.Length);

        // Header, then the stored payload
        var stored = compress ? BinaryFormat.Compress(payload) : payload;
        var file = new byte[Lexicon.HeaderLength + stored.Length];
        file[0] = (byte)'P';
        file[1] = (byte)'O';
        file[2] = (byte)'S';
        file[3] = (byte)'L';
        BinaryFormat.WriteUInt16(file, 4, Lexicon.FormatVersion);
        BinaryFormat.WriteUInt16(file, 6, compress ? (ushort)BinaryFormat.FlagDeflate : (ushort)0);
        file[8] = UposExtensions.Count;
        file[9] = (byte)blockShift;
        BinaryFormat.WriteUInt16(file, 10, (ushort)palette.Length);
        BinaryFormat.WriteUInt32(file, 12, (uint)entries.Count);
        BinaryFormat.WriteUInt32(file, 16, (uint)entryCount);
        BinaryFormat.WriteUInt32(file, 20, (uint)blockCount);
        BinaryFormat.WriteUInt32(file, 24, (uint)payload.Length);
        BinaryFormat.WriteUInt32(file, 28, (uint)stored.Length);
        BinaryFormat.WriteUInt32(file, 32, BinaryFormat.Fnv1a(stored, 0, stored.Length));
        BinaryFormat.WriteUInt16(file, 36, (ushort)longest);
        BinaryFormat.WriteUInt16(file, 40, (ushort)section.FeatureSetCount);
        BinaryFormat.WriteUInt32(file, 44, (uint)section.TemplateCount);
        BinaryFormat.WriteUInt32(file, 48, (uint)section.ListCount);
        BinaryFormat.WriteUInt32(file, 52, (uint)section.AnalysisCount);
        Buffer.BlockCopy(stored, 0, file, Lexicon.HeaderLength, stored.Length);
        return file;
    }

    /// <summary>
    ///     Encodes a lexicon in the <c>POSL</c> format and writes it to a stream.
    /// </summary>
    /// <param name="entries">The spellings with their tags.</param>
    /// <param name="output">The destination stream.</param>
    /// <param name="compress">Whether to store the payload as raw Deflate.</param>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> or <paramref name="output"/> is <c>null</c>.</exception>
    public static void Write(IReadOnlyList<KeyValuePair<string, UposSet>> entries, Stream output, bool compress)
    {
        ArgumentNullException.ThrowIfNull(output);
        var file = Compile(entries, compress);
        output.Write(file, 0, file.Length);
    }

    /// <summary>
    ///     Parses and validates the analyses table (<c>data/lexicon/analyses.tsv</c>) against the lexicon entries.
    /// </summary>
    /// <remarks>
    ///     A row is the spelling, then one tab separated analysis per form: <c>lemma TAG features</c>, the features
    ///     (UD notation, names ascending, joined by <c>|</c>) omitted when there are none. The spellings must be
    ///     lexicon spellings in strictly ascending UTF-8 order, the tag of an analysis a tag of its spelling, the analyses of
    ///     a spelling strictly ascending by tag, base word and features. Comment lines start with a hash and a space; a
    ///     comment that records <c>spellings=N, analyses=M</c> is checked against the actual counts. Nothing is dropped
    ///     silently. Which feature names and values occur is a data rule checked by the data tests, not by this parser.
    /// </remarks>
    /// <param name="lines">The lines of the table.</param>
    /// <param name="entries">The lexicon entries that the analyses belong to.</param>
    /// <returns>The spellings with their analyses, in table order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="lines"/> or <paramref name="entries"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidDataException">A line is malformed, out of order, not a lexicon spelling or the header counts do not match.</exception>
    public static IReadOnlyList<KeyValuePair<string, IReadOnlyList<Lexicon.Analysis>>> AnalysesFromTsv(
        IEnumerable<string> lines,
        IReadOnlyList<KeyValuePair<string, UposSet>> entries)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(entries);
        var tags = new Dictionary<string, UposSet>(entries.Count, StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            tags[entry.Key] = entry.Value;
        }

        var rows = new List<KeyValuePair<string, IReadOnlyList<Lexicon.Analysis>>>();
        byte[]? previous = null;
        long declaredSpellings = -1;
        long declaredAnalyses = -1;
        long analysisCount = 0;
        var lineNumber = 0;
        foreach (var line in lines)
        {
            lineNumber++;
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                var match = AnalysesHeaderCounts.Match(line);
                if (match.Success)
                {
                    declaredSpellings = long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                    declaredAnalyses = long.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                }

                continue;
            }

            // The spelling must be a lexicon spelling and strictly after the previous row
            var cells = line.Split('\t');
            if (cells.Length < 2 || !tags.TryGetValue(cells[0], out var spellingTags))
            {
                throw new InvalidDataException($"Line {lineNumber}: expected 'spelling TAB analysis ...' with a spelling of the lexicon.");
            }

            var bytes = Encoding.UTF8.GetBytes(cells[0]);
            if (previous != null && BinaryFormat.Compare(previous, 0, previous.Length, bytes, 0, bytes.Length) >= 0)
            {
                throw new InvalidDataException($"Line {lineNumber}: '{cells[0]}' is not strictly after the previous spelling (unsorted or duplicate).");
            }

            previous = bytes;
            var analyses = new List<Lexicon.Analysis>(cells.Length - 1);
            for (var i = 1; i < cells.Length; i++)
            {
                var analysis = ParseAnalysis(cells[i], lineNumber);
                if (!spellingTags.Contains(analysis.Tag))
                {
                    throw new InvalidDataException($"Line {lineNumber}: the tag {analysis.Tag.ToTagString()} is not a lexicon tag of '{cells[0]}'.");
                }

                if (analyses.Count > 0 && CompareAnalyses(analyses[analyses.Count - 1], analysis) >= 0)
                {
                    throw new InvalidDataException($"Line {lineNumber}: the analyses of '{cells[0]}' are not strictly ascending by tag, base word and features.");
                }

                analyses.Add(analysis);
            }

            rows.Add(new KeyValuePair<string, IReadOnlyList<Lexicon.Analysis>>(cells[0], analyses));
            analysisCount += analyses.Count;
        }

        if ((declaredSpellings >= 0 && declaredSpellings != rows.Count) || (declaredAnalyses >= 0 && declaredAnalyses != analysisCount))
        {
            throw new InvalidDataException(
                $"The header declares {declaredSpellings} spellings and {declaredAnalyses} analyses but the table has {rows.Count} and {analysisCount}.");
        }

        return rows;
    }

    /// <summary>
    ///     Orders two analyses of one spelling: by tag, then base word and then features, each by UTF-8 bytes.
    /// </summary>
    /// <param name="left">The first analysis.</param>
    /// <param name="right">The second analysis.</param>
    /// <returns>A negative number, zero or a positive number as <paramref name="left"/> is before, equal to or after <paramref name="right"/>.</returns>
    internal static int CompareAnalyses(Lexicon.Analysis left, Lexicon.Analysis right)
    {
        var byTag = ((int)left.Tag).CompareTo((int)right.Tag);
        if (byTag != 0)
        {
            return byTag;
        }

        var leftLemma = Encoding.UTF8.GetBytes(left.Lemma);
        var rightLemma = Encoding.UTF8.GetBytes(right.Lemma);
        var byLemma = BinaryFormat.Compare(leftLemma, 0, leftLemma.Length, rightLemma, 0, rightLemma.Length);
        if (byLemma != 0)
        {
            return byLemma;
        }

        var leftFeatures = Encoding.UTF8.GetBytes(left.Features);
        var rightFeatures = Encoding.UTF8.GetBytes(right.Features);
        return BinaryFormat.Compare(leftFeatures, 0, leftFeatures.Length, rightFeatures, 0, rightFeatures.Length);
    }

    /// <summary>
    ///     Parses one analysis of a table row: <c>lemma TAG</c> or <c>lemma TAG features</c>.
    /// </summary>
    /// <param name="cell">The cell.</param>
    /// <param name="lineNumber">The line number for messages.</param>
    /// <returns>The analysis.</returns>
    /// <exception cref="InvalidDataException">The cell is malformed.</exception>
    private static Lexicon.Analysis ParseAnalysis(string cell, int lineNumber)
    {
        var parts = cell.Split(' ');
        if (parts.Length is < 2 or > 3 || !UposExtensions.TryParse(parts[1], out var tag))
        {
            throw new InvalidDataException($"Line {lineNumber}: expected 'lemma TAG' or 'lemma TAG features' but found '{cell}'.");
        }

        var lemma = parts[0];
        var features = parts.Length == 3 ? parts[2] : string.Empty;
        if (lemma.Length == 0 || !lemma.IsNormalized(NormalizationForm.FormC) || lemma.Any(char.IsWhiteSpace)
            || Encoding.UTF8.GetByteCount(lemma) > Lexicon.MaxLemmaBytes || (parts.Length == 3 && (features.Length == 0 || !Lexicon.IsValidFeatureSet(features))))
        {
            throw new InvalidDataException($"Line {lineNumber}: the base word or the features of '{cell}' are invalid.");
        }

        return new Lexicon.Analysis(lemma, tag, features);
    }

    /// <summary>
    ///     Encodes the analyses section: feature sets, templates, lists and the list index of every spelling.
    /// </summary>
    /// <param name="entries">The lexicon entries.</param>
    /// <param name="encoded">The UTF-8 bytes of the spellings of <paramref name="entries"/>.</param>
    /// <param name="analyses">The analyses.</param>
    /// <returns>The section bytes and the counts that the header records.</returns>
    /// <exception cref="ArgumentException">An analysis violates the format rules, or the analyses need too many feature sets or lists.</exception>
    private static AnalysesSection EncodeAnalyses(
        IReadOnlyList<KeyValuePair<string, UposSet>> entries,
        byte[][] encoded,
        IReadOnlyList<KeyValuePair<string, IReadOnlyList<Lexicon.Analysis>>> analyses)
    {
        // Place every analyzed spelling in the lexicon and check its analyses; the template of each is its edit of the spelling
        var ordinals = new Dictionary<string, int>(entries.Count, StringComparer.Ordinal);
        for (var i = 0; i < entries.Count; i++)
        {
            ordinals[entries[i].Key] = i;
        }

        var featureSets = new SortedSet<string>(StringComparer.Ordinal) { string.Empty };
        var templates = new Dictionary<string, TemplateInfo>(StringComparer.Ordinal);
        var rows = new List<(int Ordinal, TemplateInfo[] Templates)>(analyses.Count);
        long analysisCount = 0;
        var lastOrdinal = -1;
        foreach (var row in analyses)
        {
            if (!ordinals.TryGetValue(row.Key, out var ordinal) || ordinal <= lastOrdinal)
            {
                throw new ArgumentException($"The analyses of '{row.Key}' are not for a lexicon spelling or not strictly after the previous spelling.", nameof(analyses));
            }

            lastOrdinal = ordinal;
            if (row.Value.Count == 0 || row.Value.Count > Lexicon.MaxAnalysesPerSpelling)
            {
                throw new ArgumentException($"'{row.Key}' has {row.Value.Count} analyses; a spelling has 1 to {Lexicon.MaxAnalysesPerSpelling}.", nameof(analyses));
            }

            var chosen = new TemplateInfo[row.Value.Count];
            for (var i = 0; i < chosen.Length; i++)
            {
                var analysis = row.Value[i];
                if (!entries[ordinal].Value.Contains(analysis.Tag) || (i > 0 && CompareAnalyses(row.Value[i - 1], analysis) >= 0))
                {
                    throw new ArgumentException($"An analysis of '{row.Key}' has a tag that the spelling lacks or is not strictly after the previous analysis.", nameof(analyses));
                }

                chosen[i] = TemplateOf(row.Key, encoded[ordinal], analysis, templates, featureSets);
            }

            rows.Add((ordinal, chosen));
            analysisCount += chosen.Length;
        }

        if (featureSets.Count > ushort.MaxValue)
        {
            throw new ArgumentException($"The analyses need {featureSets.Count} feature sets; the format allows at most {ushort.MaxValue}.", nameof(analyses));
        }

        // Number the feature sets (ascending) and the templates (descending use, then content) so the output is deterministic
        var featureSetIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var set in featureSets)
        {
            featureSetIndex[set] = featureSetIndex.Count;
        }

        var ordered = templates.Values
            .OrderByDescending(t => t.Count)
            .ThenBy(t => t.Tag)
            .ThenBy(t => featureSetIndex[t.Features])
            .ThenBy(t => t.Cut)
            .ThenBy(t => t.SuffixText, StringComparer.Ordinal)
            .ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].Index = i;
        }

        // Lists in the order of the first spelling that needs them; list 0 is the empty list
        var listIndex = new Dictionary<string, int>(StringComparer.Ordinal) { [string.Empty] = 0 };
        var lists = new List<int[]> { Array.Empty<int>() };
        var spellingLists = new ushort[entries.Count];
        foreach (var (ordinal, chosen) in rows)
        {
            var list = chosen.Select(t => t.Index).ToArray();
            var key = string.Join(",", list);
            if (!listIndex.TryGetValue(key, out var index))
            {
                if (lists.Count >= Lexicon.MaxLists)
                {
                    throw new ArgumentException($"The analyses need more than {Lexicon.MaxLists} distinct lists of templates; the format allows at most {Lexicon.MaxLists}.", nameof(analyses));
                }

                index = lists.Count;
                listIndex[key] = index;
                lists.Add(list);
            }

            spellingLists[ordinal] = (ushort)index;
        }

        // Write the four regions
        using var output = new MemoryStream();
        foreach (var set in featureSets)
        {
            var bytes = Encoding.ASCII.GetBytes(set);
            BinaryFormat.WriteVarint(output, (uint)bytes.Length);
            output.Write(bytes, 0, bytes.Length);
        }

        foreach (var template in ordered)
        {
            output.WriteByte((byte)template.Tag);
            BinaryFormat.WriteVarint(output, (uint)featureSetIndex[template.Features]);
            BinaryFormat.WriteVarint(output, (uint)template.Cut);
            BinaryFormat.WriteVarint(output, (uint)template.Suffix.Length);
            output.Write(template.Suffix, 0, template.Suffix.Length);
        }

        foreach (var list in lists)
        {
            BinaryFormat.WriteVarint(output, (uint)list.Length);
            foreach (var template in list)
            {
                BinaryFormat.WriteVarint(output, (uint)template);
            }
        }

        foreach (var list in spellingLists)
        {
            output.WriteByte((byte)(list & 0xFF));
            output.WriteByte((byte)(list >> 8));
        }

        return new AnalysesSection(output.ToArray(), featureSets.Count, templates.Count, lists.Count, (int)analysisCount);
    }

    /// <summary>
    ///     Finds or creates the template that turns a spelling into the base word of an analysis.
    /// </summary>
    /// <param name="key">The spelling, for messages.</param>
    /// <param name="spelling">The UTF-8 bytes of the spelling.</param>
    /// <param name="analysis">The analysis.</param>
    /// <param name="templates">The templates found so far; the use count of the template is incremented.</param>
    /// <param name="featureSets">The feature sets found so far; the one of the analysis is added.</param>
    /// <returns>The template.</returns>
    /// <exception cref="ArgumentException">The base word or the features are invalid.</exception>
    private static TemplateInfo TemplateOf(
        string key,
        byte[] spelling,
        Lexicon.Analysis analysis,
        Dictionary<string, TemplateInfo> templates,
        SortedSet<string> featureSets)
    {
        var lemma = Encoding.UTF8.GetBytes(analysis.Lemma);
        if (lemma.Length == 0 || lemma.Length > Lexicon.MaxLemmaBytes || analysis.Lemma.Any(char.IsWhiteSpace)
            || !IsNormalizedFormC(analysis.Lemma) || !Lexicon.IsValidFeatureSet(analysis.Features))
        {
            throw new ArgumentException($"An analysis of '{key}' has an empty, too long, white space or non-NFC base word or malformed features.", nameof(analysis));
        }

        // The longest shared prefix of the spelling and the base word, moved back to a character boundary of both
        var keep = SharedPrefix(spelling, lemma);
        while (keep > 0 && ((keep < spelling.Length && (spelling[keep] & 0xC0) == 0x80) || (keep < lemma.Length && (lemma[keep] & 0xC0) == 0x80)))
        {
            keep--;
        }

        var cut = spelling.Length - keep;
        var suffix = new byte[lemma.Length - keep];
        Buffer.BlockCopy(lemma, keep, suffix, 0, suffix.Length);
        var suffixText = Convert.ToBase64String(suffix);
        var templateKey = $"{(int)analysis.Tag}/{analysis.Features}/{cut}/{suffixText}";
        if (!templates.TryGetValue(templateKey, out var template))
        {
            template = new TemplateInfo((int)analysis.Tag, analysis.Features, cut, suffix, suffixText);
            templates[templateKey] = template;
            featureSets.Add(analysis.Features);
        }

        template.Count++;
        return template;
    }

    /// <summary>
    ///     Tells whether a text is well-formed UTF-16 in Unicode normalization form C.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns><c>true</c> if the text is normalized; <c>false</c> if it is not or has an unpaired surrogate.</returns>
    private static bool IsNormalizedFormC(string text)
    {
        try
        {
            return text.IsNormalized(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    ///     Parses and validates one data row of the source table.
    /// </summary>
    /// <param name="line">The row.</param>
    /// <param name="lineNumber">The line number for messages.</param>
    /// <returns>The spelling and its tag set.</returns>
    /// <exception cref="InvalidDataException">The row is malformed.</exception>
    private static KeyValuePair<string, UposSet> ParseRow(string line, int lineNumber)
    {
        // Split spelling and tags at the first tab
        var tab = line.IndexOf('\t');
        if (tab <= 0 || tab == line.Length - 1)
        {
            throw new InvalidDataException($"Line {lineNumber}: expected 'spelling TAB tags'.");
        }

        var spelling = line.Substring(0, tab);
        if (spelling.IndexOf(' ') >= 0 || !spelling.IsNormalized(NormalizationForm.FormC) ||
            !string.Equals(spelling.ToLowerInvariant(), spelling, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Line {lineNumber}: the spelling '{spelling}' contains a space, is not NFC or is not lower-case.");
        }

        // Tags must be valid and in alphabetical order, which also rules out repeats
        var tags = UposSet.Empty;
        var last = -1;
        foreach (var text in line.Substring(tab + 1).Split(' '))
        {
            if (!UposExtensions.TryParse(text, out var tag) || (int)tag <= last)
            {
                throw new InvalidDataException($"Line {lineNumber}: invalid, repeated or unordered tag '{text}'.");
            }

            last = (int)tag;
            tags = tags.Add(tag);
        }

        return new KeyValuePair<string, UposSet>(spelling, tags);
    }

    /// <summary>
    ///     Counts the leading bytes that two spellings share.
    /// </summary>
    /// <param name="a">The first spelling.</param>
    /// <param name="b">The second spelling.</param>
    /// <returns>The length of the common prefix.</returns>
    private static int SharedPrefix(byte[] a, byte[] b)
    {
        var limit = Math.Min(a.Length, b.Length);
        var shared = 0;
        while (shared < limit && a[shared] == b[shared])
        {
            shared++;
        }

        return shared;
    }

    /// <summary>
    ///     The encoded analyses section and the counts that the header records.
    /// </summary>
    /// <param name="Bytes">The section: feature sets, templates, lists and the list index.</param>
    /// <param name="FeatureSetCount">The number of feature sets.</param>
    /// <param name="TemplateCount">The number of templates.</param>
    /// <param name="ListCount">The number of lists.</param>
    /// <param name="AnalysisCount">The number of analyses of all spellings.</param>
    private readonly record struct AnalysesSection(byte[] Bytes, int FeatureSetCount, int TemplateCount, int ListCount, int AnalysisCount);

    /// <summary>
    ///     One analysis template while the writer collects them: a tag, a feature set and the edit of the spelling.
    /// </summary>
    private sealed class TemplateInfo
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="TemplateInfo"/> class.
        /// </summary>
        /// <param name="tag">The tag index.</param>
        /// <param name="features">The feature set.</param>
        /// <param name="cut">The number of bytes dropped from the end of the spelling.</param>
        /// <param name="suffix">The UTF-8 suffix that is appended.</param>
        /// <param name="suffixText">The suffix as Base64 text, for keys and ordering.</param>
        public TemplateInfo(int tag, string features, int cut, byte[] suffix, string suffixText)
        {
            Tag = tag;
            Features = features;
            Cut = cut;
            Suffix = suffix;
            SuffixText = suffixText;
        }

        /// <summary>
        ///     Gets the tag index.
        /// </summary>
        public int Tag { get; }

        /// <summary>
        ///     Gets the feature set.
        /// </summary>
        public string Features { get; }

        /// <summary>
        ///     Gets the number of bytes dropped from the end of the spelling.
        /// </summary>
        public int Cut { get; }

        /// <summary>
        ///     Gets the UTF-8 suffix that is appended.
        /// </summary>
        public byte[] Suffix { get; }

        /// <summary>
        ///     Gets the suffix as Base64 text.
        /// </summary>
        public string SuffixText { get; }

        /// <summary>
        ///     Gets or sets the number of analyses that use the template.
        /// </summary>
        public int Count { get; set; }

        /// <summary>
        ///     Gets or sets the index of the template in the palette.
        /// </summary>
        public int Index { get; set; }
    }
}
