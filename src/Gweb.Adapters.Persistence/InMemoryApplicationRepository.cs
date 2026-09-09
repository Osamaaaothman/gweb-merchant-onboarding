using System.Collections.Concurrent;
using Gweb.Domain.Applications;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;

namespace Gweb.Adapters.Persistence;

/// <summary>
/// Test/local-dev implementation of IApplicationRepository. Thread-safe via
/// ConcurrentDictionary so the same conditional-create guarantee the DynamoDB
/// implementation makes (no silent overwrite) holds here too.
/// </summary>
public sealed class InMemoryApplicationRepository : IApplicationRepository
{
    private readonly ConcurrentDictionary<Guid, Application> _store = new();

    public Task CreateAsync(Application application, DeadlineBudget budget, CancellationToken cancellationToken = default)
    {
        // Store a snapshot, not the caller's live reference -- see
        // Application.Snapshot() for why.
        if (!_store.TryAdd(application.Id, application.Snapshot()))
        {
            throw new ConflictException($"Application {application.Id} already exists.");
        }
        return Task.CompletedTask;
    }

    public Task<Application?> GetByIdAsync(Guid id, DeadlineBudget budget, CancellationToken cancellationToken = default)
    {
        // Return a snapshot, not the stored reference -- see
        // InMemoryApplicantRepository.GetByApplicationIdAsync for why.
        _store.TryGetValue(id, out var application);
        return Task.FromResult(application?.Snapshot());
    }

    public Task UpdateAsync(Application application, long expectedVersion, DeadlineBudget budget, CancellationToken cancellationToken = default)
    {
        var snapshot = application.Snapshot();

        _store.AddOrUpdate(
            application.Id,
            addValueFactory: _ => throw new ConflictException($"Application {application.Id} does not exist."),
            updateValueFactory: (_, current) => current.Version == expectedVersion
                ? snapshot
                : throw new ConflictException($"Application {application.Id} was modified concurrently."));

        return Task.CompletedTask;
    }
}
