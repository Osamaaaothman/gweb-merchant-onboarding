using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Gweb.Adapters.Persistence;
using Gweb.Domain.Applications;
using Gweb.Shared.Clock;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;
using Moq;

namespace Gweb.Tests.Adapters.Persistence;

public class DynamoDbApplicantRepositoryTests
{
    private const string TableName = "test-table";

    private static DeadlineBudget Budget() => DeadlineBudget.Start(35_000, new FakeClock(0), targetMs: 35_000);

    [Fact]
    public async Task SaveRoundTripsEveryFieldThroughAFakeInMemoryDynamoDb()
    {
        // A fake IAmazonDynamoDB that actually stores/returns items, so this proves
        // ToItem/FromItem are inverses of each other -- not just that each compiles.
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
        var repository = new DynamoDbApplicantRepository(mockClient.Object, TableName);

        var applicant = Applicant.CreateEmpty(Guid.NewGuid(), DateTimeOffset.UtcNow, "actor", "corr-1");
        applicant.ApplyUpdate(
            new ApplicantUpdate(
                LegalFirstName: "Jane",
                LegalMiddleName: "Q",
                LegalLastName: "Testerson",
                DateOfBirth: new DateOnly(1985, 6, 15),
                ResidentialAddress: new Address("1 Main St", "Apt 2", "Springfield", "IL", "62701", "US"),
                Email: "jane@example.invalid",
                Phone: "+1 555-000-1234",
                RoleTitle: "CEO",
                OwnershipPercentage: 60m,
                GovernmentId: new GovernmentIdInput(GovernmentIdentificationType.Passport, "X1234567"),
                ConsentVersion: "v1"),
            DateTimeOffset.UtcNow);

        await repository.SaveAsync(applicant, expectedVersion: 0, Budget());
        var result = await repository.GetByApplicationIdAsync(applicant.ApplicationId, Budget());

        Assert.NotNull(result);
        Assert.Equal("Jane", result!.LegalFirstName);
        Assert.Equal("Q", result.LegalMiddleName);
        Assert.Equal("Testerson", result.LegalLastName);
        Assert.Equal(new DateOnly(1985, 6, 15), result.DateOfBirth);
        Assert.Equal("Apt 2", result.ResidentialAddress!.Line2);
        Assert.Equal("jane@example.invalid", result.Email);
        Assert.Equal(60m, result.OwnershipPercentage);
        Assert.Equal("4567", result.GovernmentId!.Last4);
        Assert.Equal(GovernmentIdentificationType.Passport, result.GovernmentId.Type);
        Assert.Equal("v1", result.ConsentVersion);
        Assert.NotNull(result.ConsentTimestamp);
    }

    [Fact]
    public async Task GetReturnsNullWhenNoItemExists()
    {
        var mockClient = new Mock<IAmazonDynamoDB>();
        mockClient
            .Setup(c => c.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetItemResponse { Item = null });
        var repository = new DynamoDbApplicantRepository(mockClient.Object, TableName);

        var result = await repository.GetByApplicationIdAsync(Guid.NewGuid(), Budget());

        Assert.Null(result);
    }

    [Fact]
    public async Task SaveUsesACreateConditionWhenExpectedVersionIsZero()
    {
        var mockClient = new Mock<IAmazonDynamoDB>();
        mockClient
            .Setup(c => c.PutItemAsync(It.IsAny<PutItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PutItemResponse());
        var repository = new DynamoDbApplicantRepository(mockClient.Object, TableName);
        var applicant = Applicant.CreateEmpty(Guid.NewGuid(), DateTimeOffset.UtcNow, "actor", "corr-1");
        applicant.ApplyUpdate(new ApplicantUpdate(LegalFirstName: "Jane"), DateTimeOffset.UtcNow);

        await repository.SaveAsync(applicant, expectedVersion: 0, Budget());

        mockClient.Verify(
            c => c.PutItemAsync(It.Is<PutItemRequest>(r => r.ConditionExpression == "attribute_not_exists(pk)"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SaveUsesAVersionMatchConditionWhenExpectedVersionIsNonZero()
    {
        var mockClient = new Mock<IAmazonDynamoDB>();
        mockClient
            .Setup(c => c.PutItemAsync(It.IsAny<PutItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PutItemResponse());
        var repository = new DynamoDbApplicantRepository(mockClient.Object, TableName);
        var applicant = Applicant.CreateEmpty(Guid.NewGuid(), DateTimeOffset.UtcNow, "actor", "corr-1");
        applicant.ApplyUpdate(new ApplicantUpdate(LegalFirstName: "Jane"), DateTimeOffset.UtcNow);

        await repository.SaveAsync(applicant, expectedVersion: 3, Budget());

        mockClient.Verify(
            c => c.PutItemAsync(It.Is<PutItemRequest>(r => r.ConditionExpression == "version = :expectedVersion"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SaveTranslatesAConditionalCheckFailureIntoConflictException()
    {
        var mockClient = new Mock<IAmazonDynamoDB>();
        mockClient
            .Setup(c => c.PutItemAsync(It.IsAny<PutItemRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConditionalCheckFailedException("failed"));
        var repository = new DynamoDbApplicantRepository(mockClient.Object, TableName);
        var applicant = Applicant.CreateEmpty(Guid.NewGuid(), DateTimeOffset.UtcNow, "actor", "corr-1");
        applicant.ApplyUpdate(new ApplicantUpdate(LegalFirstName: "Jane"), DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<ConflictException>(() => repository.SaveAsync(applicant, expectedVersion: 0, Budget()));
    }
}
