using Gweb.Domain.Applications;

namespace Gweb.Api.Applications;

public sealed record RegistrationIdentifierResponse(RegistrationIdentifierType Type, string MaskedValue)
{
    public static RegistrationIdentifierResponse From(RegistrationIdentifier value) => new(value.Type, value.MaskedValue);
}

public sealed record VolumeProfileResponse(
    decimal ExpectedAnnualCardVolume,
    decimal AverageTicket,
    decimal HighestTicket,
    int MonthlyTransactionCount,
    decimal CardPresentPercentage,
    decimal EcommercePercentage)
{
    public static VolumeProfileResponse From(VolumeProfile v) =>
        new(v.ExpectedAnnualCardVolume, v.AverageTicket, v.HighestTicket, v.MonthlyTransactionCount, v.CardPresentPercentage, v.EcommercePercentage);
}

public sealed record BeneficialOwnerResponse(string Name, string RoleTitle, decimal OwnershipPercentage)
{
    public static BeneficialOwnerResponse From(BeneficialOwner o) => new(o.Name, o.RoleTitle, o.OwnershipPercentage);
}

public sealed record SettlementBankAccountResponse(string AccountHolder, string BankName, string Last4, DateOnly StatementDate)
{
    public static SettlementBankAccountResponse From(SettlementBankAccount b) => new(b.AccountHolder, b.BankName, b.Last4, b.StatementDate);
}

/// <summary>Never carries a full EIN/UBI or bank account number -- see ApplicantResponse's doc comment.</summary>
public sealed record BusinessResponse(
    string? LegalBusinessName,
    string? DbaName,
    EntityType? EntityType,
    string? FormationCountry,
    string? FormationState,
    RegistrationIdentifierResponse? RegistrationIdentifier,
    AddressResponse? RegisteredAddress,
    AddressResponse? OperatingAddress,
    string? WebsiteUrl,
    string? BusinessDescription,
    DateOnly? BusinessStartDate,
    VolumeProfileResponse? VolumeProfile,
    IReadOnlyList<BeneficialOwnerResponse> BeneficialOwners,
    SettlementBankAccountResponse? SettlementBankAccount,
    string? ExistingProcessor,
    long Version)
{
    public static BusinessResponse? From(Business? business)
    {
        if (business is null)
        {
            return null;
        }
        return new BusinessResponse(
            business.LegalBusinessName,
            business.DbaName,
            business.EntityType,
            business.FormationCountry,
            business.FormationState,
            business.RegistrationIdentifier is null ? null : RegistrationIdentifierResponse.From(business.RegistrationIdentifier),
            business.RegisteredAddress is null ? null : AddressResponse.From(business.RegisteredAddress),
            business.OperatingAddress is null ? null : AddressResponse.From(business.OperatingAddress),
            business.WebsiteUrl,
            business.BusinessDescription,
            business.BusinessStartDate,
            business.VolumeProfile is null ? null : VolumeProfileResponse.From(business.VolumeProfile),
            business.BeneficialOwners.Select(BeneficialOwnerResponse.From).ToList(),
            business.SettlementBankAccount is null ? null : SettlementBankAccountResponse.From(business.SettlementBankAccount),
            business.ExistingProcessor,
            business.Version);
    }
}
