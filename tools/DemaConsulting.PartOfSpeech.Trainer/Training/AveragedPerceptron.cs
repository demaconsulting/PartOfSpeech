namespace DemaConsulting.PartOfSpeech.Trainer;

/// <summary>
///     The weights of an averaged perceptron in exact integer form: the current weights <c>W</c>, the time-weighted
///     update sums <c>U</c> and the step counter <c>T</c>, with the averaging and quantization that produce the file rows.
/// </summary>
/// <remarks>
///     <para>
///         The algorithm is the spike's: for every token step <c>t</c> (counted for masked tokens too, because they are
///         decoded at run time) a mistake adds +1 to the gold tag and -1 to the guessed tag of every feature, and
///         the update is also recorded as <c>t * delta</c> in <c>U</c>. The averaged weight is <c>W - U / T</c>.
///     </para>
///     <para>
///         Everything is integer arithmetic, so the result does not depend on the platform: <c>W</c> is <c>int</c> and
///         <c>U</c> is <c>long</c> (a 32-bit sum could overflow with millions of steps and hundreds of thousands of
///         updates), and the quantized weight is <c>RoundHalfAwayFromZero(S * (W * T - U) / T)</c> evaluated exactly
///         in <c>long</c> with <c>S</c> a power of two, clamped to 16-bit range without -32768. No floating-point
///         value enters the weights, which is what makes the written file reproducible byte for byte. The updates, the
///         sums and the quantization use checked arithmetic: a run whose sums do not fit 64 bits fails with a clear
///         exception instead of writing wrong weights.
///     </para>
///     <para>
///         The class is not thread-safe; one trainer uses it from one thread. The tables are dense
///         (<c>2^hashBits * 17</c> cells each), which is 430 MB for 21 bits.
///     </para>
/// </remarks>
internal sealed class AveragedPerceptron
{
    /// <summary>
    ///     The largest magnitude of a quantized weight (-32768 cannot be stored).
    /// </summary>
    public const int MaxQuantized = 32767;

    /// <summary>
    ///     The number of tags, which is the row length.
    /// </summary>
    private const int Tags = UposExtensions.Count;

    /// <summary>
    ///     The message of the exception that reports an exhausted integer range.
    /// </summary>
    private const string OverflowMessage =
        "The exact integer arithmetic of the averaged perceptron exceeded its range: epochs * repeats * tokens * scale is too large. Reduce the epochs, the source weights, the corpus size or the quantization scale.";

    /// <summary>
    ///     The current weights of every bucket and tag.
    /// </summary>
    private readonly int[] _weights;

    /// <summary>
    ///     The sum of <c>step * delta</c> over all updates of every bucket and tag.
    /// </summary>
    private readonly long[] _totals;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AveragedPerceptron"/> class with all weights zero.
    /// </summary>
    /// <param name="hashBits">The bucket space exponent (18 to 24).</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="hashBits"/> is outside 18 to 24.</exception>
    public AveragedPerceptron(int hashBits)
    {
        if (hashBits < PerceptronModel.MinHashBits || hashBits > PerceptronModel.MaxHashBits)
        {
            throw new ArgumentOutOfRangeException(nameof(hashBits), hashBits, "hashBits must be between 18 and 24.");
        }

        HashBits = hashBits;
        _weights = new int[(1 << hashBits) * Tags];
        _totals = new long[(1 << hashBits) * Tags];
    }

    /// <summary>
    ///     Gets the bucket space exponent.
    /// </summary>
    public int HashBits { get; }

    /// <summary>
    ///     Gets the number of token steps taken so far (the divisor of the averaging).
    /// </summary>
    public long Steps { get; private set; }

    /// <summary>
    ///     Gets the number of updates (mistakes with a training signal) so far.
    /// </summary>
    public long Mistakes { get; private set; }

    /// <summary>
    ///     Adds the weights of one bucket to a score vector.
    /// </summary>
    /// <param name="bucket">The bucket.</param>
    /// <param name="scores">The score vector with one element per tag.</param>
    public void AddScores(int bucket, int[] scores)
    {
        var offset = bucket * Tags;
        for (var tag = 0; tag < Tags; tag++)
        {
            scores[tag] += _weights[offset + tag];
        }
    }

