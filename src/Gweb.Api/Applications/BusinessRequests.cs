using System.Text.Json.Serialization;
using Gweb.Domain.Applications;

namespace Gweb.Api.Applications;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RegistrationIdentifierRequest(RegistrationIdentifierType Type, string Value)
{
    public RegistrationIdentifierInput ToDomain() => new(Type, Value);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record VolumeProfileRequest(
    decimal ExpectedAnnualCardVolume,
    decimal AverageTicket,
    decimal HighestTicket,
    int MonthlyTransactionCount,
    decimal CardPresentPercentage,
    decimal EcommercePercentage)
{
    public VolumeProfile ToDomain() => new(
        ExpectedAnnualCardVolume, AverageTicket, HighestTicket, MonthlyTransactionCount, CardPresentPercentage, EcommercePercentage);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BeneficialOwnerRequest(string Name, string RoleTitle, decimal OwnershipPercentage)
{
    public BeneficialOwner ToDomain() => new(Name, RoleTitle, OwnershipPercentage);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SettlementBankAccountRequest(string AccountHolder, string BankName, string AccountNumber, DateOnly StatementDate)
{
    public SettlementBankAccountInput ToDomain() => new(AccountHolder, BankName, AccountNumber, StatementDate);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PatchBusinessRequest(
    string? LegalBusinessName = null,
    string? DbaName = null,
    EntityType? EntityType = null,
    string? FormationCountry = null,
    string? FormationState = null,
    RegistrationIdentifierRequest? RegistrationIdentifier = null,
    AddressRequest? RegisteredAddress = null,
    AddressRequest? OperatingAddress = null,
    string? WebsiteUrl = null,
    string? BusinessDescription = null,
    DateOnly? BusinessStartDate = null,
    VolumeProfileRequest? VolumeProfile = null,
    IReadOnlyList<BeneficialOwnerRequest>? BeneficialOwners = null,
    SettlementBankAccountRequest? SettlementBankAccount = null,
    string? ExistingProcessor = null)
{
    public BusinessUpdate ToDomain() => new(
        LegalBusinessName,
        DbaName,
        EntityType,
        FormationCountry,
        FormationState,
        RegistrationIdentifier?.ToDomain(),
        RegisteredAddress?.ToDomain(),
        OperatingAddress?.ToDomain(),
        WebsiteUrl,
        BusinessDescription,
        BusinessStartDate,
        VolumeProfile?.ToDomain(),
        BeneficialOwners?.Select(o => o.ToDomain()).ToList(),
        SettlementBankAccount?.ToDomain(),
        ExistingProcessor);
}
