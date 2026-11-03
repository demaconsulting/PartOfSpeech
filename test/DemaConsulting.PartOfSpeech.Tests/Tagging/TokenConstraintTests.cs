namespace DemaConsulting.PartOfSpeech.Tests.Tagging;

/// <summary>
///     Unit tests of <see cref="TokenConstraint"/>.
/// </summary>
public class TokenConstraintTests
{
    /// <summary>
    ///     Proves that an empty allowed set and invalid boosts or penalties are rejected.
    /// </summary>
    [Fact]
    public void TokenConstraint_Only_EmptySet_Throws()
    {
        // Arrange / Act / Assert: an empty set, no tags, and undefined tags are invalid
        Assert.Throws<ArgumentException>(() => TokenConstraint.Only(UposSet.Empty));
        Assert.Throws<ArgumentException>(() => TokenConstraint.Only(new Upos[0]));
        Assert.Throws<ArgumentNullException>(() => TokenConstraint.Only((Upos[])null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => TokenConstraint.Only((Upos)40));
    }

    /// <summary>
    ///     Proves that a boost or penalty must be finite and positive and for a defined tag.
    /// </summary>
    /// <param name="amount">The invalid amount.</param>
    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void TokenConstraint_Prefer_NonFiniteOrNonPositive_Throws(double amount)
    {
        // Arrange / Act / Assert: both soft factories reject the amount
        Assert.Throws<ArgumentOutOfRangeException>(() => TokenConstraint.Prefer(Upos.Noun, amount));
        Assert.Throws<ArgumentOutOfRangeException>(() => TokenConstraint.Penalize(Upos.Noun, amount));
        Assert.Throws<ArgumentOutOfRangeException>(() => TokenConstraint.Prefer((Upos)99, 1.0));
    }

    /// <summary>
    ///     Proves that the default value and <see cref="TokenConstraint.None"/> change nothing.
    /// </summary>
    [Fact]
    public void TokenConstraint_Default_IsNone()
    {
        // Arrange: the default value
        var constraint = default(TokenConstraint);

        // Act / Assert: all tags are allowed, nothing is adjusted, and it equals None
        Assert.True(constraint.IsNone);
        Assert.Equal(UposSet.All, constraint.Allowed);
        Assert.Equal(TokenConstraint.None, constraint);
        Assert.Equal(0.0, constraint.AdjustmentOf((int)Upos.Noun));
        Assert.True(TokenConstraint.Only(UposSet.All).IsNone);
        Assert.False(TokenConstraint.Only(Upos.Noun).IsNone);
        Assert.False(TokenConstraint.Prefer(Upos.Noun, 1).IsNone);
        Assert.Equal("None", constraint.ToString());
    }

    /// <summary>
    ///     Proves that combining intersects the allowed sets and adds the adjustments, and that disjoint sets are rejected.
    /// </summary>
    [Fact]
    public void TokenConstraint_And_IntersectsAndSumsBoosts()
    {
        // Arrange: two hard constraints with an overlap and two soft constraints on the same tag
        var a = TokenConstraint.Only(Upos.Noun, Upos.Verb);
        var b = TokenConstraint.Only(Upos.Verb, Upos.Adj);
        var boost1 = TokenConstraint.Prefer(Upos.Noun, 1.5);
        var boost2 = TokenConstraint.Prefer(Upos.Noun, 2.0);
        var penalty = TokenConstraint.Penalize(Upos.Verb, 1.0);

        // Act: combine
        var hard = a.And(b);
        var soft = boost1.And(boost2).And(penalty);
        var mixed = a.And(boost1);

        // Assert: intersection, sums, and the empty intersection is an error
        Assert.Equal(UposSet.Of(Upos.Verb), hard.Allowed);
        Assert.Equal(3.5, soft.AdjustmentOf((int)Upos.Noun), 9);
        Assert.Equal(-1.0, soft.AdjustmentOf((int)Upos.Verb), 9);
        Assert.Equal(UposSet.All, soft.Allowed);
        Assert.Equal(UposSet.Of(Upos.Noun, Upos.Verb), mixed.Allowed);
        Assert.Equal(1.5, mixed.AdjustmentOf((int)Upos.Noun), 9);
        Assert.Throws<ArgumentException>(() => TokenConstraint.Only(Upos.Noun).And(TokenConstraint.Only(Upos.Verb)));
    }

    /// <summary>
    ///     Proves that combining creates new adjustment storage so that earlier constraints are never changed.
    /// </summary>
    [Fact]
    public void TokenConstraint_Boosts_AreCopied()
    {
        // Arrange: a boost and the result of combining it with another one
        var original = TokenConstraint.Prefer(Upos.Noun, 1.0);
        var snapshot = original.AdjustmentOf((int)Upos.Noun);

        // Act: combine twice
        var first = original.And(TokenConstraint.Prefer(Upos.Noun, 2.0));
        var second = original.And(TokenConstraint.Prefer(Upos.Verb, 4.0));

        // Assert: the original and both results are independent
        Assert.Equal(snapshot, original.AdjustmentOf((int)Upos.Noun));
        Assert.Equal(3.0, first.AdjustmentOf((int)Upos.Noun), 9);
        Assert.Equal(0.0, first.AdjustmentOf((int)Upos.Verb), 9);
        Assert.Equal(1.0, second.AdjustmentOf((int)Upos.Noun), 9);
        Assert.Equal(4.0, second.AdjustmentOf((int)Upos.Verb), 9);
    }

    /// <summary>
    ///     Proves that equality is by content and that equal constraints have equal hash codes.
    /// </summary>
    [Fact]
    public void TokenConstraint_Equals_ByContent()
    {
        // Arrange: equal and different constraints built separately
        var a = TokenConstraint.Only(Upos.Noun, Upos.Verb).And(TokenConstraint.Prefer(Upos.Noun, 2.0));
        var b = TokenConstraint.Prefer(Upos.Noun, 2.0).And(TokenConstraint.Only(Upos.Verb, Upos.Noun));
        var c = TokenConstraint.Only(Upos.Noun, Upos.Verb);

        // Act / Assert: equality, operators, object equality and hash code
        Assert.True(a == b);
        Assert.True(a != c);
        Assert.True(a.Equals((object)b));
        Assert.False(a.Equals("x"));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.Contains("adjusted", a.ToString());
    }

    /// <summary>
    ///     Proves that adjustments that cancel out combine to the one representation of "no adjustment", so that
    ///     <see cref="TokenConstraint.IsNone"/>, equality, <see cref="TokenConstraint.ToString"/> and the constrained
    ///     flag of a tagged token agree.
    /// </summary>
    [Fact]
    public void TokenConstraint_And_CancelingAdjustments_EqualsNone()
    {
        // Arrange: a boost and a penalty of the same amount on the same tag
        var boost = TokenConstraint.Prefer(Upos.Noun, 1);
        var penalty = TokenConstraint.Penalize(Upos.Noun, 1);

        // Act: combine them in both orders, and with a hard constraint that allows every tag
        var combined = boost.And(penalty);
        var reversed = penalty.And(boost);
        var withAll = combined.And(TokenConstraint.Only(UposSet.All));

        // Assert: every observation agrees that nothing is constrained
        Assert.All(new[] { combined, reversed, withAll }, constraint =>
        {
            Assert.True(constraint.IsNone);
            Assert.False(constraint.HasAdjustments);
            Assert.Equal(TokenConstraint.None, constraint);
            Assert.Equal(TokenConstraint.None.GetHashCode(), constraint.GetHashCode());
            Assert.Equal("None", constraint.ToString());
        });
        Assert.False(TokenConstraint.Only(Upos.Noun).And(boost.And(penalty)).IsNone);
    }

    /// <summary>
    ///     Proves that a sum of adjustments that overflows to infinity is rejected, while the largest finite
    ///     adjustment of a single factory call and sums that stay finite are accepted.
    /// </summary>
    [Fact]
    public void TokenConstraint_And_NonFiniteSum_Throws()
    {
        // Arrange: boosts of the largest finite double on the same tag, and on different tags
        var large = TokenConstraint.Prefer(Upos.Noun, double.MaxValue);
        var largeVerb = TokenConstraint.Prefer(Upos.Verb, double.MaxValue);
        var largePenalty = TokenConstraint.Penalize(Upos.Noun, double.MaxValue);

        // Act: combine the cases that stay finite
        var differentTags = large.And(largeVerb);
        var cancelled = large.And(largePenalty);

        // Assert: a single large value and independent tags are finite; overflowing in either direction is refused
        Assert.Equal(double.MaxValue, large.AdjustmentOf((int)Upos.Noun));
        Assert.Equal(double.MaxValue, differentTags.AdjustmentOf((int)Upos.Verb));
        Assert.True(cancelled.IsNone);
        Assert.Throws<ArgumentException>(() => large.And(large));
        Assert.Throws<ArgumentException>(() => largePenalty.And(largePenalty));
        Assert.Throws<ArgumentException>(() => large.And(TokenConstraint.Prefer(Upos.Noun, double.MaxValue)).And(TokenConstraint.None));
    }
}
