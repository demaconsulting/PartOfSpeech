using System.Diagnostics;

namespace DemaConsulting.PartOfSpeech.Trainer;

/// <summary>
///     The numbers of one finished epoch, for progress output and for checkpoint evaluation.
/// </summary>
internal sealed class EpochReport
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="EpochReport"/> class.
    /// </summary>
    /// <param name="epoch">The number of the epoch, starting at 1.</param>
    /// <param name="steps">The number of token steps of the epoch.</param>
    /// <param name="mistakes">The number of updates of the epoch.</param>
    /// <param name="seconds">The wall-clock time of the epoch.</param>
    /// <param name="perceptron">The perceptron after the epoch; read it only inside the callback.</param>
    public EpochReport(int epoch, long steps, long mistakes, double seconds, AveragedPerceptron perceptron)
    {
        Epoch = epoch;
        Steps = steps;
        Mistakes = mistakes;
        Seconds = seconds;
        Perceptron = perceptron;
    }

    /// <summary>
    ///     Gets the number of the epoch, starting at 1.
    /// </summary>
    public int Epoch { get; }

    /// <summary>
    ///     Gets the number of token steps of the epoch.
    /// </summary>
    public long Steps { get; }

    /// <summary>
    ///     Gets the number of updates of the epoch.
    /// </summary>
    public long Mistakes { get; }

    /// <summary>
    ///     Gets the wall-clock time of the epoch in seconds.
    /// </summary>
    public double Seconds { get; }

    /// <summary>
    ///     Gets the perceptron after the epoch; the averaged weights of that moment can be quantized from it.
    /// </summary>
    public AveragedPerceptron Perceptron { get; }
}

