using Gweb.Domain.Documents;
using Gweb.Shared.Clock;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;

namespace Gweb.Services.Documents;

public sealed record PresignedUploadResult(Document Document, PresignedUpload Upload);

/// <summary>
/// Presign + complete orchestration. Lambda never sees document bytes at any point --
/// presign hands the caller a direct-to-S3 upload target, complete re-fetches only
/// what's needed (metadata + a few leading bytes) to verify what actually landed.
/// </summary>
public sealed class DocumentService(IDocumentRepository repository, IDocumentStorage storage, IClock clock, TimeSpan presignTtl)
{
    public async Task<PresignedUploadResult> RequestPresignedUploadAsync(
        Guid applicationId,
        DocumentType type,
        string originalFilename,
        string contentType,
        long declaredSizeBytes,
        string declaredChecksumSha256Base64,
        string actor,
        string correlationId,
        DeadlineBudget budget,
        CancellationToken cancellationToken = default)
    {
        if (!AllowedContentTypes.IsAllowed(contentType))
        {
            throw new ValidationException($"Unsupported content type: {contentType}.");
        }
        if (declaredSizeBytes <= 0 || declaredSizeBytes > DocumentUploadLimits.MaxSizeBytes)
        {
            throw new ValidationException($"declaredSizeBytes must be between 1 and {DocumentUploadLimits.MaxSizeBytes}.");
        }
        if (string.IsNullOrWhiteSpace(declaredChecksumSha256Base64))
        {
            throw new ValidationException("declaredChecksumSha256Base64 is required.");
        }

        var now = DateTimeOffset.FromUnixTimeMilliseconds(clock.NowMs());
        var documentId = Guid.NewGuid();
        var sanitizedFilename = FilenameSanitizer.Sanitize(originalFilename);
        var s3Key = DocumentKeyGenerator.Generate(applicationId, documentId, contentType);

        var document = Document.CreateAndBeginUpload(
            documentId, applicationId, type, sanitizedFilename, contentType, declaredSizeBytes,
            s3Key, declaredChecksumSha256Base64, now, actor, correlationId);

        await repository.SaveAsync(document, expectedVersion: 0, budget, cancellationToken).ConfigureAwait(false);

        var upload = await storage.CreatePresignedUploadAsync(
            s3Key, contentType, declaredSizeBytes, declaredChecksumSha256Base64, presignTtl, budget, cancellationToken)
            .ConfigureAwait(false);

        return new PresignedUploadResult(document, upload);
    }

    /// <summary>
    /// Idempotent: once a document has left Uploading (Received, Rejected, or further
    /// along), a repeat call just returns the current record instead of re-verifying --
    /// required so a client retry after a dropped response never double-processes.
    /// </summary>
    public async Task<Document> CompleteUploadAsync(
        Guid applicationId,
        Guid documentId,
        DeadlineBudget budget,
        CancellationToken cancellationToken = default)
    {
        var document = await repository.GetByIdAsync(applicationId, documentId, budget, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException($"Document {documentId} not found.");

        if (document.Status != DocumentStatus.Uploading)
        {
            return document;
        }

        var uploaded = await storage.GetUploadedObjectAsync(document.S3Key, budget, cancellationToken).ConfigureAwait(false)
            ?? throw new ValidationException("No upload found at the presigned location yet.");

        var expectedVersion = document.Version;
        var now = DateTimeOffset.FromUnixTimeMilliseconds(clock.NowMs());

        // Defense in depth: the presigned POST already pinned checksum/size/content-type
        // as S3 conditions, so a mismatch here means either S3 checksum enforcement was
        // bypassed (e.g. a direct console PUT) or the declared metadata was wrong.
        var checksumMatches = uploaded.ChecksumSha256Base64 == document.DeclaredChecksumSha256;
        var sizeMatches = uploaded.SizeBytes == document.DeclaredSizeBytes;
        var signatureMatches = FileSignatureValidator.Matches(document.ContentType, uploaded.LeadingBytes);

        if (checksumMatches && sizeMatches && signatureMatches)
        {
            document.MarkReceived(uploaded.ChecksumSha256Base64!, uploaded.SizeBytes, now);
        }
        else
        {
            document.MarkRejected("Uploaded content does not match the declared checksum, size, or file type.", now);
        }

        await repository.SaveAsync(document, expectedVersion, budget, cancellationToken).ConfigureAwait(false);

        return document;
    }

    public async Task<Document> GetDocumentAsync(
        Guid applicationId, Guid documentId, DeadlineBudget budget, CancellationToken cancellationToken = default) =>
        await repository.GetByIdAsync(applicationId, documentId, budget, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException($"Document {documentId} not found.");

    /// <summary>
    /// Thin wrapper around the repository query SubmissionService already uses
    /// internally (see docs/adr/0008-submission-gate.md) -- exposed here too because a
    /// resumed session has no other way to discover which documents were already
    /// uploaded for this application (there was previously no client-facing "list
    /// documents" route at all, only "fetch one by ID" -- a real gap for the brief's
    /// "preserve state so a partially completed application can be resumed"
    /// requirement, closed while building the Phase 11 frontend that needed it).
    /// </summary>
    public Task<IReadOnlyList<Document>> ListDocumentsAsync(
        Guid applicationId, DeadlineBudget budget, CancellationToken cancellationToken = default) =>
        repository.ListByApplicationIdAsync(applicationId, budget, cancellationToken);
}
