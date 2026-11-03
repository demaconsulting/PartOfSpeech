namespace DemaConsulting.PartOfSpeech.Tests;

/// <summary>
///     A deterministic averaged-perceptron trainer over a few dozen fixture sentences.
/// </summary>
/// <remarks>
///     <para>
///         The trainer produces the "tiny trained weights file" that the end-to-end tests use. It deliberately uses only
///         <see cref="FeatureExtractor"/> and <see cref="PerceptronModelWriter"/>, which is also the proof that the
///         trainer of a later phase can reuse the extractor and the writer unchanged.
///     </para>
///     <para>
///         The algorithm is the one of the spike: left to right, unrestricted greedy decoding with the previous
///         predicted tags as history, +1 for the gold tag and -1 for the guess on a mistake, weights averaged over all
///         steps. The shuffle uses a fixed seeded generator so every run gives the same file.
///     </para>
/// </remarks>
internal static class TinyTrainer
{
    /// <summary>
    ///     The fixture sentences, one per row: tokens with their tags written as <c>word|TAG</c>.
    /// </summary>
    private static readonly string[] Fixtures =
    {
        "The|DET cat|NOUN sat|VERB on|ADP the|DET mat|NOUN .|PUNCT",
        "A|DET dog|NOUN ran|VERB in|ADP the|DET park|NOUN .|PUNCT",
        "She|PRON reads|VERB a|DET book|NOUN .|PUNCT",
        "He|PRON writes|VERB a|DET letter|NOUN .|PUNCT",
        "They|PRON play|VERB games|NOUN in|ADP the|DET garden|NOUN .|PUNCT",
        "The|DET big|ADJ dog|NOUN chased|VERB a|DET small|ADJ cat|NOUN .|PUNCT",
        "I|PRON like|VERB green|ADJ apples|NOUN .|PUNCT",
        "We|PRON saw|VERB the|DET red|ADJ car|NOUN .|PUNCT",
        "Birds|NOUN fly|VERB over|ADP the|DET river|NOUN .|PUNCT",
        "The|DET children|NOUN eat|VERB fresh|ADJ bread|NOUN .|PUNCT",
        "My|DET sister|NOUN has|VERB a|DET new|ADJ bike|NOUN .|PUNCT",
        "The|DET old|ADJ man|NOUN walks|VERB to|ADP the|DET store|NOUN .|PUNCT",
        "You|PRON can|AUX see|VERB the|DET sea|NOUN .|PUNCT",
        "She|PRON will|AUX run|VERB to|ADP school|NOUN .|PUNCT",
        "They|PRON run|VERB a|DET small|ADJ shop|NOUN .|PUNCT",
        "We|PRON went|VERB for|ADP a|DET run|NOUN .|PUNCT",
        "The|DET road|NOUN was|VERB long|ADJ .|PUNCT",
        "A|DET quick|ADJ fox|NOUN jumps|VERB over|ADP the|DET lazy|ADJ dog|NOUN .|PUNCT",
        "The|DET teacher|NOUN reads|VERB the|DET long|ADJ story|NOUN .|PUNCT",
        "He|PRON did|AUX not|PART see|VERB the|DET dog|NOUN .|PUNCT",
        "I|PRON do|AUX not|PART like|VERB cold|ADJ milk|NOUN .|PUNCT",
        "Tom|PROPN is|VERB a|DET doctor|NOUN .|PUNCT",
        "Mary|PROPN and|CCONJ Tom|PROPN play|VERB in|ADP the|DET snow|NOUN .|PUNCT",
        "The|DET boy|NOUN and|CCONJ the|DET girl|NOUN sing|VERB .|PUNCT",
        "She|PRON sings|VERB and|CCONJ he|PRON dances|VERB .|PUNCT",
        "Please|INTJ open|VERB the|DET door|NOUN .|PUNCT",
        "Who|PRON is|VERB that|DET man|NOUN ?|PUNCT",
        "The|DET cat|NOUN is|VERB very|ADV small|ADJ .|PUNCT",
        "He|PRON runs|VERB very|ADV fast|ADV .|PUNCT",
        "It|PRON rains|VERB in|ADP spring|NOUN .|PUNCT",
        "We|PRON have|VERB two|NUM cats|NOUN .|PUNCT",
        "Three|NUM birds|NOUN sit|VERB on|ADP a|DET tree|NOUN .|PUNCT",
        "If|SCONJ you|PRON run|VERB ,|PUNCT you|PRON win|VERB .|PUNCT",
        "I|PRON know|VERB that|SCONJ she|PRON sings|VERB .|PUNCT",
        "The|DET sun|NOUN is|VERB bright|ADJ today|ADV .|PUNCT",
        "Wow|INTJ ,|PUNCT that|PRON is|VERB a|DET big|ADJ house|NOUN !|PUNCT",
        "The|DET girl|NOUN in|ADP the|DET red|ADJ hat|NOUN smiles|VERB .|PUNCT",
        "Bob|PROPN eats|VERB an|DET apple|NOUN every|DET day|NOUN .|PUNCT",
        "They|PRON live|VERB near|ADP the|DET old|ADJ bridge|NOUN .|PUNCT",
        "She|PRON asked|VERB him|PRON to|PART leave|VERB .|PUNCT",
    };

