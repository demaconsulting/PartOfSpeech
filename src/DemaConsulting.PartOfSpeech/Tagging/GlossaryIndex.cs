// cspell:ignore CCDUL
namespace DemaConsulting.PartOfSpeech;

/// <summary>
///     One spelling of a glossary entry that the matcher looks for: the term, a listed variant or a generated inflected form.
/// </summary>
/// <remarks>
///     Forms are immutable after the index is built. Several forms may belong to one entry (the term, its variants, the
///     inflected forms of its head word, and the term as the tokenizer splits it at the end of a text).
/// </remarks>
internal sealed class GlossaryForm
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="GlossaryForm"/> class.
    /// </summary>
    /// <param name="entryIndex">The index of the entry in the glossary.</param>
    /// <param name="headOffset">The index of the head token within the form.</param>
    /// <param name="tags">The tags of the head token for this form.</param>
    /// <param name="rank">The priority of the form: 0 the term, 1 a listed variant, 2 a generated form (lower wins on equal spellings).</param>
    /// <param name="caseSensitive">Whether the exact letter case must match.</param>
    /// <param name="exact">The folded tokens with their case.</param>
    /// <param name="lower">The folded, lower-cased tokens.</param>
    public GlossaryForm(int entryIndex, int headOffset, UposSet tags, int rank, bool caseSensitive, string[] exact, string[] lower)
    {
        EntryIndex = entryIndex;
        HeadOffset = headOffset;
        Tags = tags;
        Rank = rank;
        CaseSensitive = caseSensitive;
        Exact = exact;
        Lower = lower;
    }

    /// <summary>
    ///     Gets the index of the entry in the glossary.
    /// </summary>
    public int EntryIndex { get; }

    /// <summary>
    ///     Gets the index of the head token within the form.
    /// </summary>
    public int HeadOffset { get; }

    /// <summary>
    ///     Gets the tags of the head token for this form.
    /// </summary>
    public UposSet Tags { get; private set; }

    /// <summary>
    ///     Gets the priority of the form (0 the term, 1 a listed variant, 2 a generated form).
    /// </summary>
    public int Rank { get; }

    /// <summary>
    ///     Gets a value that indicates whether the exact letter case must match.
    /// </summary>
    public bool CaseSensitive { get; }

    /// <summary>
    ///     Gets the folded tokens with their case.
    /// </summary>
    public string[] Exact { get; }

    /// <summary>
    ///     Gets the folded, lower-cased tokens (the key in the trie).
    /// </summary>
    public string[] Lower { get; }

    /// <summary>
    ///     Gets a value that indicates whether the form is not the term as written (a variant or an inflected form).
    /// </summary>
    public bool IsVariant => Rank > 0;

    /// <summary>
    ///     Gets the identifier of the tag set of the form in the index (assigned when the index is built).
    /// </summary>
    public int TagSetId { get; set; }

    /// <summary>
    ///     Gets the index of the next form that ends at the same trie node, or -1 (assigned when the index is built).
    /// </summary>
    public int Next { get; set; }

    /// <summary>
    ///     Unites the tags of this form with the tags of a form of the same entry, rank and spelling.
    /// </summary>
    /// <param name="other">The tags to add.</param>
    public void UniteTags(UposSet other) => Tags = Tags.Union(other);
}

/// <summary>
///     The compiled lookup structure of a glossary: a trie over the folded, lower-cased tokens of every spelling, with a
///     leftmost-longest matcher that does not allocate per token.
/// </summary>
/// <remarks>
///     <para>
///         <strong>Why a trie over tokens:</strong> a sentence is matched against thousands of terms of one to a few
///         words. Walking the tokens from every start position through a trie costs at most the length of the longest term
///         per token, whatever the glossary size, and it yields the longest match at a position for free.
///     </para>
///     <para>
///         <strong>Layout:</strong> the trie nodes and edges are flat arrays built once. An edge is found with an
///         open-addressing table keyed by the parent node and a 64-bit hash of the folded lower-case token; the stored
///         key is compared with the token's characters, so a hash collision never produces a wrong match. Each node that
///         ends a spelling points to a chain of <see cref="GlossaryForm"/> objects (at most one case-insensitive form and
///         any number of case-sensitive forms that differ in letter case).
///     </para>
///     <para>
///         <strong>Allocation:</strong> <see cref="Find"/> writes into a per-thread <see cref="GlossaryWorkspace"/> and
///         allocates nothing for tokens whose characters are ASCII, Latin-1 or the typographic quotes, dashes and
///         ellipsis that the tagger folds (the tokens of ordinary English text). A token with any other character takes
///         a slow path through <see cref="FeatureExtractor.Fold(string)"/> (Unicode normalization), which allocates.
///     </para>
///     <para>
///         <strong>Thread safety:</strong> the index is immutable after construction and may be searched from any
///         number of threads, each with its own workspace.
///     </para>
/// </remarks>
internal sealed class GlossaryIndex
{
    /// <summary>
    ///     The first character that may need Unicode normalization; below it NFC changes nothing.
    /// </summary>
    private const char FirstNormalizableChar = '\u0300';

