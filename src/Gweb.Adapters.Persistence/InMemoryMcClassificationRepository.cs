using System.Collections.Concurrent;
using Gweb.Domain.Evaluation;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;

namespace Gweb.Adapters.Persistence;

public sealed class InMemoryMcClassificationRepository : IMcClassificationRepository
{
    private readonly ConcurrentDictionary<Guid, McClassification> _store = new();

    public Task<McClassification?> GetByApplicationIdAsync(Guid applicationId, DeadlineBudget budget, CancellationToken cancellationToken = default)
    {
        // Snapshot on read -- see InMemoryApplicantRepository for why this matters.
        return Task.FromResult(_store.TryGetValue(applicationId, out var classification) ? classification.Snapshot() : null);
    }

    public Task SaveAsync(McClassification classification, long expectedVersion, DeadlineBudget budget, CancellationToken cancellationToken = default)
    {
        var snapshot = classification.Snapshot();

        _store.AddOrUpdate(
            classification.ApplicationId,
            addValueFactory: _ => expectedVersion == 0
                ? snapshot
                : throw new ConflictException($"MCC classification for application {classification.ApplicationId} does not exist."),
            updateValueFactory: (_, current) => current.Version == expectedVersion
                ? snapshot
                : throw new ConflictException($"MCC classification for application {classification.ApplicationId} was modified concurrently."));

        return Task.CompletedTask;
    }
}
