using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace DemaConsulting.PartOfSpeech.Trainer;

/// <summary>
///     Computes the first-pass (preview) tags that the training of the <c>rightPreview</c> feature group reads: every sentence is
///     tagged by a model that did not see its document (a jackknife, also called cross-fitting).
/// </summary>
/// <remarks>
///     <para>
///         The runtime decodes a sentence twice with this group: the first pass chooses tags without the group's features, the
///         second pass reads the tags of the next two tokens. A model that is trained on the preview tags of its own training
///         sentences would see tags that are better than the ones it gets on new text, so the training uses tags that
///         fold models made on text they had not seen.
///     </para>
///     <para>
///         The folds are made from groups of documents. A document is the <c>source_id</c> of the corpus (the sentence id when a
///         sentence has none). Two documents belong to the same group when they share a sentence key (the lower-case letters and
///         numbers of the text, the sentence key of the leakage rule of <c>data/README.md</c>), directly or through a chain of other
///         documents, so an exact duplicate sentence under two document ids never lands in two folds. The fold of a group is the
///         FNV-1a hash of its smallest document id (ordinal order) modulo the number of folds, so a document is never split and
///         the folds do not depend on the order of the files, on the seed, on the feature groups or on the platform: the same
///         sentences are in the same fold for every run, and no sentence of a held-out fold has its key in the training set of the
///         fold model. Every fold model is trained on the other folds without the <c>rightPreview</c> group, on the other feature
///         groups of the run, with the settings and the seed of the run, and tags its own fold exactly as the first pass of the
///         decoder does: the quantized weights, the lexicon restriction, the predicted history. Known and accepted mismatch: the
///         first pass at run time uses the final model, the preview tags of the training come from the fold models.
///     </para>
///     <para>
///         The class is stateless and, like the training, single-threaded. It is engine code and uses no file API.
///     </para>
/// </remarks>
internal static class Jackknife
{
    /// <summary>
    ///     Gets the document id that decides the fold of a sentence.
    /// </summary>
    /// <param name="sentence">The sentence.</param>
    /// <returns>The document id (<c>source_id</c>), or the sentence id when there is none.</returns>
    public static string DocumentOf(TrainingSentence sentence)
    {
        ArgumentNullException.ThrowIfNull(sentence);
        return sentence.SourceId.Length > 0 ? sentence.SourceId : sentence.SentenceId;
    }

    /// <summary>
    ///     Gets the sentence key of a sentence: the lower-case letters and numbers of its tokens, exactly as the leakage rule
    ///     computes it.
    /// </summary>
    /// <remarks>
    ///     This is the key of the leakage rule (<c>data/README.md</c>, the leakage check; <c>key</c> of <c>tools/data/common.py</c>),
    ///     so two sentences that differ in spacing, case or punctuation only have the same key. See <see cref="SentenceKey(string)"/>
    ///     for the definition.
    /// </remarks>
    /// <param name="sentence">The sentence.</param>
    /// <returns>The key; empty when the sentence has no letter and no digit (an empty key never matches another sentence).</returns>
    public static string SentenceKey(TrainingSentence sentence)
    {
        ArgumentNullException.ThrowIfNull(sentence);
        return SentenceKey(string.Concat(sentence.Tokens));
    }

