using BenchmarkDotNet.Running;

namespace DemaConsulting.PartOfSpeech.Benchmarks;

/// <summary>
///     Command-line entry point of the benchmarks.
/// </summary>
/// <remarks>
///     The benchmarks measure the performance and memory properties that the design document states; they are opt-in
///     developer tooling and are not evidence of a requirement. The arguments are those of BenchmarkDotNet, for example
///     <c>--filter *</c> to run every benchmark or <c>--filter *Tagging*</c> to run one class.
/// </remarks>
internal static class Program
{
    /// <summary>
    ///     Runs the benchmarks that the arguments select.
    /// </summary>
    /// <param name="args">The command-line arguments of BenchmarkDotNet.</param>
    public static void Main(string[] args) =>
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
