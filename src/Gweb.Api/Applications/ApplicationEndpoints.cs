using Gweb.Config;
using Gweb.Services.Applications;
using Gweb.Shared.Clock;
using Gweb.Shared.Correlation;
using Gweb.Shared.Errors;
using Gweb.Shared.Logging;

namespace Gweb.Api.Applications;

internal static class ApplicationEndpoints
{
    private const string UnknownActor = "anonymous";

    public static void MapApplicationEndpoints(this WebApplication app)
    {
        app.MapPost("/v1/applications", CreateApplicationAsync);
        app.MapGet("/v1/applications/{id}", GetApplicationAsync);
    }

    // No auth exists yet (see README "Assumptions") -- the actor is whatever the
    // client claims via a header, purely for audit-field purposes. A real authorizer
    // slots in here: replace this header read with the identity API Gateway/Cognito
    // attaches to the request.
    private static Task<IResult> CreateApplicationAsync(
        HttpContext httpContext,
        ApplicationService service,
        IClock clock,
        BaseConfig config,
        StructuredLogger logger) =>
        RequestExecution.RunAsync(httpContext, "create_application", clock, config, logger, async budget =>
        {
            var actor = httpContext.Request.Headers.TryGetValue("x-actor", out var actorHeader)
                ? actorHeader.ToString()
                : UnknownActor;
            var correlationId = CorrelationScope.GetCurrent()!.CorrelationId;

            var application = await service.CreateApplicationAsync(actor, correlationId, budget).ConfigureAwait(false);

            return Results.Created(
                $"/v1/applications/{application.Id}",
                ApplicationResponse.From(application, correlationId));
        });

    private static Task<IResult> GetApplicationAsync(
        HttpContext httpContext,
        string id,
        ApplicationService service,
        IClock clock,
        BaseConfig config,
        StructuredLogger logger) =>
        RequestExecution.RunAsync(httpContext, "get_application", clock, config, logger, async budget =>
        {
            if (!Guid.TryParse(id, out var applicationId))
            {
                // Strict format validation at the boundary -- never interpolate raw
                // path input into a DynamoDB key. See docs/04-SECURITY-RULES.md §3.
                throw new ValidationException("id must be a valid UUID.");
            }

            var correlationId = CorrelationScope.GetCurrent()!.CorrelationId;
            var application = await service.GetApplicationAsync(applicationId, budget).ConfigureAwait(false);

            return Results.Ok(ApplicationResponse.From(application, correlationId));
        });
}