    /// <summary>
    ///     The scratch memory of the current thread.
    /// </summary>
    [ThreadStatic]
    private static GlossaryWorkspace? _threadWorkspace;

    /// <summary>
    ///     The mask applied to every hash (all ones in production; a test narrows it to force collisions).
    /// </summary>
    private readonly ulong _hashMask;

    /// <summary>
    ///     The forms in a fixed order.
    /// </summary>
    private readonly GlossaryForm[] _forms;

    /// <summary>
    ///     The index of the first form that ends at each node, or -1.
    /// </summary>
    private readonly int[] _nodeForm;

    /// <summary>
    ///     The parent node of each slot of the edge table, or -1 for an empty slot.
    /// </summary>
    private readonly int[] _edgeParent;

    /// <summary>
    ///     The hash of the key of each slot of the edge table.
    /// </summary>
    private readonly ulong[] _edgeHash;

    /// <summary>
    ///     The key (the lower-case folded token) of each slot of the edge table.
    /// </summary>
    private readonly string?[] _edgeKey;

    /// <summary>
    ///     The child node of each slot of the edge table.
    /// </summary>
    private readonly int[] _edgeChild;

    /// <summary>
    ///     The slot mask (the table size minus one; the size is a power of two).
    /// </summary>
    private readonly int _slotMask;

    /// <summary>
    ///     Initializes a new instance of the <see cref="GlossaryIndex"/> class.
    /// </summary>
    /// <param name="entries">The entries of the glossary, in order.</param>
    /// <param name="options">The validated options of the glossary.</param>
    /// <param name="hashMask">The mask applied to every hash; leave it at the default except in tests that force collisions.</param>
    public GlossaryIndex(IReadOnlyList<GlossaryEntry> entries, GlossaryOptions options, ulong hashMask = ulong.MaxValue)
    {
        _hashMask = hashMask;

        // Collect every spelling, resolving equal spellings by rank and order
        var forms = CollectForms(entries, options);
        _forms = forms.ToArray();

        // Number the distinct head tag sets so that the constraint tables can be indexed by a small integer
        var tagSetIds = new Dictionary<int, int>();
        var masks = new List<int>();
        foreach (var form in _forms)
        {
            if (!tagSetIds.TryGetValue(form.Tags.Mask, out var id))
            {
                id = masks.Count;
                tagSetIds[form.Tags.Mask] = id;
                masks.Add(form.Tags.Mask);
            }

            form.TagSetId = id;
        }

        TagSetMasks = masks.ToArray();

        // Build the trie, then freeze it into flat arrays
        var children = new Dictionary<(int Parent, string Key), int>();
        var nodeForms = new List<int> { -1 };
        for (var f = 0; f < _forms.Length; f++)
        {
            var node = 0;
            foreach (var key in _forms[f].Lower)
            {
                if (!children.TryGetValue((node, key), out var child))
                {
                    child = nodeForms.Count;
                    nodeForms.Add(-1);
                    children[(node, key)] = child;
                }

                node = child;
            }

            _forms[f].Next = nodeForms[node];
            nodeForms[node] = f;
        }

        _nodeForm = nodeForms.ToArray();
        NodeCount = _nodeForm.Length;

        var size = 4;
        while (size < children.Count * 2)
        {
            size <<= 1;
        }

        _slotMask = size - 1;
        _edgeParent = new int[size];
        _edgeHash = new ulong[size];
        _edgeKey = new string?[size];
        _edgeChild = new int[size];
        for (var s = 0; s < size; s++)
        {
            _edgeParent[s] = -1;
        }
        foreach (var edge in children)
        {
            var hash = HashOf(edge.Key.Key) & _hashMask;
            var slot = SlotOf(edge.Key.Parent, hash, _slotMask);
            while (_edgeParent[slot] >= 0)
            {
                slot = (slot + 1) & _slotMask;
            }

            _edgeParent[slot] = edge.Key.Parent;
            _edgeHash[slot] = hash;
            _edgeKey[slot] = edge.Key.Key;
            _edgeChild[slot] = edge.Value;
        }
    }

