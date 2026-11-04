using System.Globalization;

namespace DemaConsulting.PartOfSpeech.Trainer;

/// <summary>
///     The category of a scored token in a glossary scenario.
/// </summary>
internal enum GlossaryCategory : byte
{
    /// <summary>The token is in a sentence without a match.</summary>
    NoMatch = 0,

    /// <summary>The token is not covered and lies before the first match of its sentence.</summary>
    Before = 1,

    /// <summary>The token is not covered and is the token right after the nearest preceding match.</summary>
    Plus1 = 2,

    /// <summary>The token is not covered and is the second token after the nearest preceding match.</summary>
    Plus2 = 3,

    /// <summary>The token is not covered and is three or more tokens after the nearest preceding match.</summary>
    Later = 4,

    /// <summary>The token is the head of a match and the entry contains its gold tag.</summary>
    HeadRight = 5,

    /// <summary>The token is the head of a match and the entry does not contain its gold tag.</summary>
    HeadWrong = 6,

    /// <summary>The token belongs to a match but is not its head.</summary>
    NonHead = 7,
}

/// <summary>
///     A simulated glossary term: its tokens and the tag that the (possibly wrong) entry gives to its head.
/// </summary>
internal sealed class SimTerm
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="SimTerm"/> class.
    /// </summary>
    /// <param name="tokens">The lower-cased tokens of the term.</param>
    /// <param name="tag">The tag of the entry.</param>
    public SimTerm(string[] tokens, Upos tag)
    {
        Tokens = tokens;
        Tag = tag;
    }

    /// <summary>
    ///     Gets the tokens of the term.
    /// </summary>
    public string[] Tokens { get; }

    /// <summary>
    ///     Gets the tag of the entry.
    /// </summary>
    public Upos Tag { get; }

    /// <summary>
    ///     Gets the term as the text of a glossary entry (the tokens joined by single spaces).
    /// </summary>
    public string Text => string.Join(" ", Tokens);
}

/// <summary>
///     One sentence of an evaluation set: its tokens and the tokens that are scored with their gold labels.
/// </summary>
internal sealed class EvalSentence
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="EvalSentence"/> class.
    /// </summary>
    /// <param name="tokens">The tokens of the sentence.</param>
    /// <param name="documentId">The id of the document (the unit of the bootstrap).</param>
    /// <param name="scored">The indices of the scored tokens.</param>
    /// <param name="gold">The gold label of every scored token.</param>
    /// <param name="goldTags">The gold UPOS ordinal of every scored token, or -1 when the label has no single tag.</param>
    public EvalSentence(string[] tokens, string documentId, int[] scored, string[] gold, sbyte[] goldTags)
    {
        Tokens = tokens;
        DocumentId = documentId;
        Scored = scored;
        Gold = gold;
        GoldTags = goldTags;
    }

    /// <summary>
    ///     Gets the tokens of the sentence.
    /// </summary>
    public string[] Tokens { get; }

    /// <summary>
    ///     Gets the id of the document.
    /// </summary>
    public string DocumentId { get; }

    /// <summary>
    ///     Gets the indices of the scored tokens.
    /// </summary>
    public int[] Scored { get; }

    /// <summary>
    ///     Gets the gold label of every scored token.
    /// </summary>
    public string[] Gold { get; }

    /// <summary>
    ///     Gets the gold UPOS ordinal of every scored token (-1 when unknown).
    /// </summary>
    public sbyte[] GoldTags { get; }
}

/// <summary>
///     An evaluation set: development sentences with the labels of their scored tokens and the way to compare tags with labels.
/// </summary>
/// <remarks>
///     The set is built from sentences that were already loaded through <see cref="DevSets"/>; it never reads a file.
///     Open development sentences score every token with a UPOS label; the gold v2 set scores the one target of every
///     sentence with its class label (the targets <c>to</c> and <c>not</c> are left out, as in every other evaluation).
/// </remarks>
internal sealed class EvalSet
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="EvalSet"/> class.
    /// </summary>
    /// <param name="sentences">The sentences.</param>
    /// <param name="label">Maps a tag to its label.</param>
    /// <param name="nounLabel">The label that stands for nouns.</param>
    /// <param name="verbLabel">The label that stands for verbs.</param>
    private EvalSet(List<EvalSentence> sentences, Func<Upos, string> label, string nounLabel, string verbLabel)
    {
        Sentences = sentences;
        Label = label;
        NounLabel = nounLabel;
        VerbLabel = verbLabel;
        ScoredCount = sentences.Sum(s => s.Scored.Length);
    }

    /// <summary>
    ///     Gets the sentences (only sentences with at least one scored token).
    /// </summary>
    public List<EvalSentence> Sentences { get; }

    /// <summary>
    ///     Gets the function that maps a tag to the label of this set.
    /// </summary>
    public Func<Upos, string> Label { get; }

    /// <summary>
    ///     Gets the label that stands for nouns.
    /// </summary>
    public string NounLabel { get; }

    /// <summary>
    ///     Gets the label that stands for verbs.
    /// </summary>
    public string VerbLabel { get; }

    /// <summary>
    ///     Gets the number of scored tokens.
    /// </summary>
    public int ScoredCount { get; }

    /// <summary>
    ///     Creates the set of the open development sentences: every token with a gold tag is scored with its UPOS label.
    /// </summary>
    /// <param name="sentences">The sentences.</param>
    /// <returns>The set.</returns>
    public static EvalSet FromOpen(IReadOnlyList<TrainingSentence> sentences)
    {
        ArgumentNullException.ThrowIfNull(sentences);
        var list = new List<EvalSentence>();
        foreach (var sentence in sentences)
        {
            var scored = Enumerable.Range(0, sentence.Tokens.Length).Where(i => sentence.Gold[i] >= 0).ToArray();
            if (scored.Length > 0)
            {
                list.Add(new EvalSentence(
                    sentence.Tokens,
                    "open:" + sentence.SourceId,
                    scored,
                    scored.Select(i => ((Upos)sentence.Gold[i]).ToTagString()).ToArray(),
                    scored.Select(i => sentence.Gold[i]).ToArray()));
            }
        }

        return new EvalSet(list, t => t.ToTagString(), "NOUN", "VERB");
    }

    /// <summary>
    ///     Creates the set of the gold v2 development targets: the target of every sentence is scored with its class label.
    /// </summary>
    /// <param name="targets">The targets.</param>
    /// <returns>The set.</returns>
    public static EvalSet FromGold(IReadOnlyList<GoldTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        var list = new List<EvalSentence>();
        foreach (var target in targets.Where(t => !t.IsConventionWord))
        {
            sbyte tag = -1;
            if (target.GoldClass == "Noun")
            {
                tag = (sbyte)Upos.Noun;
            }
            else if (target.GoldClass == "Verb")
            {
                tag = (sbyte)Upos.Verb;
            }

            list.Add(new EvalSentence(target.Tokens, "gold:" + target.DocumentId, new[] { target.Target }, new[] { target.GoldClass }, new[] { tag }));
        }

        return new EvalSet(list, DevSets.GoldClassOf, "Noun", "Verb");
    }
}

