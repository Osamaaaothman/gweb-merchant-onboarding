namespace Gweb.Domain.Evaluation;

/// <summary>
/// Deliberately just these two -- see NoAutoApprovalPathTests, which asserts no
/// domain outcome enum anywhere in this codebase ever grows an "Approved" value.
/// Processing is the brief's "async fallback if the budget cannot be met" state
/// (202 + PROCESSING + poll); Completed carries the actual result.
/// </summary>
public enum EvaluationStatus
{
    Processing,
    Completed,
}
