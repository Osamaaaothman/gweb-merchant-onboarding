namespace Gweb.Domain.Evaluation;

/// <summary>
/// Deterministic effective-processing-rate arithmetic. Per brief "Deterministic
/// arithmetic must be separated from AI commentary" -- this type never reads
/// <see cref="StatementExtraction.Commentary"/> or <see cref="StatementExtraction.Provider"/>,
/// only the five numeric fields, so an AI response can never influence the math
/// through anything other than the numbers it (fallibly) extracted -- and even those
/// are validated by the caller (EvaluationService) before reaching here, the same
/// trust boundary ClassificationService applies to MCC candidates.
/// </summary>
public static class EffectiveRateCalculator
{
    /// <summary>
    /// Null if <paramref name="extraction"/> is null, or is missing either
    /// MonthlyVolume or DiscountRatePercent -- the two fields with no sane zero/default
    /// to substitute (a missing discount rate is not the same as a 0% discount rate).
    /// A present-but-zero MonthlyVolume is a valid, calculable input (0% effective rate,
    /// not a missing-data case) -- see EffectiveRateCalculatorTests for both.
    /// </summary>
    public static EffectiveRateResult? Calculate(StatementExtraction? extraction, int monthlyTransactionCount)
    {
        if (extraction is null || extraction.MonthlyVolume is null || extraction.DiscountRatePercent is null)
        {
            return null;
        }

        var monthlyVolume = extraction.MonthlyVolume.Value;
        var discountFee = monthlyVolume * (extraction.DiscountRatePercent.Value / 100m);
        var transactionFee = (extraction.PerTransactionFee ?? 0m) * monthlyTransactionCount;
        var monthlyFee = extraction.MonthlyFee ?? 0m;
        var chargebackFee = extraction.ChargebackFeeTotal ?? 0m;

        var totalCost = discountFee + transactionFee + monthlyFee + chargebackFee;
        var effectiveRatePercent = monthlyVolume == 0m ? 0m : totalCost / monthlyVolume * 100m;

        return new EffectiveRateResult(totalCost, effectiveRatePercent, discountFee, transactionFee, monthlyFee, chargebackFee);
    }
}