/// <summary>
///     The judged tokens of one system (no glossary, hard, or soft at one strength) over an evaluation set.
/// </summary>
internal sealed class SystemRun
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="SystemRun"/> class.
    /// </summary>
    /// <param name="name">The name of the system.</param>
    /// <param name="count">The number of scored tokens.</param>
    public SystemRun(string name, int count)
    {
        Name = name;
        Outcomes = new Outcome[count];
        Swaps = new bool[count];
        Tags = new sbyte[count];
        Categories = new GlossaryCategory[count];
        AtRisk = new bool[count];
    }

    /// <summary>
    ///     Gets the name of the system.
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     Gets the outcome of every scored token (the E0 judgment).
    /// </summary>
    public Outcome[] Outcomes { get; }

    /// <summary>
    ///     Gets whether each scored token is a confident noun-verb swap, whether or not both were candidates.
    /// </summary>
    public bool[] Swaps { get; }

    /// <summary>
    ///     Gets the chosen tag ordinal of every scored token.
    /// </summary>
    public sbyte[] Tags { get; }

    /// <summary>
    ///     Gets the category of every scored token (all <see cref="GlossaryCategory.NoMatch"/> for a run without a glossary).
    /// </summary>
    public GlossaryCategory[] Categories { get; }

    /// <summary>
    ///     Gets whether each scored token is the first token of a match whose first token the lexicon lists with VERB.
    /// </summary>
    public bool[] AtRisk { get; }
}

/// <summary>
///     The paired differences (system minus no glossary) of one system in one scenario.
/// </summary>
internal sealed class PairedCell
{
    /// <summary>
    ///     Gets or sets the accuracy difference over all scored tokens in points.
    /// </summary>
    public BootstrapResult Accuracy { get; set; } = new();

    /// <summary>
    ///     Gets or sets the difference of the harmful errors per 1,000 baseline noun-verb decisions.
    /// </summary>
    public BootstrapResult Harmful { get; set; } = new();

    /// <summary>
    ///     Gets or sets the difference of the confident swaps per 1,000 baseline noun-verb decisions.
    /// </summary>
    public BootstrapResult Swaps { get; set; } = new();
}

/// <summary>
///     The runs of all systems of one simulated glossary on one evaluation set.
/// </summary>
internal sealed class ScenarioResult
{
    /// <summary>
    ///     Gets or sets the share of distinct terms in the glossary, in permille.
    /// </summary>
    public int CoveragePermille { get; set; }

    /// <summary>
    ///     Gets or sets the share of wrong entries, in percent.
    /// </summary>
    public int WrongPercent { get; set; }

    /// <summary>
    ///     Gets or sets the number of entries of the glossary.
    /// </summary>
    public int EntryCount { get; set; }

    /// <summary>
    ///     Gets or sets the run without a glossary.
    /// </summary>
    public SystemRun None { get; set; } = new("no glossary", 0);

    /// <summary>
    ///     Gets or sets the hard run followed by the soft runs in the order of <see cref="GlossaryEvaluation.StrengthGrid"/>.
    /// </summary>
    public SystemRun[] Systems { get; set; } = Array.Empty<SystemRun>();

    /// <summary>
    ///     Gets or sets the category of every scored token (identical for every system, because a match does not depend on the mode).
    /// </summary>
    public GlossaryCategory[] Categories { get; set; } = Array.Empty<GlossaryCategory>();

    /// <summary>
    ///     Gets or sets the at-risk flag of every scored token.
    /// </summary>
    public bool[] AtRisk { get; set; } = Array.Empty<bool>();

    /// <summary>
    ///     Gets or sets the paired differences of the systems (the same order as <see cref="Systems"/>).
    /// </summary>
    public PairedCell[] Cells { get; set; } = Array.Empty<PairedCell>();
}

/// <summary>
///     The figures of one soft strength that the pre-registered rule needs.
/// </summary>
internal sealed class StrengthCandidate
{
    /// <summary>
    ///     Gets or sets the strength.
    /// </summary>
    public double Strength { get; set; }

    /// <summary>
    ///     Gets or sets the overall accuracy difference to the no-glossary run at r = 10%, in points.
    /// </summary>
    public double AccuracyDifference10 { get; set; }

    /// <summary>
    ///     Gets or sets the difference of the confident swaps at r = 10%, per 1,000 baseline noun-verb decisions.
    /// </summary>
    public double SwapDifference10 { get; set; }

