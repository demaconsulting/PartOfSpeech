namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     The averaged-perceptron weights (format <c>POSW</c> version 2) in the in-memory form that the decoder reads.
/// </summary>
/// <remarks>
///     <para>
///         The model maps a feature bucket (the hash of one feature template instance, below <c>2^hashBits</c>) to a
///         sparse row of integer weights, one weight per tag. Only occupied buckets are stored: a bit set with a rank
///         table finds the row of a bucket with one bit test and one population count, and the rows are packed into
///         flat arrays so that a lookup touches no object. Integer weights (<c>int16</c> times one global scale) make
///         the score sums exact, so the chosen tags do not depend on the floating-point behavior of a platform.
///     </para>
///     <para>
///         The class is immutable after <see cref="Load(Stream, int)"/> and safe for concurrent use.
///         <see cref="Load(byte[], int)"/> validates the whole file, so <see cref="AddScores"/> cannot fail on
///         malformed data.
///     </para>
/// </remarks>
internal sealed class PerceptronModel
{
    /// <summary>
    ///     The magic of a weights file.
    /// </summary>
    public const string Magic = "POSW";

    /// <summary>
    ///     The length of the fixed header in bytes.
    /// </summary>
    public const int HeaderLength = 48;

    /// <summary>
    ///     The format version that this build reads and writes.
    /// </summary>
    public const int FormatVersion = 2;

    /// <summary>
    ///     The smallest allowed bucket space exponent.
    /// </summary>
    public const int MinHashBits = 18;

    /// <summary>
    ///     The largest allowed bucket space exponent.
    /// </summary>
    public const int MaxHashBits = 24;

    /// <summary>
    ///     The largest uncompressed payload length that is accepted (256 MiB; a bound independent of the untrusted row and weight counts).
    /// </summary>
    public const int MaxPayloadLength = 256 * 1024 * 1024;

    /// <summary>
    ///     The description of the file kind used in error messages.
    /// </summary>
    private const string Kind = "weights file";

    /// <summary>
    ///     One bit per bucket: set when the bucket has a row.
    /// </summary>
    private readonly ulong[] _occupancy;

    /// <summary>
    ///     For every 64-bit word of <see cref="_occupancy"/>, the number of rows in all earlier words.
    /// </summary>
    private readonly int[] _rankBase;

    /// <summary>
    ///     The index of the first weight of every row, plus one end marker.
    /// </summary>
    private readonly int[] _rowStart;

    /// <summary>
    ///     The tag of every weight.
    /// </summary>
    private readonly byte[] _tags;

    /// <summary>
    ///     The quantized value of every weight.
    /// </summary>
    private readonly short[] _weights;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PerceptronModel"/> class from validated parts.
    /// </summary>
    /// <param name="hashBits">The bucket space exponent.</param>
    /// <param name="temperature">The softmax temperature.</param>
    /// <param name="scale">The weight scale.</param>
    /// <param name="occupancy">The occupancy bit set.</param>
    /// <param name="rankBase">The rank table.</param>
    /// <param name="rowStart">The row starts.</param>
    /// <param name="tags">The tag of every weight.</param>
    /// <param name="weights">The weights.</param>
    private PerceptronModel(int hashBits, float temperature, float scale, ulong[] occupancy, int[] rankBase, int[] rowStart, byte[] tags, short[] weights)
    {
        HashBits = hashBits;
        Temperature = temperature;
        Scale = scale;
        _occupancy = occupancy;
        _rankBase = rankBase;
        _rowStart = rowStart;
        _tags = tags;
        _weights = weights;
    }

    /// <summary>
    ///     Gets the exponent of the bucket space (the model has <c>2^HashBits</c> buckets).
    /// </summary>
    public int HashBits { get; }

    /// <summary>
    ///     Gets the softmax temperature that calibrates the confidences of the model.
    /// </summary>
    public float Temperature { get; }

    /// <summary>
    ///     Gets the factor that converts an integer score into the real score scale.
    /// </summary>
    public float Scale { get; }

    /// <summary>
    ///     Gets the number of occupied buckets.
    /// </summary>
    public int RowCount => _rowStart.Length - 1;

    /// <summary>
    ///     Gets the total number of non-zero weights.
    /// </summary>
    public int WeightCount => _weights.Length;

