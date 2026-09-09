namespace Gweb.Domain.Evaluation;

/// <summary>Purely computed -- see EffectiveRateCalculator. Never touched by an AI
/// response; the "calculated" side of brief "response cleanly separates extracted /
/// calculated / commentary."</summary>
public sealed record EffectiveRateResult(
    decimal TotalMonthlyCostAmount,
    decimal EffectiveRatePercent,
    decimal DiscountFeeAmount,
    decimal TransactionFeeAmount,
    decimal MonthlyFeeAmount,
    decimal ChargebackFeeAmount);
