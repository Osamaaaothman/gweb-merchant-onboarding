using Gweb.Adapters.Persistence;
using Gweb.Adapters.Storage;
using Gweb.Domain.Documents;
using Gweb.Services.Documents;
using Gweb.Shared.Clock;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;

namespace Gweb.Tests.Services.Documents;

public class DocumentServiceTests
{
    private const string DeclaredChecksum = "ZGVjbGFyZWQ=";
    private static readonly byte[] ValidPdfBytes = [.. "%PDF-1.7 rest of a fake pdf document body"u8];

    private static (DocumentService service, InMemoryDocumentRepository repository, InMemoryDocumentStorage storage) Build() =>
        (BuildService(out var repository, out var storage), repository, storage);

    private static DocumentService BuildService(out InMemoryDocumentRepository repository, out InMemoryDocumentStorage storage)
    {
        repository = new InMemoryDocumentRepository();
        storage = new InMemoryDocumentStorage();
        return new DocumentService(repository, storage, new FakeClock(0), TimeSpan.FromMinutes(5));
    }

    private static DeadlineBudget Budget() => DeadlineBudget.Start(35_000, new FakeClock(0), targetMs: 35_000);

    [Fact]
    public async Task PresignCreatesADocumentInUploadingStateAndReturnsAnUploadTarget()
    {
        var (service, repository, _) = Build();
        var applicationId = Guid.NewGuid();

        var result = await service.RequestPresignedUploadAsync(
            applicationId, DocumentType.BankEvidence, "voided check.pdf", "application/pdf",
            ValidPdfBytes.Length, DeclaredChecksum, "actor", "corr-1", Budget());

        Assert.Equal(DocumentStatus.Uploading, result.Document.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Upload.Url));
        var persisted = await repository.GetByIdAsync(applicationId, result.Document.Id, Budget());
        Assert.NotNull(persisted);
    }

    [Fact]
    public async Task PresignRejectsAContentTypeThatIsNotOnTheAllowlist()
    {
        var (service, _, _) = Build();

        await Assert.ThrowsAsync<ValidationException>(() => service.RequestPresignedUploadAsync(
            Guid.NewGuid(), DocumentType.BankEvidence, "evidence.exe", "application/x-msdownload",
            1024, DeclaredChecksum, "actor", "corr-1", Budget()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task PresignRejectsANonPositiveDeclaredSize(long declaredSizeBytes)
    {
        var (service, _, _) = Build();

        await Assert.ThrowsAsync<ValidationException>(() => service.RequestPresignedUploadAsync(
            Guid.NewGuid(), DocumentType.BankEvidence, "check.pdf", "application/pdf",
            declaredSizeBytes, DeclaredChecksum, "actor", "corr-1", Budget()));
    }

    [Fact]
    public async Task PresignRejectsADeclaredSizeAboveTheConfiguredLimit()
    {
        var (service, _, _) = Build();

        await Assert.ThrowsAsync<ValidationException>(() => service.RequestPresignedUploadAsync(
            Guid.NewGuid(), DocumentType.BankEvidence, "check.pdf", "application/pdf",
            DocumentUploadLimits.MaxSizeBytes + 1, DeclaredChecksum, "actor", "corr-1", Budget()));
    }

    [Fact]
    public async Task CompleteThrowsWhenNoUploadHasLandedYet()
    {
        var (service, _, _) = Build();
        var applicationId = Guid.NewGuid();
        var presign = await service.RequestPresignedUploadAsync(
            applicationId, DocumentType.BankEvidence, "check.pdf", "application/pdf",
            ValidPdfBytes.Length, Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(ValidPdfBytes)),
            "actor", "corr-1", Budget());

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.CompleteUploadAsync(applicationId, presign.Document.Id, Budget()));
    }

    [Fact]
    public async Task CompleteThrowsNotFoundForAnUnknownDocument()
    {
        var (service, _, _) = Build();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.CompleteUploadAsync(Guid.NewGuid(), Guid.NewGuid(), Budget()));
    }

    [Fact]
    public async Task CompleteMarksReceivedWhenChecksumSizeAndSignatureAllMatch()
    {
        var (service, repository, storage) = Build();
        var applicationId = Guid.NewGuid();
        var checksum = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(ValidPdfBytes));
        var presign = await service.RequestPresignedUploadAsync(
            applicationId, DocumentType.BankEvidence, "check.pdf", "application/pdf",
            ValidPdfBytes.Length, checksum, "actor", "corr-1", Budget());
        var s3Key = presign.Document.S3Key;
        storage.SimulateUpload(s3Key, new UploadedObject(ValidPdfBytes.Length, checksum, ValidPdfBytes[..16]));

        var result = await service.CompleteUploadAsync(applicationId, presign.Document.Id, Budget());

        Assert.Equal(DocumentStatus.Received, result.Status);
        Assert.Equal(ValidPdfBytes.Length, result.ActualSizeBytes);
        var persisted = await repository.GetByIdAsync(applicationId, presign.Document.Id, Budget());
        Assert.Equal(DocumentStatus.Received, persisted!.Status);
    }

    [Fact]
    public async Task CompleteMarksRejectedWhenTheChecksumDoesNotMatchWhatWasDeclared()
    {
        var (service, _, storage) = Build();
        var applicationId = Guid.NewGuid();
        var presign = await service.RequestPresignedUploadAsync(
            applicationId, DocumentType.BankEvidence, "check.pdf", "application/pdf",
            ValidPdfBytes.Length, DeclaredChecksum, "actor", "corr-1", Budget());
        storage.SimulateUpload(presign.Document.S3Key, new UploadedObject(ValidPdfBytes.Length, "dGFtcGVyZWQ=", ValidPdfBytes[..16]));

        var result = await service.CompleteUploadAsync(applicationId, presign.Document.Id, Budget());

        Assert.Equal(DocumentStatus.Rejected, result.Status);
        Assert.NotNull(result.RejectionReason);
    }

    [Fact]
    public async Task CompleteMarksRejectedWhenTheFileSignatureDoesNotMatchTheDeclaredContentType()
    {
        var (service, _, storage) = Build();
        var applicationId = Guid.NewGuid();
        var notAPdf = "this is not a pdf"u8.ToArray();
        var checksum = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(notAPdf));
        var presign = await service.RequestPresignedUploadAsync(
            applicationId, DocumentType.BankEvidence, "check.pdf", "application/pdf",
            notAPdf.Length, checksum, "actor", "corr-1", Budget());
        storage.SimulateUpload(presign.Document.S3Key, new UploadedObject(notAPdf.Length, checksum, notAPdf[..Math.Min(16, notAPdf.Length)]));

        var result = await service.CompleteUploadAsync(applicationId, presign.Document.Id, Budget());

        Assert.Equal(DocumentStatus.Rejected, result.Status);
    }

    [Fact]
    public async Task CompleteIsIdempotentAndDoesNotReVerifyOnceAlreadyReceived()
    {
        var (service, _, storage) = Build();
        var applicationId = Guid.NewGuid();
        var checksum = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(ValidPdfBytes));
        var presign = await service.RequestPresignedUploadAsync(
            applicationId, DocumentType.BankEvidence, "check.pdf", "application/pdf",
            ValidPdfBytes.Length, checksum, "actor", "corr-1", Budget());
        storage.SimulateUpload(presign.Document.S3Key, new UploadedObject(ValidPdfBytes.Length, checksum, ValidPdfBytes[..16]));
        var first = await service.CompleteUploadAsync(applicationId, presign.Document.Id, Budget());

        var second = await service.CompleteUploadAsync(applicationId, presign.Document.Id, Budget());

        Assert.Equal(first.Version, second.Version);
        Assert.Equal(DocumentStatus.Received, second.Status);
    }

    [Fact]
    public async Task GetDocumentThrowsNotFoundForAnUnknownDocument()
    {
        var (service, _, _) = Build();

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetDocumentAsync(Guid.NewGuid(), Guid.NewGuid(), Budget()));
    }
}
