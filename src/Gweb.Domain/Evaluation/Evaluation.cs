namespace Gweb.Domain.Evaluation;

/// <summary>
/// Own aggregate (pk=APP#{applicationId}, sk=EVALUATION) -- same reasoning as
/// McClassification: a system-driven write pattern distinct from the user-PATCH-driven
/// Business/Applicant entities, kept separate rather than bolted onto Business.
/// </summary>
public sealed class Evaluation
{
    public Guid ApplicationId { get; }
    public EvaluationStatus Status { get; private set; }
    public StatementExtraction? Extraction { get; private set; }
    public EffectiveRateResult? Calculated { get; private set; }
    public IReadOnlyList<RiskSignal> RiskSignals { get; private set; } = [];
    public Guid? ProcessingStatementDocumentId { get; private set; }
    public DateTimeOffset? EvaluatedAt { get; private set; }

    public long Version { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public string CorrelationId { get; private set; }

    private Evaluation(Guid applicationId, DateTimeOffset now, string correlationId)
    {
        ApplicationId = applicationId;
        Status = EvaluationStatus.Processing;
        CreatedAt = now;
        UpdatedAt = now;
        CorrelationId = correlationId;
        Version = 0;
    }

    public static Evaluation CreateEmpty(Guid applicationId, DateTimeOffset now, string correlationId) =>
        new(applicationId, now, correlationId);

    public static Evaluation Rehydrate(
        Guid applicationId,
        EvaluationStatus status,
        StatementExtraction? extraction,
        EffectiveRateResult? calculated,
        IReadOnlyList<RiskSignal> riskSignals,
        Guid? processingStatementDocumentId,
        DateTimeOffset? evaluatedAt,
        long version,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        string correlationId)
    {
        var evaluation = new Evaluation(applicationId, createdAt, correlationId)
        {
            Status = status,
            Extraction = extraction,
            Calculated = calculated,
            RiskSignals = riskSignals,
            ProcessingStatementDocumentId = processingStatementDocumentId,
            EvaluatedAt = evaluatedAt,
            Version = version,
            UpdatedAt = updatedAt,
        };
        return evaluation;
    }

    /// <summary>An independent copy -- see Applicant.Snapshot() for why an in-memory
    /// repository needs this.</summary>
    public Evaluation Snapshot() => Rehydrate(
        ApplicationId, Status, Extraction, Calculated, RiskSignals, ProcessingStatementDocumentId,
        EvaluatedAt, Version, CreatedAt, UpdatedAt, CorrelationId);

    /// <summary>
    /// Brief "async fallback (202 + PROCESSING + poll) if the budget cannot be met" --
    /// called when EvaluationService determines up front there isn't enough deadline
    /// budget left to safely attempt evaluation at all. Idempotent no-op if already
    /// Processing (a repeat call under the same low-budget condition shouldn't bump
    /// the version pointlessly).
    /// </summary>
    public void MarkProcessing(DateTimeOffset now)
    {
        if (Status == EvaluationStatus.Processing)
        {
            return;
        }
        Status = EvaluationStatus.Processing;
        Touch(now);
    }

    public void Complete(
        StatementExtraction? extraction,
        EffectiveRateResult? calculated,
        IReadOnlyList<RiskSignal> riskSignals,
        Guid? processingStatementDocumentId,
        DateTimeOffset now)
    {
        Status = EvaluationStatus.Completed;
        Extraction = extraction;
        Calculated = calculated;
        RiskSignals = riskSignals;
        ProcessingStatementDocumentId = processingStatementDocumentId;
        EvaluatedAt = now;
        Touch(now);
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version += 1;
    }
}
