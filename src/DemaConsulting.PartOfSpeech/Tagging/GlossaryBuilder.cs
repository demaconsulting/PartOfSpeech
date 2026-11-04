using System.Collections;

namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     Collects the entries of a <see cref="Glossary"/> and builds it.
/// </summary>
/// <remarks>
///     <para>
///         The builder is made for the C# collection initializer: it implements <see cref="IEnumerable{T}"/> of
///         <see cref="GlossaryEntry"/> and has <c>Add</c> methods, so a glossary reads like the list of terms that it is.
///         Settings that apply to all entries go into the <see cref="GlossaryOptions"/> that is passed to the
///         constructor (an initializer cannot assign members and add elements in one block). The options are validated
///         and copied by the constructor.
///     </para>
///     <para>
///         Every <c>Add</c> checks for a duplicate under the effective case rule right away, so the mistake is reported at
///         the line that made it. <see cref="Build"/> takes a snapshot: the builder stays usable and later additions
///         do not change a glossary that was built.
///     </para>
///     <para>
///         <strong>Thread safety:</strong> a builder is <em>not</em> thread-safe; use it from one thread. The glossary
///         that it builds is immutable and thread-safe.
///     </para>
/// </remarks>
/// <example>
///     Build a glossary with the collection initializer; strings and tags make short entries, and a full
///     <see cref="GlossaryEntry"/> can be added for the special cases:
///     <code>
///     Glossary glossary = new GlossaryBuilder
///     {
///         { "bleed valve", Upos.Noun, "A valve that releases trapped air." },
///         { "isolate", UposSet.Of(Upos.Verb) },
///         new GlossaryEntry("LRU", Upos.Noun).WithCaseSensitive(true),
///     }.Build();
///
///     Console.WriteLine(glossary.Count);   // 3
///     </code>
///     Use soft mode for every entry by passing options to the constructor:
///     <code>
///     var builder = new GlossaryBuilder(new GlossaryOptions { Mode = GlossaryMode.Soft })
///     {
///         { "torque wrench", Upos.Noun },
///     };
///     Glossary glossary = builder.Build();
///     </code>
/// </example>
public sealed class GlossaryBuilder : IEnumerable<GlossaryEntry>
{
    /// <summary>
    ///     The entries added so far.
    /// </summary>
    private readonly List<GlossaryEntry> _entries = new();

    /// <summary>
    ///     The duplicate keys of the entries added so far.
    /// </summary>
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);

    /// <summary>
    ///     The validated copy of the options.
    /// </summary>
    private readonly GlossaryOptions _options;

    /// <summary>
    ///     Initializes a new instance of the <see cref="GlossaryBuilder"/> class.
    /// </summary>
    /// <param name="options">The settings that apply to every entry that does not say otherwise, or <c>null</c> for the defaults. They are copied.</param>
    /// <exception cref="ArgumentOutOfRangeException">The mode of <paramref name="options"/> is undefined, or its <see cref="GlossaryOptions.SoftStrength"/> is not finite and greater than zero.</exception>
    public GlossaryBuilder(GlossaryOptions? options = null)
    {
        _options = GlossaryOptions.CopyOf(options);
    }

    /// <summary>
    ///     Gets the number of entries added so far.
    /// </summary>
    public int Count => _entries.Count;

    /// <summary>
    ///     Adds an entry.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <returns>This builder, so that calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entry"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">The glossary already has an entry with the same tokens under the same effective case rule. Merge the tags into one entry instead.</exception>
    public GlossaryBuilder Add(GlossaryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!_keys.Add(entry.IdentityKey(entry.CaseSensitive ?? _options.CaseSensitive)))
        {
            throw new ArgumentException($"The glossary already has an entry for the term '{entry.Term}' under the same case rule.", nameof(entry));
        }

        _entries.Add(entry);
        return this;
    }

    /// <summary>
    ///     Adds a term with one tag.
    /// </summary>
    /// <param name="term">The term: one word or several words separated by white space.</param>
    /// <param name="tag">The tag that the head word of the term takes.</param>
    /// <param name="definition">An optional definition, carried through to the results.</param>
    /// <returns>This builder, so that calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="term"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="term"/> is empty or has no token with a letter or a digit, or the term is a duplicate (see <see cref="Add(GlossaryEntry)"/>).</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tag"/> is not one of the 17 defined values.</exception>
    public GlossaryBuilder Add(string term, Upos tag, string? definition = null) =>
        Add(new GlossaryEntry(term, tag, definition));

    /// <summary>
    ///     Adds a term with several allowed tags.
    /// </summary>
    /// <param name="term">The term: one word or several words separated by white space.</param>
    /// <param name="tags">The tags that the head word of the term may take; at least one.</param>
    /// <param name="definition">An optional definition, carried through to the results.</param>
    /// <returns>This builder, so that calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="term"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="term"/> is empty or has no token with a letter or a digit, <paramref name="tags"/> is empty, or the term is a duplicate (see <see cref="Add(GlossaryEntry)"/>).</exception>
    public GlossaryBuilder Add(string term, UposSet tags, string? definition = null) =>
        Add(new GlossaryEntry(term, tags, definition));

    /// <summary>
    ///     Builds an immutable glossary from the entries added so far.
    /// </summary>
    /// <remarks>
    ///     The glossary is a snapshot: adding entries afterwards does not change it, and the builder stays usable.
    ///     Building costs a pass over all entries (tokenizing is done when an entry is created; the inflected forms and the
    ///     index are made here).
    /// </remarks>
    /// <returns>The glossary.</returns>
    public Glossary Build() => Glossary.Create(_entries, _options);

    /// <summary>
    ///     Gets an enumerator over the entries added so far, in order.
    /// </summary>
    /// <remarks>
    ///     The interface exists so that the collection initializer works; enumerate a built <see cref="Glossary"/> through
    ///     <see cref="Glossary.Entries"/> instead.
    /// </remarks>
    /// <returns>An enumerator over a snapshot of the entries.</returns>
    public IEnumerator<GlossaryEntry> GetEnumerator() => _entries.ToList().GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
