using System.Diagnostics;
using System.Reflection;

namespace DemaConsulting.PartOfSpeech.Tests;

/// <summary>
///     The xUnit collection of the throughput tests: its tests never run in parallel with any other test of the process.
/// </summary>
/// <remarks>
///     A collection with parallelization disabled runs after all parallel collections have finished, so a throughput test
///     does not compete with the other test classes of its own test host for the processor.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public class ThroughputCollection
{
    /// <summary>
    ///     The name of the collection.
    /// </summary>
    public const string Name = "Throughput";

    /// <summary>
    ///     Initializes a new instance of the <see cref="ThroughputCollection"/> class; xUnit never creates it, the class only carries the collection definition.
    /// </summary>
    protected ThroughputCollection()
    {
    }
}

/// <summary>
///     Measures the single-thread throughput of an operation in a way that is robust against load on the machine.
/// </summary>
/// <remarks>
///     The test proves a capability: the implementation can process more items per second than the gate. The method is
///     warm-up, then timed windows until one window exceeds the gate. A window that reads below the gate may have been
///     slowed by other processes (for example the test hosts of the other target frameworks, which <c>dotnet test</c>
///     runs at the same time, or a runner with few cores), so the meter sleeps for a short backoff, which lets the
///     contention clear, and measures again. The meter gives up only when the time budget or the window cap is used up;
///     a genuinely slow implementation is slow in every window and so still fails, while one fast window is enough to
///     prove the capability. Each window repeats the operation until a minimum duration has elapsed, so that a window is
///     long enough not to be decided by a single time slice of the scheduler. The 50,000 tokens per second requirement
///     is enforced in every run without code coverage; a run in which the library is instrumented (see
///     <see cref="IsInstrumented"/>) uses the relaxed <see cref="InstrumentedGateTokensPerSecond"/>, and the output of
///     every test states which gate applied.
/// </remarks>
internal static class ThroughputMeter
{
    /// <summary>
    ///     The requirement of the throughput tests, in tokens per second; it is enforced whenever the library is not instrumented.
    /// </summary>
    public const double GateTokensPerSecond = 50000;

    /// <summary>
    ///     The gate of a run in which the library is instrumented for code coverage (as in the CI pipeline), in tokens per second.
    /// </summary>
    /// <remarks>
    ///     Coverage instrumentation adds a counter update to every sequence point of the library, which slows the hot
    ///     loops by a factor of 20 or more: with coverlet the tagger and the decoder were measured at 24,000 to 64,000
    ///     tokens per second on a development machine (446,000 to 1,244,000 without coverage there) and at 49,000 on
    ///     the CI runner (CI run 41724380423). The 50,000 requirement therefore cannot be proven in such a run; it is
    ///     proven by the runs without instrumentation (<c>build.ps1</c>). This gate is one tenth of the requirement and
    ///     still about five times below the slowest instrumented rate measured, so a loop that is an order of
    ///     magnitude too slow, or broken, still fails.
    /// </remarks>
    public const double InstrumentedGateTokensPerSecond = 5000;

    /// <summary>
    ///     The environment variables that make the runtime load a profiler; coverage tools that use a profiler set them.
    /// </summary>
    private static readonly string[] ProfilerVariables = ["CORECLR_ENABLE_PROFILING", "COR_ENABLE_PROFILING"];

    /// <summary>
    ///     The namespace of the type that the coverlet instrumentation injects into every assembly that it instruments.
    /// </summary>
    private const string CoverletTrackerNamespace = "Coverlet.Core.Instrumentation.Tracker";

    /// <summary>
    ///     Gets a value indicating whether the run is instrumented, that is whether the library under test is rewritten by
    ///     coverlet or a profiler is enabled for the process (<c>CORECLR_ENABLE_PROFILING=1</c> or <c>COR_ENABLE_PROFILING=1</c>).
    /// </summary>
    public static bool IsInstrumented { get; } = DetectInstrumentation();

    /// <summary>
    ///     Gets the gate that applies to this run: <see cref="GateTokensPerSecond"/>, or
    ///     <see cref="InstrumentedGateTokensPerSecond"/> when <see cref="IsInstrumented"/> is <c>true</c>.
    /// </summary>
    public static double Gate => IsInstrumented ? InstrumentedGateTokensPerSecond : GateTokensPerSecond;