    /// <summary>
    ///     Gets the distinct head tag sets of the forms, as masks; a form's <see cref="GlossaryForm.TagSetId"/> indexes this array.
    /// </summary>
    public int[] TagSetMasks { get; }

    /// <summary>
    ///     Gets the number of forms (spellings) in the index.
    /// </summary>
    public int FormCount => _forms.Length;

    /// <summary>
    ///     Gets the number of trie nodes, including the root.
    /// </summary>
    public int NodeCount { get; }

    /// <summary>
    ///     Gets the per-thread workspace, creating it on first use.
    /// </summary>
    /// <returns>The workspace; only the calling thread may use it.</returns>
    public static GlossaryWorkspace GetThreadWorkspace()
    {
        var workspace = _threadWorkspace;
        if (workspace == null)
        {
            workspace = new GlossaryWorkspace();
            _threadWorkspace = workspace;
        }

        return workspace;
    }

    /// <summary>
    ///     Folds a token to the string that the matcher compares: NFC, typographic quotes, dashes and ellipsis mapped to ASCII, optionally lower-cased.
    /// </summary>
    /// <remarks>
    ///     This is the readable twin of the character-buffer folding that <see cref="Find"/> does on sentence tokens (the
    ///     tests prove that both agree). Lower-casing is done character by character, like the matcher does.
    /// </remarks>
    /// <param name="token">The token as written.</param>
    /// <param name="lowerCase">Whether to lower-case the result.</param>
    /// <returns>The folded token.</returns>
    public static string FoldToString(string token, bool lowerCase)
    {
        var folded = FeatureExtractor.Fold(token);
        if (!lowerCase)
        {
            return folded;
        }

        var chars = folded.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = char.ToLowerInvariant(chars[i]);
        }

