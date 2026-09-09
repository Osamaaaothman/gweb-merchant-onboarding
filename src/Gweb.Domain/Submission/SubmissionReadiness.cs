using Gweb.Domain.Documents;

namespace Gweb.Domain.Submission;

/// <summary>Per brief "Submission blocks on missing required items" -- every category
/// of missing item a reviewer (or the submit endpoint) needs to see, in one place.</summary>
public sealed record SubmissionReadiness(
    IReadOnlyList<string> MissingApplicantFields,
    IReadOnlyList<string> MissingBusinessFields,
    IReadOnlyList<DocumentType> MissingRequiredDocuments)
{
    public bool IsReady => MissingApplicantFields.Count == 0 && MissingBusinessFields.Count == 0 && MissingRequiredDocuments.Count == 0;
}
