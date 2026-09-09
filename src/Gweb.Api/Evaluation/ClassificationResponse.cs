using Gweb.Domain.Evaluation;

namespace Gweb.Api.Evaluation;

public sealed record McClassificationCandidateResponse(string MccCode, decimal Confidence, string Explanation)
{
    public static McClassificationCandidateResponse From(McClassificationCandidate candidate) =>
        new(candidate.MccCode, candidate.Confidence, candidate.Explanation);
}

public sealed record McClassificationResponse(
    IReadOnlyList<McClassificationCandidateResponse> Candidates,
    string? ProposedMccCode,
    string? ProposedProvider,
    DateTimeOffset? ClassifiedAt,
    string? SelfSelectedMccCode,
    DateTimeOffset? SelfSelectedAt,
    bool HasMismatch,
    long Version)
{
    public static McClassificationResponse From(McClassification classification) => new(
        [.. classification.Candidates.Select(McClassificationCandidateResponse.From)],
        classification.ProposedMccCode,
        classification.ProposedProvider,
        classification.ClassifiedAt,
        classification.SelfSelectedMccCode,
        classification.SelfSelectedAt,
        classification.HasMismatch,
        classification.Version);
}
