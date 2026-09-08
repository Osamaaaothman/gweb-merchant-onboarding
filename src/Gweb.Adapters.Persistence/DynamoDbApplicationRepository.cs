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
    private const long DefaultReserveMs = 500;
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
            await ExecuteAsync(ct => client.PutItemAsync(request, ct), budget, cancellationToken).ConfigureAwait(false);
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

        var response = await ExecuteAsync(ct => client.GetItemAsync(request, ct), budget, cancellationToken).ConfigureAwait(false);

        return response.IsItemSet ? FromItem(response.Item) : null;
    }

    /// <summary>
    /// Wraps a single outbound DynamoDB call: derives its timeout from the remaining
    /// deadline budget, and translates SDK-level failures into the domain taxonomy.
    /// Never lets a raw AmazonDynamoDBException (which can carry table/ARN details in
    /// its message) reach the HTTP boundary.
    /// </summary>
    private static async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        DeadlineBudget budget,
        CancellationToken cancellationToken)
    {
        var timeoutMs = budget.ForCall(DefaultReserveMs);
        if (timeoutMs <= 0)
        {
            throw new DependencyTimeoutException("Deadline budget exhausted before the DynamoDB call could be attempted.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));

        try
        {
            return await action(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Our own timeout fired, not the caller's cancellation -- report as a
            // structured, retryable dependency timeout, never a raw crash.
            throw new DependencyTimeoutException("DynamoDB call exceeded its allotted timeout budget.");
        }
        catch (AmazonDynamoDBException ex) when (ex is not ConditionalCheckFailedException)
        {
            // ConditionalCheckFailedException is deliberately NOT caught here -- it
            // is a subtype of AmazonDynamoDBException, but callers (e.g. CreateAsync)
            // need to translate it into a ConflictException, not a generic
            // "unavailable" one. Everything else (throttling, internal errors, ...)
            // becomes a retryable DependencyUnavailableException. No exception
            // message/details forwarded to the client either way -- AWS SDK exception
            // text can contain table names or ARNs.
            throw new DependencyUnavailableException("DynamoDB is temporarily unavailable.");
        }
    }

    private static string PartitionKey(Guid id) => $"APP#{id}";

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