/// <summary>
///     Trains an <see cref="AveragedPerceptron"/> over training sentences with the feature extractor and the lexicon
///     restriction of the library.
/// </summary>
/// <remarks>
///     <para>
///         Per epoch the sentence order is shuffled with <see cref="DeterministicRandom"/> (continuing from the order
///         of the previous epoch, as the spike did). Each sentence is decoded left to right: the features of a token
///         use the two previous tags (the guess, or the gold tag in <see cref="HistoryMode.Gold"/>), the integer scores
///         of the 17 tags are summed, the guess is made, and on a mistake against a gold tag the weights are updated.
///         A token without a gold tag is only decoded: it is a step of the averaging and it enters the history.
///     </para>
///     <para>
///         <b>Predicted history.</b> In the default mode the history is the tag that the current weights chose, never
///         the gold tag. That is the history that the runtime decoder creates, so the model is trained on the
///         conditions it is used under (no exposure bias), as in the spike and in the test trainer of the library.
///     </para>
///     <para>
///         The class is stateless, and a training run is deterministic: the corpora, the configuration, the
///         lexicon and the library code determine every weight.
///     </para>
/// </remarks>
internal static class PerceptronTrainer
{
    /// <summary>
    ///     Trains the weights.
    /// </summary>
    /// <param name="config">The validated configuration.</param>
    /// <param name="lexicon">The lexicon that defines the allowed tags of every token.</param>
    /// <param name="sentences">The training sentences of one epoch; a repeated reference is a sentence that is seen several times per epoch.</param>
    /// <param name="onEpoch">Called after every epoch, or <c>null</c>.</param>
    /// <param name="glossary">The simulated glossary process of the dev-only glossary experiment (model M1), or <c>null</c> for the shipped feature set.</param>
    /// <returns>The trained perceptron.</returns>
    /// <exception cref="ArgumentNullException">An argument other than <paramref name="onEpoch"/> and <paramref name="glossary"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidDataException">The configuration is invalid.</exception>
    public static AveragedPerceptron Train(TrainingConfig config, Lexicon lexicon, IReadOnlyList<TrainingSentence> sentences, Action<EpochReport>? onEpoch = null, GlossaryTraining? glossary = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(lexicon);
        ArgumentNullException.ThrowIfNull(sentences);
        config.Validate();

        // Allowed tags of every token (once per distinct sentence), and the occurrence counts that the minimum count needs
        var workspace = new DecoderWorkspace();
        var allowed = ComputeAllowed(sentences, lexicon, workspace, config.HashBits);
        var counts = config.MinCount > 1 ? CountFeatures(sentences, workspace, config.HashBits) : null;

        var perceptron = new AveragedPerceptron(config.HashBits);
        var random = new DeterministicRandom((ulong)config.Seed);
        var order = new int[sentences.Count];
        for (var i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        var buckets = new int[FeatureExtractor.MaxFeatures + GlossaryFeatures.Count];
        var scores = new int[UposExtensions.Count];
        for (var epoch = 1; epoch <= config.Epochs; epoch++)
        {
            var clock = Stopwatch.StartNew();
            var stepsBefore = perceptron.Steps;
            var mistakesBefore = perceptron.Mistakes;
            random.Shuffle(order);
            var hintRandom = glossary == null ? null : new DeterministicRandom(glossary.Seed + (ulong)epoch);
            foreach (var index in order)
            {
                var sentence = sentences[index];
                var hints = glossary == null ? null : glossary.Draw(sentence, hintRandom!);
                TrainSentence(config, perceptron, workspace, sentence, allowed[sentence], counts, hints, buckets, scores);
            }

            onEpoch?.Invoke(new EpochReport(epoch, perceptron.Steps - stepsBefore, perceptron.Mistakes - mistakesBefore, clock.Elapsed.TotalSeconds, perceptron));
        }

        return perceptron;
    }

    /// <summary>
    ///     Trains on one sentence: decodes it left to right and updates on every mistake against a gold tag.
    /// </summary>
    /// <param name="config">The configuration.</param>
    /// <param name="perceptron">The weights.</param>
    /// <param name="workspace">The scratch workspace.</param>
    /// <param name="sentence">The sentence.</param>
    /// <param name="allowed">The allowed-tag mask of every token of the sentence.</param>
    /// <param name="counts">The feature occurrence counts when a minimum count applies, otherwise <c>null</c>.</param>
    /// <param name="hints">The glossary hint of every token (-1 for none) in the glossary experiment, otherwise <c>null</c>.</param>
    /// <param name="buckets">The scratch buffer for the buckets of a token.</param>
    /// <param name="scores">The scratch buffer for the scores of a token.</param>
    private static void TrainSentence(
        TrainingConfig config,
        AveragedPerceptron perceptron,
        DecoderWorkspace workspace,
        TrainingSentence sentence,
        int[] allowed,
        int[]? counts,
        int[]? hints,
        int[] buckets,
        int[] scores)
    {
        FeatureExtractor.Prepare(workspace, sentence.Tokens, config.HashBits);
        var previous1 = FeatureExtractor.StartTag1;
        var previous2 = FeatureExtractor.StartTag2;
        for (var i = 0; i < sentence.Tokens.Length; i++)
        {
            // Sum the weights of the features of the token for every tag; rare static features are not used
            var count = FeatureExtractor.Extract(workspace, i, previous1, previous2, config.HashBits, buckets);
            if (counts != null)
            {
                count = DropRareFeatures(buckets, count, workspace.StaticCount[i], counts, config.MinCount);
            }

            if (hints != null && hints[i] >= 0)
            {
                count = GlossaryFeatures.Append(buckets, count, hints[i], previous1, config.HashBits);
            }

            Array.Clear(scores, 0, scores.Length);
            for (var k = 0; k < count; k++)
            {
                perceptron.AddScores(buckets[k], scores);
            }

            perceptron.BeginStep();

            // The guess for the update and the guess that becomes history differ only in the restriction modes
            int gold = sentence.Gold[i];
            var updateGuess = TagSelection.ArgMax(scores, TagSelection.AllTags);
            var historyGuess = updateGuess;
            if (config.Restriction == RestrictionMode.History)
            {
                historyGuess = TagSelection.ArgMax(scores, allowed[i]);
            }
            else if (config.Restriction == RestrictionMode.Full)
            {
                // The gold tag counts as allowed when the lexicon misses it, so that the update stays defined
                var mask = gold >= 0 ? allowed[i] | (1 << gold) : allowed[i];
                updateGuess = TagSelection.ArgMax(scores, mask);
                historyGuess = updateGuess;
            }

            if (gold >= 0 && updateGuess != gold)
            {
                perceptron.Update(buckets, count, gold, updateGuess);
            }

            previous2 = previous1;
            previous1 = config.History == HistoryMode.Gold && gold >= 0 ? gold : historyGuess;
        }
    }

    /// <summary>
    ///     Removes the tag-independent features that occur less than the minimum count, keeping the order of the rest.
    /// </summary>
    /// <param name="buckets">The buckets of the token; the static ones come first.</param>
    /// <param name="count">The number of buckets.</param>
    /// <param name="staticCount">The number of tag-independent buckets at the front.</param>
    /// <param name="counts">The occurrence count of every bucket.</param>
    /// <param name="minCount">The minimum count.</param>
    /// <returns>The number of buckets that remain; the history features always remain.</returns>
    private static int DropRareFeatures(int[] buckets, int count, int staticCount, int[] counts, int minCount)
    {
        var kept = 0;
        for (var k = 0; k < count; k++)
        {
            if (k >= staticCount || counts[buckets[k]] >= minCount)
            {
                buckets[kept++] = buckets[k];
            }
        }

        return kept;
    }

    /// <summary>
    ///     Computes the allowed-tag mask of every token of every distinct sentence.
    /// </summary>
    /// <param name="sentences">The sentences (a sentence may appear several times).</param>
    /// <param name="lexicon">The lexicon.</param>
    /// <param name="workspace">The scratch workspace.</param>
    /// <param name="hashBits">The bucket space exponent (the workspace needs it to prepare a sentence).</param>
    /// <returns>The masks by sentence.</returns>
    private static Dictionary<TrainingSentence, int[]> ComputeAllowed(IReadOnlyList<TrainingSentence> sentences, Lexicon lexicon, DecoderWorkspace workspace, int hashBits)
    {
        var result = new Dictionary<TrainingSentence, int[]>();
        foreach (var sentence in sentences)
        {
            if (result.ContainsKey(sentence))
            {
                continue;
            }

            FeatureExtractor.Prepare(workspace, sentence.Tokens, hashBits);
            var masks = new int[sentence.Tokens.Length];
            for (var i = 0; i < masks.Length; i++)
            {
                masks[i] = TagSelection.AllowedMask(workspace, i, lexicon);
            }

            result[sentence] = masks;
        }

        return result;
    }

    /// <summary>
    ///     Counts how often every bucket occurs as a tag-independent feature of a token with a training signal.
    /// </summary>
    /// <remarks>
    ///     The counting is over buckets, not feature strings, so colliding features share a count; repeated sentences
    ///     count every time they are seen in an epoch.
    /// </remarks>
    /// <param name="sentences">The sentences of one epoch.</param>
    /// <param name="workspace">The scratch workspace.</param>
    /// <param name="hashBits">The bucket space exponent.</param>
    /// <returns>The count of every bucket.</returns>
    private static int[] CountFeatures(IReadOnlyList<TrainingSentence> sentences, DecoderWorkspace workspace, int hashBits)
    {
        var counts = new int[1 << hashBits];
        foreach (var sentence in sentences)
        {
            FeatureExtractor.Prepare(workspace, sentence.Tokens, hashBits);
            for (var i = 0; i < sentence.Tokens.Length; i++)
            {
                if (sentence.Gold[i] < 0)
                {
                    continue;
                }

                var offset = i * FeatureExtractor.StaticFeatureCount;
                for (var k = 0; k < workspace.StaticCount[i]; k++)
                {
                    counts[workspace.StaticBuckets[offset + k]]++;
                }
            }
        }

        return counts;
    }
}
