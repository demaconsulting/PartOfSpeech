namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     Encodes averaged-perceptron weights in the <c>POSW</c> binary format.
/// </summary>
/// <remarks>
///     The writer is used by the tests now and by the phase 3 trainer (through <c>InternalsVisibleTo</c>); the runtime
///     only reads weights. Rows are given as dense 17-element weight vectors keyed by bucket so that a trainer can
///     quantize its float weights once and hand them over without knowing the sparse layout. All members are stateless
///     and thread-safe.
/// </remarks>
internal static class PerceptronModelWriter
{
    /// <summary>
    ///     Encodes a model.
    /// </summary>
    /// <param name="hashBits">The bucket space exponent (18 to 24).</param>
    /// <param name="temperature">The softmax temperature; finite and greater than zero.</param>
    /// <param name="scale">The factor from integer weight to real weight; finite and greater than zero.</param>
    /// <param name="featureSetVersion">The feature set version of the extractor that the weights belong to.</param>
    /// <param name="rows">The occupied buckets with a 17-element weight vector each; zero rows are skipped; buckets must be unique.</param>
    /// <param name="compress">Whether to store the payload as raw Deflate.</param>
    /// <returns>The file bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rows"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A numeric parameter or a bucket is out of range.</exception>
    /// <exception cref="ArgumentException">A weight vector has the wrong length, a weight is -32768 or a bucket repeats.</exception>
    public static byte[] Compile(
        int hashBits,
        float temperature,
        float scale,
        int featureSetVersion,
        IEnumerable<KeyValuePair<int, short[]>> rows,
        bool compress)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (hashBits < PerceptronModel.MinHashBits || hashBits > PerceptronModel.MaxHashBits)
        {
            throw new ArgumentOutOfRangeException(nameof(hashBits), hashBits, "hashBits must be between 18 and 24.");
        }

        if (!BinaryFormat.IsFinitePositive(temperature))
        {
            throw new ArgumentOutOfRangeException(nameof(temperature), temperature, "The temperature must be finite and greater than zero.");
        }

        if (!BinaryFormat.IsFinitePositive(scale))
        {
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "The scale must be finite and greater than zero.");
        }

        if (featureSetVersion < 0 || featureSetVersion > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(featureSetVersion), featureSetVersion, "The feature set version must fit 16 bits.");
        }

        // Validate and order the rows; the format stores buckets as ascending gaps
        var ordered = rows.OrderBy(row => row.Key).ToList();
        var bucketLimit = 1L << hashBits;
        var weightCount = 0;
        var rowCount = 0;
        using var gaps = new MemoryStream();
        using var counts = new MemoryStream();
        using var tags = new MemoryStream();
        using var weights = new MemoryStream();
        long previous = -1;
        long seen = -1;
        foreach (var row in ordered)
        {
            if (row.Key < 0 || row.Key >= bucketLimit)
            {
                throw new ArgumentOutOfRangeException(nameof(rows), row.Key, "A bucket is outside the bucket space.");
            }

            if (row.Value == null || row.Value.Length != UposExtensions.Count)
            {
                throw new ArgumentException("Every row needs exactly 17 weights.", nameof(rows));
            }

            if (row.Key == seen)
            {
                throw new ArgumentException($"Bucket {row.Key} appears twice.", nameof(rows));
            }

            seen = row.Key;

            // Collect the non-zero weights of the row in tag order
            var size = 0;
            for (var tag = 0; tag < UposExtensions.Count; tag++)
            {
                var weight = row.Value[tag];
                if (weight == short.MinValue)
                {
                    throw new ArgumentException("The weight -32768 is not allowed (it cannot be negated).", nameof(rows));
                }

                if (weight != 0)
                {
                    tags.WriteByte((byte)tag);
                    weights.WriteByte((byte)weight);
                    weights.WriteByte((byte)(weight >> 8));
                    size++;
                }
            }

            // A row without a weight carries no information and is not stored
            if (size == 0)
            {
                continue;
            }

            BinaryFormat.WriteVarint(gaps, (uint)(row.Key - previous));
            counts.WriteByte((byte)size);
            previous = row.Key;
            weightCount += size;
            rowCount++;
        }

        // Payload: gaps, row sizes, tags, weights
        var payload = new byte[(int)(gaps.Length + counts.Length + tags.Length + weights.Length)];
        var offset = 0;
        foreach (var section in new[] { gaps, counts, tags, weights })
        {
            Buffer.BlockCopy(section.GetBuffer(), 0, payload, offset, (int)section.Length);
            offset += (int)section.Length;
        }

        // Header, then the stored payload
        var stored = compress ? BinaryFormat.Compress(payload) : payload;
        var file = new byte[PerceptronModel.HeaderLength + stored.Length];
        file[0] = (byte)'P';
        file[1] = (byte)'O';
        file[2] = (byte)'S';
        file[3] = (byte)'W';
        BinaryFormat.WriteUInt16(file, 4, PerceptronModel.FormatVersion);
        BinaryFormat.WriteUInt16(file, 6, compress ? (ushort)BinaryFormat.FlagDeflate : (ushort)0);
        BinaryFormat.WriteUInt16(file, 8, (ushort)featureSetVersion);
        file[10] = UposExtensions.Count;
        file[11] = (byte)hashBits;
        BinaryFormat.WriteSingle(file, 12, temperature);
        BinaryFormat.WriteSingle(file, 16, scale);
        BinaryFormat.WriteUInt32(file, 20, (uint)rowCount);
        BinaryFormat.WriteUInt32(file, 24, (uint)weightCount);
        BinaryFormat.WriteUInt32(file, 28, (uint)payload.Length);
        BinaryFormat.WriteUInt32(file, 32, (uint)stored.Length);
        BinaryFormat.WriteUInt32(file, 36, BinaryFormat.Fnv1a(stored, 0, stored.Length));
        Buffer.BlockCopy(stored, 0, file, PerceptronModel.HeaderLength, stored.Length);
        return file;
    }

    /// <summary>
    ///     Encodes a model and writes it to a stream.
    /// </summary>
    /// <param name="output">The destination stream.</param>
    /// <param name="hashBits">The bucket space exponent (18 to 24).</param>
    /// <param name="temperature">The softmax temperature.</param>
    /// <param name="scale">The factor from integer weight to real weight.</param>
    /// <param name="featureSetVersion">The feature set version of the extractor that the weights belong to.</param>
    /// <param name="rows">The occupied buckets with a 17-element weight vector each.</param>
    /// <param name="compress">Whether to store the payload as raw Deflate.</param>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> or <paramref name="rows"/> is <c>null</c>.</exception>
    public static void Write(
        Stream output,
        int hashBits,
        float temperature,
        float scale,
        int featureSetVersion,
        IEnumerable<KeyValuePair<int, short[]>> rows,
        bool compress)
    {
        ArgumentNullException.ThrowIfNull(output);
        var file = Compile(hashBits, temperature, scale, featureSetVersion, rows, compress);
        output.Write(file, 0, file.Length);
    }
}
