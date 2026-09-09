using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Gweb.Domain.Applications;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;

namespace Gweb.Adapters.Persistence;

/// <summary>
/// Real implementation of IApplicationRepository against the single table from
/// docs/adr/0003-dynamodb-table-strategy.md. Every AWS SDK call is wrapped with a
/// timeout derived from the deadline budget and translated into the shared domain
/// error taxonomy -- callers never see a raw AWS SDK exception or its internal
/// message (which can carry table names/ARNs and must not reach the client).
/// </summary>
public sealed class DynamoDbApplicationRepository(IAmazonDynamoDB client, string tableName) : IApplicationRepository
{
    private const string EntityType = "APPLICATION";
    private const string MetaSortKey = "META";

    public async Task CreateAsync(Application application, DeadlineBudget budget, CancellationToken cancellationToken = default)
    {
        var request = new PutItemRequest
        {
            TableName = tableName,
            Item = ToItem(application),
            ConditionExpression = "attribute_not_exists(pk)",
        };

        try
        {
            await DynamoDbCallExecutor.ExecuteAsync(ct => client.PutItemAsync(request, ct), budget, cancellationToken).ConfigureAwait(false);
        }
        catch (ConditionalCheckFailedException)
        {
            throw new ConflictException($"Application {application.Id} already exists.");
        }
    }

    public async Task<Application?> GetByIdAsync(Guid id, DeadlineBudget budget, CancellationToken cancellationToken = default)
    {
        var request = new GetItemRequest
        {
            TableName = tableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["pk"] = new AttributeValue { S = PartitionKey(id) },
                ["sk"] = new AttributeValue { S = MetaSortKey },
            },
            ConsistentRead = true,
        };

        var response = await DynamoDbCallExecutor.ExecuteAsync(ct => client.GetItemAsync(request, ct), budget, cancellationToken).ConfigureAwait(false);

        return response.IsItemSet ? FromItem(response.Item) : null;
    }

    public async Task UpdateAsync(Application application, long expectedVersion, DeadlineBudget budget, CancellationToken cancellationToken = default)
    {
        var request = new PutItemRequest
        {
            TableName = tableName,
            Item = ToItem(application),
            ConditionExpression = "attribute_exists(pk) AND version = :expectedVersion",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":expectedVersion"] = new() { N = expectedVersion.ToString(CultureInfo.InvariantCulture) },
            },
        };

        try
        {
            await DynamoDbCallExecutor.ExecuteAsync(ct => client.PutItemAsync(request, ct), budget, cancellationToken).ConfigureAwait(false);
        }
        catch (ConditionalCheckFailedException)
        {
            throw new ConflictException($"Application {application.Id} was modified concurrently.");
        }
    }

    internal static string PartitionKey(Guid id) => $"APP#{id}";

    private static Dictionary<string, AttributeValue> ToItem(Application application) => new()
    {
        ["pk"] = new AttributeValue { S = PartitionKey(application.Id) },
        ["sk"] = new AttributeValue { S = MetaSortKey },
        ["entityType"] = new AttributeValue { S = EntityType },
        ["applicationId"] = new AttributeValue { S = application.Id.ToString() },
        ["status"] = new AttributeValue { S = application.Status.ToString() },
        ["version"] = new AttributeValue { N = application.Version.ToString(CultureInfo.InvariantCulture) },
        ["createdAt"] = new AttributeValue { S = application.CreatedAt.ToString("O", CultureInfo.InvariantCulture) },
        ["updatedAt"] = new AttributeValue { S = application.UpdatedAt.ToString("O", CultureInfo.InvariantCulture) },
        ["createdBy"] = new AttributeValue { S = application.CreatedBy },
        ["correlationId"] = new AttributeValue { S = application.CorrelationId },
    };

    private static Application FromItem(Dictionary<string, AttributeValue> item) => Application.Rehydrate(
        Guid.Parse(item["applicationId"].S),
        Enum.Parse<ApplicationStatus>(item["status"].S),
        long.Parse(item["version"].N, CultureInfo.InvariantCulture),
        DateTimeOffset.Parse(item["createdAt"].S, CultureInfo.InvariantCulture),
        DateTimeOffset.Parse(item["updatedAt"].S, CultureInfo.InvariantCulture),
        item["createdBy"].S,
        item["correlationId"].S);
}
