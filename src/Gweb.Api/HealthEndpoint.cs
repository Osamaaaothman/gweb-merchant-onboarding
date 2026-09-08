using Gweb.Config;
using Gweb.Shared.Clock;
using Gweb.Shared.Correlation;
using Gweb.Shared.Logging;

namespace Gweb.Api;

internal static class HealthEndpoint
{
    public static Task<IResult> GetHealthAsync(
        HttpContext httpContext,
        IClock clock,
        BaseConfig config,
        StructuredLogger logger) =>
        RequestExecution.RunAsync(httpContext, "health", clock, config, logger, budget =>
        {
            var correlationId = CorrelationScope.GetCurrent()!.CorrelationId;
            var body = new { status = "ok", correlationId, remainingBudgetMs = budget.RemainingMs() };
            return Task.FromResult(Results.Json(body));
        });
}
