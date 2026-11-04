using System.Globalization;

namespace DemaConsulting.PartOfSpeech.Trainer;

/// <summary>
///     The tags and confidences that a <see cref="HintedPredictor"/> produced for one sentence.
/// </summary>
internal sealed class HintedTagging
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="HintedTagging"/> class.
    /// </summary>
    /// <param name="tags">The chosen tag ordinal of every token.</param>
    /// <param name="confidences">The probability of the chosen tag of every token.</param>
    /// <param name="candidates">The candidate mask of every token.</param>
    public HintedTagging(int[] tags, double[] confidences, int[] candidates)
    {
        Tags = tags;
        Confidences = confidences;
        Candidates = candidates;
    }

    /// <summary>
    ///     Gets the chosen tag ordinal of every token.
    /// </summary>
    public int[] Tags { get; }

    /// <summary>
    ///     Gets the probability of the chosen tag of every token.
    /// </summary>
    public double[] Confidences { get; }

    /// <summary>
    ///     Gets the candidate mask of every token.
    /// </summary>
    public int[] Candidates { get; }
}

/// <summary>
///     The experiment's own decoder: the computation of the runtime decoder (integer weights times the scale, candidate
///     restriction, softmax at the temperature) plus an optional glossary hint, either as a score boost, as a hard
///     constraint, or as the two glossary features of model M1.
/// </summary>
/// <remarks>
///     <para>
///         The runtime decoder cannot take the M1 features (they are not in the library's feature set), so the
///         experiment decodes everything with this class, and a parity check against the real <c>Tagger</c> on the
///         same weights shows that it is the same decoder when no feature is used (the experiment prints that check).
///         Not thread-safe (it owns a workspace).
///     </para>
/// </remarks>
internal sealed class HintedPredictor
{
    /// <summary>
    ///     The quantized weights of every bucket and tag.
    /// </summary>
    private readonly short[] _weights;

    /// <summary>
    ///     The scratch workspace.
    /// </summary>
    private readonly DecoderWorkspace _workspace = new();

    /// <summary>
    ///     The scratch buffer for the buckets of a token.
    /// </summary>
    private readonly int[] _buckets = new int[FeatureExtractor.MaxFeatures + GlossaryFeatures.Count];

    /// <summary>
    ///     The lexicon.
    /// </summary>
    private readonly Lexicon _lexicon;

    /// <summary>
    ///     The weight scale, as the runtime reads it from the header.
    /// </summary>
    private readonly double _scale;

    /// <summary>
    ///     The softmax temperature, as the runtime reads it from the header.
    /// </summary>
    private readonly double _temperature;

    /// <summary>
    ///     Initializes a new instance of the <see cref="HintedPredictor"/> class.
    /// </summary>
    /// <param name="hashBits">The bucket space exponent.</param>
    /// <param name="rows">The quantized rows.</param>
    /// <param name="scale">The weight scale.</param>
    /// <param name="temperature">The softmax temperature.</param>
    /// <param name="lexicon">The lexicon.</param>
    public HintedPredictor(int hashBits, IEnumerable<KeyValuePair<int, short[]>> rows, float scale, float temperature, Lexicon lexicon)
    {
        HashBits = hashBits;
        _scale = scale;
        _temperature = temperature;
        _lexicon = lexicon;
        _weights = new short[(1 << hashBits) * UposExtensions.Count];
        foreach (var row in rows)
        {
            Array.Copy(row.Value, 0, _weights, row.Key * UposExtensions.Count, UposExtensions.Count);
        }
    }

    /// <summary>
    ///     Gets the bucket space exponent.
    /// </summary>
    public int HashBits { get; }

