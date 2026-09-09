using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Gweb.Adapters.Persistence;
using Gweb.Domain.Evaluation;
using Gweb.Shared.Clock;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;
using Moq;

namespace Gweb.Tests.Adapters.Persistence;

// See InMemoryEvaluationRepositoryTests.cs: a sibling test namespace shadows the bare
// "Evaluation" identifier here too, so every reference below is fully qualified.

public class DynamoDbEvaluationRepositoryTests
{
    private const string TableName = "test-table";

    private static DeadlineBudget Budget() => DeadlineBudget.Start(35_000, new FakeClock(0), targetMs: 35_000);

    [Fact]
    public async Task SaveRoundTripsExtractionCalculatedAndRiskSignalsThroughAFakeInMemoryDynamoDb()
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
        var repository = new DynamoDbEvaluationRepository(mockClient.Object, TableName);
        var applicationId = Guid.NewGuid();
        var documentId = Guid.NewGuid();

        // expectedVersion is the version already stored, not the object's own current
        // Version -- see InMemoryEvaluationRepositoryTests.cs for the full reasoning.
        var evaluation = global::Gweb.Domain.Evaluation.Evaluation.CreateEmpty(applicationId, DateTimeOffset.UtcNow, "corr-1");
        await repository.SaveAsync(evaluation, expectedVersion: 0, Budget());

        var extraction = new StatementExtraction("Acme", 50_000m, 2.6m, 0.1m, 25m, 15m, "2026-08", "note", "gemini");
        var calculated = new EffectiveRateResult(1_440m, 2.88m, 1_300m, 100m, 25m, 15m);
        var signals = new List<RiskSignal> { new("CODE", "message", "field", documentId), new("OTHER", "msg2", "field2") };
        evaluation.Complete(extraction, calculated, signals, documentId, DateTimeOffset.UtcNow);
        await repository.SaveAsync(evaluation, expectedVersion: 0, Budget());

        var result = await repository.GetByApplicationIdAsync(applicationId, Budget());

        Assert.NotNull(result);
        Assert.Equal(EvaluationStatus.Completed, result!.Status);
        Assert.Equal(extraction, result.Extraction);
        Assert.Equal(calculated, result.Calculated);
        Assert.Equal(2, result.RiskSignals.Count);
        Assert.Equal(documentId, result.RiskSignals[0].SourceDocumentId);
        Assert.Null(result.RiskSignals[1].SourceDocumentId);
        Assert.Equal(documentId, result.ProcessingStatementDocumentId);
        Assert.NotNull(result.EvaluatedAt);
    }

    [Fact]
    public async Task GetReturnsNullWhenNoItemExists()
    {
        var mockClient = new Mock<IAmazonDynamoDB>();
        mockClient
            .Setup(c => c.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetItemResponse { Item = null });
        var repository = new DynamoDbEvaluationRepository(mockClient.Object, TableName);

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
        var repository = new DynamoDbEvaluationRepository(mockClient.Object, TableName);
        var evaluation = global::Gweb.Domain.Evaluation.Evaluation.CreateEmpty(Guid.NewGuid(), DateTimeOffset.UtcNow, "corr-1");

        await Assert.ThrowsAsync<ConflictException>(() => repository.SaveAsync(evaluation, expectedVersion: 0, Budget()));
    }
}
