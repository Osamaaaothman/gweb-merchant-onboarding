namespace Gweb.Domain.Documents;

/// <summary>
/// The non-guessable S3 key pattern from docs/03-ARCHITECTURE-RULES.md §3:
/// applications/{applicationId}/documents/{documentId}/{uuid}{ext} -- never a raw
/// filename, never a user-controlled path segment. The extension comes from
/// AllowedContentTypes (server-decided), never from the client-supplied filename.
/// </summary>
public static class DocumentKeyGenerator
{
    public static string Generate(Guid applicationId, Guid documentId, string contentType)
    {
        var extension = AllowedContentTypes.ExtensionFor(contentType);
        return $"applications/{applicationId}/documents/{documentId}/{Guid.NewGuid()}{extension}";
    }
}
