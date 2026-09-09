using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Gweb.Domain.Documents;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;

namespace Gweb.Adapters.Persistence;

/// <summary>Item shape: pk=APP#{applicationId}, sk=DOC#{documentId} (see
/// docs/adr/0003-dynamodb-table-strategy.md access pattern #3). Same optimistic-
/// concurrency convention as DynamoDbApplicantRepository.</summary>
public sealed class DynamoDbDocumentRepository(IAmazonDynamoDB client, string tableName) : IDocumentRepository
{
    private const string EntityType = "DOCUMENT";

    public async Task<Document?> GetByIdAsync(Guid applicationId, Guid documentId, DeadlineBudget budget, CancellationToken cancellationToken = default)
    {
        var request = new GetItemRequest
        {
            TableName = tableName,
            Key = Key(applicationId, documentId),
            ConsistentRead = true,
        };

        var response = await DynamoDbCallExecutor.ExecuteAsync(ct => client.GetItemAsync(request, ct), budget, cancellationToken).ConfigureAwait(false);

        return response.IsItemSet ? FromItem(applicationId, documentId, response.Item) : null;
    }

    public async Task SaveAsync(Document document, long expectedVersion, DeadlineBudget budget, CancellationToken cancellationToken = default)
    {
        var request = new PutItemRequest
        {
            TableName = tableName,
            Item = ToItem(document),
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
            throw new ConflictException($"Document {document.Id} was modified concurrently.");
        }
    }

    private static Dictionary<string, AttributeValue> Key(Guid applicationId, Guid documentId) => new()
    {
        ["pk"] = new AttributeValue { S = DynamoDbApplicationRepository.PartitionKey(applicationId) },
        ["sk"] = new AttributeValue { S = $"DOC#{documentId}" },
    };

    private static Dictionary<string, AttributeValue> ToItem(Document document)
    {
        var item = Key(document.ApplicationId, document.Id);
        item["entityType"] = new AttributeValue { S = EntityType };
        item["documentId"] = new AttributeValue { S = document.Id.ToString() };
        item["documentType"] = new AttributeValue { S = document.Type.ToString() };
        item["status"] = new AttributeValue { S = document.Status.ToString() };
        item["originalFilename"] = new AttributeValue { S = document.OriginalFilename };
        item["contentType"] = new AttributeValue { S = document.ContentType };
        item["declaredSizeBytes"] = new AttributeValue { N = document.DeclaredSizeBytes.ToString() };
        item["s3Key"] = new AttributeValue { S = document.S3Key };
        item["declaredChecksumSha256"] = new AttributeValue { S = document.DeclaredChecksumSha256 };
        item["version"] = new AttributeValue { N = document.Version.ToString() };
        item["createdAt"] = new AttributeValue { S = document.CreatedAt.ToString("O") };
        item["updatedAt"] = new AttributeValue { S = document.UpdatedAt.ToString("O") };
        item["createdBy"] = new AttributeValue { S = document.CreatedBy };
        item["correlationId"] = new AttributeValue { S = document.CorrelationId };

        item.PutIfNotNull("actualSizeBytes", document.ActualSizeBytes);
        item.PutIfNotNull("actualChecksumSha256", document.ActualChecksumSha256);
        item.PutIfNotNull("uploadedAt", document.UploadedAt);
        item.PutIfNotNull("rejectionReason", document.RejectionReason);

        return item;
    }

    private static Document FromItem(Guid applicationId, Guid documentId, Dictionary<string, AttributeValue> item) =>
        Document.Rehydrate(
            documentId,
            applicationId,
            Enum.Parse<DocumentType>(item["documentType"].S),
            Enum.Parse<DocumentStatus>(item["status"].S),
            item["originalFilename"].S,
            item["contentType"].S,
            long.Parse(item["declaredSizeBytes"].N),
            item["s3Key"].S,
            item["declaredChecksumSha256"].S,
            item.GetOptionalLong("actualSizeBytes"),
            item.GetOptionalString("actualChecksumSha256"),
            item.GetOptionalTimestamp("uploadedAt"),
            item.GetOptionalString("rejectionReason"),
            long.Parse(item["version"].N),
            DateTimeOffset.Parse(item["createdAt"].S),
            DateTimeOffset.Parse(item["updatedAt"].S),
            item["createdBy"].S,
            item["correlationId"].S);
}
