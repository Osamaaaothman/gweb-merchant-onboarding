using Gweb.Adapters.Persistence;
using Gweb.Domain.Applications;
using Gweb.Domain.Documents;
using Gweb.Domain.Submission;
using Gweb.Services.Applications;
using Gweb.Shared.Clock;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;

namespace Gweb.Tests.Services.Applications;

public class SubmissionServiceTests
{
    private static DeadlineBudget Budget() => DeadlineBudget.Start(35_000, new FakeClock(0), targetMs: 35_000);

    private sealed record Harness(
        SubmissionService Service,
        InMemoryApplicationRepository Applications,
        InMemoryApplicantRepository Applicants,
        InMemoryBusinessRepository Businesses,
        InMemoryDocumentRepository Documents);

    private static Harness Build()
    {
        var applications = new InMemoryApplicationRepository();
        var applicants = new InMemoryApplicantRepository();
        var businesses = new InMemoryBusinessRepository();
        var documents = new InMemoryDocumentRepository();
        var classifications = new InMemoryMcClassificationRepository();
        var evaluations = new InMemoryEvaluationRepository();
        var service = new SubmissionService(applications, applicants, businesses, documents, classifications, evaluations, new FakeClock(0));
        return new Harness(service, applications, applicants, businesses, documents);
    }

    private static async Task<Guid> SeedInProgressApplicationAsync(InMemoryApplicationRepository applications)
    {
        var application = Application.Create(Guid.NewGuid(), DateTimeOffset.UtcNow, "actor", "corr-1");
        await applications.CreateAsync(application, Budget());
        return application.Id;
    }

    private static async Task CompleteApplicantAsync(InMemoryApplicantRepository applicants, Guid applicationId)
    {
        var applicant = Applicant.CreateEmpty(applicationId, DateTimeOffset.UtcNow, "actor", "corr-1");
        applicant.ApplyUpdate(new ApplicantUpdate(
            LegalFirstName: "Jane", LegalLastName: "Doe", DateOfBirth: new DateOnly(1990, 1, 1),
            ResidentialAddress: new Address("1 Main St", null, "Town", "ST", "00000", "US"),
            Email: "jane@example.invalid", Phone: "+10000000000", RoleTitle: "CEO",
            GovernmentId: new GovernmentIdInput(GovernmentIdentificationType.Passport, "X1234567"),
            ConsentVersion: "v1"), DateTimeOffset.UtcNow);
        await applicants.SaveAsync(applicant, expectedVersion: 0, Budget());
    }

    private static async Task CompleteBusinessAsync(InMemoryBusinessRepository businesses, Guid applicationId)
    {
        var business = Business.CreateEmpty(applicationId, DateTimeOffset.UtcNow, "actor", "corr-1");
        business.ApplyUpdate(new BusinessUpdate(
            LegalBusinessName: "Acme LLC", EntityType: EntityType.Llc, FormationCountry: "US",
            RegistrationIdentifier: new RegistrationIdentifierInput(RegistrationIdentifierType.Ein, "12-3456789"),
            RegisteredAddress: new Address("1 Main St", null, "Town", "ST", "00000", "US"),
            OperatingAddress: new Address("1 Main St", null, "Town", "ST", "00000", "US"),
            BusinessDescription: "A store.", BusinessStartDate: new DateOnly(2020, 1, 1),
            VolumeProfile: new VolumeProfile(100_000m, 50m, 200m, 100, 60m, 40m),
            SettlementBankAccount: new SettlementBankAccountInput("Acme LLC", "Bank", "000123456789", new DateOnly(2026, 8, 1))),
            DateTimeOffset.UtcNow);
        await businesses.SaveAsync(business, expectedVersion: 0, Budget());
    }

