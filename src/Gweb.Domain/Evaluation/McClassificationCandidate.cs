namespace Gweb.Domain.Evaluation;

/// <summary>One ranked MCC suggestion. Confidence is always in [0, 1] -- enforced by
/// ClassificationService before this is ever persisted, not trusted from the provider.</summary>
public sealed record McClassificationCandidate(string MccCode, decimal Confidence, string Explanation);
