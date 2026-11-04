namespace DemaConsulting.PartOfSpeech.Tests.ModelStorage;

/// <summary>
///     Unit tests of <see cref="EmbeddedModel"/>.
/// </summary>
public class EmbeddedModelTests
{
    /// <summary>
    ///     Proves that the lexicon resource is part of the assembly and loads with the documented counts.
    /// </summary>
    [Fact]
    public void EmbeddedModel_Lexicon_IsPresentAndLoads()
    {
        // Arrange: the resource stream of the assembly
        using var stream = EmbeddedModel.OpenResource(EmbeddedModel.LexiconResourceName);

        // Act: access the lexicon
        var lexicon = EmbeddedModel.Lexicon;

        // Assert: the resource exists and the lexicon has the counts of lexicon.tsv
        Assert.NotNull(stream);
        Assert.Equal(262667, lexicon.SpellingCount);
        Assert.Equal(339880, lexicon.EntryCount);
        var chars = "the".ToCharArray();
        Assert.True(lexicon.TryGetTags(chars, 0, 3, new byte[lexicon.ScratchSize], out var tags));
        Assert.True(tags.Contains(Upos.Det));
    }

    /// <summary>
    ///     Proves that repeated access returns the same instance.
    /// </summary>
    [Fact]
    public void EmbeddedModel_Lexicon_SecondAccess_ReturnsSameInstance()
    {
        // Arrange / Act: access twice
        var first = EmbeddedModel.Lexicon;
        var second = EmbeddedModel.Lexicon;

        // Assert: one instance
        Assert.Same(first, second);
    }

    /// <summary>
    ///     Proves that a build without a weights resource reports no weights instead of failing.
    /// </summary>
    [Fact]
    public void EmbeddedModel_Weights_NoResource_ReturnsNull()
    {
        // Arrange / Act: ask for the weights (phase 3 replaces this test by one that loads them)
        var weights = EmbeddedModel.Weights;

        // Assert: none embedded yet
        Assert.Null(weights);
        Assert.Null(EmbeddedModel.OpenResource(EmbeddedModel.WeightsResourceName));
    }

    /// <summary>
    ///     Proves that many threads asking at once all receive one instance.
    /// </summary>
    [Fact]
    public async Task EmbeddedModel_Lexicon_ParallelAccess_ReturnsSingleInstance()
    {
        // Arrange: 16 tasks that start together
        using var barrier = new Barrier(16);
        var tasks = Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
        {
            barrier.SignalAndWait();
            return EmbeddedModel.Lexicon;
        }, TestContext.Current.CancellationToken)).ToArray();

        // Act: wait for all of them
        var results = await Task.WhenAll(tasks);

        // Assert: every task saw the same instance
        Assert.All(results, r => Assert.Same(results[0], r));
    }
}
