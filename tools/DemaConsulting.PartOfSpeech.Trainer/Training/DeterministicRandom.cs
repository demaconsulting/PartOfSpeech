// cspell:ignore EBUL
namespace DemaConsulting.PartOfSpeech.Trainer;

/// <summary>
///     A small, platform-independent pseudo-random generator (SplitMix64) with an unbiased bounded draw and a
///     Fisher-Yates shuffle.
/// </summary>
/// <remarks>
///     <para>
///         The trainer must produce a byte-identical weights file on every operating system and runtime, and the
///         shuffle order decides the result of a perceptron. <see cref="Random"/> promises no sequence across .NET
///         versions, so the trainer owns its generator: the sequence below is fixed by the algorithm (Steele, Lea and
///         Flood, 2014, with the constants of the reference implementation) and by nothing else.
///     </para>
///     <para>
///         Not thread-safe; use one instance per thread. The state is a single 64-bit value, so a run is reproduced
///         by its seed.
///     </para>
/// </remarks>
internal sealed class DeterministicRandom
{
    /// <summary>
    ///     The increment of the SplitMix64 state (the 64-bit golden ratio).
    /// </summary>
    private const ulong Increment = 0x9E3779B97F4A7C15UL;

    /// <summary>
    ///     The state of the generator.
    /// </summary>
    private ulong _state;

    /// <summary>
    ///     Initializes a new instance of the <see cref="DeterministicRandom"/> class.
    /// </summary>
    /// <param name="seed">The seed; the same seed always gives the same sequence.</param>
    public DeterministicRandom(ulong seed)
    {
        _state = seed;
    }

    /// <summary>
    ///     Draws the next 64 random bits.
    /// </summary>
    /// <returns>A value that is uniformly distributed over the 64-bit range.</returns>
    public ulong NextUInt64()
    {
        // The state advances by a constant; the output is a bijective mix of the state
        unchecked
        {
            _state += Increment;
            var z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    /// <summary>
    ///     Draws a uniformly distributed integer below a bound without modulo bias.
    /// </summary>
    /// <param name="bound">The exclusive upper bound; must be greater than zero.</param>
    /// <returns>A value from 0 to <paramref name="bound"/> - 1.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bound"/> is not positive.</exception>
    public int NextInt(int bound)
    {
        if (bound <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bound), bound, "The bound must be greater than zero.");
        }

        // Rejection sampling: discard the draws below the threshold so that every residue is equally likely
        var range = (ulong)bound;
        var threshold = unchecked(0UL - range) % range;
        ulong draw;
        do
        {
            draw = NextUInt64();
        }
        while (draw < threshold);

        return (int)(draw % range);
    }

    /// <summary>
    ///     Shuffles an array in place with the Fisher-Yates algorithm (from the last position down).
    /// </summary>
    /// <param name="values">The array to shuffle.</param>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> is <c>null</c>.</exception>
    public void Shuffle(int[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        for (var i = values.Length - 1; i > 0; i--)
        {
            var j = NextInt(i + 1);
            (values[i], values[j]) = (values[j], values[i]);
        }
    }
}
