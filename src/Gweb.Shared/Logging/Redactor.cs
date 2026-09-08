using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Gweb.Shared.Logging;

/// <summary>
/// Every log line in this codebase must route through Redact() before it reaches
/// stdout. See docs/04-SECURITY-RULES.md §2 for the authoritative list of what must
/// never appear in logs.
/// </summary>
public static partial class Redactor
{
    private const string Redacted = "[REDACTED]";

    private static readonly Regex[] SensitiveKeyPatterns =
    [
        GovernmentIdRegex(), SsnRegex(), TaxIdRegex(), EinRegex(), DateOfBirthRegex(), DobRegex(),
        BankAccountRegex(), RoutingNumberRegex(), AccountNumberRegex(), SecretRegex(), TokenRegex(),
        PasswordRegex(), ApiKeyRegex(), PresignedUrlKeyRegex(), DocumentBodyRegex(), DocumentContentRegex(),
        PromptRegex(),
    ];

    // Matches an S3 pre-signed URL by its query-string signature, so a credential-bearing
    // URL is caught even when logged under an innocuous key name like "uploadUrl".
    [GeneratedRegex("X-Amz-Signature=|X-Amz-Credential=", RegexOptions.IgnoreCase)]
    private static partial Regex PresignedUrlValueRegex();

    /// <summary>
    /// Deep-clones <paramref name="node"/>, replacing any value under a sensitive key
    /// (or any string that looks like a pre-signed URL) with a fixed redaction marker.
    /// </summary>
    public static JsonNode? Redact(JsonNode? node)
    {
        switch (node)
        {
            case null:
                return null;
            case JsonValue value when value.TryGetValue(out string? s):
                return PresignedUrlValueRegex().IsMatch(s) ? JsonValue.Create(Redacted) : JsonValue.Create(s);
            case JsonValue:
                return node.DeepClone();
            case JsonArray array:
                var redactedArray = new JsonArray();
                foreach (var item in array)
                {
                    redactedArray.Add(Redact(item));
                }
                return redactedArray;
            case JsonObject obj:
                var redactedObj = new JsonObject();
                foreach (var (key, value) in obj)
                {
                    redactedObj[key] = IsSensitiveKey(key) ? JsonValue.Create(Redacted) : Redact(value);
                }
                return redactedObj;
            default:
                return node.DeepClone();
        }
    }

    /// <summary>Convenience overload: serializes an object, redacts it, returns the JsonNode.</summary>
    public static JsonNode? Redact<T>(T value)
    {
        var node = JsonSerializer.SerializeToNode(value);
        return Redact(node);
    }

    private static bool IsSensitiveKey(string key) => SensitiveKeyPatterns.Any(pattern => pattern.IsMatch(key));

    [GeneratedRegex("government.?id", RegexOptions.IgnoreCase)]
    private static partial Regex GovernmentIdRegex();

    [GeneratedRegex(@"\bssn\b", RegexOptions.IgnoreCase)]
    private static partial Regex SsnRegex();

    [GeneratedRegex("tax.?id", RegexOptions.IgnoreCase)]
    private static partial Regex TaxIdRegex();

    [GeneratedRegex(@"\bein\b", RegexOptions.IgnoreCase)]
    private static partial Regex EinRegex();

    [GeneratedRegex("date.?of.?birth", RegexOptions.IgnoreCase)]
    private static partial Regex DateOfBirthRegex();

    [GeneratedRegex(@"\bdob\b", RegexOptions.IgnoreCase)]
    private static partial Regex DobRegex();

    [GeneratedRegex("bank.?account", RegexOptions.IgnoreCase)]
    private static partial Regex BankAccountRegex();

    [GeneratedRegex("routing.?number", RegexOptions.IgnoreCase)]
    private static partial Regex RoutingNumberRegex();

    [GeneratedRegex("account.?number", RegexOptions.IgnoreCase)]
    private static partial Regex AccountNumberRegex();

    [GeneratedRegex(@"\bsecret", RegexOptions.IgnoreCase)]
    private static partial Regex SecretRegex();

    [GeneratedRegex(@"\btoken", RegexOptions.IgnoreCase)]
    private static partial Regex TokenRegex();

    [GeneratedRegex("password", RegexOptions.IgnoreCase)]
    private static partial Regex PasswordRegex();

    [GeneratedRegex("api.?key", RegexOptions.IgnoreCase)]
    private static partial Regex ApiKeyRegex();

    [GeneratedRegex("presigned.?url", RegexOptions.IgnoreCase)]
    private static partial Regex PresignedUrlKeyRegex();

    [GeneratedRegex("document.?body", RegexOptions.IgnoreCase)]
    private static partial Regex DocumentBodyRegex();

    [GeneratedRegex("document.?content", RegexOptions.IgnoreCase)]
    private static partial Regex DocumentContentRegex();

    [GeneratedRegex(@"\bprompt", RegexOptions.IgnoreCase)]
    private static partial Regex PromptRegex();
}
