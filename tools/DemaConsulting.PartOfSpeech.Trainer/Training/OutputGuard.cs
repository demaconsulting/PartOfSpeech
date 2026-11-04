namespace DemaConsulting.PartOfSpeech.Trainer;

/// <summary>
///     Protects the shipped weights file from being overwritten by an experiment.
/// </summary>
/// <remarks>
///     The default output of <c>train</c> is the embedded weights file, which the build holds equal to the committed
///     configuration. A training run with experiment overrides or with calibration must therefore name its output file,
///     so that a casual experiment cannot replace the shipped weights silently.
/// </remarks>
internal static class OutputGuard
{
    /// <summary>
    ///     Refuses an experiment that does not name an output file.
    /// </summary>
    /// <param name="experiment"><c>true</c> if the run overrides a setting or calibrates the temperature.</param>
    /// <param name="output">The value of <c>--output</c>, or <c>null</c> if the option is absent.</param>
    /// <exception cref="ArgumentException">The run is an experiment and <paramref name="output"/> is null or empty.</exception>
    public static void EnsureExplicitOutput(bool experiment, string? output)
    {
        if (experiment && string.IsNullOrEmpty(output))
        {
            throw new ArgumentException(
                "Experiment overrides and --calibrate require --output, so that the shipped weights are never overwritten by an experiment.");
        }
    }
}
