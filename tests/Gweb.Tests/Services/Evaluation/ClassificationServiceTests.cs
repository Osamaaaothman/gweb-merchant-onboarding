using Gweb.Adapters.Mcc;
using Gweb.Adapters.Persistence;
using Gweb.Domain.Applications;
using Gweb.Domain.Evaluation;
using Gweb.Services.Evaluation;
using Gweb.Shared.Clock;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;
using Gweb.Shared.Logging;

namespace Gweb.Tests.Services.Evaluation;

public class ClassificationServiceTests
{
    private sealed class FakeEvaluationProvider(Func<McSuggestion>? onCall = null, Exception? throwOnCall = null) : IEvaluationProvider
    {
        public int CallCount { get; private set; }
        public IReadOnlyList<McCandidateSeed>? LastHints { get; private set; }

        public Task<McSuggestion> ClassifyMccAsync(
            BusinessProfileInput profile, IReadOnlyList<McCandidateSeed> catalogHints, DeadlineBudget budget, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastHints = catalogHints;
            if (throwOnCall is not null)
            {
                throw throwOnCall;
            }
            return Task.FromResult(onCall!());
        }
    }

    private static DeadlineBudget Budget() => DeadlineBudget.Start(35_000, new FakeClock(0), targetMs: 35_000);

    private static StructuredLogger Logger() => new(TextWriter.Null, LogLevel.Debug);

    private static async Task<(Guid applicationId, InMemoryBusinessRepository businesses)> SeedBusinessAsync(string? description = "A neighborhood grocery store selling produce and dairy.")
    {
        var businesses = new InMemoryBusinessRepository();
        var applicationId = Guid.NewGuid();
        var business = Business.CreateEmpty(applicationId, DateTimeOffset.UtcNow, "actor", "corr-1");
        business.ApplyUpdate(new BusinessUpdate(LegalBusinessName: "Fresh Valley Grocers", BusinessDescription: description), DateTimeOffset.UtcNow);
        await businesses.SaveAsync(business, expectedVersion: 0, Budget());
        return (applicationId, businesses);
    }

    [Fact]
    public async Task ClassifyThrowsWhenNoBusinessExistsYet()
    {
        var service = new ClassificationService(
            new InMemoryMcClassificationRepository(), new InMemoryBusinessRepository(), new StaticMccCatalog(),
            new FakeEvaluationProvider(() => new McSuggestion("mock", [])), new FakeEvaluationProvider(() => new McSuggestion("mock", [])),
            new FakeClock(0), Logger());

        await Assert.ThrowsAsync<ValidationException>(() => service.ClassifyAsync(Guid.NewGuid(), "corr-1", Budget()));
    }

    [Fact]
    public async Task ClassifyPersistsValidCandidatesFromThePrimaryProvider()
    {
        var (applicationId, businesses) = await SeedBusinessAsync();
        var repository = new InMemoryMcClassificationRepository();
        var primary = new FakeEvaluationProvider(() => new McSuggestion("gemini", [new McClassificationCandidate("5411", 0.9m, "match")]));
        var service = new ClassificationService(
            repository, businesses, new StaticMccCatalog(), primary, new FakeEvaluationProvider(() => new McSuggestion("mock", [])),
            new FakeClock(0), Logger());

        var result = await service.ClassifyAsync(applicationId, "corr-1", Budget());

        Assert.Equal("5411", result.ProposedMccCode);
        Assert.Equal("gemini", result.ProposedProvider);
        var persisted = await repository.GetByApplicationIdAsync(applicationId, Budget());
        Assert.Equal("5411", persisted!.ProposedMccCode);
    }

    [Fact]
    public async Task ClassifyDropsHallucinatedCandidatesThatAreNotInTheRealCatalog()
    {
        var (applicationId, businesses) = await SeedBusinessAsync();
        var primary = new FakeEvaluationProvider(() => new McSuggestion("gemini",
        [
            new McClassificationCandidate("0000", 0.99m, "invented code, not in the catalog"),
            new McClassificationCandidate("5411", 0.5m, "real code"),
        ]));
        var service = new ClassificationService(
            new InMemoryMcClassificationRepository(), businesses, new StaticMccCatalog(), primary, new FakeEvaluationProvider(() => new McSuggestion("mock", [])),
            new FakeClock(0), Logger());

        var result = await service.ClassifyAsync(applicationId, "corr-1", Budget());

        Assert.Equal("5411", result.ProposedMccCode);
        Assert.DoesNotContain(result.Candidates, c => c.MccCode == "0000");
    }

