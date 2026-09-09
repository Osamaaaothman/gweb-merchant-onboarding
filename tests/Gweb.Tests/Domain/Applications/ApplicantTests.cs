using Gweb.Domain.Applications;
using Gweb.Shared.Errors;

namespace Gweb.Tests.Domain.Applications;

public class ApplicantApplyUpdateAcceptTests
{
    private static Applicant Empty() => Applicant.CreateEmpty(Guid.NewGuid(), DateTimeOffset.UtcNow, "actor", "corr-1");

    [Fact]
    public void AcceptsAFullyValidFirstUpdateAndBumpsVersionToOne()
    {
        var applicant = Empty();

        applicant.ApplyUpdate(
            new ApplicantUpdate(
                LegalFirstName: "Jane",
                LegalLastName: "Testerson",
                DateOfBirth: new DateOnly(1985, 6, 15),
                ResidentialAddress: new Address("1 Main St", null, "Springfield", "IL", "62701", "US"),
                Email: "jane@example.invalid",
                Phone: "+1 555-000-1234",
                RoleTitle: "CEO",
                OwnershipPercentage: 60m,
                GovernmentId: new GovernmentIdInput(GovernmentIdentificationType.Passport, "X1234567"),
                ConsentVersion: "v1"),
            DateTimeOffset.UtcNow);

        Assert.Equal(1, applicant.Version);
        Assert.Equal("Jane", applicant.LegalFirstName);
        Assert.Equal("Testerson", applicant.LegalLastName);
        Assert.Equal(60m, applicant.OwnershipPercentage);
    }

    [Fact]
    public void MasksTheGovernmentIdDownToLast4()
    {
        var applicant = Empty();

        applicant.ApplyUpdate(
            new ApplicantUpdate(GovernmentId: new GovernmentIdInput(GovernmentIdentificationType.Passport, "X1234567")),
            DateTimeOffset.UtcNow);

        Assert.Equal("4567", applicant.GovernmentId!.Last4);
        Assert.Equal(GovernmentIdentificationType.Passport, applicant.GovernmentId.Type);
    }

    [Fact]
    public void SetsConsentTimestampWhenConsentVersionIsProvided()
    {
        var applicant = Empty();
        var now = DateTimeOffset.UtcNow;

        applicant.ApplyUpdate(new ApplicantUpdate(ConsentVersion: "v2"), now);

        Assert.Equal("v2", applicant.ConsentVersion);
        Assert.Equal(now, applicant.ConsentTimestamp);
    }

    [Fact]
    public void LeavesFieldsNotIncludedInTheUpdateUnchanged()
    {
        var applicant = Empty();
        applicant.ApplyUpdate(new ApplicantUpdate(LegalFirstName: "Jane"), DateTimeOffset.UtcNow);

        applicant.ApplyUpdate(new ApplicantUpdate(LegalLastName: "Testerson"), DateTimeOffset.UtcNow);

        Assert.Equal("Jane", applicant.LegalFirstName);
        Assert.Equal("Testerson", applicant.LegalLastName);
        Assert.Equal(2, applicant.Version);
    }

    [Fact]
    public void AllowsOwnershipPercentageOfExactlyZeroAndExactlyOneHundred()
    {
        var applicant = Empty();

        applicant.ApplyUpdate(new ApplicantUpdate(OwnershipPercentage: 0m), DateTimeOffset.UtcNow);
        Assert.Equal(0m, applicant.OwnershipPercentage);

        applicant.ApplyUpdate(new ApplicantUpdate(OwnershipPercentage: 100m), DateTimeOffset.UtcNow);
        Assert.Equal(100m, applicant.OwnershipPercentage);
    }
}

public class ApplicantApplyUpdateRejectTests
{
    private static Applicant Empty() => Applicant.CreateEmpty(Guid.NewGuid(), DateTimeOffset.UtcNow, "actor", "corr-1");

    [Fact]
    public void RejectsAnEmptyLegalFirstName()
    {
        var applicant = Empty();

        var ex = Assert.Throws<ValidationException>(() =>
            applicant.ApplyUpdate(new ApplicantUpdate(LegalFirstName: "   "), DateTimeOffset.UtcNow));

        var details = Assert.IsType<List<FieldValidationError>>(ex.Details);
        Assert.Contains(details, e => e.Field == "legalFirstName");
    }