    /// <summary>
    ///     Tags a sentence.
    /// </summary>
    /// <param name="tokens">The tokens.</param>
    /// <param name="hints">The glossary hint tag ordinal of every token (-1 for none), or <c>null</c> for no glossary.</param>
    /// <param name="boost">The score boost, in raw score units, that a hint adds to its tag (0 for none).</param>
    /// <param name="hard">Whether a hint is a hard constraint (the hinted tag is the only candidate).</param>
    /// <param name="useFeature">Whether a hint enters the model as the glossary features (model M1).</param>
    /// <returns>The tags, confidences and candidates.</returns>
    public HintedTagging Predict(string[] tokens, int[]? hints, double boost, bool hard, bool useFeature)
    {
        FeatureExtractor.Prepare(_workspace, tokens, HashBits);
        var tags = new int[tokens.Length];
        var confidences = new double[tokens.Length];
        var candidates = new int[tokens.Length];
        var previous1 = FeatureExtractor.StartTag1;
        var previous2 = FeatureExtractor.StartTag2;
        var sums = new int[UposExtensions.Count];
        var real = new double[UposExtensions.Count];
        for (var i = 0; i < tokens.Length; i++)
        {
            var hint = hints == null ? -1 : hints[i];
            var count = FeatureExtractor.Extract(_workspace, i, previous1, previous2, HashBits, _buckets);
            if (hint >= 0 && useFeature)
            {
                count = GlossaryFeatures.Append(_buckets, count, hint, previous1, HashBits);
            }

            Array.Clear(sums, 0, sums.Length);
            for (var k = 0; k < count; k++)
            {
                var offset = _buckets[k] * UposExtensions.Count;
                for (var tag = 0; tag < sums.Length; tag++)
                {
                    sums[tag] += _weights[offset + tag];
                }
            }

            // Candidates: the lexicon and shape rules, or the single hinted tag for a hard constraint
            var mask = TagSelection.AllowedMask(_workspace, i, _lexicon);
            if (hint >= 0 && hard)
            {
                mask = 1 << hint;
            }

            // Real scores, the best candidate (the lowest ordinal wins a tie) and the softmax at the temperature
            var best = -1;
            for (var tag = 0; tag < UposExtensions.Count; tag++)
            {
                if ((mask & (1 << tag)) == 0)
                {
                    continue;
                }

                real[tag] = (sums[tag] * _scale) + (tag == hint && !hard ? boost : 0.0);
                if (best < 0 || real[tag] > real[best])
                {
                    best = tag;
                }
            }

            var sum = 0.0;
            for (var tag = 0; tag < UposExtensions.Count; tag++)
            {
                if ((mask & (1 << tag)) != 0)
                {
                    sum += Math.Exp((real[tag] - real[best]) / _temperature);
                }
            }

            tags[i] = best;
            confidences[i] = 1.0 / sum;
            candidates[i] = mask;
            previous2 = previous1;
            previous1 = best;
        }

        return new HintedTagging(tags, confidences, candidates);
    }
}

/// <summary>
///     The dev-only glossary experiment (E8): calibrated score boosts on the final model (M0) against a model with
///     trained glossary features (M1), on simulated type-level glossaries over the open development split.
/// </summary>
/// <remarks>
///     <para>
///         The experiment applies the pre-registered rule of <c>RUN-LOG.md</c>: b* is the smallest boost that reaches 95%
///         of the gain of a hard constraint at 30% coverage and no wrong entries; the feature is adopted only if M1 without
///         a glossary does not lose accuracy or gain harmful errors against M0, beats M0 + b* on covered tokens (by 0.5
///         points) or on uncovered tokens of the same sentences (by 0.2 points) with a paired interval that excludes
///         zero, and has no more harmful errors than M0 + b* with 10% wrong entries.
///     </para>
///     <para>
///         It reads the open development split only (gold v2 dev has a single target per sentence and cannot show the
///         effect on neighbors). Deterministic for a seed. Stateless.
///     </para>
/// </remarks>
internal static class GlossaryExperiment
{
    /// <summary>
    ///     The boosts to try, in units of the model temperature (log-odds).
    /// </summary>
    private static readonly double[] Boosts = { 0.5, 1, 2, 4, 8, 16 };

    /// <summary>
    ///     The shares of eligible types in the glossary, in permille.
    /// </summary>
    private static readonly int[] CoveragePermille = { 300, 1000 };

    /// <summary>
    ///     The shares of wrong glossary entries, in percent.
    /// </summary>
    private static readonly int[] WrongPercent = { 0, 10, 30 };

    /// <summary>
    ///     The coverage of the primary scenario of the decision, in permille.
    /// </summary>
    private const int PrimaryCoverage = 300;

