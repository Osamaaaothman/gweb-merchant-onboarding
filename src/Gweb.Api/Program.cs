using Amazon.Lambda.AspNetCoreServer;
using Amazon.Lambda.Core;
using Gweb.Config;
using Gweb.Shared.Clock;
using Gweb.Shared.Correlation;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;
using Gweb.Shared.Logging;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Hosts this whole app as one Lambda function behind API Gateway HTTP API. See
// docs/adr/0001-runtime-and-language-choice.md for why this is one Lambda for the
// whole API rather than one-per-route (the architecture doc's stated preference) —
// it is the idiomatic way to run ASP.NET Core on Lambda, and the tradeoff is recorded
// there, not silently accepted.
builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);

// Loaded and validated once at process start (= Lambda cold start on a fresh
// execution environment), not per request.
var config = AppConfigLoader.LoadBaseConfig(Environment.GetEnvironmentVariable);
builder.Services.AddSingleton(config);
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton(new StructuredLogger(Console.Out, config.LogLevel));

var app = builder.Build();

app.MapGet("/v1/health", (HttpContext httpContext, IClock clock, StructuredLogger logger, BaseConfig cfg) =>
{
    var correlationId = httpContext.Request.Headers.TryGetValue("x-correlation-id", out var headerValues)
        ? headerValues.ToString()
        : Guid.NewGuid().ToString();

    return CorrelationScope.Run(new CorrelationContext(correlationId, "health"), () =>
    {
        try
        {
            var lambdaContext = httpContext.Items[AbstractAspNetCoreFunction.LAMBDA_CONTEXT] as ILambdaContext;
            // Outside Lambda (e.g. `dotnet run` for local dev), there is no real
            // ceiling — the hard ceiling constant is a reasonable stand-in so the
            // budget still behaves sensibly rather than throwing.
            var remainingMs = lambdaContext is not null
                ? (long)lambdaContext.RemainingTime.TotalMilliseconds
                : DeadlineBudget.HardCeilingMs;

            var budget = DeadlineBudget.Start(remainingMs, clock, cfg.DeadlineTargetMs);

            logger.Info("request_received");

            var body = new { status = "ok", correlationId, remainingBudgetMs = budget.RemainingMs() };

            logger.Info("request_completed", new { durationMs = budget.ElapsedMs() });

            return Results.Json(body);
        }
        catch (Exception ex)
        {
            return HttpErrorMapper.Map(ex, logger);
        }
    });
});

app.Run();

// Exposes the implicit top-level-statements Program class to WebApplicationFactory<Program>
// in the integration tests.
public partial class Program;
