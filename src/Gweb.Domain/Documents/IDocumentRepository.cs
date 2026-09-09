using Gweb.Shared.Deadline;

namespace Gweb.Domain.Documents;

public interface IDocumentRepository
{
    Task<Document?> GetByIdAsync(Guid applicationId, Guid documentId, DeadlineBudget budget, CancellationToken cancellationToken = default);

    /// <summary>Conditional write on the version the caller read -- see
    /// IApplicantRepository.SaveAsync for the expectedVersion=0-means-create convention.</summary>
    Task SaveAsync(Document document, long expectedVersion, DeadlineBudget budget, CancellationToken cancellationToken = default);
}
