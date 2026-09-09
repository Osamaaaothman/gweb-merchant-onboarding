using Gweb.Domain.Applications;
using Gweb.Domain.Documents;
using Gweb.Domain.Evaluation;
using Gweb.Shared.Clock;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;
using Gweb.Shared.Logging;

namespace Gweb.Services.Evaluation;

// This file's own namespace (Gweb.Services.Evaluation) shares its last segment with
// Gweb.Domain.Evaluation, so the bare identifier "Evaluation" is ambiguous between the
// namespace and the domain type -- an explicit alias resolves it without qualifying
// every reference.
using Evaluation = Gweb.Domain.Evaluation.Evaluation;

/// <summary>
/// Orchestrates rate evaluation: statement extraction (if a processing statement was
/// supplied), deterministic rate math, and deterministic risk-signal detection.
/// Mirrors ClassificationService's fallback-to-mock policy for the one AI call
/// involved (statement extraction) -- same reasoning, see that type's doc comment.
/// </summary>
public sealed class EvaluationService(
    IEvaluationRepository repository,
    IBusinessRepository businessRepository,
    IDocumentRepository documentRepository,
    IDocumentStorage documentStorage,
    IMcClassificationRepository classificationRepository,
    IEvaluationProvider primaryProvider,
    IEvaluationProvider fallbackProvider,
    IClock clock,
    StructuredLogger logger)
{
    /// <summary>
    /// Per brief "async fallback (202 + PROCESSING + poll) if the budget cannot be
    /// met" -- if there isn't even this much deadline budget left when EvaluateAsync
    /// is called, evaluation is not attempted at all; the record is marked Processing
    /// and returned immediately. Deliberately small: it only needs to cover the cheap
    /// DynamoDB lookups this method does before any AI call, since the one AI call
    /// (statement extraction) derives its own, separately-capped timeout from whatever
    /// budget remains via GeminiCallExecutor and fails safely into the mock fallback
    /// on its own if it can't fit.
    /// </summary>
    private const long MinimumBudgetReserveMs = 5_000;

    public async Task<Evaluation> EvaluateAsync(
        Guid applicationId,
        string correlationId,
        Guid? processingStatementDocumentId,
        DeadlineBudget budget,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.FromUnixTimeMilliseconds(clock.NowMs());
        var existing = await repository.GetByApplicationIdAsync(applicationId, budget, cancellationToken).ConfigureAwait(false);
        var expectedVersion = existing?.Version ?? 0;
        var evaluation = existing ?? Evaluation.CreateEmpty(applicationId, now, correlationId);

        if (!budget.CanAttempt(MinimumBudgetReserveMs))
        {
            evaluation.MarkProcessing(now);
            await repository.SaveAsync(evaluation, expectedVersion, budget, cancellationToken).ConfigureAwait(false);
            return evaluation;
        }

        var business = await businessRepository.GetByApplicationIdAsync(applicationId, budget, cancellationToken).ConfigureAwait(false)
            ?? throw new ValidationException("Business must be filled in before evaluation.");

        var extraction = processingStatementDocumentId is null
            ? null
            : await ExtractFromStatementAsync(applicationId, processingStatementDocumentId.Value, budget, cancellationToken).ConfigureAwait(false);

        var classification = await classificationRepository.GetByApplicationIdAsync(applicationId, budget, cancellationToken).ConfigureAwait(false);
        var monthlyTransactionCount = business.VolumeProfile?.MonthlyTransactionCount ?? 0;
        var calculated = EffectiveRateCalculator.Calculate(extraction, monthlyTransactionCount);
        var riskSignals = RiskSignalDetector.Detect(business, classification, extraction, processingStatementDocumentId);

        evaluation.Complete(extraction, calculated, riskSignals, processingStatementDocumentId, now);
        await repository.SaveAsync(evaluation, expectedVersion, budget, cancellationToken).ConfigureAwait(false);

        return evaluation;
    }

    public Task<Evaluation?> GetAsync(Guid applicationId, DeadlineBudget budget, CancellationToken cancellationToken = default) =>
        repository.GetByApplicationIdAsync(applicationId, budget, cancellationToken);

    private async Task<StatementExtraction?> ExtractFromStatementAsync(
        Guid applicationId, Guid documentId, DeadlineBudget budget, CancellationToken cancellationToken)
    {
        var document = await documentRepository.GetByIdAsync(applicationId, documentId, budget, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException($"Document {documentId} not found.");
        if (document.Type != DocumentType.ProcessingStatement)
        {
            throw new ValidationException($"Document {documentId} is not a ProcessingStatement document.");
        }
        if (document.Status is not (DocumentStatus.Received or DocumentStatus.Accepted))
        {
            throw new ValidationException($"Document {documentId} has not completed upload (status: {document.Status}).");
        }

        var bytes = await documentStorage.DownloadObjectAsync(document.S3Key, budget, cancellationToken).ConfigureAwait(false);
        if (bytes is null)
        {
            // Metadata says the upload completed, but the object isn't actually
            // retrievable -- treat as "no statement data available" rather than a
            // hard failure; the risk-signal detector already flags a missing
            // statement the same way it would if none had been supplied at all.
            return null;
        }

        return await GetExtractionWithFallbackAsync(bytes, document.ContentType, budget, cancellationToken).ConfigureAwait(false);
    }

    private async Task<StatementExtraction> GetExtractionWithFallbackAsync(
        byte[] documentBytes, string contentType, DeadlineBudget budget, CancellationToken cancellationToken)
    {
        if (ReferenceEquals(primaryProvider, fallbackProvider))
        {
            // AI_PROVIDER=mock -- primary already is the guaranteed-available
            // fallback, same reasoning as ClassificationService.
            return await primaryProvider.ExtractStatementAsync(documentBytes, contentType, budget, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            return await primaryProvider.ExtractStatementAsync(documentBytes, contentType, budget, cancellationToken).ConfigureAwait(false);
        }
        catch (DomainException ex)
        {
            logger.Warn("evaluation_provider_fallback", new { reason = ex.Code, message = ex.Message, capability = "extract_statement" });
            return await fallbackProvider.ExtractStatementAsync(documentBytes, contentType, budget, cancellationToken).ConfigureAwait(false);
        }
    }
}
