using System.Globalization;
using System.Text;

namespace DemaConsulting.PartOfSpeech.Trainer;

/// <summary>
///     A first, dev-only probe of the glossary question: does a calibrated score boost let a type-level glossary help
///     the tagger, and what does a wrong glossary cost?
/// </summary>
/// <remarks>
///     <para>
///         A later release adds a soft glossary mode on top of <see cref="TokenConstraint.Prefer(Upos, double)"/>. This
///         probe simulates glossaries on the open development split (the only set it may read): it takes the word types
///         with at least two allowed tags, at least three occurrences and a stable gold tag (at least 90 percent), gives
///         a share of them their majority tag as glossary tag, replaces the tag of a share of the entries by another
///         allowed tag (a wrong glossary), and tags the split with <c>Prefer(tag, b)</c> for several boosts <c>b</c> (in
///         units of the model temperature, so that <c>b</c> is the log-odds shift) and with the hard
///         <see cref="TokenConstraint.Only(Upos[])"/> as the reference. It reports the accuracy on covered tokens (glossary
///         right and wrong), the accuracy of the uncovered tokens of the same sentences (the effect on neighbors through
///         the history), the overall accuracy and the harmful errors.
///     </para>
///     <para>
///         This first version covers the boost side of the decision only; comparing it with a model that has a trained
///         glossary feature is the later full experiment. Deterministic for a seed. Stateless and thread-safe.
///     </para>
/// </remarks>
internal static class GlossaryProbe
{
    /// <summary>
    ///     The boosts to try, in units of the model temperature.
    /// </summary>
    private static readonly double[] Boosts = { 0.5, 1, 2, 4, 8 };

    /// <summary>
    ///     The shares of eligible types that are in the glossary, in permille.
    /// </summary>
    private static readonly int[] CoveragePermille = { 300, 1000 };

    /// <summary>
    ///     The shares of glossary entries that carry a wrong tag, in percent.
    /// </summary>
    private static readonly int[] WrongPercent = { 0, 10, 30 };

    /// <summary>
    ///     Runs the probe.
    /// </summary>
    /// <param name="weights">The weights file.</param>
    /// <param name="open">The open development sentences.</param>
    /// <param name="lexicon">The lexicon that defines the allowed tags of a type.</param>
    /// <param name="seed">The seed of the glossary simulation.</param>
    /// <returns>The report lines (a table in text form).</returns>
    public static List<string> Run(byte[] weights, IReadOnlyList<TrainingSentence> open, Lexicon lexicon, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(weights);
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(lexicon);
        var tagger = DevEvaluator.CreateTagger(weights);
        var temperature = PerceptronModel.Load(weights).Temperature;
        var eligible = EligibleTypes(open, lexicon);

        var lines = new List<string>
        {
            string.Format(CultureInfo.InvariantCulture, "glossary probe on the open development split: {0} eligible types, temperature {1:0.####}", eligible.Count, temperature),
            "coverage wrong  mode        covered(right) acc%  covered(wrong) acc%  uncovered-in-sentence acc%  overall acc%  harmful",
        };
        var scenario = 0;
        foreach (var coverage in CoveragePermille)
        {
            foreach (var wrong in WrongPercent)
            {
                var glossary = BuildGlossary(eligible, coverage, wrong, seed + (ulong)(++scenario));
                lines.Add(Row(coverage, wrong, "baseline", Measure(tagger, open, null, 0, false)));
                foreach (var boost in Boosts)
                {
                    lines.Add(Row(coverage, wrong, "prefer " + boost.ToString("0.#", CultureInfo.InvariantCulture), Measure(tagger, open, glossary, boost * temperature, false)));
                }

                lines.Add(Row(coverage, wrong, "only", Measure(tagger, open, glossary, 0, true)));
            }
        }

        return lines;
    }

    /// <summary>
    ///     A glossary type with its stable gold tag and its allowed tags.
    /// </summary>
    internal sealed class EligibleType
    {
        /// <summary>
        ///     Gets or sets the lower-cased spelling.
        /// </summary>
        public string Word { get; set; } = string.Empty;

        /// <summary>
        ///     Gets or sets the majority gold tag in the development split.
        /// </summary>
        public Upos Majority { get; set; }

