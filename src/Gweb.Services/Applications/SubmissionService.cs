using Gweb.Domain.Applications;
using Gweb.Domain.Documents;
using Gweb.Domain.Evaluation;
using Gweb.Domain.Submission;
using Gweb.Shared.Clock;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;

namespace Gweb.Services.Applications;

// This file's own namespace (Gweb.Services.Applications) has siblings under the same
// parent (Gweb.Services.Documents, Gweb.Services.Evaluation) that shadow the bare
// "Document"/"Evaluation" identifiers the same way Gweb.Tests.Adapters.Evaluation did
// during Phase 8's build (see AI-USAGE.md §5) -- namespace-member lookup in an
// enclosing scope beats a using-imported type, alias or not. Every reference to
// either type below is fully qualified rather than risking the same class of bug.
using DomainDocument = Gweb.Domain.Documents.Document;
using DomainEvaluation = Gweb.Domain.Evaluation.Evaluation;

/// <summary>Everything a downstream processor (or a review screen) would need,
/// gathered in one place -- the "normalized internal review payload" the brief asks
/// submission to produce. Built here, formatted into an HTTP response at the API
/// layer, same division of responsibility as every other service in this codebase.</summary>
public sealed record SubmissionResult(
    Application Application,
    Applicant? Applicant,
    Business? Business,
    IReadOnlyList<DomainDocument> Documents,
    McClassification? Classification,
    DomainEvaluation? Evaluation);

/// <summary>
/// Orchestrates the submission gate: "Submission blocks on missing required items;
/// produces normalized review payload when complete" (brief acceptance criterion).
/// Reuses SubmissionChecker (pure domain logic) so what blocks submission can never
/// drift from what a review screen would show as missing.
/// </summary>
public sealed class SubmissionService(
    IApplicationRepository applications,
    IApplicantRepository applicants,
    IBusinessRepository businesses,
    IDocumentRepository documents,
    IMcClassificationRepository classifications,
    IEvaluationRepository evaluations,
    IClock clock)
{
    public async Task<SubmissionResult> SubmitAsync(Guid applicationId, DeadlineBudget budget, CancellationToken cancellationToken = default)
    {
        var application = await applications.GetByIdAsync(applicationId, budget, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException($"Application {applicationId} not found.");
        var applicant = await applicants.GetByApplicationIdAsync(applicationId, budget, cancellationToken).ConfigureAwait(false);
        var business = await businesses.GetByApplicationIdAsync(applicationId, budget, cancellationToken).ConfigureAwait(false);
        var documentList = await documents.ListByApplicationIdAsync(applicationId, budget, cancellationToken).ConfigureAwait(false);

        var readiness = SubmissionChecker.Check(applicant, business, documentList);
        if (!readiness.IsReady)
        {
            throw new ValidationException("Application is not ready for submission.", readiness);
        }

        var now = DateTimeOffset.FromUnixTimeMilliseconds(clock.NowMs());
        var expectedVersion = application.Version;
        application.Submit(now);
        await applications.UpdateAsync(application, expectedVersion, budget, cancellationToken).ConfigureAwait(false);

        var classification = await classifications.GetByApplicationIdAsync(applicationId, budget, cancellationToken).ConfigureAwait(false);
        var evaluation = await evaluations.GetByApplicationIdAsync(applicationId, budget, cancellationToken).ConfigureAwait(false);

        return new SubmissionResult(application, applicant, business, documentList, classification, evaluation);
    }
}
