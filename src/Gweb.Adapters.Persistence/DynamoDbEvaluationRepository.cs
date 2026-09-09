using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Gweb.Domain.Evaluation;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;

namespace Gweb.Adapters.Persistence;

/// <summary>Item shape: pk=APP#{applicationId}, sk=EVALUATION. See
/// DynamoDbApplicantRepository for the optimistic-concurrency convention this mirrors.</summary>
public sealed class DynamoDbEvaluationRepository(IAmazonDynamoDB client, string tableName) : IEvaluationRepository
{
    private const string EntityType = "EVALUATION";
    private const string SortKey = "EVALUATION";

    public async Task<Evaluation?> GetByApplicationIdAsync(Guid applicationId, DeadlineBudget budget, CancellationToken cancellationToken = default)
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

    public async Task SaveAsync(Evaluation evaluation, long expectedVersion, DeadlineBudget budget, CancellationToken cancellationToken = default)
    {
        var request = new PutItemRequest
        {
            TableName = tableName,
            Item = ToItem(evaluation),
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
            throw new ConflictException($"Evaluation for application {evaluation.ApplicationId} was modified concurrently.");
        }
    }

    private static Dictionary<string, AttributeValue> Key(Guid applicationId) => new()
    {
        ["pk"] = new AttributeValue { S = DynamoDbApplicationRepository.PartitionKey(applicationId) },
        ["sk"] = new AttributeValue { S = SortKey },
    };

    private static Dictionary<string, AttributeValue> ToItem(Evaluation evaluation)
    {
        var item = Key(evaluation.ApplicationId);
        item["entityType"] = new AttributeValue { S = EntityType };
        item["status"] = new AttributeValue { S = evaluation.Status.ToString() };
        item["version"] = new AttributeValue { N = evaluation.Version.ToString() };
        item["createdAt"] = new AttributeValue { S = evaluation.CreatedAt.ToString("O") };
        item["updatedAt"] = new AttributeValue { S = evaluation.UpdatedAt.ToString("O") };
        item["correlationId"] = new AttributeValue { S = evaluation.CorrelationId };

        if (evaluation.Extraction is not null)
        {
            var extraction = evaluation.Extraction;
            var extractionMap = new Dictionary<string, AttributeValue>
            {
                ["provider"] = new AttributeValue { S = extraction.Provider },
            };
            extractionMap.PutIfNotNull("processor", extraction.Processor);
            extractionMap.PutIfNotNull("monthlyVolume", extraction.MonthlyVolume);
            extractionMap.PutIfNotNull("discountRatePercent", extraction.DiscountRatePercent);
            extractionMap.PutIfNotNull("perTransactionFee", extraction.PerTransactionFee);
            extractionMap.PutIfNotNull("monthlyFee", extraction.MonthlyFee);
            extractionMap.PutIfNotNull("chargebackFeeTotal", extraction.ChargebackFeeTotal);
            extractionMap.PutIfNotNull("statementPeriod", extraction.StatementPeriod);
            extractionMap.PutIfNotNull("commentary", extraction.Commentary);
            item["extraction"] = new AttributeValue { M = extractionMap };
        }
        if (evaluation.Calculated is not null)
        {
            var calculated = evaluation.Calculated;
            item["calculated"] = new AttributeValue
            {
                M = new Dictionary<string, AttributeValue>
                {
                    ["totalMonthlyCostAmount"] = new AttributeValue { N = calculated.TotalMonthlyCostAmount.ToString() },
                    ["effectiveRatePercent"] = new AttributeValue { N = calculated.EffectiveRatePercent.ToString() },
                    ["discountFeeAmount"] = new AttributeValue { N = calculated.DiscountFeeAmount.ToString() },
                    ["transactionFeeAmount"] = new AttributeValue { N = calculated.TransactionFeeAmount.ToString() },
                    ["monthlyFeeAmount"] = new AttributeValue { N = calculated.MonthlyFeeAmount.ToString() },
                    ["chargebackFeeAmount"] = new AttributeValue { N = calculated.ChargebackFeeAmount.ToString() },
                },
            };
        }
        if (evaluation.RiskSignals.Count > 0)
        {
            item["riskSignals"] = new AttributeValue
            {
                L = evaluation.RiskSignals.Select(signal =>
                {
                    var map = new Dictionary<string, AttributeValue>
                    {
                        ["code"] = new AttributeValue { S = signal.Code },
                        ["message"] = new AttributeValue { S = signal.Message },
                        ["sourceField"] = new AttributeValue { S = signal.SourceField },
                    };
                    if (signal.SourceDocumentId is not null)
                    {
                        map["sourceDocumentId"] = new AttributeValue { S = signal.SourceDocumentId.Value.ToString() };
                    }
                    return new AttributeValue { M = map };
                }).ToList(),
            };
        }
        if (evaluation.ProcessingStatementDocumentId is not null)
        {
            item["processingStatementDocumentId"] = new AttributeValue { S = evaluation.ProcessingStatementDocumentId.Value.ToString() };
        }
        item.PutIfNotNull("evaluatedAt", evaluation.EvaluatedAt);

        return item;
    }

    private static Evaluation FromItem(Guid applicationId, Dictionary<string, AttributeValue> item)
    {
        StatementExtraction? extraction = null;
        if (item.TryGetValue("extraction", out var extractionAttr) && extractionAttr.M is not null)
        {
            var m = extractionAttr.M;
            extraction = new StatementExtraction(
                m.GetOptionalString("processor"),
                m.GetOptionalDecimal("monthlyVolume"),
                m.GetOptionalDecimal("discountRatePercent"),
                m.GetOptionalDecimal("perTransactionFee"),
                m.GetOptionalDecimal("monthlyFee"),
                m.GetOptionalDecimal("chargebackFeeTotal"),
                m.GetOptionalString("statementPeriod"),
                m.GetOptionalString("commentary"),
                m["provider"].S);
        }

        EffectiveRateResult? calculated = null;
        if (item.TryGetValue("calculated", out var calculatedAttr) && calculatedAttr.M is not null)
        {
            var m = calculatedAttr.M;
            calculated = new EffectiveRateResult(
                decimal.Parse(m["totalMonthlyCostAmount"].N),
                decimal.Parse(m["effectiveRatePercent"].N),
                decimal.Parse(m["discountFeeAmount"].N),
                decimal.Parse(m["transactionFeeAmount"].N),
                decimal.Parse(m["monthlyFeeAmount"].N),
                decimal.Parse(m["chargebackFeeAmount"].N));
        }

        var riskSignals = item.TryGetValue("riskSignals", out var signalsAttr) && signalsAttr.L is not null
            ? signalsAttr.L.Select(s => new RiskSignal(
                s.M["code"].S,
                s.M["message"].S,
                s.M["sourceField"].S,
                s.M.TryGetValue("sourceDocumentId", out var docId) ? Guid.Parse(docId.S) : null)).ToList()
            : [];

        var processingStatementDocumentId = item.TryGetValue("processingStatementDocumentId", out var docIdAttr)
            ? Guid.Parse(docIdAttr.S)
            : (Guid?)null;

        return Evaluation.Rehydrate(
            applicationId,
            Enum.Parse<EvaluationStatus>(item["status"].S),
            extraction,
            calculated,
            riskSignals,
            processingStatementDocumentId,
            item.GetOptionalTimestamp("evaluatedAt"),
            long.Parse(item["version"].N),
            DateTimeOffset.Parse(item["createdAt"].S),
            DateTimeOffset.Parse(item["updatedAt"].S),
            item["correlationId"].S);
    }
}
