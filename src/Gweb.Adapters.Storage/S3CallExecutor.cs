using Amazon.S3;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;

namespace Gweb.Adapters.Storage;

/// <summary>
/// S3 analogue of Gweb.Adapters.Persistence.DynamoDbCallExecutor -- same
/// budget-derived timeout and error-translation shape, kept as a separate small copy
/// rather than a shared generic utility for now (a deliberate tradeoff: unifying the
/// two would mean touching already-tested Phase 2/3 DynamoDB code without a strong
/// need yet; worth doing if a third adapter needs this pattern).
/// </summary>
internal static class S3CallExecutor
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
            throw new DependencyTimeoutException("Deadline budget exhausted before the S3 call could be attempted.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));

        try
        {
            return await action(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DependencyTimeoutException("S3 call exceeded its allotted timeout budget.");
        }
        catch (AmazonS3Exception)
        {
            // No exception message/details forwarded to the client -- can carry
            // bucket names or ARNs.
            throw new DependencyUnavailableException("S3 is temporarily unavailable.");
        }
    }
}
