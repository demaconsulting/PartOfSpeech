using System.Text.Json;
using System.Text.Json.Serialization;

namespace DemaConsulting.PartOfSpeech.Trainer;

/// <summary>
///     Reads and writes the JSON artifacts of the held-out evaluation (<c>operating-points.json</c> and <c>test-results.json</c>)
///     and builds what a held-out run requires of the selection.
/// </summary>
/// <remarks>
///     <para>
///         The files are plain JSON with camel-case names; unknown members are errors, so a misspelled setting cannot be
///         silently ignored. The formatting is canonical (indented, LF line endings, final newline), so the SHA-256 of the
///         committed file identifies the selection. The class is kept out of the engine files because the engine is also
///         compiled for .NET Framework by the test project, where this JSON API does not exist (like <see cref="ConfigFile"/>).
///         Stateless and thread-safe.
///     </para>
/// </remarks>
internal static class OperatingPointsFile
{
    /// <summary>
    ///     The serializer settings shared by reading and writing.
    /// </summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        IgnoreReadOnlyProperties = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>
    ///     Formats the selection artifact.
    /// </summary>
    /// <param name="data">The artifact.</param>
    /// <returns>The JSON text with LF line endings and a final newline.</returns>
    public static string Format(OperatingPointsData data) => JsonSerializer.Serialize(data, Options).Replace("\r\n", "\n") + "\n";

    /// <summary>
    ///     Formats the results of the held-out run.
    /// </summary>
    /// <param name="data">The results.</param>
    /// <returns>The JSON text with LF line endings and a final newline.</returns>
    public static string Format(ResultsData data) => JsonSerializer.Serialize(data, Options).Replace("\r\n", "\n") + "\n";

    /// <summary>
    ///     Parses the bytes of a selection artifact.
    /// </summary>
    /// <param name="bytes">The bytes of the file.</param>
    /// <returns>The artifact (not yet validated).</returns>
    /// <exception cref="InvalidDataException">The bytes are not a valid artifact.</exception>
    public static OperatingPointsData Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        try
        {
            return JsonSerializer.Deserialize<OperatingPointsData>(bytes, Options)
                ?? throw new InvalidDataException("The selection artifact is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Invalid selection artifact: " + ex.Message, ex);
        }
    }

    /// <summary>
    ///     Parses the bytes of a results file.
    /// </summary>
    /// <param name="bytes">The bytes of the file.</param>
    /// <returns>The results.</returns>
    /// <exception cref="InvalidDataException">The bytes are not a valid results file.</exception>
    public static ResultsData ParseResults(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        try
        {
            return JsonSerializer.Deserialize<ResultsData>(bytes, Options)
                ?? throw new InvalidDataException("The results file is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Invalid results file: " + ex.Message, ex);
        }
    }

    /// <summary>
    ///     Loads and validates the selection artifact.
    /// </summary>
    /// <param name="path">The full path of the artifact.</param>
    /// <param name="expectation">What the run requires of it.</param>
    /// <returns>The validated points.</returns>
    /// <exception cref="InvalidDataException">The file is missing, malformed or not valid for the frozen model and the data manifest.</exception>
    public static SelectedOperatingPoints Load(string path, OperatingPointsExpectation expectation)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(expectation);
        if (!File.Exists(path))
        {
            throw new InvalidDataException($"The selection artifact {path} does not exist; run the thresholds command (it reads development data only) and commit its output first.");
        }

        var bytes = File.ReadAllBytes(path);
        return SelectedOperatingPoints.Validate(Parse(bytes), ModelBuilder.Sha256Hex(bytes), expectation);
    }

    /// <summary>
    ///     Builds what a held-out run requires: the hash of the embedded model, the hash in the configuration and the manifest hashes of the development files.
    /// </summary>
    /// <param name="repositoryRoot">The full path of the repository root.</param>
    /// <param name="modelImageSha256">The SHA-256 of the uncompressed image of the model that will be scored.</param>
    /// <param name="configuredImageSha256">The image hash recorded in the training configuration.</param>
    /// <returns>The expectation.</returns>
    public static OperatingPointsExpectation Expect(string repositoryRoot, string modelImageSha256, string configuredImageSha256)
    {
        var expectation = new OperatingPointsExpectation { ModelImageSha256 = modelImageSha256, ConfiguredImageSha256 = configuredImageSha256 };
        foreach (var file in DevFiles(repositoryRoot))
        {
            expectation.DevFileHashes[file.Path] = file.Sha256;
        }

        return expectation;
    }

    /// <summary>
    ///     Lists the development files with the hashes of the data manifest.
    /// </summary>
    /// <param name="repositoryRoot">The full path of the repository root.</param>
    /// <returns>The files by path below the data folder.</returns>
    /// <exception cref="InvalidDataException">The manifest has no row for a development file.</exception>
    public static List<DevFileRecord> DevFiles(string repositoryRoot)
    {
        var manifest = DataManifest.Load(repositoryRoot);
        var files = new List<DevFileRecord>();
        foreach (var file in DataGuard.DevelopmentFiles)
        {
            var relative = file.Substring("data/".Length);
            files.Add(new DevFileRecord
            {
                Path = relative,
                Sha256 = manifest.TryGetValue(relative, out var sha) ? sha : throw new InvalidDataException($"The data manifest has no row for {relative}."),
            });
        }

        return files;
    }

    /// <summary>
    ///     Reads the weights that the library embeds.
    /// </summary>
    /// <returns>The bytes of the embedded weights file.</returns>
    /// <exception cref="InvalidOperationException">The embedded resource is missing.</exception>
    public static byte[] ReadEmbeddedWeights()
    {
        using var stream = EmbeddedModel.OpenResource(EmbeddedModel.WeightsResourceName)
            ?? throw new InvalidOperationException($"The embedded resource '{EmbeddedModel.WeightsResourceName}' is missing.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
