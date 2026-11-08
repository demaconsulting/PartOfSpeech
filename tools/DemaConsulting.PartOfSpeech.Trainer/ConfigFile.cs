using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace DemaConsulting.PartOfSpeech.Trainer;

/// <summary>
///     Reads and writes the committed training configuration (<c>training.json</c>).
/// </summary>
/// <remarks>
///     The file is plain JSON with camel-case names and string enums. Unknown members are errors, so a misspelled
///     setting cannot be silently ignored. The class is kept out of the engine files because the engine is also
///     compiled for .NET Framework by the test project, where this JSON API does not exist. Stateless and thread-safe.
/// </remarks>
internal static class ConfigFile
{
    /// <summary>
    ///     The serializer settings shared by reading and writing.
    /// </summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        IgnoreReadOnlyProperties = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { OmitUnusedFeatureSettings } },
    };

    /// <summary>
    ///     Leaves the feature settings out of the file form while they are at their defaults, so that the committed configuration of the
    ///     shipped feature set keeps its form: <c>features</c> is written when a group is selected, <c>previewFolds</c> when the
    ///     <c>rightPreview</c> group is selected or the value is not the default.
    /// </summary>
    /// <param name="info">The type information that is being built.</param>
    private static void OmitUnusedFeatureSettings(JsonTypeInfo info)
    {
        if (info.Type != typeof(TrainingConfig))
        {
            return;
        }

        foreach (var property in info.Properties)
        {
            if (property.Name == "features")
            {
                property.ShouldSerialize = (_, value) => value is List<string> { Count: > 0 };
            }
            else if (property.Name == "previewFolds")
            {
                property.ShouldSerialize = (owner, value) =>
                    value is not TrainingConfig.DefaultPreviewFolds || (owner is TrainingConfig { Features: { } features } && features.Any(f => string.Equals(f, "rightPreview", StringComparison.OrdinalIgnoreCase) || string.Equals(f, "F3", StringComparison.OrdinalIgnoreCase)));
            }
        }
    }

    /// <summary>
    ///     Parses and validates a configuration.
    /// </summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>The validated configuration.</returns>
    /// <exception cref="InvalidDataException">The text is not valid JSON, has unknown members or violates a setting range.</exception>
    public static TrainingConfig Parse(string json)
    {
        TrainingConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<TrainingConfig>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Invalid training configuration: " + ex.Message, ex);
        }

        if (config == null)
        {
            throw new InvalidDataException("Invalid training configuration: the file is empty.");
        }

        config.Validate();
        return config;
    }

    /// <summary>
    ///     Reads and validates a configuration file.
    /// </summary>
    /// <param name="path">The full path of the file.</param>
    /// <returns>The validated configuration.</returns>
    /// <exception cref="IOException">The file cannot be read.</exception>
    /// <exception cref="InvalidDataException">The content is invalid.</exception>
    public static TrainingConfig Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>
    ///     Formats a configuration as JSON.
    /// </summary>
    /// <param name="config">The configuration.</param>
    /// <returns>The JSON text with a trailing newline.</returns>
    public static string Format(TrainingConfig config) => JsonSerializer.Serialize(config, Options).Replace("\r\n", "\n") + "\n";

    /// <summary>
    ///     Writes a configuration file.
    /// </summary>
    /// <param name="path">The full path of the file.</param>
    /// <param name="config">The configuration.</param>
    /// <exception cref="IOException">The file cannot be written.</exception>
    public static void Save(string path, TrainingConfig config) => File.WriteAllText(path, Format(config));
}
