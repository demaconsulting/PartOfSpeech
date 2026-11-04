using System.Collections;

namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     A read-only list over a slice of a score array: the alternatives of one token.
/// </summary>
/// <remarks>
///     <para>
///         The decoder stores the alternatives of all tokens of a sentence in one flat array. A caller must not be able
///         to modify that array (it is shared by all tokens of the sentence), so the property that exposes a slice
///         returns this class, which implements only <see cref="IReadOnlyList{T}"/>: there is no writable interface to
///         cast to, and the array itself is private.
///     </para>
///     <para>
///         Instances are immutable views and therefore thread-safe as long as the array is not modified, which only the
///         decoder does and only before it publishes the result.
///     </para>
/// </remarks>
internal sealed class ScoreSlice : IReadOnlyList<TagScore>
{
    /// <summary>
    ///     The array that holds the scores.
    /// </summary>
    private readonly TagScore[] _array;

    /// <summary>
    ///     The index of the first element of the slice.
    /// </summary>
    private readonly int _offset;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ScoreSlice"/> class.
    /// </summary>
    /// <param name="array">The array.</param>
    /// <param name="offset">The index of the first element of the slice.</param>
    /// <param name="count">The number of elements of the slice.</param>
    public ScoreSlice(TagScore[] array, int offset, int count)
    {
        _array = array;
        _offset = offset;
        Count = count;
    }

    /// <summary>
    ///     Gets the number of elements.
    /// </summary>
    public int Count { get; }

    /// <summary>
    ///     Gets an element.
    /// </summary>
    /// <param name="index">The index in the slice.</param>
    /// <returns>The element.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the slice.</exception>
    public TagScore this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "The index is outside the list.");
            }

            return _array[_offset + index];
        }
    }

    /// <summary>
    ///     Enumerates the elements in order.
    /// </summary>
    /// <returns>An enumerator.</returns>
    public IEnumerator<TagScore> GetEnumerator()
    {
        for (var i = 0; i < Count; i++)
        {
            yield return _array[_offset + i];
        }
    }

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