        return new string(chars);
    }

    /// <summary>
    ///     Finds the matches in the tokens of a sentence: leftmost, longest, without overlaps.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         At every position (from the left) the trie is walked as deep as the tokens allow and the longest spelling
    ///         whose case rule is satisfied wins; the search continues after it. A case-sensitive form beats a
    ///         case-insensitive one of the same length when its exact letters match.
    ///     </para>
    ///     <para>
    ///         The matches are left in <paramref name="workspace"/> (<see cref="GlossaryWorkspace.MatchTotal"/>); the
    ///         method allocates nothing once the workspace has grown to the sentence, except for tokens with exotic
    ///         characters (see the remarks of the class).
    ///     </para>
    /// </remarks>
    /// <param name="tokens">The tokens of the sentence.</param>
    /// <param name="workspace">The workspace of the calling thread.</param>
    /// <returns>The number of matches.</returns>
    public int Find(Token[] tokens, GlossaryWorkspace workspace)
    {
        var count = tokens.Length;
        workspace.Begin(count);
        if (_forms.Length == 0 || count == 0)
        {
            return 0;
        }

        // Fold and hash every token once, so that the walk from each start position reuses them
        var position = 0;
        for (var i = 0; i < count; i++)
        {
            position = FoldToken(tokens[i].Text, workspace, i, position);
        }

        // Leftmost-longest: take the longest match that starts at the current token and continue after it
        var chars = workspace.Chars;
        var index = 0;
        while (index < count)
        {
            var node = 0;
            var bestForm = -1;
            var bestLength = 0;
            for (var j = index; j < count; j++)
            {
                node = Child(node, workspace.Hash[j], chars, workspace.Start[j] + workspace.Length[j], workspace.Length[j]);
                if (node < 0)
                {
                    break;
                }

                if (_nodeForm[node] >= 0)
                {
                    var form = SelectForm(_nodeForm[node], workspace, index, j - index + 1);
                    if (form >= 0)
                    {
                        bestForm = form;
                        bestLength = j - index + 1;
                    }
                }
            }

            if (bestForm >= 0)
            {
                workspace.AddMatch(bestForm, index, bestLength);
                index += bestLength;
            }
            else
            {
                index++;
            }
        }

        return workspace.MatchTotal;
    }

    /// <summary>
    ///     Gets a form by its index.
    /// </summary>
    /// <param name="index">The form index from <see cref="GlossaryWorkspace.MatchForm"/>.</param>
    /// <returns>The form.</returns>
    public GlossaryForm GetForm(int index) => _forms[index];

    /// <summary>
    ///     Gets the exact folded and the lower-case folded form of a token the way <see cref="Find"/> computes them (for tests).
    /// </summary>
    /// <param name="token">The token.</param>
    /// <param name="workspace">A workspace to use.</param>
    /// <param name="lowerCase">Whether to return the lower-case form.</param>
    /// <returns>The folded form.</returns>
    public static string FoldWithWorkspace(string token, GlossaryWorkspace workspace, bool lowerCase)
    {
        workspace.Begin(1);
        FoldToken(token, workspace, 0, 0);
        var start = workspace.Start[0] + (lowerCase ? workspace.Length[0] : 0);
        return new string(workspace.Chars, start, workspace.Length[0]);
    }

    /// <summary>
    ///     Collects every spelling of every entry and resolves equal spellings.
    /// </summary>
    /// <remarks>
    ///     Equal spellings (same lower-case tokens under the same case rule, and for case-sensitive forms the same exact
    ///     tokens) collapse into one form: a better rank wins (the term before a listed variant before a generated form),
    ///     and for the same rank the earlier entry wins, except that generated forms of the same entry unite their tags
    ///     (<c>bleeds</c> as a plural and as a third-person form).
    /// </remarks>
    /// <param name="entries">The entries.</param>
    /// <param name="options">The options.</param>
    /// <returns>The distinct forms in the order of their first appearance.</returns>
    private static List<GlossaryForm> CollectForms(IReadOnlyList<GlossaryEntry> entries, GlossaryOptions options)
    {
        var order = new List<GlossaryForm>();
        var byKey = new Dictionary<string, GlossaryForm>(StringComparer.Ordinal);

        void Add(int entryIndex, string[] tokens, int head, UposSet tags, int rank, bool caseSensitive)
        {
            var exact = new string[tokens.Length];
            var lower = new string[tokens.Length];
            for (var k = 0; k < tokens.Length; k++)
            {
                exact[k] = FoldToString(tokens[k], false);
                lower[k] = FoldToString(tokens[k], true);
            }

            var key = SpellingKey(caseSensitive, exact, lower);
            if (!byKey.TryGetValue(key, out var existing))
            {
                var form = new GlossaryForm(entryIndex, head, tags, rank, caseSensitive, exact, lower);
                byKey[key] = form;
                order.Add(form);
                return;
            }

            // A better rank replaces the existing form in place (so the order stays deterministic); the same rank of the same entry unites tags
            if (rank < existing.Rank)
            {
                var index = order.IndexOf(existing);
                var form = new GlossaryForm(entryIndex, head, tags, rank, caseSensitive, exact, lower);
                byKey[key] = form;
                order[index] = form;
            }
            else if (rank == existing.Rank && rank == 2 && entryIndex == existing.EntryIndex)
            {
                existing.UniteTags(tags);
            }
        }

        for (var e = 0; e < entries.Count; e++)
        {
            var entry = entries[e];
            var caseSensitive = entry.CaseSensitive ?? options.CaseSensitive;
            var inflect = entry.Inflect ?? options.Inflect;
            var head = entry.HeadIndex;

            // The term as written (and as split at the end of a text), then the listed variants
            Add(e, entry.TokenArray, head, entry.Tags, 0, caseSensitive);
            if (entry.EndTokens != null)
            {
                Add(e, entry.EndTokens, head, entry.Tags, 0, caseSensitive);
            }

            for (var v = 0; v < entry.VariantTokens.Length; v++)
            {
                Add(e, entry.VariantTokens[v], head, entry.Tags, 1, caseSensitive);
                var end = entry.VariantEndTokens[v];
                if (end != null)
                {
                    Add(e, end, head, entry.Tags, 1, caseSensitive);
                }
            }

            // The generated forms of the head word only
            if (inflect)
            {
                foreach (var inflected in GlossaryInflector.Inflect(entry.TokenArray[head], entry.Tags))
                {
                    var tokens = (string[])entry.TokenArray.Clone();
                    tokens[head] = inflected.Text;
                    Add(e, tokens, head, inflected.Tags, 2, caseSensitive);
                }
            }
        }

        return order;
    }

    /// <summary>
    ///     Builds the key that identifies a spelling.
    /// </summary>
    /// <param name="caseSensitive">The case rule.</param>
    /// <param name="exact">The exact folded tokens.</param>
    /// <param name="lower">The lower-case folded tokens.</param>
    /// <returns>The key; each token is written with its length so no sequence can be mistaken for another.</returns>
    private static string SpellingKey(bool caseSensitive, string[] exact, string[] lower)
    {
        var builder = new System.Text.StringBuilder(caseSensitive ? "s:" : "i:");
        foreach (var token in caseSensitive ? exact : lower)
        {
            builder.Append(token.Length).Append(':').Append(token);
        }

        return builder.ToString();
    }

    /// <summary>
    ///     Folds one token into the workspace and computes its hash.
    /// </summary>
    /// <remarks>
    ///     The exact folded form is written at <paramref name="position"/> and the lower-case form directly behind it.
    ///     Tokens whose characters are all below U+0300 (where Unicode normalization changes nothing) or are one of the
    ///     typographic quotes, dashes or the ellipsis are folded in place without allocating; any other token is
    ///     normalized by <see cref="FeatureExtractor.Fold(string)"/> and copied (which allocates).
    /// </remarks>
    /// <param name="token">The token as written.</param>
    /// <param name="workspace">The workspace.</param>
    /// <param name="index">The index of the token.</param>
    /// <param name="position">The first free position of the character buffer.</param>
    /// <returns>The first free position after the token.</returns>
    private static int FoldToken(string token, GlossaryWorkspace workspace, int index, int position)
    {
        // The widest fold is the ellipsis character that becomes three periods; reserve exact and lower-case copies
        workspace.EnsureChars(position, token.Length * 6);
        if (!TryFoldFast(token, workspace.Chars, position, out var length))
        {
            var folded = FeatureExtractor.Fold(token);
            length = folded.Length;
            workspace.EnsureChars(position, length * 2);
            folded.CopyTo(0, workspace.Chars, position, length);
        }

        // Lower-case copy and its hash; the character-wise mapping is the one that FoldToString uses
        var chars = workspace.Chars;
        var hash = 14695981039346656037UL;
        unchecked
        {
            for (var i = 0; i < length; i++)
            {
                var lower = char.ToLowerInvariant(chars[position + i]);
                chars[position + length + i] = lower;
                hash = (hash ^ lower) * 1099511628211UL;
            }
        }

        workspace.Start[index] = position;
        workspace.Length[index] = length;
        workspace.Hash[index] = Finish(hash);
        return position + (2 * length);
    }

    /// <summary>
    ///     Folds a token into a buffer when no Unicode normalization is needed.
    /// </summary>
    /// <param name="token">The token as written.</param>
    /// <param name="chars">The buffer, with room for three characters per character of the token from <paramref name="position"/> on.</param>
    /// <param name="position">The first position to write.</param>
    /// <param name="length">Receives the number of characters written.</param>
    /// <returns><c>false</c> when the token has a character that may need normalization; nothing is guaranteed about the buffer then.</returns>
    private static bool TryFoldFast(string token, char[] chars, int position, out int length)
    {
        var written = 0;
        foreach (var c in token)
        {
            switch (c)
            {
                case '\u2018':
                case '\u2019':
                case '\u201B':
                case '\u02BC':
                    chars[position + written++] = '\'';
                    break;
                case '\u201C':
                case '\u201D':
                case '\u201E':
                    chars[position + written++] = '"';
                    break;
                case '\u2013':
                case '\u2014':
                case '\u2015':
                    chars[position + written++] = '-';
                    chars[position + written++] = '-';
                    break;
                case '\u2026':
                    chars[position + written++] = '.';
                    chars[position + written++] = '.';
                    chars[position + written++] = '.';
                    break;
                default:
                    if (c >= FirstNormalizableChar)
                    {
                        length = 0;
                        return false;
                    }

                    chars[position + written++] = c;
                    break;
            }
        }

        length = written;
        return true;
    }

    /// <summary>
    ///     Computes the hash of a folded lower-case string, equal to the hash that <see cref="FoldToken"/> computes for the same characters.
    /// </summary>
    /// <param name="key">The folded lower-case token.</param>
    /// <returns>The hash before the test mask.</returns>
    private static ulong HashOf(string key)
    {
        var hash = 14695981039346656037UL;
        unchecked
        {
            foreach (var c in key)
            {
                hash = (hash ^ c) * 1099511628211UL;
            }
        }

        return Finish(hash);
    }

    /// <summary>
    ///     Spreads the bits of a running hash so that the low bits are well mixed.
    /// </summary>
    /// <param name="hash">The running hash.</param>
    /// <returns>The final hash.</returns>
    private static ulong Finish(ulong hash)
    {
        unchecked
        {
            hash ^= hash >> 33;
            hash *= 0xFF51AFD7ED558CCDUL;
            hash ^= hash >> 33;
            return hash;
        }
    }

    /// <summary>
    ///     Computes the first slot to probe for an edge.
    /// </summary>
    /// <param name="parent">The parent node.</param>
    /// <param name="hash">The hash of the key.</param>
    /// <param name="mask">The slot mask.</param>
    /// <returns>The slot.</returns>
    private static int SlotOf(int parent, ulong hash, int mask)
    {
        unchecked
        {
            var mixed = hash ^ ((ulong)parent * 0x9E3779B97F4A7C15UL);
            mixed ^= mixed >> 32;
            mixed *= 0xD6E8FEB86659FD93UL;
            mixed ^= mixed >> 32;
            return (int)(mixed & (ulong)mask);
        }
    }

    /// <summary>
    ///     Finds the child of a node for a token.
    /// </summary>
    /// <param name="parent">The node.</param>
    /// <param name="hash">The hash of the token's lower-case folded form (with the mask applied here).</param>
    /// <param name="chars">The character buffer of the workspace.</param>
    /// <param name="start">The start of the lower-case folded form in the buffer.</param>
    /// <param name="length">The length of the form.</param>
    /// <returns>The child node, or -1 when the token does not continue any spelling.</returns>
    private int Child(int parent, ulong hash, char[] chars, int start, int length)
    {
        var masked = hash & _hashMask;
        var slot = SlotOf(parent, masked, _slotMask);
        while (_edgeParent[slot] >= 0)
        {
            // The hash is only a filter: the characters decide
            if (_edgeParent[slot] == parent && _edgeHash[slot] == masked && SameChars(_edgeKey[slot]!, chars, start, length))
            {
                return _edgeChild[slot];
            }

            slot = (slot + 1) & _slotMask;
        }

        return -1;
    }

    /// <summary>
    ///     Chooses the form that applies to a span of tokens that ends at a trie node.
    /// </summary>
    /// <param name="first">The first form of the node's chain.</param>
    /// <param name="workspace">The workspace holding the folded tokens.</param>
    /// <param name="firstToken">The first token of the span.</param>
    /// <param name="count">The number of tokens of the span.</param>
    /// <returns>The index of the chosen form, or -1 when no form of the node accepts the case of the tokens.</returns>
    private int SelectForm(int first, GlossaryWorkspace workspace, int firstToken, int count)
    {
        // A case-sensitive form whose exact letters match wins; otherwise the (single) case-insensitive form applies
        var insensitive = -1;
        for (var f = first; f >= 0; f = _forms[f].Next)
        {
            var form = _forms[f];
            if (!form.CaseSensitive)
            {
                insensitive = f;
                continue;
            }

            var same = true;
            for (var k = 0; k < count && same; k++)
            {
                same = SameChars(form.Exact[k], workspace.Chars, workspace.Start[firstToken + k], workspace.Length[firstToken + k]);
            }

            if (same)
            {
                return f;
            }
        }

        return insensitive;
    }

    /// <summary>
    ///     Compares a string with characters of a buffer.
    /// </summary>
    /// <param name="text">The string.</param>
    /// <param name="chars">The buffer.</param>
    /// <param name="start">The start of the characters.</param>
    /// <param name="length">The number of characters.</param>
    /// <returns><c>true</c> if they are the same sequence.</returns>
    private static bool SameChars(string text, char[] chars, int start, int length)
    {
        if (text.Length != length)
        {
            return false;
        }

        for (var i = 0; i < length; i++)
        {
            if (text[i] != chars[start + i])
            {
                return false;
            }
        }

        return true;
    }
}
