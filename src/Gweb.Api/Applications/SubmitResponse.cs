using Gweb.Api.Documents;
using Gweb.Api.Evaluation;
using Gweb.Services.Applications;

namespace Gweb.Api.Applications;

/// <summary>The "normalized internal review payload" the brief asks a successful
/// submission to produce -- everything a downstream processor mapping, or a reviewer,
/// would need in one response. Never carries raw PII (every nested *Response type it
/// composes already masks at the source -- see ApplicantResponse's own doc comment).</summary>
public sealed record SubmitResponse(
    Guid ApplicationId,
    string Status,
    DateTimeOffset SubmittedAt,
    long Version,
    ApplicantResponse? Applicant,
    BusinessResponse? Business,
    IReadOnlyList<DocumentResponse> Documents,
    McClassificationResponse? Classification,
    EvaluationResponse? Evaluation)
{
    public static SubmitResponse From(SubmissionResult result) => new(
        result.Application.Id,
        result.Application.Status.ToString(),
        result.Application.UpdatedAt,
        result.Application.Version,
        ApplicantResponse.From(result.Applicant),
        BusinessResponse.From(result.Business),
        [.. result.Documents.Select(DocumentResponse.From)],
        result.Classification is null ? null : McClassificationResponse.From(result.Classification),
        result.Evaluation is null ? null : EvaluationResponse.From(result.Evaluation));
}
