using Gweb.Shared.Errors;

namespace Gweb.Domain.Applications;

/// <summary>
/// Masked at capture: the full identification number is never stored anywhere, not
/// even transiently in a field that a redaction bug could later fail to catch. Only
/// the type and last 4 characters survive construction. See
/// docs/04-SECURITY-RULES.md §2's masking table.
/// </summary>
public sealed record GovernmentIdentification
{
    public GovernmentIdentificationType Type { get; }
    public string Last4 { get; }

    private GovernmentIdentification(GovernmentIdentificationType type, string last4)
    {
        Type = type;
        Last4 = last4;
    }

    /// <summary>
    /// Takes the full, unmasked identification number as submitted by the client and
    /// immediately discards everything except the last 4 characters. This is the one
    /// and only place in the codebase that ever sees the full value.
    /// </summary>
    public static GovernmentIdentification FromFullNumber(GovernmentIdentificationType type, string fullNumber)
    {
        var trimmed = fullNumber.Trim();
        if (trimmed.Length < 4)
        {
            throw new ValidationException("governmentId.number must be at least 4 characters.");
        }
        return new GovernmentIdentification(type, trimmed[^4..]);
    }

    /// <summary>Reconstructs from an already-masked, already-persisted last4 value.</summary>
    public static GovernmentIdentification FromMaskedLast4(GovernmentIdentificationType type, string last4) =>
        new(type, last4);
}