    /// <summary>
    ///     Describes a measurement for the test output: the best rate, the number of windows, whether the run is instrumented and the gate.
    /// </summary>
    /// <param name="rate">The best rate, in items per second.</param>
    /// <param name="windows">The number of timed windows.</param>
    /// <returns>The description.</returns>
    public static string Describe(double rate, int windows) =>
        $"best of {windows} windows: {rate:N0} tokens per second; instrumented: {(IsInstrumented ? "yes" : "no")}; gate: {Gate:N0} tokens per second" +
        (IsInstrumented ? $" (relaxed from {GateTokensPerSecond:N0} because the library is instrumented for code coverage)" : string.Empty);

    /// <summary>
    ///     The maximum number of timed windows.
    /// </summary>
    public const int MaxWindows = 60;

    /// <summary>
    ///     The minimum duration of one timed window, in seconds.
    /// </summary>
    public const double MinWindowSeconds = 0.25;

    /// <summary>
    ///     The total time budget for all timed windows and pauses, in seconds; no new window starts after it is used up.
    /// </summary>
    public const double BudgetSeconds = 30;

    /// <summary>
    ///     The pause after a window that stayed below the gate, in milliseconds.
    /// </summary>
    public const int BackoffMilliseconds = 200;

    /// <summary>
    ///     The minimum duration of the warm-up, in seconds; it lets the tiered compilation finish even when the machine is busy.
    /// </summary>
    public const double WarmUpSeconds = 0.5;

    /// <summary>
    ///     Measures the best rate of an operation over timed windows, after a warm-up, and stops at the first window above the gate.
    /// </summary>
    /// <param name="operation">The operation, which processes <paramref name="itemsPerRun"/> items on the calling thread.</param>
    /// <param name="itemsPerRun">The number of items (tokens) that one run of the operation processes.</param>
    /// <param name="gate">The rate that proves the capability, in items per second.</param>
    /// <param name="windows">Receives the number of timed windows that were taken.</param>
    /// <returns>The best rate over the timed windows, in items per second.</returns>
    public static double BestRate(Action operation, int itemsPerRun, double gate, out int windows)
    {
        // Warm up: JIT compilation, tiered compilation and cold caches must not count
        var total = Stopwatch.StartNew();
        var warmRuns = 0;
        while (warmRuns < 2 || total.Elapsed.TotalSeconds < WarmUpSeconds)
        {
            operation();
            warmRuns++;
        }

        var budget = Stopwatch.StartNew();
        var best = 0.0;
        windows = 0;
        while (windows < MaxWindows && (windows == 0 || budget.Elapsed.TotalSeconds < BudgetSeconds))
        {
            // Time whole runs until the window is long enough, then compute the rate of this window
            var runs = 0;
            var watch = Stopwatch.StartNew();
            do
            {
                operation();
                runs++;
            }
            while (watch.Elapsed.TotalSeconds < MinWindowSeconds);

            windows++;
            var rate = (double)runs * itemsPerRun / watch.Elapsed.TotalSeconds;
            best = Math.Max(best, rate);

            // One window above the gate proves the capability; otherwise let other processes finish and measure again
            if (rate > gate)
            {
                break;
            }

            Thread.Sleep(BackoffMilliseconds);
        }

        return best;
    }

    /// <summary>
    ///     Detects whether the library under test is instrumented for code coverage or a profiler is enabled.
    /// </summary>
    /// <returns><c>true</c> when the run is instrumented.</returns>
    private static bool DetectInstrumentation()
    {
        // A profiler (for example dotnet-coverage) is enabled for the process through the environment
        foreach (var name in ProfilerVariables)
        {
            if (Environment.GetEnvironmentVariable(name) == "1")
            {
                return true;
            }
        }

        // Coverlet rewrites the library on disk and adds a tracker type to it; the assembly in memory shows that
        Type?[] types;
        try
        {
            types = typeof(Tagger).Assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            types = exception.Types;
        }

        return types.Any(type => type?.Namespace == CoverletTrackerNamespace);
    }
}
