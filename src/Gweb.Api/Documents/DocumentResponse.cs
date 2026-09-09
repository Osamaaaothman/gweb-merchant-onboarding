using Gweb.Domain.Documents;
using Gweb.Services.Documents;

namespace Gweb.Api.Documents;

/// <summary>
/// Never carries the S3 key (an internal storage detail, not something a client needs
/// or should be able to guess-adjacent-from) or the declared/actual checksum in full --
/// checksums aren't secret, but there's no client use case for them post-upload, so they
/// stay out of the response surface rather than being exposed "just in case".
/// </summary>
public sealed record DocumentResponse(
    Guid Id,
    Guid ApplicationId,
    DocumentType Type,
    DocumentStatus Status,
    string OriginalFilename,
    string ContentType,
    long DeclaredSizeBytes,
    long? ActualSizeBytes,
    DateTimeOffset? UploadedAt,
    string? RejectionReason,
    long Version)
{
    public static DocumentResponse From(Document document) => new(
        document.Id,
        document.ApplicationId,
        document.Type,
        document.Status,
        document.OriginalFilename,
        document.ContentType,
        document.DeclaredSizeBytes,
        document.ActualSizeBytes,
        document.UploadedAt,
        document.RejectionReason,
        document.Version);
}

public sealed record PresignedUploadResponse(DocumentResponse Document, string UploadUrl, IReadOnlyDictionary<string, string> UploadFields)
{
    public static PresignedUploadResponse From(PresignedUploadResult result) =>
        new(DocumentResponse.From(result.Document), result.Upload.Url, result.Upload.Fields);
}
