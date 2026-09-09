namespace Gweb.Domain.Applications;

public sealed record VolumeProfile(
    decimal ExpectedAnnualCardVolume,
    decimal AverageTicket,
    decimal HighestTicket,
    int MonthlyTransactionCount,
    decimal CardPresentPercentage,
    decimal EcommercePercentage);
