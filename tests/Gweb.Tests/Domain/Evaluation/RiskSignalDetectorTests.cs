using Gweb.Domain.Applications;
using Gweb.Domain.Evaluation;

namespace Gweb.Tests.Domain.Evaluation;

public class RiskSignalDetectorTests
{
    private static Business NewBusiness(string? description = null, VolumeProfile? volumeProfile = null, IReadOnlyList<BeneficialOwner>? owners = null)
    {
        var business = Business.CreateEmpty(Guid.NewGuid(), DateTimeOffset.UtcNow, "actor", "corr-1");
        business.ApplyUpdate(new BusinessUpdate(
            LegalBusinessName: "Test Co",
            BusinessDescription: description,
            VolumeProfile: volumeProfile,
            BeneficialOwners: owners), DateTimeOffset.UtcNow);
        return business;
    }

    private static VolumeProfile NormalVolume() => new(600_000m, 100m, 500m, 1_000, 60m, 40m);

    [Fact]
    public void FlagsMissingProcessingStatementWhenNoDocumentIdWasSupplied()
    {
        var signals = RiskSignalDetector.Detect(NewBusiness(), classification: null, extraction: null, statementDocumentId: null);

        Assert.Contains(signals, s => s.Code == "MISSING_PROCESSING_STATEMENT" && s.SourceField == "processingStatementDocumentId");
    }

    [Fact]
    public void DoesNotFlagMissingStatementWhenADocumentIdWasSupplied()
    {
        var signals = RiskSignalDetector.Detect(NewBusiness(), classification: null, extraction: null, statementDocumentId: Guid.NewGuid());

        Assert.DoesNotContain(signals, s => s.Code == "MISSING_PROCESSING_STATEMENT");
    }

    [Fact]
    public void FlagsAnUnusuallyHighTicketRelativeToAverage()
    {
        var volume = new VolumeProfile(600_000m, 50m, 5_000m, 1_000, 60m, 40m); // 100x average
        var signals = RiskSignalDetector.Detect(NewBusiness(volumeProfile: volume), null, null, Guid.NewGuid());

        var signal = Assert.Single(signals, s => s.Code == "UNUSUALLY_HIGH_TICKET");
        Assert.Equal("business.volumeProfile.highestTicket", signal.SourceField);
    }

    [Fact]
    public void DoesNotFlagAReasonableTicketSpread()
    {
        var signals = RiskSignalDetector.Detect(NewBusiness(volumeProfile: NormalVolume()), null, null, Guid.NewGuid());

        Assert.DoesNotContain(signals, s => s.Code == "UNUSUALLY_HIGH_TICKET");
    }

    [Fact]
    public void FlagsAnMccSelectionMismatch()
    {
        var classification = McClassification.CreateEmpty(Guid.NewGuid(), DateTimeOffset.UtcNow, "corr-1");
        classification.RecordProposal([new McClassificationCandidate("5411", 0.9m, "x")], "mock", DateTimeOffset.UtcNow);
        classification.ConfirmSelfSelected("5999", DateTimeOffset.UtcNow);

        var signals = RiskSignalDetector.Detect(NewBusiness(), classification, null, Guid.NewGuid());

        var signal = Assert.Single(signals, s => s.Code == "MCC_SELECTION_MISMATCH");
        Assert.Equal("mcClassification.selfSelectedMccCode", signal.SourceField);
    }

    [Fact]
    public void DoesNotFlagWhenSelfSelectedMatchesProposed()
    {
        var classification = McClassification.CreateEmpty(Guid.NewGuid(), DateTimeOffset.UtcNow, "corr-1");
        classification.RecordProposal([new McClassificationCandidate("5411", 0.9m, "x")], "mock", DateTimeOffset.UtcNow);
        classification.ConfirmSelfSelected("5411", DateTimeOffset.UtcNow);

        var signals = RiskSignalDetector.Detect(NewBusiness(), classification, null, Guid.NewGuid());

        Assert.DoesNotContain(signals, s => s.Code == "MCC_SELECTION_MISMATCH");
    }

