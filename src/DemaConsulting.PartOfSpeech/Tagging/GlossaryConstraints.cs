namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     The rule that decides how the non-head token of a two-token verb term whose head is the first token is
///     constrained when the entry has no explicit tags for the token.
/// </summary>
internal enum GlossaryNonHeadRule
{
    /// <summary>
    ///     The token is left to the model.
    /// </summary>
    None = 0,

    /// <summary>
    ///     The token must not be a verb or an auxiliary (the rule that noun terms always get).
    /// </summary>
    ForbidVerbAndAux = 1,

    /// <summary>
    ///     The token is limited to adposition, adverb and particle (the second word of a phrasal verb).
    /// </summary>
    ParticlesOnly = 2,
}

/// <summary>
///     The constraints that a glossary gives to the tokens of its matches, compiled for one tagger.
/// </summary>
/// <remarks>
///     <para>
///         <strong>Why compiled:</strong> <see cref="TokenConstraint.Prefer"/>, <see cref="TokenConstraint.Penalize"/> and
///         <see cref="TokenConstraint.And"/> allocate an adjustment table each. Building them per token would allocate in
///         the tagging path, so every constraint that a glossary can produce is built once here, and a match only copies
///         the shared structs into the constraint array of the sentence.
///     </para>
///     <para>
///         <strong>Why per tagger:</strong> a soft strength is expressed in units of the model temperature, and the
///         temperature belongs to the weights file of the tagger. The compiled tables of a glossary therefore exist once
///         per glossary and tagger. Hard constraints do not depend on the temperature.
///     </para>
///     <para>
///         <strong>Rules</strong> (the head token is the token that carries the tags of the matched form):
///         the head is limited to the tags in hard mode (<see cref="TokenConstraint.Only(UposSet)"/>) and boosted per tag
///         in soft mode; a non-head token with explicit tags is treated like a head with those tags; a non-head token of a
///         noun term (tags within <c>NOUN</c> and <c>PROPN</c>) must not be a verb or an auxiliary (hard) or is penalized
///         for those tags (soft); the second token of a two-token term whose tags contain <c>VERB</c> and whose head is
///         the first token follows <see cref="NonNominalRule"/>; every other non-head token is left to the model.
///     </para>
///     <para>
///         The instance is immutable after <see cref="Compile"/> and thread-safe.
///     </para>
/// </remarks>
internal sealed class GlossaryConstraints
{
    /// <summary>
    ///     The largest boost in raw score units; a larger product of strength and temperature is clamped to it so that it stays finite.
    /// </summary>
    /// <remarks>
    ///     A shift of this size already makes the softmax choose the boosted tag with a probability of one in every double
    ///     the platform can distinguish, so the clamp changes no result; it only keeps a huge strength from overflowing.
    /// </remarks>
    private const double MaximumBoost = 1e9;

    /// <summary>
    ///     The hard constraint of the head token for every tag set of the glossary.
    /// </summary>
    private readonly TokenConstraint[] _hardHead;

    /// <summary>
    ///     The soft constraint of the head token for every tag set of the glossary.
    /// </summary>
    private readonly TokenConstraint[] _softHead;

    /// <summary>
    ///     The constraints of the non-head tokens of every entry (the head slot is unused), or <c>null</c> for an entry without any.
    /// </summary>
    private readonly TokenConstraint[]?[] _nonHead;

    /// <summary>
    ///     Initializes a new instance of the <see cref="GlossaryConstraints"/> class.
    /// </summary>
    /// <param name="hardHead">The hard head constraints by tag set.</param>
    /// <param name="softHead">The soft head constraints by tag set.</param>
    /// <param name="nonHead">The non-head constraints by entry.</param>
    private GlossaryConstraints(TokenConstraint[] hardHead, TokenConstraint[] softHead, TokenConstraint[]?[] nonHead)
    {
        _hardHead = hardHead;
        _softHead = softHead;
        _nonHead = nonHead;
    }

    /// <summary>
    ///     Gets the rule for the particle of a two-token verb term whose head is the first token.
    /// </summary>
    /// <remarks>
    ///     This is the one switch for that decision. It is <see cref="GlossaryNonHeadRule.ParticlesOnly"/> because the
    ///     pre-registered bar of scenario G (section E9 of the trainer's <c>RUN-LOG.md</c>) was met on development data:
    ///     limiting the second token of a phrasal verb to adposition, adverb and particle improved the covered-token
    ///     accuracy, also with wrong entries. The rule applies only to the shape that was measured: an entry whose tags
    ///     contain <c>VERB</c>, a term of exactly two tokens, and the head is the first token (so the non-head token is
    ///     the second). Every other non-head token of a term that is not a noun term (an adjective phrase such as
    ///     <c>ready to use</c>, a pronoun inside a longer verb term such as <c>take it apart</c>, a token before the head)
    ///     is left to the model; a user who knows better says it per token with
    ///     <see cref="GlossaryEntry.WithTokenTags(int, UposSet)"/>.
    /// </remarks>
    internal static GlossaryNonHeadRule NonNominalRule => GlossaryNonHeadRule.ParticlesOnly;

