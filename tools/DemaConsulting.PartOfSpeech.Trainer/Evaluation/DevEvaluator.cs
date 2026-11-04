namespace DemaConsulting.PartOfSpeech.Trainer;

/// <summary>
///     The outcomes of one weights file on the two development sets.
/// </summary>
internal sealed class DevResult
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="DevResult"/> class.
    /// </summary>
    /// <param name="open">The outcomes of every token of the open development split.</param>
    /// <param name="gold">The outcomes of the gold v2 targets, without <c>to</c> and <c>not</c>.</param>
    /// <param name="convention">The outcomes of the gold v2 targets that are <c>to</c> or <c>not</c> (reported separately).</param>
    public DevResult(List<Outcome> open, List<Outcome> gold, List<Outcome> convention)
    {
        Open = open;
        Gold = gold;
        Convention = convention;
    }

    /// <summary>
    ///     Gets the outcomes of the open development split.
    /// </summary>
    public List<Outcome> Open { get; }

    /// <summary>
    ///     Gets the outcomes of the gold v2 targets without <c>to</c> and <c>not</c>.
    /// </summary>
    public List<Outcome> Gold { get; }

    /// <summary>
    ///     Gets the outcomes of the gold v2 targets <c>to</c> and <c>not</c>.
    /// </summary>
    public List<Outcome> Convention { get; }

    /// <summary>
    ///     Gets the open and the gold outcomes in one list (the pooled development set), with document ids that cannot collide.
    /// </summary>
    public List<Outcome> Pooled
    {
        get
        {
            var pooled = new List<Outcome>(Open.Count + Gold.Count);
            pooled.AddRange(Open);
            pooled.AddRange(Gold);
            return pooled;
        }
    }
}

/// <summary>
///     Runs a weights file through the real runtime decoder on the development sets and judges every token.
/// </summary>
/// <remarks>
///     <para>
///         Scoring goes through <c>Tagger.Load</c> of the in-memory file, the same path a user takes, so any
///         difference between what the trainer optimizes and what the runtime does shows up as a measured difference.
///         The tagger is created with all 16 alternatives, because the candidate set of a token (needed for the
///         ambiguity, the noun-verb decisions and the log-likelihood) is the chosen tag plus its alternatives.
///     </para>
///     <para>
///         Stateless and thread-safe; the tagger it creates is thread-safe too.
///     </para>
/// </remarks>
internal static class DevEvaluator
{
    /// <summary>
    ///     Creates a tagger from an in-memory weights file with all alternatives enabled.
    /// </summary>
    /// <param name="weights">The weights file (compressed or not).</param>
    /// <returns>The tagger.</returns>
    /// <exception cref="InvalidDataException">The file is not a valid weights file of this build.</exception>
    public static Tagger CreateTagger(byte[] weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        using var stream = new MemoryStream(weights);
        return Tagger.Load(stream, new TaggerOptions { MaxAlternatives = TaggerOptions.MaxAlternativesLimit });
    }

    /// <summary>
    ///     Evaluates a tagger on both development sets.
    /// </summary>
    /// <param name="tagger">The tagger (created with all alternatives).</param>
    /// <param name="open">The open development sentences.</param>
    /// <param name="gold">The gold v2 development targets.</param>
    /// <returns>The outcomes.</returns>
    public static DevResult Evaluate(Tagger tagger, IReadOnlyList<TrainingSentence> open, IReadOnlyList<GoldTarget> gold)
    {
        ArgumentNullException.ThrowIfNull(tagger);
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(gold);
        return new DevResult(EvaluateOpen(tagger, open), EvaluateGold(tagger, gold, false), EvaluateGold(tagger, gold, true));
    }

    /// <summary>
    ///     Evaluates a tagger on the open development sentences.
    /// </summary>
    /// <param name="tagger">The tagger.</param>
    /// <param name="sentences">The sentences.</param>
    /// <returns>One outcome per token with a gold tag.</returns>
    public static List<Outcome> EvaluateOpen(Tagger tagger, IReadOnlyList<TrainingSentence> sentences)
    {
        var outcomes = new List<Outcome>();
        var lexicon = EmbeddedModel.Lexicon;
        var scratch = new byte[lexicon.ScratchSize];
        foreach (var sentence in sentences)
        {
            var tagged = tagger.Tag(sentence.Tokens);
            for (var i = 0; i < sentence.Tokens.Length; i++)
            {
                if (sentence.Gold[i] >= 0)
                {
                    var gold = ((Upos)sentence.Gold[i]).ToTagString();
                    var ambiguous = IsLexiconAmbiguous(lexicon, scratch, sentence.Tokens[i]);
                    outcomes.Add(Metrics.Judge("open:" + sentence.SourceId, gold, tagged.Tokens[i], t => t.ToTagString(), "NOUN", "VERB", ambiguous));
                }
            }
        }

        return outcomes;
    }

    /// <summary>
    ///     Evaluates a tagger on the gold v2 targets.
    /// </summary>
    /// <param name="tagger">The tagger.</param>
    /// <param name="targets">The targets.</param>
    /// <param name="conventionWords"><c>true</c> to judge only the targets <c>to</c> and <c>not</c>, <c>false</c> to judge all the others.</param>
    /// <returns>One outcome per selected target.</returns>
    public static List<Outcome> EvaluateGold(Tagger tagger, IReadOnlyList<GoldTarget> targets, bool conventionWords)
    {
        var outcomes = new List<Outcome>();
        var lexicon = EmbeddedModel.Lexicon;
        var scratch = new byte[lexicon.ScratchSize];
        foreach (var target in targets)
        {
            if (target.IsConventionWord != conventionWords)
            {
                continue;
            }

            var tagged = tagger.Tag(target.Tokens);
            var ambiguous = IsLexiconAmbiguous(lexicon, scratch, target.Form);
            outcomes.Add(Metrics.Judge("gold:" + target.DocumentId, target.GoldClass, tagged.Tokens[target.Target], DevSets.GoldClassOf, "Noun", "Verb", ambiguous));
        }

        return outcomes;
    }

    /// <summary>
    ///     Determines whether the lexicon lists more than one tag for a token (the token is folded and lower-cased like a lexicon key).
    /// </summary>
    /// <param name="lexicon">The lexicon.</param>
    /// <param name="scratch">A scratch buffer of at least <c>ScratchSize</c> bytes.</param>
    /// <param name="token">The token as written.</param>
    /// <returns><c>true</c> if the token is in the lexicon with two or more tags.</returns>
    public static bool IsLexiconAmbiguous(Lexicon lexicon, byte[] scratch, string token)
    {
        var key = FeatureExtractor.Fold(token).ToLowerInvariant().ToCharArray();
        return lexicon.TryGetTags(key, 0, key.Length, scratch, out var tags) && tags.Count >= 2;
    }
}
