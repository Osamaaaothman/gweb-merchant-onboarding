namespace Gweb.Domain.RiskPolicy;

/// <summary>
/// Deliberately independent of IMccCatalog -- "MCC taxonomy and risk policy are two
/// separate things. Risk policy must be changeable without touching the catalog"
/// (brief). Nothing here references MccCode; it takes a bare code string, so the
/// catalog could be replaced entirely without this interface or its implementations
/// changing. Same reasoning as IMccCatalog for being synchronous with no
/// DeadlineBudget: packaged data, in-memory lookup, nothing to time out on.
/// </summary>
public interface IRiskPolicy
{
    /// <summary>
    /// <paramref name="providerId"/> is the acquirer/processor whose specific
    /// overrides should apply, if any -- null (or an id with no override configured)
    /// falls back to the base policy. See docs/adr/0005-risk-policy-representation.md
    /// for the override resolution order.
    /// </summary>
    RiskLevel Evaluate(string mccCode, string? providerId = null);
}
