using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Gweb.Adapters.Persistence;
using Gweb.Domain.Applications;
using Gweb.Shared.Clock;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;
using Moq;

namespace Gweb.Tests.Adapters.Persistence;

public class DynamoDbBusinessRepositoryTests
{
    private const string TableName = "test-table";

    private static DeadlineBudget Budget() => DeadlineBudget.Start(35_000, new FakeClock(0), targetMs: 35_000);

    private static Address ValidAddress() => new("1 Main St", null, "Springfield", "IL", "62701", "US");

    [Fact]
    public async Task SaveRoundTripsEveryFieldIncludingNestedMapsAndListsThroughAFakeInMemoryDynamoDb()
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
        var repository = new DynamoDbBusinessRepository(mockClient.Object, TableName);

        var business = Business.CreateEmpty(Guid.NewGuid(), DateTimeOffset.UtcNow, "actor", "corr-1");
        business.ApplyUpdate(
            new BusinessUpdate(
                LegalBusinessName: "Testerson Trading LLC",
                DbaName: "TT",
                EntityType: EntityType.Llc,
                FormationCountry: "US",
                FormationState: "DE",
                RegistrationIdentifier: new RegistrationIdentifierInput(RegistrationIdentifierType.Ein, "00-0000000"),
                RegisteredAddress: ValidAddress(),
                OperatingAddress: ValidAddress(),
                WebsiteUrl: "https://example.invalid",
                BusinessDescription: "Sells widgets online",
                BusinessStartDate: new DateOnly(2020, 1, 1),
                VolumeProfile: new VolumeProfile(1_000_000m, 50m, 500m, 2000, 40m, 60m),
                BeneficialOwners: [new BeneficialOwner("John Doe", "Co-owner", 40m), new BeneficialOwner("Jane Roe", "Co-owner", 20m)],
                SettlementBankAccount: new SettlementBankAccountInput("Testerson Trading LLC", "Test Bank", "000123456789", new DateOnly(2026, 1, 1)),
                ExistingProcessor: "Acme Payments"),
            DateTimeOffset.UtcNow);

        await repository.SaveAsync(business, expectedVersion: 0, Budget());
        var result = await repository.GetByApplicationIdAsync(business.ApplicationId, Budget());

        Assert.NotNull(result);
        Assert.Equal("Testerson Trading LLC", result!.LegalBusinessName);
        Assert.Equal("TT", result.DbaName);
        Assert.Equal(EntityType.Llc, result.EntityType);
        Assert.Equal("**-***0000", result.RegistrationIdentifier!.MaskedValue);
        Assert.Equal("Springfield", result.RegisteredAddress!.City);
        Assert.Equal(2000, result.VolumeProfile!.MonthlyTransactionCount);
        Assert.Equal(2, result.BeneficialOwners.Count);
        Assert.Equal("John Doe", result.BeneficialOwners[0].Name);
        Assert.Equal(40m, result.BeneficialOwners[0].OwnershipPercentage);
        Assert.Equal("6789", result.SettlementBankAccount!.Last4);
        Assert.Equal("Acme Payments", result.ExistingProcessor);
    }

    [Fact]
    public async Task GetReturnsNullWhenNoItemExists()
    {
        var mockClient = new Mock<IAmazonDynamoDB>();
        mockClient
            .Setup(c => c.GetItemAsync(It.IsAny<GetItemRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetItemResponse { Item = null });
        var repository = new DynamoDbBusinessRepository(mockClient.Object, TableName);

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
        var repository = new DynamoDbBusinessRepository(mockClient.Object, TableName);
        var business = Business.CreateEmpty(Guid.NewGuid(), DateTimeOffset.UtcNow, "actor", "corr-1");
        business.ApplyUpdate(new BusinessUpdate(LegalBusinessName: "A"), DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<ConflictException>(() => repository.SaveAsync(business, expectedVersion: 0, Budget()));
    }
}
