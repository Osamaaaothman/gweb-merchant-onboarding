using Gweb.Domain.Applications;

namespace Gweb.Api.Applications;

public sealed record ApplicationResponse(
    Guid Id,
    string Status,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string CorrelationId)
{
    public static ApplicationResponse From(Application application, string correlationId) =>
        new(application.Id, application.Status.ToString(), application.Version, application.CreatedAt, application.UpdatedAt, correlationId);
}
