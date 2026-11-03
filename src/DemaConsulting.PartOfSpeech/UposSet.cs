using System.Collections;
using System.Text;

namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     An immutable set of <see cref="Upos"/> tags, stored as a 17-bit mask.
/// </summary>
/// <remarks>
///     The set is a value type with no allocation, so the decoder can intersect and test sets for every token without
///     garbage. Bit <c>i</c> of the mask stands for the tag with numeric value <c>i</c>, which is also the layout of
///     the lexicon file. Instances are immutable and therefore thread-safe; <c>default(UposSet)</c> is the empty set.
/// </remarks>
/// <example>
///     Build sets, combine them and test membership; a set can be given to <see cref="TokenConstraint.Only(UposSet)"/>:
///     <code>
///     UposSet content = UposSet.Of(Upos.Noun, Upos.Verb, Upos.Adj, Upos.Adv);
///     UposSet open = content.Remove(Upos.Adv).Add(Upos.Propn);
///
///     Console.WriteLine(open.Contains(Upos.Propn));          // True
///     Console.WriteLine(content.Intersect(open).Count);      // 3
///     TokenConstraint constraint = TokenConstraint.Only(open);
///     </code>
/// </example>
public readonly struct UposSet : IEquatable<UposSet>, IEnumerable<Upos>
{
    /// <summary>
    ///     The mask with all 17 bits set.
    /// </summary>
    private const int AllMask = (1 << UposExtensions.Count) - 1;

    /// <summary>
    ///     The bit mask of the members.
    /// </summary>
    private readonly int _mask;

    /// <summary>
    ///     Initializes a new instance of the <see cref="UposSet"/> struct from a mask.
    /// </summary>
    /// <param name="mask">The mask; bits above the 17th are discarded.</param>
    private UposSet(int mask)
    {
        _mask = mask & AllMask;
    }

    /// <summary>
    ///     Gets the set that contains no tag.
    /// </summary>
    public static UposSet Empty => default;

    /// <summary>
    ///     Gets the set that contains all 17 tags.
    /// </summary>
    public static UposSet All => new(AllMask);

    /// <summary>
    ///     Gets the number of tags in the set.
    /// </summary>
    public int Count => PopCount(_mask);

    /// <summary>
    ///     Gets a value that indicates whether the set contains no tag.
    /// </summary>
    public bool IsEmpty => _mask == 0;

    /// <summary>
    ///     Gets the bit mask of the set (bit <c>i</c> is the tag with numeric value <c>i</c>).
    /// </summary>
    internal int Mask => _mask;

    /// <summary>
    ///     Determines whether two sets contain the same tags.
    /// </summary>
    /// <param name="left">The first set.</param>
    /// <param name="right">The second set.</param>
    /// <returns><c>true</c> if the sets are equal.</returns>
    public static bool operator ==(UposSet left, UposSet right) => left._mask == right._mask;

    /// <summary>
    ///     Determines whether two sets differ.
    /// </summary>
    /// <param name="left">The first set.</param>
    /// <param name="right">The second set.</param>
    /// <returns><c>true</c> if the sets are not equal.</returns>
    public static bool operator !=(UposSet left, UposSet right) => left._mask != right._mask;

    /// <summary>
    ///     Creates a set from tags.
    /// </summary>
    /// <param name="tags">The members; duplicates are allowed and may be empty.</param>
    /// <returns>The set of the given tags.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tags"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A tag is not one of the 17 defined values.</exception>
    public static UposSet Of(params Upos[] tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        var set = Empty;
        foreach (var tag in tags)
        {
            set = set.Add(tag);
        }

        return set;
    }

    /// <summary>
    ///     Creates a set from a bit mask.
    /// </summary>
    /// <param name="mask">The mask (bit <c>i</c> is the tag with numeric value <c>i</c>); higher bits are discarded.</param>
    /// <returns>The set.</returns>
    internal static UposSet FromMask(int mask) => new(mask);

    /// <summary>
    ///     Determines whether the set contains a tag.
    /// </summary>
    /// <param name="tag">The tag.</param>
    /// <returns><c>true</c> if the tag is a member; undefined values are never members.</returns>
    public bool Contains(Upos tag) => (uint)tag < UposExtensions.Count && (_mask & (1 << (int)tag)) != 0;

    /// <summary>
    ///     Gets a copy of the set with a tag added.
    /// </summary>
    /// <param name="tag">The tag.</param>
    /// <returns>The new set.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tag"/> is not one of the 17 defined values.</exception>
    public UposSet Add(Upos tag) => new(_mask | Bit(tag));

    /// <summary>
    ///     Gets a copy of the set with a tag removed.
    /// </summary>
    /// <param name="tag">The tag.</param>
    /// <returns>The new set.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tag"/> is not one of the 17 defined values.</exception>
    public UposSet Remove(Upos tag) => new(_mask & ~Bit(tag));

    /// <summary>
    ///     Gets the union of two sets.
    /// </summary>
    /// <param name="other">The other set.</param>
    /// <returns>The tags that are in either set.</returns>
    public UposSet Union(UposSet other) => new(_mask | other._mask);

    /// <summary>
    ///     Gets the intersection of two sets.
    /// </summary>
    /// <param name="other">The other set.</param>
    /// <returns>The tags that are in both sets.</returns>
    public UposSet Intersect(UposSet other) => new(_mask & other._mask);

    /// <inheritdoc />
    public bool Equals(UposSet other) => _mask == other._mask;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is UposSet other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => _mask;

    /// <summary>
    ///     Enumerates the members in ascending tag order.
    /// </summary>
    /// <returns>An enumerator over the members.</returns>
    public IEnumerator<Upos> GetEnumerator()
    {
        // Copy the mask so the iterator does not depend on the struct instance
        var mask = _mask;
        for (var i = 0; i < UposExtensions.Count; i++)
        {
            if ((mask & (1 << i)) != 0)
            {
                yield return (Upos)i;
            }
        }
    }

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    ///     Formats the set as the spellings of its members separated by a vertical bar.
    /// </summary>
    /// <returns>For example <c>NOUN|VERB</c>; the empty set gives an empty string.</returns>
    public override string ToString()
    {
        var text = new StringBuilder();
        foreach (var tag in this)
        {
            if (text.Length > 0)
            {
                text.Append('|');
            }

            text.Append(tag.ToTagString());
        }

        return text.ToString();
    }

    /// <summary>
    ///     Gets the mask bit of a tag after validating it.
    /// </summary>
    /// <param name="tag">The tag.</param>
    /// <returns>The single-bit mask.</returns>
    private static int Bit(Upos tag)
    {
        // Validate through the one place that knows the defined range so all members share the same error
        if ((uint)tag >= UposExtensions.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(tag), tag, "The value is not one of the 17 Universal POS tags.");
        }

        return 1 << (int)tag;
    }

    /// <summary>
    ///     Counts the bits that are set in a mask.
    /// </summary>
    /// <param name="mask">The mask.</param>
    /// <returns>The number of set bits.</returns>
    private static int PopCount(int mask)
    {
        // A simple bit-clearing loop: the mask has at most 17 bits, so this is fast enough and works on every target framework
        var count = 0;
        while (mask != 0)
        {
            mask &= mask - 1;
            count++;
        }

        return count;
    }
}
