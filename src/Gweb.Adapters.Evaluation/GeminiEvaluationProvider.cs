using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gweb.Domain.Evaluation;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;
using Gweb.Shared.Resilience;

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
/// - "bounded retry with backoff + jitter, budget-aware" (Phase 10) -- every call
///   routed through <see cref="CallOnceAsync"/> is wrapped in <see cref="BoundedRetry"/>,
///   which only retries a Retryable DomainException (a transient timeout/unavailable
///   failure) and never past what the remaining budget can afford. This is deliberately
///   the one adapter that gets an app-level retry layer -- see docs/adr/0009-deadline-
///   hardening-and-retry.md for why DynamoDB/S3 rely on the AWS SDK's own built-in
///   retry policy instead of a second, redundant one stacked on top of it here.
/// </summary>
public sealed class GeminiEvaluationProvider(
    HttpClient httpClient, string apiKey, string model, IDelay? delay = null, int maxCallAttempts = 3) : IEvaluationProvider
{
    private const int MaxCandidates = 5;
    private const int MaxResponseBodyChars = 20_000;
    private readonly IDelay _delay = delay ?? new TaskDelay();

    // Empirically, this model spends a large share of its output-token budget on
    // hidden "thinking" tokens before producing visible text (confirmed via the raw
    // usageMetadata.thoughtsTokenCount field during manual verification) -- too low a
    // cap here silently truncates the answer into invalid JSON. 2048 was the smallest
    // value that reliably left room for a complete 3-candidate response during testing.
    private const int MaxOutputTokens = 2048;

    private static readonly JsonSerializerOptions ResponseJsonOptions = new() { PropertyNameCaseInsensitive = true };

    // Request bodies must omit inlineData entirely on a text-only call (classify) --
    // Gemini's API was not verified to tolerate an explicit "inlineData": null field,
    // so this is not a guess.
    private static readonly JsonSerializerOptions RequestJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

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

    /// <summary>
    /// Sends the actual document bytes as an inline multimodal part -- Gemini's API
    /// genuinely reads PDF/image content this way, verified against the live API
    /// during this phase's build (a plain-text file sent this way was correctly
    /// described back). No repair retry here, unlike ClassifyMccAsync: resending the
    /// same (potentially sizable) file bytes a second time risks blowing
    /// GeminiCallExecutor's 20s cap on its own; EvaluationService's fallback to the
    /// mock extractor is the safety net for this call instead.
    /// </summary>
    public async Task<StatementExtraction> ExtractStatementAsync(
        byte[] documentBytes, string contentType, DeadlineBudget budget, CancellationToken cancellationToken = default)
    {
        var inlineData = new GeminiInlineData(contentType, Convert.ToBase64String(documentBytes));
        var text = await CallOnceAsync(BuildExtractionPrompt(), budget, cancellationToken, inlineData).ConfigureAwait(false);

        if (!TryParseExtraction(text, out var extraction))
        {
            throw new DependencyUnavailableException("Gemini did not return valid JSON matching the statement extraction schema.");
        }
        return extraction;
    }

    private static bool TryParseExtraction(string text, out StatementExtraction extraction)
    {
        extraction = null!;
        var trimmed = StripMarkdownFences(text);

        ExtractionSchema? schema;
        try
        {
            schema = JsonSerializer.Deserialize<ExtractionSchema>(trimmed, ResponseJsonOptions);
        }
        catch (JsonException)
        {
            return false;
        }
        if (schema is null)
        {
            return false;
        }

        extraction = new StatementExtraction(
            schema.Processor,
            schema.MonthlyVolume,
            schema.DiscountRatePercent,
            schema.PerTransactionFee,
            schema.MonthlyFee,
            schema.ChargebackFeeTotal,
            schema.StatementPeriod,
            schema.Commentary,
            "gemini");
        return true;
    }

    private static string BuildExtractionPrompt() => $$"""
        You are extracting normalized figures from a merchant payment-processing statement
        (attached as a file). Return strict JSON matching exactly this schema and nothing
        else (no markdown fences, no commentary outside the "commentary" field):
        {"processor": string|null, "monthlyVolume": number|null, "discountRatePercent": number|null,
         "perTransactionFee": number|null, "monthlyFee": number|null, "chargebackFeeTotal": number|null,
         "statementPeriod": string|null, "commentary": string|null}

        Rules:
        - Use null for any figure the statement does not clearly show -- never guess or
          estimate a number that is not actually printed on the statement.
        - "discountRatePercent" is the processor's percentage-based discount/effective rate
          (e.g. 2.6 for 2.6%), not a dollar amount.
        - "commentary" is a short (1-2 sentence) plain-language note about anything notable
          in the statement -- it is read-only context for a human reviewer and must never be
          treated as one of the numeric figures above.
        - If the attached file is not a payment-processing statement at all, return every
          numeric field as null and say so in "commentary".
        """;

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

    private Task<string> CallOnceAsync(
        string prompt, DeadlineBudget budget, CancellationToken cancellationToken, GeminiInlineData? inlineData = null) =>
        BoundedRetry.ExecuteAsync(
            () => GeminiCallExecutor.ExecuteAsync(async ct =>
            {
                var parts = inlineData is null
                    ? (IReadOnlyList<GeminiPart>)[new GeminiPart(Text: prompt)]
                    : [new GeminiPart(Text: prompt), new GeminiPart(InlineData: inlineData)];
                var requestBody = new GeminiRequest(
                    [new GeminiContent(parts)],
                    new GeminiGenerationConfig("application/json", MaxOutputTokens));

                using var request = new HttpRequestMessage(HttpMethod.Post, $"/v1beta/models/{model}:generateContent")
                {
                    Content = JsonContent.Create(requestBody, options: RequestJsonOptions),
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
            }, budget, cancellationToken),
            budget,
            _delay,
            cancellationToken,
            maxCallAttempts);

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

    // Text and InlineData are both optional so the same type serializes a text-only
    // part (classify) or an inline-document part (statement extraction) without a
    // spurious null field for the one not in use -- see RequestJsonOptions.
    private sealed record GeminiPart(
        [property: JsonPropertyName("text")] string? Text = null,
        [property: JsonPropertyName("inlineData")] GeminiInlineData? InlineData = null);

    private sealed record GeminiInlineData(
        [property: JsonPropertyName("mimeType")] string MimeType,
        [property: JsonPropertyName("data")] string Data);

    private sealed record GeminiGenerationConfig(
        [property: JsonPropertyName("responseMimeType")] string ResponseMimeType,
        [property: JsonPropertyName("maxOutputTokens")] int MaxOutputTokens);

    private sealed record GeminiResponse(IReadOnlyList<GeminiResponseCandidate>? Candidates);

    private sealed record GeminiResponseCandidate(GeminiResponseContent? Content, string? FinishReason);

    private sealed record GeminiResponseContent(IReadOnlyList<GeminiPart>? Parts);

    private sealed record ClassificationSchema(IReadOnlyList<ClassificationCandidateSchema>? Candidates);

    private sealed record ClassificationCandidateSchema(string? MccCode, decimal Confidence, string? Explanation);

    private sealed record ExtractionSchema(
        string? Processor,
        decimal? MonthlyVolume,
        decimal? DiscountRatePercent,
        decimal? PerTransactionFee,
        decimal? MonthlyFee,
        decimal? ChargebackFeeTotal,
        string? StatementPeriod,
        string? Commentary);
}
