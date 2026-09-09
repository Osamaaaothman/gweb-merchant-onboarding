using Gweb.Domain.Applications;

namespace Gweb.Api.Applications;

/// <summary>
/// The "normalized state" GET /v1/applications/{id} returns per the brief's API
/// surface -- the full aggregate, not just the envelope POST returns.
/// </summary>
public sealed record ApplicationDetailResponse(
    Guid Id,
    string Status,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    ApplicantResponse? Applicant,
    BusinessResponse? Business,
    CompletenessResponse Completeness,
    string CorrelationId)
{
    public static ApplicationDetailResponse From(
        Application application, Applicant? applicant, Business? business, string correlationId) =>
        new(
            application.Id,
            application.Status.ToString(),
            application.Version,
            application.CreatedAt,
            application.UpdatedAt,
            ApplicantResponse.From(applicant),
            BusinessResponse.From(business),
            CompletenessResponse.From(CompletenessChecker.Check(applicant, business)),
            correlationId);
}
