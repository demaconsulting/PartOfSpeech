namespace DemaConsulting.PartOfSpeech.Trainer;

/// <summary>
///     One judged token of an evaluation: where it came from, what the gold answer and the model's answer were, and the full
///     probability vector of the model.
/// </summary>
/// <remarks>
///     The judgment itself is an <see cref="Outcome"/> made by <see cref="Metrics.Judge"/>, so the development and the
///     held-out evaluation use one definition of correct, noun-verb decision and harmful error. The class is immutable;
///     the probabilities are indexed by the ordinal of <see cref="Upos"/> (alphabetical order of the tag names).
/// </remarks>
internal sealed class EvalToken
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="EvalToken"/> class.
    /// </summary>
    /// <param name="set">The name of the evaluation set.</param>
    /// <param name="documentId">The document (the unit of the bootstrap); a row that has no document uses its own id.</param>
    /// <param name="sentenceId">The sentence id.</param>
    /// <param name="index">The zero-based index of the token in its sentence.</param>
    /// <param name="form">The token as written.</param>
    /// <param name="gold">The gold label (a UPOS name or a class name).</param>
    /// <param name="chosen">The label of the tag the model chose.</param>
    /// <param name="confidence">The confidence of the chosen tag.</param>
    /// <param name="probabilities">The probability of every tag (zero for a tag that is not a candidate).</param>
    /// <param name="outcome">The judgment.</param>
    /// <param name="nounVerbGold">1 when the gold label is the noun label, 2 for the verb label, 0 otherwise.</param>
    /// <param name="nounVerbChosen">1 when the chosen label is the noun label, 2 for the verb label, 0 otherwise.</param>
    public EvalToken(
        string set,
        string documentId,
        string sentenceId,
        int index,
        string form,
        string gold,
        string chosen,
        double confidence,
        double[] probabilities,
        Outcome outcome,
        int nounVerbGold,
        int nounVerbChosen)
    {
        Set = set;
        DocumentId = documentId;
        SentenceId = sentenceId;
        Index = index;
        Form = form;
        Gold = gold;
        Chosen = chosen;
        Confidence = confidence;
        Probabilities = probabilities;
        Outcome = outcome;
        NounVerbGold = nounVerbGold;
        NounVerbChosen = nounVerbChosen;
    }

    /// <summary>
    ///     Gets the name of the evaluation set.
    /// </summary>
    public string Set { get; }

    /// <summary>
    ///     Gets the document of the token.
    /// </summary>
    public string DocumentId { get; }

    /// <summary>
    ///     Gets the sentence id.
    /// </summary>
    public string SentenceId { get; }

    /// <summary>
    ///     Gets the zero-based index of the token in its sentence.
    /// </summary>
    public int Index { get; }

    /// <summary>
    ///     Gets the token as written.
    /// </summary>
    public string Form { get; }

    /// <summary>
    ///     Gets the gold label.
    /// </summary>
    public string Gold { get; }

    /// <summary>
    ///     Gets the label of the chosen tag.
    /// </summary>
    public string Chosen { get; }

    /// <summary>
    ///     Gets the confidence of the chosen tag.
    /// </summary>
    public double Confidence { get; }

    /// <summary>
    ///     Gets the probability of every tag by <see cref="Upos"/> ordinal.
    /// </summary>
    public double[] Probabilities { get; }

    /// <summary>
    ///     Gets the judgment.
    /// </summary>
    public Outcome Outcome { get; }

    /// <summary>
    ///     Gets 1 when the gold label is the noun label, 2 for the verb label and 0 otherwise.
    /// </summary>
    public int NounVerbGold { get; }

    /// <summary>
    ///     Gets 1 when the chosen label is the noun label, 2 for the verb label and 0 otherwise.
    /// </summary>
    public int NounVerbChosen { get; }

    /// <summary>
    ///     Judges a tagged token against its gold label.
    /// </summary>
    /// <param name="set">The name of the evaluation set.</param>
    /// <param name="documentId">The document of the token.</param>
    /// <param name="sentenceId">The sentence id.</param>
    /// <param name="index">The index of the token in its sentence.</param>
    /// <param name="form">The token as written.</param>
    /// <param name="gold">The gold label.</param>
    /// <param name="token">The tagged token (the tagger must have been created with all alternatives).</param>
    /// <param name="label">Maps a tag to its label (UPOS name or class name).</param>
    /// <param name="nounLabel">The label that stands for nouns.</param>
    /// <param name="verbLabel">The label that stands for verbs.</param>
    /// <param name="lexiconAmbiguous">Whether the lexicon lists more than one tag for the token.</param>
    /// <returns>The judged token.</returns>
    public static EvalToken Create(
        string set,
        string documentId,
        string sentenceId,
        int index,
        string form,
        string gold,
        TaggedToken token,
        Func<Upos, string> label,
        string nounLabel,
        string verbLabel,
        bool lexiconAmbiguous)
    {
        // The probability vector: the chosen tag plus its alternatives, everything else is zero
        var probabilities = new double[UposExtensions.Count];
        probabilities[(int)token.Tag] = token.Confidence;
        foreach (var alternative in token.Alternatives)
        {
            probabilities[(int)alternative.Tag] = alternative.Probability;
        }

        var chosen = label(token.Tag);
        var outcome = Metrics.Judge(documentId, gold, token, label, nounLabel, verbLabel, lexiconAmbiguous);
        return new EvalToken(
            set,
            documentId,
            sentenceId,
            index,
            form,
            gold,
            chosen,
            token.Confidence,
            probabilities,
            outcome,
            NounVerbClass(gold, nounLabel, verbLabel),
            NounVerbClass(chosen, nounLabel, verbLabel));
    }

    /// <summary>
    ///     Classifies a label as the noun label (1), the verb label (2) or another label (0).
    /// </summary>
    /// <param name="label">The label.</param>
    /// <param name="nounLabel">The label that stands for nouns.</param>
    /// <param name="verbLabel">The label that stands for verbs.</param>
    /// <returns>The class.</returns>
    private static int NounVerbClass(string label, string nounLabel, string verbLabel)
    {
        if (label == nounLabel)
        {
            return 1;
        }

        return label == verbLabel ? 2 : 0;
    }
}