    /// <summary>
    ///     Gets the sentence key of a text: the code points of its NFC form, in lower case, that are letters or numbers.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The definition is the one of the Python leakage check, <c>"".join(ch for ch in nfc(text).lower() if ch.isalnum())</c>, which
    ///         works on code points, so the supplementary planes are letters and numbers too. The equivalent steps here are: iterate by
    ///         code point (a surrogate pair is one code point); lower-case every code point with the simple mapping of the invariant
    ///         culture, except that the supplementary-plane mapping (the Deseret, Osage, Vithkuqi, Old Hungarian, Warang Citi, Medefaidrin
    ///         and Adlam letters) is listed here because the runtimes differ in it; map <c>U+0130</c> to <c>i</c> and its combining dot
    ///         (which is no letter); map the capital sigma to the final sigma where Python does (a cased letter before it and none
    ///         behind it, ignoring case-ignorable code points in between); keep the code points of the categories letter (<c>L*</c>)
    ///         and number (<c>N*</c>: decimal digits, letter numbers such as the Roman numerals, and other numbers such as the
    ///         superscripts and the fractions), which are the code points for which <c>str.isalnum()</c> is true.
    ///     </para>
    ///     <para>
    ///         The parity targets the Unicode version of the Python normalizer (Python 3.13: Unicode 15.1) for the code points that it
    ///         assigns; the mechanism for a code point whose general category differs between its tables and the runtime's is
    ///         <see cref="CategoryOverrides"/>, which is derived from an exhaustive comparison of every code point (in the contexts of
    ///         the sigma rules) and holds only U+1171E today. Code points that the Python version does not assign are outside the
    ///         parity; no sentence of the corpora has one.
    ///     </para>
    ///     <para>
    ///         The parity is pinned by the fixture <c>tools/data/tests/sentence-key-cases.tsv</c>, whose keys the Python function wrote.
    ///     </para>
    /// </remarks>
    /// <param name="text">The text.</param>
    /// <returns>The key; empty when the text has no letter and no number.</returns>
    public static string SentenceKey(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var normalized = text.Normalize(NormalizationForm.FormC);
        var key = new StringBuilder(normalized.Length);
        var index = 0;
        while (index < normalized.Length)
        {
            var codePoint = CodePointAt(normalized, index, out var width);
            var lower = codePoint == 0x03A3 ? SigmaAt(normalized, index) : ToLower(codePoint);
            if (IsLetterOrNumber(lower))
            {
                key.Append(char.ConvertFromUtf32(lower));
            }

            index += width;
        }

        return key.ToString();
    }

    /// <summary>
    ///     Lower-cases the capital sigma at an index: the final sigma where Python writes it, else the small sigma.
    /// </summary>
    private static int SigmaAt(string text, int index) => IsFinalSigma(text, index) ? 0x03C2 : 0x03C3;

