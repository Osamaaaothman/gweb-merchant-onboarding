namespace Gweb.Domain.Evaluation;

/// <summary>
/// Per brief "Every warning must cite the input field or document that caused it. No
/// unexplained flags." <see cref="SourceField"/> is required on every signal;
/// <see cref="SourceDocumentId"/> is set only when the signal concerns a specific
/// uploaded document (e.g. the processing statement itself).
/// </summary>
public sealed record RiskSignal(string Code, string Message, string SourceField, Guid? SourceDocumentId = null);
