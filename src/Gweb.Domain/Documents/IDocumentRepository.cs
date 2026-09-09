using Gweb.Shared.Deadline;

namespace Gweb.Domain.Documents;

public interface IDocumentRepository
{
    Task<Document?> GetByIdAsync(Guid applicationId, Guid documentId, DeadlineBudget budget, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every document for one application -- access pattern #3 in
    /// docs/adr/0003-dynamodb-table-strategy.md (`Query(pk=APP#{id}, sk begins_with
    /// "DOC#")`), planned since Phase 2's ADR but not needed by any caller until
    /// Phase 9's submission gate, which needs to know which required document types
    /// have actually been uploaded without the caller already knowing every document
    /// ID up front.
    /// </summary>
    Task<IReadOnlyList<Document>> ListByApplicationIdAsync(Guid applicationId, DeadlineBudget budget, CancellationToken cancellationToken = default);

    /// <summary>Conditional write on the version the caller read -- see
    /// IApplicantRepository.SaveAsync for the expectedVersion=0-means-create convention.</summary>
    Task SaveAsync(Document document, long expectedVersion, DeadlineBudget budget, CancellationToken cancellationToken = default);
}
