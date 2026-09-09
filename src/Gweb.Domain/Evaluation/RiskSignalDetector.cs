using Gweb.Domain.Applications;

namespace Gweb.Domain.Evaluation;

/// <summary>
/// Deterministic detection for every signal category the brief names ("contradictions,
/// missing evidence, unusually high ticket, description/MCC mismatch, incomplete
/// ownership data, regulated-license claims without evidence"). Detection is domain
/// logic, not an AI judgment call -- the same reasoning as separating calculated
/// arithmetic from AI commentary (EffectiveRateCalculator): a risk flag a reviewer
/// relies on should be reproducible and explainable from the data alone, not a model's
/// opinion that could vary run to run. Every signal cites its source, per brief "no
/// unexplained flags."
/// </summary>
public static class RiskSignalDetector
{
    private const decimal HighTicketMultiplier = 10m;
    private const decimal VolumeContradictionThreshold = 0.5m; // 50% relative difference

    private static readonly string[] RegulatedActivityKeywords =
    [
        "cannabis", "marijuana", "firearms", "ammunition", "gambling", "casino",
        "lending", "payday", "insurance", "cryptocurrency", "crypto", "securities",
        "pharmacy", "pharmaceutical",
    ];

    public static IReadOnlyList<RiskSignal> Detect(
        Business business, McClassification? classification, StatementExtraction? extraction, Guid? statementDocumentId)
    {
        var signals = new List<RiskSignal>();

        if (statementDocumentId is null)
        {
            signals.Add(new RiskSignal(
                "MISSING_PROCESSING_STATEMENT",
                "No processing statement was supplied for this evaluation -- rate analysis could not be calculated.",
                "processingStatementDocumentId"));
        }

        if (business.VolumeProfile is { AverageTicket: > 0 } volume && volume.HighestTicket > volume.AverageTicket * HighTicketMultiplier)
        {
            signals.Add(new RiskSignal(
                "UNUSUALLY_HIGH_TICKET",
                $"Highest ticket ({volume.HighestTicket:F2}) is more than {HighTicketMultiplier:F0}x the average ticket ({volume.AverageTicket:F2}).",
                "business.volumeProfile.highestTicket"));
        }

        if (classification?.HasMismatch == true)
        {
            signals.Add(new RiskSignal(
                "MCC_SELECTION_MISMATCH",
                $"Applicant-selected MCC ({classification.SelfSelectedMccCode}) does not match the system-proposed MCC ({classification.ProposedMccCode}).",
                "mcClassification.selfSelectedMccCode"));
        }

        if (business.BeneficialOwners.Count == 0)
        {
            signals.Add(new RiskSignal(
                "INCOMPLETE_OWNERSHIP_DATA",
                "No beneficial ownership information has been captured for this business.",
                "business.beneficialOwners"));
        }

        if (business.BusinessDescription is not null)
        {
            var matchedKeyword = RegulatedActivityKeywords.FirstOrDefault(
                keyword => business.BusinessDescription.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            if (matchedKeyword is not null)
            {
                signals.Add(new RiskSignal(
                    "REGULATED_ACTIVITY_MENTIONED",
                    $"Business description mentions '{matchedKeyword}' -- confirm supporting licensing/regulatory documentation has been collected.",
                    "business.businessDescription"));
            }
        }

        if (extraction?.MonthlyVolume is { } statementVolume && business.VolumeProfile is not null)
        {
            var declaredMonthlyVolume = business.VolumeProfile.ExpectedAnnualCardVolume / 12m;
            if (declaredMonthlyVolume > 0 && RelativeDifference(statementVolume, declaredMonthlyVolume) > VolumeContradictionThreshold)
            {
                signals.Add(new RiskSignal(
                    "STATEMENT_VOLUME_CONTRADICTION",
                    $"Statement-reported monthly volume ({statementVolume:F2}) differs by more than {VolumeContradictionThreshold:P0} " +
                    $"from the declared expected volume ({declaredMonthlyVolume:F2}).",
                    "statementExtraction.monthlyVolume",
                    statementDocumentId));
            }
        }

        return signals;
    }

    private static decimal RelativeDifference(decimal a, decimal b) => Math.Abs(a - b) / Math.Max(a, b);
}
