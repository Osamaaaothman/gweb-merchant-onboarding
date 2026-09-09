namespace Gweb.Domain.Applications;

/// <summary>Input to Applicant.ApplyUpdate — a change to the applicant record. Every
/// field is optional: PATCH semantics, only provided fields are applied. There is no
/// way to unset an already-set field (a deliberate scope cut for an intake form).</summary>
public sealed record ApplicantUpdate(
    string? LegalFirstName = null,
    string? LegalMiddleName = null,
    string? LegalLastName = null,
    DateOnly? DateOfBirth = null,
    Address? ResidentialAddress = null,
    string? Email = null,
    string? Phone = null,
    string? RoleTitle = null,
    decimal? OwnershipPercentage = null,
    GovernmentIdInput? GovernmentId = null,
    string? ConsentVersion = null);

/// <summary>Full, unmasked government ID as submitted by the client -- masked down to
/// last4 the moment Applicant.ApplyUpdate constructs a GovernmentIdentification from it.</summary>
public sealed record GovernmentIdInput(GovernmentIdentificationType Type, string Number);
