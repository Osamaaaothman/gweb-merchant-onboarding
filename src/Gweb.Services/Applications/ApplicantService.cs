using Gweb.Domain.Applications;
using Gweb.Shared.Clock;
using Gweb.Shared.Deadline;

namespace Gweb.Services.Applications;

/// <summary>
/// Create-or-update orchestration for the applicant. Loads the business too, only to
/// run the cross-entity ownership-percentage check -- see OwnershipValidator.
/// </summary>
public sealed class ApplicantService(IApplicantRepository applicantRepository, IBusinessRepository businessRepository, IClock clock)
{
    public async Task<Applicant> UpdateApplicantAsync(
        Guid applicationId,
        ApplicantUpdate update,
        string actor,
        string correlationId,
        DeadlineBudget budget,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.FromUnixTimeMilliseconds(clock.NowMs());

        var existing = await applicantRepository.GetByApplicationIdAsync(applicationId, budget, cancellationToken).ConfigureAwait(false);
        var expectedVersion = existing?.Version ?? 0;
        var applicant = existing ?? Applicant.CreateEmpty(applicationId, now, actor, correlationId);

        applicant.ApplyUpdate(update, now);

        var business = await businessRepository.GetByApplicationIdAsync(applicationId, budget, cancellationToken).ConfigureAwait(false);
        OwnershipValidator.EnsureCombinedOwnershipDoesNotExceedLimit(applicant, business);

        await applicantRepository.SaveAsync(applicant, expectedVersion, budget, cancellationToken).ConfigureAwait(false);

        return applicant;
    }

    public Task<Applicant?> GetApplicantAsync(Guid applicationId, DeadlineBudget budget, CancellationToken cancellationToken = default) =>
        applicantRepository.GetByApplicationIdAsync(applicationId, budget, cancellationToken);
}
