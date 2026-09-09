namespace Gweb.Domain.Evaluation;

/// <summary>
/// A real catalog entry offered to the provider as a grounding option -- the provider
/// must choose codes only from what it's given here, never invent one. Built by
/// ClassificationService from IMccCatalog, kept out of IEvaluationProvider's own
/// dependencies so the provider interface never needs to know the catalog exists.
/// </summary>
public sealed record McCandidateSeed(string MccCode, string Description);
