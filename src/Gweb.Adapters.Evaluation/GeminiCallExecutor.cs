using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;

namespace Gweb.Adapters.Evaluation;

/// <summary>
/// Same budget-derived-timeout shape as DynamoDbCallExecutor/S3CallExecutor -- kept as
/// its own small copy for the same reason those two are separate from each other (see
/// S3CallExecutor's doc comment): this is the "third adapter" that comment anticipated,
/// and unifying still isn't worth touching two already-shipped, fully-tested call
/// executors for.
///
/// One real difference from the other two: <see cref="MaxCallMs"/> is a hard cap
/// distinct from the usual "whatever's left in the budget" reserve pattern.
/// Empirically (see docs/adr/0006-ai-evaluation-provider.md), this evaluation
/// provider's "thinking" model can take 30+ seconds for even a small prompt --
/// uncomfortably close to the whole request's 35s internal target. Capping this one
/// call at 20s regardless of how much budget remains means a slow model response can
/// never eat the entire request; there is always time left for ClassificationService
/// to fall back to the mock provider and still respond within budget.
/// </summary>
internal static class GeminiCallExecutor
{
    private const long DefaultReserveMs = 3_000;
    private const long MaxCallMs = 20_000;

    public static async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        DeadlineBudget budget,
        CancellationToken cancellationToken)
    {
        var timeoutMs = budget.ForCall(DefaultReserveMs, MaxCallMs);
        if (timeoutMs <= 0)
        {
            throw new DependencyTimeoutException("Deadline budget exhausted before the Gemini call could be attempted.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));

        try
        {
            return await action(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DependencyTimeoutException("Gemini call exceeded its allotted timeout budget.");
        }
        catch (HttpRequestException)
        {
            // No exception message/details forwarded to the client -- DomainException.Details
            // is serialized straight into the HTTP response body by HttpErrorMapper, and an
            // HttpRequestException's message can carry hostnames/connection details. Same
            // convention as S3CallExecutor.
            throw new DependencyUnavailableException("Gemini is temporarily unavailable.");
        }
    }
}