    /// <summary>
    ///     Compiles the constraints of a glossary for a tagger with a given model temperature.
    /// </summary>
    /// <param name="glossary">The glossary.</param>
    /// <param name="temperature">The softmax temperature of the tagger's model.</param>
    /// <returns>The compiled constraints.</returns>
    public static GlossaryConstraints Compile(Glossary glossary, double temperature)
    {
        var boost = Math.Min(glossary.Settings.SoftStrength * temperature, MaximumBoost);

        // Head constraints per distinct tag set, in both modes (an entry may override the mode of the glossary)
        var masks = glossary.Index.TagSetMasks;
        var hardHead = new TokenConstraint[masks.Length];
        var softHead = new TokenConstraint[masks.Length];
        for (var i = 0; i < masks.Length; i++)
        {
            var tags = UposSet.FromMask(masks[i]);
            hardHead[i] = ForTags(tags, GlossaryMode.Hard, boost);
            softHead[i] = ForTags(tags, GlossaryMode.Soft, boost);
        }

        // Non-head constraints per entry, in the effective mode of the entry
        var entries = glossary.EntryArray;
        var nonHead = new TokenConstraint[]?[entries.Length];
        for (var e = 0; e < entries.Length; e++)
        {
            nonHead[e] = CompileNonHead(entries[e], entries[e].Mode ?? glossary.Settings.Mode, boost);
        }

        return new GlossaryConstraints(hardHead, softHead, nonHead);
    }

    /// <summary>
    ///     Creates the constraint of a non-head token for a rule.
    /// </summary>
    /// <param name="rule">The rule.</param>
    /// <param name="mode">The mode.</param>
    /// <param name="boost">The soft boost in raw score units.</param>
    /// <returns>The constraint.</returns>
    internal static TokenConstraint NonHead(GlossaryNonHeadRule rule, GlossaryMode mode, double boost)
    {
        switch (rule)
        {
            case GlossaryNonHeadRule.ForbidVerbAndAux:
                // A noun term is not a clause: the words in front of the head are not verbs
                return mode == GlossaryMode.Hard
                    ? TokenConstraint.Only(UposSet.All.Remove(Upos.Verb).Remove(Upos.Aux))
                    : TokenConstraint.Penalize(Upos.Verb, boost).And(TokenConstraint.Penalize(Upos.Aux, boost));
            case GlossaryNonHeadRule.ParticlesOnly:
                return ForTags(UposSet.Of(Upos.Adp, Upos.Adv, Upos.Part), mode, boost);
            default:
                return TokenConstraint.None;
        }
    }

    /// <summary>
    ///     Creates the constraint that limits (hard) or favors (soft) a set of tags.
    /// </summary>
    /// <param name="tags">The tags; at least one.</param>
    /// <param name="mode">The mode.</param>
    /// <param name="boost">The soft boost in raw score units.</param>
    /// <returns>The constraint.</returns>
    internal static TokenConstraint ForTags(UposSet tags, GlossaryMode mode, double boost)
    {
        if (mode == GlossaryMode.Hard)
        {
            return TokenConstraint.Only(tags);
        }

        // Soft: every allowed tag gets the same boost, so the model still decides among them
        var result = TokenConstraint.None;
        foreach (var tag in tags)
        {
            result = result.And(TokenConstraint.Prefer(tag, boost));
        }

        return result;
    }

