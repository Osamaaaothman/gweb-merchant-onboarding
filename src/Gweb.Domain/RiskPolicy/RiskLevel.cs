namespace Gweb.Domain.RiskPolicy;

/// <summary>
/// Per brief "MCC classification & risk policy": "Never hard-label an MCC as
/// universally high risk" -- this is a policy *outcome*, computed by IRiskPolicy from
/// an MCC code (+ optional provider), never a property stamped on MccCode itself.
/// Deliberately no "Approved"/"Rejected" value here or anywhere in this codebase --
/// see NoAutoApprovalPathTests. The most positive outcome any policy evaluation can
/// produce is Standard; a human still decides everything downstream of that.
/// </summary>
public enum RiskLevel
{
    Standard,
    EnhancedReview,
    Restricted,
}