    /// <summary>
    ///     Gets or sets the overall accuracy difference to the no-glossary run at r = 30%, in points.
    /// </summary>
    public double AccuracyDifference30 { get; set; }

    /// <summary>
    ///     Gets or sets the accuracy over the covered tokens at r = 0, in percent.
    /// </summary>
    public double CoveredAccuracy0 { get; set; }
}

/// <summary>
///     The figures of one alternative non-head rule (scenario G) that the pre-registered bar needs.
/// </summary>
internal sealed class RuleCandidate
{
    /// <summary>
    ///     Gets or sets the name of the rule.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the covered-token accuracy difference to the default at r = 0, in points.
    /// </summary>
    public double Gain0 { get; set; }

    /// <summary>
    ///     Gets or sets the lower end of the paired 95 percent interval of <see cref="Gain0"/>.
    /// </summary>
    public double Low0 { get; set; }

    /// <summary>
    ///     Gets or sets the covered-token accuracy difference to the default at r = 10%, in points.
    /// </summary>
    public double Gain10 { get; set; }
}

/// <summary>
///     The dev-only evaluation of the <c>Glossary</c> API (phase 4, E9): simulated glossaries on the open and gold v2
///     development sets, tagged through the real <c>Tagger.Tag(IReadOnlyList&lt;string&gt;, Glossary)</c> path.
/// </summary>
/// <remarks>
///     <para>
///         The protocol is written in <c>RUN-LOG.md</c> (section E9) before the first run: the scenarios A (noun
///         compounds), B (single-word technical words, with a parity check against the phase-3 path), C (inflection
///         from gold lemmas), D (acronym case sensitivity), E (gold v2 head effect), F (history effect by distance
///         after a match) and G (the non-head rule for non-nominal terms), the systems (no glossary, hard, soft at
///         the strengths of <see cref="StrengthGrid"/>), the metrics and the two pre-registered rules.
///     </para>
///     <para>
///         <strong>Dev data only:</strong> the entry point takes sentences that were loaded through <see cref="DevSets"/>
///         (which is guarded by <see cref="DataGuard"/>) and has no path parameter, so it cannot open a file at all.
///         Deterministic for a seed. Stateless and thread-safe (it is single-threaded).
///     </para>
/// </remarks>
internal static partial class GlossaryEvaluation
{
    /// <summary>
    ///     The soft strengths to evaluate, in units of the model temperature.
    /// </summary>
    public static readonly double[] StrengthGrid = { 0.5, 1, 1.5, 2, 3, 4 };

    /// <summary>
    ///     The shares of distinct terms in a simulated glossary, in permille.
    /// </summary>
    public static readonly int[] CoveragePermille = { 300, 1000 };

    /// <summary>
    ///     The shares of wrong entries, in percent.
    /// </summary>
    public static readonly int[] WrongPercent = { 0, 10, 30 };

    /// <summary>
    ///     The coverage of the primary scenario, in permille.
    /// </summary>
    public const int PrimaryCoverage = 300;

    /// <summary>
    ///     The coverage of scenario G that the pre-registered bar uses, in permille.
    /// </summary>
    public const int RuleCoverage = 1000;

    /// <summary>
    ///     The largest loss of overall accuracy at 30 percent wrong entries that an admissible strength may have, in points.
    /// </summary>
    public const double AdmissibleLoss30 = 0.5;

    /// <summary>
    ///     The tie width of the strength rule, in points of covered-token accuracy.
    /// </summary>
    public const double StrengthTie = 0.1;

    /// <summary>
    ///     The gain that a non-head rule needs at r = 0, in points.
    /// </summary>
    public const double RuleGain = 0.5;

    /// <summary>
    ///     The tags that count as open class when a lexicon entry is checked.
    /// </summary>
    private static readonly UposSet OpenClass = UposSet.Of(Upos.Noun, Upos.Propn, Upos.Verb, Upos.Adj, Upos.Adv);

