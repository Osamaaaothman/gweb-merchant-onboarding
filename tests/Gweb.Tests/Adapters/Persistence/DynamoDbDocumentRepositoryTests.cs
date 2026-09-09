using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Gweb.Adapters.Persistence;
using Gweb.Domain.Documents;
using Gweb.Shared.Clock;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;
using Moq;

namespace Gweb.Tests.Adapters.Persistence;

public class DynamoDbDocumentRepositoryTests
{
    private const string TableName = "test-table";

    private static DeadlineBudget Budget() => DeadlineBudget.Start(35_000, new FakeClock(0), targetMs: 35_000);

    [Fact]
    public async Task SaveRoundTripsEveryFieldIncludingPostCompleteStateThroughAFakeInMemoryDynamoDb()
    {
        var fakeTable = new Dictionary<string, Dictionary<string, AttributeValue>>();
        var mockClient = new Mock<IAmazonDynamoDB>();
        mockClient
            .Setup(c => c.PutItemAsync(It.IsAny<PutItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PutItemRequest r, CancellationToken _) =>
            {
                fakeTable[r.Item["pk"].S + "|" + r.Item["sk"].S] = r.Item;
                return new PutItemResponse();
            });
        mockClient
            .Setup(c => c.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GetItemRequest r, CancellationToken _) =>
            {
                var key = r.Key["pk"].S + "|" + r.Key["sk"].S;
                return fakeTable.TryGetValue(key, out var item) ? new GetItemResponse { Item = item } : new GetItemResponse { Item = null };
            });
        var repository = new DynamoDbDocumentRepository(mockClient.Object, TableName);
        var applicationId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        var document = Document.CreateAndBeginUpload(
            documentId, applicationId, DocumentType.BankEvidence, "voided-check.pdf", "application/pdf", 2048,
            $"applications/{applicationId}/documents/{documentId}/x.pdf", "ZGVjbGFyZWQ=", DateTimeOffset.UtcNow, "actor", "corr-1");
        await repository.SaveAsync(document, expectedVersion: 0, Budget());
        document.MarkReceived("ZGVjbGFyZWQ=", 2048, DateTimeOffset.UtcNow);
        await repository.SaveAsync(document, expectedVersion: 1, Budget());

        var result = await repository.GetByIdAsync(applicationId, documentId, Budget());

        Assert.NotNull(result);
        Assert.Equal(DocumentType.BankEvidence, result!.Type);
        Assert.Equal(DocumentStatus.Received, result.Status);
        Assert.Equal("voided-check.pdf", result.OriginalFilename);
        Assert.Equal(2048, result.DeclaredSizeBytes);
        Assert.Equal(2048, result.ActualSizeBytes);
        Assert.Equal("ZGVjbGFyZWQ=", result.ActualChecksumSha256);
        Assert.NotNull(result.UploadedAt);
    }

    [Fact]
    public async Task GetReturnsNullWhenNoItemExists()
    {
        var mockClient = new Mock<IAmazonDynamoDB>();
        mockClient
            .Setup(c => c.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetItemResponse { Item = null });
        var repository = new DynamoDbDocumentRepository(mockClient.Object, TableName);

        var result = await repository.GetByIdAsync(Guid.NewGuid(), Guid.NewGuid(), Budget());

        Assert.Null(result);
    }

    [Fact]
    public async Task SaveTranslatesAConditionalCheckFailureIntoConflictException()
    {
        var mockClient = new Mock<IAmazonDynamoDB>();
        mockClient
            .Setup(c => c.PutItemAsync(It.IsAny<PutItemRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConditionalCheckFailedException("failed"));
        var repository = new DynamoDbDocumentRepository(mockClient.Object, TableName);
        var applicationId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var document = Document.CreateAndBeginUpload(
            documentId, applicationId, DocumentType.GovernmentId, "id.pdf", "application/pdf", 1024,
            $"applications/{applicationId}/documents/{documentId}/x.pdf", "abc==", DateTimeOffset.UtcNow, "actor", "corr-1");

        await Assert.ThrowsAsync<ConflictException>(() => repository.SaveAsync(document, expectedVersion: 0, Budget()));
    }
}
