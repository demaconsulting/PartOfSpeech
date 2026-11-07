using System.Diagnostics;

namespace DemaConsulting.PartOfSpeech.Trainer;

/// <summary>
///     The judged tokens of every held-out set, in the views that the report needs.
/// </summary>
internal sealed class HeldOutScores
{
    /// <summary>
    ///     Gets the tokens of the open test split.
    /// </summary>
    public List<EvalToken> OpenTest { get; } = new();

    /// <summary>
    ///     Gets the gold v2 test targets without <c>to</c> and <c>not</c>.
    /// </summary>
    public List<EvalToken> Gold { get; } = new();

    /// <summary>
    ///     Gets the gold v2 test targets without <c>to</c> and <c>not</c> whose labelers agreed fully (the cleaner subset).
    /// </summary>
    public List<EvalToken> GoldFullAgreement { get; } = new();

    /// <summary>
    ///     Gets the gold v2 test targets <c>to</c> and <c>not</c> (a convention difference, reported separately).
    /// </summary>
    public List<EvalToken> GoldConvention { get; } = new();

    /// <summary>
    ///     Gets the rows of the shape probe.
    /// </summary>
    public List<EvalToken> Probe { get; } = new();

    /// <summary>
    ///     Gets the rows of the shape probe by shape, in order of first appearance.
    /// </summary>
    public List<KeyValuePair<string, List<EvalToken>>> ProbeShapes { get; } = new();

    /// <summary>
    ///     Gets the tokens of the challenge set.
    /// </summary>
    public List<EvalToken> Challenge { get; } = new();

    /// <summary>
    ///     Gets the words of the treebank against the convention-mapped tags (the primary view).
    /// </summary>
    public List<EvalToken> UdMapped { get; } = new();

    /// <summary>
    ///     Gets the words of the treebank against the raw tags (the second view).
    /// </summary>
    public List<EvalToken> UdRaw { get; } = new();

    /// <summary>
    ///     Gets the number of treebank words that a mapping rule changed.
    /// </summary>
    public long UdMappedWords { get; set; }
}

/// <summary>
///     Tags the held-out sets with the embedded model and judges every token with the definitions of the development evaluation.
/// </summary>
/// <remarks>
///     <para>
///         The judged pass uses a tagger with all 16 alternatives, because the candidate set of a token decides what a
///         noun-verb decision is. The timed pass is separate and uses the default options, as a consumer would. The class has no
///         file access: it scores what it is given, and the data come from <see cref="HeldOutReader"/>. Stateless and thread-safe.
///     </para>
/// </remarks>
internal static class HeldOutEvaluator
{
    /// <summary>
    ///     The names of the sets as they appear in the dump files and the reports.
    /// </summary>
    public static class Names
    {
        /// <summary>
        ///     The open test split.
        /// </summary>
        public const string OpenTest = "open-test";

        /// <summary>
        ///     The gold v2 test split.
        /// </summary>
        public const string GoldTest = "gold-v2-test";

        /// <summary>
        ///     The shape probe.
        /// </summary>
        public const string Probe = "shape-probe";

        /// <summary>
        ///     The challenge set.
        /// </summary>
        public const string Challenge = "challenge-set";

        /// <summary>
        ///     The treebank, mapped view.
        /// </summary>
        public const string UdMapped = "ud-test-mapped";

        /// <summary>
        ///     The treebank, raw view.
        /// </summary>
        public const string UdRaw = "ud-test-raw";
    }

    /// <summary>
    ///     Judges every held-out set.
    /// </summary>
    /// <param name="tagger">The tagger created with all alternatives.</param>
    /// <param name="lexicon">The lexicon that decides which tokens are ambiguous.</param>
    /// <param name="data">The held-out data.</param>
    /// <returns>The judged tokens.</returns>
    public static HeldOutScores Score(Tagger tagger, Lexicon lexicon, HeldOutData data)
    {
        ArgumentNullException.ThrowIfNull(tagger);
        ArgumentNullException.ThrowIfNull(lexicon);
        ArgumentNullException.ThrowIfNull(data);
        var scratch = new byte[lexicon.ScratchSize];
        var scores = new HeldOutScores();
        ScoreCorpus(tagger, lexicon, scratch, data.OpenTest, Names.OpenTest, "open:", scores.OpenTest, false);
        ScoreCorpus(tagger, lexicon, scratch, data.Challenge, Names.Challenge, "ste:", scores.Challenge, true);
        ScoreTargets(tagger, lexicon, scratch, data.Gold, Names.GoldTest, scores, false);
        ScoreTargets(tagger, lexicon, scratch, data.Probe, Names.Probe, scores, true);
        ScoreUd(tagger, lexicon, scratch, data.Ud, scores);
        return scores;
    }

    /// <summary>
    ///     Measures the throughput of the default tagger in tokens per second: one warm-up pass, then the median of the timed passes.
    /// </summary>
    /// <param name="tagger">The tagger with the default options.</param>
    /// <param name="sentences">The sentences to tag.</param>
    /// <param name="timedPasses">The number of timed passes (at least 1; the held-out run uses 3).</param>
    /// <returns>The tokens per second (the middle value of the sorted rates, the upper one for an even count).</returns>
    public static double Throughput(Tagger tagger, IReadOnlyList<string[]> sentences, int timedPasses = 3)
    {
        ArgumentNullException.ThrowIfNull(tagger);
        ArgumentNullException.ThrowIfNull(sentences);
        if (timedPasses < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(timedPasses), timedPasses, "There must be at least one timed pass.");
        }
        var tokens = sentences.Sum(s => (long)s.Length);
        var rates = new List<double>();
        for (var pass = 0; pass <= timedPasses; pass++)
        {
            var clock = Stopwatch.StartNew();
            foreach (var sentence in sentences)
            {
                tagger.Tag(sentence);
            }

            // The first pass warms the caches and the JIT and is not counted
            if (pass > 0)
            {
                rates.Add(tokens / Math.Max(clock.Elapsed.TotalSeconds, 1e-9));
            }
        }

