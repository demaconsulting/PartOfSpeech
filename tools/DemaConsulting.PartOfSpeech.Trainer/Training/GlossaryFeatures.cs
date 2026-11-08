// cspell:ignore EBUL
namespace DemaConsulting.PartOfSpeech.Trainer;

/// <summary>
///     The two extra features of the glossary experiment (model M1): "the glossary says tag X" and "the glossary says tag X
///     after the previous tag Y".
/// </summary>
/// <remarks>
///     <para>
///         The features exist only for the dev-only glossary experiment (<c>glossary-experiment</c>); they are not part
///         of the library's feature set. They are appended after the 22 features of
///         <see cref="FeatureExtractor"/> and only when a token has a glossary hint, so a token without a hint has
///         exactly the features of the shipped model. Their buckets share the hashed bucket space of the regular
///         features, as a library implementation would.
///     </para>
///     <para>
///         Stateless and thread-safe.
///     </para>
/// </remarks>
internal static class GlossaryFeatures
{
    /// <summary>
    ///     The number of extra features of a hinted token.
    /// </summary>
    public const int Count = 2;

    /// <summary>
    ///     The size of a bucket buffer that holds the features of a token with every feature group and a glossary hint: the group
    ///     capacity of <see cref="FeatureExtractor.BucketCapacity"/> plus the <see cref="Count"/> buckets appended after the extraction.
    /// </summary>
    public const int BufferCapacity = FeatureExtractor.BucketCapacity + Count;

    /// <summary>
    ///     Appends the glossary features of a hinted token to its buckets.
    /// </summary>
    /// <param name="buckets">The bucket buffer; it must have room for <see cref="Count"/> more buckets.</param>
    /// <param name="count">The number of buckets already in the buffer.</param>
    /// <param name="hint">The tag ordinal that the glossary names.</param>
    /// <param name="previous1">The previous tag code (the tag history feature of the model is combined with the hint).</param>
    /// <param name="hashBits">The bucket space exponent.</param>
    /// <returns>The new number of buckets.</returns>
    public static int Append(int[] buckets, int count, int hint, int previous1, int hashBits)
    {
        buckets[count] = Bucket(1, hint, 0, hashBits);
        buckets[count + 1] = Bucket(2, hint, previous1, hashBits);
        return count + Count;
    }

    /// <summary>
    ///     Hashes one glossary feature into the bucket space.
    /// </summary>
    /// <param name="kind">The feature kind (1 or 2).</param>
    /// <param name="hint">The tag the glossary names.</param>
    /// <param name="previous">The previous tag code, or 0 for the kind without history.</param>
    /// <param name="hashBits">The bucket space exponent.</param>
    /// <returns>The bucket.</returns>
    private static int Bucket(ulong kind, int hint, int previous, int hashBits)
    {
        unchecked
        {
            var x = (kind + 0x6A09E667UL) * 0x9E3779B97F4A7C15UL;
            x ^= ((ulong)(hint + 1) + 0x1000UL) * 0xBF58476D1CE4E5B9UL;
            x ^= x >> 29;
            x *= 0x94D049BB133111EBUL;
            x ^= ((ulong)(previous + 1) + 0x2000UL) * 0xD6E8FEB86659FD93UL;
            x ^= x >> 32;
            x *= 0x9E3779B97F4A7C15UL;
            return (int)(x >> (64 - hashBits));
        }
    }
}

/// <summary>
///     The simulated glossary process that trains model M1: a share of the lexicon-ambiguous tokens gets a hint, a
///     share of the hints is wrong.
/// </summary>
/// <remarks>
///     <para>
///         A token is eligible when it has a gold tag and the lexicon with the shape rules allows at least two tags, one
///         of them the gold tag. The allowed tags are computed for the token as written, exactly as the runtime decoder
///         does (<see cref="TagSelection.AllowedMask"/>), so a capitalized token also allows PROPN and X. An eligible token gets a hint with probability <see cref="HintPercent"/>; the hint is the
///         gold tag, or with probability <see cref="WrongPercent"/> another allowed tag. The draws come from the
///         caller's <see cref="DeterministicRandom"/>, so a run is reproducible.
///     </para>
///     <para>
///         Not thread-safe (it caches the eligible masks of the sentences it has seen).
///     </para>
/// </remarks>
internal sealed class GlossaryTraining
{
    /// <summary>
    ///     The lexicon that defines which tags a type allows.
    /// </summary>
    private readonly Lexicon _lexicon;

