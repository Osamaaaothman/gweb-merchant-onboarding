using Gweb.Domain.Applications;
using Gweb.Domain.Documents;
using Gweb.Domain.Submission;

namespace Gweb.Tests.Domain.Submission;

public class SubmissionCheckerTests
{
    private static Document AcceptedDocument(Guid applicationId, DocumentType type)
    {
        var document = Document.CreateAndBeginUpload(
            Guid.NewGuid(), applicationId, type, "file.pdf", "application/pdf",
            1024, $"applications/{applicationId}/documents/x/y.pdf", "abc==", DateTimeOffset.UtcNow, "actor", "corr-1");
        document.MarkReceived("abc==", 1024, DateTimeOffset.UtcNow);
        return document;
    }

    [Fact]
    public void NotReadyWhenApplicantAndBusinessAreBothMissing()
    {
        var readiness = SubmissionChecker.Check(applicant: null, business: null, documents: []);

        Assert.False(readiness.IsReady);
        Assert.NotEmpty(readiness.MissingApplicantFields);
        Assert.NotEmpty(readiness.MissingBusinessFields);
    }

    [Fact]
    public void FlagsEveryRequiredDocumentTypeAsMissingWhenNoDocumentsExist()
    {
        var readiness = SubmissionChecker.Check(applicant: null, business: null, documents: []);

        Assert.Contains(DocumentType.GovernmentId, readiness.MissingRequiredDocuments);
        Assert.Contains(DocumentType.BusinessRegistration, readiness.MissingRequiredDocuments);
        Assert.Contains(DocumentType.BankEvidence, readiness.MissingRequiredDocuments);
    }

    [Fact]
    public void DoesNotRequireProcessingStatementOrBusinessLicense()
    {
        var applicationId = Guid.NewGuid();
        var documents = new List<Document>
        {
            AcceptedDocument(applicationId, DocumentType.GovernmentId),
            AcceptedDocument(applicationId, DocumentType.BusinessRegistration),
            AcceptedDocument(applicationId, DocumentType.BankEvidence),
        };

        var readiness = SubmissionChecker.Check(applicant: null, business: null, documents);

        Assert.Empty(readiness.MissingRequiredDocuments);
    }

    [Fact]
    public void DoesNotCountAnUploadingOrRejectedDocumentAsProvided()
    {
        var applicationId = Guid.NewGuid();
        var stillUploading = Document.CreateAndBeginUpload(
            Guid.NewGuid(), applicationId, DocumentType.GovernmentId, "id.pdf", "application/pdf",
            1024, $"applications/{applicationId}/documents/x/y.pdf", "abc==", DateTimeOffset.UtcNow, "actor", "corr-1");
        var rejected = AcceptedDocument(applicationId, DocumentType.BusinessRegistration);
        rejected.MarkRejected("checksum mismatch", DateTimeOffset.UtcNow);

        var readiness = SubmissionChecker.Check(applicant: null, business: null, documents: [stillUploading, rejected]);

        Assert.Contains(DocumentType.GovernmentId, readiness.MissingRequiredDocuments);
        Assert.Contains(DocumentType.BusinessRegistration, readiness.MissingRequiredDocuments);
    }

    [Fact]
    public void IsReadyOnlyWhenApplicantBusinessAndAllRequiredDocumentsAreComplete()
    {
        var applicant = Applicant.CreateEmpty(Guid.NewGuid(), DateTimeOffset.UtcNow, "actor", "corr-1");
        applicant.ApplyUpdate(new ApplicantUpdate(
            LegalFirstName: "Jane", LegalLastName: "Doe", DateOfBirth: new DateOnly(1990, 1, 1),
            ResidentialAddress: new Address("1 Main St", null, "Town", "ST", "00000", "US"),
            Email: "jane@example.invalid", Phone: "+10000000000", RoleTitle: "CEO",
            GovernmentId: new GovernmentIdInput(GovernmentIdentificationType.Passport, "X1234567"),
            ConsentVersion: "v1"), DateTimeOffset.UtcNow);

        var business = Business.CreateEmpty(Guid.NewGuid(), DateTimeOffset.UtcNow, "actor", "corr-1");
        business.ApplyUpdate(new BusinessUpdate(
            LegalBusinessName: "Acme LLC", EntityType: EntityType.Llc, FormationCountry: "US",
            RegistrationIdentifier: new RegistrationIdentifierInput(RegistrationIdentifierType.Ein, "12-3456789"),
            RegisteredAddress: new Address("1 Main St", null, "Town", "ST", "00000", "US"),
            OperatingAddress: new Address("1 Main St", null, "Town", "ST", "00000", "US"),
            BusinessDescription: "A store.", BusinessStartDate: new DateOnly(2020, 1, 1),
            VolumeProfile: new VolumeProfile(100_000m, 50m, 200m, 100, 60m, 40m),
            SettlementBankAccount: new SettlementBankAccountInput("Acme LLC", "Bank", "000123456789", new DateOnly(2026, 8, 1))),
            DateTimeOffset.UtcNow);

        var applicationId = Guid.NewGuid();
        var documents = new List<Document>
        {
            AcceptedDocument(applicationId, DocumentType.GovernmentId),
            AcceptedDocument(applicationId, DocumentType.BusinessRegistration),
            AcceptedDocument(applicationId, DocumentType.BankEvidence),
        };

        var readiness = SubmissionChecker.Check(applicant, business, documents);

        Assert.True(readiness.IsReady);
    }
}
