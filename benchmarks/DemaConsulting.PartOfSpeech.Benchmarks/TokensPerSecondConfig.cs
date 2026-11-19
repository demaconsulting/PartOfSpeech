using BenchmarkDotNet.Configs;

namespace DemaConsulting.PartOfSpeech.Benchmarks;

/// <summary>
///     The configuration of the tagging benchmarks: the default jobs plus the <see cref="TokensPerSecondColumn"/>.
/// </summary>
internal sealed class TokensPerSecondConfig : ManualConfig
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="TokensPerSecondConfig"/> class.
    /// </summary>
    public TokensPerSecondConfig() => AddColumn(new TokensPerSecondColumn());
}
