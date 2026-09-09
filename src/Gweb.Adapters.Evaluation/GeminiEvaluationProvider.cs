using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gweb.Domain.Evaluation;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;

namespace Gweb.Adapters.Evaluation;

/// <summary>
/// Real implementation of IEvaluationProvider, calling the Gemini API's generateContent
/// endpoint. Verified end-to-end against the live API during this phase's build (see
/// docs/adr/0006-ai-evaluation-provider.md) -- request/response shapes here are not
/// guessed, they match what the real API actually returned.
///
/// Key design points, all direct answers to brief requirements:
/// - "AI output is untrusted structured input" -- every candidate is schema-validated
///   (non-empty code/explanation, confidence clamped to [0,1]) and then further
///   checked against the real MCC catalog by ClassificationService (this class never
///   sees IMccCatalog at all, so it structurally cannot special-case around it).
/// - "impose token/size limits" -- MaxResponseBodyChars guards oversized output;
///   maxOutputTokens is capped in the request itself.
/// - "enforce timeouts" -- every call goes through GeminiCallExecutor.
/// - "bounded repair retry" -- exactly one retry with a corrective follow-up prompt
///   if the first response isn't valid JSON, never an unbounded loop.
/// - "fall back safely" -- this class throws real DomainExceptions on every failure
///   path; it never fabricates a success. Falling back to the mock provider is
///   ClassificationService's job, not this class's.
/// - The API key travels as the `x-goog-api-key` header, not a `?key=` query
///   parameter -- verified both forms work against the real API, header chosen so the
///   key never appears in a URL that some logging middleware might capture.
/// </summary>
public sealed class GeminiEvaluationProvider(HttpClient httpClient, string apiKey, string model) : IEvaluationProvider
{
    private const int MaxCandidates = 5;
    private const int MaxResponseBodyChars = 20_000;

    // Empirically, this model spends a large share of its output-token budget on
    // hidden "thinking" tokens before producing visible text (confirmed via the raw
    // usageMetadata.thoughtsTokenCount field during manual verification) -- too low a
    // cap here silently truncates the answer into invalid JSON. 2048 was the smallest
    // value that reliably left room for a complete 3-candidate response during testing.
    private const int MaxOutputTokens = 2048;

    private static readonly JsonSerializerOptions ResponseJsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<McSuggestion> ClassifyMccAsync(
        BusinessProfileInput profile,
        IReadOnlyList<McCandidateSeed> catalogHints,
        DeadlineBudget budget,
        CancellationToken cancellationToken = default)
    {
        var prompt = BuildPrompt(profile, catalogHints);

        var candidates = await CallWithRepairRetryAsync(prompt, budget, cancellationToken).ConfigureAwait(false);

        return new McSuggestion("gemini", candidates);
    }

    private async Task<IReadOnlyList<McClassificationCandidate>> CallWithRepairRetryAsync(
        string prompt, DeadlineBudget budget, CancellationToken cancellationToken)
    {
        var firstText = await CallOnceAsync(prompt, budget, cancellationToken).ConfigureAwait(false);
        if (TryParseCandidates(firstText, out var candidates))
        {
            return candidates;
        }

        var repairPrompt = prompt +
            "\n\nYour previous response was not valid JSON matching the required schema. " +
            "Reply again with ONLY the JSON object -- no markdown fences, no commentary.";
        var secondText = await CallOnceAsync(repairPrompt, budget, cancellationToken).ConfigureAwait(false);
        if (TryParseCandidates(secondText, out var repairedCandidates))
        {
            return repairedCandidates;
        }

        throw new DependencyUnavailableException("Gemini did not return valid JSON matching the classification schema after a repair retry.");
    }

