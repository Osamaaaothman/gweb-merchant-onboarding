using Gweb.Adapters.Persistence;
using Gweb.Domain.Applications;
using Gweb.Shared.Clock;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;

namespace Gweb.Tests.Adapters.Persistence;

public class InMemoryApplicantRepositoryTests
{
    private static DeadlineBudget Budget() => DeadlineBudget.Start(35_000, new FakeClock(0), targetMs: 35_000);

    [Fact]
    public async Task ReturnsNullWhenNoApplicantExistsForTheApplication()
    {
        var repository = new InMemoryApplicantRepository();

        var result = await repository.GetByApplicationIdAsync(Guid.NewGuid(), Budget());

        Assert.Null(result);
    }

    [Fact]
    public async Task SaveThenGetRoundTripsTheApplicant()
    {
        var repository = new InMemoryApplicantRepository();
        var applicant = Applicant.CreateEmpty(Guid.NewGuid(), DateTimeOffset.UtcNow, "actor", "corr-1");
        applicant.ApplyUpdate(new ApplicantUpdate(LegalFirstName: "Jane"), DateTimeOffset.UtcNow);

        await repository.SaveAsync(applicant, expectedVersion: 0, Budget());
        var result = await repository.GetByApplicationIdAsync(applicant.ApplicationId, Budget());

        Assert.NotNull(result);
        Assert.Equal("Jane", result!.LegalFirstName);
    }

    [Fact]
    public async Task RejectsCreatingTwiceForTheSameApplication()
    {
        var repository = new InMemoryApplicantRepository();
        var applicant = Applicant.CreateEmpty(Guid.NewGuid(), DateTimeOffset.UtcNow, "actor", "corr-1");
        applicant.ApplyUpdate(new ApplicantUpdate(LegalFirstName: "Jane"), DateTimeOffset.UtcNow);
        await repository.SaveAsync(applicant, expectedVersion: 0, Budget());

        var duplicate = Applicant.CreateEmpty(applicant.ApplicationId, DateTimeOffset.UtcNow, "actor", "corr-2");
        duplicate.ApplyUpdate(new ApplicantUpdate(LegalFirstName: "Someone Else"), DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<ConflictException>(() => repository.SaveAsync(duplicate, expectedVersion: 0, Budget()));
    }

    [Fact]
    public async Task MutatingTheCallersObjectAfterSaveDoesNotAffectTheStoredCopy()
    {
        // Regression test: an earlier version of SaveAsync stored the caller's live
        // reference. Mutating it afterwards (e.g. a second ApplyUpdate on the same
        // in-memory object) silently mutated the "persisted" copy too, which broke
        // optimistic-concurrency checks against the actual last-saved state.
        var repository = new InMemoryApplicantRepository();
        var applicant = Applicant.CreateEmpty(Guid.NewGuid(), DateTimeOffset.UtcNow, "actor", "corr-1");
        applicant.ApplyUpdate(new ApplicantUpdate(LegalFirstName: "Jane"), DateTimeOffset.UtcNow);
        await repository.SaveAsync(applicant, expectedVersion: 0, Budget());

        applicant.ApplyUpdate(new ApplicantUpdate(LegalFirstName: "Mutated After Save"), DateTimeOffset.UtcNow);

        var stored = await repository.GetByApplicationIdAsync(applicant.ApplicationId, Budget());
        Assert.Equal("Jane", stored!.LegalFirstName);
        Assert.Equal(1, stored.Version);
    }

    [Fact]
    public async Task MutatingAnObjectReturnedByGetDoesNotAffectTheStoredCopy()
    {
        // Regression test: an earlier version of GetByApplicationIdAsync returned the
        // stored reference directly. A caller mutating it (e.g. via ApplyUpdate,
        // which is exactly what ApplicantService does before calling SaveAsync)
        // silently corrupted the "persisted" state before the optimistic-concurrency
        // check even ran.
        var repository = new InMemoryApplicantRepository();
        var applicant = Applicant.CreateEmpty(Guid.NewGuid(), DateTimeOffset.UtcNow, "actor", "corr-1");
        applicant.ApplyUpdate(new ApplicantUpdate(LegalFirstName: "Jane"), DateTimeOffset.UtcNow);
        await repository.SaveAsync(applicant, expectedVersion: 0, Budget());

        var loaded = await repository.GetByApplicationIdAsync(applicant.ApplicationId, Budget());
        loaded!.ApplyUpdate(new ApplicantUpdate(LegalFirstName: "Mutated After Get"), DateTimeOffset.UtcNow);

        var stillStored = await repository.GetByApplicationIdAsync(applicant.ApplicationId, Budget());
        Assert.Equal("Jane", stillStored!.LegalFirstName);
        Assert.Equal(1, stillStored.Version);
    }

    [Fact]
    public async Task RejectsAnUpdateAgainstAStaleVersion()
    {
        var repository = new InMemoryApplicantRepository();
        var applicant = Applicant.CreateEmpty(Guid.NewGuid(), DateTimeOffset.UtcNow, "actor", "corr-1");
        applicant.ApplyUpdate(new ApplicantUpdate(LegalFirstName: "Jane"), DateTimeOffset.UtcNow);
        await repository.SaveAsync(applicant, expectedVersion: 0, Budget());

        // Simulate a second writer's already-applied change (version now 2).
        applicant.ApplyUpdate(new ApplicantUpdate(LegalLastName: "Testerson"), DateTimeOffset.UtcNow);
        await repository.SaveAsync(applicant, expectedVersion: 1, Budget());

        // A third write believing the version is still 1 (stale) must be rejected.
        var staleWrite = Applicant.CreateEmpty(applicant.ApplicationId, DateTimeOffset.UtcNow, "actor", "corr-3");
        staleWrite.ApplyUpdate(new ApplicantUpdate(Email: "jane@example.invalid"), DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<ConflictException>(() => repository.SaveAsync(staleWrite, expectedVersion: 1, Budget()));
    }
}
