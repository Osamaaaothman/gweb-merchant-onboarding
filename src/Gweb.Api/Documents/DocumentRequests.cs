using System.Text.Json.Serialization;
using Gweb.Domain.Documents;

namespace Gweb.Api.Documents;

// [JsonUnmappedMemberHandling(Disallow)] rejects unknown fields explicitly -- see
// ApplicantRequests.cs for the same convention.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PresignDocumentRequest(
    DocumentType Type,
    string OriginalFilename,
    string ContentType,
    long DeclaredSizeBytes,
    string DeclaredChecksumSha256Base64);
