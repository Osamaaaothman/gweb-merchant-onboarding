using Gweb.Adapters.Persistence;
using Gweb.Domain.Applications;
using Gweb.Shared.Clock;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;

namespace Gweb.Tests.Adapters.Persistence;

public class InMemoryBusinessRepositoryTests
{
    private static DeadlineBudget Budget() => DeadlineBudget.Start(35_000, new FakeClock(0), targetMs: 35_000);

    [Fact]
    public async Task ReturnsNullWhenNoBusinessExistsForTheApplication()
    {
        var repository = new InMemoryBusinessRepository();

        var result = await repository.GetByApplicationIdAsync(Guid.NewGuid(), Budget());

        Assert.Null(result);
    }

    [Fact]
    public async Task SaveThenGetRoundTripsTheBusiness()
    {
        var repository = new InMemoryBusinessRepository();
        var business = Business.CreateEmpty(Guid.NewGuid(), DateTimeOffset.UtcNow, "actor", "corr-1");
        business.ApplyUpdate(new BusinessUpdate(LegalBusinessName: "Testerson Trading LLC"), DateTimeOffset.UtcNow);

        await repository.SaveAsync(business, expectedVersion: 0, Budget());
        var result = await repository.GetByApplicationIdAsync(business.ApplicationId, Budget());

        Assert.NotNull(result);
        Assert.Equal("Testerson Trading LLC", result!.LegalBusinessName);
    }

    [Fact]
    public async Task RejectsCreatingTwiceForTheSameApplication()
    {
        var repository = new InMemoryBusinessRepository();
        var business = Business.CreateEmpty(Guid.NewGuid(), DateTimeOffset.UtcNow, "actor", "corr-1");
        business.ApplyUpdate(new BusinessUpdate(LegalBusinessName: "A"), DateTimeOffset.UtcNow);
        await repository.SaveAsync(business, expectedVersion: 0, Budget());

        var duplicate = Business.CreateEmpty(business.ApplicationId, DateTimeOffset.UtcNow, "actor", "corr-2");
        duplicate.ApplyUpdate(new BusinessUpdate(LegalBusinessName: "B"), DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<ConflictException>(() => repository.SaveAsync(duplicate, expectedVersion: 0, Budget()));
    }

    [Fact]
    public async Task RejectsAnUpdateAgainstAStaleVersion()
    {
        var repository = new InMemoryBusinessRepository();
        var business = Business.CreateEmpty(Guid.NewGuid(), DateTimeOffset.UtcNow, "actor", "corr-1");
        business.ApplyUpdate(new BusinessUpdate(LegalBusinessName: "A"), DateTimeOffset.UtcNow);
        await repository.SaveAsync(business, expectedVersion: 0, Budget());

        // Simulate a second writer's already-applied change (version now 2).
        business.ApplyUpdate(new BusinessUpdate(DbaName: "A Trading Co"), DateTimeOffset.UtcNow);
        await repository.SaveAsync(business, expectedVersion: 1, Budget());

        // A third write believing the version is still 1 (stale) must be rejected.
        var staleWrite = Business.CreateEmpty(business.ApplicationId, DateTimeOffset.UtcNow, "actor", "corr-2");
        staleWrite.ApplyUpdate(new BusinessUpdate(LegalBusinessName: "Stale"), DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<ConflictException>(() => repository.SaveAsync(staleWrite, expectedVersion: 1, Budget()));
    }
}
