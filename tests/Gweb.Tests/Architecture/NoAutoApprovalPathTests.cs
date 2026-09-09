using Gweb.Domain.Applications;
using Gweb.Domain.RiskPolicy;

namespace Gweb.Tests.Architecture;

/// <summary>
/// Enforces brief "MCC classification & risk policy": "Never auto-approve a merchant
/// because a model labelled it low risk." That's a design invariant, not a single
/// function to unit-test -- the strongest guarantee available is that the vocabulary
/// itself (every status/level enum in the domain) never contains an "approved"-shaped
/// value. If someone adds one later, this test fails immediately and by name, rather
/// than the invariant silently eroding.
/// </summary>
public class NoAutoApprovalPathTests
{
    private static readonly Type[] OutcomeEnumsThatMustNeverContainApproval =
    [
        typeof(ApplicationStatus),
        typeof(RiskLevel),
    ];

    [Fact]
    public void NoDomainOutcomeEnumContainsAnApprovedOrRejectedValue()
    {
        foreach (var enumType in OutcomeEnumsThatMustNeverContainApproval)
        {
            var names = Enum.GetNames(enumType);
            Assert.DoesNotContain(names, n => n.Contains("Approved", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(names, n => n.Contains("Rejected", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void TheMostPositiveApplicationStatusIsSubmittedNotApproved()
    {
        // The brief's stated most-positive terminal state is READY_FOR_MANUAL_REVIEW
        // (Phase 9); as of this phase the furthest state reachable is Submitted. Either
        // way, "approved" must never be a value a human doesn't have to act on first.
        var values = Enum.GetValues<ApplicationStatus>();

        Assert.All(values, v => Assert.NotEqual("Approved", v.ToString()));
    }
}
