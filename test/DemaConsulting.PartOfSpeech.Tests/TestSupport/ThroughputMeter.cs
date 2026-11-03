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
///     The method is warm-up, then the best of several timed passes: the best pass is the one least disturbed by other
///     processes (for example the test hosts of the other target frameworks, which <c>dotnet test</c> runs at the same
///     time), so a slow pass caused by contention cannot fail the gate while a genuinely slow implementation still does.
///     Each timed pass repeats the operation until a minimum duration has elapsed, so that a pass is long enough not to
///     be decided by a single time slice of the scheduler.
/// </remarks>
internal static class ThroughputMeter
{
    /// <summary>
    ///     The number of timed passes of which the best is taken.
    /// </summary>
    public const int Passes = 7;

    /// <summary>
    ///     The minimum duration of one timed pass, in seconds.
    /// </summary>
    public const double MinPassSeconds = 0.25;

    /// <summary>
    ///     Measures the best rate of an operation over several timed passes, after one warm-up run.
    /// </summary>
    /// <param name="operation">The operation, which processes <paramref name="itemsPerRun"/> items on the calling thread.</param>
    /// <param name="itemsPerRun">The number of items (tokens) that one run of the operation processes.</param>
    /// <returns>The best rate over the timed passes, in items per second.</returns>
    public static double BestRate(Action operation, int itemsPerRun)
    {
        // Warm up: JIT compilation, tiered compilation and cold caches must not count
        operation();
        operation();

        var best = 0.0;
        for (var pass = 0; pass < Passes; pass++)
        {
            // Time whole runs until the pass is long enough, then compute the rate of this pass
            var runs = 0;
            var watch = Stopwatch.StartNew();
            do
            {
                operation();
                runs++;
            }
            while (watch.Elapsed.TotalSeconds < MinPassSeconds);

            best = Math.Max(best, (double)runs * itemsPerRun / watch.Elapsed.TotalSeconds);
        }

        return best;
    }
}
