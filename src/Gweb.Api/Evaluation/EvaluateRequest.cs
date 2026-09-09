using System.Text.Json.Serialization;

namespace Gweb.Api.Evaluation;

// [JsonUnmappedMemberHandling(Disallow)] rejects unknown fields explicitly -- see
// ApplicantRequests.cs for the same convention. A body is always required (even an
// empty "{}") -- processingStatementDocumentId is the only field, and it's optional
// within the body, not the body itself.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EvaluateRequest(Guid? ProcessingStatementDocumentId = null);