    [Fact]
    public void FlagsIncompleteOwnershipDataWhenNoBeneficialOwnersAreCaptured()
    {
        var signals = RiskSignalDetector.Detect(NewBusiness(owners: []), null, null, Guid.NewGuid());

        Assert.Contains(signals, s => s.Code == "INCOMPLETE_OWNERSHIP_DATA" && s.SourceField == "business.beneficialOwners");
    }

    [Fact]
    public void DoesNotFlagOwnershipWhenAtLeastOneBeneficialOwnerIsCaptured()
    {
        var owners = new List<BeneficialOwner> { new("Jane Doe", "CEO", 60m) };
        var signals = RiskSignalDetector.Detect(NewBusiness(owners: owners), null, null, Guid.NewGuid());

        Assert.DoesNotContain(signals, s => s.Code == "INCOMPLETE_OWNERSHIP_DATA");
    }

    [Theory]
    [InlineData("We sell recreational cannabis products to licensed dispensaries.")]
    [InlineData("A payday lending service for short-term loans.")]
    public void FlagsRegulatedActivityKeywordsInTheBusinessDescription(string description)
    {
        var signals = RiskSignalDetector.Detect(NewBusiness(description), null, null, Guid.NewGuid());

        Assert.Contains(signals, s => s.Code == "REGULATED_ACTIVITY_MENTIONED" && s.SourceField == "business.businessDescription");
    }

    [Fact]
    public void DoesNotFlagAnOrdinaryBusinessDescription()
    {
        var signals = RiskSignalDetector.Detect(NewBusiness("We sell fresh produce and dairy at a neighborhood grocery store."), null, null, Guid.NewGuid());

        Assert.DoesNotContain(signals, s => s.Code == "REGULATED_ACTIVITY_MENTIONED");
    }

    [Fact]
    public void FlagsAStatementVolumeThatContradictsTheDeclaredExpectedVolume()
    {
        var volume = new VolumeProfile(ExpectedAnnualCardVolume: 1_200_000m, 100m, 500m, 1_000, 60m, 40m); // 100k/mo declared
        var extraction = new StatementExtraction("Acme", MonthlyVolume: 10_000m, 2.6m, 0.1m, 25m, 15m, "2026-08", null, "gemini"); // wildly lower
        var documentId = Guid.NewGuid();

        var signals = RiskSignalDetector.Detect(NewBusiness(volumeProfile: volume), null, extraction, documentId);

        var signal = Assert.Single(signals, s => s.Code == "STATEMENT_VOLUME_CONTRADICTION");
        Assert.Equal("statementExtraction.monthlyVolume", signal.SourceField);
        Assert.Equal(documentId, signal.SourceDocumentId);
    }

    [Fact]
    public void DoesNotFlagAStatementVolumeCloseToTheDeclaredExpectedVolume()
    {
        var volume = new VolumeProfile(ExpectedAnnualCardVolume: 600_000m, 100m, 500m, 1_000, 60m, 40m); // 50k/mo declared
        var extraction = new StatementExtraction("Acme", MonthlyVolume: 52_000m, 2.6m, 0.1m, 25m, 15m, "2026-08", null, "gemini");

        var signals = RiskSignalDetector.Detect(NewBusiness(volumeProfile: volume), null, extraction, Guid.NewGuid());

        Assert.DoesNotContain(signals, s => s.Code == "STATEMENT_VOLUME_CONTRADICTION");
    }

    [Fact]
    public void EveryReturnedSignalHasANonEmptySourceField()
    {
        var volume = new VolumeProfile(1_200_000m, 50m, 5_000m, 1_000, 60m, 40m);
        var classification = McClassification.CreateEmpty(Guid.NewGuid(), DateTimeOffset.UtcNow, "corr-1");
        classification.RecordProposal([new McClassificationCandidate("5411", 0.9m, "x")], "mock", DateTimeOffset.UtcNow);
        classification.ConfirmSelfSelected("7995", DateTimeOffset.UtcNow);
        var extraction = new StatementExtraction("Acme", 10_000m, 2.6m, 0.1m, 25m, 15m, "2026-08", null, "gemini");

        var signals = RiskSignalDetector.Detect(
            NewBusiness("cannabis dispensary", volume, []), classification, extraction, Guid.NewGuid());

        Assert.NotEmpty(signals);
        Assert.All(signals, s => Assert.False(string.IsNullOrWhiteSpace(s.SourceField)));
    }
}
