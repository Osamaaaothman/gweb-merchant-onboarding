using Gweb.Adapters.Persistence;
using Gweb.Domain.Applications;
using Gweb.Services.Applications;
using Gweb.Shared.Clock;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;

namespace Gweb.Tests.Services.Applications;

public class ApplicantServiceUpdateTests
{
    private static (ApplicantService service, InMemoryApplicantRepository applicants, InMemoryBusinessRepository businesses) Build()
    {
        var applicants = new InMemoryApplicantRepository();
        var businesses = new InMemoryBusinessRepository();
        return (new ApplicantService(applicants, businesses, new FakeClock(0)), applicants, businesses);
    }

    private static DeadlineBudget Budget() => DeadlineBudget.Start(35_000, new FakeClock(0), targetMs: 35_000);

    [Fact]
    public async Task CreatesAndPersistsANewApplicantOnFirstUpdate()
    {
        var (service, applicants, _) = Build();
        var applicationId = Guid.NewGuid();

        var result = await service.UpdateApplicantAsync(
            applicationId, new ApplicantUpdate(LegalFirstName: "Jane"), "actor", "corr-1", Budget());

        Assert.Equal("Jane", result.LegalFirstName);
        var persisted = await applicants.GetByApplicationIdAsync(applicationId, Budget());
        Assert.Equal("Jane", persisted!.LegalFirstName);
    }

    [Fact]
    public async Task SecondUpdateMergesOntoTheFirst()
    {
        var (service, _, _) = Build();
        var applicationId = Guid.NewGuid();
        await service.UpdateApplicantAsync(applicationId, new ApplicantUpdate(LegalFirstName: "Jane"), "actor", "corr-1", Budget());

        var result = await service.UpdateApplicantAsync(
            applicationId, new ApplicantUpdate(LegalLastName: "Testerson"), "actor", "corr-2", Budget());

        Assert.Equal("Jane", result.LegalFirstName);
        Assert.Equal("Testerson", result.LegalLastName);
    }

    [Fact]
    public async Task RejectsAnApplicantOwnershipPercentageThatWouldExceedOneHundredCombinedWithExistingBeneficialOwners()
    {
        var (service, _, businesses) = Build();
        var applicationId = Guid.NewGuid();
        var business = Business.CreateEmpty(applicationId, DateTimeOffset.UtcNow, "actor", "corr-1");
        business.ApplyUpdate(new BusinessUpdate(BeneficialOwners: [new BeneficialOwner("Owner", "Co-owner", 70m)]), DateTimeOffset.UtcNow);
        await businesses.SaveAsync(business, expectedVersion: 0, Budget());

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.UpdateApplicantAsync(applicationId, new ApplicantUpdate(OwnershipPercentage: 40m), "actor", "corr-2", Budget()));
    }

    [Fact]
    public async Task AllowsAnApplicantOwnershipPercentageThatStaysWithinTheCombinedLimit()
    {
        var (service, _, businesses) = Build();
        var applicationId = Guid.NewGuid();
        var business = Business.CreateEmpty(applicationId, DateTimeOffset.UtcNow, "actor", "corr-1");
        business.ApplyUpdate(new BusinessUpdate(BeneficialOwners: [new BeneficialOwner("Owner", "Co-owner", 40m)]), DateTimeOffset.UtcNow);
        await businesses.SaveAsync(business, expectedVersion: 0, Budget());

        var result = await service.UpdateApplicantAsync(applicationId, new ApplicantUpdate(OwnershipPercentage: 60m), "actor", "corr-2", Budget());

        Assert.Equal(60m, result.OwnershipPercentage);
    }
}

public class ApplicantServiceGetTests
{
    [Fact]
    public async Task ReturnsNullWhenNoApplicantExistsYet()
    {
        var applicants = new InMemoryApplicantRepository();
        var service = new ApplicantService(applicants, new InMemoryBusinessRepository(), new FakeClock(0));

        var result = await service.GetApplicantAsync(Guid.NewGuid(), DeadlineBudget.Start(35_000, new FakeClock(0), targetMs: 35_000));

        Assert.Null(result);
    }
}
