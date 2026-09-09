using Gweb.Domain.Evaluation;

namespace Gweb.Tests.Domain.Evaluation;

public class EffectiveRateCalculatorTests
{
    private static StatementExtraction FullExtraction(decimal monthlyVolume = 50_000m, decimal discountRatePercent = 2.6m) =>
        new(
            Processor: "Acme Processing",
            MonthlyVolume: monthlyVolume,
            DiscountRatePercent: discountRatePercent,
            PerTransactionFee: 0.10m,
            MonthlyFee: 25m,
            ChargebackFeeTotal: 15m,
            StatementPeriod: "2026-08",
            Commentary: "test fixture",
            Provider: "test");

    [Fact]
    public void ReturnsNullWhenExtractionIsNull()
    {
        var result = EffectiveRateCalculator.Calculate(null, monthlyTransactionCount: 100);

        Assert.Null(result);
    }

    [Fact]
    public void ReturnsNullWhenMonthlyVolumeIsMissing()
    {
        var extraction = FullExtraction() with { MonthlyVolume = null };

        var result = EffectiveRateCalculator.Calculate(extraction, monthlyTransactionCount: 100);

        Assert.Null(result);
    }

    [Fact]
    public void ReturnsNullWhenDiscountRatePercentIsMissing()
    {
        var extraction = FullExtraction() with { DiscountRatePercent = null };

        var result = EffectiveRateCalculator.Calculate(extraction, monthlyTransactionCount: 100);

        Assert.Null(result);
    }

    [Fact]
    public void ZeroMonthlyVolumeIsACalculableCaseNotAMissingDataCase()
    {
        // A statement can genuinely report $0 volume for a slow month -- that's a
        // valid input (0% effective rate), not the same as "we don't know the volume."
        var extraction = FullExtraction(monthlyVolume: 0m) with { PerTransactionFee = 0m, MonthlyFee = 0m, ChargebackFeeTotal = 0m };

        var result = EffectiveRateCalculator.Calculate(extraction, monthlyTransactionCount: 0);

        Assert.NotNull(result);
        Assert.Equal(0m, result!.EffectiveRatePercent);
        Assert.Equal(0m, result.TotalMonthlyCostAmount);
    }

    [Fact]
    public void TreatsMissingOptionalFeesAsZeroRatherThanFailing()
    {
        var extraction = FullExtraction() with { PerTransactionFee = null, MonthlyFee = null, ChargebackFeeTotal = null };

        var result = EffectiveRateCalculator.Calculate(extraction, monthlyTransactionCount: 100);

        Assert.NotNull(result);
        // Only the discount fee (volume * rate) contributes -- 50,000 * 2.6% = 1,300.
        Assert.Equal(1_300m, result!.TotalMonthlyCostAmount);
        Assert.Equal(0m, result.TransactionFeeAmount);
        Assert.Equal(0m, result.MonthlyFeeAmount);
        Assert.Equal(0m, result.ChargebackFeeAmount);
    }

    [Fact]
    public void ComputesTheFullCostBreakdownAndEffectiveRateForARealisticStatement()
    {
        var extraction = FullExtraction(monthlyVolume: 50_000m, discountRatePercent: 2.6m);

        var result = EffectiveRateCalculator.Calculate(extraction, monthlyTransactionCount: 1_000);

        // discount: 50,000 * 2.6% = 1,300
        // transaction: 0.10 * 1,000 = 100
        // monthly: 25, chargeback: 15
        // total = 1,440; effective rate = 1,440 / 50,000 * 100 = 2.88%
        Assert.Equal(1_300m, result!.DiscountFeeAmount);
        Assert.Equal(100m, result.TransactionFeeAmount);
        Assert.Equal(25m, result.MonthlyFeeAmount);
        Assert.Equal(15m, result.ChargebackFeeAmount);
        Assert.Equal(1_440m, result.TotalMonthlyCostAmount);
        Assert.Equal(2.88m, result.EffectiveRatePercent);
    }
}
