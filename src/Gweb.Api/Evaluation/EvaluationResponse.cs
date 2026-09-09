using Gweb.Domain.Evaluation;

namespace Gweb.Api.Evaluation;

public sealed record RiskSignalResponse(string Code, string Message, string SourceField, Guid? SourceDocumentId)
{
    public static RiskSignalResponse From(RiskSignal signal) => new(signal.Code, signal.Message, signal.SourceField, signal.SourceDocumentId);
}

/// <summary>Deliberately excludes Commentary -- see EvaluationResponse, which lifts it
/// to a top-level sibling field so the response literally separates extracted /
/// calculated / commentary, per brief wording.</summary>
public sealed record StatementExtractionResponse(
    string? Processor,
    decimal? MonthlyVolume,
    decimal? DiscountRatePercent,
    decimal? PerTransactionFee,
    decimal? MonthlyFee,
    decimal? ChargebackFeeTotal,
    string? StatementPeriod,
    string Provider)
{
    public static StatementExtractionResponse From(StatementExtraction extraction) => new(
        extraction.Processor, extraction.MonthlyVolume, extraction.DiscountRatePercent, extraction.PerTransactionFee,
        extraction.MonthlyFee, extraction.ChargebackFeeTotal, extraction.StatementPeriod, extraction.Provider);
}

public sealed record EffectiveRateResponse(
    decimal TotalMonthlyCostAmount,
    decimal EffectiveRatePercent,
    decimal DiscountFeeAmount,
    decimal TransactionFeeAmount,
    decimal MonthlyFeeAmount,
    decimal ChargebackFeeAmount)
{
    public static EffectiveRateResponse From(EffectiveRateResult result) => new(
        result.TotalMonthlyCostAmount, result.EffectiveRatePercent, result.DiscountFeeAmount,
        result.TransactionFeeAmount, result.MonthlyFeeAmount, result.ChargebackFeeAmount);
}

public sealed record EvaluationResponse(
    string Status,
    StatementExtractionResponse? Extracted,
    EffectiveRateResponse? Calculated,
    string? Commentary,
    IReadOnlyList<RiskSignalResponse> RiskSignals,
    Guid? ProcessingStatementDocumentId,
    DateTimeOffset? EvaluatedAt,
    long Version)
{
    public static EvaluationResponse From(global::Gweb.Domain.Evaluation.Evaluation evaluation) => new(
        evaluation.Status.ToString(),
        evaluation.Extraction is null ? null : StatementExtractionResponse.From(evaluation.Extraction),
        evaluation.Calculated is null ? null : EffectiveRateResponse.From(evaluation.Calculated),
        evaluation.Extraction?.Commentary,
        [.. evaluation.RiskSignals.Select(RiskSignalResponse.From)],
        evaluation.ProcessingStatementDocumentId,
        evaluation.EvaluatedAt,
        evaluation.Version);
}
