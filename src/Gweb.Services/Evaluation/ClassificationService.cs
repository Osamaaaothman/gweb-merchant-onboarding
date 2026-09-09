using Gweb.Domain.Applications;
using Gweb.Domain.Evaluation;
using Gweb.Domain.Mcc;
using Gweb.Shared.Clock;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;
using Gweb.Shared.Logging;

namespace Gweb.Services.Evaluation;

/// <summary>
/// Orchestrates classify/confirm. Owns the one policy decision that belongs at this
/// layer, not inside either provider: if the configured primary provider fails in any
/// way (timeout, invalid JSON, network error, hallucinated-only candidates), fall back
/// to <paramref name="fallbackProvider"/> (always the mock, regardless of config) and
/// clearly label the result "provider": "mock" -- per brief "fall back safely" and
/// "mock output is always labelled." When AI_PROVIDER=mock, primaryProvider already
/// *is* the mock provider, so this fallback path is a harmless no-op in that
/// configuration, not dead code -- it's what makes "the interface and failure behavior
/// must be production-minded, even in mock mode" true structurally.
/// </summary>
public sealed class ClassificationService(
    IMcClassificationRepository repository,
    IBusinessRepository businessRepository,
    IMccCatalog catalog,
    IEvaluationProvider primaryProvider,
    IEvaluationProvider fallbackProvider,
    IClock clock,
    StructuredLogger logger)
{
    private const int CatalogHintCount = 25;
    private const int KeywordsToTry = 10;
    private const int HitsPerKeyword = 5;
    private const int MinKeywordLength = 4;

    public async Task<McClassification> ClassifyAsync(
        Guid applicationId, string correlationId, DeadlineBudget budget, CancellationToken cancellationToken = default)
    {
        var business = await businessRepository.GetByApplicationIdAsync(applicationId, budget, cancellationToken).ConfigureAwait(false)
            ?? throw new ValidationException("Business must be filled in before classification.");

        var profile = new BusinessProfileInput(business.LegalBusinessName, business.BusinessDescription, business.WebsiteUrl, business.EntityType);
        var hints = BuildCatalogHints(business.BusinessDescription);

        var suggestion = await GetSuggestionWithFallbackAsync(profile, hints, budget, cancellationToken).ConfigureAwait(false);

        var validCandidates = suggestion.Candidates
            .Where(c => catalog.GetByCode(c.MccCode) is not null)
            .Where(c => c.Confidence is >= 0m and <= 1m)
            .OrderByDescending(c => c.Confidence)
            .ToList();

        if (validCandidates.Count == 0)
        {
            // Every candidate the provider returned was either a hallucinated code
            // (not in the real catalog) or out-of-range -- not a network/parse
            // failure, so no further fallback makes sense; this is a hard failure.
            throw new DependencyUnavailableException("The evaluation provider returned no valid MCC candidates.");
        }

        var now = DateTimeOffset.FromUnixTimeMilliseconds(clock.NowMs());
        var existing = await repository.GetByApplicationIdAsync(applicationId, budget, cancellationToken).ConfigureAwait(false);
        var expectedVersion = existing?.Version ?? 0;
        var classification = existing ?? McClassification.CreateEmpty(applicationId, now, correlationId);

        classification.RecordProposal(validCandidates, suggestion.Provider, now);

        await repository.SaveAsync(classification, expectedVersion, budget, cancellationToken).ConfigureAwait(false);

        return classification;
    }

    public async Task<McClassification> ConfirmSelfSelectedAsync(
        Guid applicationId, string mccCode, string correlationId, DeadlineBudget budget, CancellationToken cancellationToken = default)
    {
        if (catalog.GetByCode(mccCode) is null)
        {
            throw new ValidationException($"'{mccCode}' is not a known MCC code.");
        }

        var now = DateTimeOffset.FromUnixTimeMilliseconds(clock.NowMs());
        var existing = await repository.GetByApplicationIdAsync(applicationId, budget, cancellationToken).ConfigureAwait(false);
        var expectedVersion = existing?.Version ?? 0;
        var classification = existing ?? McClassification.CreateEmpty(applicationId, now, correlationId);

        classification.ConfirmSelfSelected(mccCode, now);

        await repository.SaveAsync(classification, expectedVersion, budget, cancellationToken).ConfigureAwait(false);

        return classification;
    }

    public Task<McClassification?> GetAsync(Guid applicationId, DeadlineBudget budget, CancellationToken cancellationToken = default) =>
        repository.GetByApplicationIdAsync(applicationId, budget, cancellationToken);

    /// <summary>
    /// IMccCatalog.Search does substring/prefix matching (see StaticMccCatalog),
    /// tuned for a short typed query like "grocery" -- not a full free-text
    /// description. Passing the whole BusinessDescription sentence straight through
    /// as one query essentially never matches anything (found for real while manually
    /// verifying this phase's Gemini integration end-to-end: a legitimate grocery-store
    /// description produced zero catalog hints, which meant neither Gemini nor the
    /// mock fallback had anything real to rank). Fixed by searching per significant
    /// word and unioning the results, falling back to the catalog's own browsing
    /// default only if every keyword search comes up empty.
    /// </summary>
    private List<McCandidateSeed> BuildCatalogHints(string? businessDescription)
    {
        if (string.IsNullOrWhiteSpace(businessDescription))
        {
            return ToHints(catalog.Search(null, CatalogHintCount));
        }

        var keywords = businessDescription
            .Split([' ', ',', '.', ';', ':', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= MinKeywordLength)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(KeywordsToTry);

        var matches = new Dictionary<string, MccCode>();
        foreach (var keyword in keywords)
        {
            foreach (var match in catalog.Search(keyword, HitsPerKeyword))
            {
                matches.TryAdd(match.Code, match);
            }
        }

        return matches.Count > 0 ? ToHints(matches.Values.Take(CatalogHintCount)) : ToHints(catalog.Search(null, CatalogHintCount));
    }

    private static List<McCandidateSeed> ToHints(IEnumerable<MccCode> codes) =>
        [.. codes.Select(c => new McCandidateSeed(c.Code, c.Description))];

    private async Task<McSuggestion> GetSuggestionWithFallbackAsync(
        BusinessProfileInput profile, IReadOnlyList<McCandidateSeed> hints, DeadlineBudget budget, CancellationToken cancellationToken)
    {
        if (ReferenceEquals(primaryProvider, fallbackProvider))
        {
            // AI_PROVIDER=mock -- primary already is the guaranteed-available
            // fallback, so there's nothing to catch a failure from.
            return await primaryProvider.ClassifyMccAsync(profile, hints, budget, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            return await primaryProvider.ClassifyMccAsync(profile, hints, budget, cancellationToken).ConfigureAwait(false);
        }
        catch (DomainException ex)
        {
            logger.Warn("evaluation_provider_fallback", new { reason = ex.Code, message = ex.Message });
            return await fallbackProvider.ClassifyMccAsync(profile, hints, budget, cancellationToken).ConfigureAwait(false);
        }
    }
}
