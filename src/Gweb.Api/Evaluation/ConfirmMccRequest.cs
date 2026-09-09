using System.Text.Json.Serialization;

namespace Gweb.Api.Evaluation;

// [JsonUnmappedMemberHandling(Disallow)] rejects unknown fields explicitly -- see
// ApplicantRequests.cs for the same convention.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfirmMccRequest(string MccCode);
