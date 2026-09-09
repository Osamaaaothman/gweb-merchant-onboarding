using Gweb.Domain.Applications;
using Gweb.Shared.Errors;

namespace Gweb.Tests.Domain.Applications;

public class BusinessApplyUpdateAcceptTests
{
    private static Business Empty() => Business.CreateEmpty(Guid.NewGuid(), DateTimeOffset.UtcNow, "actor", "corr-1");

    private static Address ValidAddress() => new("1 Main St", null, "Springfield", "IL", "62701", "US");

    [Fact]
    public void AcceptsAFullyValidFirstUpdateAndBumpsVersionToOne()
    {
        var business = Empty();

        business.ApplyUpdate(
            new BusinessUpdate(
                LegalBusinessName: "Testerson Trading LLC",
                EntityType: EntityType.Llc,
                FormationCountry: "US",
                FormationState: "DE",
                RegistrationIdentifier: new RegistrationIdentifierInput(RegistrationIdentifierType.Ein, "00-0000000"),
                RegisteredAddress: ValidAddress(),
                OperatingAddress: ValidAddress(),
                WebsiteUrl: "https://example.invalid",
                BusinessDescription: "Sells widgets online",
                BusinessStartDate: new DateOnly(2020, 1, 1),
                VolumeProfile: new VolumeProfile(1_000_000m, 50m, 500m, 2000, 40m, 60m),
                BeneficialOwners: [new BeneficialOwner("John Doe", "Co-owner", 40m)],
                SettlementBankAccount: new SettlementBankAccountInput("Testerson Trading LLC", "Test Bank", "000123456789", new DateOnly(2026, 1, 1)),
                ExistingProcessor: "Acme Payments"),
            DateTimeOffset.UtcNow);

        Assert.Equal(1, business.Version);
        Assert.Equal("Testerson Trading LLC", business.LegalBusinessName);
        Assert.Equal(EntityType.Llc, business.EntityType);
        Assert.Single(business.BeneficialOwners);
    }

    [Fact]
    public void MasksTheRegistrationIdentifierDownToLast4()
    {
        var business = Empty();

        business.ApplyUpdate(
            new BusinessUpdate(RegistrationIdentifier: new RegistrationIdentifierInput(RegistrationIdentifierType.Ein, "00-0000000")),
            DateTimeOffset.UtcNow);

        Assert.Equal("**-***0000", business.RegistrationIdentifier!.MaskedValue);
    }

    [Fact]
    public void MasksTheSettlementBankAccountDownToLast4()
    {
        var business = Empty();

        business.ApplyUpdate(
            new BusinessUpdate(SettlementBankAccount: new SettlementBankAccountInput("Actor", "Bank", "000123456789", new DateOnly(2026, 1, 1))),
            DateTimeOffset.UtcNow);

        Assert.Equal("6789", business.SettlementBankAccount!.Last4);
    }

    [Fact]
    public void AllowsExactlyOneHundredPercentCombinedBeneficialOwnership()
    {
        var business = Empty();

        business.ApplyUpdate(
            new BusinessUpdate(BeneficialOwners: [new BeneficialOwner("A", "Owner", 60m), new BeneficialOwner("B", "Owner", 40m)]),
            DateTimeOffset.UtcNow);

        Assert.Equal(100m, business.BeneficialOwnersOwnershipTotal());
    }
}

public class BusinessApplyUpdateRejectTests
{
    private static Business Empty() => Business.CreateEmpty(Guid.NewGuid(), DateTimeOffset.UtcNow, "actor", "corr-1");

    [Fact]
    public void RejectsAnEmptyLegalBusinessName()
    {
        var business = Empty();

        var ex = Assert.Throws<ValidationException>(() =>
            business.ApplyUpdate(new BusinessUpdate(LegalBusinessName: "  "), DateTimeOffset.UtcNow));

        Assert.Contains((List<FieldValidationError>)ex.Details!, e => e.Field == "legalBusinessName");
    }

