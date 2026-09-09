using Gweb.Adapters.Persistence;
using Gweb.Adapters.Storage;
using Gweb.Domain.Applications;
using Gweb.Domain.Documents;
using Gweb.Domain.Evaluation;
using Gweb.Services.Evaluation;
using Gweb.Shared.Clock;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;
using Gweb.Shared.Logging;

namespace Gweb.Tests.Services.Evaluation;

public class EvaluationServiceTests
{
    private sealed class FakeEvaluationProvider(Func<StatementExtraction>? onCall = null, Exception? throwOnCall = null) : IEvaluationProvider
    {
        public int CallCount { get; private set; }

        public Task<McSuggestion> ClassifyMccAsync(
            BusinessProfileInput profile, IReadOnlyList<McCandidateSeed> catalogHints, DeadlineBudget budget, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException("Not exercised by EvaluationServiceTests.");

        public Task<StatementExtraction> ExtractStatementAsync(
            byte[] documentBytes, string contentType, DeadlineBudget budget, CancellationToken cancellationToken = default)
        {
            CallCount++;
            if (throwOnCall is not null)
            {
                throw throwOnCall;
            }
            return Task.FromResult(onCall!());
        }
    }

    private static DeadlineBudget Budget(long remainingMs = 35_000) => DeadlineBudget.Start(remainingMs, new FakeClock(0), targetMs: 35_000);

    private static StructuredLogger Logger() => new(TextWriter.Null, LogLevel.Debug);

    private static StatementExtraction FakeExtraction() =>
        new("Acme", 50_000m, 2.6m, 0.1m, 25m, 15m, "2026-08", "note", "gemini");

    private sealed record Harness(
        EvaluationService Service,
        InMemoryEvaluationRepository Evaluations,
        InMemoryBusinessRepository Businesses,
        InMemoryDocumentRepository Documents,
        InMemoryDocumentStorage Storage,
        InMemoryMcClassificationRepository Classifications,
        FakeEvaluationProvider Primary,
        FakeEvaluationProvider Fallback);

    private static Harness Build(FakeEvaluationProvider? primary = null, FakeEvaluationProvider? fallback = null)
    {
        var evaluations = new InMemoryEvaluationRepository();
        var businesses = new InMemoryBusinessRepository();
        var documents = new InMemoryDocumentRepository();
        var storage = new InMemoryDocumentStorage();
        var classifications = new InMemoryMcClassificationRepository();
        primary ??= new FakeEvaluationProvider(FakeExtraction);
        fallback ??= new FakeEvaluationProvider(FakeExtraction);
        var service = new EvaluationService(
            evaluations, businesses, documents, storage, classifications, primary, fallback, new FakeClock(0), Logger());
        return new Harness(service, evaluations, businesses, documents, storage, classifications, primary, fallback);
    }

    private static async Task<Guid> SeedBusinessAsync(InMemoryBusinessRepository businesses, VolumeProfile? volumeProfile = null)
    {
        var applicationId = Guid.NewGuid();
        var business = Business.CreateEmpty(applicationId, DateTimeOffset.UtcNow, "actor", "corr-1");
        business.ApplyUpdate(new BusinessUpdate(LegalBusinessName: "Test Co", VolumeProfile: volumeProfile), DateTimeOffset.UtcNow);
        await businesses.SaveAsync(business, expectedVersion: 0, Budget());
        return applicationId;
    }

    private static async Task<Guid> SeedReceivedStatementDocumentAsync(
        InMemoryDocumentRepository documents, InMemoryDocumentStorage storage, Guid applicationId, byte[]? bytes = null)
    {
        var documentId = Guid.NewGuid();
        var s3Key = $"applications/{applicationId}/documents/{documentId}/statement.pdf";
        var document = Document.CreateAndBeginUpload(
            documentId, applicationId, DocumentType.ProcessingStatement, "statement.pdf", "application/pdf",
            1024, s3Key, "abc==", DateTimeOffset.UtcNow, "actor", "corr-1");
        document.MarkReceived("abc==", 1024, DateTimeOffset.UtcNow);
        await documents.SaveAsync(document, expectedVersion: 0, Budget());
        storage.SimulateFullUpload(s3Key, bytes ?? "%PDF-1.7 fake statement"u8.ToArray());
        return documentId;
    }

    [Fact]
    public async Task ThrowsWhenNoBusinessExistsYet()
    {
        var harness = Build();

        await Assert.ThrowsAsync<ValidationException>(
            () => harness.Service.EvaluateAsync(Guid.NewGuid(), "corr-1", null, Budget()));
    }

    [Fact]
    public async Task WithNoStatementDocumentIdSkipsExtractionButStillComputesRiskSignals()
    {
        var harness = Build();
        var applicationId = await SeedBusinessAsync(harness.Businesses);

        var evaluation = await harness.Service.EvaluateAsync(applicationId, "corr-1", null, Budget());

        Assert.Equal(EvaluationStatus.Completed, evaluation.Status);
        Assert.Null(evaluation.Extraction);
        Assert.Null(evaluation.Calculated);
        Assert.Contains(evaluation.RiskSignals, s => s.Code == "MISSING_PROCESSING_STATEMENT");
        Assert.Equal(0, harness.Primary.CallCount);
    }

