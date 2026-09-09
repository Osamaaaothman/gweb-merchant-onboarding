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
        app.MapPatch("/v1/applications/{id}/applicant", PatchApplicantAsync);
        app.MapPatch("/v1/applications/{id}/business", PatchBusinessAsync);
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
            var actor = ReadActor(httpContext);
            var correlationId = CorrelationScope.GetCurrent()!.CorrelationId;

            var application = await service.CreateApplicationAsync(actor, correlationId, budget).ConfigureAwait(false);

            return Results.Created(
                $"/v1/applications/{application.Id}",
                ApplicationResponse.From(application, correlationId));
        });

    private static Task<IResult> GetApplicationAsync(
        HttpContext httpContext,
        string id,
        ApplicationService applicationService,
        ApplicantService applicantService,
        BusinessService businessService,
        IClock clock,
        BaseConfig config,
        StructuredLogger logger) =>
        RequestExecution.RunAsync(httpContext, "get_application", clock, config, logger, async budget =>
        {
            var applicationId = ParseApplicationId(id);
            var correlationId = CorrelationScope.GetCurrent()!.CorrelationId;

            var application = await applicationService.GetApplicationAsync(applicationId, budget).ConfigureAwait(false);
            var applicant = await applicantService.GetApplicantAsync(applicationId, budget).ConfigureAwait(false);
            var business = await businessService.GetBusinessAsync(applicationId, budget).ConfigureAwait(false);

            return Results.Ok(ApplicationDetailResponse.From(application, applicant, business, correlationId));
        });

    private static Task<IResult> PatchApplicantAsync(
        HttpContext httpContext,
        string id,
        PatchApplicantRequest request,
        ApplicationService applicationService,
        ApplicantService applicantService,
        IClock clock,
        BaseConfig config,
        StructuredLogger logger) =>
        RequestExecution.RunAsync(httpContext, "patch_applicant", clock, config, logger, async budget =>
        {
            var applicationId = ParseApplicationId(id);
            // Confirms the application exists before touching its sub-resources --
            // 404 on the parent, not a confusing "created an orphan applicant" state.
            await applicationService.GetApplicationAsync(applicationId, budget).ConfigureAwait(false);

            var actor = ReadActor(httpContext);
            var correlationId = CorrelationScope.GetCurrent()!.CorrelationId;

            var applicant = await applicantService
                .UpdateApplicantAsync(applicationId, request.ToDomain(), actor, correlationId, budget)
                .ConfigureAwait(false);

            return Results.Ok(ApplicantResponse.From(applicant));
        });

    private static Task<IResult> PatchBusinessAsync(
        HttpContext httpContext,
        string id,
        PatchBusinessRequest request,
        ApplicationService applicationService,
        BusinessService businessService,
        IClock clock,
        BaseConfig config,
        StructuredLogger logger) =>
        RequestExecution.RunAsync(httpContext, "patch_business", clock, config, logger, async budget =>
        {
            var applicationId = ParseApplicationId(id);
            await applicationService.GetApplicationAsync(applicationId, budget).ConfigureAwait(false);

            var actor = ReadActor(httpContext);
            var correlationId = CorrelationScope.GetCurrent()!.CorrelationId;

            var business = await businessService
                .UpdateBusinessAsync(applicationId, request.ToDomain(), actor, correlationId, budget)
                .ConfigureAwait(false);

            return Results.Ok(BusinessResponse.From(business));
        });

    private static string ReadActor(HttpContext httpContext) =>
        httpContext.Request.Headers.TryGetValue("x-actor", out var actorHeader) ? actorHeader.ToString() : UnknownActor;

    private static Guid ParseApplicationId(string id)
    {
        // Strict format validation at the boundary -- never interpolate raw path
        // input into a DynamoDB key. See docs/04-SECURITY-RULES.md §3.
        if (!Guid.TryParse(id, out var applicationId))
        {
            throw new ValidationException("id must be a valid UUID.");
        }
        return applicationId;
    }
}
