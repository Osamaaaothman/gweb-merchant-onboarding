namespace Gweb.Domain.Evaluation;

/// <summary>
/// Normalized values pulled from a processing statement, per brief "Statement
/// extraction — normalize processor, monthly volume, discount/effective rate,
/// transaction fees, monthly fees, chargeback fees, statement period." Every numeric
/// field is nullable -- a real statement can be illegible or missing a line item, and
/// EffectiveRateCalculator must handle that as "missing data," not a crash (see its
/// own doc comment). <see cref="Commentary"/> is the one AI-authored prose field
/// anywhere in this type; it is never read by EffectiveRateCalculator -- see that
/// type for why the separation is structural, not just documented.
/// </summary>
public sealed record StatementExtraction(
    string? Processor,
    decimal? MonthlyVolume,
    decimal? DiscountRatePercent,
    decimal? PerTransactionFee,
    decimal? MonthlyFee,
    decimal? ChargebackFeeTotal,
    string? StatementPeriod,
    string? Commentary,
    string Provider);
