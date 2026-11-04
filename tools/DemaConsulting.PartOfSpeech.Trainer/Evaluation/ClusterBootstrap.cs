namespace DemaConsulting.PartOfSpeech.Trainer;

/// <summary>
///     A percentile bootstrap that resamples whole clusters (documents, or single rows for a set that has no documents) and
///     evaluates any number of statistics of a tally on the same draws.
/// </summary>
/// <remarks>
///     <para>
///         The statistics are functions of an <see cref="EvalTally"/>, so a resample is the sum of the tallies of the drawn
///         clusters. A paired difference (for example the harmful rate at an operating point minus the harmful rate at 0.45
///         on the same decisions) is the statistic <c>t =&gt; f(t) - g(t)</c>, evaluated on the same draw, so the pairing is
///         exact. The generator is <see cref="DeterministicRandom"/> with a fixed seed, so the intervals are reproducible.
///         The interval rule is the one of <see cref="Bootstrap"/>. Stateless and thread-safe.
///     </para>
/// </remarks>
internal static class ClusterBootstrap
{
    /// <summary>
    ///     Computes the observed value and the 95 percent interval of every statistic.
    /// </summary>
    /// <param name="clusters">The tally of every cluster.</param>
    /// <param name="statistics">The statistics to evaluate.</param>
    /// <param name="resamples">The number of resamples.</param>
    /// <param name="seed">The seed of the generator.</param>
    /// <returns>One result for each statistic; <see cref="BootstrapResult.Difference"/> holds the observed value.</returns>
    /// <exception cref="ArgumentException">There are no clusters or no statistic, or the number of resamples is not positive.</exception>
    public static BootstrapResult[] Intervals(IReadOnlyList<EvalTally> clusters, IReadOnlyList<Func<EvalTally, double>> statistics, int resamples, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(clusters);
        ArgumentNullException.ThrowIfNull(statistics);
        if (clusters.Count == 0 || statistics.Count == 0 || resamples <= 0)
        {
            throw new ArgumentException("A bootstrap needs clusters, statistics and a positive number of resamples.");
        }

        // The observed values come from the sum of all clusters
        var all = new EvalTally();
        foreach (var cluster in clusters)
        {
            all.Add(cluster);
        }

        // Each resample draws as many clusters as there are, with replacement, and evaluates every statistic on the draw
        var random = new DeterministicRandom(seed);
        var samples = new double[statistics.Count][];
        for (var s = 0; s < samples.Length; s++)
        {
            samples[s] = new double[resamples];
        }

        for (var r = 0; r < resamples; r++)
        {
            var draw = new EvalTally();
            for (var c = 0; c < clusters.Count; c++)
            {
                draw.Add(clusters[random.NextInt(clusters.Count)]);
            }

            for (var s = 0; s < samples.Length; s++)
            {
                samples[s][r] = statistics[s](draw);
            }
        }

        var results = new BootstrapResult[statistics.Count];
        for (var s = 0; s < results.Length; s++)
        {
            results[s] = Bootstrap.Interval(statistics[s](all), samples[s]);
        }

        return results;
    }

    /// <summary>
    ///     Computes the observed value and the 95 percent interval of one statistic.
    /// </summary>
    /// <param name="clusters">The tally of every cluster.</param>
    /// <param name="statistic">The statistic.</param>
    /// <param name="resamples">The number of resamples.</param>
    /// <param name="seed">The seed of the generator.</param>
    /// <returns>The result; <see cref="BootstrapResult.Difference"/> holds the observed value.</returns>
    public static BootstrapResult Interval(IReadOnlyList<EvalTally> clusters, Func<EvalTally, double> statistic, int resamples, ulong seed) =>
        Intervals(clusters, new[] { statistic }, resamples, seed)[0];
}
