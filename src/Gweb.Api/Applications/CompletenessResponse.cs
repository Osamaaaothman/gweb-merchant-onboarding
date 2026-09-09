using Gweb.Domain.Applications;

namespace Gweb.Api.Applications;

public sealed record CompletenessResponse(bool IsComplete, IReadOnlyList<string> MissingApplicantFields, IReadOnlyList<string> MissingBusinessFields)
{
    public static CompletenessResponse From(CompletenessResult result) =>
        new(result.IsComplete, result.MissingApplicantFields, result.MissingBusinessFields);
}
