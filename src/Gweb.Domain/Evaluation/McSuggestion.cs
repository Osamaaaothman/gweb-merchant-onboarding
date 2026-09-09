namespace Gweb.Domain.Evaluation;

/// <summary>
/// Raw provider output before ClassificationService validates it against the real
/// catalog (drops any hallucinated code) and persists it. <paramref name="Provider"/>
/// is always "mock" or "gemini" -- every response this touches is labelled, per brief
/// "mock output is always labelled provider: mock."
/// </summary>
public sealed record McSuggestion(string Provider, IReadOnlyList<McClassificationCandidate> Candidates);
