using Gweb.Shared.Errors;

namespace Gweb.Domain.Documents;

/// <summary>
/// The extension/MIME allowlist from brief §4.1 ("Allow PDF, JPG/JPEG, and PNG at
/// minimum"). Single source of truth for validation (presign) and key generation
/// (the S3 key's extension comes from this map, never from the client-supplied
/// filename) so the two can never disagree.
/// </summary>
public static class AllowedContentTypes
{
    private static readonly Dictionary<string, string> ContentTypeToExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        ["application/pdf"] = ".pdf",
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
    };

    public static bool IsAllowed(string contentType) => ContentTypeToExtension.ContainsKey(contentType);

    public static string ExtensionFor(string contentType)
    {
        if (!ContentTypeToExtension.TryGetValue(contentType, out var extension))
        {
            throw new ValidationException($"Unsupported content type: {contentType}.");
        }
        return extension;
    }

    public static IReadOnlyCollection<string> AllContentTypes => ContentTypeToExtension.Keys;
}
