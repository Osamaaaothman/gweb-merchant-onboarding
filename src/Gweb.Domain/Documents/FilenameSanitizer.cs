using System.Text;

namespace Gweb.Domain.Documents;

/// <summary>
/// Sanitizes a client-supplied filename for display/metadata purposes only -- per
/// docs/03-ARCHITECTURE-RULES.md §3, it must never be used to build the S3 key (see
/// DocumentKeyGenerator, which never touches this value at all).
/// </summary>
public static class FilenameSanitizer
{
    private const int MaxLength = 200;

    public static string Sanitize(string original)
    {
        // Strip path separators and any directory traversal component -- keep only
        // the final path segment, exactly as a browser's <input type="file"> would
        // report it, but defensively in case a client sends something else.
        var lastSegment = original.Replace('\\', '/').Split('/').Last();

        var builder = new StringBuilder(Math.Min(lastSegment.Length, MaxLength));
        foreach (var c in lastSegment)
        {
            if (char.IsControl(c))
            {
                continue;
            }
            builder.Append(c);
            if (builder.Length >= MaxLength)
            {
                break;
            }
        }

        var sanitized = builder.ToString().Trim();
        return sanitized.Length == 0 ? "document" : sanitized;
    }
}