    /// <summary>
    ///     The share of wrong entries of the primary scenario of the decision, in percent.
    /// </summary>
    private const int PrimaryWrong = 10;

    /// <summary>
    ///     The share of eligible training tokens that get a hint when M1 is trained, in percent.
    /// </summary>
    public const int TrainingHintPercent = 30;

    /// <summary>
    ///     The share of training hints that are wrong, in percent.
    /// </summary>
    public const int TrainingWrongPercent = 10;

    /// <summary>
    ///     The token categories of a scenario.
    /// </summary>
    private enum Category : byte
    {
        /// <summary>The token is in a sentence without a covered token.</summary>
        Other = 0,

        /// <summary>The token is covered and the glossary entry is its gold tag.</summary>
        CoveredRight = 1,

        /// <summary>The token is covered and the glossary entry is not its gold tag.</summary>
        CoveredWrong = 2,

        /// <summary>The token is not covered but its sentence has a covered token.</summary>
        Neighbor = 3,
    }

    /// <summary>
    ///     The outcomes of one system in one scenario, aligned with the categories of the scenario.
    /// </summary>
    private sealed class SystemResult
    {
        /// <summary>
        ///     Gets or sets the name of the system.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        ///     Gets or sets the outcomes of every token with a gold tag.
        /// </summary>
        public List<Outcome> Outcomes { get; set; } = new();
    }

    /// <summary>
    ///     Runs the experiment.
    /// </summary>
    /// <param name="m0">The final model without glossary features: the predictor.</param>
    /// <param name="m1">The model with glossary features: the predictor.</param>
    /// <param name="m0File">The weights file of M0 (for the parity check against the real decoder).</param>
    /// <param name="open">The open development sentences.</param>
    /// <param name="lexicon">The lexicon.</param>
    /// <param name="seed">The seed of the glossary simulation.</param>
    /// <returns>The report lines.</returns>
    public static List<string> Run(HintedPredictor m0, HintedPredictor m1, byte[] m0File, IReadOnlyList<TrainingSentence> open, Lexicon lexicon, ulong seed)
    {
        var lines = new List<string>();
        var temperature0 = PerceptronModel.Load(m0File, FeatureExtractor.FeatureSetVersion).Temperature;
        var eligible = GlossaryProbe.EligibleTypes(open, lexicon);
        lines.Add(string.Format(CultureInfo.InvariantCulture, "glossary experiment on the open development split: {0} eligible types, M0 temperature {1:0.###}", eligible.Count, temperature0));

        // Parity of the experiment's decoder with the real decoder, without and with a boost
        lines.Add(ParityCheck(m0, m0File, open, eligible, seed, temperature0));

        // The no-glossary results of both models (the same for every scenario)
        var m0None = Evaluate(m0, open, null, 0, false, false);
        var m1None = Evaluate(m1, open, null, 0, false, true);

        // The scenarios, in the order and with the seeds of the probe
        var scenarios = new Dictionary<(int, int), Dictionary<string, SystemResult>>();
        var categories = new Dictionary<(int, int), Category[]>();
        var scenarioNumber = 0;
        foreach (var coverage in CoveragePermille)
        {
            foreach (var wrong in WrongPercent)
            {
                var glossary = GlossaryProbe.BuildGlossary(eligible, coverage, wrong, seed + (ulong)(++scenarioNumber));
                var hints = Hints(open, glossary);
                var category = Categorize(open, hints);
                var results = new Dictionary<string, SystemResult>(StringComparer.Ordinal)
                {
                    ["M0 none"] = new SystemResult { Name = "M0 none", Outcomes = m0None },
                };
                foreach (var boost in Boosts)
                {
                    var name = "M0 prefer " + boost.ToString("0.#", CultureInfo.InvariantCulture);
                    results[name] = new SystemResult { Name = name, Outcomes = Evaluate(m0, open, hints, boost * temperature0, false, false) };
                }

                results["M0 only"] = new SystemResult { Name = "M0 only", Outcomes = Evaluate(m0, open, hints, 0, true, false) };
                results["M1 none"] = new SystemResult { Name = "M1 none", Outcomes = m1None };
                results["M1 feature"] = new SystemResult { Name = "M1 feature", Outcomes = Evaluate(m1, open, hints, 0, false, true) };
                scenarios[(coverage, wrong)] = results;
                categories[(coverage, wrong)] = category;

                lines.Add(string.Empty);
                lines.Add(string.Format(CultureInfo.InvariantCulture, "scenario: coverage {0}% of eligible types, wrong entries {1}%", coverage / 10, wrong));
                lines.Add("system        covered(right) n acc%    covered(wrong) n acc%    neighbors n acc%    overall acc%   harmful");
                foreach (var result in results.Values)
                {
                    lines.Add(Row(result, category));
                }
            }
        }

        lines.Add(string.Empty);
        Decide(lines, scenarios, categories);
        return lines;
    }

