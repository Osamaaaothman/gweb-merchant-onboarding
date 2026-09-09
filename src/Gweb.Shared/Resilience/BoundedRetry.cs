using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;

namespace Gweb.Shared.Resilience;

/// <summary>
/// Budget-aware bounded retry with exponential backoff and full jitter (the AWS-recommended
/// shape: delay = Random(0, min(cap, base * 2^attempt)), not a fixed or capped-only delay).
/// Retries only <see cref="DomainException"/>s whose own <see cref="DomainException.Retryable"/>
/// flag is true -- DependencyTimeoutException/DependencyUnavailableException today -- reusing
/// that existing classification as the single source of truth for "worth retrying" instead of
/// inventing a second one. Never sleeps toward a retry the remaining deadline budget could not
/// actually afford: if the computed delay plus a minimal reserve would leave less than that
/// reserve in the budget, the most recent failure is rethrown immediately rather than waiting
/// for an attempt that would just fail at DeadlineBudget.ForCall &lt;= 0 anyway.
/// </summary>
public static class BoundedRetry
{
    private const long BaseDelayMs = 100;
    private const long MaxDelayMs = 2_000;
    private const long MinReserveToRetryMs = 200;

    public static async Task<T> ExecuteAsync<T>(
        Func<Task<T>> attempt,
        DeadlineBudget budget,
        IDelay delay,
        CancellationToken cancellationToken,
        int maxAttempts = 3)
    {
        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), maxAttempts, "At least one attempt is required.");
        }

        for (var attemptNumber = 1; ; attemptNumber++)
        {
            try
            {
                return await attempt().ConfigureAwait(false);
            }
            catch (DomainException ex) when (ex.Retryable && attemptNumber < maxAttempts)
            {
                var backoffCapMs = Math.Min(MaxDelayMs, BaseDelayMs * (1L << (attemptNumber - 1)));
                var jitteredMs = (long)(Random.Shared.NextDouble() * backoffCapMs);

                if (!budget.CanAttempt(MinReserveToRetryMs + jitteredMs))
                {
                    throw;
                }

                await delay.WaitAsync(jitteredMs, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