/// <summary>
///     The additive counts of a set of judged tokens: everything a metric of the evaluation needs, so that the tally of a
///     bootstrap resample is the sum of the tallies of its documents.
/// </summary>
/// <remarks>
///     <para>
///         A tally holds counts (tokens, correct, ambiguous, noun-verb decisions, harmful errors), the sum of the negative
///         log-likelihood, the 15 calibration bins, the answered and harmful counts at every threshold of
///         <see cref="ConfidenceCurve.Grid"/>, the reliability bins of the noun-verb decisions, the noun-verb confusion and
///         the true positives, false positives and false negatives of every label. The metrics follow the formulas of
///         <see cref="Metrics.Compute"/>; a test asserts that they agree.
///     </para>
///     <para>
///         Not thread-safe; a tally is owned by the code that builds it.
///     </para>
/// </remarks>
internal sealed class EvalTally
{
    /// <summary>
    ///     The labels that a gold or chosen label can have: the 17 UPOS names and the classes of gold v2.
    /// </summary>
    public static readonly IReadOnlyList<string> Labels = BuildLabels();

    /// <summary>
    ///     The lower edges of the reliability bins of the noun-verb decisions (the last bin ends at 1).
    /// </summary>
    public static readonly IReadOnlyList<double> ReliabilityEdges = new[] { 0.0, 0.5, 0.6, 0.7, 0.8, 0.9, 0.95, 0.99 };

    /// <summary>
    ///     The index of every label.
    /// </summary>
    private static readonly Dictionary<string, int> LabelIndex = Labels.Select((l, i) => (l, i)).ToDictionary(p => p.l, p => p.i, StringComparer.Ordinal);

    /// <summary>
    ///     Gets the number of judged tokens.
    /// </summary>
    public long Tokens { get; private set; }

    /// <summary>
    ///     Gets the number of correct tokens.
    /// </summary>
    public long Correct { get; private set; }

    /// <summary>
    ///     Gets the number of lexicon-ambiguous tokens.
    /// </summary>
    public long Ambiguous { get; private set; }

    /// <summary>
    ///     Gets the number of correct lexicon-ambiguous tokens.
    /// </summary>
    public long AmbiguousCorrect { get; private set; }

    /// <summary>
    ///     Gets the number of noun-verb decisions.
    /// </summary>
    public long Decisions { get; private set; }

    /// <summary>
    ///     Gets the number of harmful errors at the harmful confidence (0.45).
    /// </summary>
    public long Harmful { get; private set; }

    /// <summary>
    ///     Gets the number of tokens whose gold label was not among the candidates.
    /// </summary>
    public long OutsideCandidates { get; private set; }

