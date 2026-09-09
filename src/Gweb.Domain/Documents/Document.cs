using Gweb.Shared.Errors;

namespace Gweb.Domain.Documents;

/// <summary>
/// Document metadata only -- the bytes live in S3 (see IDocumentStorage), this is the
/// DynamoDB-persisted record of what was requested, its lifecycle, and enough to
/// verify integrity. Explicit state machine per brief §4.1's exact lifecycle:
/// REQUESTED -> UPLOADING -> RECEIVED -> PROCESSING -> ACCEPTED | NEEDS_REVIEW | REJECTED.
/// </summary>
public sealed class Document
{
    public Guid Id { get; }
    public Guid ApplicationId { get; }
    public DocumentType Type { get; }
    public DocumentStatus Status { get; private set; }

    /// <summary>Sanitized display name only -- never used to build the S3 key.</summary>
    public string OriginalFilename { get; }
    public string ContentType { get; }
    public long DeclaredSizeBytes { get; }

    /// <summary>Non-guessable, server-generated -- see DocumentKeyGenerator.</summary>
    public string S3Key { get; }

    /// <summary>Client-declared at presign time; verified against the real upload at complete time.</summary>
    public string DeclaredChecksumSha256 { get; }

    public long? ActualSizeBytes { get; private set; }
    public string? ActualChecksumSha256 { get; private set; }
    public DateTimeOffset? UploadedAt { get; private set; }
    public string? RejectionReason { get; private set; }

    public long Version { get; private set; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public string CreatedBy { get; }
    public string CorrelationId { get; }

    private Document(
        Guid id,
        Guid applicationId,
        DocumentType type,
        DocumentStatus status,
        string originalFilename,
        string contentType,
        long declaredSizeBytes,
        string s3Key,
        string declaredChecksumSha256,
        long? actualSizeBytes,
        string? actualChecksumSha256,
        DateTimeOffset? uploadedAt,
        string? rejectionReason,
        long version,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        string createdBy,
        string correlationId)
    {
        Id = id;
        ApplicationId = applicationId;
        Type = type;
        Status = status;
        OriginalFilename = originalFilename;
        ContentType = contentType;
        DeclaredSizeBytes = declaredSizeBytes;
        S3Key = s3Key;
        DeclaredChecksumSha256 = declaredChecksumSha256;
        ActualSizeBytes = actualSizeBytes;
        ActualChecksumSha256 = actualChecksumSha256;
        UploadedAt = uploadedAt;
        RejectionReason = rejectionReason;
        Version = version;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        CreatedBy = createdBy;
        CorrelationId = correlationId;
    }

    /// <summary>
    /// Creates the record at REQUESTED, then immediately transitions it to UPLOADING
    /// -- both happen within the same presign request, but going through the guarded
    /// transition (rather than constructing directly into UPLOADING) keeps the state
    /// machine real and means an illegal-transition bug here would actually be caught
    /// by a test, not just asserted away by construction.
    /// </summary>
    public static Document CreateAndBeginUpload(
        Guid id,
        Guid applicationId,
        DocumentType type,
        string originalFilename,
        string contentType,
        long declaredSizeBytes,
        string s3Key,
        string declaredChecksumSha256,
        DateTimeOffset now,
        string createdBy,
        string correlationId)
    {
        var document = new Document(
            id, applicationId, type, DocumentStatus.Requested, originalFilename, contentType, declaredSizeBytes,
            s3Key, declaredChecksumSha256, null, null, null, null, version: 0, now, now, createdBy, correlationId);
        document.MarkUploadInitiated(now);
        return document;
    }

    public static Document Rehydrate(
        Guid id,
        Guid applicationId,
        DocumentType type,
        DocumentStatus status,
        string originalFilename,
        string contentType,
        long declaredSizeBytes,
        string s3Key,
        string declaredChecksumSha256,
        long? actualSizeBytes,
        string? actualChecksumSha256,
        DateTimeOffset? uploadedAt,
        string? rejectionReason,
        long version,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        string createdBy,
        string correlationId) =>
        new(id, applicationId, type, status, originalFilename, contentType, declaredSizeBytes, s3Key,
            declaredChecksumSha256, actualSizeBytes, actualChecksumSha256, uploadedAt, rejectionReason,
            version, createdAt, updatedAt, createdBy, correlationId);

    /// <summary>An independent copy -- see Applicant.Snapshot() for why an in-memory
    /// repository needs this.</summary>
    public Document Snapshot() => Rehydrate(
        Id, ApplicationId, Type, Status, OriginalFilename, ContentType, DeclaredSizeBytes, S3Key,
        DeclaredChecksumSha256, ActualSizeBytes, ActualChecksumSha256, UploadedAt, RejectionReason,
        Version, CreatedAt, UpdatedAt, CreatedBy, CorrelationId);

    private void MarkUploadInitiated(DateTimeOffset now)
    {
        RequireStatus(DocumentStatus.Requested);
        Status = DocumentStatus.Uploading;
        Touch(now);
    }

    /// <summary>
    /// Called after the caller has independently verified (against S3) that the
    /// upload matches what was declared. Idempotent: calling again with the exact
    /// same checksum is a no-op, not a version bump -- required so `complete` can be
    /// safely retried. A different checksum on a second call is a real conflict, not
    /// a retry, and is rejected.
    /// </summary>
    public void MarkReceived(string actualChecksumSha256, long actualSizeBytes, DateTimeOffset now)
    {
        if (Status == DocumentStatus.Received && ActualChecksumSha256 == actualChecksumSha256)
        {
            return;
        }
        RequireStatus(DocumentStatus.Uploading);
        ActualChecksumSha256 = actualChecksumSha256;
        ActualSizeBytes = actualSizeBytes;
        UploadedAt = now;
        Status = DocumentStatus.Received;
        Touch(now);
    }

    /// <summary>Verification failed (checksum mismatch or bad file signature) --
    /// terminal, not retryable under the same document record.</summary>
    public void MarkRejected(string reason, DateTimeOffset now)
    {
        if (Status == DocumentStatus.Rejected && RejectionReason == reason)
        {
            return;
        }
        if (Status is not (DocumentStatus.Uploading or DocumentStatus.Received or DocumentStatus.Processing))
        {
            throw new ConflictException($"Cannot reject a document in status '{Status}'.");
        }
        RejectionReason = reason;
        Status = DocumentStatus.Rejected;
        Touch(now);
    }

    public void MarkProcessing(DateTimeOffset now)
    {
        RequireStatus(DocumentStatus.Received);
        Status = DocumentStatus.Processing;
        Touch(now);
    }

    public void MarkAccepted(DateTimeOffset now)
    {
        if (Status is not (DocumentStatus.Received or DocumentStatus.Processing))
        {
            throw new ConflictException($"Cannot accept a document in status '{Status}'.");
        }
        Status = DocumentStatus.Accepted;
        Touch(now);
    }

    public void MarkNeedsReview(DateTimeOffset now)
    {
        if (Status is not (DocumentStatus.Received or DocumentStatus.Processing))
        {
            throw new ConflictException($"Cannot flag for review a document in status '{Status}'.");
        }
        Status = DocumentStatus.NeedsReview;
        Touch(now);
    }

    private void RequireStatus(DocumentStatus required)
    {
        if (Status != required)
        {
            throw new ConflictException($"Expected document status '{required}' but was '{Status}'.");
        }
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version += 1;
    }
}
