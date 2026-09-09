using Gweb.Shared.Errors;

namespace Gweb.Domain.Evaluation;

/// <summary>
/// Persists both the system-proposed MCC (from IEvaluationProvider, via
/// RecordProposal) and the applicant's own confirmed/corrected pick (via
/// ConfirmSelfSelected), per brief "Persist both the applicant-selected activity and
/// the system-proposed MCC so reviewers can inspect mismatches." Deliberately its own
/// aggregate (not a field bag on Business) -- see docs/adr/0006-ai-evaluation-provider.md
/// for why: it has a different write pattern (system-driven vs. user-PATCH-driven) and
/// keeping it separate meant zero changes to the already-shipped, fully-tested
/// Business entity and its DynamoDB mapping.
/// </summary>
public sealed class McClassification
{
    public Guid ApplicationId { get; }
    public IReadOnlyList<McClassificationCandidate> Candidates { get; private set; } = [];
    public string? ProposedMccCode { get; private set; }
    public string? ProposedProvider { get; private set; }
    public DateTimeOffset? ClassifiedAt { get; private set; }
    public string? SelfSelectedMccCode { get; private set; }
    public DateTimeOffset? SelfSelectedAt { get; private set; }

    public long Version { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public string CorrelationId { get; private set; }

    /// <summary>True only once both a system proposal and an applicant selection
    /// exist and they disagree -- null before either side has weighed in, never a
    /// false positive from a half-filled record.</summary>
    public bool HasMismatch => ProposedMccCode is not null && SelfSelectedMccCode is not null && ProposedMccCode != SelfSelectedMccCode;

    private McClassification(Guid applicationId, DateTimeOffset now, string correlationId)
    {
        ApplicationId = applicationId;
        CreatedAt = now;
        UpdatedAt = now;
        CorrelationId = correlationId;
        Version = 0;
    }

    public static McClassification CreateEmpty(Guid applicationId, DateTimeOffset now, string correlationId) =>
        new(applicationId, now, correlationId);

    public static McClassification Rehydrate(
        Guid applicationId,
        IReadOnlyList<McClassificationCandidate> candidates,
        string? proposedMccCode,
        string? proposedProvider,
        DateTimeOffset? classifiedAt,
        string? selfSelectedMccCode,
        DateTimeOffset? selfSelectedAt,
        long version,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        string correlationId)
    {
        var classification = new McClassification(applicationId, createdAt, correlationId)
        {
            Candidates = candidates,
            ProposedMccCode = proposedMccCode,
            ProposedProvider = proposedProvider,
            ClassifiedAt = classifiedAt,
            SelfSelectedMccCode = selfSelectedMccCode,
            SelfSelectedAt = selfSelectedAt,
            Version = version,
            UpdatedAt = updatedAt,
        };
        return classification;
    }

    /// <summary>An independent copy -- see Applicant.Snapshot() for why an in-memory
    /// repository needs this.</summary>
    public McClassification Snapshot() => Rehydrate(
        ApplicationId, Candidates, ProposedMccCode, ProposedProvider, ClassifiedAt,
        SelfSelectedMccCode, SelfSelectedAt, Version, CreatedAt, UpdatedAt, CorrelationId);

    /// <summary>
    /// <paramref name="candidates"/> must be non-empty and already validated (real MCC
    /// codes, confidence in [0,1]) by the caller -- this method trusts its input, the
    /// same way Document.MarkReceived trusts a checksum ClassificationService already
    /// verified. The first candidate is taken as the top proposal; callers must sort
    /// by confidence descending before calling.
    /// </summary>
    public void RecordProposal(IReadOnlyList<McClassificationCandidate> candidates, string provider, DateTimeOffset now)
    {
        if (candidates.Count == 0)
        {
            throw new ValidationException("At least one classification candidate is required.");
        }
        Candidates = candidates;
        ProposedMccCode = candidates[0].MccCode;
        ProposedProvider = provider;
        ClassifiedAt = now;
        Touch(now);
    }

    /// <summary>The applicant confirming the system's proposal, or correcting it to a
    /// different code -- either way, this is what "self-selected" means here.
    /// <paramref name="mccCode"/> must already be validated against the catalog by the
    /// caller (ClassificationService), same trust boundary as RecordProposal.</summary>
    public void ConfirmSelfSelected(string mccCode, DateTimeOffset now)
    {
        SelfSelectedMccCode = mccCode;
        SelfSelectedAt = now;
        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version += 1;
    }
}
