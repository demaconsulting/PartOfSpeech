namespace DemaConsulting.PartOfSpeech.Trainer;

/// <summary>
///     The two small decisions that the training and the runtime decoder must make identically: which tags a token may
///     take, and which allowed tag scores best.
/// </summary>
/// <remarks>
///     The decoder keeps these steps inside its private methods, and the trainer must not drift from them, so the
///     trainer states them once here and a test proves that they agree with <c>TagDecoder</c> on real weights. The
///     class is stateless and thread-safe.
/// </remarks>
internal static class TagSelection
{
    /// <summary>
    ///     The mask that contains all 17 tags.
    /// </summary>
    public const int AllTags = (1 << UposExtensions.Count) - 1;

    /// <summary>
    ///     Computes the tags that the lexicon and the token-shape rules allow for a token of a prepared workspace.
    /// </summary>
    /// <remarks>
    ///     This is the same sequence as the decoder uses: the lower-cased folded token follows the folded form in the
    ///     character buffer, and <c>Lexicon.AllowedTags</c> adds what the shape of the token permits.
    /// </remarks>
    /// <param name="workspace">A workspace that <see cref="FeatureExtractor.Prepare"/> filled.</param>
    /// <param name="index">The token.</param>
    /// <param name="lexicon">The lexicon.</param>
    /// <returns>The allowed tags as a bit mask over the tag ordinals; never empty.</returns>
    public static int AllowedMask(DecoderWorkspace workspace, int index, Lexicon lexicon)
    {
        workspace.EnsureLexiconScratch(lexicon.ScratchSize);
        var length = workspace.Length[index];
        var lowerStart = workspace.Start[index] + length;
        var known = lexicon.TryGetTags(workspace.Chars, lowerStart, length, workspace.LexiconScratch, out var tags);
        var flags = workspace.Flags[index];
        return Lexicon.AllowedTags(
            known,
            tags,
            (flags & FeatureExtractor.FlagLetterOrDigit) != 0,
            (flags & FeatureExtractor.FlagDigit) != 0,
            (flags & FeatureExtractor.FlagFirstUpper) != 0).Mask;
    }

    /// <summary>
    ///     Picks the best-scoring tag among a set of candidates; the lowest tag ordinal wins a tie.
    /// </summary>
    /// <param name="scores">The integer score of every tag.</param>
    /// <param name="mask">The candidates as a bit mask; must not be empty.</param>
    /// <returns>The chosen tag ordinal.</returns>
    public static int ArgMax(int[] scores, int mask)
    {
        var best = -1;
        for (var tag = 0; tag < UposExtensions.Count; tag++)
        {
            if ((mask & (1 << tag)) != 0 && (best < 0 || scores[tag] > scores[best]))
            {
                best = tag;
            }
        }

        return best;
    }
}
