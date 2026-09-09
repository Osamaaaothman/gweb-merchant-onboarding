using Gweb.Config;
using Gweb.Services.Applications;
using Gweb.Services.Documents;
using Gweb.Shared.Clock;
using Gweb.Shared.Correlation;
using Gweb.Shared.Errors;
using Gweb.Shared.Logging;

namespace Gweb.Api.Documents;

internal static class DocumentEndpoints
{
    private const string UnknownActor = "anonymous";

    public static void MapDocumentEndpoints(this WebApplication app)
    {
        app.MapPost("/v1/applications/{id}/documents/presign", PresignDocumentAsync);
        app.MapPost("/v1/applications/{id}/documents/{documentId}/complete", CompleteDocumentAsync);
        app.MapGet("/v1/applications/{id}/documents/{documentId}", GetDocumentAsync);
    }

    private static Task<IResult> PresignDocumentAsync(
        HttpContext httpContext,
        string id,
        PresignDocumentRequest request,
        ApplicationService applicationService,
        DocumentService documentService,
        IClock clock,
        BaseConfig config,
        StructuredLogger logger) =>
        RequestExecution.RunAsync(httpContext, "presign_document", clock, config, logger, async budget =>
        {
            var applicationId = ParseId(id, "id");
            // Confirms the application exists before creating a document under it --
            // same 404-on-the-parent convention as PatchApplicantAsync.
            await applicationService.GetApplicationAsync(applicationId, budget).ConfigureAwait(false);

            var actor = ReadActor(httpContext);
            var correlationId = CorrelationScope.GetCurrent()!.CorrelationId;

            var result = await documentService.RequestPresignedUploadAsync(
                applicationId,
                request.Type,
                request.OriginalFilename,
                request.ContentType,
                request.DeclaredSizeBytes,
                request.DeclaredChecksumSha256Base64,
                actor,
                correlationId,
                budget).ConfigureAwait(false);

            return Results.Created(
                $"/v1/applications/{applicationId}/documents/{result.Document.Id}",
                PresignedUploadResponse.From(result));
        });

    private static Task<IResult> CompleteDocumentAsync(
        HttpContext httpContext,
        string id,
        string documentId,
        DocumentService documentService,
        IClock clock,
        BaseConfig config,
        StructuredLogger logger) =>
        RequestExecution.RunAsync(httpContext, "complete_document", clock, config, logger, async budget =>
        {
            var applicationId = ParseId(id, "id");
            var docId = ParseId(documentId, "documentId");

            var document = await documentService.CompleteUploadAsync(applicationId, docId, budget).ConfigureAwait(false);

            return Results.Ok(DocumentResponse.From(document));
        });

    private static Task<IResult> GetDocumentAsync(
        HttpContext httpContext,
        string id,
        string documentId,
        DocumentService documentService,
        IClock clock,
        BaseConfig config,
        StructuredLogger logger) =>
        RequestExecution.RunAsync(httpContext, "get_document", clock, config, logger, async budget =>
        {
            var applicationId = ParseId(id, "id");
            var docId = ParseId(documentId, "documentId");

            var document = await documentService.GetDocumentAsync(applicationId, docId, budget).ConfigureAwait(false);

            return Results.Ok(DocumentResponse.From(document));
        });

    private static string ReadActor(HttpContext httpContext) =>
        httpContext.Request.Headers.TryGetValue("x-actor", out var actorHeader) ? actorHeader.ToString() : UnknownActor;

    private static Guid ParseId(string value, string fieldName)
    {
        // Strict format validation at the boundary -- never interpolate raw path input
        // into a DynamoDB key or S3 key. See docs/04-SECURITY-RULES.md §3.
        if (!Guid.TryParse(value, out var id))
        {
            throw new ValidationException($"{fieldName} must be a valid UUID.");
        }
        return id;
    }
}
