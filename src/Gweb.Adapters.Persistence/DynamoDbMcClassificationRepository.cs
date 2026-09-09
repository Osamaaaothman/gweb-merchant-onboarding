using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Gweb.Domain.Evaluation;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;

namespace Gweb.Adapters.Persistence;

/// <summary>Item shape: pk=APP#{applicationId}, sk=MCC_CLASSIFICATION. See
/// DynamoDbApplicantRepository for the optimistic-concurrency convention this mirrors.</summary>
public sealed class DynamoDbMcClassificationRepository(IAmazonDynamoDB client, string tableName) : IMcClassificationRepository
{
    private const string EntityType = "MCC_CLASSIFICATION";
    private const string SortKey = "MCC_CLASSIFICATION";

    public async Task<McClassification?> GetByApplicationIdAsync(Guid applicationId, DeadlineBudget budget, CancellationToken cancellationToken = default)
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

    public async Task SaveAsync(McClassification classification, long expectedVersion, DeadlineBudget budget, CancellationToken cancellationToken = default)
    {
        var request = new PutItemRequest
        {
            TableName = tableName,
            Item = ToItem(classification),
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
            throw new ConflictException($"MCC classification for application {classification.ApplicationId} was modified concurrently.");
        }
    }

    private static Dictionary<string, AttributeValue> Key(Guid applicationId) => new()
    {
        ["pk"] = new AttributeValue { S = DynamoDbApplicationRepository.PartitionKey(applicationId) },
        ["sk"] = new AttributeValue { S = SortKey },
    };

    private static Dictionary<string, AttributeValue> ToItem(McClassification classification)
    {
        var item = Key(classification.ApplicationId);
        item["entityType"] = new AttributeValue { S = EntityType };
        item["version"] = new AttributeValue { N = classification.Version.ToString() };
        item["createdAt"] = new AttributeValue { S = classification.CreatedAt.ToString("O") };
        item["updatedAt"] = new AttributeValue { S = classification.UpdatedAt.ToString("O") };
        item["correlationId"] = new AttributeValue { S = classification.CorrelationId };

        if (classification.Candidates.Count > 0)
        {
            item["candidates"] = new AttributeValue
            {
                L = classification.Candidates.Select(c => new AttributeValue
                {
                    M = new Dictionary<string, AttributeValue>
                    {
                        ["mccCode"] = new AttributeValue { S = c.MccCode },
                        ["confidence"] = new AttributeValue { N = c.Confidence.ToString() },
                        ["explanation"] = new AttributeValue { S = c.Explanation },
                    },
                }).ToList(),
            };
        }
        item.PutIfNotNull("proposedMccCode", classification.ProposedMccCode);
        item.PutIfNotNull("proposedProvider", classification.ProposedProvider);
        item.PutIfNotNull("classifiedAt", classification.ClassifiedAt);
        item.PutIfNotNull("selfSelectedMccCode", classification.SelfSelectedMccCode);
        item.PutIfNotNull("selfSelectedAt", classification.SelfSelectedAt);

        return item;
    }

    private static McClassification FromItem(Guid applicationId, Dictionary<string, AttributeValue> item)
    {
        var candidates = item.TryGetValue("candidates", out var candidatesAttr) && candidatesAttr.L is not null
            ? candidatesAttr.L.Select(c => new McClassificationCandidate(
                c.M["mccCode"].S,
                decimal.Parse(c.M["confidence"].N),
                c.M["explanation"].S)).ToList()
            : [];

        return McClassification.Rehydrate(
            applicationId,
            candidates,
            item.GetOptionalString("proposedMccCode"),
            item.GetOptionalString("proposedProvider"),
            item.GetOptionalTimestamp("classifiedAt"),
            item.GetOptionalString("selfSelectedMccCode"),
            item.GetOptionalTimestamp("selfSelectedAt"),
            long.Parse(item["version"].N),
            DateTimeOffset.Parse(item["createdAt"].S),
            DateTimeOffset.Parse(item["updatedAt"].S),
            item["correlationId"].S);
    }
}
