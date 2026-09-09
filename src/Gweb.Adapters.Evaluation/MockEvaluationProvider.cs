using Gweb.Domain.Evaluation;
using Gweb.Shared.Deadline;

namespace Gweb.Adapters.Evaluation;

/// <summary>
/// Default provider, per brief "Must work through an adapter with a mock
/// implementation when no credentials exist." Deterministic and offline: ranks the
/// catalog hints it's handed (already relevance-ordered by IMccCatalog.Search, via
/// ClassificationService) by position, with a synthetic decreasing confidence -- no
/// network call, no randomness, exactly reproducible. Reuses real catalog data rather
/// than a hand-maintained fixture list, so it can never suggest a code that doesn't
/// exist.
/// </summary>
public sealed class MockEvaluationProvider : IEvaluationProvider
{
    private const int MaxCandidates = 3;

    public Task<McSuggestion> ClassifyMccAsync(
        BusinessProfileInput profile,
        IReadOnlyList<McCandidateSeed> catalogHints,
        DeadlineBudget budget,
        CancellationToken cancellationToken = default)
    {
        var top = catalogHints.Take(MaxCandidates).ToList();

        var candidates = top.Count > 0
            ? top.Select((hint, index) => new McClassificationCandidate(
                hint.MccCode,
                Math.Round(0.9m - index * 0.15m, 2),
                $"Mock match: business profile keywords align with catalog entry \"{hint.Description}\"."))
                .ToList()
            : [
                new McClassificationCandidate(
                    "5999",
                    0.3m,
                    "Mock fallback: no catalog hints were available to match against; defaulted to a general retail category pending manual review."),
            ];

        return Task.FromResult(new McSuggestion("mock", candidates));
    }
}
