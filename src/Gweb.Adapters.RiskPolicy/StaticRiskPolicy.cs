using System.Text.Json;
using System.Text.Json.Serialization;
using Gweb.Domain.RiskPolicy;

namespace Gweb.Adapters.RiskPolicy;

/// <summary>
/// Loads the packaged risk-policy document (an embedded JSON resource -- see
/// docs/adr/0005-risk-policy-representation.md) once at construction. Resolution
/// order per <see cref="Evaluate"/>: a provider-specific override for that exact MCC,
/// else the base rule for that MCC, else the document's own default level -- never a
/// hardcoded fallback buried in code, so the "no default risk level anywhere but this
/// one config value" invariant is easy to audit.
/// </summary>
public sealed class StaticRiskPolicy : IRiskPolicy
{
    private const string ResourceName = "Gweb.Adapters.RiskPolicy.Resources.risk-policy.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly RiskLevel _defaultLevel;
    private readonly Dictionary<string, RiskLevel> _baseRules;
    private readonly Dictionary<string, Dictionary<string, RiskLevel>> _providerOverrides;

    public StaticRiskPolicy()
    {
        var document = LoadFromEmbeddedResource();

        _defaultLevel = document.DefaultLevel;
        _baseRules = document.BaseRules.ToDictionary(r => r.MccCode, r => r.Level);
        _providerOverrides = document.ProviderOverrides.ToDictionary(
            provider => provider.Key,
            provider => provider.Value.ToDictionary(r => r.MccCode, r => r.Level));
    }

    public RiskLevel Evaluate(string mccCode, string? providerId = null)
    {
        if (providerId is not null
            && _providerOverrides.TryGetValue(providerId, out var overrides)
            && overrides.TryGetValue(mccCode, out var overriddenLevel))
        {
            return overriddenLevel;
        }

        return _baseRules.TryGetValue(mccCode, out var baseLevel) ? baseLevel : _defaultLevel;
    }

    private static PolicyDocument LoadFromEmbeddedResource()
    {
        var assembly = typeof(StaticRiskPolicy).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found.");
        var document = JsonSerializer.Deserialize<PolicyDocument>(stream, SerializerOptions)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' deserialized to null.");
        return document;
    }

    private sealed record PolicyDocument(
        RiskLevel DefaultLevel,
        IReadOnlyList<PolicyRule> BaseRules,
        IReadOnlyDictionary<string, IReadOnlyList<PolicyRule>> ProviderOverrides);

    private sealed record PolicyRule(string MccCode, RiskLevel Level, string Reason);
}
