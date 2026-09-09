using Gweb.Domain.Applications;

namespace Gweb.Domain.Evaluation;

/// <summary>
/// The only business data an evaluation provider ever sees -- deliberately excludes
/// everything about the Applicant (name, DOB, government ID, address) and every
/// Business field that isn't relevant to what the business *does*. PII minimization
/// by construction: there is no field here to accidentally leak, not a filter applied
/// at call time. See docs/adr/0006-ai-evaluation-provider.md.
/// </summary>
public sealed record BusinessProfileInput(
    string? LegalBusinessName,
    string? BusinessDescription,
    string? WebsiteUrl,
    EntityType? EntityType);
