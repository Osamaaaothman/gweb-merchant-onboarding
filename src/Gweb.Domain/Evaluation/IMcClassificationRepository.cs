using Gweb.Shared.Deadline;

namespace Gweb.Domain.Evaluation;

public interface IMcClassificationRepository
{
    Task<McClassification?> GetByApplicationIdAsync(Guid applicationId, DeadlineBudget budget, CancellationToken cancellationToken = default);

    /// <summary>Conditional write on the version the caller read -- see
    /// IApplicantRepository.SaveAsync for the expectedVersion=0-means-create convention.</summary>
    Task SaveAsync(McClassification classification, long expectedVersion, DeadlineBudget budget, CancellationToken cancellationToken = default);
}
