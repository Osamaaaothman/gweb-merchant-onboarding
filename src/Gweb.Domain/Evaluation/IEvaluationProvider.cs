using Gweb.Shared.Deadline;

namespace Gweb.Domain.Evaluation;

/// <summary>
/// "Must work through an adapter with a mock implementation when no credentials exist.
/// The interface and failure behavior must be production-minded, even in mock mode."
/// (brief). A real implementation must throw a Gweb.Shared.Errors.DomainException
/// (DependencyTimeoutException/DependencyUnavailableException) on failure -- never
/// return a fabricated success -- so ClassificationService's fallback-to-mock policy
/// has something real to catch.
/// </summary>
public interface IEvaluationProvider
{
    Task<McSuggestion> ClassifyMccAsync(
        BusinessProfileInput profile,
        IReadOnlyList<McCandidateSeed> catalogHints,
        DeadlineBudget budget,
        CancellationToken cancellationToken = default);
}