        /// <summary>
        ///     Gets or sets the tags that the lexicon allows for the type.
        /// </summary>
        public List<Upos> Allowed { get; set; } = new();
    }

    /// <summary>
    ///     The counts of one measurement.
    /// </summary>
    private sealed class Measurement
    {
        /// <summary>
        ///     Gets or sets the number of covered tokens whose glossary tag is the gold tag.
        /// </summary>
        public int RightCount { get; set; }

        /// <summary>
        ///     Gets or sets how many of them the tagger got right.
        /// </summary>
        public int RightCorrect { get; set; }

        /// <summary>
        ///     Gets or sets the number of covered tokens whose glossary tag is not the gold tag.
        /// </summary>
        public int WrongCount { get; set; }

        /// <summary>
        ///     Gets or sets how many of them the tagger got right.
        /// </summary>
        public int WrongCorrect { get; set; }

        /// <summary>
        ///     Gets or sets the number of uncovered tokens in sentences with a covered token.
        /// </summary>
        public int NeighborCount { get; set; }

        /// <summary>
        ///     Gets or sets how many of them the tagger got right.
        /// </summary>
        public int NeighborCorrect { get; set; }

        /// <summary>
        ///     Gets or sets the number of all tokens.
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        ///     Gets or sets how many of all tokens the tagger got right.
        /// </summary>
        public int TotalCorrect { get; set; }

        /// <summary>
        ///     Gets or sets the number of harmful errors.
        /// </summary>
        public int Harmful { get; set; }
    }

    /// <summary>
    ///     Finds the word types that qualify for a simulated glossary.
    /// </summary>
    /// <param name="open">The development sentences.</param>
    /// <param name="lexicon">The lexicon.</param>
    /// <returns>The eligible types in ordinal order of their spelling.</returns>
    internal static List<EligibleType> EligibleTypes(IReadOnlyList<TrainingSentence> open, Lexicon lexicon)
    {
        // Count the gold tags of every lower-cased type
        var counts = new SortedDictionary<string, int[]>(StringComparer.Ordinal);
        foreach (var sentence in open)
        {
            for (var i = 0; i < sentence.Tokens.Length; i++)
            {
                if (sentence.Gold[i] < 0)
                {
                    continue;
                }

                var key = sentence.Tokens[i].ToLowerInvariant();
                if (!counts.TryGetValue(key, out var row))
                {
                    row = new int[UposExtensions.Count];
                    counts[key] = row;
                }

                row[sentence.Gold[i]]++;
            }
        }

        // Keep the ambiguous types that occur often enough with a stable gold tag
        var result = new List<EligibleType>();
        foreach (var pair in counts)
        {
            var total = pair.Value.Sum();
            var majority = Array.IndexOf(pair.Value, pair.Value.Max());
            var allowed = Lexicon.AllowedTags(pair.Key, lexicon);
            if (total >= 3 && allowed.Count >= 2 && pair.Value[majority] * 10 >= total * 9 && allowed.Contains((Upos)majority))
            {
                result.Add(new EligibleType { Word = pair.Key, Majority = (Upos)majority, Allowed = allowed.ToList() });
            }
        }

        return result;
    }

