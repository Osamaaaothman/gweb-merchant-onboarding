using Gweb.Shared.Deadline;

namespace Gweb.Domain.Documents;

/// <summary>A pre-signed direct-to-S3 upload -- Lambda never receives document bytes.</summary>
public sealed record PresignedUpload(string Url, IReadOnlyDictionary<string, string> Fields);

/// <summary>
/// What complete() needs to verify an upload: real size, S3's own recorded checksum
/// (present only if the object was uploaded with checksum enforcement -- see
/// S3DocumentStorage), and a handful of leading bytes for the file-signature check.
/// </summary>
public sealed record UploadedObject(long SizeBytes, string? ChecksumSha256Base64, byte[] LeadingBytes);

public interface IDocumentStorage
{
    /// <summary>
    /// Builds a pre-signed POST that pins Content-Type, a size range, and the
    /// declared SHA-256 checksum as policy conditions -- S3 itself rejects the
    /// upload if the actual bytes don't match, before this system ever sees it.
    /// </summary>
    Task<PresignedUpload> CreatePresignedUploadAsync(
        string s3Key,
        string contentType,
        long maxSizeBytes,
        string checksumSha256Base64,
        TimeSpan ttl,
        DeadlineBudget budget,
        CancellationToken cancellationToken = default);

    /// <summary>Null if no object exists at that key yet (upload not completed).</summary>
    Task<UploadedObject?> GetUploadedObjectAsync(string s3Key, DeadlineBudget budget, CancellationToken cancellationToken = default);

    /// <summary>
    /// Full object bytes -- unlike GetUploadedObjectAsync (16 leading bytes, for the
    /// file-signature check), this is for a caller that actually needs the content,
    /// e.g. EvaluationService sending a processing statement to a multimodal AI
    /// provider. Null if no object exists at that key. Implementations should cap the
    /// size they'll actually read (see S3DocumentStorage) rather than trust
    /// DeclaredSizeBytes blindly.
    /// </summary>
    Task<byte[]?> DownloadObjectAsync(string s3Key, DeadlineBudget budget, CancellationToken cancellationToken = default);
}
