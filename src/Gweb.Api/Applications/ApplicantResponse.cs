using Gweb.Domain.Applications;

namespace Gweb.Api.Applications;

public sealed record AddressResponse(string Line1, string? Line2, string City, string State, string PostalCode, string Country)
{
    public static AddressResponse From(Address address) =>
        new(address.Line1, address.Line2, address.City, address.State, address.PostalCode, address.Country);
}

public sealed record GovernmentIdResponse(GovernmentIdentificationType Type, string Last4)
{
    public static GovernmentIdResponse From(GovernmentIdentification governmentId) =>
        new(governmentId.Type, governmentId.Last4);
}

/// <summary>
/// Never carries a full government ID number, tax ID, or bank account number -- the
/// domain types this maps from (GovernmentIdentification, RegistrationIdentifier,
/// SettlementBankAccount) only ever hold the masked/last4 form to begin with, so there
/// is no unmasked value anywhere in this type to accidentally return.
/// </summary>
public sealed record ApplicantResponse(
    string? LegalFirstName,
    string? LegalMiddleName,
    string? LegalLastName,
    DateOnly? DateOfBirth,
    AddressResponse? ResidentialAddress,
    string? Email,
    string? Phone,
    string? RoleTitle,
    decimal? OwnershipPercentage,
    GovernmentIdResponse? GovernmentId,
    string? ConsentVersion,
    long Version)
{
    public static ApplicantResponse? From(Applicant? applicant)
    {
        if (applicant is null)
        {
            return null;
        }
        return new ApplicantResponse(
            applicant.LegalFirstName,
            applicant.LegalMiddleName,
            applicant.LegalLastName,
            applicant.DateOfBirth,
            applicant.ResidentialAddress is null ? null : AddressResponse.From(applicant.ResidentialAddress),
            applicant.Email,
            applicant.Phone,
            applicant.RoleTitle,
            applicant.OwnershipPercentage,
            applicant.GovernmentId is null ? null : GovernmentIdResponse.From(applicant.GovernmentId),
            applicant.ConsentVersion,
            applicant.Version);
    }
}