    /// <summary>
    ///     Applies the pre-registered decision rule.
    /// </summary>
    /// <param name="lines">The report lines.</param>
    /// <param name="scenarios">The results by scenario and system.</param>
    /// <param name="categories">The token categories by scenario.</param>
    private static void Decide(List<string> lines, Dictionary<(int, int), Dictionary<string, SystemResult>> scenarios, Dictionary<(int, int), Category[]> categories)
    {
        lines.Add("=== decision (pre-registered rule of RUN-LOG.md) ===");

        // b*: the smallest boost with at least 95% of the hard-constraint gain at 30% coverage and no wrong entries
        var primary0 = scenarios[(PrimaryCoverage, 0)];
        var baseline = Accuracy(primary0["M0 none"].Outcomes);
        var hardGain = Accuracy(primary0["M0 only"].Outcomes) - baseline;
        string? chosen = null;
        foreach (var boost in Boosts)
        {
            var name = "M0 prefer " + boost.ToString("0.#", CultureInfo.InvariantCulture);
            var gain = Accuracy(primary0[name].Outcomes) - baseline;
            lines.Add(string.Format(CultureInfo.InvariantCulture, "gain at 30% coverage, r=0: {0,-14} {1:+0.000;-0.000} points ({2:0.0}% of the hard gain {3:+0.000;-0.000})", name, gain, hardGain == 0 ? 0 : 100.0 * gain / hardGain, hardGain));
            if (chosen == null && gain >= 0.95 * hardGain)
            {
                chosen = name;
            }
        }

        var reached = chosen != null;
        chosen ??= "M0 prefer " + Boosts[Boosts.Length - 1].ToString("0.#", CultureInfo.InvariantCulture);
        lines.Add(reached ? $"b* = {chosen}" : $"b* not reached in the grid; the largest boost is used: {chosen}");

        // The loss clause of the plan (non-binding): covered-wrong accuracy at r=10%
        var primary = scenarios[(PrimaryCoverage, PrimaryWrong)];
        var category = categories[(PrimaryCoverage, PrimaryWrong)];
        var wrongBaseline = SubsetAccuracy(primary["M0 none"].Outcomes, category, Category.CoveredWrong);
        var hardLoss = wrongBaseline - SubsetAccuracy(primary["M0 only"].Outcomes, category, Category.CoveredWrong);
        var boostLoss = wrongBaseline - SubsetAccuracy(primary[chosen].Outcomes, category, Category.CoveredWrong);
        lines.Add(string.Format(CultureInfo.InvariantCulture, "covered-wrong loss at r=10%: {0} {1:0.00} points, hard {2:0.00} points: clause {3}", chosen, boostLoss, hardLoss, boostLoss <= 2 * hardLoss ? "holds" : "violated"));

        // (a) M1 without a glossary against M0 without a glossary, all open-dev tokens
        Bootstrap.Compare(primary["M1 none"].Outcomes, primary["M0 none"].Outcomes, Bootstrap.DefaultResamples, 1, out var noneAccuracy, out var noneHarmful);
        var passA = noneAccuracy.Difference >= -0.05 && (noneHarmful.Difference <= 0 || noneHarmful.ContainsZero || noneHarmful.Low <= 0);
        lines.Add(string.Format(CultureInfo.InvariantCulture, "(a) M1 none minus M0 none: accuracy {0:+0.000;-0.000} [{1:+0.000;-0.000}, {2:+0.000;-0.000}] points (needs >= -0.05); harmful per 1k {3:+0.00;-0.00} [{4:+0.00;-0.00}, {5:+0.00;-0.00}] (must not be higher): {6}", noneAccuracy.Difference, noneAccuracy.Low, noneAccuracy.High, noneHarmful.Difference, noneHarmful.Low, noneHarmful.High, passA ? "PASS" : "FAIL"));

        // (b) M1 with the glossary against M0 + b* on covered tokens and on neighbors, 30% coverage and 10% wrong
        var m1Feature = primary["M1 feature"].Outcomes;
        var m0Boost = primary[chosen].Outcomes;
        var covered = Subset(m1Feature, category, Category.CoveredRight, Category.CoveredWrong);
        var coveredBoost = Subset(m0Boost, category, Category.CoveredRight, Category.CoveredWrong);
        Bootstrap.Compare(covered, coveredBoost, Bootstrap.DefaultResamples, 1, out var coveredAccuracy, out _);
        var neighbors = Subset(m1Feature, category, Category.Neighbor);
        var neighborsBoost = Subset(m0Boost, category, Category.Neighbor);
        Bootstrap.Compare(neighbors, neighborsBoost, Bootstrap.DefaultResamples, 1, out var neighborAccuracy, out _);
        var passCovered = coveredAccuracy.Difference >= 0.5 && coveredAccuracy.Low > 0;
        var passNeighbors = neighborAccuracy.Difference >= 0.2 && neighborAccuracy.Low > 0;
        var passB = passCovered || passNeighbors;
        lines.Add(string.Format(CultureInfo.InvariantCulture, "(b) M1 feature minus {0}, 30% coverage, r=10%: covered accuracy {1:+0.000;-0.000} [{2:+0.000;-0.000}, {3:+0.000;-0.000}] points (needs >= +0.5 with the interval above 0: {4}); neighbors {5:+0.000;-0.000} [{6:+0.000;-0.000}, {7:+0.000;-0.000}] (needs >= +0.2, interval above 0: {8}): {9}", chosen, coveredAccuracy.Difference, coveredAccuracy.Low, coveredAccuracy.High, passCovered ? "yes" : "no", neighborAccuracy.Difference, neighborAccuracy.Low, neighborAccuracy.High, passNeighbors ? "yes" : "no", passB ? "PASS" : "FAIL"));

        // (c) harmful errors with 10% wrong entries: M1 must not be worse than M0 + b*
        Bootstrap.Compare(m1Feature, m0Boost, Bootstrap.DefaultResamples, 1, out _, out var harmful);
        var passC = harmful.Difference <= 0 || harmful.ContainsZero;
        lines.Add(string.Format(CultureInfo.InvariantCulture, "(c) harmful per 1k, M1 feature minus {0}, r=10%: {1:+0.00;-0.00} [{2:+0.00;-0.00}, {3:+0.00;-0.00}] ({4} against {5} harmful tokens; not worse = at most 0 or interval contains 0): {6}", chosen, harmful.Difference, harmful.Low, harmful.High, m1Feature.Count(o => o.Harmful), m0Boost.Count(o => o.Harmful), passC ? "PASS" : "FAIL"));

        lines.Add(passA && passB && passC
            ? "DECISION: add the trained glossary feature (all three conditions hold)."
            : "DECISION: boost only; the Prefer boosts are calibrated on dev in phase 4 (at least one condition fails).");
    }