    /// <summary>
    ///     Starts the next token step.
    /// </summary>
    /// <remarks>
    ///     Every token of every sentence is one step, whether or not it carries a training signal.
    /// </remarks>
    public void BeginStep()
    {
        Steps++;
    }

    /// <summary>
    ///     Applies the update of the current step: +1 for the gold tag and -1 for the guess in every given bucket.
    /// </summary>
    /// <param name="buckets">The buckets of the token's features.</param>
    /// <param name="count">The number of buckets to use.</param>
    /// <param name="gold">The gold tag ordinal.</param>
    /// <param name="guess">The guessed tag ordinal; different from <paramref name="gold"/>.</param>
    /// <exception cref="InvalidOperationException">A weight or an update sum exceeded its integer range.</exception>
    public void Update(int[] buckets, int count, int gold, int guess)
    {
        // Checked arithmetic keeps an overflow from silently corrupting the weights; the cost is one flag test per add
        try
        {
            var step = Steps;
            for (var k = 0; k < count; k++)
            {
                var offset = buckets[k] * Tags;
                checked
                {
                    _weights[offset + gold]++;
                    _totals[offset + gold] += step;
                    _weights[offset + guess]--;
                    _totals[offset + guess] -= step;
                }
            }
        }
        catch (OverflowException ex)
        {
            throw new InvalidOperationException(OverflowMessage, ex);
        }

        Mistakes++;
    }

    /// <summary>
    ///     Averages the weights, quantizes them and returns the rows of the weights file.
    /// </summary>
    /// <param name="quantScale">The factor <c>S</c> from averaged weight to integer; a power of two.</param>
    /// <param name="pruneThreshold">The largest magnitude that is dropped (0 drops only exact zeros).</param>
    /// <returns>The rows in ascending bucket order; rows without a weight are omitted.</returns>
    /// <exception cref="InvalidOperationException">No step was taken, so there is nothing to average.</exception>
    public List<KeyValuePair<int, short[]>> Quantize(int quantScale, int pruneThreshold)
    {
        if (Steps == 0)
        {
            throw new InvalidOperationException("No training step was taken, so there are no weights to average.");
        }

        var rows = new List<KeyValuePair<int, short[]>>();
        var bucketCount = 1 << HashBits;
        for (var bucket = 0; bucket < bucketCount; bucket++)
        {
            short[]? row = null;
            var offset = bucket * Tags;
            for (var tag = 0; tag < Tags; tag++)
            {
                if (_weights[offset + tag] == 0 && _totals[offset + tag] == 0)
                {
                    continue;
                }

                var q = Quantize(_weights[offset + tag], _totals[offset + tag], Steps, quantScale);
                if (q == 0 || Math.Abs(q) <= pruneThreshold)
                {
                    continue;
                }

                row ??= new short[Tags];
                row[tag] = (short)q;
            }

            if (row != null)
            {
                rows.Add(new KeyValuePair<int, short[]>(bucket, row));
            }
        }

        return rows;
    }

    /// <summary>
    ///     Quantizes one averaged weight exactly: <c>RoundHalfAwayFromZero(S * (W * T - U) / T)</c>, clamped to 16 bits without -32768.
    /// </summary>
    /// <param name="weight">The current weight <c>W</c>.</param>
    /// <param name="total">The time-weighted update sum <c>U</c>.</param>
    /// <param name="steps">The number of steps <c>T</c>; positive.</param>
    /// <param name="quantScale">The factor <c>S</c>.</param>
    /// <returns>The quantized weight from -32767 to 32767.</returns>
    /// <exception cref="InvalidOperationException">The exact integer arithmetic does not fit 64 bits (too many steps or too large a scale).</exception>
    public static int Quantize(int weight, long total, long steps, int quantScale)
    {
        // numerator / steps is S times the averaged weight; the products are checked, so that a run that is too long
        // for the 64-bit sums fails with a clear message instead of writing wrong weights
        try
        {
            checked
            {
                var numerator = quantScale * (((long)weight * steps) - total);
                var magnitude = Math.Abs(numerator);

                // Round half away from zero: floor(magnitude / steps + 1/2)
                var rounded = ((2 * magnitude) + steps) / (2 * steps);
                var clamped = (int)Math.Min(rounded, MaxQuantized);
                return numerator < 0 ? -clamped : clamped;
            }
        }
        catch (OverflowException ex)
        {
            throw new InvalidOperationException(OverflowMessage, ex);
        }
    }
}