    /// <summary>
    ///     Gets the name of a system by its position in <see cref="ScenarioResult.Systems"/> (0 is hard).
    /// </summary>
    /// <param name="index">The position.</param>
    /// <returns>The name.</returns>
    public static string SystemName(int index) =>
        index == 0 ? "hard" : "soft " + StrengthGrid[index - 1].ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>
    ///     Applies the pre-registered rule for the soft strength.
    /// </summary>
    /// <remarks>
    ///     A strength is admissible if the accuracy difference at r = 10% is at least 0, the swap difference at r = 10% is
    ///     at most 0 and the accuracy difference at r = 30% is at least <c>-0.5</c>. Among the admissible strengths the
    ///     one with the highest covered-token accuracy at r = 0 wins; strengths within 0.1 points of the best are tied and
    ///     the smallest wins. If none is admissible the smallest strength of the candidates is chosen.
    /// </remarks>
    /// <param name="candidates">The figures of every strength.</param>
    /// <param name="admissible">Receives whether at least one strength was admissible.</param>
    /// <returns>The chosen strength.</returns>
    /// <exception cref="ArgumentException">There is no candidate.</exception>
    public static double ChooseSoftStrength(IReadOnlyList<StrengthCandidate> candidates, out bool admissible)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0)
        {
            throw new ArgumentException("At least one strength is needed.", nameof(candidates));
        }

        var ok = candidates.Where(IsAdmissible).ToList();
        admissible = ok.Count > 0;
        if (!admissible)
        {
            return candidates.Min(c => c.Strength);
        }

        var best = ok.Max(c => c.CoveredAccuracy0);
        var tied = ok.Where(c => best - c.CoveredAccuracy0 <= StrengthTie + 1e-9).ToList();
        return (tied.Count > 0 ? tied : ok).Min(c => c.Strength);
    }

    /// <summary>
    ///     Tells whether a strength is admissible under the pre-registered rule.
    /// </summary>
    /// <param name="candidate">The figures of the strength.</param>
    /// <returns><c>true</c> if all three conditions hold.</returns>
    public static bool IsAdmissible(StrengthCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return candidate.AccuracyDifference10 >= 0
            && candidate.SwapDifference10 <= 0
            && candidate.AccuracyDifference30 >= -AdmissibleLoss30;
    }

    /// <summary>
    ///     Applies the pre-registered bar of scenario G: a rule replaces the default only with a gain of at least 0.5 points
    ///     at r = 0 whose paired interval excludes zero, and no loss at r = 10%.
    /// </summary>
    /// <param name="candidates">The figures of the alternative rules.</param>
    /// <returns>The name of the adopted rule (the larger gain at r = 0 if several qualify), or <c>null</c> to keep the default.</returns>
    public static string? ChooseNonHeadRule(IReadOnlyList<RuleCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        return candidates
            .Where(c => c.Gain0 >= RuleGain && c.Low0 > 0 && c.Gain10 >= 0)
            .OrderByDescending(c => c.Gain0)
            .Select(c => c.Name)
            .FirstOrDefault();
    }

    /// <summary>
    ///     Finds the distinct noun compounds of the open development sentences (scenario A).
    /// </summary>
    /// <remarks>
    ///     A compound is the longest suffix of two or three tokens of a maximal run of gold NOUN, PROPN and ADJ tokens that
    ///     ends in NOUN or PROPN, whose tokens are all alphabetic (a hyphen is allowed) and listed in the lexicon with an
    ///     open-class tag. The tag is the gold tag of the last token of the first occurrence.
    /// </remarks>
    /// <param name="open">The open development sentences.</param>
    /// <param name="lexicon">The lexicon.</param>
    /// <returns>The distinct terms in ordinal order of their text.</returns>
    public static List<SimTerm> CompoundCandidates(IReadOnlyList<TrainingSentence> open, Lexicon lexicon)
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(lexicon);
        var found = new SortedDictionary<string, SimTerm>(StringComparer.Ordinal);
        var scratch = new byte[lexicon.ScratchSize];
        foreach (var sentence in open)
        {
            var end = 0;
            while (end < sentence.Tokens.Length)
            {
                if (!IsNominalModifier(sentence.Gold[end]))
                {
                    end++;
                    continue;
                }

                var start = end;
                while (end < sentence.Tokens.Length && IsNominalModifier(sentence.Gold[end]))
                {
                    end++;
                }

                AddCompound(found, sentence, start, end - 1, lexicon, scratch);
            }
        }

        return found.Values.ToList();
    }

    /// <summary>
    ///     Finds the distinct phrasal-verb candidates of the open development sentences (scenario G): a gold VERB directly
    ///     followed by a gold ADP, ADV or PART, both alphabetic.
    /// </summary>
    /// <param name="open">The open development sentences.</param>
    /// <returns>The distinct terms (tag VERB) in ordinal order of their text.</returns>
    public static List<SimTerm> PhrasalCandidates(IReadOnlyList<TrainingSentence> open)
    {
        ArgumentNullException.ThrowIfNull(open);
        var found = new SortedDictionary<string, SimTerm>(StringComparer.Ordinal);
        foreach (var sentence in open)
        {
            for (var i = 0; i + 1 < sentence.Tokens.Length; i++)
            {
                var next = sentence.Gold[i + 1];
                var particle = next == (int)Upos.Adp || next == (int)Upos.Adv || next == (int)Upos.Part;
                if (sentence.Gold[i] == (int)Upos.Verb && particle && IsAlphabetic(sentence.Tokens[i]) && IsAlphabetic(sentence.Tokens[i + 1]))
                {
                    var tokens = new[] { sentence.Tokens[i].ToLowerInvariant(), sentence.Tokens[i + 1].ToLowerInvariant() };
                    var key = string.Join(" ", tokens);
                    if (!found.ContainsKey(key))
                    {
                        found[key] = new SimTerm(tokens, Upos.Verb);
                    }
                }
            }
        }

        return found.Values.ToList();
    }

    /// <summary>
    ///     Finds the distinct target forms of the gold v2 Noun and Verb targets (scenario E), with the majority class as tag.
    /// </summary>
    /// <param name="gold">The gold v2 development targets.</param>
    /// <returns>The distinct terms in ordinal order of their text; a tie between the classes is a noun.</returns>
    public static List<SimTerm> HeadCandidates(IReadOnlyList<GoldTarget> gold)
    {
        ArgumentNullException.ThrowIfNull(gold);
        var counts = new SortedDictionary<string, int[]>(StringComparer.Ordinal);
        foreach (var target in gold.Where(t => !t.IsConventionWord && (t.GoldClass == "Noun" || t.GoldClass == "Verb")))
        {
            var key = target.Form.ToLowerInvariant();
            if (!counts.TryGetValue(key, out var row))
            {
                row = new int[2];
                counts[key] = row;
            }

            row[target.GoldClass == "Noun" ? 0 : 1]++;
        }

        return counts.Select(p => new SimTerm(new[] { p.Key }, p.Value[0] >= p.Value[1] ? Upos.Noun : Upos.Verb)).ToList();
    }

    /// <summary>
    ///     Draws the terms of a simulated glossary: a deterministic share of the candidates, a share of them with a wrong tag.
    /// </summary>
    /// <param name="candidates">The candidates in ordinal order.</param>
    /// <param name="coveragePermille">The share of candidates in the glossary.</param>
    /// <param name="wrongPercent">The share of entries with a wrong tag.</param>
    /// <param name="seed">The seed.</param>
    /// <param name="wrongTags">Gives the tags that a wrong entry may take instead of the right one.</param>
    /// <returns>The terms of the glossary, with their entry tags.</returns>
    public static List<SimTerm> DrawTerms(IReadOnlyList<SimTerm> candidates, int coveragePermille, int wrongPercent, ulong seed, Func<SimTerm, Upos[]> wrongTags)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(wrongTags);
        var random = new DeterministicRandom(seed);
        var order = new int[candidates.Count];
        for (var i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        random.Shuffle(order);
        var take = (int)((long)candidates.Count * coveragePermille / 1000);
        var result = new List<SimTerm>(take);
        for (var n = 0; n < take; n++)
        {
            var term = candidates[order[n]];
            var tag = term.Tag;
            if (random.NextInt(100) < wrongPercent)
            {
                var others = wrongTags(term);
                tag = others[random.NextInt(others.Length)];
            }

            result.Add(new SimTerm(term.Tokens, tag));
        }

        return result;
    }

    /// <summary>
    ///     The tags that a wrong compound entry may take: a verb, an adjective, or the other kind of noun.
    /// </summary>
    /// <param name="term">The term.</param>
    /// <returns>The tags.</returns>
    public static Upos[] WrongCompoundTags(SimTerm term) =>
        new[] { Upos.Verb, Upos.Adj, term.Tag == Upos.Noun ? Upos.Propn : Upos.Noun };

    /// <summary>
    ///     The tags that a wrong phrasal-verb entry may take: a noun or an adjective.
    /// </summary>
    /// <param name="term">The term.</param>
    /// <returns>The tags.</returns>
    public static Upos[] WrongPhrasalTags(SimTerm term) => new[] { Upos.Noun, Upos.Adj };

    /// <summary>
    ///     The tag that a wrong head entry takes: the other class of the noun-verb pair.
    /// </summary>
    /// <param name="term">The term.</param>
    /// <returns>The tag.</returns>
    public static Upos[] WrongHeadTags(SimTerm term) => new[] { term.Tag == Upos.Noun ? Upos.Verb : Upos.Noun };

    /// <summary>
    ///     Draws the single-word glossary of scenario B with the phase-3 routine and seeds.
    /// </summary>
    /// <param name="eligible">The eligible types of <see cref="GlossaryProbe.EligibleTypes"/>.</param>
    /// <param name="coveragePermille">The coverage.</param>
    /// <param name="wrongPercent">The share of wrong entries.</param>
    /// <param name="seed">The seed of the scenario.</param>
    /// <returns>The terms in ordinal order of their text.</returns>
    public static List<SimTerm> SingleWordTerms(List<GlossaryProbe.EligibleType> eligible, int coveragePermille, int wrongPercent, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(eligible);
        var dictionary = GlossaryProbe.BuildGlossary(eligible, coveragePermille, wrongPercent, seed);
        return dictionary.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new SimTerm(new[] { p.Key }, p.Value)).ToList();
    }

    /// <summary>
    ///     Turns simulated terms into glossary entries; a term that the glossary cannot represent as written is skipped.
    /// </summary>
    /// <remarks>
    ///     A term is skipped when the entry cannot be created (for example, no letter or digit) or when the tokenizer
    ///     splits it into another number of tokens than the simulation assumed; a repeated term is skipped too.
    /// </remarks>
    /// <param name="terms">The terms.</param>
    /// <param name="skipped">Receives the number of skipped terms.</param>
    /// <param name="skippedTexts">Receives the text of every skipped term when not <c>null</c>.</param>
    /// <returns>The entries in the order of the terms.</returns>
    public static List<GlossaryEntry> ToEntries(IEnumerable<SimTerm> terms, out int skipped, ISet<string>? skippedTexts = null)
    {
        ArgumentNullException.ThrowIfNull(terms);
        var entries = new List<GlossaryEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        skipped = 0;
        foreach (var term in terms)
        {
            var entry = TryCreate(term);
            if (entry == null || !seen.Add(term.Text))
            {
                skipped++;
                skippedTexts?.Add(term.Text);
            }
            else
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    /// <summary>
    ///     Creates a glossary for one system of a scenario: the entries with a mode and a strength; no inflection unless an entry says so.
    /// </summary>
    /// <param name="entries">The entries.</param>
    /// <param name="mode">The mode of the glossary.</param>
    /// <param name="strength">The soft strength (unused in hard mode).</param>
    /// <returns>The glossary.</returns>
    public static Glossary MakeGlossary(IEnumerable<GlossaryEntry> entries, GlossaryMode mode, double strength) =>
        Glossary.Create(entries, new GlossaryOptions { Mode = mode, SoftStrength = strength, Inflect = false });

    /// <summary>
    ///     Tags an evaluation set with a glossary (or without) and judges every scored token.
    /// </summary>
    /// <param name="tagger">The tagger (created with all alternatives).</param>
    /// <param name="set">The evaluation set.</param>
    /// <param name="glossary">The glossary, or <c>null</c> for the no-glossary system.</param>
    /// <param name="lexicon">The lexicon.</param>
    /// <param name="name">The name of the system.</param>
    /// <returns>The judged tokens.</returns>
    public static SystemRun RunSystem(Tagger tagger, EvalSet set, Glossary? glossary, Lexicon lexicon, string name)
    {
        ArgumentNullException.ThrowIfNull(tagger);
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(lexicon);
        var run = new SystemRun(name, set.ScoredCount);
        var scratch = new byte[lexicon.ScratchSize];
        var offset = 0;
        foreach (var sentence in set.Sentences)
        {
            var tagged = glossary == null ? tagger.Tag(sentence.Tokens) : tagger.Tag(sentence.Tokens, glossary);
            for (var j = 0; j < sentence.Scored.Length; j++)
            {
                var token = tagged.Tokens[sentence.Scored[j]];
                run.Outcomes[offset + j] = Metrics.Judge(sentence.DocumentId, sentence.Gold[j], token, set.Label, set.NounLabel, set.VerbLabel, false);
                run.Swaps[offset + j] = IsConfidentSwap(sentence.Gold[j], token, set);
                run.Tags[offset + j] = (sbyte)token.Tag;
            }

            if (tagged.GlossaryMatches.Count > 0)
            {
                Categorize(sentence, tagged.GlossaryMatches, set, lexicon, scratch, run, offset);
            }

            offset += sentence.Scored.Length;
        }

        return run;
    }

    /// <summary>
    ///     Runs every system (no glossary, hard, soft at each strength) of one simulated glossary on an evaluation set.
    /// </summary>
    /// <param name="tagger">The tagger.</param>
    /// <param name="set">The evaluation set.</param>
    /// <param name="entries">The entries of the glossary.</param>
    /// <param name="lexicon">The lexicon.</param>
    /// <param name="none">The no-glossary run of the set (computed once by the caller).</param>
    /// <returns>The runs; the paired cells are computed by <see cref="Pair"/>.</returns>
    /// <exception cref="InvalidOperationException">The systems disagree about which tokens a glossary covers (a defect: a match does not depend on the mode).</exception>
    public static ScenarioResult RunScenario(Tagger tagger, EvalSet set, IReadOnlyList<GlossaryEntry> entries, Lexicon lexicon, SystemRun none)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(none);
        var systems = new SystemRun[StrengthGrid.Length + 1];
        for (var s = 0; s < systems.Length; s++)
        {
            var glossary = s == 0
                ? MakeGlossary(entries, GlossaryMode.Hard, GlossaryOptions.DefaultSoftStrength)
                : MakeGlossary(entries, GlossaryMode.Soft, StrengthGrid[s - 1]);
            systems[s] = RunSystem(tagger, set, glossary, lexicon, SystemName(s));
            if (s > 0 && (!systems[s].Categories.SequenceEqual(systems[0].Categories) || !systems[s].AtRisk.SequenceEqual(systems[0].AtRisk)))
            {
                throw new InvalidOperationException("The matches of a glossary must not depend on its mode.");
            }
        }

        return new ScenarioResult
        {
            EntryCount = entries.Count,
            None = none,
            Systems = systems,
            Categories = systems[0].Categories,
            AtRisk = systems[0].AtRisk,
        };
    }

    /// <summary>
    ///     Computes the paired differences of every system against the no-glossary run of the same scenario.
    /// </summary>
    /// <remarks>
    ///     The harmful errors and the swaps are counted per 1,000 noun-verb decisions of the <em>no-glossary</em> run (a
    ///     hard constraint leaves one candidate, so the decisions of the constrained run itself would shrink).
    /// </remarks>
    /// <param name="scenario">The scenario; its <see cref="ScenarioResult.Cells"/> are set.</param>
    /// <param name="resamples">The number of bootstrap resamples.</param>
    /// <param name="seed">The seed of the bootstrap.</param>
    public static void Pair(ScenarioResult scenario, int resamples, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        var none = scenario.None;
        var noneHarmful = WithBaselineDenominator(none.Outcomes, none.Outcomes, null);
        var noneSwaps = WithBaselineDenominator(none.Outcomes, none.Outcomes, none.Swaps);
        scenario.Cells = new PairedCell[scenario.Systems.Length];
        for (var s = 0; s < scenario.Systems.Length; s++)
        {
            var run = scenario.Systems[s];
            Bootstrap.Compare(WithBaselineDenominator(run.Outcomes, none.Outcomes, null), noneHarmful, resamples, seed, out var accuracy, out var harmful);
            Bootstrap.Compare(WithBaselineDenominator(run.Outcomes, none.Outcomes, run.Swaps), noneSwaps, resamples, seed, out _, out var swaps);
            scenario.Cells[s] = new PairedCell { Accuracy = accuracy, Harmful = harmful, Swaps = swaps };
        }
    }

    /// <summary>
    ///     Counts the correct tokens and the tokens of the categories that a predicate picks.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <param name="categories">The category of every scored token.</param>
    /// <param name="pick">Tells whether a category is counted.</param>
    /// <param name="correct">Receives the number of correct tokens among them.</param>
    /// <returns>The number of tokens in the categories.</returns>
    public static int Tally(SystemRun run, GlossaryCategory[] categories, Func<GlossaryCategory, bool> pick, out int correct)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(pick);
        var count = 0;
        correct = 0;
        for (var i = 0; i < categories.Length; i++)
        {
            if (pick(categories[i]))
            {
                count++;
                correct += run.Outcomes[i].Correct ? 1 : 0;
            }
        }

        return count;
    }

    /// <summary>
    ///     Tells whether a category belongs to a match (head right, head wrong or non-head).
    /// </summary>
    /// <param name="category">The category.</param>
    /// <returns><c>true</c> for a covered token.</returns>
    public static bool IsCovered(GlossaryCategory category) => category >= GlossaryCategory.HeadRight;

    /// <summary>
    ///     Tells whether a category is a neighbor: an uncovered token of a sentence with a match.
    /// </summary>
    /// <param name="category">The category.</param>
    /// <returns><c>true</c> for before, +1, +2 and later.</returns>
    public static bool IsNeighbor(GlossaryCategory category) => category >= GlossaryCategory.Before && category <= GlossaryCategory.Later;

    /// <summary>
    ///     Counts the tokens before the first match of their sentence whose tag differs from the no-glossary run (must be zero).
    /// </summary>
    /// <param name="none">The no-glossary run.</param>
    /// <param name="run">A glossary run of the same scenario.</param>
    /// <param name="categories">The categories of the scenario.</param>
    /// <param name="before">Receives the number of tokens before the first match that were compared.</param>
    /// <returns>The number of differences.</returns>
    public static int CountBeforeDifferences(SystemRun none, SystemRun run, GlossaryCategory[] categories, out int before)
    {
        ArgumentNullException.ThrowIfNull(none);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(categories);
        before = 0;
        var differences = 0;
        for (var i = 0; i < categories.Length; i++)
        {
            if (categories[i] == GlossaryCategory.Before)
            {
                before++;
                differences += none.Tags[i] != run.Tags[i] ? 1 : 0;
            }
        }

        return differences;
    }

    /// <summary>
    ///     Counts the flips of a glossary run against the no-glossary run on the tokens of a category: right to wrong and wrong to right.
    /// </summary>
    /// <param name="none">The no-glossary run.</param>
    /// <param name="run">The glossary run.</param>
    /// <param name="categories">The categories of the scenario.</param>
    /// <param name="category">The category to count.</param>
    /// <param name="rightToWrong">Receives the number of tokens that were right without and are wrong with the glossary.</param>
    /// <param name="wrongToRight">Receives the number of tokens that were wrong without and are right with the glossary.</param>
    /// <returns>The number of tokens in the category.</returns>
    public static int CountFlips(SystemRun none, SystemRun run, GlossaryCategory[] categories, GlossaryCategory category, out int rightToWrong, out int wrongToRight)
    {
        ArgumentNullException.ThrowIfNull(none);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(categories);
        var count = 0;
        rightToWrong = 0;
        wrongToRight = 0;
        for (var i = 0; i < categories.Length; i++)
        {
            if (categories[i] != category)
            {
                continue;
            }

            count++;
            rightToWrong += none.Outcomes[i].Correct && !run.Outcomes[i].Correct ? 1 : 0;
            wrongToRight += !none.Outcomes[i].Correct && run.Outcomes[i].Correct ? 1 : 0;
        }

        return count;
    }

    /// <summary>
    ///     Creates the entry of a simulated term, or <c>null</c> when the glossary cannot represent the term as the simulation assumed.
    /// </summary>
    /// <param name="term">The term.</param>
    /// <returns>The entry, or <c>null</c>.</returns>
    internal static GlossaryEntry? TryCreate(SimTerm term)
    {
        try
        {
            var entry = new GlossaryEntry(term.Text, term.Tag);
            return entry.Tokens.Count == term.Tokens.Length ? entry : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    ///     Tells whether a gold tag may be part of a nominal run: noun, proper noun or adjective.
    /// </summary>
    /// <param name="gold">The gold tag ordinal.</param>
    /// <returns><c>true</c> if the tag is NOUN, PROPN or ADJ.</returns>
    private static bool IsNominalModifier(sbyte gold) => gold == (int)Upos.Noun || gold == (int)Upos.Propn || gold == (int)Upos.Adj;

    /// <summary>
    ///     Tells whether a token consists of letters and hyphens only (with at least one letter).
    /// </summary>
    /// <param name="token">The token.</param>
    /// <returns><c>true</c> for an alphabetic token.</returns>
    private static bool IsAlphabetic(string token) =>
        token.Length > 0 && token.All(c => char.IsLetter(c) || c == '-') && token.Any(char.IsLetter);

    /// <summary>
    ///     Adds the compound that ends a run of nominal tokens, if there is one.
    /// </summary>
    /// <param name="found">The compounds found so far.</param>
    /// <param name="sentence">The sentence.</param>
    /// <param name="start">The first token of the run.</param>
    /// <param name="last">The last token of the run.</param>
    /// <param name="lexicon">The lexicon.</param>
    /// <param name="scratch">A scratch buffer for lexicon look-ups.</param>
    private static void AddCompound(SortedDictionary<string, SimTerm> found, TrainingSentence sentence, int start, int last, Lexicon lexicon, byte[] scratch)
    {
        if (sentence.Gold[last] != (int)Upos.Noun && sentence.Gold[last] != (int)Upos.Propn)
        {
            return;
        }

        for (var length = 3; length >= 2; length--)
        {
            var first = last - length + 1;
            if (first < start || !Enumerable.Range(first, length).All(i => IsAlphabetic(sentence.Tokens[i]) && IsOpenClass(lexicon, scratch, sentence.Tokens[i])))
            {
                continue;
            }

            var tokens = Enumerable.Range(first, length).Select(i => sentence.Tokens[i].ToLowerInvariant()).ToArray();
            var key = string.Join(" ", tokens);
            if (!found.ContainsKey(key))
            {
                found[key] = new SimTerm(tokens, (Upos)sentence.Gold[last]);
            }

            return;
        }
    }

    /// <summary>
    ///     Tells whether the lexicon lists a token with an open-class tag.
    /// </summary>
    /// <param name="lexicon">The lexicon.</param>
    /// <param name="scratch">A scratch buffer.</param>
    /// <param name="token">The token as written.</param>
    /// <returns><c>true</c> if the lexicon lists the token with NOUN, PROPN, VERB, ADJ or ADV.</returns>
    private static bool IsOpenClass(Lexicon lexicon, byte[] scratch, string token) =>
        TryLexiconTags(lexicon, scratch, token, out var tags) && !tags.Intersect(OpenClass).IsEmpty;

    /// <summary>
    ///     Looks up the lexicon tags of a token (shape rules do not count).
    /// </summary>
    /// <param name="lexicon">The lexicon.</param>
    /// <param name="scratch">A scratch buffer.</param>
    /// <param name="token">The token as written.</param>
    /// <param name="tags">Receives the lexicon tags.</param>
    /// <returns><c>true</c> if the token is in the lexicon.</returns>
    private static bool TryLexiconTags(Lexicon lexicon, byte[] scratch, string token, out UposSet tags)
    {
        var key = FeatureExtractor.Fold(token).ToLowerInvariant().ToCharArray();
        return lexicon.TryGetTags(key, 0, key.Length, scratch, out tags);
    }

    /// <summary>
    ///     Tells whether a token is a confident swap of the noun-verb pair (counted whether or not both were candidates).
    /// </summary>
    /// <param name="gold">The gold label.</param>
    /// <param name="token">The tagged token.</param>
    /// <param name="set">The evaluation set (for the labels).</param>
    /// <returns><c>true</c> for a gold noun answered as a verb or the reverse with a confidence at or above the harmful limit.</returns>
    private static bool IsConfidentSwap(string gold, TaggedToken token, EvalSet set)
    {
        var chosen = set.Label(token.Tag);
        var swapped = (gold == set.NounLabel && chosen == set.VerbLabel) || (gold == set.VerbLabel && chosen == set.NounLabel);
        return swapped && token.Confidence >= Metrics.HarmfulConfidence;
    }

    /// <summary>
    ///     Fills the categories and at-risk flags of the scored tokens of a sentence that has matches.
    /// </summary>
    /// <param name="sentence">The sentence.</param>
    /// <param name="matches">The matches in text order.</param>
    /// <param name="set">The evaluation set.</param>
    /// <param name="lexicon">The lexicon.</param>
    /// <param name="scratch">A scratch buffer.</param>
    /// <param name="run">The run to fill.</param>
    /// <param name="offset">The index of the first scored token of the sentence in the run.</param>
    private static void Categorize(EvalSentence sentence, IReadOnlyList<GlossaryMatch> matches, EvalSet set, Lexicon lexicon, byte[] scratch, SystemRun run, int offset)
    {
        var cover = new int[sentence.Tokens.Length];
        for (var i = 0; i < cover.Length; i++)
        {
            cover[i] = -1;
        }

        for (var m = 0; m < matches.Count; m++)
        {
            for (var k = 0; k < matches[m].TokenCount; k++)
            {
                cover[matches[m].FirstToken + k] = m;
            }
        }

        for (var j = 0; j < sentence.Scored.Length; j++)
        {
            var index = sentence.Scored[j];
            if (cover[index] < 0)
            {
                run.Categories[offset + j] = DistanceCategory(index, matches);
                continue;
            }

            var match = matches[cover[index]];
            run.Categories[offset + j] = HeadCategory(match, index, sentence.Gold[j], set);
            run.AtRisk[offset + j] = index == match.FirstToken
                && match.TokenCount >= 2
                && TryLexiconTags(lexicon, scratch, sentence.Tokens[index], out var tags)
                && tags.Contains(Upos.Verb);
        }
    }

    /// <summary>
    ///     Classifies a covered token: non-head, or head with a right or wrong entry.
    /// </summary>
    /// <param name="match">The match that covers the token.</param>
    /// <param name="index">The index of the token.</param>
    /// <param name="gold">The gold label of the token.</param>
    /// <param name="set">The evaluation set (for the labels).</param>
    /// <returns>The category.</returns>
    private static GlossaryCategory HeadCategory(GlossaryMatch match, int index, string gold, EvalSet set)
    {
        if (index != match.HeadToken)
        {
            return GlossaryCategory.NonHead;
        }

        return EntryHasGold(match.Entry.Tags, gold, set.Label) ? GlossaryCategory.HeadRight : GlossaryCategory.HeadWrong;
    }

    /// <summary>
    ///     Classifies an uncovered token of a sentence with matches by its distance after the nearest preceding match.
    /// </summary>
    /// <param name="index">The index of the token.</param>
    /// <param name="matches">The matches in text order.</param>
    /// <returns>Before, +1, +2 or later.</returns>
    private static GlossaryCategory DistanceCategory(int index, IReadOnlyList<GlossaryMatch> matches)
    {
        var distance = int.MaxValue;
        foreach (var match in matches)
        {
            var end = match.FirstToken + match.TokenCount - 1;
            if (end < index)
            {
                distance = index - end;
            }
        }

        if (distance == int.MaxValue)
        {
            return GlossaryCategory.Before;
        }

        if (distance == 1)
        {
            return GlossaryCategory.Plus1;
        }

        return distance == 2 ? GlossaryCategory.Plus2 : GlossaryCategory.Later;
    }

    /// <summary>
    ///     Tells whether any tag of an entry has the gold label of a token.
    /// </summary>
    /// <param name="tags">The tags of the entry.</param>
    /// <param name="gold">The gold label.</param>
    /// <param name="label">Maps a tag to its label.</param>
    /// <returns><c>true</c> if the entry gives the token its gold label.</returns>
    internal static bool EntryHasGold(UposSet tags, string gold, Func<Upos, string> label) => tags.Any(t => label(t) == gold);

    /// <summary>
    ///     Copies outcomes with the noun-verb decisions of the baseline, and optionally with swaps in place of harmful errors.
    /// </summary>
    /// <param name="outcomes">The outcomes to copy.</param>
    /// <param name="baseline">The outcomes of the no-glossary run (for the decisions).</param>
    /// <param name="swaps">The swaps to report as harmful errors, or <c>null</c> to keep the harmful errors.</param>
    /// <returns>The copies.</returns>
    private static List<Outcome> WithBaselineDenominator(Outcome[] outcomes, Outcome[] baseline, bool[]? swaps)
    {
        var list = new List<Outcome>(outcomes.Length);
        for (var i = 0; i < outcomes.Length; i++)
        {
            var o = outcomes[i];
            list.Add(new Outcome(o.DocumentId, o.Correct, o.Confidence, o.GoldProbability, o.Ambiguous, baseline[i].NounVerb, swaps == null ? o.Harmful : swaps[i]));
        }

        return list;
    }
}
