using Gweb.Domain.Applications;

namespace Gweb.Tests.Domain.Applications;

public class CompletenessCheckerTests
{
    [Fact]
    public void ReportsEverythingMissingWhenNeitherApplicantNorBusinessExists()
    {
        var result = CompletenessChecker.Check(null, null);

        Assert.False(result.IsComplete);
        Assert.NotEmpty(result.MissingApplicantFields);
        Assert.NotEmpty(result.MissingBusinessFields);
    }

    [Fact]
    public void ReportsCompleteWhenEveryRequiredFieldIsPresent()
    {
        var applicant = Applicant.CreateEmpty(Guid.NewGuid(), DateTimeOffset.UtcNow, "actor", "corr-1");
        applicant.ApplyUpdate(
            new ApplicantUpdate(
                LegalFirstName: "Jane",
                LegalLastName: "Testerson",
                DateOfBirth: new DateOnly(1985, 6, 15),
                ResidentialAddress: new Address("1 Main St", null, "Springfield", "IL", "62701", "US"),
                Email: "jane@example.invalid",
                Phone: "+1 555-000-1234",
                RoleTitle: "CEO",
                GovernmentId: new GovernmentIdInput(GovernmentIdentificationType.Passport, "X1234567"),
                ConsentVersion: "v1"),
            DateTimeOffset.UtcNow);

        var business = Business.CreateEmpty(Guid.NewGuid(), DateTimeOffset.UtcNow, "actor", "corr-1");
        business.ApplyUpdate(
            new BusinessUpdate(
                LegalBusinessName: "Testerson Trading LLC",
                EntityType: EntityType.Llc,
                FormationCountry: "US",
                RegistrationIdentifier: new RegistrationIdentifierInput(RegistrationIdentifierType.Ein, "00-0000000"),
                RegisteredAddress: new Address("1 Main St", null, "Springfield", "IL", "62701", "US"),
                OperatingAddress: new Address("1 Main St", null, "Springfield", "IL", "62701", "US"),
                BusinessDescription: "Sells widgets online",
                BusinessStartDate: new DateOnly(2020, 1, 1),
                VolumeProfile: new VolumeProfile(1_000_000m, 50m, 500m, 2000, 40m, 60m),
                SettlementBankAccount: new SettlementBankAccountInput("Testerson Trading LLC", "Test Bank", "000123456789", new DateOnly(2026, 1, 1))),
            DateTimeOffset.UtcNow);

        var result = CompletenessChecker.Check(applicant, business);

        Assert.True(result.IsComplete);
        Assert.Empty(result.MissingApplicantFields);
        Assert.Empty(result.MissingBusinessFields);
    }

    [Fact]
    public void ListsExactlyTheStillMissingFieldsForAPartiallyFilledApplicant()
    {
        var applicant = Applicant.CreateEmpty(Guid.NewGuid(), DateTimeOffset.UtcNow, "actor", "corr-1");
        applicant.ApplyUpdate(new ApplicantUpdate(LegalFirstName: "Jane", LegalLastName: "Testerson"), DateTimeOffset.UtcNow);

        var result = CompletenessChecker.Check(applicant, null);

        Assert.DoesNotContain("legalFirstName", result.MissingApplicantFields);
        Assert.DoesNotContain("legalLastName", result.MissingApplicantFields);
        Assert.Contains("email", result.MissingApplicantFields);
        Assert.Contains("governmentId", result.MissingApplicantFields);
    }

    [Fact]
    public void OwnershipPercentageIsNotRequiredForCompleteness()
    {
        // "if applicable" per brief §3.1 -- the applicant may not be an owner at all.
        var applicant = Applicant.CreateEmpty(Guid.NewGuid(), DateTimeOffset.UtcNow, "actor", "corr-1");
        applicant.ApplyUpdate(
            new ApplicantUpdate(
                LegalFirstName: "Jane",
                LegalLastName: "Testerson",
                DateOfBirth: new DateOnly(1985, 6, 15),
                ResidentialAddress: new Address("1 Main St", null, "Springfield", "IL", "62701", "US"),
                Email: "jane@example.invalid",
                Phone: "+1 555-000-1234",
                RoleTitle: "CEO",
                GovernmentId: new GovernmentIdInput(GovernmentIdentificationType.Passport, "X1234567"),
                ConsentVersion: "v1"),
            DateTimeOffset.UtcNow);

        var result = CompletenessChecker.Check(applicant, null);

        Assert.DoesNotContain("ownershipPercentage", result.MissingApplicantFields);
    }
}
