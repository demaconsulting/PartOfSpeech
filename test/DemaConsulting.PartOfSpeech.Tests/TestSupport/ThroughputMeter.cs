using System.Diagnostics;

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
///     long enough not to be decided by a single time slice of the scheduler.
/// </remarks>
internal static class ThroughputMeter
{
    /// <summary>
    ///     The gate of the throughput tests, in tokens per second.
    /// </summary>
    public const double GateTokensPerSecond = 50000;

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
}
