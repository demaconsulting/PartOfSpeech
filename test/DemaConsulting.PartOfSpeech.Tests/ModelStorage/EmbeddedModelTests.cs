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
        Assert.Equal(262635, lexicon.SpellingCount);
        Assert.Equal(339823, lexicon.EntryCount);
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
    ///     Proves that the weights resource is part of the assembly and loads as a model of this feature set.
    /// </summary>
    [Fact]
    public void EmbeddedModel_Weights_IsPresentAndLoads()
    {
        // Arrange: the resource stream of the assembly
        using var stream = EmbeddedModel.OpenResource(EmbeddedModel.WeightsResourceName);

        // Act: access the weights (loading validates the format, the checksum and the feature set)
        var weights = EmbeddedModel.Weights;

        // Assert: the resource exists and is the model that the trainer wrote: 19 hash bits, scale 1/8, a temperature
        Assert.NotNull(stream);
        Assert.Equal(19, weights.HashBits);
        Assert.Equal(0.125f, weights.Scale);
        Assert.True(weights.Temperature > 0f);
        Assert.True(weights.RowCount > 100_000);
        Assert.True(weights.WeightCount > weights.RowCount);
    }

    /// <summary>
    ///     Proves that repeated access to the weights returns the same instance.
    /// </summary>
    [Fact]
    public void EmbeddedModel_Weights_SecondAccess_ReturnsSameInstance()
    {
        // Arrange / Act: access twice
        var first = EmbeddedModel.Weights;
        var second = EmbeddedModel.Weights;

        // Assert: one instance
        Assert.Same(first, second);
    }

    /// <summary>
    ///     Proves that many threads asking at once all receive one weights instance.
    /// </summary>
    [Fact]
    public async Task EmbeddedModel_Weights_ParallelAccess_ReturnsSingleInstance()
    {
        // Arrange: 16 tasks that start together
        using var barrier = new Barrier(16);
        var tasks = Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
        {
            barrier.SignalAndWait();
            return EmbeddedModel.Weights;
        }, TestContext.Current.CancellationToken)).ToArray();

        // Act: wait for all of them
        var results = await Task.WhenAll(tasks);

        // Assert: every task saw the same instance
        Assert.All(results, r => Assert.Same(results[0], r));
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
