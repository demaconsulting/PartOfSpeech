using System.Globalization;

namespace DemaConsulting.PartOfSpeech.Trainer;

/// <summary>
///     The orchestration and report of the glossary evaluation: runs the scenarios and formats the tables (see the other part of this class).
/// </summary>
internal static partial class GlossaryEvaluation
{
    /// <summary>
    ///     Everything the scenarios share: the tagger, the lexicon and the settings of the run.
    /// </summary>
    private sealed class RunContext
    {
        /// <summary>
        ///     Gets or sets the weights file.
        /// </summary>
        public byte[] Weights { get; set; } = Array.Empty<byte>();

        /// <summary>
        ///     Gets or sets the tagger (all alternatives enabled).
        /// </summary>
        public Tagger Tagger { get; set; } = null!;

        /// <summary>
        ///     Gets or sets the lexicon.
        /// </summary>
        public Lexicon Lexicon { get; set; } = null!;

        /// <summary>
        ///     Gets or sets the temperature of the model.
        /// </summary>
        public double Temperature { get; set; }

        /// <summary>
        ///     Gets or sets the seed of the simulation and of the bootstrap.
        /// </summary>
        public ulong Seed { get; set; }

        /// <summary>
        ///     Gets or sets the number of bootstrap resamples.
        /// </summary>
        public int Resamples { get; set; }
    }

    /// <summary>
    ///     The results of the open-dev scenarios A, B and AB, one result per coverage and wrong share.
    /// </summary>
    private sealed class OpenScenarios
    {
        /// <summary>
        ///     Gets the results by scenario name; the index is <c>coverageIndex * 3 + wrongIndex</c>.
        /// </summary>
        public Dictionary<string, ScenarioResult[]> Results { get; } = new(StringComparer.Ordinal);

        /// <summary>
        ///     Gets the single-word entries of every B scenario as the phase-3 path wants them (term to tag).
        /// </summary>
        public List<Dictionary<string, Upos>> SingleWords { get; } = new();

        /// <summary>
        ///     Gets the complete single-word glossary of every B scenario, including the terms that could not be represented (the phase-3 path).
        /// </summary>
        public List<Dictionary<string, Upos>> SingleWordsFull { get; } = new();

        /// <summary>
        ///     Gets the text of the simulated terms that the glossary could not represent as written.
        /// </summary>
        public SortedSet<string> SkippedTexts { get; } = new(StringComparer.Ordinal);

        /// <summary>
        ///     Gets or sets the number of simulated terms that the glossary could not represent as written.
        /// </summary>
        public int Skipped { get; set; }
    }

    /// <summary>
    ///     Counters of one row of the inflection table (scenario C).
    /// </summary>
    private sealed class InflectionCounts
    {
        /// <summary>Gets or sets the number of targets.</summary>
        public int Targets { get; set; }

        /// <summary>Gets or sets how many were covered by a match.</summary>
        public int Found { get; set; }

        /// <summary>Gets or sets how many were covered by a generated form.</summary>
        public int Generated { get; set; }

        /// <summary>Gets or sets how many found targets have a gold class among the tags of the matched form.</summary>
        public int Within { get; set; }

        /// <summary>Gets or sets how many targets the tagger gets right without a glossary.</summary>
        public int BaseCorrect { get; set; }

        /// <summary>Gets or sets how many targets the tagger gets right with the glossary.</summary>
        public int GlossaryCorrect { get; set; }
    }

    /// <summary>
    ///     Counters of one kind of token in the case-sensitivity table (scenario D).
    /// </summary>
    private sealed class CaseCounts
    {
        /// <summary>Gets or sets the number of tokens.</summary>
        public int Tokens { get; set; }

        /// <summary>Gets or sets how many the tagger gets right without a glossary.</summary>
        public int BaseCorrect { get; set; }

        /// <summary>Gets the matched, forced-wrong, right-to-wrong and correct counts of the case-insensitive (0) and case-sensitive (1) glossary.</summary>
        public int[][] Variant { get; } = { new int[4], new int[4] };
    }

    /// <summary>
    ///     Runs the whole evaluation on the development sets and formats the report.
    /// </summary>
    /// <param name="weights">The weights file (the embedded model in the command; any file in tests).</param>
    /// <param name="open">The open development sentences (loaded through <see cref="DevSets"/>).</param>
    /// <param name="gold">The gold v2 development targets (loaded through <see cref="DevSets"/>).</param>
    /// <param name="lexicon">The lexicon.</param>
    /// <param name="seed">The seed of the simulation and of the bootstrap.</param>
    /// <param name="resamples">The number of bootstrap resamples.</param>
    /// <returns>The report lines (Markdown tables).</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="resamples"/> is less than one.</exception>
    public static List<string> Run(byte[] weights, IReadOnlyList<TrainingSentence> open, IReadOnlyList<GoldTarget> gold, Lexicon lexicon, ulong seed, int resamples)
    {
        ArgumentNullException.ThrowIfNull(weights);
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(gold);
        ArgumentNullException.ThrowIfNull(lexicon);
        if (resamples < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(resamples), resamples, "At least one resample is needed.");
        }

        var context = new RunContext
        {
            Weights = weights,
            Tagger = DevEvaluator.CreateTagger(weights),
            Lexicon = lexicon,
            Temperature = PerceptronModel.Load(weights, FeatureExtractor.FeatureSetVersion).Temperature,
            Seed = seed,
            Resamples = resamples,
        };
        var openSet = EvalSet.FromOpen(open);
        var goldSet = EvalSet.FromGold(gold);
        var lines = new List<string>
        {
            string.Format(
                CultureInfo.InvariantCulture,
                "glossary evaluation (E9) on development data only: open dev {0} sentences, {1} scored tokens; gold v2 dev {2} targets; temperature {3:0.###}; seed {4}; {5} bootstrap resamples",
                openSet.Sentences.Count,
                openSet.ScoredCount,
                goldSet.ScoredCount,
                context.Temperature,
                seed,
                resamples),
        };

        var noneOpen = RunSystem(context.Tagger, openSet, null, lexicon, "no glossary");
        var scenarios = BuildOpenScenarios(context, openSet, open, noneOpen, lines);
        WriteParity(context, openSet, open, scenarios, lines);
        WriteSizes(lines, scenarios);
        foreach (var name in new[] { "A", "B", "AB" })
        {
            for (var c = 0; c < CoveragePermille.Length; c++)
            {
                WriteScenarioTables(lines, name, scenarios.Results[name].Skip(c * WrongPercent.Length).Take(WrongPercent.Length).ToArray(), true);
            }
        }

