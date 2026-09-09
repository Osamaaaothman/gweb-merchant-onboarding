namespace Gweb.Domain.Applications;

public sealed record CompletenessResult(IReadOnlyList<string> MissingApplicantFields, IReadOnlyList<string> MissingBusinessFields)
{
    public bool IsComplete => MissingApplicantFields.Count == 0 && MissingBusinessFields.Count == 0;
}