    private Task<string> CallOnceAsync(string prompt, DeadlineBudget budget, CancellationToken cancellationToken) =>
        GeminiCallExecutor.ExecuteAsync(async ct =>
        {
            var requestBody = new GeminiRequest(
                [new GeminiContent([new GeminiPart(prompt)])],
                new GeminiGenerationConfig("application/json", MaxOutputTokens));

            using var request = new HttpRequestMessage(HttpMethod.Post, $"/v1beta/models/{model}:generateContent")
            {
                Content = JsonContent.Create(requestBody),
            };
            request.Headers.Add("x-goog-api-key", apiKey);

            using var response = await httpClient.SendAsync(request, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (body.Length > MaxResponseBodyChars)
            {
                throw new DependencyUnavailableException("Gemini response exceeded the maximum allowed size.");
            }
            if (!response.IsSuccessStatusCode)
            {
                throw new DependencyUnavailableException($"Gemini returned HTTP {(int)response.StatusCode}.");
            }

            return ExtractText(body);
        }, budget, cancellationToken);

    private static string ExtractText(string responseBody)
    {
        GeminiResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<GeminiResponse>(responseBody, ResponseJsonOptions);
        }
        catch (JsonException)
        {
            throw new DependencyUnavailableException("Gemini returned a response that could not be parsed.");
        }

        var candidate = parsed?.Candidates is { Count: > 0 } candidateList ? candidateList[0] : null;
        var text = candidate?.Content?.Parts is { Count: > 0 } partsList ? partsList[0].Text : null;
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new DependencyUnavailableException("Gemini returned no usable content, possibly blocked by a safety filter.");
        }
        // Deliberately NOT throwing here on finishReason == "MAX_TOKENS", even though
        // that means the output-token cap cut the response off before it finished
        // writing JSON -- exactly the "oversized output" failure mode the brief calls
        // out. Throwing from inside CallOnceAsync would skip CallWithRepairRetryAsync's
        // retry entirely (a thrown exception propagates past the TryParseCandidates
        // check, not through it). A caught real bug during this phase's own test
        // suite: the first version threw here and the "MAX_TOKENS triggers a repair
        // retry" test failed because no retry ever happened. Truncated JSON text is
        // syntactically invalid on its own, so returning it here and letting
        // TryParseCandidates's normal JsonException handling reject it achieves the
        // same "invalid response" outcome through the one retry path that actually
        // exists, rather than a second, broken one.
        return text;
    }

    private static bool TryParseCandidates(string text, out IReadOnlyList<McClassificationCandidate> candidates)
    {
        candidates = [];
        var trimmed = StripMarkdownFences(text);

        ClassificationSchema? schema;
        try
        {
            schema = JsonSerializer.Deserialize<ClassificationSchema>(trimmed, ResponseJsonOptions);
        }
        catch (JsonException)
        {
            return false;
        }
        if (schema?.Candidates is null || schema.Candidates.Count == 0)
        {
            return false;
        }

        var parsed = new List<McClassificationCandidate>();
        foreach (var candidate in schema.Candidates.Take(MaxCandidates))
        {
            if (string.IsNullOrWhiteSpace(candidate.MccCode) || string.IsNullOrWhiteSpace(candidate.Explanation))
            {
                continue; // Drop malformed entries rather than fail the whole response.
            }
            var confidence = Math.Clamp(candidate.Confidence, 0m, 1m);
            parsed.Add(new McClassificationCandidate(candidate.MccCode.Trim(), confidence, candidate.Explanation.Trim()));
        }
        if (parsed.Count == 0)
        {
            return false;
        }
        candidates = parsed;
        return true;
    }

    private static string StripMarkdownFences(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }
        var firstNewline = trimmed.IndexOf('\n');
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return firstNewline > 0 && lastFence > firstNewline
            ? trimmed[(firstNewline + 1)..lastFence].Trim()
            : trimmed;
    }

    private static string BuildPrompt(BusinessProfileInput profile, IReadOnlyList<McCandidateSeed> catalogHints)
    {
        var hintsText = string.Join('\n', catalogHints.Select(h => $"{h.MccCode}: {h.Description}"));
        // $$""" (not $""") because the JSON schema line below needs literal single
        // braces -- with a double-dollar prefix, interpolation holes need {{ }} and a
        // bare { is just a brace, avoiding the escaping mess a single-$ raw string
        // would need for JSON-shaped literal text.
        return $$"""
            You are classifying a merchant's business into a Merchant Category Code (MCC) for payment processing underwriting.
            Choose ONLY from the candidate MCC list below -- never invent a code that is not listed.
            Return strict JSON matching exactly this schema and nothing else (no markdown fences, no commentary):
            {"candidates": [{"mccCode": string, "confidence": number between 0 and 1, "explanation": string}]}
            Return up to 3 candidates, ranked best first.

            Business name: {{profile.LegalBusinessName ?? "(not provided)"}}
            Business description: {{profile.BusinessDescription ?? "(not provided)"}}
            Website: {{profile.WebsiteUrl ?? "(not provided)"}}
            Legal entity type: {{profile.EntityType?.ToString() ?? "(not provided)"}}

            Candidate MCCs:
            {{hintsText}}
            """;
    }

    private sealed record GeminiRequest(
        [property: JsonPropertyName("contents")] IReadOnlyList<GeminiContent> Contents,
        [property: JsonPropertyName("generationConfig")] GeminiGenerationConfig GenerationConfig);

    private sealed record GeminiContent([property: JsonPropertyName("parts")] IReadOnlyList<GeminiPart> Parts);

    private sealed record GeminiPart([property: JsonPropertyName("text")] string Text);

    private sealed record GeminiGenerationConfig(
        [property: JsonPropertyName("responseMimeType")] string ResponseMimeType,
        [property: JsonPropertyName("maxOutputTokens")] int MaxOutputTokens);

    private sealed record GeminiResponse(IReadOnlyList<GeminiResponseCandidate>? Candidates);

    private sealed record GeminiResponseCandidate(GeminiResponseContent? Content, string? FinishReason);

    private sealed record GeminiResponseContent(IReadOnlyList<GeminiPart>? Parts);

    private sealed record ClassificationSchema(IReadOnlyList<ClassificationCandidateSchema>? Candidates);

    private sealed record ClassificationCandidateSchema(string? MccCode, decimal Confidence, string? Explanation);
}
