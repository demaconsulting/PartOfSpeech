using System.Security.Cryptography;
using System.Text;

namespace DemaConsulting.PartOfSpeech.Trainer;

/// <summary>
///     The outcome of comparing a freshly trained image with a committed weights file.
/// </summary>
internal enum CheckStatus
{
    /// <summary>
    ///     The committed file has the same uncompressed image as the fresh training.
    /// </summary>
    Current = 0,

    /// <summary>
    ///     There is no committed file.
    /// </summary>
    Missing = 1,

    /// <summary>
    ///     The committed file is valid but differs from the fresh training.
    /// </summary>
    Stale = 2,

    /// <summary>
    ///     The committed file is not a valid weights file of this build.
    /// </summary>
    Invalid = 3,
}

/// <summary>
///     Turns trained weights into the weights file and decides whether a committed file is current.
/// </summary>
/// <remarks>
///     <para>
///         The identity claim is made for the <em>uncompressed</em> image: Deflate output depends on the zlib of the
///         runtime, but the header and the payload before compression are fully determined by the weights. A check
///         therefore compares the uncompressed image of the fresh run with the uncompressed image of the committed
///         (compressed) file, which is also what <c>LexiconCompiler --check</c> does for the lexicon.
///     </para>
///     <para>
///         Stateless and thread-safe.
///     </para>
/// </remarks>
internal static class ModelBuilder
{
    /// <summary>
    ///     The offset of the temperature field in the weights header.
    /// </summary>
    private const int TemperatureOffset = 12;

    /// <summary>
    ///     Averages and quantizes a trained perceptron and encodes the weights file.
    /// </summary>
    /// <param name="perceptron">The trained perceptron.</param>
    /// <param name="config">The configuration that supplies the scale, the pruning threshold and the temperature.</param>
    /// <param name="compress">Whether to Deflate the payload (the committed file) or not (the image that is compared).</param>
    /// <returns>The file bytes.</returns>
    public static byte[] Build(AveragedPerceptron perceptron, TrainingConfig config, bool compress) =>
        Encode(Rows(perceptron, config), config, compress);

    /// <summary>
    ///     Averages and quantizes a trained perceptron into the rows of the weights file.
    /// </summary>
    /// <param name="perceptron">The trained perceptron.</param>
    /// <param name="config">The configuration that supplies the quantization scale and the pruning threshold.</param>
    /// <returns>The non-empty rows in ascending bucket order.</returns>
    public static List<KeyValuePair<int, short[]>> Rows(AveragedPerceptron perceptron, TrainingConfig config)
    {
        ArgumentNullException.ThrowIfNull(perceptron);
        ArgumentNullException.ThrowIfNull(config);
        return perceptron.Quantize(config.QuantScale, config.PruneThreshold);
    }

    /// <summary>
    ///     Encodes quantized rows as a weights file with the header values of a configuration.
    /// </summary>
    /// <param name="rows">The rows.</param>
    /// <param name="config">The configuration that supplies the bucket space, the scale, the temperature and the feature groups (the mask of the header).</param>
    /// <param name="compress">Whether to Deflate the payload.</param>
    /// <returns>The file bytes.</returns>
    public static byte[] Encode(IEnumerable<KeyValuePair<int, short[]>> rows, TrainingConfig config, bool compress)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(config);
        var groups = config.ResolveGroups();
        return PerceptronModelWriter.Compile(config.HashBits, (float)config.Temperature, config.Scale, FeatureExtractor.FeatureSetVersionFor(groups), rows, compress, (uint)groups);
    }

    /// <summary>
    ///     Computes the lower-case hexadecimal SHA-256 of bytes.
    /// </summary>
    /// <param name="data">The bytes.</param>
    /// <returns>The hash as 64 hexadecimal digits.</returns>
    public static string Sha256Hex(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(data);
        var text = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
        {
            text.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        }

        return text.ToString();
    }

    /// <summary>
    ///     Returns a copy of a weights file with another temperature in its header.
    /// </summary>
    /// <remarks>
    ///     The header checksum covers the stored payload only, so the temperature can be replaced without re-encoding;
    ///     the calibration uses this to evaluate many temperatures through the real decoder.
    /// </remarks>
    /// <param name="file">A valid weights file (compressed or not).</param>
    /// <param name="temperature">The new temperature; finite and greater than zero.</param>
    /// <returns>The patched copy.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="temperature"/> is not a finite positive number.</exception>
    public static byte[] WithTemperature(byte[] file, float temperature)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (!BinaryFormat.IsFinitePositive(temperature))
        {
            throw new ArgumentOutOfRangeException(nameof(temperature), temperature, "The temperature must be finite and greater than zero.");
        }

        var copy = (byte[])file.Clone();
        BinaryFormat.WriteSingle(copy, TemperatureOffset, temperature);
        return copy;
    }

    /// <summary>
    ///     Compares a freshly built uncompressed image with a committed weights file.
    /// </summary>
    /// <param name="freshImage">The uncompressed image of the fresh training.</param>
    /// <param name="committedFile">The committed file (compressed or not), or <c>null</c> if it does not exist.</param>
    /// <returns>The status.</returns>
    public static CheckStatus Check(byte[] freshImage, byte[]? committedFile)
    {
        ArgumentNullException.ThrowIfNull(freshImage);
        if (committedFile == null)
        {
            return CheckStatus.Missing;
        }

        byte[] committedImage;
        try
        {
            committedImage = PerceptronModel.ToUncompressedImage(committedFile);
        }
        catch (InvalidDataException)
        {
            return CheckStatus.Invalid;
        }

        return ImagesEqual(freshImage, committedImage) ? CheckStatus.Current : CheckStatus.Stale;
    }

    /// <summary>
    ///     Compares two byte arrays for equality of length and content.
    /// </summary>
    /// <param name="left">The first array.</param>
    /// <param name="right">The second array.</param>
    /// <returns><c>true</c> if both hold the same bytes.</returns>
    private static bool ImagesEqual(byte[] left, byte[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }
}
