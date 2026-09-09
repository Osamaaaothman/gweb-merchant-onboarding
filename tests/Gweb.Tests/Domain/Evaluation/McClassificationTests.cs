using Gweb.Domain.Evaluation;
using Gweb.Shared.Errors;

namespace Gweb.Tests.Domain.Evaluation;

public class McClassificationTests
{
    private static McClassification NewClassification() =>
        McClassification.CreateEmpty(Guid.NewGuid(), DateTimeOffset.UtcNow, "corr-1");

    [Fact]
    public void RecordProposalSetsTheTopCandidateAsProposedMccCode()
    {
        var classification = NewClassification();
        var candidates = new List<McClassificationCandidate>
        {
            new("5411", 0.9m, "Best match"),
            new("5499", 0.5m, "Second best"),
        };

        classification.RecordProposal(candidates, "gemini", DateTimeOffset.UtcNow);

        Assert.Equal("5411", classification.ProposedMccCode);
        Assert.Equal("gemini", classification.ProposedProvider);
        Assert.NotNull(classification.ClassifiedAt);
        Assert.Equal(candidates, classification.Candidates);
    }

    [Fact]
    public void RecordProposalThrowsOnAnEmptyCandidateList()
    {
        var classification = NewClassification();

        Assert.Throws<ValidationException>(() =>
            classification.RecordProposal([], "mock", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ConfirmSelfSelectedSetsTheApplicantsPick()
    {
        var classification = NewClassification();

        classification.ConfirmSelfSelected("5411", DateTimeOffset.UtcNow);

        Assert.Equal("5411", classification.SelfSelectedMccCode);
        Assert.NotNull(classification.SelfSelectedAt);
    }

    [Fact]
    public void HasMismatchIsFalseWhenOnlyOneSideExists()
    {
        var proposedOnly = NewClassification();
        proposedOnly.RecordProposal([new McClassificationCandidate("5411", 0.9m, "x")], "mock", DateTimeOffset.UtcNow);
        Assert.False(proposedOnly.HasMismatch);

        var selfSelectedOnly = NewClassification();
        selfSelectedOnly.ConfirmSelfSelected("5411", DateTimeOffset.UtcNow);
        Assert.False(selfSelectedOnly.HasMismatch);
    }

    [Fact]
    public void HasMismatchIsTrueWhenBothSidesDisagree()
    {
        var classification = NewClassification();
        classification.RecordProposal([new McClassificationCandidate("5411", 0.9m, "x")], "mock", DateTimeOffset.UtcNow);
        classification.ConfirmSelfSelected("5499", DateTimeOffset.UtcNow);

        Assert.True(classification.HasMismatch);
    }

    [Fact]
    public void HasMismatchIsFalseWhenBothSidesAgree()
    {
        var classification = NewClassification();
        classification.RecordProposal([new McClassificationCandidate("5411", 0.9m, "x")], "mock", DateTimeOffset.UtcNow);
        classification.ConfirmSelfSelected("5411", DateTimeOffset.UtcNow);

        Assert.False(classification.HasMismatch);
    }

    [Fact]
    public void EachMutationIncrementsVersion()
    {
        var classification = NewClassification();
        Assert.Equal(0, classification.Version);

        classification.RecordProposal([new McClassificationCandidate("5411", 0.9m, "x")], "mock", DateTimeOffset.UtcNow);
        Assert.Equal(1, classification.Version);

        classification.ConfirmSelfSelected("5411", DateTimeOffset.UtcNow);
        Assert.Equal(2, classification.Version);
    }

    [Fact]
    public void SnapshotProducesAnIndependentCopy()
    {
        var classification = NewClassification();
        classification.RecordProposal([new McClassificationCandidate("5411", 0.9m, "x")], "mock", DateTimeOffset.UtcNow);

        var snapshot = classification.Snapshot();
        classification.ConfirmSelfSelected("5499", DateTimeOffset.UtcNow);

        Assert.Null(snapshot.SelfSelectedMccCode);
        Assert.Equal(1, snapshot.Version);
    }
}
