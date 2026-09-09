namespace Gweb.Domain.Applications;

/// <summary>
/// A lightweight ownership record embedded in the business payload -- name, role,
/// ownership percentage. Deliberately NOT a full applicant-grade KYC profile (no DOB,
/// address, or government ID of their own): the brief's data model places "ownership /
/// beneficial-owner structure" as a field under §3.2 Business, not as its own
/// individually-addressable resource, and the API surface names only one PATCH route
/// for individual/control-person data (the primary applicant). A production system
/// would give each beneficial owner their own full KYC record; documented here as a
/// deliberate scope cut, not an oversight -- see the README Known Gaps.
/// </summary>
public sealed record BeneficialOwner(string Name, string RoleTitle, decimal OwnershipPercentage);
