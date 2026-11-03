#if NET
using System.Diagnostics;

namespace DemaConsulting.PartOfSpeech.Tests;

/// <summary>
///     Measures the bytes that an operation allocates on the calling thread in a way that is robust against the runtime's own
///     background activity.
/// </summary>
/// <remarks>
///     <para>
///         The method is a thorough warm-up, then the minimum over several measured batches. The warm-up runs the operation
///         for at least a number of rounds and a minimum duration, so that static initialization, JIT compilation, on-stack
///         replacement and the promotion from tier-0 to optimized code (which the runtime performs on a background thread
///         after a delay) are finished before anything is counted. A batch that is still disturbed, for example by a late
///         tier-up or by a one-time runtime allocation, is not decisive: the minimum over the batches is the batch that the
///         runtime disturbed least.
///     </para>
///     <para>
///         The minimum cannot hide a genuine allocation: an operation that allocates on every round allocates in every
///         batch, so its minimum is greater than zero too. Only a one-time or sporadic allocation, which is not a property
///         of the operation, is filtered out. The counter is <c>GC.GetAllocatedBytesForCurrentThread</c>, which exists on
///         .NET 8 and later only, so this class is not compiled for <c>net481</c>.
///     </para>
/// </remarks>
internal static class AllocationMeter
{
    /// <summary>
    ///     The minimum number of warm-up rounds.
    /// </summary>
    public const int WarmUpRounds = 500;

    /// <summary>
    ///     The minimum duration of the warm-up, in seconds; tiered compilation promotes hot methods on a background timer.
    /// </summary>
    public const double WarmUpSeconds = 0.3;

    /// <summary>
    ///     The number of measured batches of which the minimum is taken.
    /// </summary>
    public const int Batches = 10;

    /// <summary>
    ///     Measures the minimum number of bytes that one batch of an operation allocates on the calling thread.
    /// </summary>
    /// <param name="operation">The operation to measure; it must run on the calling thread.</param>
    /// <param name="roundsPerBatch">The number of rounds of the operation in one measured batch.</param>
    /// <returns>The minimum over the measured batches of the bytes allocated by one batch of <paramref name="roundsPerBatch"/> rounds.</returns>
    public static long MinimumBatchBytes(Action operation, int roundsPerBatch)
    {
        // Warm up: at least a number of rounds and a minimum duration, so that tiered compilation has completed
        var watch = Stopwatch.StartNew();
        var rounds = 0;
        while (rounds < WarmUpRounds || watch.Elapsed.TotalSeconds < WarmUpSeconds)
        {
            operation();
            rounds++;
        }

        // Measure several batches and keep the least-disturbed one
        var minimum = long.MaxValue;
        for (var batch = 0; batch < Batches; batch++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var round = 0; round < roundsPerBatch; round++)
            {
                operation();
            }

            minimum = Math.Min(minimum, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        return minimum;
    }
}
#endif
