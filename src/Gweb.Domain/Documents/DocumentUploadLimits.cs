namespace Gweb.Domain.Documents;

/// <summary>
/// Named upload-size ceiling per docs/02-ENGINEERING-STANDARDS.md §"magic numbers
/// become named constants" -- the brief does not specify a number, so this is a
/// deliberate, documented choice (generous enough for a scanned PDF or phone photo,
/// bounded so a single upload can never approach Lambda's own payload/memory limits).
/// </summary>
public static class DocumentUploadLimits
{
    public const long MaxSizeBytes = 25_000_000;
}
