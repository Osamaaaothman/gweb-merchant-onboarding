using Gweb.Domain.Mcc;

namespace Gweb.Api.Mcc;

public sealed record McCodeResponse(string Code, string Description, string Category)
{
    public static McCodeResponse From(MccCode code) => new(code.Code, code.Description, code.Category);
}

public sealed record McSearchResponse(string? Query, IReadOnlyList<McCodeResponse> Results)
{
    public static McSearchResponse From(string? query, IReadOnlyList<MccCode> results) =>
        new(query, [.. results.Select(McCodeResponse.From)]);
}