    [Fact]
    public void RejectsADateOfBirthInTheFuture()
    {
        var applicant = Empty();
        var future = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1));

        var ex = Assert.Throws<ValidationException>(() =>
            applicant.ApplyUpdate(new ApplicantUpdate(DateOfBirth: future), DateTimeOffset.UtcNow));

        var details = Assert.IsType<List<FieldValidationError>>(ex.Details);
        Assert.Contains(details, e => e.Field == "dateOfBirth");
    }

    [Fact]
    public void RejectsAMalformedEmail()
    {
        var applicant = Empty();

        var ex = Assert.Throws<ValidationException>(() =>
            applicant.ApplyUpdate(new ApplicantUpdate(Email: "not-an-email"), DateTimeOffset.UtcNow));

        Assert.Contains((List<FieldValidationError>)ex.Details!, e => e.Field == "email");
    }

    [Fact]
    public void RejectsAMalformedPhone()
    {
        var applicant = Empty();

        var ex = Assert.Throws<ValidationException>(() =>
            applicant.ApplyUpdate(new ApplicantUpdate(Phone: "abc"), DateTimeOffset.UtcNow));

        Assert.Contains((List<FieldValidationError>)ex.Details!, e => e.Field == "phone");
    }

    [Fact]
    public void RejectsAnOwnershipPercentageAboveOneHundred()
    {
        var applicant = Empty();

        var ex = Assert.Throws<ValidationException>(() =>
            applicant.ApplyUpdate(new ApplicantUpdate(OwnershipPercentage: 100.01m), DateTimeOffset.UtcNow));

        Assert.Contains((List<FieldValidationError>)ex.Details!, e => e.Field == "ownershipPercentage");
    }

    [Fact]
    public void RejectsANegativeOwnershipPercentage()
    {
        var applicant = Empty();

        var ex = Assert.Throws<ValidationException>(() =>
            applicant.ApplyUpdate(new ApplicantUpdate(OwnershipPercentage: -1m), DateTimeOffset.UtcNow));

        Assert.Contains((List<FieldValidationError>)ex.Details!, e => e.Field == "ownershipPercentage");
    }

    [Fact]
    public void RejectsAnAddressMissingARequiredSubField()
    {
        var applicant = Empty();
        var addressMissingCity = new Address("1 Main St", null, "", "IL", "62701", "US");

        var ex = Assert.Throws<ValidationException>(() =>
            applicant.ApplyUpdate(new ApplicantUpdate(ResidentialAddress: addressMissingCity), DateTimeOffset.UtcNow));

        Assert.Contains((List<FieldValidationError>)ex.Details!, e => e.Field == "residentialAddress.city");
    }

    [Fact]
    public void RejectsAGovernmentIdNumberShorterThanFourCharacters()
    {
        var applicant = Empty();

        Assert.Throws<ValidationException>(() =>
            applicant.ApplyUpdate(
                new ApplicantUpdate(GovernmentId: new GovernmentIdInput(GovernmentIdentificationType.Passport, "X12")),
                DateTimeOffset.UtcNow));
    }

    [Fact]
    public void RejectsAllInvalidFieldsInOneCallRatherThanFailingFast()
    {
        var applicant = Empty();

        var ex = Assert.Throws<ValidationException>(() =>
            applicant.ApplyUpdate(new ApplicantUpdate(Email: "bad", Phone: "x"), DateTimeOffset.UtcNow));

        var details = (List<FieldValidationError>)ex.Details!;
        Assert.Contains(details, e => e.Field == "email");
        Assert.Contains(details, e => e.Field == "phone");
    }

    [Fact]
    public void DoesNotChangeAnyFieldWhenValidationFails()
    {
        var applicant = Empty();
        applicant.ApplyUpdate(new ApplicantUpdate(LegalFirstName: "Jane"), DateTimeOffset.UtcNow);

        Assert.Throws<ValidationException>(() =>
            applicant.ApplyUpdate(new ApplicantUpdate(LegalFirstName: "Ignored", Email: "not-an-email"), DateTimeOffset.UtcNow));

        Assert.Equal("Jane", applicant.LegalFirstName);
        Assert.Equal(1, applicant.Version);
    }
}