    /// <summary>
    ///     Decodes the open development split with one system and judges every token with a gold tag.
    /// </summary>
    /// <param name="predictor">The predictor.</param>
    /// <param name="open">The sentences.</param>
    /// <param name="hints">The hints of every sentence, or <c>null</c> for no glossary.</param>
    /// <param name="boost">The boost in raw score units.</param>
    /// <param name="hard">Whether hints are hard constraints.</param>
    /// <param name="useFeature">Whether hints enter as glossary features.</param>
    /// <returns>One outcome per token with a gold tag, in corpus order.</returns>
    private static List<Outcome> Evaluate(HintedPredictor predictor, IReadOnlyList<TrainingSentence> open, List<int[]>? hints, double boost, bool hard, bool useFeature)
    {
        var outcomes = new List<Outcome>();
        for (var s = 0; s < open.Count; s++)
        {
            var sentence = open[s];
            var tagging = predictor.Predict(sentence.Tokens, hints?[s], boost, hard, useFeature);
            for (var i = 0; i < sentence.Tokens.Length; i++)
            {
                if (sentence.Gold[i] >= 0)
                {
                    outcomes.Add(Judge("open:" + sentence.SourceId, sentence.Gold[i], tagging.Tags[i], tagging.Confidences[i], tagging.Candidates[i]));
                }
            }
        }

        return outcomes;
    }

