using Gweb.Domain.Applications;
using Gweb.Domain.Documents;

namespace Gweb.Domain.Submission;

/// <summary>
/// What "ready to submit" means, per brief §4's document table plus §3.1/§3.2's
/// required fields (the latter via CompletenessChecker, reused here rather than
/// re-derived -- see that type's own doc comment for why that matters). Pure
/// function, no I/O.
/// </summary>
public static class SubmissionChecker
{
    /// <summary>
    /// Per brief's document table: Government ID, Business registration, and Bank
    /// evidence are "Required"; Business license and Additional evidence are
    /// "Conditional" (depend on jurisdiction/business type, which this system has no
    /// rule engine for yet -- not blocked on, documented as a known gap); Processing
    /// statement is explicitly "Optional."
    /// </summary>
    private static readonly DocumentType[] RequiredDocumentTypes =
    [
        DocumentType.GovernmentId,
        DocumentType.BusinessRegistration,
        DocumentType.BankEvidence,
    ];

    public static SubmissionReadiness Check(Applicant? applicant, Business? business, IReadOnlyList<Document> documents)
    {
        var completeness = CompletenessChecker.Check(applicant, business);

        // A document type counts as "provided" only once it has actually landed and
        // been verified (Received) or reviewed in (Accepted) -- Uploading/Requested/
        // Rejected don't count, the same bar Document's own state machine uses to mean
        // "usable."
        var providedTypes = documents
            .Where(d => d.Status is DocumentStatus.Received or DocumentStatus.Accepted)
            .Select(d => d.Type)
            .ToHashSet();
        var missingDocuments = RequiredDocumentTypes.Where(t => !providedTypes.Contains(t)).ToList();

        return new SubmissionReadiness(completeness.MissingApplicantFields, completeness.MissingBusinessFields, missingDocuments);
    }
}