    /// <summary>
    ///     Gets the sum of the negative log-likelihoods of the gold labels.
    /// </summary>
    public double NllSum { get; private set; }

    /// <summary>
    ///     Gets the number of tokens whose labels were recorded (the per-label counts and the confusion need them).
    /// </summary>
    public long Labeled { get; private set; }

    /// <summary>
    ///     Gets the number of noun-verb decisions answered at each threshold of the grid.
    /// </summary>
    public long[] AnsweredAt { get; } = new long[ConfidenceCurve.Grid.Count];

    /// <summary>
    ///     Gets the number of harmful errors at each threshold of the grid.
    /// </summary>
    public long[] HarmfulAt { get; } = new long[ConfidenceCurve.Grid.Count];

    /// <summary>
    ///     Gets the number of decisions in each reliability bin.
    /// </summary>
    public long[] BinDecisions { get; } = new long[ReliabilityEdges.Count];

    /// <summary>
    ///     Gets the number of correct decisions in each reliability bin.
    /// </summary>
    public long[] BinDecisionsCorrect { get; } = new long[ReliabilityEdges.Count];

    /// <summary>
    ///     Gets the number of harmful errors in each reliability bin.
    /// </summary>
    public long[] BinHarmful { get; } = new long[ReliabilityEdges.Count];

    /// <summary>
    ///     Gets the sum of the confidences of the decisions in each reliability bin.
    /// </summary>
    public double[] BinDecisionConfidence { get; } = new double[ReliabilityEdges.Count];

    /// <summary>
    ///     Gets the noun-verb confusion: 6 cells, gold noun then gold verb, each answered noun, verb or another tag.
    /// </summary>
    public long[] Confusion { get; } = new long[6];

    /// <summary>
    ///     Gets the true positives of each label.
    /// </summary>
    public long[] TruePositives { get; } = new long[Labels.Count];

    /// <summary>
    ///     Gets the false positives of each label.
    /// </summary>
    public long[] FalsePositives { get; } = new long[Labels.Count];

    /// <summary>
    ///     Gets the false negatives of each label.
    /// </summary>
    public long[] FalseNegatives { get; } = new long[Labels.Count];

    /// <summary>
    ///     Gets the counts of (gold, chosen) label pairs, for the tag-level confusion.
    /// </summary>
    public long[] Pairs { get; } = new long[Labels.Count * Labels.Count];

    /// <summary>
    ///     Gets the calibration bins: count, correct count and the sum of the confidences, 15 bins each.
    /// </summary>
    private long[] EceCount { get; } = new long[Metrics.EceBins];

    /// <summary>
    ///     Gets the correct counts of the calibration bins.
    /// </summary>
    private long[] EceCorrect { get; } = new long[Metrics.EceBins];

    /// <summary>
    ///     Gets the confidence sums of the calibration bins.
    /// </summary>
    private double[] EceConfidence { get; } = new double[Metrics.EceBins];

    /// <summary>
    ///     Gets the share of correct tokens in percent.
    /// </summary>
    public double Accuracy => Tokens == 0 ? 0 : 100.0 * Correct / Tokens;

    /// <summary>
    ///     Gets the share of correct tokens among the lexicon-ambiguous ones in percent.
    /// </summary>
    public double AmbiguousAccuracy => Ambiguous == 0 ? 0 : 100.0 * AmbiguousCorrect / Ambiguous;

    /// <summary>
    ///     Gets the harmful errors per 1,000 noun-verb decisions at the harmful confidence.
    /// </summary>
    public double HarmfulPer1000 => Decisions == 0 ? 0 : 1000.0 * Harmful / Decisions;

    /// <summary>
    ///     Gets the mean negative log-likelihood of the gold label.
    /// </summary>
    public double Nll => Tokens == 0 ? 0 : NllSum / Tokens;

    /// <summary>
    ///     Gets the expected calibration error in percent (15 equal-width bins).
    /// </summary>
    public double Ece
    {
        get
        {
            var ece = 0.0;
            for (var b = 0; b < Metrics.EceBins; b++)
            {
                if (EceCount[b] > 0)
                {
                    ece += EceCount[b] * Math.Abs(((double)EceCorrect[b] / EceCount[b]) - (EceConfidence[b] / EceCount[b]));
                }
            }

            return Tokens == 0 ? 0 : 100.0 * ece / Tokens;
        }
    }

