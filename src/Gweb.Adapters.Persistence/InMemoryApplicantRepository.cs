using System.Collections.Concurrent;
using Gweb.Domain.Applications;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;

namespace Gweb.Adapters.Persistence;

public sealed class InMemoryApplicantRepository : IApplicantRepository
{
    private readonly ConcurrentDictionary<Guid, Applicant> _store = new();

    public Task<Applicant?> GetByApplicationIdAsync(Guid applicationId, DeadlineBudget budget, CancellationToken cancellationToken = default)
    {
        _store.TryGetValue(applicationId, out var applicant);
        return Task.FromResult(applicant);
    }

    public Task SaveAsync(Applicant applicant, long expectedVersion, DeadlineBudget budget, CancellationToken cancellationToken = default)
    {
        // Store a snapshot, not the caller's live reference -- otherwise the caller
        // mutating its own object after this call would silently mutate the
        // "persisted" copy too (Applicant is a mutable reference type).
        var snapshot = applicant.Snapshot();

        _store.AddOrUpdate(
            applicant.ApplicationId,
            addValueFactory: _ => expectedVersion == 0
                ? snapshot
                : throw new ConflictException($"Applicant for application {applicant.ApplicationId} does not exist."),
            updateValueFactory: (_, current) => current.Version == expectedVersion
                ? snapshot
                : throw new ConflictException($"Applicant for application {applicant.ApplicationId} was modified concurrently."));

        return Task.CompletedTask;
    }
}
