using Gweb.Adapters.Persistence;
using Gweb.Domain.Documents;
using Gweb.Shared.Clock;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;

namespace Gweb.Tests.Adapters.Persistence;

public class InMemoryDocumentRepositoryTests
{
    private static DeadlineBudget Budget() => DeadlineBudget.Start(35_000, new FakeClock(0), targetMs: 35_000);

    private static Document NewUploadingDocument(Guid applicationId, Guid documentId) => Document.CreateAndBeginUpload(
        documentId, applicationId, DocumentType.GovernmentId, "id.pdf", "application/pdf", 1024,
        $"applications/{applicationId}/documents/{documentId}/x.pdf", "abc==", DateTimeOffset.UtcNow, "actor", "corr-1");

    [Fact]
    public async Task ReturnsNullWhenNoDocumentExists()
    {
        var repository = new InMemoryDocumentRepository();

        var result = await repository.GetByIdAsync(Guid.NewGuid(), Guid.NewGuid(), Budget());

        Assert.Null(result);
    }

    [Fact]
    public async Task SaveThenGetRoundTripsTheDocument()
    {
        var repository = new InMemoryDocumentRepository();
        var applicationId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var document = NewUploadingDocument(applicationId, documentId);

        await repository.SaveAsync(document, expectedVersion: 0, Budget());
        var result = await repository.GetByIdAsync(applicationId, documentId, Budget());

        Assert.NotNull(result);
        Assert.Equal(DocumentStatus.Uploading, result!.Status);
    }

    [Fact]
    public async Task ReturnsNullWhenTheApplicationIdDoesNotMatch()
    {
        // A document belongs to one application -- fetching it under a different
        // application id must behave like it doesn't exist, not leak across tenants.
        var repository = new InMemoryDocumentRepository();
        var applicationId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        await repository.SaveAsync(NewUploadingDocument(applicationId, documentId), expectedVersion: 0, Budget());

        var result = await repository.GetByIdAsync(Guid.NewGuid(), documentId, Budget());

        Assert.Null(result);
    }

    [Fact]
    public async Task RejectsAnUpdateAgainstAStaleVersion()
    {
        var repository = new InMemoryDocumentRepository();
        var applicationId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var document = NewUploadingDocument(applicationId, documentId);
        await repository.SaveAsync(document, expectedVersion: 0, Budget());

        document.MarkReceived("abc==", 1024, DateTimeOffset.UtcNow);
        await repository.SaveAsync(document, expectedVersion: 1, Budget());

        var staleWrite = NewUploadingDocument(applicationId, documentId);
        await Assert.ThrowsAsync<ConflictException>(() => repository.SaveAsync(staleWrite, expectedVersion: 1, Budget()));
    }
}