        rates.Sort();
        return rates[rates.Count / 2];
    }

    /// <summary>
    ///     Judges the sentences of a corpus-format set.
    /// </summary>
    /// <param name="tagger">The tagger.</param>
    /// <param name="lexicon">The lexicon.</param>
    /// <param name="scratch">The scratch buffer of the lexicon.</param>
    /// <param name="sentences">The sentences.</param>
    /// <param name="set">The name of the set.</param>
    /// <param name="prefix">The prefix of the document ids.</param>
    /// <param name="into">The list that receives the judged tokens.</param>
    /// <param name="sentenceIsDocument"><c>true</c> to make every sentence its own document (a set that has one document id for everything).</param>
    private static void ScoreCorpus(Tagger tagger, Lexicon lexicon, byte[] scratch, IReadOnlyList<TrainingSentence> sentences, string set, string prefix, List<EvalToken> into, bool sentenceIsDocument)
    {
        foreach (var sentence in sentences)
        {
            var tagged = tagger.Tag(sentence.Tokens);
            var document = prefix + (sentenceIsDocument ? sentence.SentenceId : sentence.SourceId);
            for (var i = 0; i < sentence.Tokens.Length; i++)
            {
                if (sentence.Gold[i] >= 0)
                {
                    var ambiguous = DevEvaluator.IsLexiconAmbiguous(lexicon, scratch, sentence.Tokens[i]);
                    into.Add(EvalToken.Create(set, document, sentence.SentenceId, i, sentence.Tokens[i], ((Upos)sentence.Gold[i]).ToTagString(), tagged.Tokens[i], t => t.ToTagString(), "NOUN", "VERB", ambiguous));
                }
            }
        }
    }

    /// <summary>
    ///     Judges the single-target rows of gold v2 or of the shape probe.
    /// </summary>
    /// <param name="tagger">The tagger.</param>
    /// <param name="lexicon">The lexicon.</param>
    /// <param name="scratch">The scratch buffer of the lexicon.</param>
    /// <param name="targets">The rows.</param>
    /// <param name="set">The name of the set.</param>
    /// <param name="scores">The scores that receive the tokens.</param>
    /// <param name="probe"><c>true</c> for the shape probe, <c>false</c> for gold v2.</param>
    private static void ScoreTargets(Tagger tagger, Lexicon lexicon, byte[] scratch, IReadOnlyList<GoldTarget> targets, string set, HeldOutScores scores, bool probe)
    {
        foreach (var target in targets)
        {
            var tagged = tagger.Tag(target.Tokens);
            var ambiguous = DevEvaluator.IsLexiconAmbiguous(lexicon, scratch, target.Form);
            var token = EvalToken.Create(set, target.DocumentId, target.Id, target.Target, target.Form, target.GoldClass, tagged.Tokens[target.Target], DevSets.GoldClassOf, "Noun", "Verb", ambiguous);
            if (probe)
            {
                scores.Probe.Add(token);
                var shape = scores.ProbeShapes.FirstOrDefault(p => p.Key == target.Group).Value;
                if (shape == null)
                {
                    shape = new List<EvalToken>();
                    scores.ProbeShapes.Add(new(target.Group, shape));
                }

                shape.Add(token);
            }
            else if (target.IsConventionWord)
            {
                scores.GoldConvention.Add(token);
            }
            else
            {
                scores.Gold.Add(token);
                if (target.Agreement == "full")
                {
                    scores.GoldFullAgreement.Add(token);
                }
            }
        }
    }

    /// <summary>
    ///     Judges the treebank words once and builds the mapped and the raw view from the same tags.
    /// </summary>
    /// <param name="tagger">The tagger.</param>
    /// <param name="lexicon">The lexicon.</param>
    /// <param name="scratch">The scratch buffer of the lexicon.</param>
    /// <param name="sentences">The sentences.</param>
    /// <param name="scores">The scores that receive the tokens.</param>
    private static void ScoreUd(Tagger tagger, Lexicon lexicon, byte[] scratch, IReadOnlyList<UdSentence> sentences, HeldOutScores scores)
    {
        foreach (var sentence in sentences)
        {
            var tagged = tagger.Tag(sentence.Tokens);
            var document = "ud:" + sentence.DocumentId;
            for (var i = 0; i < sentence.Tokens.Length; i++)
            {
                var ambiguous = DevEvaluator.IsLexiconAmbiguous(lexicon, scratch, sentence.Tokens[i]);
                scores.UdMapped.Add(EvalToken.Create(Names.UdMapped, document, sentence.SentenceId, i, sentence.Tokens[i], sentence.Mapped[i].ToTagString(), tagged.Tokens[i], t => t.ToTagString(), "NOUN", "VERB", ambiguous));
                scores.UdRaw.Add(EvalToken.Create(Names.UdRaw, document, sentence.SentenceId, i, sentence.Tokens[i], sentence.Raw[i].ToTagString(), tagged.Tokens[i], t => t.ToTagString(), "NOUN", "VERB", ambiguous));
                scores.UdMappedWords += sentence.Rules[i] == "_" ? 0 : 1;
            }
        }
    }
}
