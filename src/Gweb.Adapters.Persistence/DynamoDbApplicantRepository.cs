using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Gweb.Domain.Applications;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;

namespace Gweb.Adapters.Persistence;

/// <summary>
/// Item shape: pk=APP#{applicationId}, sk=PERSON#APPLICANT (see
/// docs/adr/0003-dynamodb-table-strategy.md). Optimistic concurrency via a conditional
/// PutItem on the version the caller read -- expectedVersion=0 means "must not already
/// exist" (attribute_not_exists(pk)), any other value means "must currently be exactly
/// this version".
/// </summary>
public sealed class DynamoDbApplicantRepository(IAmazonDynamoDB client, string tableName) : IApplicantRepository
{
    private const string EntityType = "APPLICANT";
    private const string SortKey = "PERSON#APPLICANT";

    public async Task<Applicant?> GetByApplicationIdAsync(Guid applicationId, DeadlineBudget budget, CancellationToken cancellationToken = default)
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

    public async Task SaveAsync(Applicant applicant, long expectedVersion, DeadlineBudget budget, CancellationToken cancellationToken = default)
    {
        var request = new PutItemRequest
        {
            TableName = tableName,
            Item = ToItem(applicant),
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
            throw new ConflictException($"Applicant for application {applicant.ApplicationId} was modified concurrently.");
        }
    }

    private static Dictionary<string, AttributeValue> Key(Guid applicationId) => new()
    {
        ["pk"] = new AttributeValue { S = DynamoDbApplicationRepository.PartitionKey(applicationId) },
        ["sk"] = new AttributeValue { S = SortKey },
    };

    private static Dictionary<string, AttributeValue> ToItem(Applicant applicant)
    {
        var item = Key(applicant.ApplicationId);
        item["entityType"] = new AttributeValue { S = EntityType };
        item["version"] = new AttributeValue { N = applicant.Version.ToString() };
        item["createdAt"] = new AttributeValue { S = applicant.CreatedAt.ToString("O") };
        item["updatedAt"] = new AttributeValue { S = applicant.UpdatedAt.ToString("O") };
        item["createdBy"] = new AttributeValue { S = applicant.CreatedBy };
        item["correlationId"] = new AttributeValue { S = applicant.CorrelationId };

        item.PutIfNotNull("legalFirstName", applicant.LegalFirstName);
        item.PutIfNotNull("legalMiddleName", applicant.LegalMiddleName);
        item.PutIfNotNull("legalLastName", applicant.LegalLastName);
        item.PutIfNotNull("dateOfBirth", applicant.DateOfBirth);
        item.PutIfNotNull("residentialAddress", applicant.ResidentialAddress);
        item.PutIfNotNull("email", applicant.Email);
        item.PutIfNotNull("phone", applicant.Phone);
        item.PutIfNotNull("roleTitle", applicant.RoleTitle);
        item.PutIfNotNull("ownershipPercentage", applicant.OwnershipPercentage);
        if (applicant.GovernmentId is not null)
        {
            item["governmentId"] = new AttributeValue
            {
                M = new Dictionary<string, AttributeValue>
                {
                    ["type"] = new AttributeValue { S = applicant.GovernmentId.Type.ToString() },
                    ["last4"] = new AttributeValue { S = applicant.GovernmentId.Last4 },
                },
            };
        }
        item.PutIfNotNull("consentTimestamp", applicant.ConsentTimestamp);
        item.PutIfNotNull("consentVersion", applicant.ConsentVersion);

        return item;
    }

    private static Applicant FromItem(Guid applicationId, Dictionary<string, AttributeValue> item)
    {
        GovernmentIdentification? governmentId = null;
        if (item.TryGetValue("governmentId", out var govIdAttr) && govIdAttr.M is not null)
        {
            governmentId = GovernmentIdentification.FromMaskedLast4(
                Enum.Parse<GovernmentIdentificationType>(govIdAttr.M["type"].S),
                govIdAttr.M["last4"].S);
        }

        return Applicant.Rehydrate(
            applicationId,
            item.GetOptionalString("legalFirstName"),
            item.GetOptionalString("legalMiddleName"),
            item.GetOptionalString("legalLastName"),
            item.GetOptionalDate("dateOfBirth"),
            item.GetOptionalAddress("residentialAddress"),
            item.GetOptionalString("email"),
            item.GetOptionalString("phone"),
            item.GetOptionalString("roleTitle"),
            item.GetOptionalDecimal("ownershipPercentage"),
            governmentId,
            item.GetOptionalTimestamp("consentTimestamp"),
            item.GetOptionalString("consentVersion"),
            long.Parse(item["version"].N),
            DateTimeOffset.Parse(item["createdAt"].S),
            DateTimeOffset.Parse(item["updatedAt"].S),
            item["createdBy"].S,
            item["correlationId"].S);
    }
}
