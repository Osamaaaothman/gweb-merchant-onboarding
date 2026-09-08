using Amazon.Lambda.AspNetCoreServer;
using Amazon.Lambda.Core;
using Gweb.Config;
using Gweb.Shared.Clock;
using Gweb.Shared.Correlation;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;
using Gweb.Shared.Logging;

namespace Gweb.Api;

/// <summary>
/// Shared endpoint boilerplate: correlation ID extraction, deadline budget from the
/// real Lambda remaining time, request/response logging, and error mapping. Every
/// endpoint (health, applications, ...) runs through this instead of repeating it.
/// </summary>
internal static class RequestExecution
{
    public static Task<IResult> RunAsync(
        HttpContext httpContext,
        string handlerName,
        IClock clock,
        BaseConfig config,
        StructuredLogger logger,
        Func<DeadlineBudget, Task<IResult>> action)
    {
        var correlationId = httpContext.Request.Headers.TryGetValue("x-correlation-id", out var headerValues)
            ? headerValues.ToString()
            : Guid.NewGuid().ToString();

        return CorrelationScope.Run(new CorrelationContext(correlationId, handlerName), async () =>
        {
            try
            {
                var budget = StartBudget(httpContext, clock, config);
                logger.Info("request_received");

                var result = await action(budget).ConfigureAwait(false);

                logger.Info("request_completed", new { durationMs = budget.ElapsedMs() });
                return result;
            }
            catch (Exception ex)
            {
                return HttpErrorMapper.Map(ex, logger);
            }
        });
    }

    private static DeadlineBudget StartBudget(HttpContext httpContext, IClock clock, BaseConfig config)
    {
        var lambdaContext = httpContext.Items[AbstractAspNetCoreFunction.LAMBDA_CONTEXT] as ILambdaContext;
        // Outside Lambda (e.g. `dotnet run` for local dev), there is no real ceiling --
        // the hard ceiling constant is a reasonable stand-in so the budget still
        // behaves sensibly rather than throwing.
        var remainingMs = lambdaContext is not null
            ? (long)lambdaContext.RemainingTime.TotalMilliseconds
            : DeadlineBudget.HardCeilingMs;

        return DeadlineBudget.Start(remainingMs, clock, config.DeadlineTargetMs);
    }
}
