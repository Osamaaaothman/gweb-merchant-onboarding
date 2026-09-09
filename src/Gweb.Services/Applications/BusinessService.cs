using Gweb.Domain.Applications;
using Gweb.Shared.Clock;
using Gweb.Shared.Deadline;

namespace Gweb.Services.Applications;

/// <summary>Mirrors ApplicantService -- see there for the cross-entity ownership-check rationale.</summary>
public sealed class BusinessService(IBusinessRepository businessRepository, IApplicantRepository applicantRepository, IClock clock)
{
    public async Task<Business> UpdateBusinessAsync(
        Guid applicationId,
        BusinessUpdate update,
        string actor,
        string correlationId,
        DeadlineBudget budget,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.FromUnixTimeMilliseconds(clock.NowMs());

        var existing = await businessRepository.GetByApplicationIdAsync(applicationId, budget, cancellationToken).ConfigureAwait(false);
        var expectedVersion = existing?.Version ?? 0;
        var business = existing ?? Business.CreateEmpty(applicationId, now, actor, correlationId);

        business.ApplyUpdate(update, now);

        var applicant = await applicantRepository.GetByApplicationIdAsync(applicationId, budget, cancellationToken).ConfigureAwait(false);
        OwnershipValidator.EnsureCombinedOwnershipDoesNotExceedLimit(applicant, business);

        await businessRepository.SaveAsync(business, expectedVersion, budget, cancellationToken).ConfigureAwait(false);

        return business;
    }

    public Task<Business?> GetBusinessAsync(Guid applicationId, DeadlineBudget budget, CancellationToken cancellationToken = default) =>
        businessRepository.GetByApplicationIdAsync(applicationId, budget, cancellationToken);
}