    /// <summary>
    ///     Loads and validates a weights file.
    /// </summary>
    /// <param name="stream">A stream positioned at the start of the file; it is read to its end.</param>
    /// <param name="expectedFeatureSetVersion">The feature set version of the extractor that will use the weights.</param>
    /// <returns>The model.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidDataException">The file is not a valid weights file of a supported version and feature set.</exception>
    public static PerceptronModel Load(Stream stream, int expectedFeatureSetVersion) =>
        Load(BinaryFormat.ReadAllBytes(stream), expectedFeatureSetVersion);

    /// <summary>
    ///     Loads and validates a weights file held in memory.
    /// </summary>
    /// <param name="file">The whole file.</param>
    /// <param name="expectedFeatureSetVersion">The feature set version of the extractor that will use the weights.</param>
    /// <returns>The model.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="file"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidDataException">The file is not a valid weights file of a supported version and feature set.</exception>
    public static PerceptronModel Load(byte[] file, int expectedFeatureSetVersion)
    {
        ArgumentNullException.ThrowIfNull(file);

        // Header: identity first, then the numeric fields that size the payload
        var flags = BinaryFormat.ReadHeader(file, Magic, HeaderLength, FormatVersion, Kind);
        var featureSet = BinaryFormat.ReadUInt16(file, 8);
        if (featureSet != expectedFeatureSetVersion)
        {
            throw new InvalidDataException(
                $"Feature set mismatch: the weights were trained with feature set {featureSet} but this build uses {expectedFeatureSetVersion}.");
        }

        if (file[10] != UposExtensions.Count)
        {
            throw new InvalidDataException($"Invalid weights file: it has {file[10]} tags but this build uses {UposExtensions.Count}.");
        }

        var hashBits = (int)file[11];
        if (hashBits < MinHashBits || hashBits > MaxHashBits)
        {
            throw new InvalidDataException($"Invalid weights file: hashBits {hashBits} is outside {MinHashBits} to {MaxHashBits}.");
        }

        var temperature = BinaryFormat.ReadSingle(file, 12);
        var scale = BinaryFormat.ReadSingle(file, 16);
        if (!BinaryFormat.IsFinitePositive(temperature) || !BinaryFormat.IsFinitePositive(scale))
        {
            throw new InvalidDataException("Invalid weights file: the temperature and the weight scale must be finite and greater than zero.");
        }

        var rowCount = BinaryFormat.ReadInt32Field(file, 20, "row count");
        var weightCount = BinaryFormat.ReadInt32Field(file, 24, "weight count");
        var payloadLength = BinaryFormat.ReadInt32Field(file, 28, "payload length");
        var storedLength = BinaryFormat.ReadInt32Field(file, 32, "stored length");
        var checksum = BinaryFormat.ReadUInt32(file, 36);
        for (var i = 40; i < HeaderLength; i++)
        {
            if (file[i] != 0)
            {
                throw new InvalidDataException("Invalid weights file: the reserved bytes are not zero.");
            }
        }

        if (rowCount > (1L << hashBits) || weightCount < rowCount || (long)weightCount > (long)UposExtensions.Count * rowCount)
        {
            throw new InvalidDataException("Invalid weights file: the row and weight counts are inconsistent.");
        }

        // Payload: verified bytes, then the four columnar sections
        var payload = BinaryFormat.ReadPayload(file, HeaderLength, flags, payloadLength, MaxPayloadLength, storedLength, checksum, Kind);
        return Build(payload, hashBits, temperature, scale, rowCount, weightCount);
    }

    /// <summary>
    ///     Adds the weights of one bucket to a score vector.
    /// </summary>
    /// <remarks>
    ///     This is the innermost operation of the decoder, so it is a bit test, a population count and a short loop
    ///     over flat arrays. A bucket without a row adds nothing.
    /// </remarks>
    /// <param name="bucket">The bucket, from <c>0</c> to <c>2^HashBits - 1</c>.</param>
    /// <param name="scores">The score vector with one element per tag; the weights are added to it.</param>
    public void AddScores(int bucket, int[] scores)
    {
        // Test the occupancy bit of the bucket
        var wordIndex = bucket >> 6;
        var word = _occupancy[wordIndex];
        var bit = bucket & 63;
        if (((word >> bit) & 1UL) == 0)
        {
            return;
        }

        // The row index is the number of occupied buckets before this one
        var row = _rankBase[wordIndex] + BinaryFormat.PopCount(word & ((1UL << bit) - 1));
        var end = _rowStart[row + 1];
        for (var i = _rowStart[row]; i < end; i++)
        {
            scores[_tags[i]] += _weights[i];
        }
    }

