using Gweb.Adapters.Persistence;
using Gweb.Domain.Applications;
using Gweb.Services.Applications;
using Gweb.Shared.Clock;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;

namespace Gweb.Tests.Services.Applications;

public class BusinessServiceUpdateTests
{
    private static (BusinessService service, InMemoryApplicantRepository applicants, InMemoryBusinessRepository businesses) Build()
    {
        var applicants = new InMemoryApplicantRepository();
        var businesses = new InMemoryBusinessRepository();
        return (new BusinessService(businesses, applicants, new FakeClock(0)), applicants, businesses);
    }

    private static DeadlineBudget Budget() => DeadlineBudget.Start(35_000, new FakeClock(0), targetMs: 35_000);

    [Fact]
    public async Task CreatesAndPersistsANewBusinessOnFirstUpdate()
    {
        var (service, _, businesses) = Build();
        var applicationId = Guid.NewGuid();

        var result = await service.UpdateBusinessAsync(
            applicationId, new BusinessUpdate(LegalBusinessName: "Testerson Trading LLC"), "actor", "corr-1", Budget());

        Assert.Equal("Testerson Trading LLC", result.LegalBusinessName);
        var persisted = await businesses.GetByApplicationIdAsync(applicationId, Budget());
        Assert.Equal("Testerson Trading LLC", persisted!.LegalBusinessName);
    }

    [Fact]
    public async Task RejectsBeneficialOwnersThatWouldExceedOneHundredCombinedWithTheApplicantsOwnPercentage()
    {
        var (service, applicants, _) = Build();
        var applicationId = Guid.NewGuid();
        var applicant = Applicant.CreateEmpty(applicationId, DateTimeOffset.UtcNow, "actor", "corr-1");
        applicant.ApplyUpdate(new ApplicantUpdate(OwnershipPercentage: 60m), DateTimeOffset.UtcNow);
        await applicants.SaveAsync(applicant, expectedVersion: 0, Budget());

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.UpdateBusinessAsync(
                applicationId,
                new BusinessUpdate(BeneficialOwners: [new BeneficialOwner("Owner", "Co-owner", 50m)]),
                "actor",
                "corr-2",
                Budget()));
    }

    [Fact]
    public async Task AllowsBeneficialOwnersThatStayWithinTheCombinedLimit()
    {
        var (service, applicants, _) = Build();
        var applicationId = Guid.NewGuid();
        var applicant = Applicant.CreateEmpty(applicationId, DateTimeOffset.UtcNow, "actor", "corr-1");
        applicant.ApplyUpdate(new ApplicantUpdate(OwnershipPercentage: 40m), DateTimeOffset.UtcNow);
        await applicants.SaveAsync(applicant, expectedVersion: 0, Budget());

        var result = await service.UpdateBusinessAsync(
            applicationId,
            new BusinessUpdate(BeneficialOwners: [new BeneficialOwner("Owner", "Co-owner", 60m)]),
            "actor",
            "corr-2",
            Budget());

        Assert.Single(result.BeneficialOwners);
    }
}