    /// <summary>
    ///     Reads the code point at an index (a lone surrogate is its own code point).
    /// </summary>
    private static int CodePointAt(string text, int index, out int width)
    {
        if (char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
        {
            width = 2;
            return char.ConvertToUtf32(text[index], text[index + 1]);
        }

        width = 1;
        return text[index];
    }

    /// <summary>
    ///     Reads the code point that ends right before an index (a lone surrogate is its own code point).
    /// </summary>
    private static int CodePointBefore(string text, int index, out int width)
    {
        if (index >= 2 && char.IsLowSurrogate(text[index - 1]) && char.IsHighSurrogate(text[index - 2]))
        {
            width = 2;
            return char.ConvertToUtf32(text[index - 2], text[index - 1]);
        }

        width = 1;
        return text[index - 1];
    }

    /// <summary>
    ///     Lower-cases a code point with the simple (one to one) mapping; the combining dot of <c>U+0130</c> is no letter, so the
    ///     result for it is <c>i</c>.
    /// </summary>
    private static int ToLower(int codePoint)
    {
        if (codePoint == 0x0130)
        {
            return 'i';
        }

        if (codePoint <= 0xFFFF)
        {
            return char.ToLowerInvariant((char)codePoint);
        }

        // The supplementary-plane letters that have a lower case: the first, the last and the offset of every run
        foreach (var (first, last, offset) in SupplementaryLowerCase)
        {
            if (codePoint >= first && codePoint <= last)
            {
                return codePoint + offset;
            }
        }

        return codePoint;
    }

    /// <summary>
    ///     The runs of the supplementary-plane lower-case mapping (Unicode 15.1, as Python 3.13 has it).
    /// </summary>
    private static readonly (int First, int Last, int Offset)[] SupplementaryLowerCase =
    {
        (0x10400, 0x10427, 0x28),
        (0x104B0, 0x104D3, 0x28),
        (0x10570, 0x1057A, 0x27),
        (0x1057C, 0x1058A, 0x27),
        (0x1058C, 0x10592, 0x27),
        (0x10594, 0x10595, 0x27),
        (0x10C80, 0x10CB2, 0x40),
        (0x118A0, 0x118BF, 0x20),
        (0x16E40, 0x16E5F, 0x20),
        (0x1E900, 0x1E921, 0x22)
    };

    /// <summary>
    ///     Tells whether a code point is a letter or a number (the code points for which Python's <c>str.isalnum()</c> is true).
    /// </summary>
    private static bool IsLetterOrNumber(int codePoint)
    {
        switch (UnicodeCategoryOf(codePoint))
        {
            case UnicodeCategory.UppercaseLetter:
            case UnicodeCategory.LowercaseLetter:
            case UnicodeCategory.TitlecaseLetter:
            case UnicodeCategory.ModifierLetter:
            case UnicodeCategory.OtherLetter:
            case UnicodeCategory.DecimalDigitNumber:
            case UnicodeCategory.LetterNumber:
            case UnicodeCategory.OtherNumber:
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    ///     Gets the Unicode category of a code point (a lone surrogate has the category surrogate; a code point of
    ///     <see cref="CategoryOverrides"/> has the category of the Python tables).
    /// </summary>
    private static UnicodeCategory UnicodeCategoryOf(int codePoint)
    {
        if (CategoryOverrides.TryGetValue(codePoint, out var category))
        {
            return category;
        }

        return codePoint is >= 0xD800 and <= 0xDFFF
            ? UnicodeCategory.Surrogate
            : CharUnicodeInfo.GetUnicodeCategory(char.ConvertFromUtf32(codePoint), 0);
    }

    /// <summary>
    ///     The code points whose general category in Python 3.13 (Unicode 15.1) differs from the one of the runtime, so that the
    ///     rules that read categories (the letters and numbers of the key, and the case-ignorable code points of the final sigma)
    ///     give the Python result. U+1171E (Ahom consonant sign medial wa) is a nonspacing mark in Python and a spacing mark in the
    ///     newer Unicode data of .NET 10; as a case-ignorable mark it decides a final sigma next to it.
    /// </summary>
    private static readonly Dictionary<int, UnicodeCategory> CategoryOverrides = new()
    {
        [0x1171E] = UnicodeCategory.NonSpacingMark
    };

    /// <summary>
    ///     Tells whether a capital sigma at an index is a final sigma: a cased letter before it and none behind it, where the
    ///     case-ignorable code points in between do not count (the Final_Sigma rule of the Unicode standard, as Python applies it).
    /// </summary>
    private static bool IsFinalSigma(string text, int index)
    {
        var before = index;
        var precededByCased = false;
        while (before > 0)
        {
            var codePoint = CodePointBefore(text, before, out var width);
            before -= width;
            if (IsCaseIgnorable(codePoint))
            {
                continue;
            }

            precededByCased = IsCased(codePoint);
            break;
        }

        if (!precededByCased)
        {
            return false;
        }

        var after = index + 1;
        while (after < text.Length)
        {
            var codePoint = CodePointAt(text, after, out var width);
            after += width;
            if (IsCaseIgnorable(codePoint))
            {
                continue;
            }

            return !IsCased(codePoint);
        }

        return true;
    }

    /// <summary>
    ///     Tells whether a code point is cased: an upper-case, lower-case or title-case letter, or a letter-like code point that
    ///     has the property Other_Lowercase or Other_Uppercase (the indicator ordinals, the modifier letters, the Roman numerals and
    ///     the circled letters).
    /// </summary>
    private static bool IsCased(int codePoint)
    {
        var category = UnicodeCategoryOf(codePoint);
        if (category is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter)
        {
            return true;
        }

        foreach (var (first, last) in OtherCased)
        {
            if (codePoint >= first && codePoint <= last)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     The ranges of the properties Other_Lowercase and Other_Uppercase that are no cased category.
    /// </summary>
    private static readonly (int First, int Last)[] OtherCased =
    {
        (0x00AA, 0x00AA), (0x00BA, 0x00BA), (0x02B0, 0x02B8), (0x02C0, 0x02C1), (0x02E0, 0x02E4), (0x0345, 0x0345),
        (0x037A, 0x037A), (0x10FC, 0x10FC), (0x1D2C, 0x1D6A), (0x1D78, 0x1D78), (0x1D9B, 0x1DBF), (0x2071, 0x2071),
        (0x207F, 0x207F), (0x2090, 0x209C), (0x2160, 0x217F), (0x24B6, 0x24E9), (0x2C7C, 0x2C7D), (0xA69C, 0xA69D),
        (0xA770, 0xA770), (0xA7F8, 0xA7F9), (0xAB5C, 0xAB5F), (0x1F130, 0x1F149), (0x1F150, 0x1F169), (0x1F170, 0x1F189)
    };

    /// <summary>
    ///     Tells whether a code point is case-ignorable: a nonspacing or enclosing mark, a format character, a modifier letter or
    ///     symbol, or one of the apostrophes, full stops and colons that the word-break rules treat as part of a word.
    /// </summary>
    private static bool IsCaseIgnorable(int codePoint)
    {
        switch (UnicodeCategoryOf(codePoint))
        {
            case UnicodeCategory.NonSpacingMark:
            case UnicodeCategory.EnclosingMark:
            case UnicodeCategory.Format:
            case UnicodeCategory.ModifierLetter:
            case UnicodeCategory.ModifierSymbol:
                return true;
            default:
                return codePoint is 0x0027 or 0x002E or 0x003A or 0x00B7 or 0x0387 or 0x055F or 0x05F4 or 0x2018 or 0x2019
                    or 0x2024 or 0x2027 or 0xFE13 or 0xFE52 or 0xFE55 or 0xFF07 or 0xFF0E or 0xFF1A;
        }
    }

    /// <summary>
    ///     Groups the documents of the sentences by shared sentence keys.
    /// </summary>
    /// <remarks>
    ///     Documents are joined (union-find) when they share any non-empty sentence key, so a group is a connected component of the
    ///     documents. The group is named by its smallest document id in ordinal order, which does not depend on the order of the
    ///     sentences.
    /// </remarks>
    /// <param name="sentences">The sentences.</param>
    /// <returns>The group name (the smallest document id of the group) of every document.</returns>
    public static Dictionary<string, string> GroupDocuments(IReadOnlyList<TrainingSentence> sentences)
    {
        ArgumentNullException.ThrowIfNull(sentences);
        var parent = new Dictionary<string, string>(StringComparer.Ordinal);
        var firstDocumentOfKey = new Dictionary<string, string>(StringComparer.Ordinal);

        // Follows the parents to the root of a document and shortens the path on the way
        string Find(string document)
        {
            var root = document;
            while (parent[root] != root)
            {
                root = parent[root];
            }

            while (parent[document] != root)
            {
                var next = parent[document];
                parent[document] = root;
                document = next;
            }

            return root;
        }

        foreach (var sentence in sentences.Distinct())
        {
            var document = DocumentOf(sentence);
            if (!parent.ContainsKey(document))
            {
                parent[document] = document;
            }

            var key = SentenceKey(sentence);
            if (key.Length == 0)
            {
                continue;
            }

            if (!firstDocumentOfKey.TryGetValue(key, out var other))
            {
                firstDocumentOfKey[key] = document;
                continue;
            }

            // The smaller id becomes the root, so the root of a group is its smallest document id whatever the order
            var a = Find(document);
            var b = Find(other);
            if (a != b)
            {
                if (string.CompareOrdinal(a, b) < 0)
                {
                    parent[b] = a;
                }
                else
                {
                    parent[a] = b;
                }
            }
        }

        // Path compression changes the dictionary, so the documents are copied before the roots are read
        var documents = new List<string>(parent.Keys);
        return documents.ToDictionary(d => d, Find, StringComparer.Ordinal);
    }

    /// <summary>
    ///     Gets the fold of a document.
    /// </summary>
    /// <param name="documentId">The document id.</param>
    /// <param name="folds">The number of folds.</param>
    /// <returns>The fold, from 0 to <paramref name="folds"/> - 1.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="folds"/> is less than 2.</exception>
    public static int FoldOf(string documentId, int folds)
    {
        ArgumentNullException.ThrowIfNull(documentId);
        if (folds < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(folds), folds, "There must be at least two folds.");
        }

        var bytes = Encoding.UTF8.GetBytes(documentId);
        return (int)(BinaryFormat.Fnv1a(bytes, 0, bytes.Length) % (uint)folds);
    }

    /// <summary>
    ///     Assigns every distinct sentence to its fold.
    /// </summary>
    /// <remarks>
    ///     The fold is the hash of the smallest document id of the group of documents that share sentence keys (see
    ///     <see cref="GroupDocuments"/>) and the number of folds, so it does not depend on the seed, the order or the feature groups. A
    ///     document is never split, a sentence key never occurs in two folds, and every run of an experiment has the same folds. A
    ///     document that shares no key with another one has the fold of its own id.
    /// </remarks>
    /// <param name="sentences">The sentences (a repeated reference is one sentence).</param>
    /// <param name="folds">The number of folds.</param>
    /// <returns>The fold of every distinct sentence, by reference.</returns>
    public static Dictionary<TrainingSentence, int> Partition(IReadOnlyList<TrainingSentence> sentences, int folds)
    {
        ArgumentNullException.ThrowIfNull(sentences);
        var groupOf = GroupDocuments(sentences);
        return sentences.Distinct().ToDictionary(s => s, s => FoldOf(groupOf[DocumentOf(s)], folds));
    }

    /// <summary>
    ///     Computes the jackknife preview tags of the distinct sentences of a training set.
    /// </summary>
    /// <param name="config">The configuration of the run: its <see cref="TrainingConfig.PreviewFolds"/>, settings, seed and feature groups (the fold models drop <c>rightPreview</c> and keep the others).</param>
    /// <param name="lexicon">The lexicon that restricts the candidates.</param>
    /// <param name="sentences">The training sentences of one epoch (a repeated reference is one sentence).</param>
    /// <param name="progress">Receives one line per finished fold, or <c>null</c>.</param>
    /// <returns>The preview tags (<see cref="Upos"/> ordinals) of every distinct sentence, by reference.</returns>
    /// <exception cref="ArgumentNullException">An argument other than <paramref name="progress"/> is <c>null</c>.</exception>
    /// <exception cref="InvalidDataException">The configuration is invalid, or the sentences do not fill at least two folds.</exception>
    public static Dictionary<TrainingSentence, int[]> Compute(TrainingConfig config, Lexicon lexicon, IReadOnlyList<TrainingSentence> sentences, Action<string>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(lexicon);
        ArgumentNullException.ThrowIfNull(sentences);
        config.Validate();
        var folds = config.PreviewFolds;
        var foldGroups = config.ResolveGroups() & ~FeatureGroups.RightPreview;
        var foldConfig = config.Clone();
        foldConfig.Features = TrainingConfig.NamesOf(foldGroups);

        // The fold of every distinct sentence, in order of first appearance
        var foldOf = Partition(sentences, folds);
        var distinct = sentences.Distinct().ToList();
        if (Enumerable.Range(0, folds).Count(f => distinct.Any(s => foldOf[s] == f)) < 2)
        {
            throw new InvalidDataException("The jackknife needs sentences of at least two folds; the sentences come from too few documents.");
        }

        var result = new Dictionary<TrainingSentence, int[]>(distinct.Count);
        for (var fold = 0; fold < folds; fold++)
        {
            var held = distinct.Where(s => foldOf[s] == fold).ToList();
            if (held.Count == 0)
            {
                continue;
            }

            var clock = Stopwatch.StartNew();
            var training = sentences.Where(s => foldOf[s] != fold).ToList();
            var perceptron = PerceptronTrainer.Train(foldConfig, lexicon, training);
            var predictor = new QuantizedPredictor(foldConfig.HashBits, ModelBuilder.Rows(perceptron, foldConfig), foldGroups);
            foreach (var sentence in held)
            {
                result[sentence] = predictor.Predict(sentence.Tokens, lexicon);
            }

            progress?.Invoke(string.Format(
                CultureInfo.InvariantCulture,
                "jackknife fold {0}/{1}: trained on {2:N0} sentences, tagged {3:N0} held out ({4:N0} tokens), {5:0.0} s",
                fold + 1,
                folds,
                training.Count,
                held.Count,
                held.Sum(s => (long)s.Tokens.Length),
                clock.Elapsed.TotalSeconds));
        }

        return result;
    }
}
