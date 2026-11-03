namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     A caller's restriction or preference for the tag of one token.
/// </summary>
/// <remarks>
///     <para>
///         A constraint lets a caller inject knowledge that the tagger does not have, for example a glossary that says a
///         word must be a noun in this document. It has two forms that can be combined: a <em>hard</em> form that limits
///         the allowed tags (<see cref="Only(UposSet)"/>) and a <em>soft</em> form that adds to or subtracts from the
///         score of a tag (<see cref="Prefer"/>, <see cref="Penalize"/>) so that the model can still disagree when it is
///         sure.
///     </para>
///     <para>
///         Semantics inside the tagger: the effective tags of a token are the constraint's allowed set intersected with
///         what the lexicon allows; if that is empty the constraint wins (a glossary is authoritative over a
///         dictionary). Soft adjustments are in raw score units, i.e. they are divided by the model temperature in the
///         softmax like the model's own scores. The tag that is chosen enters the history of the following tokens like
///         any other decision, so a constraint also influences its neighbors.
///     </para>
///     <para>
///         The struct is immutable (the adjustment table is copied whenever it changes) and therefore thread-safe.
///         <c>default(TokenConstraint)</c> is <see cref="None"/>.
///     </para>
/// </remarks>
/// <example>
///     Combine a hard and a soft constraint and pass one constraint per token to the tagger:
///     <code>
///     Sentence sentence = Tokenizer.Tokenize("Please book a table.")[0];
///
///     // One entry per token, in token order; None leaves a token to the model
///     var constraints = sentence.Tokens
///         .Select(token => token.Text == "book"
///             ? TokenConstraint.Only(Upos.Verb, Upos.Noun).And(TokenConstraint.Prefer(Upos.Verb, 2.0))
///             : TokenConstraint.None)
///         .ToArray();
///
///     TaggedSentence tagged = tagger.Tag(sentence, constraints);
///     Console.WriteLine(tagged.Tokens[1].IsConstrained);   // True
///     </code>
/// </example>
public readonly struct TokenConstraint : IEquatable<TokenConstraint>
{
    /// <summary>
    ///     The allowed tags as a mask; zero means "all" so that the default value is the neutral constraint.
    /// </summary>
    private readonly int _allowedMask;

    /// <summary>
    ///     The score adjustment of every tag, or <c>null</c> when there is none.
    /// </summary>
    private readonly double[]? _adjustments;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TokenConstraint"/> struct.
    /// </summary>
    /// <param name="allowedMask">The allowed tags as a mask; zero for all.</param>
    /// <param name="adjustments">The adjustment table or <c>null</c>; ownership passes to the constraint.</param>
    private TokenConstraint(int allowedMask, double[]? adjustments)
    {
        _allowedMask = allowedMask;
        _adjustments = adjustments;
    }

    /// <summary>
    ///     Gets the constraint that changes nothing.
    /// </summary>
    public static TokenConstraint None => default;

    /// <summary>
    ///     Gets the set of tags that the constraint allows (<see cref="UposSet.All"/> when it does not restrict).
    /// </summary>
    public UposSet Allowed => _allowedMask == 0 ? UposSet.All : UposSet.FromMask(_allowedMask);

    /// <summary>
    ///     Gets a value that indicates whether the constraint neither restricts tags nor adjusts scores.
    /// </summary>
    public bool IsNone => (_allowedMask == 0 || _allowedMask == UposSet.All.Mask) && _adjustments == null;

    /// <summary>
    ///     Gets a value that indicates whether the constraint adjusts scores.
    /// </summary>
    internal bool HasAdjustments => _adjustments != null;

    /// <summary>
    ///     Determines whether two constraints are equal.
    /// </summary>
    /// <param name="left">The first constraint.</param>
    /// <param name="right">The second constraint.</param>
    /// <returns><c>true</c> if they restrict and adjust identically.</returns>
    public static bool operator ==(TokenConstraint left, TokenConstraint right) => left.Equals(right);

    /// <summary>
    ///     Determines whether two constraints differ.
    /// </summary>
    /// <param name="left">The first constraint.</param>
    /// <param name="right">The second constraint.</param>
    /// <returns><c>true</c> if they do not restrict and adjust identically.</returns>
    public static bool operator !=(TokenConstraint left, TokenConstraint right) => !left.Equals(right);

    /// <summary>
    ///     Creates a hard constraint that allows only the given tags.
    /// </summary>
    /// <param name="allowed">The allowed tags; must not be empty.</param>
    /// <returns>The constraint.</returns>
    /// <exception cref="ArgumentException"><paramref name="allowed"/> is empty.</exception>
    public static TokenConstraint Only(UposSet allowed)
    {
        if (allowed.IsEmpty)
        {
            throw new ArgumentException("The allowed set must contain at least one tag.", nameof(allowed));
        }

        // Allowing every tag is the neutral state; store it as such so that every constraint has one representation
        return new TokenConstraint(allowed.Mask == UposSet.All.Mask ? 0 : allowed.Mask, null);
    }

    /// <summary>
    ///     Creates a hard constraint that allows only the given tags.
    /// </summary>
    /// <param name="allowed">The allowed tags; at least one.</param>
    /// <returns>The constraint.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="allowed"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="allowed"/> is empty.</exception>
    public static TokenConstraint Only(params Upos[] allowed) => Only(UposSet.Of(allowed));

    /// <summary>
    ///     Creates a soft constraint that adds a boost to the score of a tag.
    /// </summary>
    /// <param name="tag">The tag to favor.</param>
    /// <param name="boost">The amount added to the raw score of the tag; finite and greater than zero.</param>
    /// <returns>The constraint.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tag"/> is undefined or <paramref name="boost"/> is not finite and positive.</exception>
    public static TokenConstraint Prefer(Upos tag, double boost) => Adjust(tag, boost, false, nameof(boost));

    /// <summary>
    ///     Creates a soft constraint that subtracts a penalty from the score of a tag.
    /// </summary>
    /// <param name="tag">The tag to disfavor.</param>
    /// <param name="penalty">The amount subtracted from the raw score of the tag; finite and greater than zero.</param>
    /// <returns>The constraint.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tag"/> is undefined or <paramref name="penalty"/> is not finite and positive.</exception>
    public static TokenConstraint Penalize(Upos tag, double penalty) => Adjust(tag, penalty, true, nameof(penalty));

    /// <summary>
    ///     Combines this constraint with another one.
    /// </summary>
    /// <remarks>
    ///     The allowed sets are intersected and the adjustments of every tag are added. The result never shares its
    ///     adjustment table with either operand. When the adjustments of every tag cancel out, the result has no
    ///     adjustments at all, so it is equal to a constraint that never had any (and <see cref="IsNone"/> is
    ///     <c>true</c> if it also allows every tag).
    /// </remarks>
    /// <param name="other">The other constraint.</param>
    /// <returns>The combined constraint.</returns>
    /// <exception cref="ArgumentException">
    ///     The two allowed sets have no tag in common, or the sum of the adjustments of a tag is not a finite number
    ///     (for example two boosts of <see cref="double.MaxValue"/> overflow).
    /// </exception>
    public TokenConstraint And(TokenConstraint other)
    {
        // The intersection of two non-empty sets can be empty; that contradiction is reported, not guessed away
        var mask = Allowed.Intersect(other.Allowed);
        if (mask.IsEmpty)
        {
            throw new ArgumentException("The two constraints allow no tag in common.", nameof(other));
        }

        // Add the adjustment tables into a new array; the sum of two finite numbers can still overflow to infinity,
        // which would turn the confidences into NaN, so it is refused
        double[]? adjustments = null;
        if (_adjustments != null || other._adjustments != null)
        {
            adjustments = new double[UposExtensions.Count];
            var any = false;
            for (var i = 0; i < adjustments.Length; i++)
            {
                var sum = (_adjustments?[i] ?? 0) + (other._adjustments?[i] ?? 0);
                if (double.IsNaN(sum) || double.IsInfinity(sum))
                {
                    throw new ArgumentException("The combined score adjustment of a tag is not a finite number.", nameof(other));
                }

                adjustments[i] = sum;
                any |= sum != 0;
            }

            // Adjustments that cancel out are the same as none: one representation keeps IsNone, Equals and ToString in agreement
            if (!any)
            {
                adjustments = null;
            }
        }

        return new TokenConstraint(mask.Mask == UposSet.All.Mask ? 0 : mask.Mask, adjustments);
    }

    /// <summary>
    ///     Gets the score adjustment of a tag.
    /// </summary>
    /// <param name="tag">The tag ordinal (0 to 16).</param>
    /// <returns>The adjustment; zero when the constraint has none for the tag.</returns>
    internal double AdjustmentOf(int tag) => _adjustments == null ? 0 : _adjustments[tag];

    /// <inheritdoc />
    public bool Equals(TokenConstraint other)
    {
        if (Allowed != other.Allowed)
        {
            return false;
        }

        // A missing table equals a table of zeros
        for (var i = 0; i < UposExtensions.Count; i++)
        {
            if (AdjustmentOf(i).CompareTo(other.AdjustmentOf(i)) != 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is TokenConstraint other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = Allowed.Mask;
        for (var i = 0; i < UposExtensions.Count; i++)
        {
            hash = unchecked((hash * 31) + AdjustmentOf(i).GetHashCode());
        }

        return hash;
    }

    /// <summary>
    ///     Formats the constraint for diagnostics.
    /// </summary>
    /// <returns>For example <c>Only(NOUN|VERB)</c> or <c>None</c>.</returns>
    public override string ToString()
    {
        if (IsNone)
        {
            return "None";
        }

        return _adjustments == null ? $"Only({Allowed})" : $"Only({Allowed})+adjusted";
    }

    /// <summary>
    ///     Creates a soft constraint for one tag.
    /// </summary>
    /// <param name="tag">The tag.</param>
    /// <param name="amount">The amount; must be finite and greater than zero.</param>
    /// <param name="subtract">Whether the amount is subtracted (a penalty) instead of added (a boost).</param>
    /// <param name="parameterName">The parameter name for the error message.</param>
    /// <returns>The constraint.</returns>
    private static TokenConstraint Adjust(Upos tag, double amount, bool subtract, string parameterName)
    {
        if ((uint)tag >= UposExtensions.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(tag), tag, "The value is not one of the 17 Universal POS tags.");
        }

        // Reject NaN, infinities, zero and negatives: such an amount would silently do nothing or poison the softmax
        if (double.IsNaN(amount) || double.IsInfinity(amount) || amount <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, amount, "The amount must be finite and greater than zero.");
        }

        var adjustments = new double[UposExtensions.Count];
        adjustments[(int)tag] = subtract ? -amount : amount;
        return new TokenConstraint(0, adjustments);
    }
}
