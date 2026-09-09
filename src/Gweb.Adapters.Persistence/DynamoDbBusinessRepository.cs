using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Gweb.Domain.Applications;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;

namespace Gweb.Adapters.Persistence;

/// <summary>Item shape: pk=APP#{applicationId}, sk=BUSINESS. See
/// DynamoDbApplicantRepository for the optimistic-concurrency convention this mirrors.</summary>
public sealed class DynamoDbBusinessRepository(IAmazonDynamoDB client, string tableName) : IBusinessRepository
{
    private const string EntityType = "BUSINESS";
    private const string SortKey = "BUSINESS";

    public async Task<Business?> GetByApplicationIdAsync(Guid applicationId, DeadlineBudget budget, CancellationToken cancellationToken = default)
    {
        var request = new GetItemRequest
        {
            TableName = tableName,
            Key = Key(applicationId),
            ConsistentRead = true,
        };

        var response = await DynamoDbCallExecutor.ExecuteAsync(ct => client.GetItemAsync(request, ct), budget, cancellationToken).ConfigureAwait(false);

        return response.IsItemSet ? FromItem(applicationId, response.Item) : null;
    }

    public async Task SaveAsync(Business business, long expectedVersion, DeadlineBudget budget, CancellationToken cancellationToken = default)
    {
        var request = new PutItemRequest
        {
            TableName = tableName,
            Item = ToItem(business),
            ConditionExpression = expectedVersion == 0 ? "attribute_not_exists(pk)" : "version = :expectedVersion",
            ExpressionAttributeValues = expectedVersion == 0
                ? null
                : new Dictionary<string, AttributeValue> { [":expectedVersion"] = new() { N = expectedVersion.ToString() } },
        };

        try
        {
            await DynamoDbCallExecutor.ExecuteAsync(ct => client.PutItemAsync(request, ct), budget, cancellationToken).ConfigureAwait(false);
        }
        catch (ConditionalCheckFailedException)
        {
            throw new ConflictException($"Business for application {business.ApplicationId} was modified concurrently.");
        }
    }

    private static Dictionary<string, AttributeValue> Key(Guid applicationId) => new()
    {
        ["pk"] = new AttributeValue { S = DynamoDbApplicationRepository.PartitionKey(applicationId) },
        ["sk"] = new AttributeValue { S = SortKey },
    };

    private static Dictionary<string, AttributeValue> ToItem(Business business)
    {
        var item = Key(business.ApplicationId);
        item["entityType"] = new AttributeValue { S = EntityType };
        item["version"] = new AttributeValue { N = business.Version.ToString() };
        item["createdAt"] = new AttributeValue { S = business.CreatedAt.ToString("O") };
        item["updatedAt"] = new AttributeValue { S = business.UpdatedAt.ToString("O") };
        item["createdBy"] = new AttributeValue { S = business.CreatedBy };
        item["correlationId"] = new AttributeValue { S = business.CorrelationId };

        item.PutIfNotNull("legalBusinessName", business.LegalBusinessName);
        item.PutIfNotNull("dbaName", business.DbaName);
        item.PutIfNotNull("entityType", business.EntityType);
        item.PutIfNotNull("formationCountry", business.FormationCountry);
        item.PutIfNotNull("formationState", business.FormationState);
        if (business.RegistrationIdentifier is not null)
        {
            item["registrationIdentifier"] = new AttributeValue
            {
                M = new Dictionary<string, AttributeValue>
                {
                    ["type"] = new AttributeValue { S = business.RegistrationIdentifier.Type.ToString() },
                    ["maskedValue"] = new AttributeValue { S = business.RegistrationIdentifier.MaskedValue },
                },
            };
        }
        item.PutIfNotNull("registeredAddress", business.RegisteredAddress);
        item.PutIfNotNull("operatingAddress", business.OperatingAddress);
        item.PutIfNotNull("websiteUrl", business.WebsiteUrl);
        item.PutIfNotNull("businessDescription", business.BusinessDescription);
        item.PutIfNotNull("businessStartDate", business.BusinessStartDate);
        if (business.VolumeProfile is not null)
        {
            var vp = business.VolumeProfile;
            item["volumeProfile"] = new AttributeValue
            {
                M = new Dictionary<string, AttributeValue>
                {
                    ["expectedAnnualCardVolume"] = new AttributeValue { N = vp.ExpectedAnnualCardVolume.ToString() },
                    ["averageTicket"] = new AttributeValue { N = vp.AverageTicket.ToString() },
                    ["highestTicket"] = new AttributeValue { N = vp.HighestTicket.ToString() },
                    ["monthlyTransactionCount"] = new AttributeValue { N = vp.MonthlyTransactionCount.ToString() },
                    ["cardPresentPercentage"] = new AttributeValue { N = vp.CardPresentPercentage.ToString() },
                    ["ecommercePercentage"] = new AttributeValue { N = vp.EcommercePercentage.ToString() },
                },
            };
        }
        if (business.BeneficialOwners.Count > 0)
        {
            item["beneficialOwners"] = new AttributeValue
            {
                L = business.BeneficialOwners.Select(owner => new AttributeValue
                {
                    M = new Dictionary<string, AttributeValue>
                    {
                        ["name"] = new AttributeValue { S = owner.Name },
                        ["roleTitle"] = new AttributeValue { S = owner.RoleTitle },
                        ["ownershipPercentage"] = new AttributeValue { N = owner.OwnershipPercentage.ToString() },
                    },
                }).ToList(),
            };
        }
        if (business.SettlementBankAccount is not null)
        {
            var bank = business.SettlementBankAccount;
            item["settlementBankAccount"] = new AttributeValue
            {
                M = new Dictionary<string, AttributeValue>
                {
                    ["accountHolder"] = new AttributeValue { S = bank.AccountHolder },
                    ["bankName"] = new AttributeValue { S = bank.BankName },
                    ["last4"] = new AttributeValue { S = bank.Last4 },
                    ["statementDate"] = new AttributeValue { S = bank.StatementDate.ToString("yyyy-MM-dd") },
                },
            };
        }
        item.PutIfNotNull("existingProcessor", business.ExistingProcessor);

        return item;
    }

