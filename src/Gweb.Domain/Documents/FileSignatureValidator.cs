namespace Gweb.Domain.Documents;

/// <summary>
/// File signature (magic bytes) check per docs/04-SECURITY-RULES.md §3 -- "a .pdf
/// extension proves nothing." Pure function: given the content type the client
/// declared and the first few bytes actually stored in S3, confirms they agree.
/// </summary>
public static class FileSignatureValidator
{
    public static bool Matches(string contentType, ReadOnlySpan<byte> leadingBytes)
    {
        return contentType switch
        {
            "application/pdf" => StartsWith(leadingBytes, "%PDF"u8),
            "image/jpeg" => leadingBytes.Length >= 3 && leadingBytes[0] == 0xFF && leadingBytes[1] == 0xD8 && leadingBytes[2] == 0xFF,
            "image/png" => StartsWith(leadingBytes, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
            _ => false,
        };
    }

    private static bool StartsWith(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> signature) =>
        haystack.Length >= signature.Length && haystack[..signature.Length].SequenceEqual(signature);
}
