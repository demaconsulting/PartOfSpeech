namespace DemaConsulting.PartOfSpeech.Tests;

/// <summary>
///     Unit tests of <see cref="Upos"/>, <see cref="UposExtensions"/> and <see cref="UposSet"/>.
/// </summary>
public class UposTests
{
    /// <summary>
    ///     Proves that the tag set has 17 members whose order is alphabetical (the order of the binary formats).
    /// </summary>
    [Fact]
    public void Upos_Values_AreSeventeenInAlphabeticalOrder()
    {
        // Arrange: the defined values in numeric order
        var values = Enum.GetValues(typeof(Upos)).Cast<Upos>().OrderBy(v => (int)v).ToList();

        // Act: spell every tag
        var spellings = values.Select(v => v.ToTagString()).ToList();

        // Assert: 17 values numbered 0 to 16 whose spellings are sorted
        Assert.Equal(UposExtensions.Count, values.Count);
        Assert.Equal(Enumerable.Range(0, 17), values.Select(v => (int)v));
        Assert.Equal(spellings.OrderBy(s => s, StringComparer.Ordinal), spellings);
        Assert.Equal(DataFiles.UniversalTags, spellings);
    }

    /// <summary>
    ///     Proves that every tag survives a round trip through its spelling.
    /// </summary>
    [Fact]
    public void Upos_ToTagString_RoundTripsAllTags()
    {
        // Arrange / Act: spell and parse every tag
        var results = Enum.GetValues(typeof(Upos)).Cast<Upos>()
            .Select(tag => (tag, ok: UposExtensions.TryParse(tag.ToTagString(), out var parsed), parsed))
            .ToList();

        // Assert: parsing gives the tag back
        Assert.All(results, r =>
        {
            Assert.True(r.ok);
            Assert.Equal(r.tag, r.parsed);
        });
    }

    /// <summary>
    ///     Proves that text that is not an exact tag spelling does not parse.
    /// </summary>
    /// <param name="text">The text.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("noun")]
    [InlineData("NOUN ")]
    [InlineData("NOUNS")]
    [InlineData("-S-")]
    public void Upos_TryParse_UnknownText_ReturnsFalse(string? text)
    {
        // Arrange / Act: parse the text
        var parsed = UposExtensions.TryParse(text, out _);

        // Assert: it is rejected
        Assert.False(parsed);
    }

    /// <summary>
    ///     Proves that an undefined enum value cannot be spelled.
    /// </summary>
    [Fact]
    public void Upos_ToTagString_UndefinedValue_Throws()
    {
        // Arrange: a value that was cast from a number
        var undefined = (Upos)17;

        // Act / Assert: spelling it throws
        Assert.Throws<ArgumentOutOfRangeException>(() => undefined.ToTagString());
    }

    /// <summary>
    ///     Proves that adding and removing tags behaves like a set and does not change the original value.
    /// </summary>
    [Fact]
    public void UposSet_AddRemove_BehaveAsSet()
    {
        // Arrange: the empty set
        var empty = UposSet.Empty;

        // Act: add (twice) and remove
        var added = empty.Add(Upos.Noun).Add(Upos.Noun).Add(Upos.Verb);
        var removed = added.Remove(Upos.Noun).Remove(Upos.Adj);

        // Assert: set semantics and immutability
        Assert.True(empty.IsEmpty);
        Assert.Equal(2, added.Count);
        Assert.True(added.Contains(Upos.Noun));
        Assert.Equal(1, removed.Count);
        Assert.False(removed.Contains(Upos.Noun));
        Assert.True(removed.Contains(Upos.Verb));
        Assert.False(added.Contains((Upos)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => empty.Add((Upos)17));
    }

    /// <summary>
    ///     Proves union and intersection.
    /// </summary>
    [Fact]
    public void UposSet_UnionIntersect_ComputeSetResults()
    {
        // Arrange: two overlapping sets
        var a = UposSet.Of(Upos.Noun, Upos.Verb, Upos.Adj);
        var b = UposSet.Of(Upos.Verb, Upos.Adj, Upos.Adv);

        // Act: combine them
        var union = a.Union(b);
        var intersection = a.Intersect(b);

        // Assert: the expected members
        Assert.Equal(UposSet.Of(Upos.Noun, Upos.Verb, Upos.Adj, Upos.Adv), union);
        Assert.Equal(UposSet.Of(Upos.Verb, Upos.Adj), intersection);
        Assert.True(a.Intersect(UposSet.Empty).IsEmpty);
    }

    /// <summary>
    ///     Proves that the full set has all 17 tags.
    /// </summary>
    [Fact]
    public void UposSet_All_ContainsSeventeen()
    {
        // Arrange / Act: take the full set
        var all = UposSet.All;

        // Assert: 17 members and every tag is in it
        Assert.Equal(17, all.Count);
        Assert.All(Enum.GetValues(typeof(Upos)).Cast<Upos>(), tag => Assert.True(all.Contains(tag)));
        Assert.Equal(all, UposSet.Empty.Union(all));
    }

    /// <summary>
    ///     Proves that equality is by content, whatever the construction order.
    /// </summary>
    [Fact]
    public void UposSet_Equality_IsByContent()
    {
        // Arrange: the same content built in two orders and a different set
        var a = UposSet.Of(Upos.Noun, Upos.Verb);
        var b = UposSet.Of(Upos.Verb, Upos.Noun, Upos.Noun);
        var c = UposSet.Of(Upos.Noun);

        // Act / Assert: equality, operators and hash code agree
        Assert.True(a == b);
        Assert.True(a != c);
        Assert.True(a.Equals((object)b));
        Assert.False(a.Equals("NOUN|VERB"));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.True(default(UposSet) == UposSet.Empty);
    }

    /// <summary>
    ///     Proves that enumeration is in ascending tag order.
    /// </summary>
    [Fact]
    public void UposSet_Enumeration_IsAscending()
    {
        // Arrange: a set added in descending order
        var set = UposSet.Of(Upos.X, Upos.Verb, Upos.Adj, Upos.Punct);

        // Act: enumerate it
        var tags = set.ToList();

        // Assert: ascending order
        Assert.Equal(new[] { Upos.Adj, Upos.Punct, Upos.Verb, Upos.X }, tags);
    }

    /// <summary>
    ///     Proves the text form of a set.
    /// </summary>
    [Fact]
    public void UposSet_ToString_ListsTags()
    {
        // Arrange / Act / Assert: the empty, single and multiple forms
        Assert.Equal(string.Empty, UposSet.Empty.ToString());
        Assert.Equal("NOUN", UposSet.Of(Upos.Noun).ToString());
        Assert.Equal("NOUN|VERB", UposSet.Of(Upos.Verb, Upos.Noun).ToString());
    }
}
