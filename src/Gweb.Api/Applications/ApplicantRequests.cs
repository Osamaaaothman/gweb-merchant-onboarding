using System.Text.Json.Serialization;
using Gweb.Domain.Applications;

namespace Gweb.Api.Applications;

// [JsonUnmappedMemberHandling(Disallow)] rejects unknown fields explicitly rather than
// silently ignoring them, per docs/04-SECURITY-RULES.md §3 ("Unknown/extra fields ->
// reject explicitly rather than ignore").
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AddressRequest(string Line1, string? Line2, string City, string State, string PostalCode, string Country)
{
    public Address ToDomain() => new(Line1, Line2, City, State, PostalCode, Country);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record GovernmentIdRequest(GovernmentIdentificationType Type, string Number)
{
    public GovernmentIdInput ToDomain() => new(Type, Number);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PatchApplicantRequest(
    string? LegalFirstName = null,
    string? LegalMiddleName = null,
    string? LegalLastName = null,
    DateOnly? DateOfBirth = null,
    AddressRequest? ResidentialAddress = null,
    string? Email = null,
    string? Phone = null,
    string? RoleTitle = null,
    decimal? OwnershipPercentage = null,
    GovernmentIdRequest? GovernmentId = null,
    string? ConsentVersion = null)
{
    public ApplicantUpdate ToDomain() => new(
        LegalFirstName,
        LegalMiddleName,
        LegalLastName,
        DateOfBirth,
        ResidentialAddress?.ToDomain(),
        Email,
        Phone,
        RoleTitle,
        OwnershipPercentage,
        GovernmentId?.ToDomain(),
        ConsentVersion);
}
