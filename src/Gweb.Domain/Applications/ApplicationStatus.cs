namespace Gweb.Domain.Applications;

public enum ApplicationStatus
{
    /// <summary>Created, resumable, still being filled in.</summary>
    InProgress,

    /// <summary>Locked and versioned per docs/00-PRODUCT-BRIEF.md's submit flow. Terminal.</summary>
    Submitted,
}
