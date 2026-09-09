using System.Text.Json;
using Gweb.Domain.Mcc;

namespace Gweb.Adapters.Mcc;

/// <summary>
/// Loads the packaged MCC catalog (an embedded JSON resource, regenerated from
/// tools/McCatalogImport -- see docs/adr/0004-mcc-catalog-storage.md) once at
/// construction and serves every lookup from memory. No AWS resource, no I/O per
/// request -- the catalog is read-only reference data, not per-application state.
/// </summary>
public sealed class StaticMccCatalog : IMccCatalog
{
    private const string ResourceName = "Gweb.Adapters.Mcc.Resources.mcc-codes.json";

    private readonly IReadOnlyList<MccCode> _allCodesByCode;
    private readonly Dictionary<string, MccCode> _byCode;

    public StaticMccCatalog()
    {
        var codes = LoadFromEmbeddedResource();
        _allCodesByCode = [.. codes.OrderBy(c => c.Code, StringComparer.Ordinal)];
        _byCode = _allCodesByCode.ToDictionary(c => c.Code);
    }

    public MccCode? GetByCode(string code) => _byCode.GetValueOrDefault(code);

    /// <summary>
    /// Empty/whitespace query -> first <paramref name="limit"/> codes, ordered by
    /// code (a sensible browsing default for an empty search box). Otherwise, ranked:
    /// exact code match, then code-starts-with, then description-contains -- each
    /// group ordered by code -- so typing "60" surfaces 6010/6011/6012 before an
    /// unrelated code whose description happens to contain "60".
    /// </summary>
    public IReadOnlyList<MccCode> Search(string? query, int limit)
    {
        if (limit <= 0)
        {
            return [];
        }
        var trimmed = query?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return [.. _allCodesByCode.Take(limit)];
        }

        var exact = new List<MccCode>();
        var codePrefix = new List<MccCode>();
        var descriptionMatch = new List<MccCode>();

        foreach (var entry in _allCodesByCode)
        {
            if (entry.Code == trimmed)
            {
                exact.Add(entry);
            }
            else if (entry.Code.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase))
            {
                codePrefix.Add(entry);
            }
            else if (entry.Description.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            {
                descriptionMatch.Add(entry);
            }
        }

        return [.. exact.Concat(codePrefix).Concat(descriptionMatch).Take(limit)];
    }

    private static List<MccCode> LoadFromEmbeddedResource()
    {
        var assembly = typeof(StaticMccCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found.");
        var codes = JsonSerializer.Deserialize<List<MccCode>>(stream)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' deserialized to null.");
        return codes;
    }
}