    /// <summary>
    ///     Judges one token the way <see cref="Metrics.Judge"/> does, from tag ordinals.
    /// </summary>
    /// <param name="document">The document id.</param>
    /// <param name="gold">The gold tag ordinal.</param>
    /// <param name="tag">The chosen tag ordinal.</param>
    /// <param name="confidence">The confidence of the chosen tag.</param>
    /// <param name="candidates">The candidate mask.</param>
    /// <returns>The outcome.</returns>
    private static Outcome Judge(string document, int gold, int tag, double confidence, int candidates)
    {
        var noun = (int)Upos.Noun;
        var verb = (int)Upos.Verb;
        var nounVerb = (gold == noun || gold == verb) && (candidates & (1 << noun)) != 0 && (candidates & (1 << verb)) != 0;
        var swapped = (gold == noun && tag == verb) || (gold == verb && tag == noun);
        return new Outcome(document, tag == gold, confidence, 0.0, false, nounVerb, nounVerb && swapped && confidence >= Metrics.HarmfulConfidence);
    }

    /// <summary>
    ///     Builds the hint arrays of a glossary for every sentence.
    /// </summary>
    /// <param name="open">The sentences.</param>
    /// <param name="glossary">The glossary tag of every covered type.</param>
    /// <returns>The hint of every token (-1 for none), per sentence.</returns>
    private static List<int[]> Hints(IReadOnlyList<TrainingSentence> open, Dictionary<string, Upos> glossary) =>
        open.Select(sentence => sentence.Tokens.Select(token => glossary.TryGetValue(token.ToLowerInvariant(), out var tag) ? (int)tag : -1).ToArray()).ToList();

    /// <summary>
    ///     Classifies every token with a gold tag of a scenario, in corpus order.
    /// </summary>
    /// <param name="open">The sentences.</param>
    /// <param name="hints">The hints of every sentence.</param>
    /// <returns>The category of every token with a gold tag.</returns>
    private static Category[] Categorize(IReadOnlyList<TrainingSentence> open, List<int[]> hints)
    {
        var categories = new List<Category>();
        for (var s = 0; s < open.Count; s++)
        {
            var sentence = open[s];
            var any = hints[s].Any(h => h >= 0);
            for (var i = 0; i < sentence.Tokens.Length; i++)
            {
                if (sentence.Gold[i] < 0)
                {
                    continue;
                }

                if (hints[s][i] >= 0)
                {
                    categories.Add(hints[s][i] == sentence.Gold[i] ? Category.CoveredRight : Category.CoveredWrong);
                }
                else
                {
                    categories.Add(any ? Category.Neighbor : Category.Other);
                }
            }
        }

        return categories.ToArray();
    }

