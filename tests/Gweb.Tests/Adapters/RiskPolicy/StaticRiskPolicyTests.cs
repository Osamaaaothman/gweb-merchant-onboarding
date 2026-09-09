using Gweb.Adapters.RiskPolicy;
using Gweb.Domain.RiskPolicy;

namespace Gweb.Tests.Adapters.RiskPolicy;

public class StaticRiskPolicyTests
{
    private static StaticRiskPolicy Policy() => new();

    [Theory]
    [InlineData("6012")]
    [InlineData("6051")]
    [InlineData("6211")]
    public void FlagsEveryMccMastercardGuidanceNamesForEnhancedReviewUnderTheBasePolicy(string mccCode)
    {
        // The three codes docs/08-IMPLEMENTATION-PLAN.md Phase 6 explicitly requires
        // enhanced review be demonstrated for.
        var result = Policy().Evaluate(mccCode);

        Assert.Equal(RiskLevel.EnhancedReview, result);
    }

    [Fact]
    public void TheSameMccYieldsDifferentOutcomesUnderTwoDifferentProviderConfigs()
    {
        // This is the Phase 6 gate, proven directly: 6012 is EnhancedReview with no
        // provider, Restricted under a conservative acquirer, Standard under a
        // permissive one -- three different outcomes for one MCC, purely from
        // configuration, no code branch keyed on the MCC anywhere.
        var policy = Policy();

        var noProvider = policy.Evaluate("6012");
        var conservative = policy.Evaluate("6012", "acquirer-conservative");
        var permissive = policy.Evaluate("6012", "acquirer-permissive");

        Assert.Equal(RiskLevel.EnhancedReview, noProvider);
        Assert.Equal(RiskLevel.Restricted, conservative);
        Assert.Equal(RiskLevel.Standard, permissive);
    }

    [Fact]
    public void AnUnknownProviderIdFallsBackToTheBasePolicy()
    {
        var result = Policy().Evaluate("6012", "some-acquirer-with-no-overrides-configured");

        Assert.Equal(RiskLevel.EnhancedReview, result);
    }

    [Fact]
    public void AProviderOverrideOnlyAppliesToTheMccItNames()
    {
        // acquirer-conservative overrides 6012 but not 6211 -- 6211 should still fall
        // through to the base rule under that same provider, not be swept into
        // Restricted just because the provider has *some* overrides.
        var result = Policy().Evaluate("6211", "acquirer-conservative");

        Assert.Equal(RiskLevel.EnhancedReview, result);
    }

    [Fact]
    public void AnMccWithNoRuleAtAllFallsBackToTheDocumentDefault()
    {
        var result = Policy().Evaluate("5411"); // grocery stores -- not a special-cased MCC

        Assert.Equal(RiskLevel.Standard, result);
    }

    [Fact]
    public void GamblingIsRestrictedUnderTheBasePolicy()
    {
        var result = Policy().Evaluate("7995");

        Assert.Equal(RiskLevel.Restricted, result);
    }
}