    /// <summary>
    ///     Builds the constraint array of a sentence from its matches and the caller's own constraints.
    /// </summary>
    /// <remarks>
    ///     The caller's list is read only after the glossary's constraints are in place and is read once, so the result is
    ///     one consistent snapshot. Per token: no glossary constraint leaves the caller's, no caller constraint leaves the
    ///     glossary's, and two constraints are combined with <see cref="TokenConstraint.And(TokenConstraint)"/>; when they
    ///     allow no tag in common (or cannot be combined), the caller's constraint wins because it is explicit and per call.
    /// </remarks>
    /// <param name="matches">The matches of the sentence; at least one.</param>
    /// <param name="tokenCount">The number of tokens of the sentence.</param>
    /// <param name="constraints">The constraints of the caller, one per token, or <c>null</c>.</param>
    /// <returns>One constraint per token.</returns>
    /// <exception cref="ArgumentException"><paramref name="constraints"/> does not have one element per token.</exception>
    public TokenConstraint[] Build(IReadOnlyList<GlossaryMatch> matches, int tokenCount, IReadOnlyList<TokenConstraint>? constraints)
    {
        var result = new TokenConstraint[tokenCount];
        for (var m = 0; m < matches.Count; m++)
        {
            Apply(matches[m], result);
        }

        if (constraints == null)
        {
            return result;
        }

        // Read Count once and take exactly that many items, like the decoder does for a list of the caller
        var callerCount = constraints.Count;
        if (callerCount != tokenCount)
        {
            throw new ArgumentException($"Expected {tokenCount} constraints (one per token) but got {callerCount}.", nameof(constraints));
        }

        for (var i = 0; i < tokenCount; i++)
        {
            result[i] = Merge(constraints[i], result[i]);
        }

        return result;
    }

    /// <summary>
    ///     Combines the constraint of the caller with the constraint of the glossary for one token.
    /// </summary>
    /// <param name="caller">The caller's constraint.</param>
    /// <param name="glossary">The glossary's constraint.</param>
    /// <returns>The combined constraint; the caller's when the two cannot be combined.</returns>
    internal static TokenConstraint Merge(TokenConstraint caller, TokenConstraint glossary)
    {
        if (glossary.IsNone)
        {
            return caller;
        }

        if (caller.IsNone)
        {
            return glossary;
        }

        // Disjoint allowed sets are a contradiction between two explicit statements; the caller's is per call, so it wins
        if (caller.Allowed.Intersect(glossary.Allowed).IsEmpty)
        {
            return caller;
        }

        try
        {
            return caller.And(glossary);
        }
        catch (ArgumentException)
        {
            // The only other reason is a sum of adjustments that is not finite; the caller's constraint wins here too
            return caller;
        }
    }

    /// <summary>
    ///     Writes the constraints of a match into the constraint array of its sentence.
    /// </summary>
    /// <param name="match">The match.</param>
    /// <param name="target">The constraints of the sentence, one per token; the slots of the match are overwritten.</param>
    public void Apply(GlossaryMatch match, TokenConstraint[] target)
    {
        var head = match.HeadToken - match.FirstToken;
        var nonHead = _nonHead[match.EntryIndex];
        for (var k = 0; k < match.TokenCount; k++)
        {
            if (k == head)
            {
                target[match.FirstToken + k] = match.Mode == GlossaryMode.Hard ? _hardHead[match.TagSetId] : _softHead[match.TagSetId];
            }
            else if (nonHead != null && k < nonHead.Length)
            {
                target[match.FirstToken + k] = nonHead[k];
            }
        }
    }

    /// <summary>
    ///     Compiles the constraints of the non-head tokens of one entry.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <param name="mode">The effective mode of the entry.</param>
    /// <param name="boost">The soft boost in raw score units.</param>
    /// <returns>One constraint per token of the term (the head slot is <see cref="TokenConstraint.None"/>), or <c>null</c> when no token has a constraint.</returns>
    private static TokenConstraint[]? CompileNonHead(GlossaryEntry entry, GlossaryMode mode, double boost)
    {
        var isNounTerm = entry.Tags.Remove(Upos.Noun).Remove(Upos.Propn).IsEmpty;
        var isVerbTerm = entry.Tags.Contains(Upos.Verb);
        var table = new TokenConstraint[entry.Tokens.Count];
        var any = false;
        for (var k = 0; k < table.Length; k++)
        {
            if (k == entry.HeadIndex)
            {
                continue;
            }

            // Explicit tags beat the rules; otherwise a noun term forbids verbs, the particle of a two-token verb term whose
            // head is the first token follows the switch, and every other token is left to the model
            var explicitTags = entry.GetTokenTags(k);
            var rule = GlossaryNonHeadRule.None;
            if (isNounTerm)
            {
                rule = GlossaryNonHeadRule.ForbidVerbAndAux;
            }
            else if (isVerbTerm && table.Length == 2 && entry.HeadIndex == 0)
            {
                rule = NonNominalRule;
            }

            var constraint = explicitTags.HasValue
                ? ForTags(explicitTags.Value, mode, boost)
                : NonHead(rule, mode, boost);
            table[k] = constraint;
            any |= !constraint.IsNone;
        }

        return any ? table : null;
    }
}
