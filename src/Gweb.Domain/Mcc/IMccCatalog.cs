namespace Gweb.Domain.Mcc;

/// <summary>
/// No DeadlineBudget parameter here, unlike every repository/storage interface
/// elsewhere in this codebase -- deliberately. The catalog is packaged static data
/// loaded once at cold start (see docs/adr/0004-mcc-catalog-storage.md); a lookup
/// against it is an in-memory computation, not I/O, so there is nothing for a budget
/// to bound. Adding one anyway would be ceremony, not correctness. If a future
/// implementation moves the catalog to DynamoDB, this interface changes to match --
/// it should not pretend to be async today for a hypothetical it doesn't serve yet.
/// </summary>
public interface IMccCatalog
{
    /// <summary>
    /// Case-insensitive search across code and description. A null/empty query
    /// returns the first <paramref name="limit"/> codes ordered by code (a browsing
    /// default), not an empty result -- see StaticMccCatalog for the ranking rules
    /// when a query is present.
    /// </summary>
    IReadOnlyList<MccCode> Search(string? query, int limit);

    MccCode? GetByCode(string code);
}
