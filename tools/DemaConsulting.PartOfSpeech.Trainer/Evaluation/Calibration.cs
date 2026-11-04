using System.Globalization;

namespace DemaConsulting.PartOfSpeech.Trainer;

/// <summary>
///     One temperature of the calibration search with its open-development log-likelihood and calibration error.
/// </summary>
internal sealed class CalibrationPoint
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="CalibrationPoint"/> class.
    /// </summary>
    /// <param name="temperature">The temperature.</param>
    /// <param name="nll">The mean negative log-likelihood on the open development split.</param>
    /// <param name="ece">The expected calibration error in percent on the open development split.</param>
    public CalibrationPoint(double temperature, double nll, double ece)
    {
        Temperature = temperature;
        Nll = nll;
        Ece = ece;
    }

    /// <summary>
    ///     Gets the temperature.
    /// </summary>
    public double Temperature { get; }

    /// <summary>
    ///     Gets the mean negative log-likelihood on the open development split.
    /// </summary>
    public double Nll { get; }

    /// <summary>
    ///     Gets the expected calibration error in percent on the open development split.
    /// </summary>
    public double Ece { get; }
}

/// <summary>
///     Chooses the softmax temperature of a weights file by minimizing the negative log-likelihood on the open development split.
/// </summary>
/// <remarks>
///     <para>
///         The temperature only rescales the confidences; it never changes a chosen tag. The search therefore re-uses
///         one trained file, writes each candidate temperature into its header and scores the development split through
///         the real decoder. A coarse grid (a quarter of an octave) finds the basin and a fine grid (a 32nd of an
///         octave) around the best coarse point refines it. The result is rounded to three decimals so that the committed
///         configuration is readable; calibration is a one-time dev-only choice and not part of <c>train</c>, so the
///         training does not depend on <c>Math.Log</c> and <c>Math.Exp</c>.
///     </para>
///     <para>
///         Stateless and thread-safe.
///     </para>
/// </remarks>
internal static class Calibration
{
    /// <summary>
    ///     The smallest temperature of the coarse grid.
    /// </summary>
    private const double Lowest = 0.125;

    /// <summary>
    ///     Searches the temperature.
    /// </summary>
    /// <param name="weights">The trained weights file (its own temperature is ignored).</param>
    /// <param name="open">The open development sentences.</param>
    /// <param name="log">Called with every evaluated point, or <c>null</c>.</param>
    /// <returns>The point at the best temperature (rounded to three decimals); <see cref="CalibrationPoint.Temperature"/> is the value to commit.</returns>
    public static CalibrationPoint Fit(byte[] weights, IReadOnlyList<TrainingSentence> open, Action<CalibrationPoint>? log = null)
    {
        ArgumentNullException.ThrowIfNull(weights);
        ArgumentNullException.ThrowIfNull(open);

        // Coarse grid over 0.125 to 16 in quarter octaves
        CalibrationPoint? best = null;
        double bestExponent = 0;
        for (var step = 0; step <= 28; step++)
        {
            var exponent = step / 4.0;
            var point = Evaluate(weights, open, Lowest * Math.Pow(2, exponent));
            log?.Invoke(point);
            if (best == null || point.Nll < best.Nll)
            {
                best = point;
                bestExponent = exponent;
            }
        }

        // Fine grid around the best coarse point, a 32nd of an octave apart
        for (var step = -8; step <= 8; step++)
        {
            var exponent = bestExponent + (step / 32.0);
            if (step == 0 || exponent < 0)
            {
                continue;
            }

            var point = Evaluate(weights, open, Lowest * Math.Pow(2, exponent));
            log?.Invoke(point);
            if (point.Nll < best!.Nll)
            {
                best = point;
            }
        }

        // Report the rounded value, evaluated like every other candidate
        var rounded = Math.Round(best!.Temperature, 3);
        return Evaluate(weights, open, rounded);
    }

    /// <summary>
    ///     Evaluates one temperature on the open development split through the real decoder.
    /// </summary>
    /// <param name="weights">The weights file.</param>
    /// <param name="open">The open development sentences.</param>
    /// <param name="temperature">The temperature.</param>
    /// <returns>The log-likelihood and calibration error at that temperature.</returns>
    public static CalibrationPoint Evaluate(byte[] weights, IReadOnlyList<TrainingSentence> open, double temperature)
    {
        var tagger = DevEvaluator.CreateTagger(ModelBuilder.WithTemperature(weights, (float)temperature));
        var metrics = Metrics.Compute(DevEvaluator.EvaluateOpen(tagger, open));
        return new CalibrationPoint(temperature, metrics.Nll, metrics.Ece);
    }

    /// <summary>
    ///     Formats a point for the console.
    /// </summary>
    /// <param name="point">The point.</param>
    /// <returns>The text.</returns>
    public static string Format(CalibrationPoint point) =>
        string.Format(CultureInfo.InvariantCulture, "T={0,8:0.0000}  NLL={1:0.00000}  ECE={2:0.000}%", point.Temperature, point.Nll, point.Ece);
}
