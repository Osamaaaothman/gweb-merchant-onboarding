using System.Collections.Concurrent;
using Gweb.Domain.Documents;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;

namespace Gweb.Adapters.Persistence;

public sealed class InMemoryDocumentRepository : IDocumentRepository
{
    private readonly ConcurrentDictionary<Guid, Document> _store = new();

    public Task<Document?> GetByIdAsync(Guid applicationId, Guid documentId, DeadlineBudget budget, CancellationToken cancellationToken = default)
    {
        // Snapshot on read -- see InMemoryApplicantRepository for why this matters.
        if (_store.TryGetValue(documentId, out var document) && document.ApplicationId == applicationId)
        {
            return Task.FromResult<Document?>(document.Snapshot());
        }
        return Task.FromResult<Document?>(null);
    }

    public Task SaveAsync(Document document, long expectedVersion, DeadlineBudget budget, CancellationToken cancellationToken = default)
    {
        var snapshot = document.Snapshot();

        _store.AddOrUpdate(
            document.Id,
            addValueFactory: _ => expectedVersion == 0
                ? snapshot
                : throw new ConflictException($"Document {document.Id} does not exist."),
            updateValueFactory: (_, current) => current.Version == expectedVersion
                ? snapshot
                : throw new ConflictException($"Document {document.Id} was modified concurrently."));

        return Task.CompletedTask;
    }
}
