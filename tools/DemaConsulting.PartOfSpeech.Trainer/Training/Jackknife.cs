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
///         digits of the text, the sentence key of the leakage rule of <c>data/README.md</c>), directly or through a chain of other
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
    ///     Gets the sentence key of a sentence: the lower-case letters and digits of its tokens (NFC, invariant lower case).
    /// </summary>
    /// <remarks>
    ///     This is the key of the leakage rule (<c>data/README.md</c>, the leakage check), so two sentences that differ in spacing,
    ///     case or punctuation only have the same key.
    /// </remarks>
    /// <param name="sentence">The sentence.</param>
    /// <returns>The key; empty when the sentence has no letter and no digit (an empty key never matches another sentence).</returns>
    public static string SentenceKey(TrainingSentence sentence)
    {
        ArgumentNullException.ThrowIfNull(sentence);
        var key = new StringBuilder();
        var text = string.Concat(sentence.Tokens).Normalize(NormalizationForm.FormC).ToLowerInvariant();
        foreach (var ch in text.Where(char.IsLetterOrDigit))
        {
            key.Append(ch);
        }

        return key.ToString();
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