    private static async Task UploadRequiredDocumentsAsync(InMemoryDocumentRepository documents, Guid applicationId)
    {
        foreach (var type in new[] { DocumentType.GovernmentId, DocumentType.BusinessRegistration, DocumentType.BankEvidence })
        {
            var documentId = Guid.NewGuid();
            var document = Document.CreateAndBeginUpload(
                documentId, applicationId, type, "file.pdf", "application/pdf", 1024,
                $"applications/{applicationId}/documents/{documentId}/x.pdf", "abc==", DateTimeOffset.UtcNow, "actor", "corr-1");
            document.MarkReceived("abc==", 1024, DateTimeOffset.UtcNow);
            await documents.SaveAsync(document, expectedVersion: 0, Budget());
        }
    }

    [Fact]
    public async Task ThrowsNotFoundWhenTheApplicationDoesNotExist()
    {
        var harness = Build();

        await Assert.ThrowsAsync<NotFoundException>(() => harness.Service.SubmitAsync(Guid.NewGuid(), Budget()));
    }

    [Fact]
    public async Task ThrowsValidationExceptionWithReadinessDetailsWhenNotComplete()
    {
        var harness = Build();
        var applicationId = await SeedInProgressApplicationAsync(harness.Applications);

        var exception = await Assert.ThrowsAsync<ValidationException>(() => harness.Service.SubmitAsync(applicationId, Budget()));

        var readiness = Assert.IsType<SubmissionReadiness>(exception.Details);
        Assert.False(readiness.IsReady);
        Assert.NotEmpty(readiness.MissingApplicantFields);
    }

    [Fact]
    public async Task DoesNotChangeTheApplicationStatusWhenSubmissionIsBlocked()
    {
        var harness = Build();
        var applicationId = await SeedInProgressApplicationAsync(harness.Applications);

        await Assert.ThrowsAsync<ValidationException>(() => harness.Service.SubmitAsync(applicationId, Budget()));

        var application = await harness.Applications.GetByIdAsync(applicationId, Budget());
        Assert.Equal(ApplicationStatus.InProgress, application!.Status);
    }

    [Fact]
    public async Task SucceedsAndReturnsTheFullResultWhenEverythingIsComplete()
    {
        var harness = Build();
        var applicationId = await SeedInProgressApplicationAsync(harness.Applications);
        await CompleteApplicantAsync(harness.Applicants, applicationId);
        await CompleteBusinessAsync(harness.Businesses, applicationId);
        await UploadRequiredDocumentsAsync(harness.Documents, applicationId);

        var result = await harness.Service.SubmitAsync(applicationId, Budget());

        Assert.Equal(ApplicationStatus.Submitted, result.Application.Status);
        Assert.NotNull(result.Applicant);
        Assert.NotNull(result.Business);
        Assert.Equal(3, result.Documents.Count);
    }

    [Fact]
    public async Task PersistsTheSubmittedStatus()
    {
        var harness = Build();
        var applicationId = await SeedInProgressApplicationAsync(harness.Applications);
        await CompleteApplicantAsync(harness.Applicants, applicationId);
        await CompleteBusinessAsync(harness.Businesses, applicationId);
        await UploadRequiredDocumentsAsync(harness.Documents, applicationId);

        await harness.Service.SubmitAsync(applicationId, Budget());

        var application = await harness.Applications.GetByIdAsync(applicationId, Budget());
        Assert.Equal(ApplicationStatus.Submitted, application!.Status);
    }

    [Fact]
    public async Task ThrowsConflictWhenSubmittingAnAlreadySubmittedApplication()
    {
        var harness = Build();
        var applicationId = await SeedInProgressApplicationAsync(harness.Applications);
        await CompleteApplicantAsync(harness.Applicants, applicationId);
        await CompleteBusinessAsync(harness.Businesses, applicationId);
        await UploadRequiredDocumentsAsync(harness.Documents, applicationId);
        await harness.Service.SubmitAsync(applicationId, Budget());

        await Assert.ThrowsAsync<ConflictException>(() => harness.Service.SubmitAsync(applicationId, Budget()));
    }
}
