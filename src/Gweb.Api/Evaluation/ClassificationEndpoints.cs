using Gweb.Config;
using Gweb.Services.Applications;
using Gweb.Services.Evaluation;
using Gweb.Shared.Clock;
using Gweb.Shared.Correlation;
using Gweb.Shared.Errors;
using Gweb.Shared.Logging;

namespace Gweb.Api.Evaluation;

internal static class ClassificationEndpoints
{
    public static void MapClassificationEndpoints(this WebApplication app)
    {
        app.MapPost("/v1/applications/{id}/classify", ClassifyAsync);
        app.MapPost("/v1/applications/{id}/classify/confirm", ConfirmAsync);
        app.MapGet("/v1/applications/{id}/classify", GetAsync);
    }

    private static Task<IResult> ClassifyAsync(
        HttpContext httpContext,
        string id,
        ApplicationService applicationService,
        ClassificationService classificationService,
        IClock clock,
        BaseConfig config,
        StructuredLogger logger) =>
        RequestExecution.RunAsync(httpContext, "classify_mcc", clock, config, logger, async budget =>
        {
            var applicationId = ParseId(id, "id");
            // Confirms the application exists before touching its sub-resources --
            // same 404-on-the-parent convention as PatchApplicantAsync.
            await applicationService.GetApplicationAsync(applicationId, budget).ConfigureAwait(false);

            var correlationId = CorrelationScope.GetCurrent()!.CorrelationId;
            var classification = await classificationService
                .ClassifyAsync(applicationId, correlationId, budget)
                .ConfigureAwait(false);

            return Results.Ok(McClassificationResponse.From(classification));
        });

    private static Task<IResult> ConfirmAsync(
        HttpContext httpContext,
        string id,
        ConfirmMccRequest request,
        ApplicationService applicationService,
        ClassificationService classificationService,
        IClock clock,
        BaseConfig config,
        StructuredLogger logger) =>
        RequestExecution.RunAsync(httpContext, "confirm_mcc", clock, config, logger, async budget =>
        {
            var applicationId = ParseId(id, "id");
            await applicationService.GetApplicationAsync(applicationId, budget).ConfigureAwait(false);

            var correlationId = CorrelationScope.GetCurrent()!.CorrelationId;
            var classification = await classificationService
                .ConfirmSelfSelectedAsync(applicationId, request.MccCode, correlationId, budget)
                .ConfigureAwait(false);

            return Results.Ok(McClassificationResponse.From(classification));
        });

    private static Task<IResult> GetAsync(
        HttpContext httpContext,
        string id,
        ClassificationService classificationService,
        IClock clock,
        BaseConfig config,
        StructuredLogger logger) =>
        RequestExecution.RunAsync(httpContext, "get_mcc_classification", clock, config, logger, async budget =>
        {
            var applicationId = ParseId(id, "id");

            var classification = await classificationService.GetAsync(applicationId, budget).ConfigureAwait(false)
                ?? throw new NotFoundException($"No MCC classification exists yet for application {applicationId}.");

            return Results.Ok(McClassificationResponse.From(classification));
        });

    private static Guid ParseId(string value, string fieldName)
    {
        // Strict format validation at the boundary -- never interpolate raw path
        // input into a DynamoDB key. See docs/04-SECURITY-RULES.md §3.
        if (!Guid.TryParse(value, out var id))
        {
            throw new ValidationException($"{fieldName} must be a valid UUID.");
        }
        return id;
    }
}
