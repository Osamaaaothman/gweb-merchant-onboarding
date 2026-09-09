using Gweb.Config;
using Gweb.Domain.Evaluation;
using Gweb.Services.Applications;
using Gweb.Shared.Clock;
using Gweb.Shared.Correlation;
using Gweb.Shared.Errors;
using Gweb.Shared.Logging;
using EvaluationService = Gweb.Services.Evaluation.EvaluationService;

namespace Gweb.Api.Evaluation;

internal static class EvaluationEndpoints
{
    public static void MapEvaluationEndpoints(this WebApplication app)
    {
        app.MapPost("/v1/applications/{id}/evaluate", EvaluateAsync);
        app.MapGet("/v1/applications/{id}/evaluation", GetAsync);
    }

    private static Task<IResult> EvaluateAsync(
        HttpContext httpContext,
        string id,
        EvaluateRequest request,
        ApplicationService applicationService,
        EvaluationService evaluationService,
        IClock clock,
        BaseConfig config,
        StructuredLogger logger) =>
        RequestExecution.RunAsync(httpContext, "evaluate", clock, config, logger, async budget =>
        {
            var applicationId = ParseId(id, "id");
            // Confirms the application exists before touching its sub-resources --
            // same 404-on-the-parent convention as PatchApplicantAsync.
            await applicationService.GetApplicationAsync(applicationId, budget).ConfigureAwait(false);

            var correlationId = CorrelationScope.GetCurrent()!.CorrelationId;
            var evaluation = await evaluationService
                .EvaluateAsync(applicationId, correlationId, request.ProcessingStatementDocumentId, budget)
                .ConfigureAwait(false);

            var response = EvaluationResponse.From(evaluation);
            // Brief "async fallback (202 + PROCESSING + poll) if the budget cannot be
            // met" -- Location points at the GET endpoint the caller should poll.
            return evaluation.Status == EvaluationStatus.Processing
                ? Results.Accepted($"/v1/applications/{applicationId}/evaluation", response)
                : Results.Ok(response);
        });

    private static Task<IResult> GetAsync(
        HttpContext httpContext,
        string id,
        EvaluationService evaluationService,
        IClock clock,
        BaseConfig config,
        StructuredLogger logger) =>
        RequestExecution.RunAsync(httpContext, "get_evaluation", clock, config, logger, async budget =>
        {
            var applicationId = ParseId(id, "id");

            var evaluation = await evaluationService.GetAsync(applicationId, budget).ConfigureAwait(false)
                ?? throw new NotFoundException($"No evaluation exists yet for application {applicationId}.");

            return Results.Ok(EvaluationResponse.From(evaluation));
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