    /// <summary>
    ///     Formats the row of one system of a scenario.
    /// </summary>
    /// <param name="result">The system's outcomes.</param>
    /// <param name="category">The categories of the scenario.</param>
    /// <returns>The row.</returns>
    private static string Row(SystemResult result, Category[] category)
    {
        string Part(Category wanted)
        {
            var count = 0;
            var correct = 0;
            for (var i = 0; i < category.Length; i++)
            {
                if (category[i] == wanted)
                {
                    count++;
                    correct += result.Outcomes[i].Correct ? 1 : 0;
                }
            }

            return string.Format(CultureInfo.InvariantCulture, "{0,6} {1,7}", count, count == 0 ? "-" : (100.0 * correct / count).ToString("0.00", CultureInfo.InvariantCulture));
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0,-13} {1}         {2}         {3}      {4,8:0.00}   {5,7}",
            result.Name,
            Part(Category.CoveredRight),
            Part(Category.CoveredWrong),
            Part(Category.Neighbor),
            Accuracy(result.Outcomes),
            result.Outcomes.Count(o => o.Harmful));
    }

    /// <summary>
    ///     Computes the accuracy of a list of outcomes in percent.
    /// </summary>
    /// <param name="outcomes">The outcomes.</param>
    /// <returns>The accuracy.</returns>
    private static double Accuracy(IReadOnlyList<Outcome> outcomes) =>
        outcomes.Count == 0 ? 0 : 100.0 * outcomes.Count(o => o.Correct) / outcomes.Count;

    /// <summary>
    ///     Computes the accuracy of the tokens of one category in percent.
    /// </summary>
    /// <param name="outcomes">The outcomes.</param>
    /// <param name="category">The categories.</param>
    /// <param name="wanted">The category.</param>
    /// <returns>The accuracy.</returns>
    private static double SubsetAccuracy(IReadOnlyList<Outcome> outcomes, Category[] category, Category wanted) =>
        Accuracy(Subset(outcomes, category, wanted));

    /// <summary>
    ///     Selects the outcomes of the tokens in the given categories.
    /// </summary>
    /// <param name="outcomes">The outcomes.</param>
    /// <param name="category">The categories.</param>
    /// <param name="wanted">The categories to keep.</param>
    /// <returns>The selected outcomes, in order.</returns>
    private static List<Outcome> Subset(IReadOnlyList<Outcome> outcomes, Category[] category, params Category[] wanted)
    {
        var result = new List<Outcome>();
        for (var i = 0; i < category.Length; i++)
        {
            if (Array.IndexOf(wanted, category[i]) >= 0)
            {
                result.Add(outcomes[i]);
            }
        }

        return result;
    }

    /// <summary>
    ///     Checks that the experiment's decoder equals the real decoder on the weights of M0.
    /// </summary>
    /// <param name="m0">The experiment's decoder over M0.</param>
    /// <param name="file">The weights file of M0.</param>
    /// <param name="open">The sentences.</param>
    /// <param name="eligible">The eligible glossary types.</param>
    /// <param name="seed">The seed.</param>
    /// <param name="temperature">The temperature of M0.</param>
    /// <returns>The report line.</returns>
    private static string ParityCheck(HintedPredictor m0, byte[] file, IReadOnlyList<TrainingSentence> open, List<GlossaryProbe.EligibleType> eligible, ulong seed, double temperature)
    {
        var tagger = DevEvaluator.CreateTagger(file);
        var glossary = GlossaryProbe.BuildGlossary(eligible, PrimaryCoverage, 0, seed + 1);
        var hints = Hints(open, glossary);
        var tokens = 0;
        var mismatchPlain = 0;
        var mismatchBoost = 0;
        for (var s = 0; s < open.Count; s++)
        {
            var sentence = open[s];
            var plain = tagger.Tag(sentence.Tokens);
            var mine = m0.Predict(sentence.Tokens, null, 0, false, false);
            var constraints = new TokenConstraint[sentence.Tokens.Length];
            for (var i = 0; i < constraints.Length; i++)
            {
                constraints[i] = hints[s][i] >= 0 ? TokenConstraint.Prefer((Upos)hints[s][i], 2 * temperature) : TokenConstraint.None;
            }

            var boosted = tagger.Tag(sentence.Tokens, constraints);
            var mineBoost = m0.Predict(sentence.Tokens, hints[s], 2 * temperature, false, false);
            for (var i = 0; i < constraints.Length; i++)
            {
                tokens++;
                mismatchPlain += (int)plain.Tokens[i].Tag != mine.Tags[i] ? 1 : 0;
                mismatchBoost += (int)boosted.Tokens[i].Tag != mineBoost.Tags[i] ? 1 : 0;
            }
        }

        return string.Format(CultureInfo.InvariantCulture, "parity with the real decoder on M0 ({0} tokens): {1} tag mismatches without a glossary, {2} with Prefer(2 x temperature) on a 30% glossary", tokens, mismatchPlain, mismatchBoost);
    }
}
