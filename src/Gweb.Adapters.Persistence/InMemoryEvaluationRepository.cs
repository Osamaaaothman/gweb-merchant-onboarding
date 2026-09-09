using System.Collections.Concurrent;
using Gweb.Domain.Evaluation;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;

namespace Gweb.Adapters.Persistence;

public sealed class InMemoryEvaluationRepository : IEvaluationRepository
{
    private readonly ConcurrentDictionary<Guid, Evaluation> _store = new();

    public Task<Evaluation?> GetByApplicationIdAsync(Guid applicationId, DeadlineBudget budget, CancellationToken cancellationToken = default)
    {
        // Snapshot on read -- see InMemoryApplicantRepository for why this matters.
        return Task.FromResult(_store.TryGetValue(applicationId, out var evaluation) ? evaluation.Snapshot() : null);
    }

    public Task SaveAsync(Evaluation evaluation, long expectedVersion, DeadlineBudget budget, CancellationToken cancellationToken = default)
    {
        var snapshot = evaluation.Snapshot();

        _store.AddOrUpdate(
            evaluation.ApplicationId,
            addValueFactory: _ => expectedVersion == 0
                ? snapshot
                : throw new ConflictException($"Evaluation for application {evaluation.ApplicationId} does not exist."),
            updateValueFactory: (_, current) => current.Version == expectedVersion
                ? snapshot
                : throw new ConflictException($"Evaluation for application {evaluation.ApplicationId} was modified concurrently."));

        return Task.CompletedTask;
    }
}