    [Fact]
    public void RejectsABusinessStartDateInTheFuture()
    {
        var business = Empty();
        var future = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

        var ex = Assert.Throws<ValidationException>(() =>
            business.ApplyUpdate(new BusinessUpdate(BusinessStartDate: future), DateTimeOffset.UtcNow));

        Assert.Contains((List<FieldValidationError>)ex.Details!, e => e.Field == "businessStartDate");
    }

    [Fact]
    public void RejectsAMalformedWebsiteUrl()
    {
        var business = Empty();

        var ex = Assert.Throws<ValidationException>(() =>
            business.ApplyUpdate(new BusinessUpdate(WebsiteUrl: "not a url"), DateTimeOffset.UtcNow));

        Assert.Contains((List<FieldValidationError>)ex.Details!, e => e.Field == "websiteUrl");
    }

    [Fact]
    public void RejectsANonHttpWebsiteUrlScheme()
    {
        var business = Empty();

        var ex = Assert.Throws<ValidationException>(() =>
            business.ApplyUpdate(new BusinessUpdate(WebsiteUrl: "ftp://example.invalid"), DateTimeOffset.UtcNow));

        Assert.Contains((List<FieldValidationError>)ex.Details!, e => e.Field == "websiteUrl");
    }

    [Fact]
    public void RejectsANegativeVolumeProfileField()
    {
        var business = Empty();

        var ex = Assert.Throws<ValidationException>(() =>
            business.ApplyUpdate(
                new BusinessUpdate(VolumeProfile: new VolumeProfile(-1m, 50m, 500m, 2000, 40m, 60m)),
                DateTimeOffset.UtcNow));

        Assert.Contains((List<FieldValidationError>)ex.Details!, e => e.Field == "volumeProfile.expectedAnnualCardVolume");
    }

    [Fact]
    public void RejectsAHighestTicketLowerThanTheAverageTicket()
    {
        var business = Empty();

        var ex = Assert.Throws<ValidationException>(() =>
            business.ApplyUpdate(
                new BusinessUpdate(VolumeProfile: new VolumeProfile(1000m, 500m, 100m, 10, 40m, 60m)),
                DateTimeOffset.UtcNow));

        Assert.Contains((List<FieldValidationError>)ex.Details!, e => e.Field == "volumeProfile.highestTicket");
    }

    [Fact]
    public void RejectsAnEcommercePercentageAboveOneHundred()
    {
        var business = Empty();

        var ex = Assert.Throws<ValidationException>(() =>
            business.ApplyUpdate(
                new BusinessUpdate(VolumeProfile: new VolumeProfile(1000m, 50m, 500m, 10, 40m, 101m)),
                DateTimeOffset.UtcNow));

        Assert.Contains((List<FieldValidationError>)ex.Details!, e => e.Field == "volumeProfile.ecommercePercentage");
    }

    [Fact]
    public void RejectsCombinedBeneficialOwnershipAboveOneHundredPercent()
    {
        var business = Empty();

        var ex = Assert.Throws<ValidationException>(() =>
            business.ApplyUpdate(
                new BusinessUpdate(BeneficialOwners: [new BeneficialOwner("A", "Owner", 60m), new BeneficialOwner("B", "Owner", 50m)]),
                DateTimeOffset.UtcNow));

        Assert.Contains((List<FieldValidationError>)ex.Details!, e => e.Field == "beneficialOwners");
    }

    [Fact]
    public void RejectsABeneficialOwnerWithAnEmptyName()
    {
        var business = Empty();

        var ex = Assert.Throws<ValidationException>(() =>
            business.ApplyUpdate(new BusinessUpdate(BeneficialOwners: [new BeneficialOwner("", "Owner", 10m)]), DateTimeOffset.UtcNow));

        Assert.Contains((List<FieldValidationError>)ex.Details!, e => e.Field == "beneficialOwners[0].name");
    }

    [Fact]
    public void RejectsARegistrationIdentifierShorterThanFourCharacters()
    {
        var business = Empty();

        Assert.Throws<ValidationException>(() =>
            business.ApplyUpdate(
                new BusinessUpdate(RegistrationIdentifier: new RegistrationIdentifierInput(RegistrationIdentifierType.Ein, "12")),
                DateTimeOffset.UtcNow));
    }
}