    [Fact]
    public async Task ClassifyFallsBackToTheMockProviderWhenThePrimaryProviderFails()
    {
        var (applicationId, businesses) = await SeedBusinessAsync();
        var primary = new FakeEvaluationProvider(throwOnCall: new DependencyTimeoutException("Gemini timed out."));
        var fallback = new FakeEvaluationProvider(() => new McSuggestion("mock", [new McClassificationCandidate("5411", 0.7m, "fallback match")]));
        var service = new ClassificationService(
            new InMemoryMcClassificationRepository(), businesses, new StaticMccCatalog(), primary, fallback,
            new FakeClock(0), Logger());

        var result = await service.ClassifyAsync(applicationId, "corr-1", Budget());

        Assert.Equal("mock", result.ProposedProvider);
        Assert.Equal(1, primary.CallCount);
        Assert.Equal(1, fallback.CallCount);
    }

    [Fact]
    public async Task ClassifyBuildsCatalogHintsFromDescriptionKeywordsNotAsOneLiteralSubstring()
    {
        // Regression test for a real bug found manually verifying the live Gemini
        // integration: IMccCatalog.Search does substring/prefix matching tuned for a
        // short typed query, so passing the whole free-text description as one query
        // string matched nothing at all -- both providers received zero hints. Fixed
        // by ClassificationService.BuildCatalogHints searching per keyword instead.
        var (applicationId, businesses) = await SeedBusinessAsync(
            "We operate a neighborhood grocery store selling fresh produce, dairy, and packaged foods to local residents.");
        var primary = new FakeEvaluationProvider(() => new McSuggestion("gemini", [new McClassificationCandidate("5411", 0.9m, "x")]));
        var service = new ClassificationService(
            new InMemoryMcClassificationRepository(), businesses, new StaticMccCatalog(), primary, new FakeEvaluationProvider(() => new McSuggestion("mock", [])),
            new FakeClock(0), Logger());

        await service.ClassifyAsync(applicationId, "corr-1", Budget());

        Assert.NotNull(primary.LastHints);
        Assert.NotEmpty(primary.LastHints!);
        Assert.Contains(primary.LastHints!, h => h.MccCode == "5411");
    }

    [Fact]
    public async Task ClassifyThrowsWhenEveryCandidateFromTheProviderIsInvalid()
    {
        var (applicationId, businesses) = await SeedBusinessAsync();
        var primary = new FakeEvaluationProvider(() => new McSuggestion("gemini", [new McClassificationCandidate("0000", 0.9m, "invented")]));
        var service = new ClassificationService(
            new InMemoryMcClassificationRepository(), businesses, new StaticMccCatalog(), primary, new FakeEvaluationProvider(() => new McSuggestion("mock", [])),
            new FakeClock(0), Logger());

        await Assert.ThrowsAsync<DependencyUnavailableException>(() => service.ClassifyAsync(applicationId, "corr-1", Budget()));
    }

    [Fact]
    public async Task ConfirmSelfSelectedRejectsAnUnknownMccCode()
    {
        var service = new ClassificationService(
            new InMemoryMcClassificationRepository(), new InMemoryBusinessRepository(), new StaticMccCatalog(),
            new FakeEvaluationProvider(() => new McSuggestion("mock", [])), new FakeEvaluationProvider(() => new McSuggestion("mock", [])),
            new FakeClock(0), Logger());

        await Assert.ThrowsAsync<ValidationException>(() => service.ConfirmSelfSelectedAsync(Guid.NewGuid(), "0000", "corr-1", Budget()));
    }

    [Fact]
    public async Task ConfirmSelfSelectedPersistsAValidChoiceEvenBeforeAnyProposalExists()
    {
        var repository = new InMemoryMcClassificationRepository();
        var applicationId = Guid.NewGuid();
        var service = new ClassificationService(
            repository, new InMemoryBusinessRepository(), new StaticMccCatalog(),
            new FakeEvaluationProvider(() => new McSuggestion("mock", [])), new FakeEvaluationProvider(() => new McSuggestion("mock", [])),
            new FakeClock(0), Logger());

        var result = await service.ConfirmSelfSelectedAsync(applicationId, "5411", "corr-1", Budget());

        Assert.Equal("5411", result.SelfSelectedMccCode);
        Assert.Null(result.ProposedMccCode);
        Assert.False(result.HasMismatch);
    }

    [Fact]
    public async Task GetReturnsNullWhenNoClassificationExistsYet()
    {
        var service = new ClassificationService(
            new InMemoryMcClassificationRepository(), new InMemoryBusinessRepository(), new StaticMccCatalog(),
            new FakeEvaluationProvider(() => new McSuggestion("mock", [])), new FakeEvaluationProvider(() => new McSuggestion("mock", [])),
            new FakeClock(0), Logger());

        var result = await service.GetAsync(Guid.NewGuid(), Budget());

        Assert.Null(result);
    }
}
