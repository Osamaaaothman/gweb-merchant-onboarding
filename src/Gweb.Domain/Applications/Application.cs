using Gweb.Shared.Errors;

namespace Gweb.Domain.Applications;

/// <summary>
/// The aggregate root for a merchant onboarding application. Phase 2 only covers the
/// lifecycle envelope (create, resume, submit-transition guard); applicant/business
/// fields land in Phase 3 as their own PATCH-able sub-documents in the same DynamoDB
/// partition (see docs/adr/0003-dynamodb-table-strategy.md).
/// </summary>
public sealed class Application
{
    public Guid Id { get; }
    public ApplicationStatus Status { get; private set; }

    /// <summary>Optimistic-concurrency version. Incremented on every state change.</summary>
    public long Version { get; private set; }

    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public string CreatedBy { get; }
    public string CorrelationId { get; }

    private Application(
        Guid id,
        ApplicationStatus status,
        long version,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        string createdBy,
        string correlationId)
    {
        Id = id;
        Status = status;
        Version = version;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        CreatedBy = createdBy;
        CorrelationId = correlationId;
    }

    /// <summary>Starts a brand-new application. Always InProgress, always version 1.</summary>
    public static Application Create(Guid id, DateTimeOffset now, string createdBy, string correlationId) =>
        new(id, ApplicationStatus.InProgress, version: 1, now, now, createdBy, correlationId);

    /// <summary>
    /// Reconstructs an application from persisted state. Only repositories call this —
    /// application logic that wants a NEW application must call <see cref="Create"/>.
    /// </summary>
    public static Application Rehydrate(
        Guid id,
        ApplicationStatus status,
        long version,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        string createdBy,
        string correlationId) =>
        new(id, status, version, createdAt, updatedAt, createdBy, correlationId);

    /// <summary>
    /// Locks the application for review. Explicit state-machine guard — illegal
    /// transitions (e.g. submitting an already-submitted application) are rejected,
    /// never silently allowed through blind field assignment.
    /// </summary>
    public void Submit(DateTimeOffset now)
    {
        if (Status != ApplicationStatus.InProgress)
        {
            throw new ConflictException($"Cannot submit an application in status '{Status}'.");
        }
        Status = ApplicationStatus.Submitted;
        UpdatedAt = now;
        Version += 1;
    }
}
