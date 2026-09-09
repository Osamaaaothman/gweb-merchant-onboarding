using System.Collections.Concurrent;
using Gweb.Domain.Applications;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;

namespace Gweb.Adapters.Persistence;

public sealed class InMemoryBusinessRepository : IBusinessRepository
{
    private readonly ConcurrentDictionary<Guid, Business> _store = new();

    public Task<Business?> GetByApplicationIdAsync(Guid applicationId, DeadlineBudget budget, CancellationToken cancellationToken = default)
    {
        // Return a snapshot, not the stored reference -- see
        // InMemoryApplicantRepository.GetByApplicationIdAsync for why.
        _store.TryGetValue(applicationId, out var business);
        return Task.FromResult(business?.Snapshot());
    }

    public Task SaveAsync(Business business, long expectedVersion, DeadlineBudget budget, CancellationToken cancellationToken = default)
    {
        // Store a snapshot, not the caller's live reference -- see
        // InMemoryApplicantRepository.SaveAsync for why.
        var snapshot = business.Snapshot();

        _store.AddOrUpdate(
            business.ApplicationId,
            addValueFactory: _ => expectedVersion == 0
                ? snapshot
                : throw new ConflictException($"Business for application {business.ApplicationId} does not exist."),
            updateValueFactory: (_, current) => current.Version == expectedVersion
                ? snapshot
                : throw new ConflictException($"Business for application {business.ApplicationId} was modified concurrently."));

        return Task.CompletedTask;
    }
}
