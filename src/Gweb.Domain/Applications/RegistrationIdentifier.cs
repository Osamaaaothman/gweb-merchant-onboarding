using Gweb.Shared.Errors;

namespace Gweb.Domain.Applications;

public enum RegistrationIdentifierType
{
    Ein,
    StateUbi,
    Other,
}

/// <summary>
/// Masked at capture, same discipline as GovernmentIdentification: the full EIN/UBI
/// value is never stored, only a masked representation like "**-***4821" (matching
/// docs/04-SECURITY-RULES.md §2's masking table for Tax ID / EIN).
/// </summary>
public sealed record RegistrationIdentifier
{
    public RegistrationIdentifierType Type { get; }
    public string MaskedValue { get; }

    private RegistrationIdentifier(RegistrationIdentifierType type, string maskedValue)
    {
        Type = type;
        MaskedValue = maskedValue;
    }

    public static RegistrationIdentifier FromFullValue(RegistrationIdentifierType type, string fullValue)
    {
        var trimmed = fullValue.Trim();
        if (trimmed.Length < 4)
        {
            throw new ValidationException("registrationIdentifier.value must be at least 4 characters.");
        }
        var last4 = trimmed[^4..];
        return new RegistrationIdentifier(type, $"**-***{last4}");
    }

    public static RegistrationIdentifier FromMaskedValue(RegistrationIdentifierType type, string maskedValue) =>
        new(type, maskedValue);
}
