namespace Gweb.Domain.Applications;

/// <summary>Input to Business.ApplyUpdate -- see ApplicantUpdate for the PATCH-semantics rationale.</summary>
public sealed record BusinessUpdate(
    string? LegalBusinessName = null,
    string? DbaName = null,
    EntityType? EntityType = null,
    string? FormationCountry = null,
    string? FormationState = null,
    RegistrationIdentifierInput? RegistrationIdentifier = null,
    Address? RegisteredAddress = null,
    Address? OperatingAddress = null,
    string? WebsiteUrl = null,
    string? BusinessDescription = null,
    DateOnly? BusinessStartDate = null,
    VolumeProfile? VolumeProfile = null,
    IReadOnlyList<BeneficialOwner>? BeneficialOwners = null,
    SettlementBankAccountInput? SettlementBankAccount = null,
    string? ExistingProcessor = null);

/// <summary>Full, unmasked registration identifier as submitted -- masked immediately
/// when Business.ApplyUpdate constructs a RegistrationIdentifier from it.</summary>
public sealed record RegistrationIdentifierInput(RegistrationIdentifierType Type, string Value);

/// <summary>Full, unmasked bank account number as submitted -- masked immediately when
/// Business.ApplyUpdate constructs a SettlementBankAccount from it.</summary>
public sealed record SettlementBankAccountInput(string AccountHolder, string BankName, string AccountNumber, DateOnly StatementDate);