    /// <summary>
    ///     Draws a glossary from the eligible types.
    /// </summary>
    /// <param name="eligible">The eligible types.</param>
    /// <param name="coveragePermille">The share of types in the glossary.</param>
    /// <param name="wrongPercent">The share of entries with a wrong tag.</param>
    /// <param name="seed">The seed.</param>
    /// <returns>The glossary tag of every covered type, and whether it is the majority tag is implied by comparison with the gold.</returns>
    internal static Dictionary<string, Upos> BuildGlossary(List<EligibleType> eligible, int coveragePermille, int wrongPercent, ulong seed)
    {
        var random = new DeterministicRandom(seed);
        var order = new int[eligible.Count];
        for (var i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        random.Shuffle(order);
        var take = (int)((long)eligible.Count * coveragePermille / 1000);
        var glossary = new Dictionary<string, Upos>(StringComparer.Ordinal);
        for (var n = 0; n < take; n++)
        {
            var type = eligible[order[n]];
            var tag = type.Majority;
            if (random.NextInt(100) < wrongPercent)
            {
                var others = type.Allowed.Where(t => t != type.Majority).ToList();
                tag = others[random.NextInt(others.Count)];
            }

            glossary[type.Word] = tag;
        }

        return glossary;
    }

    /// <summary>
    ///     Tags the development split with a glossary and counts the outcomes.
    /// </summary>
    /// <param name="tagger">The tagger.</param>
    /// <param name="open">The sentences.</param>
    /// <param name="glossary">The glossary, or <c>null</c> for none.</param>
    /// <param name="boost">The boost in raw score units for a soft glossary.</param>
    /// <param name="hard">Whether the glossary is a hard constraint.</param>
    /// <returns>The counts.</returns>
    private static Measurement Measure(Tagger tagger, IReadOnlyList<TrainingSentence> open, Dictionary<string, Upos>? glossary, double boost, bool hard)
    {
        var measurement = new Measurement();
        foreach (var sentence in open)
        {
            // One constraint per token: the glossary tag of a covered type, otherwise none
            var constraints = new TokenConstraint[sentence.Tokens.Length];
            var covered = new bool[sentence.Tokens.Length];
            var any = false;
            for (var i = 0; i < constraints.Length; i++)
            {
                if (glossary != null && glossary.TryGetValue(sentence.Tokens[i].ToLowerInvariant(), out var tag))
                {
                    constraints[i] = hard ? TokenConstraint.Only(tag) : TokenConstraint.Prefer(tag, boost);
                    covered[i] = true;
                    any = true;
                }
            }

            var tagged = tagger.Tag(sentence.Tokens, constraints);
            for (var i = 0; i < constraints.Length; i++)
            {
                var gold = sentence.Gold[i];
                if (gold < 0)
                {
                    continue;
                }

                var outcome = Metrics.Judge("probe", ((Upos)gold).ToTagString(), tagged.Tokens[i], t => t.ToTagString(), "NOUN", "VERB", false);
                measurement.TotalCount++;
                measurement.TotalCorrect += outcome.Correct ? 1 : 0;
                measurement.Harmful += outcome.Harmful ? 1 : 0;
                if (covered[i])
                {
                    // Covered token: tally by whether the glossary entry is the gold tag or a wrong one
                    if (glossary![sentence.Tokens[i].ToLowerInvariant()] == (Upos)gold)
                    {
                        measurement.RightCount++;
                        measurement.RightCorrect += outcome.Correct ? 1 : 0;
                    }
                    else
                    {
                        measurement.WrongCount++;
                        measurement.WrongCorrect += outcome.Correct ? 1 : 0;
                    }
                }
                else if (any)
                {
                    measurement.NeighborCount++;
                    measurement.NeighborCorrect += outcome.Correct ? 1 : 0;
                }
            }
        }

        return measurement;
    }

    /// <summary>
    ///     Formats one table row.
    /// </summary>
    /// <param name="coveragePermille">The coverage.</param>
    /// <param name="wrongPercent">The wrong share.</param>
    /// <param name="mode">The mode name.</param>
    /// <param name="m">The counts.</param>
    /// <returns>The row.</returns>
    private static string Row(int coveragePermille, int wrongPercent, string mode, Measurement m)
    {
        static string Percent(int correct, int count) => count == 0 ? "-" : (100.0 * correct / count).ToString("0.00", CultureInfo.InvariantCulture);
        var text = new StringBuilder();
        text.Append(string.Format(CultureInfo.InvariantCulture, "{0,6}% {1,4}%  {2,-10}  ", coveragePermille / 10, wrongPercent, mode));
        text.Append(string.Format(CultureInfo.InvariantCulture, "{0,6} {1,9}   {2,6} {3,9}   {4,8} {5,17}   {6,12}  {7,7}", m.RightCount, Percent(m.RightCorrect, m.RightCount), m.WrongCount, Percent(m.WrongCorrect, m.WrongCount), m.NeighborCount, Percent(m.NeighborCorrect, m.NeighborCount), Percent(m.TotalCorrect, m.TotalCount), m.Harmful));
        return text.ToString();
    }
}