    [Fact]
    public async Task WithAValidStatementDocumentComputesExtractionAndCalculatedRate()
    {
        var volumeProfile = new VolumeProfile(600_000m, 100m, 500m, 1_000, 60m, 40m);
        var harness = Build();
        var applicationId = await SeedBusinessAsync(harness.Businesses, volumeProfile);
        var documentId = await SeedReceivedStatementDocumentAsync(harness.Documents, harness.Storage, applicationId);

        var evaluation = await harness.Service.EvaluateAsync(applicationId, "corr-1", documentId, Budget());

        Assert.Equal(EvaluationStatus.Completed, evaluation.Status);
        Assert.NotNull(evaluation.Extraction);
        Assert.Equal("gemini", evaluation.Extraction!.Provider);
        Assert.NotNull(evaluation.Calculated);
        Assert.Equal(documentId, evaluation.ProcessingStatementDocumentId);
        Assert.DoesNotContain(evaluation.RiskSignals, s => s.Code == "MISSING_PROCESSING_STATEMENT");
    }

    [Fact]
    public async Task FallsBackToTheMockProviderWhenThePrimaryProviderFails()
    {
        var harness = Build(
            primary: new FakeEvaluationProvider(throwOnCall: new DependencyTimeoutException("Gemini timed out.")),
            fallback: new FakeEvaluationProvider(() => FakeExtraction() with { Provider = "mock" }));
        var applicationId = await SeedBusinessAsync(harness.Businesses);
        var documentId = await SeedReceivedStatementDocumentAsync(harness.Documents, harness.Storage, applicationId);

        var evaluation = await harness.Service.EvaluateAsync(applicationId, "corr-1", documentId, Budget());

        Assert.Equal("mock", evaluation.Extraction!.Provider);
        Assert.Equal(1, harness.Primary.CallCount);
        Assert.Equal(1, harness.Fallback.CallCount);
    }

    [Fact]
    public async Task ThrowsWhenTheDocumentIdDoesNotReferenceAProcessingStatement()
    {
        var harness = Build();
        var applicationId = await SeedBusinessAsync(harness.Businesses);
        var documentId = Guid.NewGuid();
        var document = Document.CreateAndBeginUpload(
            documentId, applicationId, DocumentType.GovernmentId, "id.pdf", "application/pdf",
            1024, $"applications/{applicationId}/documents/{documentId}/x.pdf", "abc==", DateTimeOffset.UtcNow, "actor", "corr-1");
        document.MarkReceived("abc==", 1024, DateTimeOffset.UtcNow);
        await harness.Documents.SaveAsync(document, expectedVersion: 0, Budget());

        await Assert.ThrowsAsync<ValidationException>(
            () => harness.Service.EvaluateAsync(applicationId, "corr-1", documentId, Budget()));
    }

    [Fact]
    public async Task ThrowsWhenTheDocumentHasNotFinishedUploading()
    {
        var harness = Build();
        var applicationId = await SeedBusinessAsync(harness.Businesses);
        var documentId = Guid.NewGuid();
        // CreateAndBeginUpload leaves the document in Uploading -- MarkReceived is
        // never called, so it never reaches Received/Accepted.
        var document = Document.CreateAndBeginUpload(
            documentId, applicationId, DocumentType.ProcessingStatement, "statement.pdf", "application/pdf",
            1024, $"applications/{applicationId}/documents/{documentId}/x.pdf", "abc==", DateTimeOffset.UtcNow, "actor", "corr-1");
        await harness.Documents.SaveAsync(document, expectedVersion: 0, Budget());

        await Assert.ThrowsAsync<ValidationException>(
            () => harness.Service.EvaluateAsync(applicationId, "corr-1", documentId, Budget()));
    }

    [Fact]
    public async Task ThrowsNotFoundWhenTheDocumentIdDoesNotExist()
    {
        var harness = Build();
        var applicationId = await SeedBusinessAsync(harness.Businesses);

        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.Service.EvaluateAsync(applicationId, "corr-1", Guid.NewGuid(), Budget()));
    }

    [Fact]
    public async Task MarksProcessingAndReturnsImmediatelyWhenTheBudgetIsTooLowToAttemptAnything()
    {
        var harness = Build();
        var applicationId = Guid.NewGuid(); // deliberately no Business seeded -- must never be reached

        var evaluation = await harness.Service.EvaluateAsync(applicationId, "corr-1", null, Budget(remainingMs: 100));

        Assert.Equal(EvaluationStatus.Processing, evaluation.Status);
        Assert.Equal(0, harness.Primary.CallCount);
    }

    [Fact]
    public async Task GetReturnsNullWhenNoEvaluationExistsYet()
    {
        var harness = Build();

        var result = await harness.Service.GetAsync(Guid.NewGuid(), Budget());

        Assert.Null(result);
    }

    [Fact]
    public async Task ASecondEvaluateCallAfterCompletionUpdatesTheSameRecordNotACopy()
    {
        var harness = Build();
        var applicationId = await SeedBusinessAsync(harness.Businesses);
        await harness.Service.EvaluateAsync(applicationId, "corr-1", null, Budget());

        var second = await harness.Service.EvaluateAsync(applicationId, "corr-2", null, Budget());

        // First call: Version 0 -> Complete() bumps to 1. Second call loads that same
        // record and Complete()s it again: 1 -> 2. Two records would mean this stayed
        // at 1 (a fresh CreateEmpty each time) -- asserting 2 is what actually proves
        // "same record, not a copy."
        Assert.Equal(2, second.Version);
    }
}
