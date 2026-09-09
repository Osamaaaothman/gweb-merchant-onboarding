using Gweb.Adapters.Evaluation;
using Gweb.Domain.Evaluation;
using Gweb.Shared.Clock;
using Gweb.Shared.Deadline;

namespace Gweb.Tests.Adapters.Evaluation;

public class MockEvaluationProviderTests
{
    private static DeadlineBudget Budget() => DeadlineBudget.Start(35_000, new FakeClock(0), targetMs: 35_000);

    private static readonly BusinessProfileInput Profile = new("Fresh Valley Grocers", "A neighborhood grocery store.", null, null);

    [Fact]
    public async Task LabelsEveryResultAsTheMockProvider()
    {
        var provider = new MockEvaluationProvider();
        var hints = new List<McCandidateSeed> { new("5411", "Grocery Stores, Supermarkets") };

        var result = await provider.ClassifyMccAsync(Profile, hints, Budget());

        Assert.Equal("mock", result.Provider);
    }

    [Fact]
    public async Task RanksCandidatesByTheOrderOfTheGivenCatalogHintsWithDecreasingConfidence()
    {
        var provider = new MockEvaluationProvider();
        var hints = new List<McCandidateSeed>
        {
            new("5411", "Grocery Stores, Supermarkets"),
            new("5499", "Miscellaneous Food Stores"),
        };

        var result = await provider.ClassifyMccAsync(Profile, hints, Budget());

        Assert.Equal("5411", result.Candidates[0].MccCode);
        Assert.Equal("5499", result.Candidates[1].MccCode);
        Assert.True(result.Candidates[0].Confidence > result.Candidates[1].Confidence);
    }

    [Fact]
    public async Task EveryConfidenceIsWithinZeroToOne()
    {
        var provider = new MockEvaluationProvider();
        var hints = new List<McCandidateSeed>
        {
            new("5411", "a"), new("5499", "b"), new("5462", "c"),
        };

        var result = await provider.ClassifyMccAsync(Profile, hints, Budget());

        Assert.All(result.Candidates, c => Assert.InRange(c.Confidence, 0m, 1m));
    }

    [Fact]
    public async Task NeverReturnsMoreThanThreeCandidatesEvenWithManyHints()
    {
        var provider = new MockEvaluationProvider();
        var hints = Enumerable.Range(0, 10).Select(i => new McCandidateSeed(i.ToString("D4"), $"desc {i}")).ToList();

        var result = await provider.ClassifyMccAsync(Profile, hints, Budget());

        Assert.True(result.Candidates.Count <= 3);
    }

    [Fact]
    public async Task FallsBackToAGenericCategoryWhenNoCatalogHintsAreGiven()
    {
        var provider = new MockEvaluationProvider();

        var result = await provider.ClassifyMccAsync(Profile, [], Budget());

        Assert.NotEmpty(result.Candidates);
    }
}
