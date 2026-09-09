using System.Net;
using System.Text;
using System.Text.Json;
using Gweb.Adapters.Evaluation;
using Gweb.Domain.Evaluation;
using Gweb.Shared.Clock;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;

namespace Gweb.Tests.Adapters.Evaluation;

public class GeminiEvaluationProviderTests
{
    private static readonly BusinessProfileInput Profile = new("Fresh Valley Grocers", "A neighborhood grocery store.", null, null);
    private static readonly List<McCandidateSeed> Hints = [new("5411", "Grocery Stores, Supermarkets")];

    private static DeadlineBudget Budget(long remainingMs = 35_000) => DeadlineBudget.Start(remainingMs, new FakeClock(0), targetMs: 35_000);

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            if (request.Content is not null)
            {
                RequestBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }
            return respond(request, CallCount);
        }
    }

    private static HttpClient ClientWith(HttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("https://generativelanguage.googleapis.com") };

    private static HttpResponseMessage GeminiEnvelope(string innerText, string finishReason = "STOP", HttpStatusCode status = HttpStatusCode.OK)
    {
        var envelope = new
        {
            candidates = new[]
            {
                new { content = new { parts = new[] { new { text = innerText } } }, finishReason },
            },
        };
        var json = JsonSerializer.Serialize(envelope);
        return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private static string ValidCandidateJson(string mccCode = "5411", decimal confidence = 0.9m) =>
        JsonSerializer.Serialize(new { candidates = new[] { new { mccCode, confidence, explanation = "matches description" } } });

    [Fact]
    public async Task ReturnsCandidatesOnAValidFirstResponseWithoutRetrying()
    {
        var handler = new FakeHttpMessageHandler((_, _) => GeminiEnvelope(ValidCandidateJson()));
        var provider = new GeminiEvaluationProvider(ClientWith(handler), "test-key", "gemini-test-model");

        var result = await provider.ClassifyMccAsync(Profile, Hints, Budget());

        Assert.Equal("gemini", result.Provider);
        Assert.Equal("5411", result.Candidates[0].MccCode);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task RetriesOnceWhenTheFirstResponseIsNotValidJsonThenSucceeds()
    {
        var handler = new FakeHttpMessageHandler((_, count) => count == 1
            ? GeminiEnvelope("this is not json at all")
            : GeminiEnvelope(ValidCandidateJson()));
        var provider = new GeminiEvaluationProvider(ClientWith(handler), "test-key", "gemini-test-model");

        var result = await provider.ClassifyMccAsync(Profile, Hints, Budget());

        Assert.Equal("5411", result.Candidates[0].MccCode);
        Assert.Equal(2, handler.CallCount);
        Assert.Contains("not valid JSON", handler.RequestBodies[1]);
    }

    [Fact]
    public async Task ThrowsAfterBothAttemptsReturnInvalidJson()
    {
        var handler = new FakeHttpMessageHandler((_, _) => GeminiEnvelope("still not json"));
        var provider = new GeminiEvaluationProvider(ClientWith(handler), "test-key", "gemini-test-model");

        await Assert.ThrowsAsync<DependencyUnavailableException>(() => provider.ClassifyMccAsync(Profile, Hints, Budget()));
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task TreatsAMaxTokensFinishReasonAsATruncatedResponseAndRetries()
    {
        var handler = new FakeHttpMessageHandler((_, count) => count == 1
            ? GeminiEnvelope("{\"candidates\": [{\"mccCode\": \"541", finishReason: "MAX_TOKENS")
            : GeminiEnvelope(ValidCandidateJson()));
        var provider = new GeminiEvaluationProvider(ClientWith(handler), "test-key", "gemini-test-model");

        var result = await provider.ClassifyMccAsync(Profile, Hints, Budget());

        Assert.Equal("5411", result.Candidates[0].MccCode);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task ThrowsImmediatelyOnANonSuccessStatusCodeWithoutRetrying()
    {
        var handler = new FakeHttpMessageHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("boom") });
        var provider = new GeminiEvaluationProvider(ClientWith(handler), "test-key", "gemini-test-model");

        await Assert.ThrowsAsync<DependencyUnavailableException>(() => provider.ClassifyMccAsync(Profile, Hints, Budget()));
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ThrowsImmediatelyWhenTheResponseBodyExceedsTheSizeLimit()
    {
        var oversized = new string('x', 25_000);
        var handler = new FakeHttpMessageHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(oversized) });
        var provider = new GeminiEvaluationProvider(ClientWith(handler), "test-key", "gemini-test-model");

        await Assert.ThrowsAsync<DependencyUnavailableException>(() => provider.ClassifyMccAsync(Profile, Hints, Budget()));
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ThrowsDependencyTimeoutExceptionWhenTheCallHangsPastItsBudget()
    {
        var hangingCall = new TaskCompletionSource<HttpResponseMessage>();
        var handler = new HangingHttpMessageHandler(hangingCall);
        var provider = new GeminiEvaluationProvider(ClientWith(handler), "test-key", "gemini-test-model");
        // remaining 3050ms - GeminiCallExecutor's 3000ms reserve = a 50ms real-time
        // timeout, so this test runs in well under a second, not 20+.
        var budget = Budget(remainingMs: 3_050);

        await Assert.ThrowsAsync<DependencyTimeoutException>(() => provider.ClassifyMccAsync(Profile, Hints, budget));
    }

    [Fact]
    public async Task NeverAttemptsTheCallWhenTheRemainingBudgetIsBelowTheReserve()
    {
        var handler = new FakeHttpMessageHandler((_, _) => throw new InvalidOperationException("should never be called"));
        var provider = new GeminiEvaluationProvider(ClientWith(handler), "test-key", "gemini-test-model");

        await Assert.ThrowsAsync<DependencyTimeoutException>(() => provider.ClassifyMccAsync(Profile, Hints, Budget(remainingMs: 100)));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task SendsTheApiKeyAsAHeaderNeverAsAQueryStringParameter()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new FakeHttpMessageHandler((request, _) =>
        {
            capturedRequest = request;
            return GeminiEnvelope(ValidCandidateJson());
        });
        var provider = new GeminiEvaluationProvider(ClientWith(handler), "super-secret-key", "gemini-test-model");

        await provider.ClassifyMccAsync(Profile, Hints, Budget());

        Assert.NotNull(capturedRequest);
        Assert.DoesNotContain("super-secret-key", capturedRequest!.RequestUri!.ToString());
        Assert.Equal("super-secret-key", capturedRequest.Headers.GetValues("x-goog-api-key").Single());
    }

    private sealed class HangingHttpMessageHandler(TaskCompletionSource<HttpResponseMessage> source) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.Register(() => source.TrySetCanceled(cancellationToken));
            return source.Task;
        }
    }
}
