using Gweb.Domain.Evaluation;

namespace Gweb.Tests.Domain.Evaluation;

public class EvaluationTests
{
    private static global::Gweb.Domain.Evaluation.Evaluation NewEvaluation() =>
        global::Gweb.Domain.Evaluation.Evaluation.CreateEmpty(Guid.NewGuid(), DateTimeOffset.UtcNow, "corr-1");

    [Fact]
    public void StartsInProcessingStatusBeforeAnythingHappens()
    {
        var evaluation = NewEvaluation();

        Assert.Equal(EvaluationStatus.Processing, evaluation.Status);
        Assert.Equal(0, evaluation.Version);
    }

    [Fact]
    public void CompleteSetsStatusToCompletedAndStoresEveryField()
    {
        var evaluation = NewEvaluation();
        var extraction = new StatementExtraction("Acme", 50_000m, 2.6m, 0.1m, 25m, 15m, "2026-08", "note", "gemini");
        var calculated = new EffectiveRateResult(1_440m, 2.88m, 1_300m, 100m, 25m, 15m);
        var signals = new List<RiskSignal> { new("CODE", "message", "field") };
        var documentId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        evaluation.Complete(extraction, calculated, signals, documentId, now);

        Assert.Equal(EvaluationStatus.Completed, evaluation.Status);
        Assert.Equal(extraction, evaluation.Extraction);
        Assert.Equal(calculated, evaluation.Calculated);
        Assert.Equal(signals, evaluation.RiskSignals);
        Assert.Equal(documentId, evaluation.ProcessingStatementDocumentId);
        Assert.Equal(now, evaluation.EvaluatedAt);
        Assert.Equal(1, evaluation.Version);
    }

    [Fact]
    public void MarkProcessingIsANoOpWhenAlreadyProcessing()
    {
        var evaluation = NewEvaluation();

        evaluation.MarkProcessing(DateTimeOffset.UtcNow);

        Assert.Equal(0, evaluation.Version);
    }

    [Fact]
    public void MarkProcessingResetsFromCompletedAndBumpsVersion()
    {
        var evaluation = NewEvaluation();
        evaluation.Complete(null, null, [], null, DateTimeOffset.UtcNow);

        evaluation.MarkProcessing(DateTimeOffset.UtcNow);

        Assert.Equal(EvaluationStatus.Processing, evaluation.Status);
        Assert.Equal(2, evaluation.Version);
    }

    [Fact]
    public void SnapshotProducesAnIndependentCopy()
    {
        var evaluation = NewEvaluation();
        evaluation.Complete(null, null, [], null, DateTimeOffset.UtcNow);

        var snapshot = evaluation.Snapshot();
        evaluation.MarkProcessing(DateTimeOffset.UtcNow);

        Assert.Equal(EvaluationStatus.Completed, snapshot.Status);
        Assert.Equal(1, snapshot.Version);
    }

    [Fact]
    public void CompleteWithNoExtractionOrCalculatedIsValidForTheNoStatementCase()
    {
        var evaluation = NewEvaluation();

        evaluation.Complete(null, null, [], null, DateTimeOffset.UtcNow);

        Assert.Equal(EvaluationStatus.Completed, evaluation.Status);
        Assert.Null(evaluation.Extraction);
        Assert.Null(evaluation.Calculated);
    }
}