    /// <summary>
    ///     Gets the index of a label in the label space.
    /// </summary>
    /// <param name="label">The label.</param>
    /// <returns>The index.</returns>
    /// <exception cref="InvalidDataException">The label is not a UPOS name or a class name.</exception>
    public static int IndexOf(string label) =>
        LabelIndex.TryGetValue(label, out var index) ? index : throw new InvalidDataException($"'{label}' is not a known tag or class name.");

    /// <summary>
    ///     Builds the tally of a list of judged tokens.
    /// </summary>
    /// <param name="tokens">The tokens.</param>
    /// <returns>The tally.</returns>
    public static EvalTally Of(IEnumerable<EvalToken> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        var tally = new EvalTally();
        foreach (var token in tokens)
        {
            tally.Add(token);
        }

        return tally;
    }

    /// <summary>
    ///     Builds the tally of a list of outcomes (no labels: the per-label counts and the confusion stay empty).
    /// </summary>
    /// <param name="outcomes">The outcomes.</param>
    /// <returns>The tally.</returns>
    public static EvalTally Of(IEnumerable<Outcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        var tally = new EvalTally();
        foreach (var outcome in outcomes)
        {
            tally.Add(outcome);
        }

        return tally;
    }

    /// <summary>
    ///     Groups judged tokens by document and builds one tally for each.
    /// </summary>
    /// <param name="tokens">The tokens.</param>
    /// <returns>The tallies in order of first appearance of their documents.</returns>
    public static List<EvalTally> ByDocument(IEnumerable<EvalToken> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<EvalTally>();
        foreach (var token in tokens)
        {
            if (!index.TryGetValue(token.DocumentId, out var slot))
            {
                slot = result.Count;
                index[token.DocumentId] = slot;
                result.Add(new EvalTally());
            }

            result[slot].Add(token);
        }

        return result;
    }

    /// <summary>
    ///     Groups outcomes by document and builds one tally for each.
    /// </summary>
    /// <param name="outcomes">The outcomes.</param>
    /// <returns>The tallies in order of first appearance of their documents.</returns>
    public static List<EvalTally> ByDocument(IEnumerable<Outcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(outcomes);
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new List<EvalTally>();
        foreach (var outcome in outcomes)
        {
            if (!index.TryGetValue(outcome.DocumentId, out var slot))
            {
                slot = result.Count;
                index[outcome.DocumentId] = slot;
                result.Add(new EvalTally());
            }

            result[slot].Add(outcome);
        }

        return result;
    }

    /// <summary>
    ///     Adds a judged token, with its labels.
    /// </summary>
    /// <param name="token">The token.</param>
    public void Add(EvalToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        Add(token.Outcome);

        // The labels: per-label counts, the pair counts and the noun-verb confusion
        var gold = IndexOf(token.Gold);
        var chosen = IndexOf(token.Chosen);
        Labeled++;
        Pairs[(gold * Labels.Count) + chosen]++;
        if (gold == chosen)
        {
            TruePositives[gold]++;
        }
        else
        {
            FalseNegatives[gold]++;
            FalsePositives[chosen]++;
        }

        if (token.Outcome.NounVerb && token.NounVerbGold > 0)
        {
            Confusion[((token.NounVerbGold - 1) * 3) + token.NounVerbChosen]++;
        }
    }

    /// <summary>
    ///     Adds an outcome (counts, calibration, thresholds and reliability, but no labels).
    /// </summary>
    /// <param name="outcome">The outcome.</param>
    public void Add(Outcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        Tokens++;
        Correct += outcome.Correct ? 1 : 0;
        if (outcome.Ambiguous)
        {
            Ambiguous++;
            AmbiguousCorrect += outcome.Correct ? 1 : 0;
        }

        Decisions += outcome.NounVerb ? 1 : 0;
        Harmful += outcome.Harmful ? 1 : 0;
        OutsideCandidates += outcome.GoldProbability <= 0 ? 1 : 0;
        NllSum -= Math.Log(Math.Max(outcome.GoldProbability, Metrics.ProbabilityFloor));
        var bin = Math.Min(Metrics.EceBins - 1, (int)(outcome.Confidence * Metrics.EceBins));
        EceCount[bin]++;
        EceCorrect[bin] += outcome.Correct ? 1 : 0;
        EceConfidence[bin] += outcome.Confidence;
        if (!outcome.NounVerb)
        {
            return;
        }

        // The decision: answered and harmful at every threshold, and its reliability bin
        for (var i = 0; i < AnsweredAt.Length; i++)
        {
            if (outcome.Confidence >= ConfidenceCurve.Grid[i])
            {
                AnsweredAt[i]++;
                HarmfulAt[i] += outcome.Harmful ? 1 : 0;
            }
        }

        var reliability = ReliabilityBin(outcome.Confidence);
        BinDecisions[reliability]++;
        BinDecisionsCorrect[reliability] += outcome.Correct ? 1 : 0;
        BinHarmful[reliability] += outcome.Harmful ? 1 : 0;
        BinDecisionConfidence[reliability] += outcome.Confidence;
    }

