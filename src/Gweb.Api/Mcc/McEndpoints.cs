using Gweb.Config;
using Gweb.Services.Mcc;
using Gweb.Shared.Clock;
using Gweb.Shared.Logging;

namespace Gweb.Api.Mcc;

internal static class McEndpoints
{
    public static void MapMcEndpoints(this WebApplication app)
    {
        app.MapGet("/v1/mcc", SearchAsync);
    }

    private static Task<IResult> SearchAsync(
        HttpContext httpContext,
        string? query,
        int? limit,
        McCatalogService catalogService,
        IClock clock,
        BaseConfig config,
        StructuredLogger logger) =>
        RequestExecution.RunAsync(httpContext, "search_mcc", clock, config, logger, budget =>
        {
            // Purely in-memory (see IMccCatalog's doc comment) -- budget is unused
            // here, not threaded through, because there is nothing in this call that
            // can time out.
            _ = budget;

            var results = catalogService.Search(query, limit);
            return Task.FromResult(Results.Ok(McSearchResponse.From(query, results)));
        });
}
