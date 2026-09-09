using Gweb.Adapters.Persistence;
using Gweb.Domain.Evaluation;
using Gweb.Shared.Clock;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;

namespace Gweb.Tests.Adapters.Persistence;

public class InMemoryMcClassificationRepositoryTests
{
    private static DeadlineBudget Budget() => DeadlineBudget.Start(35_000, new FakeClock(0), targetMs: 35_000);

    [Fact]
    public async Task ReturnsNullWhenNoClassificationExists()
    {
        var repository = new InMemoryMcClassificationRepository();

        var result = await repository.GetByApplicationIdAsync(Guid.NewGuid(), Budget());

        Assert.Null(result);
    }

    [Fact]
    public async Task SaveThenGetRoundTripsCandidatesAndSelfSelection()
    {
        var repository = new InMemoryMcClassificationRepository();
        var applicationId = Guid.NewGuid();
        var classification = McClassification.CreateEmpty(applicationId, DateTimeOffset.UtcNow, "corr-1");
        classification.RecordProposal([new McClassificationCandidate("5411", 0.9m, "Grocery match")], "mock", DateTimeOffset.UtcNow);
        await repository.SaveAsync(classification, expectedVersion: 0, Budget());
        classification.ConfirmSelfSelected("5411", DateTimeOffset.UtcNow);
        await repository.SaveAsync(classification, expectedVersion: 1, Budget());

        var result = await repository.GetByApplicationIdAsync(applicationId, Budget());

        Assert.NotNull(result);
        Assert.Equal("5411", result!.ProposedMccCode);
        Assert.Equal("mock", result.ProposedProvider);
        Assert.Equal("5411", result.SelfSelectedMccCode);
        Assert.Single(result.Candidates);
        Assert.False(result.HasMismatch);
    }

    [Fact]
    public async Task RejectsAnUpdateAgainstAStaleVersion()
    {
        var repository = new InMemoryMcClassificationRepository();
        var applicationId = Guid.NewGuid();
        var classification = McClassification.CreateEmpty(applicationId, DateTimeOffset.UtcNow, "corr-1");
        classification.RecordProposal([new McClassificationCandidate("5411", 0.9m, "x")], "mock", DateTimeOffset.UtcNow);
        await repository.SaveAsync(classification, expectedVersion: 0, Budget());

        var stale = McClassification.CreateEmpty(applicationId, DateTimeOffset.UtcNow, "corr-2");

        await Assert.ThrowsAsync<ConflictException>(() => repository.SaveAsync(stale, expectedVersion: 0, Budget()));
    }
}