    /// <summary>
    ///     Adds another tally.
    /// </summary>
    /// <param name="other">The tally to add.</param>
    public void Add(EvalTally other)
    {
        ArgumentNullException.ThrowIfNull(other);
        Tokens += other.Tokens;
        Correct += other.Correct;
        Ambiguous += other.Ambiguous;
        AmbiguousCorrect += other.AmbiguousCorrect;
        Decisions += other.Decisions;
        Harmful += other.Harmful;
        OutsideCandidates += other.OutsideCandidates;
        NllSum += other.NllSum;
        Labeled += other.Labeled;
        Sum(AnsweredAt, other.AnsweredAt);
        Sum(HarmfulAt, other.HarmfulAt);
        Sum(BinDecisions, other.BinDecisions);
        Sum(BinDecisionsCorrect, other.BinDecisionsCorrect);
        Sum(BinHarmful, other.BinHarmful);
        Sum(Confusion, other.Confusion);
        Sum(TruePositives, other.TruePositives);
        Sum(FalsePositives, other.FalsePositives);
        Sum(FalseNegatives, other.FalseNegatives);
        Sum(Pairs, other.Pairs);
        Sum(EceCount, other.EceCount);
        Sum(EceCorrect, other.EceCorrect);
        for (var i = 0; i < EceConfidence.Length; i++)
        {
            EceConfidence[i] += other.EceConfidence[i];
        }

        for (var i = 0; i < BinDecisionConfidence.Length; i++)
        {
            BinDecisionConfidence[i] += other.BinDecisionConfidence[i];
        }
    }

    /// <summary>
    ///     Gets the precision of a label in percent.
    /// </summary>
    /// <param name="label">The label index.</param>
    /// <returns>The precision, or 0 when the label was never chosen.</returns>
    public double Precision(int label)
    {
        var chosen = TruePositives[label] + FalsePositives[label];
        return chosen == 0 ? 0 : 100.0 * TruePositives[label] / chosen;
    }

    /// <summary>
    ///     Gets the recall of a label in percent.
    /// </summary>
    /// <param name="label">The label index.</param>
    /// <returns>The recall, or 0 when the label has no gold token.</returns>
    public double Recall(int label)
    {
        var gold = TruePositives[label] + FalseNegatives[label];
        return gold == 0 ? 0 : 100.0 * TruePositives[label] / gold;
    }

    /// <summary>
    ///     Finds the reliability bin of a confidence.
    /// </summary>
    /// <param name="confidence">The confidence.</param>
    /// <returns>The index of the bin.</returns>
    public static int ReliabilityBin(double confidence)
    {
        var bin = 0;
        for (var i = 1; i < ReliabilityEdges.Count; i++)
        {
            if (confidence >= ReliabilityEdges[i])
            {
                bin = i;
            }
        }

        return bin;
    }

    /// <summary>
    ///     Builds the label space: the 17 UPOS names in ordinal order, then the classes of gold v2.
    /// </summary>
    /// <returns>The labels.</returns>
    private static string[] BuildLabels()
    {
        var labels = new List<string>();
        for (var i = 0; i < UposExtensions.Count; i++)
        {
            labels.Add(((Upos)i).ToTagString());
        }

        labels.AddRange(new[] { "Noun", "Verb", "Auxiliary", "Adjective", "Adverb", "Preposition", "Conjunction", "Determiner", "Pronoun", "Interjection", "Other" });
        return labels.ToArray();
    }

    /// <summary>
    ///     Adds one array of counts to another.
    /// </summary>
    /// <param name="total">The array that receives the sum.</param>
    /// <param name="other">The counts to add.</param>
    private static void Sum(long[] total, long[] other)
    {
        for (var i = 0; i < total.Length; i++)
        {
            total[i] += other[i];
        }
    }
}