    private static Business FromItem(Guid applicationId, Dictionary<string, AttributeValue> item)
    {
        RegistrationIdentifier? registrationIdentifier = null;
        if (item.TryGetValue("registrationIdentifier", out var regIdAttr) && regIdAttr.M is not null)
        {
            registrationIdentifier = RegistrationIdentifier.FromMaskedValue(
                Enum.Parse<RegistrationIdentifierType>(regIdAttr.M["type"].S),
                regIdAttr.M["maskedValue"].S);
        }

        VolumeProfile? volumeProfile = null;
        if (item.TryGetValue("volumeProfile", out var vpAttr) && vpAttr.M is not null)
        {
            var m = vpAttr.M;
            volumeProfile = new VolumeProfile(
                decimal.Parse(m["expectedAnnualCardVolume"].N),
                decimal.Parse(m["averageTicket"].N),
                decimal.Parse(m["highestTicket"].N),
                int.Parse(m["monthlyTransactionCount"].N),
                decimal.Parse(m["cardPresentPercentage"].N),
                decimal.Parse(m["ecommercePercentage"].N));
        }

        var beneficialOwners = item.TryGetValue("beneficialOwners", out var ownersAttr) && ownersAttr.L is not null
            ? ownersAttr.L.Select(o => new BeneficialOwner(
                o.M["name"].S,
                o.M["roleTitle"].S,
                decimal.Parse(o.M["ownershipPercentage"].N))).ToList()
            : [];

        SettlementBankAccount? settlementBankAccount = null;
        if (item.TryGetValue("settlementBankAccount", out var bankAttr) && bankAttr.M is not null)
        {
            var m = bankAttr.M;
            settlementBankAccount = SettlementBankAccount.FromMaskedLast4(
                m["accountHolder"].S, m["bankName"].S, m["last4"].S, DateOnly.Parse(m["statementDate"].S));
        }

        return Business.Rehydrate(
            applicationId,
            item.GetOptionalString("legalBusinessName"),
            item.GetOptionalString("dbaName"),
            item.GetOptionalEnum<EntityType>("entityType"),
            item.GetOptionalString("formationCountry"),
            item.GetOptionalString("formationState"),
            registrationIdentifier,
            item.GetOptionalAddress("registeredAddress"),
            item.GetOptionalAddress("operatingAddress"),
            item.GetOptionalString("websiteUrl"),
            item.GetOptionalString("businessDescription"),
            item.GetOptionalDate("businessStartDate"),
            volumeProfile,
            beneficialOwners,
            settlementBankAccount,
            item.GetOptionalString("existingProcessor"),
            long.Parse(item["version"].N),
            DateTimeOffset.Parse(item["createdAt"].S),
            DateTimeOffset.Parse(item["updatedAt"].S),
            item["createdBy"].S,
            item["correlationId"].S);
    }
}
