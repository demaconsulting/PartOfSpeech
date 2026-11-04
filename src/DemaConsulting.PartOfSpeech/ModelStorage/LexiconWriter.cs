using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     Compiles the lexicon source table (<c>data/lexicon/lexicon.tsv</c>) into the <c>POSL</c> binary format.
/// </summary>
/// <remarks>
///     The writer is build-time code: the <c>LexiconCompiler</c> tool and the tests use it, the runtime never does.
///     It lives next to the reader so that both sides of the format are reviewed together and the round-trip tests can
///     reach it. The output of <see cref="Compile"/> is deterministic for the uncompressed form; only the Deflate bytes
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
    ///     Encodes a lexicon in the <c>POSL</c> format.
    /// </summary>
    /// <param name="entries">The spellings with their tags, strictly ascending in UTF-8 byte order, no tag set empty.</param>
    /// <param name="compress">Whether to store the payload as raw Deflate.</param>
    /// <param name="blockShift">The log2 of the number of spellings per block (1 to 8).</param>
    /// <returns>The file bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="blockShift"/> is outside 1 to 8.</exception>
    /// <exception cref="ArgumentException">An entry violates the format rules (empty or unsorted spelling, empty tag set, too many masks).</exception>
    public static byte[] Compile(IReadOnlyList<KeyValuePair<string, UposSet>> entries, bool compress, int blockShift = DefaultBlockShift)
    {
        ArgumentNullException.ThrowIfNull(entries);
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

        // Payload: palette, block index, data
        var payload = new byte[(palette.Length * 4) + (blockCount * 4) + (int)data.Length];
        for (var i = 0; i < palette.Length; i++)
        {
            BinaryFormat.WriteUInt32(payload, i * 4, (uint)palette[i]);
        }

        for (var i = 0; i < blockCount; i++)
        {
            BinaryFormat.WriteUInt32(payload, (palette.Length * 4) + (i * 4), blockOffsets[i]);
        }

        Buffer.BlockCopy(data.GetBuffer(), 0, payload, (palette.Length * 4) + (blockCount * 4), (int)data.Length);

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
}
