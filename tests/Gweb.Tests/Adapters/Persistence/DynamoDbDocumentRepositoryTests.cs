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

    [Fact]
    public async Task ListByApplicationIdQueriesByPartitionKeyAndTheDocSortKeyPrefix()
    {
        var applicationId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var item = new Dictionary<string, AttributeValue>
        {
            ["documentId"] = new AttributeValue { S = documentId.ToString() },
            ["documentType"] = new AttributeValue { S = "GovernmentId" },
            ["status"] = new AttributeValue { S = "Received" },
            ["originalFilename"] = new AttributeValue { S = "id.pdf" },
            ["contentType"] = new AttributeValue { S = "application/pdf" },
            ["declaredSizeBytes"] = new AttributeValue { N = "1024" },
            ["s3Key"] = new AttributeValue { S = $"applications/{applicationId}/documents/{documentId}/x.pdf" },
            ["declaredChecksumSha256"] = new AttributeValue { S = "abc==" },
            ["version"] = new AttributeValue { N = "1" },
            ["createdAt"] = new AttributeValue { S = DateTimeOffset.UtcNow.ToString("O") },
            ["updatedAt"] = new AttributeValue { S = DateTimeOffset.UtcNow.ToString("O") },
            ["createdBy"] = new AttributeValue { S = "actor" },
            ["correlationId"] = new AttributeValue { S = "corr-1" },
        };
        var mockClient = new Mock<IAmazonDynamoDB>();
        mockClient
            .Setup(c => c.QueryAsync(It.IsAny<QueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryResponse { Items = [item] });
        var repository = new DynamoDbDocumentRepository(mockClient.Object, TableName);

        var result = await repository.ListByApplicationIdAsync(applicationId, Budget());

        Assert.Single(result);
        Assert.Equal(documentId, result[0].Id);
        mockClient.Verify(
            c => c.QueryAsync(
                It.Is<QueryRequest>(r =>
                    r.TableName == TableName &&
                    r.KeyConditionExpression == "pk = :pk AND begins_with(sk, :skPrefix)" &&
                    r.ExpressionAttributeValues[":skPrefix"].S == "DOC#"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ListByApplicationIdReturnsEmptyWhenTheQueryHasNoMatches()
    {
        var mockClient = new Mock<IAmazonDynamoDB>();
        mockClient
            .Setup(c => c.QueryAsync(It.IsAny<QueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryResponse { Items = [] });
        var repository = new DynamoDbDocumentRepository(mockClient.Object, TableName);

        var result = await repository.ListByApplicationIdAsync(Guid.NewGuid(), Budget());

        Assert.Empty(result);
    }
}
