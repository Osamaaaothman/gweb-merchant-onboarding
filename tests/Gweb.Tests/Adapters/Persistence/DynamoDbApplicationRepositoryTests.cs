using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Gweb.Adapters.Persistence;
using Gweb.Domain.Applications;
using Gweb.Shared.Clock;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;
using Moq;

namespace Gweb.Tests.Adapters.Persistence;

public class DynamoDbApplicationRepositoryTests
{
    private const string TableName = "test-applications-table";

    private static DeadlineBudget Budget(long remainingMs = 35_000) =>
        DeadlineBudget.Start(remainingMs, new FakeClock(0), targetMs: remainingMs);

    private static Application NewApplication() =>
        Application.Create(Guid.NewGuid(), DateTimeOffset.UtcNow, "actor", "corr-1");

    [Fact]
    public async Task CreateSucceedsWhenTheConditionalPutSucceeds()
    {
        var mockClient = new Mock<IAmazonDynamoDB>();
        mockClient
            .Setup(c => c.PutItemAsync(It.IsAny<PutItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PutItemResponse());
        var repository = new DynamoDbApplicationRepository(mockClient.Object, TableName);

        await repository.CreateAsync(NewApplication(), Budget());

        mockClient.Verify(
            c => c.PutItemAsync(
                It.Is<PutItemRequest>(r => r.TableName == TableName && r.ConditionExpression == "attribute_not_exists(pk)"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateTranslatesAConditionalCheckFailureIntoConflictException()
    {
        var mockClient = new Mock<IAmazonDynamoDB>();
        mockClient
            .Setup(c => c.PutItemAsync(It.IsAny<PutItemRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConditionalCheckFailedException("The conditional request failed"));
        var repository = new DynamoDbApplicationRepository(mockClient.Object, TableName);

        await Assert.ThrowsAsync<ConflictException>(() => repository.CreateAsync(NewApplication(), Budget()));
    }

    [Fact]
    public async Task CreateNeverAttemptsTheCallWhenTheRemainingBudgetIsBelowTheReserve()
    {
        // MockBehavior.Strict throws on any unconfigured member access -- proves the
        // client method was never invoked, not just that we handled a failure from it.
        var mockClient = new Mock<IAmazonDynamoDB>(MockBehavior.Strict);
        var repository = new DynamoDbApplicationRepository(mockClient.Object, TableName);

        await Assert.ThrowsAsync<DependencyTimeoutException>(
            () => repository.CreateAsync(NewApplication(), Budget(remainingMs: 100)));
    }

    [Fact]
    public async Task CreateThrowsDependencyTimeoutExceptionWhenTheCallHangsPastItsBudget()
    {
        var hangingCall = new TaskCompletionSource<PutItemResponse>();
        var mockClient = new Mock<IAmazonDynamoDB>();
        mockClient
            .Setup(c => c.PutItemAsync(It.IsAny<PutItemRequest>(), It.IsAny<CancellationToken>()))
            .Returns((PutItemRequest _, CancellationToken ct) =>
            {
                ct.Register(() => hangingCall.TrySetCanceled(ct));
                return hangingCall.Task;
            });
        var repository = new DynamoDbApplicationRepository(mockClient.Object, TableName);
        // remaining 550ms - the repository's internal 500ms reserve = a 50ms real-time
        // timeout, so this test still runs in well under a second, not 45.
        var budget = Budget(remainingMs: 550);

        await Assert.ThrowsAsync<DependencyTimeoutException>(() => repository.CreateAsync(NewApplication(), budget));
    }

    [Fact]
    public async Task GetByIdReturnsNullWhenTheItemDoesNotExist()
    {
        // In AWSSDK v4, a "not found" GetItem response leaves Item as null (not an
        // empty dictionary) -- IsItemSet reflects whether Item was set at all, not
        // whether it's non-empty. Confirmed against the installed package's own XML
        // docs after an earlier version of this test (with Item = []) failed for real.
        var mockClient = new Mock<IAmazonDynamoDB>();
        mockClient
            .Setup(c => c.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetItemResponse { Item = null });
        var repository = new DynamoDbApplicationRepository(mockClient.Object, TableName);

        var result = await repository.GetByIdAsync(Guid.NewGuid(), Budget());

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByIdMapsEveryFieldBackFromTheStoredItem()
    {
        var id = Guid.NewGuid();
        var createdAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var updatedAt = DateTimeOffset.Parse("2026-01-02T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var item = new Dictionary<string, AttributeValue>
        {
            ["applicationId"] = new AttributeValue { S = id.ToString() },
            ["status"] = new AttributeValue { S = "InProgress" },
            ["version"] = new AttributeValue { N = "3" },
            ["createdAt"] = new AttributeValue { S = createdAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture) },
            ["updatedAt"] = new AttributeValue { S = updatedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture) },
            ["createdBy"] = new AttributeValue { S = "actor" },
            ["correlationId"] = new AttributeValue { S = "corr-9" },
        };
        var mockClient = new Mock<IAmazonDynamoDB>();
        mockClient
            .Setup(c => c.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetItemResponse { Item = item });
        var repository = new DynamoDbApplicationRepository(mockClient.Object, TableName);

        var result = await repository.GetByIdAsync(id, Budget());

        Assert.NotNull(result);
        Assert.Equal(id, result!.Id);
        Assert.Equal(ApplicationStatus.InProgress, result.Status);
        Assert.Equal(3, result.Version);
        Assert.Equal(createdAt, result.CreatedAt);
        Assert.Equal(updatedAt, result.UpdatedAt);
        Assert.Equal("actor", result.CreatedBy);
        Assert.Equal("corr-9", result.CorrelationId);
    }

    [Fact]
    public async Task GetByIdUsesStronglyConsistentReads()
    {
        var mockClient = new Mock<IAmazonDynamoDB>();
        mockClient
            .Setup(c => c.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetItemResponse { Item = null });
        var repository = new DynamoDbApplicationRepository(mockClient.Object, TableName);

        await repository.GetByIdAsync(Guid.NewGuid(), Budget());

        mockClient.Verify(
            c => c.GetItemAsync(It.Is<GetItemRequest>(r => r.ConsistentRead == true), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
