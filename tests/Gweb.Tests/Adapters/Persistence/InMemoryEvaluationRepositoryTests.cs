using Gweb.Adapters.Persistence;
using Gweb.Domain.Evaluation;
using Gweb.Shared.Clock;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;

namespace Gweb.Tests.Adapters.Persistence;

// A sibling test namespace (Gweb.Tests.Adapters.Evaluation, from
// GeminiEvaluationProviderTests.cs et al.) shadows the bare "Evaluation" identifier
// anywhere under Gweb.Tests.Adapters.* -- namespace-member lookup in an enclosing
// scope always wins over a using-alias, so even `using Evaluation = ...;` cannot fix
// this; every reference below is fully qualified with `global::` instead.

public class InMemoryEvaluationRepositoryTests
{
    private static DeadlineBudget Budget() => DeadlineBudget.Start(35_000, new FakeClock(0), targetMs: 35_000);

    [Fact]
    public async Task ReturnsNullWhenNoEvaluationExists()
    {
        var repository = new InMemoryEvaluationRepository();

        var result = await repository.GetByApplicationIdAsync(Guid.NewGuid(), Budget());

        Assert.Null(result);
    }

    [Fact]
    public async Task SaveThenGetRoundTripsExtractionCalculatedAndRiskSignals()
    {
        var repository = new InMemoryEvaluationRepository();
        var applicationId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        // expectedVersion is always the version that was already stored (i.e. before
        // the local mutation that just bumped Version), never the object's own current
        // Version -- Evaluation.CreateEmpty starts at Version=0 and does no internal
        // transition (unlike e.g. Document.CreateAndBeginUpload), so what's actually
        // stored after this first save is still Version=0.
        var evaluation = global::Gweb.Domain.Evaluation.Evaluation.CreateEmpty(applicationId, DateTimeOffset.UtcNow, "corr-1");
        await repository.SaveAsync(evaluation, expectedVersion: 0, Budget());

        var extraction = new StatementExtraction("Acme", 50_000m, 2.6m, 0.1m, 25m, 15m, "2026-08", "note", "gemini");
        var calculated = new EffectiveRateResult(1_440m, 2.88m, 1_300m, 100m, 25m, 15m);
        var signals = new List<RiskSignal> { new("CODE", "message", "field", documentId) };
        evaluation.Complete(extraction, calculated, signals, documentId, DateTimeOffset.UtcNow);
        await repository.SaveAsync(evaluation, expectedVersion: 0, Budget());

        var result = await repository.GetByApplicationIdAsync(applicationId, Budget());

        Assert.NotNull(result);
        Assert.Equal(EvaluationStatus.Completed, result!.Status);
        Assert.Equal(extraction, result.Extraction);
        Assert.Equal(calculated, result.Calculated);
        Assert.Equal(signals, result.RiskSignals);
        Assert.Equal(documentId, result.ProcessingStatementDocumentId);
    }

    [Fact]
    public async Task RejectsAnUpdateAgainstAStaleVersion()
    {
        var repository = new InMemoryEvaluationRepository();
        var applicationId = Guid.NewGuid();
        var evaluation = global::Gweb.Domain.Evaluation.Evaluation.CreateEmpty(applicationId, DateTimeOffset.UtcNow, "corr-1");
        await repository.SaveAsync(evaluation, expectedVersion: 0, Budget());

        evaluation.Complete(null, null, [], null, DateTimeOffset.UtcNow);
        await repository.SaveAsync(evaluation, expectedVersion: 0, Budget()); // now stored at Version=1

        var staleWrite = global::Gweb.Domain.Evaluation.Evaluation.CreateEmpty(applicationId, DateTimeOffset.UtcNow, "corr-2");

        await Assert.ThrowsAsync<ConflictException>(() => repository.SaveAsync(staleWrite, expectedVersion: 0, Budget()));
    }
}
