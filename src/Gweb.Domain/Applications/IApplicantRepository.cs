using Gweb.Shared.Deadline;

namespace Gweb.Domain.Applications;

public interface IApplicantRepository
{
    Task<Applicant?> GetByApplicationIdAsync(Guid applicationId, DeadlineBudget budget, CancellationToken cancellationToken = default);

    /// <summary>
    /// Conditional write on the version the caller read: throws
    /// <see cref="Gweb.Shared.Errors.ConflictException"/> on a concurrent modification
    /// rather than silently overwriting it.
    /// </summary>
    Task SaveAsync(Applicant applicant, long expectedVersion, DeadlineBudget budget, CancellationToken cancellationToken = default);
}
