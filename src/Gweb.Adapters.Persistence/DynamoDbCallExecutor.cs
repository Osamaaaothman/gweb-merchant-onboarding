using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;

namespace Gweb.Adapters.Persistence;

/// <summary>
/// Wraps a single outbound DynamoDB call: derives its timeout from the remaining
/// deadline budget, and translates SDK-level failures into the domain taxonomy. Never
/// lets a raw AmazonDynamoDBException (which can carry table/ARN details in its
/// message) reach the HTTP boundary. Shared by every DynamoDB*Repository so the
/// timeout/error-translation behavior can't drift between them.
/// </summary>
internal static class DynamoDbCallExecutor
{
    private const long DefaultReserveMs = 500;

    public static async Task<T> ExecuteAsync<T>(
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
            // ConditionalCheckFailedException is deliberately NOT caught here -- it is
            // a subtype of AmazonDynamoDBException, but callers need to translate it
            // into a ConflictException, not a generic "unavailable" one. Deliberately
            // no exception message/details forwarded to the client either way -- AWS
            // SDK exception text can contain table names or ARNs.
            throw new DependencyUnavailableException("DynamoDB is temporarily unavailable.");
        }
    }
}
