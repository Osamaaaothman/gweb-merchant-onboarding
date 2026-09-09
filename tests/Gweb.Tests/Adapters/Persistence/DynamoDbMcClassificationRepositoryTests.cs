using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Gweb.Adapters.Persistence;
using Gweb.Domain.Evaluation;
using Gweb.Shared.Clock;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;
using Moq;

namespace Gweb.Tests.Adapters.Persistence;

public class DynamoDbMcClassificationRepositoryTests
{
    private const string TableName = "test-table";

    private static DeadlineBudget Budget() => DeadlineBudget.Start(35_000, new FakeClock(0), targetMs: 35_000);

    [Fact]
    public async Task SaveRoundTripsCandidatesAndBothSelectionSidesThroughAFakeInMemoryDynamoDb()
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
        var repository = new DynamoDbMcClassificationRepository(mockClient.Object, TableName);
        var applicationId = Guid.NewGuid();

        var classification = McClassification.CreateEmpty(applicationId, DateTimeOffset.UtcNow, "corr-1");
        classification.RecordProposal(
            [new McClassificationCandidate("5411", 0.9m, "Grocery match"), new McClassificationCandidate("5499", 0.3m, "Alt match")],
            "gemini", DateTimeOffset.UtcNow);
        await repository.SaveAsync(classification, expectedVersion: 0, Budget());
        classification.ConfirmSelfSelected("5499", DateTimeOffset.UtcNow);
        await repository.SaveAsync(classification, expectedVersion: 1, Budget());

        var result = await repository.GetByApplicationIdAsync(applicationId, Budget());

        Assert.NotNull(result);
        Assert.Equal("5411", result!.ProposedMccCode);
        Assert.Equal("gemini", result.ProposedProvider);
        Assert.Equal(2, result.Candidates.Count);
        Assert.Equal("5499", result.SelfSelectedMccCode);
        Assert.True(result.HasMismatch);
        Assert.NotNull(result.ClassifiedAt);
        Assert.NotNull(result.SelfSelectedAt);
    }

    [Fact]
    public async Task GetReturnsNullWhenNoItemExists()
    {
        var mockClient = new Mock<IAmazonDynamoDB>();
        mockClient
            .Setup(c => c.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetItemResponse { Item = null });
        var repository = new DynamoDbMcClassificationRepository(mockClient.Object, TableName);

        var result = await repository.GetByApplicationIdAsync(Guid.NewGuid(), Budget());

        Assert.Null(result);
    }

    [Fact]
    public async Task SaveTranslatesAConditionalCheckFailureIntoConflictException()
    {
        var mockClient = new Mock<IAmazonDynamoDB>();
        mockClient
            .Setup(c => c.PutItemAsync(It.IsAny<PutItemRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConditionalCheckFailedException("failed"));
        var repository = new DynamoDbMcClassificationRepository(mockClient.Object, TableName);
        var classification = McClassification.CreateEmpty(Guid.NewGuid(), DateTimeOffset.UtcNow, "corr-1");

        await Assert.ThrowsAsync<ConflictException>(() => repository.SaveAsync(classification, expectedVersion: 0, Budget()));
    }
}
