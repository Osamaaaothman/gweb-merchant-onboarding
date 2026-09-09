using Gweb.Shared.Errors;

namespace Gweb.Domain.Applications;

/// <summary>
/// The applicant's own ownership percentage and the business's beneficial owners live
/// on two different entities/DynamoDB items, so this cross-entity check can't live on
/// either Applicant or Business alone (see docs/03-ARCHITECTURE-RULES.md's "domain ->
/// nothing" rule: an entity can't reach into a sibling entity to validate itself).
/// Still a pure function -- no I/O -- callers (ApplicantService/BusinessService)
/// supply both sides after loading them.
/// </summary>
public static class OwnershipValidator
{
    public static void EnsureCombinedOwnershipDoesNotExceedLimit(Applicant? applicant, Business? business)
    {
        var total = (applicant?.OwnershipPercentage ?? 0m) + (business?.BeneficialOwnersOwnershipTotal() ?? 0m);
        if (total > 100m)
        {
            throw new ValidationException(
                "Combined ownership across the applicant and beneficial owners must not exceed 100%.",
                new List<FieldValidationError> { new("ownershipPercentage", $"combined total ({total}) exceeds 100") });
        }
    }
}