    /// <summary>
    ///     The allowed-tag mask of every token of the sentences seen so far (0 for a token that cannot get a hint).
    /// </summary>
    private readonly Dictionary<TrainingSentence, int[]> _eligible = new();

    /// <summary>
    ///     The scratch workspace that prepares a sentence for the same candidate computation as the decoder.
    /// </summary>
    private readonly DecoderWorkspace _workspace = new();

    /// <summary>
    ///     Initializes a new instance of the <see cref="GlossaryTraining"/> class.
    /// </summary>
    /// <param name="lexicon">The lexicon.</param>
    /// <param name="hintPercent">The share of eligible tokens that get a hint, in percent.</param>
    /// <param name="wrongPercent">The share of hints that are wrong, in percent.</param>
    /// <param name="seed">The seed of the per-epoch random generators.</param>
    /// <exception cref="ArgumentNullException"><paramref name="lexicon"/> is <c>null</c>.</exception>
    public GlossaryTraining(Lexicon lexicon, int hintPercent, int wrongPercent, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(lexicon);
        _lexicon = lexicon;
        HintPercent = hintPercent;
        WrongPercent = wrongPercent;
        Seed = seed;
    }

    /// <summary>
    ///     Gets the share of eligible tokens that get a hint, in percent.
    /// </summary>
    public int HintPercent { get; }

    /// <summary>
    ///     Gets the share of hints that are wrong, in percent.
    /// </summary>
    public int WrongPercent { get; }

    /// <summary>
    ///     Gets the seed of the per-epoch random generators.
    /// </summary>
    public ulong Seed { get; }

    /// <summary>
    ///     Draws the hints of one sentence for one epoch.
    /// </summary>
    /// <param name="sentence">The sentence.</param>
    /// <param name="random">The generator of the epoch.</param>
    /// <returns>The hint tag ordinal of every token, or -1 for a token without a hint.</returns>
    public int[] Draw(TrainingSentence sentence, DeterministicRandom random)
    {
        if (!_eligible.TryGetValue(sentence, out var masks))
        {
            // The candidates are those of the runtime decoder for the token as written: capitalization adds PROPN and X
            FeatureExtractor.Prepare(_workspace, sentence.Tokens, PerceptronModel.MinHashBits);
            masks = new int[sentence.Tokens.Length];
            for (var i = 0; i < masks.Length; i++)
            {
                var gold = sentence.Gold[i];
                var allowed = TagSelection.AllowedMask(_workspace, i, _lexicon);
                masks[i] = gold >= 0 && BitCount(allowed) >= 2 && (allowed & (1 << gold)) != 0 ? allowed : 0;
            }

            _eligible[sentence] = masks;
        }

        var hints = new int[masks.Length];
        for (var i = 0; i < hints.Length; i++)
        {
            hints[i] = Hint(sentence, i, masks[i], random);
        }

        return hints;
    }

    /// <summary>
    ///     Counts the tags of a mask.
    /// </summary>
    /// <param name="mask">The mask.</param>
    /// <returns>The number of set bits.</returns>
    private static int BitCount(int mask)
    {
        var count = 0;
        for (; mask != 0; mask &= mask - 1)
        {
            count++;
        }

        return count;
    }

    /// <summary>
    ///     Draws the hint of one token.
    /// </summary>
    /// <param name="sentence">The sentence.</param>
    /// <param name="index">The token.</param>
    /// <param name="mask">The allowed tags of the token, or 0 when the token cannot get a hint.</param>
    /// <param name="random">The generator.</param>
    /// <returns>The hint tag ordinal, or -1 for no hint.</returns>
    private int Hint(TrainingSentence sentence, int index, int mask, DeterministicRandom random)
    {
        if (mask == 0 || random.NextInt(100) >= HintPercent)
        {
            return -1;
        }

        var hint = (int)sentence.Gold[index];
        return random.NextInt(100) < WrongPercent ? PickOther(mask, hint, random) : hint;
    }

    /// <summary>
    ///     Picks a random tag of a mask other than the given one.
    /// </summary>
    /// <param name="mask">The allowed tags; at least two.</param>
    /// <param name="exclude">The tag to avoid.</param>
    /// <param name="random">The generator.</param>
    /// <returns>The picked tag ordinal.</returns>
    private static int PickOther(int mask, int exclude, DeterministicRandom random)
    {
        var others = new List<int>();
        for (var tag = 0; tag < UposExtensions.Count; tag++)
        {
            if ((mask & (1 << tag)) != 0 && tag != exclude)
            {
                others.Add(tag);
            }
        }

        return others[random.NextInt(others.Count)];
    }
}