    /// <summary>
    ///     Builds the runtime structure from a verified payload and validates its columns.
    /// </summary>
    /// <param name="payload">The uncompressed payload.</param>
    /// <param name="hashBits">The bucket space exponent.</param>
    /// <param name="temperature">The softmax temperature.</param>
    /// <param name="scale">The weight scale.</param>
    /// <param name="rowCount">The declared number of rows.</param>
    /// <param name="weightCount">The declared number of weights.</param>
    /// <returns>The model.</returns>
    /// <exception cref="InvalidDataException">A column violates the format rules.</exception>
    private static PerceptronModel Build(byte[] payload, int hashBits, float temperature, float scale, int rowCount, int weightCount)
    {
        // Section A: bucket gaps. The first bucket is gap - 1 so that a gap is never zero
        var occupancy = new ulong[(int)(((1L << hashBits) + 63) >> 6)];
        var position = 0;
        long bucket = -1;
        var bucketLimit = 1L << hashBits;
        for (var row = 0; row < rowCount; row++)
        {
            var gap = BinaryFormat.ReadVarint(payload, ref position, payload.Length);
            if (gap == 0)
            {
                throw new InvalidDataException("Invalid weights file: a bucket gap is zero (buckets must ascend strictly).");
            }

            bucket += gap;
            if (bucket >= bucketLimit)
            {
                throw new InvalidDataException("Invalid weights file: a bucket is outside the bucket space.");
            }

            occupancy[bucket >> 6] |= 1UL << (int)(bucket & 63);
        }

        // The rest of the payload has exactly three fixed-size columns
        var rowCountsStart = position;
        var tagsStart = rowCountsStart + rowCount;
        var weightsStart = tagsStart + weightCount;
        if ((long)weightsStart + (2L * weightCount) != payload.Length)
        {
            throw new InvalidDataException("Invalid weights file: the payload length does not match the row and weight counts.");
        }

        // Section B: row sizes become the row start table
        var rowStart = new int[rowCount + 1];
        for (var row = 0; row < rowCount; row++)
        {
            var size = payload[rowCountsStart + row];
            if (size < 1 || size > UposExtensions.Count)
            {
                throw new InvalidDataException("Invalid weights file: a row has no weights or more weights than tags.");
            }

            rowStart[row + 1] = rowStart[row] + size;
        }

        if (rowStart[rowCount] != weightCount)
        {
            throw new InvalidDataException("Invalid weights file: the row sizes do not add up to the weight count.");
        }

        // Sections C and D: tags ascend strictly within a row; weights are never zero and never -32768
        var tags = new byte[weightCount];
        var weights = new short[weightCount];
        for (var row = 0; row < rowCount; row++)
        {
            var previousTag = -1;
            for (var i = rowStart[row]; i < rowStart[row + 1]; i++)
            {
                var tag = payload[tagsStart + i];
                if (tag >= UposExtensions.Count || tag <= previousTag)
                {
                    throw new InvalidDataException("Invalid weights file: the tags of a row are out of range or not strictly ascending.");
                }

                previousTag = tag;
                tags[i] = tag;
                var weight = (short)(payload[weightsStart + (2 * i)] | (payload[weightsStart + (2 * i) + 1] << 8));
                if (weight == 0 || weight == short.MinValue)
                {
                    throw new InvalidDataException("Invalid weights file: a weight is zero or -32768.");
                }

                weights[i] = weight;
            }
        }

        // The rank table: rows stored before each 64-bit word
        var rankBase = new int[occupancy.Length];
        var running = 0;
        for (var i = 0; i < occupancy.Length; i++)
        {
            rankBase[i] = running;
            running += BinaryFormat.PopCount(occupancy[i]);
        }

        return new PerceptronModel(hashBits, temperature, scale, occupancy, rankBase, rowStart, tags, weights);
    }
}