        WriteAtRisk(lines, scenarios.Results["A"]);
        WriteHistory(lines, scenarios);
        WriteHeadScenario(context, goldSet, gold, lines);
        WriteInflection(context, gold, lines);
        WriteCaseSensitivity(context, new[] { openSet, goldSet }, lines);
        var rules = RunPhrasal(context, openSet, open, noneOpen, lines);
        WriteDecisions(lines, scenarios, rules);
        return lines;
    }

    /// <summary>
    ///     Runs the scenarios A, B and AB for every coverage and wrong share and computes their paired differences.
    /// </summary>
    /// <param name="context">The run context.</param>
    /// <param name="openSet">The open development set.</param>
    /// <param name="open">The open development sentences.</param>
    /// <param name="none">The no-glossary run of the open set.</param>
    /// <param name="lines">The report lines (candidate counts are added).</param>
    /// <returns>The results.</returns>
    private static OpenScenarios BuildOpenScenarios(RunContext context, EvalSet openSet, IReadOnlyList<TrainingSentence> open, SystemRun none, List<string> lines)
    {
        var eligible = GlossaryProbe.EligibleTypes(open, context.Lexicon);
        var compounds = CompoundCandidates(open, context.Lexicon);
        lines.Add(string.Format(CultureInfo.InvariantCulture, "candidates: {0} distinct noun compounds (A), {1} eligible single-word types (B)", compounds.Count, eligible.Count));
        var scenarios = new OpenScenarios();
        foreach (var name in new[] { "A", "B", "AB" })
        {
            scenarios.Results[name] = new ScenarioResult[CoveragePermille.Length * WrongPercent.Length];
        }

        for (var ci = 0; ci < CoveragePermille.Length; ci++)
        {
            for (var ri = 0; ri < WrongPercent.Length; ri++)
            {
                var index = (ci * WrongPercent.Length) + ri;
                var number = (ulong)(index + 1);
                var singles = SingleWordTerms(eligible, CoveragePermille[ci], WrongPercent[ri], context.Seed + number);
                var drawn = DrawTerms(compounds, CoveragePermille[ci], WrongPercent[ri], context.Seed + 10 + number, WrongCompoundTags);
                var entriesB = ToEntries(singles, out var skippedB, scenarios.SkippedTexts);
                var entriesA = ToEntries(drawn, out var skippedA, scenarios.SkippedTexts);
                scenarios.Skipped += skippedA + skippedB;
                scenarios.SingleWords.Add(entriesB.ToDictionary(e => e.Term, e => e.Tags.First(), StringComparer.Ordinal));
                scenarios.SingleWordsFull.Add(singles.ToDictionary(t => t.Text, t => t.Tag, StringComparer.Ordinal));
                var combined = new Dictionary<string, List<GlossaryEntry>>(StringComparer.Ordinal)
                {
                    ["A"] = entriesA,
                    ["B"] = entriesB,
                    ["AB"] = entriesA.Concat(entriesB).ToList(),
                };
                foreach (var pair in combined)
                {
                    var result = RunScenario(context.Tagger, openSet, pair.Value, context.Lexicon, none);
                    result.CoveragePermille = CoveragePermille[ci];
                    result.WrongPercent = WrongPercent[ri];
                    Pair(result, context.Resamples, context.Seed);
                    scenarios.Results[pair.Key][index] = result;
                }
            }
        }

        return scenarios;
    }

    /// <summary>
    ///     Writes the parity check of scenario B: the real glossary path against the hand-built constraints and against the rows of the phase-3 probe.
    /// </summary>
    /// <param name="context">The run context.</param>
    /// <param name="openSet">The open development set.</param>
    /// <param name="open">The open development sentences.</param>
    /// <param name="scenarios">The scenario results.</param>
    /// <param name="lines">The report lines.</param>
    private static void WriteParity(RunContext context, EvalSet openSet, IReadOnlyList<TrainingSentence> open, OpenScenarios scenarios, List<string> lines)
    {
        var b = scenarios.Results["B"];
        var compared = 0;
        var mismatches = 0;
        for (var i = 0; i < b.Length; i++)
        {
            for (var s = 0; s < b[i].Systems.Length; s++)
            {
                var hand = HandBuiltTags(context.Tagger, openSet, scenarios.SingleWords[i], s == 0, s == 0 ? 0 : StrengthGrid[s - 1] * context.Temperature);
                var real = b[i].Systems[s].Tags;
                compared += real.Length;
                for (var k = 0; k < real.Length; k++)
                {
                    mismatches += hand[k] != real[k] ? 1 : 0;
                }
            }
        }

        lines.Add(string.Empty);
        lines.Add(string.Format(CultureInfo.InvariantCulture, "**Parity (B), tags**: the real glossary path and the hand-built constraints of the phase-3 probe (the same entries) give {0} different tags over {1} token decisions ({2} scenarios x {3} systems).", mismatches, compared, b.Length, b[0].Systems.Length));
        lines.Add(string.Format(CultureInfo.InvariantCulture, "{0} simulated terms (A and B, all scenarios) could not be represented as written and were left out of the glossaries of the real path: {1}", scenarios.SkippedTexts.Count, string.Join(" ", scenarios.SkippedTexts.Take(40))));

        // The printed rows of the phase-3 probe, for the systems that both tools have (hard is the probe's "only")
        var probe = ParseProbe(GlossaryProbe.Run(context.Weights, open, context.Lexicon, context.Seed));
        var rows = 0;
        var handDifferent = 0;
        var realDifferent = 0;
        for (var i = 0; i < b.Length; i++)
        {
            for (var s = 0; s < b[i].Systems.Length; s++)
            {
                var mode = s == 0 ? "only" : "prefer " + StrengthGrid[s - 1].ToString("0.#", CultureInfo.InvariantCulture);
                var key = string.Format(CultureInfo.InvariantCulture, "{0}% {1}% {2}", b[i].CoveragePermille / 10, b[i].WrongPercent, mode);
                if (probe.TryGetValue(key, out var figures))
                {
                    rows++;
                    var hand = HandBuiltFigures(context.Tagger, openSet, scenarios.SingleWordsFull[i], s == 0, s == 0 ? 0 : StrengthGrid[s - 1] * context.Temperature);
                    handDifferent += hand != figures ? 1 : 0;
                    realDifferent += ProbeFigures(b[i], s) != figures ? 1 : 0;
                }
            }
        }

        lines.Add(string.Format(CultureInfo.InvariantCulture, "**Parity (B), rows**: {0} rows of `glossary-probe` (covered right and wrong counts and accuracy, neighbor count and accuracy, overall accuracy, harmful errors); the hand-built path over the complete phase-3 glossaries reproduces {1} of them exactly ({2} differ); the real path differs in {3} rows, which is the effect of the left-out terms above.", rows, rows - handDifferent, handDifferent, realDifferent));
    }

    /// <summary>
    ///     Computes the eight figures of a probe row with the hand-built constraint path over a complete phase-3 glossary.
    /// </summary>
    /// <param name="tagger">The tagger.</param>
    /// <param name="set">The open set.</param>
    /// <param name="glossary">The single-word glossary (term to tag), as <c>GlossaryProbe.BuildGlossary</c> draws it.</param>
    /// <param name="hard">Whether the constraint is hard (<c>Only</c>) or a boost (<c>Prefer</c>).</param>
    /// <param name="boost">The boost in raw score units.</param>
    /// <returns>The figures separated by single spaces, formatted like <see cref="ProbeFigures"/>.</returns>
    private static string HandBuiltFigures(Tagger tagger, EvalSet set, Dictionary<string, Upos> glossary, bool hard, double boost)
    {
        var counts = new int[9];
        foreach (var sentence in set.Sentences)
        {
            var constraints = new TokenConstraint[sentence.Tokens.Length];
            var covered = new bool[sentence.Tokens.Length];
            for (var i = 0; i < constraints.Length; i++)
            {
                if (glossary.TryGetValue(sentence.Tokens[i].ToLowerInvariant(), out var tag))
                {
                    constraints[i] = hard ? TokenConstraint.Only(tag) : TokenConstraint.Prefer(tag, boost);
                    covered[i] = true;
                }
            }

            var tagged = tagger.Tag(sentence.Tokens, constraints);
            var any = covered.Any(c => c);
            for (var j = 0; j < sentence.Scored.Length; j++)
            {
                var index = sentence.Scored[j];
                var outcome = Metrics.Judge(sentence.DocumentId, sentence.Gold[j], tagged.Tokens[index], set.Label, set.NounLabel, set.VerbLabel, false);
                var right = covered[index] && glossary[sentence.Tokens[index].ToLowerInvariant()] == (Upos)sentence.GoldTags[j];
                var slot = FigureSlot(covered[index], any, right);
                counts[6]++;
                counts[7] += outcome.Correct ? 1 : 0;
                counts[8] += outcome.Harmful ? 1 : 0;
                if (slot >= 0)
                {
                    counts[slot]++;
                    counts[slot + 1] += outcome.Correct ? 1 : 0;
                }
            }
        }

        string Ratio(int correct, int count) => count == 0 ? "-" : Pct(100.0 * correct / count);
        return string.Join(
            " ",
            counts[0].ToString(CultureInfo.InvariantCulture),
            Ratio(counts[1], counts[0]),
            counts[2].ToString(CultureInfo.InvariantCulture),
            Ratio(counts[3], counts[2]),
            counts[4].ToString(CultureInfo.InvariantCulture),
            Ratio(counts[5], counts[4]),
            Ratio(counts[7], counts[6]),
            counts[8].ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    ///     Picks the counter pair of a probe row for a token: 0 covered right, 2 covered wrong, 4 neighbor, -1 none.
    /// </summary>
    /// <param name="covered">Whether the token is covered by the glossary.</param>
    /// <param name="sentenceHasCovered">Whether its sentence has a covered token.</param>
    /// <param name="right">Whether the entry is the gold tag (only for a covered token).</param>
    /// <returns>The index of the first counter of the pair, or -1.</returns>
    private static int FigureSlot(bool covered, bool sentenceHasCovered, bool right)
    {
        if (covered)
        {
            return right ? 0 : 2;
        }

        return sentenceHasCovered ? 4 : -1;
    }

    /// <summary>
    ///     Tags the open set with the hand-built constraint path of the phase-3 probe.
    /// </summary>
    /// <param name="tagger">The tagger.</param>
    /// <param name="set">The open set.</param>
    /// <param name="glossary">The single-word glossary (term to tag).</param>
    /// <param name="hard">Whether the constraint is hard (<c>Only</c>) or a boost (<c>Prefer</c>).</param>
    /// <param name="boost">The boost in raw score units.</param>
    /// <returns>The chosen tag of every scored token.</returns>
    private static sbyte[] HandBuiltTags(Tagger tagger, EvalSet set, Dictionary<string, Upos> glossary, bool hard, double boost)
    {
        var tags = new sbyte[set.ScoredCount];
        var offset = 0;
        foreach (var sentence in set.Sentences)
        {
            var constraints = new TokenConstraint[sentence.Tokens.Length];
            for (var i = 0; i < constraints.Length; i++)
            {
                if (glossary.TryGetValue(sentence.Tokens[i].ToLowerInvariant(), out var tag))
                {
                    constraints[i] = hard ? TokenConstraint.Only(tag) : TokenConstraint.Prefer(tag, boost);
                }
            }

            var tagged = tagger.Tag(sentence.Tokens, constraints);
            for (var j = 0; j < sentence.Scored.Length; j++)
            {
                tags[offset + j] = (sbyte)tagged.Tokens[sentence.Scored[j]].Tag;
            }

            offset += sentence.Scored.Length;
        }

        return tags;
    }

    /// <summary>
    ///     Reads the printed rows of the phase-3 probe: the key (coverage, wrong share, mode) and the eight figures.
    /// </summary>
    /// <param name="probe">The lines of the probe.</param>
    /// <returns>The figures by key.</returns>
    private static Dictionary<string, string> ParseProbe(IEnumerable<string> probe)
    {
        var rows = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in probe)
        {
            var fields = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length > 10 && fields[0].EndsWith("%", StringComparison.Ordinal) && fields[1].EndsWith("%", StringComparison.Ordinal))
            {
                var mode = string.Join(" ", fields.Skip(2).Take(fields.Length - 10));
                rows[$"{fields[0]} {fields[1]} {mode}"] = string.Join(" ", fields.Skip(fields.Length - 8));
            }
        }

        return rows;
    }

    /// <summary>
    ///     Formats the eight figures of a probe row for a system of a B scenario.
    /// </summary>
    /// <param name="scenario">The scenario.</param>
    /// <param name="system">The index of the system.</param>
    /// <returns>The figures separated by single spaces.</returns>
    private static string ProbeFigures(ScenarioResult scenario, int system)
    {
        var run = scenario.Systems[system];
        var right = Tally(run, scenario.Categories, c => c == GlossaryCategory.HeadRight, out var rightCorrect);
        var wrong = Tally(run, scenario.Categories, c => c == GlossaryCategory.HeadWrong, out var wrongCorrect);
        var neighbors = Tally(run, scenario.Categories, IsNeighbor, out var neighborCorrect);
        var all = run.Outcomes.Length;
        string Ratio(int correct, int count) => count == 0 ? "-" : Pct(100.0 * correct / count);
        return string.Join(
            " ",
            right.ToString(CultureInfo.InvariantCulture),
            Ratio(rightCorrect, right),
            wrong.ToString(CultureInfo.InvariantCulture),
            Ratio(wrongCorrect, wrong),
            neighbors.ToString(CultureInfo.InvariantCulture),
            Ratio(neighborCorrect, neighbors),
            Ratio(run.Outcomes.Count(o => o.Correct), all),
            run.Outcomes.Count(o => o.Harmful).ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    ///     Writes the table of scenario sizes: entries and the number of tokens in every category.
    /// </summary>
    /// <param name="lines">The report lines.</param>
    /// <param name="scenarios">The scenario results.</param>
    private static void WriteSizes(List<string> lines, OpenScenarios scenarios)
    {
        var rows = new List<string[]>();
        foreach (var pair in scenarios.Results)
        {
            foreach (var s in pair.Value)
            {
                rows.Add(SizeRow(pair.Key, s));
            }
        }

        Table(lines, "Scenario sizes (scored tokens per category; the term sets differ between rows because every row has its own seed)", new[] { "Scenario", "coverage %", "wrong %", "entries", "head right", "head wrong", "non-head", "neighbors", "no match", "at-risk first tokens" }, rows);
    }

    /// <summary>
    ///     Creates the row of the size table for a scenario.
    /// </summary>
    /// <param name="name">The name of the scenario.</param>
    /// <param name="s">The result.</param>
    /// <returns>The cells.</returns>
    private static string[] SizeRow(string name, ScenarioResult s)
    {
        int Count(Func<GlossaryCategory, bool> pick) => s.Categories.Count(pick);
        return new[]
        {
            name,
            (s.CoveragePermille / 10).ToString(CultureInfo.InvariantCulture),
            s.WrongPercent.ToString(CultureInfo.InvariantCulture),
            s.EntryCount.ToString("N0", CultureInfo.InvariantCulture),
            Count(c => c == GlossaryCategory.HeadRight).ToString("N0", CultureInfo.InvariantCulture),
            Count(c => c == GlossaryCategory.HeadWrong).ToString("N0", CultureInfo.InvariantCulture),
            Count(c => c == GlossaryCategory.NonHead).ToString("N0", CultureInfo.InvariantCulture),
            Count(IsNeighbor).ToString("N0", CultureInfo.InvariantCulture),
            Count(c => c == GlossaryCategory.NoMatch).ToString("N0", CultureInfo.InvariantCulture),
            s.AtRisk.Count(a => a).ToString("N0", CultureInfo.InvariantCulture),
        };
    }

    /// <summary>
    ///     Writes the three result tables of a scenario at one coverage: accuracy, harmful errors and swaps, and the categories at r = 10%.
    /// </summary>
    /// <param name="lines">The report lines.</param>
    /// <param name="name">The name of the scenario.</param>
    /// <param name="byWrong">The three results (r = 0, 10, 30%).</param>
    /// <param name="open"><c>true</c> for an open-dev scenario, <c>false</c> for the gold v2 head scenario (only the title differs).</param>
    private static void WriteScenarioTables(List<string> lines, string name, ScenarioResult[] byWrong, bool open)
    {
        var title = $"{name}, coverage {byWrong[0].CoveragePermille / 10}%" + (open ? string.Empty : " (gold v2 dev targets)");

        // Accuracy on covered tokens (no glossary on the same tokens first) and the change of overall accuracy
        var accuracyRows = new List<string[]>
        {
            new[] { "no glossary" }
                .Concat(byWrong.Select(r => Pct(Acc(r.None, r.Categories, IsCovered))))
                .Concat(Enumerable.Repeat("-", byWrong.Length))
                .ToArray(),
        };
        for (var s = 0; s < byWrong[0].Systems.Length; s++)
        {
            var index = s;
            accuracyRows.Add(
                new[] { SystemName(s) }
                    .Concat(byWrong.Select(r => Pct(Acc(r.Systems[index], r.Categories, IsCovered))))
                    .Concat(byWrong.Select(r => Interval(r.Cells[index].Accuracy)))
                    .ToArray());
        }

        Table(lines, title + ": accuracy on the covered tokens (%) and change of the overall accuracy (points, paired 95% interval) by share of wrong entries", new[] { "System", "covered r=0", "covered r=10", "covered r=30", "overall r=0", "overall r=10", "overall r=30" }, accuracyRows);

        // Harmful errors (change) and swaps (absolute and change), per 1,000 no-glossary noun-verb decisions
        var harmRows = new List<string[]>
        {
            new[] { "no glossary", "-", "-", "-" }.Concat(byWrong.Select(r => SwapsPer1000(r.None, r.None))).ToArray(),
        };
        for (var s = 0; s < byWrong[0].Systems.Length; s++)
        {
            var index = s;
            harmRows.Add(
                new[] { SystemName(s) }
                    .Concat(byWrong.Select(r => Interval(r.Cells[index].Harmful)))
                    .Concat(byWrong.Select(r => SwapsPer1000(r.Systems[index], r.None) + " (" + Interval(r.Cells[index].Swaps) + ")"))
                    .ToArray());
        }

        Table(lines, title + ": change of the harmful errors and confident noun-verb swaps per 1,000 no-glossary noun-verb decisions (paired 95% interval)", new[] { "System", "harmful r=0", "harmful r=10", "harmful r=30", "swaps r=0", "swaps r=10", "swaps r=30" }, harmRows);

        // The categories at r = 10%
        var tens = byWrong[1];
        var categoryRows = new List<string[]> { CategoryRow("no glossary", tens.None, tens.Categories) };
        categoryRows.AddRange(tens.Systems.Select(run => CategoryRow(run.Name, run, tens.Categories)));
        Table(lines, title + ": accuracy (%) by token category at r=10%", new[] { "System", "head right", "head wrong", "non-head", "neighbors", "no match in sentence" }, categoryRows);
    }

    /// <summary>
    ///     Creates the row of the category table for a system.
    /// </summary>
    /// <param name="name">The name of the system.</param>
    /// <param name="run">The run.</param>
    /// <param name="categories">The categories of the scenario.</param>
    /// <returns>The cells.</returns>
    private static string[] CategoryRow(string name, SystemRun run, GlossaryCategory[] categories) => new[]
    {
        name,
        Pct(Acc(run, categories, c => c == GlossaryCategory.HeadRight)),
        Pct(Acc(run, categories, c => c == GlossaryCategory.HeadWrong)),
        Pct(Acc(run, categories, c => c == GlossaryCategory.NonHead)),
        Pct(Acc(run, categories, IsNeighbor)),
        Pct(Acc(run, categories, c => c == GlossaryCategory.NoMatch)),
    };

    /// <summary>
    ///     Writes the accuracy of the at-risk first tokens of scenario A (the first token of a compound is listed as a verb in the lexicon).
    /// </summary>
    /// <param name="lines">The report lines.</param>
    /// <param name="a">The results of scenario A.</param>
    private static void WriteAtRisk(List<string> lines, ScenarioResult[] a)
    {
        var rows = new List<string[]>();
        var header = new List<string> { "System" };
        for (var i = 0; i < a.Length; i++)
        {
            header.Add($"cov {a[i].CoveragePermille / 10}% r={a[i].WrongPercent} (n={a[i].AtRisk.Count(x => x)})");
        }

        rows.Add(new[] { "no glossary" }.Concat(a.Select(r => Pct(AccFlag(r.None, r.AtRisk)))).ToArray());
        for (var s = 0; s < a[0].Systems.Length; s++)
        {
            var index = s;
            rows.Add(new[] { SystemName(s) }.Concat(a.Select(r => Pct(AccFlag(r.Systems[index], r.AtRisk)))).ToArray());
        }

        Table(lines, "A, at-risk subset: accuracy (%) of the first token of the matches whose first token the lexicon lists as a verb", header.ToArray(), rows);
    }

    /// <summary>
    ///     Writes the history effect (scenario F) and the invariant for the open scenarios at 30 percent coverage.
    /// </summary>
    /// <param name="lines">The report lines.</param>
    /// <param name="scenarios">The scenario results.</param>
    private static void WriteHistory(List<string> lines, OpenScenarios scenarios)
    {
        var classes = new[]
        {
            ("before the first match", GlossaryCategory.Before),
            ("+1 after a match", GlossaryCategory.Plus1),
            ("+2 after a match", GlossaryCategory.Plus2),
            ("+3 or later", GlossaryCategory.Later),
        };
        foreach (var name in new[] { "A", "B", "AB" })
        {
            var rows = new List<string[]>();
            foreach (var r in new[] { 0, 1 })
            {
                var s = scenarios.Results[name][r];
                foreach (var (label, category) in classes)
                {
                    var n = s.Categories.Count(c => c == category);
                    var row = new List<string>
                    {
                        WrongPercent[r].ToString(CultureInfo.InvariantCulture),
                        label,
                        n.ToString("N0", CultureInfo.InvariantCulture),
                        Pct(Acc(s.None, s.Categories, c => c == category)),
                    };
                    foreach (var run in s.Systems)
                    {
                        CountFlips(s.None, run, s.Categories, category, out var rightToWrong, out var wrongToRight);
                        row.Add(n == 0 ? "-" : $"{Sign(100.0 * (wrongToRight - rightToWrong) / n)} (-{rightToWrong}/+{wrongToRight})");
                    }

                    rows.Add(row.ToArray());
                }
            }

            var header = new List<string> { "r %", "uncovered tokens", "tokens", "no glossary %" };
            header.AddRange(Enumerable.Range(0, StrengthGrid.Length + 1).Select(SystemName));
            Table(lines, $"F, history effect, scenario {name} at coverage {PrimaryCoverage / 10}%: change of accuracy in points on identical tokens (flips right to wrong / wrong to right)", header.ToArray(), rows);
        }

        // The invariant: nothing before the first match of a sentence may change
        var checkedTokens = 0;
        var differences = 0;
        foreach (var results in scenarios.Results.Values)
        {
            foreach (var s in results)
            {
                foreach (var run in s.Systems)
                {
                    differences += CountBeforeDifferences(s.None, run, s.Categories, out var before);
                    checkedTokens += before;
                }
            }
        }

        lines.Add(string.Empty);
        lines.Add(string.Format(CultureInfo.InvariantCulture, "**Invariant (F)**: tokens before the first match of their sentence differ from the no-glossary tags in {0} of {1} comparisons (all scenarios and systems).", differences, checkedTokens.ToString("N0", CultureInfo.InvariantCulture)));
    }

    /// <summary>
    ///     Runs and writes scenario E: the head effect on the gold v2 development targets.
    /// </summary>
    /// <param name="context">The run context.</param>
    /// <param name="goldSet">The gold v2 set.</param>
    /// <param name="gold">The gold v2 targets.</param>
    /// <param name="lines">The report lines.</param>
    private static void WriteHeadScenario(RunContext context, EvalSet goldSet, IReadOnlyList<GoldTarget> gold, List<string> lines)
    {
        var none = RunSystem(context.Tagger, goldSet, null, context.Lexicon, "no glossary");
        var candidates = HeadCandidates(gold);
        lines.Add(string.Empty);
        lines.Add(string.Format(CultureInfo.InvariantCulture, "**E, gold v2 dev**: {0} distinct target forms of Noun and Verb targets; wrong entry = the other class of the noun-verb pair.", candidates.Count));
        for (var ci = 0; ci < CoveragePermille.Length; ci++)
        {
            var byWrong = new ScenarioResult[WrongPercent.Length];
            for (var ri = 0; ri < WrongPercent.Length; ri++)
            {
                var number = (ulong)((ci * WrongPercent.Length) + ri + 1);
                var entries = ToEntries(DrawTerms(candidates, CoveragePermille[ci], WrongPercent[ri], context.Seed + 30 + number, WrongHeadTags), out _);
                byWrong[ri] = RunScenario(context.Tagger, goldSet, entries, context.Lexicon, none);
                byWrong[ri].CoveragePermille = CoveragePermille[ci];
                byWrong[ri].WrongPercent = WrongPercent[ri];
                Pair(byWrong[ri], context.Resamples, context.Seed);
            }

            WriteScenarioTables(lines, "E", byWrong, false);
        }
    }

    /// <summary>
    ///     Runs and writes scenario C: inflection from gold lemmas.
    /// </summary>
    /// <param name="context">The run context.</param>
    /// <param name="gold">The gold v2 targets.</param>
    /// <param name="lines">The report lines.</param>
    private static void WriteInflection(RunContext context, IReadOnlyList<GoldTarget> gold, List<string> lines)
    {
        var lemmas = new SortedDictionary<string, UposSet>(StringComparer.Ordinal);
        foreach (var target in gold.Where(t => !t.IsConventionWord && t.Lemma.Length > 0 && (t.GoldClass == "Noun" || t.GoldClass == "Verb")))
        {
            var key = target.Lemma.ToLowerInvariant();
            var tag = target.GoldClass == "Noun" ? Upos.Noun : Upos.Verb;
            lemmas[key] = lemmas.TryGetValue(key, out var existing) ? existing.Add(tag) : UposSet.Of(tag);
        }

        var entries = new List<GlossaryEntry>();
        foreach (var pair in lemmas)
        {
            var entry = TryCreateLemma(pair.Key, pair.Value);
            if (entry != null)
            {
                entries.Add(entry);
            }
        }

        var glossary = Glossary.Create(entries, new GlossaryOptions { Mode = GlossaryMode.Hard, Inflect = true });
        var counts = new InflectionCounts[4];
        for (var i = 0; i < counts.Length; i++)
        {
            counts[i] = new InflectionCounts();
        }

        var missed = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var target in gold.Where(t => !t.IsConventionWord && t.Lemma.Length > 0 && (t.GoldClass == "Noun" || t.GoldClass == "Verb")))
        {
            var inflected = !string.Equals(target.Form, target.Lemma, StringComparison.OrdinalIgnoreCase);
            var row = counts[(target.GoldClass == "Noun" ? 0 : 2) + (inflected ? 0 : 1)];
            if (!CountInflectionTarget(context, glossary, target, row) && inflected)
            {
                missed.Add(target.Form.ToLowerInvariant() + " (" + target.Lemma.ToLowerInvariant() + ")");
            }
        }

        var rows = new List<string[]>();
        var names = new[] { "Noun, inflected form", "Noun, base form", "Verb, inflected form", "Verb, base form" };
        for (var i = 0; i < counts.Length; i++)
        {
            var c = counts[i];
            rows.Add(new[]
            {
                names[i],
                c.Targets.ToString("N0", CultureInfo.InvariantCulture),
                Share(c.Found, c.Targets),
                Share(c.Generated, c.Targets),
                Share(c.Within, c.Found),
                Share(c.BaseCorrect, c.Targets),
                Share(c.GlossaryCorrect, c.Targets),
            });
        }

        Table(lines, $"C, inflection from gold lemmas ({entries.Count:N0} lemma entries, hard): share of the targets found (recall), found by a generated form, found targets whose gold class is allowed by the matched form (precision), class accuracy without and with the glossary (%)", new[] { "Targets", "n", "found %", "generated form %", "precision %", "no glossary %", "hard glossary %" }, rows);
        lines.Add(string.Empty);
        lines.Add("Inflected targets that no entry found (form (lemma), up to 12): " + string.Join(", ", missed.Take(12)));
    }

    /// <summary>
    ///     Tags the sentence of a gold target with and without the lemma glossary and counts the outcome.
    /// </summary>
    /// <param name="context">The run context.</param>
    /// <param name="glossary">The lemma glossary.</param>
    /// <param name="target">The target.</param>
    /// <param name="counts">The counters of the row of the target.</param>
    /// <returns><c>true</c> if a match covers the target.</returns>
    private static bool CountInflectionTarget(RunContext context, Glossary glossary, GoldTarget target, InflectionCounts counts)
    {
        var tagged = context.Tagger.Tag(target.Tokens, glossary);
        var plain = context.Tagger.Tag(target.Tokens);
        var match = tagged.GlossaryMatches.FirstOrDefault(m => m.FirstToken <= target.Target && target.Target < m.FirstToken + m.TokenCount);
        counts.Targets++;
        counts.BaseCorrect += DevSets.GoldClassOf(plain.Tokens[target.Target].Tag) == target.GoldClass ? 1 : 0;
        counts.GlossaryCorrect += DevSets.GoldClassOf(tagged.Tokens[target.Target].Tag) == target.GoldClass ? 1 : 0;
        if (match == null)
        {
            return false;
        }

        var allowed = UposSet.FromMask(glossary.Index.TagSetMasks[match.TagSetId]);
        counts.Found++;
        counts.Generated += match.IsVariant ? 1 : 0;
        counts.Within += allowed.Any(t => DevSets.GoldClassOf(t) == target.GoldClass) ? 1 : 0;
        return true;
    }

    /// <summary>
    ///     Creates the entry of a lemma, or <c>null</c> when the glossary cannot represent it as one token.
    /// </summary>
    /// <param name="lemma">The lemma.</param>
    /// <param name="tags">The classes of the lemma.</param>
    /// <returns>The entry, or <c>null</c>.</returns>
    private static GlossaryEntry? TryCreateLemma(string lemma, UposSet tags)
    {
        try
        {
            var entry = new GlossaryEntry(lemma, tags);
            return entry.Tokens.Count == 1 ? entry : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    ///     Runs and writes scenario D: case sensitivity of acronym entries.
    /// </summary>
    /// <param name="context">The run context.</param>
    /// <param name="sets">The evaluation sets (open and gold v2).</param>
    /// <param name="lines">The report lines.</param>
    private static void WriteCaseSensitivity(RunContext context, EvalSet[] sets, List<string> lines)
    {
        var entries = AcronymEntries(context.Lexicon, sets);
        if (entries.Count == 0)
        {
            lines.Add(string.Empty);
            lines.Add("**D, case sensitivity**: no acronym type qualifies in the data.");
            return;
        }

        var insensitive = Glossary.Create(entries, new GlossaryOptions { Mode = GlossaryMode.Hard, Inflect = false });
        var sensitive = Glossary.Create(entries, new GlossaryOptions { Mode = GlossaryMode.Hard, Inflect = false, CaseSensitive = true });
        var spellings = entries.ToDictionary(e => e.Term.ToLowerInvariant(), e => e.Term, StringComparer.Ordinal);
        var acronyms = new CaseCounts();
        var homographs = new CaseCounts();
        foreach (var set in sets)
        {
            foreach (var sentence in set.Sentences.Where(s => s.Scored.Any(i => spellings.ContainsKey(s.Tokens[i].ToLowerInvariant()))))
            {
                var tagged = new[] { context.Tagger.Tag(sentence.Tokens), context.Tagger.Tag(sentence.Tokens, insensitive), context.Tagger.Tag(sentence.Tokens, sensitive) };
                for (var j = 0; j < sentence.Scored.Length; j++)
                {
                    var token = sentence.Tokens[sentence.Scored[j]];
                    if (spellings.TryGetValue(token.ToLowerInvariant(), out var spelling))
                    {
                        CountCase(set, sentence, j, tagged, token == spelling ? acronyms : homographs);
                    }
                }
            }
        }

        var rows = new List<string[]> { CaseRow("acronym tokens (all capitals)", acronyms), CaseRow("homograph tokens (other letter case)", homographs) };
        Table(lines, $"D, case sensitivity: {entries.Count} acronym entries (hard); tokens, right without a glossary, and per glossary the tokens matched, forced to a tag that is not their gold, turned from right to wrong, and right", new[] { "Tokens", "n", "no glossary right", "insensitive matched", "forced wrong", "right to wrong", "right", "sensitive matched", "forced wrong", "right to wrong", "right" }, rows);
    }

    /// <summary>
    ///     Finds the acronym types of the evaluation sets: all-capital tokens of two to five letters whose lower case is a lexicon word and whose majority gold is NOUN or PROPN.
    /// </summary>
    /// <param name="lexicon">The lexicon.</param>
    /// <param name="sets">The evaluation sets.</param>
    /// <returns>The entries (term = the all-capital spelling, tag = the majority gold tag) in ordinal order.</returns>
    internal static List<GlossaryEntry> AcronymEntries(Lexicon lexicon, EvalSet[] sets)
    {
        var counts = new SortedDictionary<string, int[]>(StringComparer.Ordinal);
        var scratch = new byte[lexicon.ScratchSize];
        foreach (var set in sets)
        {
            foreach (var sentence in set.Sentences)
            {
                for (var j = 0; j < sentence.Scored.Length; j++)
                {
                    var token = sentence.Tokens[sentence.Scored[j]];
                    if (sentence.GoldTags[j] >= 0 && IsAcronymShape(token) && TryLexiconTags(lexicon, scratch, token, out _))
                    {
                        counts.TryGetValue(token, out var row);
                        row ??= new int[UposExtensions.Count];
                        counts[token] = row;
                        row[sentence.GoldTags[j]]++;
                    }
                }
            }
        }

        var entries = new List<GlossaryEntry>();
        foreach (var pair in counts)
        {
            var majority = Array.IndexOf(pair.Value, pair.Value.Max());
            if (majority == (int)Upos.Noun || majority == (int)Upos.Propn)
            {
                var entry = TryCreate(new SimTerm(new[] { pair.Key }, (Upos)majority));
                if (entry != null)
                {
                    entries.Add(entry);
                }
            }
        }

        return entries;
    }

    /// <summary>
    ///     Tells whether a token is two to five capital letters.
    /// </summary>
    /// <param name="token">The token.</param>
    /// <returns><c>true</c> for an acronym spelling.</returns>
    private static bool IsAcronymShape(string token) =>
        token.Length >= 2 && token.Length <= 5 && token.All(c => char.IsLetter(c) && char.IsUpper(c));

    /// <summary>
    ///     Counts one scored token of scenario D.
    /// </summary>
    /// <param name="set">The evaluation set.</param>
    /// <param name="sentence">The sentence.</param>
    /// <param name="j">The index of the scored token in the sentence.</param>
    /// <param name="tagged">The sentence tagged without a glossary, with the insensitive and with the sensitive glossary.</param>
    /// <param name="counts">The counters of the kind of token.</param>
    private static void CountCase(EvalSet set, EvalSentence sentence, int j, TaggedSentence[] tagged, CaseCounts counts)
    {
        var index = sentence.Scored[j];
        var gold = sentence.Gold[j];
        var baseRight = set.Label(tagged[0].Tokens[index].Tag) == gold;
        counts.Tokens++;
        counts.BaseCorrect += baseRight ? 1 : 0;
        for (var v = 0; v < 2; v++)
        {
            var result = tagged[v + 1];
            var match = result.GlossaryMatches.FirstOrDefault(m => m.FirstToken <= index && index < m.FirstToken + m.TokenCount);
            var right = set.Label(result.Tokens[index].Tag) == gold;
            counts.Variant[v][0] += match != null ? 1 : 0;
            counts.Variant[v][1] += match != null && !EntryHasGold(match.Entry.Tags, gold, set.Label) ? 1 : 0;
            counts.Variant[v][2] += baseRight && !right ? 1 : 0;
            counts.Variant[v][3] += right ? 1 : 0;
        }
    }

    /// <summary>
    ///     Creates the row of the case table.
    /// </summary>
    /// <param name="name">The kind of token.</param>
    /// <param name="c">The counters.</param>
    /// <returns>The cells.</returns>
    private static string[] CaseRow(string name, CaseCounts c) => new[]
    {
        name,
        c.Tokens.ToString("N0", CultureInfo.InvariantCulture),
        c.BaseCorrect.ToString("N0", CultureInfo.InvariantCulture),
        c.Variant[0][0].ToString("N0", CultureInfo.InvariantCulture),
        c.Variant[0][1].ToString("N0", CultureInfo.InvariantCulture),
        c.Variant[0][2].ToString("N0", CultureInfo.InvariantCulture),
        c.Variant[0][3].ToString("N0", CultureInfo.InvariantCulture),
        c.Variant[1][0].ToString("N0", CultureInfo.InvariantCulture),
        c.Variant[1][1].ToString("N0", CultureInfo.InvariantCulture),
        c.Variant[1][2].ToString("N0", CultureInfo.InvariantCulture),
        c.Variant[1][3].ToString("N0", CultureInfo.InvariantCulture),
    };

    /// <summary>
    ///     Runs and writes scenario G: the three rules for the second token of a phrasal verb.
    /// </summary>
    /// <param name="context">The run context.</param>
    /// <param name="openSet">The open development set.</param>
    /// <param name="open">The open development sentences.</param>
    /// <param name="none">The no-glossary run of the open set.</param>
    /// <param name="lines">The report lines.</param>
    /// <returns>The figures of the alternative rules at the coverage of the pre-registered bar.</returns>
    private static List<RuleCandidate> RunPhrasal(RunContext context, EvalSet openSet, IReadOnlyList<TrainingSentence> open, SystemRun none, List<string> lines)
    {
        var candidates = PhrasalCandidates(open);
        var rows = new List<string[]>();
        var gains = new Dictionary<string, RuleCandidate>(StringComparer.Ordinal);
        var ruleNames = new[] { "R0 default (" + GlossaryConstraints.NonNominalRule + ")", "R1 particles only", "R2 no verb or auxiliary" };
        for (var ci = 0; ci < CoveragePermille.Length; ci++)
        {
            for (var ri = 0; ri < WrongPercent.Length; ri++)
            {
                var number = (ulong)((ci * WrongPercent.Length) + ri + 1);
                var terms = DrawTerms(candidates, CoveragePermille[ci], WrongPercent[ri], context.Seed + 20 + number, WrongPhrasalTags);
                var runs = Enumerable.Range(0, 3).Select(rule => RunSystem(context.Tagger, openSet, PhrasalGlossary(terms, rule), context.Lexicon, ruleNames[rule])).ToArray();
                var categories = runs[0].Categories;
                rows.Add(PhrasalRow(CoveragePermille[ci], WrongPercent[ri], "no glossary", none, categories, null, 0, 0, out _));
                for (var rule = 0; rule < runs.Length; rule++)
                {
                    rows.Add(PhrasalRow(CoveragePermille[ci], WrongPercent[ri], ruleNames[rule], runs[rule], categories, rule == 0 ? null : runs[0], context.Resamples, context.Seed, out var gain));
                    if (rule > 0 && CoveragePermille[ci] == RuleCoverage && WrongPercent[ri] <= 10 && gain != null)
                    {
                        UpdateRuleCandidate(gains, ruleNames[rule], WrongPercent[ri], gain);
                    }
                }
            }
        }

        Table(lines, $"G, phrasal verbs ({candidates.Count} distinct candidates, hard mode): covered-token accuracy (head and second token, %), head and second token separately, and the change against R0 on the covered tokens (points, paired 95% interval)", new[] { "coverage %", "wrong %", "System", "covered tokens", "covered", "head", "second token", "change vs R0 (covered)" }, rows);
        return gains.Values.ToList();
    }

    /// <summary>
    ///     Records the figure of a rule at r = 0 or r = 10% for the bar of scenario G.
    /// </summary>
    /// <param name="gains">The candidates by name.</param>
    /// <param name="name">The name of the rule.</param>
    /// <param name="wrongPercent">The share of wrong entries (0 or 10).</param>
    /// <param name="gain">The covered-token difference against R0.</param>
    private static void UpdateRuleCandidate(Dictionary<string, RuleCandidate> gains, string name, int wrongPercent, BootstrapResult gain)
    {
        if (!gains.TryGetValue(name, out var candidate))
        {
            candidate = new RuleCandidate { Name = name };
            gains[name] = candidate;
        }

        if (wrongPercent == 0)
        {
            candidate.Gain0 = gain.Difference;
            candidate.Low0 = gain.Low;
        }
        else
        {
            candidate.Gain10 = gain.Difference;
        }
    }

    /// <summary>
    ///     Creates the hard glossary of scenario G for one rule: the pair is a verb with the first token as head, with inflection on.
    /// </summary>
    /// <param name="terms">The terms.</param>
    /// <param name="rule">0 for the library default, 1 for particles only, 2 for no verb or auxiliary.</param>
    /// <returns>The glossary.</returns>
    private static Glossary PhrasalGlossary(IEnumerable<SimTerm> terms, int rule)
    {
        var entries = new List<GlossaryEntry>();
        foreach (var term in terms)
        {
            var entry = TryCreate(term)?.WithHead(0).WithInflection(true);
            if (entry != null && rule == 1)
            {
                entry = entry.WithTokenTags(1, UposSet.Of(Upos.Adp, Upos.Adv, Upos.Part));
            }
            else if (entry != null && rule == 2)
            {
                entry = entry.WithTokenTags(1, UposSet.All.Remove(Upos.Verb).Remove(Upos.Aux));
            }

            if (entry != null)
            {
                entries.Add(entry);
            }
        }

        return Glossary.Create(entries, new GlossaryOptions { Mode = GlossaryMode.Hard, Inflect = false });
    }

    /// <summary>
    ///     Creates the row of the table of scenario G for a system.
    /// </summary>
    /// <param name="coveragePermille">The coverage.</param>
    /// <param name="wrongPercent">The share of wrong entries.</param>
    /// <param name="name">The name of the system.</param>
    /// <param name="run">The run.</param>
    /// <param name="categories">The categories of the scenario.</param>
    /// <param name="reference">The R0 run for the paired difference, or <c>null</c>.</param>
    /// <param name="resamples">The number of bootstrap resamples.</param>
    /// <param name="seed">The seed of the bootstrap.</param>
    /// <param name="gain">Receives the paired difference on the covered tokens, or <c>null</c> without a reference.</param>
    /// <returns>The cells.</returns>
    private static string[] PhrasalRow(int coveragePermille, int wrongPercent, string name, SystemRun run, GlossaryCategory[] categories, SystemRun? reference, int resamples, ulong seed, out BootstrapResult? gain)
    {
        gain = null;
        var covered = Tally(run, categories, IsCovered, out var coveredCorrect);
        var head = Tally(run, categories, c => c == GlossaryCategory.HeadRight || c == GlossaryCategory.HeadWrong, out var headCorrect);
        var second = Tally(run, categories, c => c == GlossaryCategory.NonHead, out var secondCorrect);
        var change = "-";
        if (reference != null)
        {
            Bootstrap.Compare(Subset(run.Outcomes, categories), Subset(reference.Outcomes, categories), resamples, seed, out var accuracy, out _);
            gain = accuracy;
            change = Interval(accuracy);
        }

        return new[]
        {
            (coveragePermille / 10).ToString(CultureInfo.InvariantCulture),
            wrongPercent.ToString(CultureInfo.InvariantCulture),
            name,
            covered.ToString("N0", CultureInfo.InvariantCulture),
            Share(coveredCorrect, covered),
            Share(headCorrect, head),
            Share(secondCorrect, second),
            change,
        };
    }

    /// <summary>
    ///     Selects the outcomes of the covered tokens.
    /// </summary>
    /// <param name="outcomes">The outcomes of a run.</param>
    /// <param name="categories">The categories of the scenario.</param>
    /// <returns>The outcomes of the covered tokens in order.</returns>
    private static List<Outcome> Subset(Outcome[] outcomes, GlossaryCategory[] categories) =>
        outcomes.Where((_, i) => IsCovered(categories[i])).ToList();

    /// <summary>
    ///     Applies the two pre-registered rules to the results and writes the decisions.
    /// </summary>
    /// <param name="lines">The report lines.</param>
    /// <param name="scenarios">The open scenario results.</param>
    /// <param name="rules">The figures of the alternative non-head rules.</param>
    private static void WriteDecisions(List<string> lines, OpenScenarios scenarios, List<RuleCandidate> rules)
    {
        // The strength rule on the primary scenario: AB at 30% coverage (r = 0, 10, 30%)
        var ab = scenarios.Results["AB"];
        var candidates = new List<StrengthCandidate>();
        for (var s = 1; s <= StrengthGrid.Length; s++)
        {
            candidates.Add(new StrengthCandidate
            {
                Strength = StrengthGrid[s - 1],
                AccuracyDifference10 = ab[1].Cells[s].Accuracy.Difference,
                SwapDifference10 = ab[1].Cells[s].Swaps.Difference,
                AccuracyDifference30 = ab[2].Cells[s].Accuracy.Difference,
                CoveredAccuracy0 = NaNToZero(Acc(ab[0].Systems[s], ab[0].Categories, IsCovered)),
            });
        }

        var chosen = ChooseSoftStrength(candidates, out var admissible);
        var rows = candidates.Select(c => new[]
        {
            c.Strength.ToString("0.#", CultureInfo.InvariantCulture),
            Sign(c.AccuracyDifference10),
            Sign(c.SwapDifference10),
            Sign(c.AccuracyDifference30),
            Pct(c.CoveredAccuracy0),
            IsAdmissible(c) ? "yes" : "no",
        });
        Table(lines, "Soft strength rule, scenario AB at 30% coverage: overall accuracy change at r=10% (points), swap change at r=10% (per 1,000), overall accuracy change at r=30% (points), covered-token accuracy at r=0 (%), admissible", new[] { "Strength", "accuracy r=10", "swaps r=10", "accuracy r=30", "covered r=0", "admissible" }, rows);
        lines.Add(string.Empty);
        lines.Add(string.Format(
            CultureInfo.InvariantCulture,
            "**Decision (soft strength)**: {0}; chosen strength {1}; the constant GlossaryOptions.DefaultSoftStrength is {2} and {3} in the grid.",
            admissible ? "at least one strength is admissible" : "no strength is admissible, so the smallest of the grid applies",
            chosen.ToString("0.#", CultureInfo.InvariantCulture),
            GlossaryOptions.DefaultSoftStrength.ToString("0.#", CultureInfo.InvariantCulture),
            StrengthGrid.Contains(GlossaryOptions.DefaultSoftStrength) ? "is" : "is NOT"));

        var rule = ChooseNonHeadRule(rules);
        foreach (var candidate in rules)
        {
            lines.Add(string.Format(CultureInfo.InvariantCulture, "G bar, {0}: gain at r=0 {1} points (interval low {2}), change at r=10% {3} points.", candidate.Name, Sign(candidate.Gain0), Sign(candidate.Low0), Sign(candidate.Gain10)));
        }

        lines.Add(rule == null
            ? "**Decision (non-head rule)**: no rule meets the pre-registered bar against R0 (the default of the library at the time of the run), so R0 stays."
            : $"**Decision (non-head rule)**: {rule} meets the pre-registered bar and replaces the default.");
    }

    /// <summary>
    ///     Computes the accuracy over the tokens of the categories that a predicate picks.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <param name="categories">The categories of the scenario.</param>
    /// <param name="pick">Tells whether a category is counted.</param>
    /// <returns>The accuracy in percent, or NaN when no token is in the categories.</returns>
    private static double Acc(SystemRun run, GlossaryCategory[] categories, Func<GlossaryCategory, bool> pick)
    {
        var count = Tally(run, categories, pick, out var correct);
        return count == 0 ? double.NaN : 100.0 * correct / count;
    }

    /// <summary>
    ///     Replaces NaN (an empty category) by zero, so that a rule can compare it.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The value, or zero for NaN.</returns>
    private static double NaNToZero(double value) => double.IsNaN(value) ? 0 : value;

    /// <summary>
    ///     Computes the accuracy over the flagged tokens.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <param name="flags">The flag of every scored token.</param>
    /// <returns>The accuracy in percent, or NaN when no token is flagged.</returns>
    private static double AccFlag(SystemRun run, bool[] flags)
    {
        var count = flags.Count(f => f);
        return count == 0 ? double.NaN : 100.0 * run.Outcomes.Where((o, i) => flags[i] && o.Correct).Count() / count;
    }

    /// <summary>
    ///     Formats the confident swaps of a run per 1,000 noun-verb decisions of the no-glossary run.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <param name="none">The no-glossary run.</param>
    /// <returns>The swaps per 1,000 baseline decisions with two decimals.</returns>
    private static string SwapsPer1000(SystemRun run, SystemRun none)
    {
        var decisions = none.Outcomes.Count(o => o.NounVerb);
        return decisions == 0 ? "-" : Pct(1000.0 * run.Swaps.Count(s => s) / decisions);
    }

    /// <summary>
    ///     Formats a count as a share of a total in percent.
    /// </summary>
    /// <param name="part">The part.</param>
    /// <param name="total">The total.</param>
    /// <returns>The share with two decimals, or a dash for an empty total.</returns>
    private static string Share(int part, int total) => total == 0 ? "-" : Pct(100.0 * part / total);

    /// <summary>
    ///     Formats a number with two decimals (a dash for NaN).
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The text.</returns>
    private static string Pct(double value) => double.IsNaN(value) ? "-" : value.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>
    ///     Formats a signed number with two decimals.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The text.</returns>
    private static string Sign(double value) => value.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);

    /// <summary>
    ///     Formats a paired difference with its 95 percent interval.
    /// </summary>
    /// <param name="result">The bootstrap result.</param>
    /// <returns>The text.</returns>
    private static string Interval(BootstrapResult result) => $"{Sign(result.Difference)} [{Sign(result.Low)}, {Sign(result.High)}]";

    /// <summary>
    ///     Adds a titled Markdown table to the report.
    /// </summary>
    /// <param name="lines">The report lines.</param>
    /// <param name="title">The title.</param>
    /// <param name="header">The column names.</param>
    /// <param name="rows">The rows.</param>
    private static void Table(List<string> lines, string title, string[] header, IEnumerable<string[]> rows)
    {
        lines.Add(string.Empty);
        lines.Add("**" + title + "**");
        lines.Add(string.Empty);
        lines.Add("| " + string.Join(" | ", header) + " |");
        lines.Add("| --- |" + string.Concat(Enumerable.Repeat(" ---: |", header.Length - 1)));
        lines.AddRange(rows.Select(row => "| " + string.Join(" | ", row) + " |"));
    }
}
