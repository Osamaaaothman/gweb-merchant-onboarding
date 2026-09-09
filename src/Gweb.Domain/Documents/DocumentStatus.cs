namespace Gweb.Domain.Documents;

/// <summary>The exact lifecycle from brief §4.1.</summary>
public enum DocumentStatus
{
    Requested,
    Uploading,
    Received,
    Processing,
    Accepted,
    NeedsReview,
    Rejected,
}