    /// <summary>
    ///     Gets the fixture sentences with their gold tags.
    /// </summary>
    /// <returns>The sentences in file order.</returns>
    public static IReadOnlyList<(string[] Tokens, Upos[] Tags)> Sentences()
    {
        var result = new List<(string[], Upos[])>();
        foreach (var line in Fixtures)
        {
            var parts = line.Split(' ').Select(p => p.Split('|')).ToList();
            var tags = parts.Select(p =>
            {
                Assert.True(UposExtensions.TryParse(p[1], out var tag), "unknown tag in fixture " + p[1]);
                return tag;
            }).ToArray();
            result.Add((parts.Select(p => p[0]).ToArray(), tags));
        }

        return result;
    }

    /// <summary>
    ///     Gets the lexicon that lists every fixture word with the tags it has in the fixtures.
    /// </summary>
    /// <returns>The lexicon file.</returns>
    public static byte[] FixtureLexicon()
    {
        var tags = new Dictionary<string, UposSet>(StringComparer.Ordinal);
        foreach (var (tokens, gold) in Sentences())
        {
            for (var i = 0; i < tokens.Length; i++)
            {
                var key = tokens[i].ToLowerInvariant();
                tags[key] = (tags.TryGetValue(key, out var set) ? set : UposSet.Empty).Add(gold[i]);
            }
        }

        return TinyModels.CreateLexicon(tags);
    }

    /// <summary>
    ///     Trains the weights.
    /// </summary>
    /// <param name="epochs">The number of passes over the fixtures.</param>
    /// <param name="compress">Whether to Deflate the weights file.</param>
    /// <returns>The weights file.</returns>
    public static byte[] Train(int epochs = 15, bool compress = false)
    {
        var sentences = Sentences();
        var weights = new Dictionary<int, double[]>();
        var totals = new Dictionary<int, double[]>();
        var workspace = new DecoderWorkspace();
        var buckets = new int[FeatureExtractor.MaxFeatures];
        var scores = new double[UposExtensions.Count];
        var order = Enumerable.Range(0, sentences.Count).ToArray();
        long seed = 12345;
        long step = 0;
        for (var epoch = 0; epoch < epochs; epoch++)
        {
            // Deterministic Fisher-Yates shuffle with a seeded generator
            for (var i = order.Length - 1; i > 0; i--)
            {
                seed = ((seed * 1103515245) + 12345) & 0x7FFFFFFF;
                var j = (int)(seed % (i + 1));
                (order[i], order[j]) = (order[j], order[i]);
            }

            foreach (var index in order)
            {
                var (tokens, gold) = sentences[index];
                FeatureExtractor.Prepare(workspace, tokens, TinyModels.HashBits);
                var previous1 = FeatureExtractor.StartTag1;
                var previous2 = FeatureExtractor.StartTag2;
                for (var i = 0; i < tokens.Length; i++)
                {
                    var count = FeatureExtractor.Extract(workspace, i, previous1, previous2, TinyModels.HashBits, buckets);
                    Array.Clear(scores, 0, scores.Length);
                    for (var k = 0; k < count; k++)
                    {
                        if (weights.TryGetValue(buckets[k], out var row))
                        {
                            for (var t = 0; t < scores.Length; t++)
                            {
                                scores[t] += row[t];
                            }
                        }
                    }

                    // Greedy guess, the lowest tag wins a tie
                    var guess = 0;
                    for (var t = 1; t < scores.Length; t++)
                    {
                        if (scores[t] > scores[guess])
                        {
                            guess = t;
                        }
                    }

                    step++;
                    var truth = (int)gold[i];
                    if (guess != truth)
                    {
                        for (var k = 0; k < count; k++)
                        {
                            Update(weights, totals, buckets[k], truth, 1, step);
                            Update(weights, totals, buckets[k], guess, -1, step);
                        }
                    }

                    previous2 = previous1;
                    previous1 = guess;
                }
            }
        }

        return Quantize(weights, totals, step, compress);
    }

    /// <summary>
    ///     Adds a perceptron update to the weights and to the running totals that the averaging needs.
    /// </summary>
    private static void Update(Dictionary<int, double[]> weights, Dictionary<int, double[]> totals, int bucket, int tag, int delta, long step)
    {
        if (!weights.TryGetValue(bucket, out var row))
        {
            row = new double[UposExtensions.Count];
            weights[bucket] = row;
            totals[bucket] = new double[UposExtensions.Count];
        }

        row[tag] += delta;
        totals[bucket][tag] += delta * step;
    }

    /// <summary>
    ///     Averages the weights, quantizes them to 16 bits and writes the weights file.
    /// </summary>
    private static byte[] Quantize(Dictionary<int, double[]> weights, Dictionary<int, double[]> totals, long steps, bool compress)
    {
        // Average as in the spike: the weight minus the time-weighted updates divided by the number of steps
        var averaged = new Dictionary<int, double[]>();
        double max = 0;
        foreach (var (bucket, row) in weights)
        {
            var values = new double[row.Length];
            for (var t = 0; t < row.Length; t++)
            {
                values[t] = row[t] - (totals[bucket][t] / steps);
                max = Math.Max(max, Math.Abs(values[t]));
            }

            averaged[bucket] = values;
        }

        // One global scale maps the largest weight to 30000
        var scale = (float)(max / 30000.0);
        var rows = averaged.Select(r => new KeyValuePair<int, short[]>(
            r.Key,
            r.Value.Select(v => (short)Math.Round(v / scale)).ToArray()));
        return PerceptronModelWriter.Compile(TinyModels.HashBits, 1f, scale, FeatureExtractor.FeatureSetVersion, rows, compress);
    }
}
