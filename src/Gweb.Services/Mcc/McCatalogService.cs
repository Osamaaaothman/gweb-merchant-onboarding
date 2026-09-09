using Gweb.Domain.Mcc;

namespace Gweb.Services.Mcc;

/// <summary>
/// Thin pass-through today, but a real seam: if the catalog ever moves to a
/// DynamoDB-backed IMccCatalog implementation (see docs/adr/0004-mcc-catalog-storage.md
/// for why it currently doesn't), caching/paging policy lands here without touching
/// the endpoint.
/// </summary>
public sealed class McCatalogService(IMccCatalog catalog)
{
    private const int DefaultLimit = 25;
    private const int MaxLimit = 100;

    public IReadOnlyList<MccCode> Search(string? query, int? limit)
    {
        var effectiveLimit = limit switch
        {
            null or <= 0 => DefaultLimit,
            > MaxLimit => MaxLimit,
            _ => limit.Value,
        };
        return catalog.Search(query, effectiveLimit);
    }

    public MccCode? GetByCode(string code) => catalog.GetByCode(code);
}
