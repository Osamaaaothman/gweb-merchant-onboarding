namespace Gweb.Domain.Mcc;

/// <summary>
/// One row of the Merchant Category Code taxonomy. Deliberately carries no risk
/// designation -- brief §"MCC classification & risk policy" requires the taxonomy and
/// the risk policy to be two separate, independently changeable things. See
/// docs/adr/0004-mcc-catalog-storage.md for where this data comes from and
/// src/Gweb.Domain/RiskPolicy (Phase 6) for where risk actually gets decided.
/// </summary>
public sealed record MccCode(string Code, string Description, string Category);
