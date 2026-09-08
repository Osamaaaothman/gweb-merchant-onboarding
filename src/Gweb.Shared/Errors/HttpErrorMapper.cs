using Gweb.Shared.Correlation;
using Gweb.Shared.Logging;
using Microsoft.AspNetCore.Http;

namespace Gweb.Shared.Errors;

public sealed record ApiErrorDetail(string Code, string Message, object? Details);

public sealed record ApiErrorBody(ApiErrorDetail Error, string? CorrelationId);

/// <summary>
/// The one place domain errors become HTTP responses. Endpoints never build error
/// bodies themselves. Never leaks stack traces, table/bucket names, or ARNs to the
/// client — those go to CloudWatch only, keyed by correlation ID.
/// </summary>
public static class HttpErrorMapper
{
    public static IResult Map(Exception error, StructuredLogger logger)
    {
        var correlationId = CorrelationScope.GetCurrent()?.CorrelationId;

        if (error is DomainException domainException)
        {
            var body = new ApiErrorBody(
                new ApiErrorDetail(domainException.Code, domainException.Message, domainException.Details),
                correlationId);
            return Results.Json(body, statusCode: domainException.HttpStatus);
        }

        logger.Error("unhandled_error", new { errorName = error.GetType().Name, errorMessage = error.Message });

        var fallbackBody = new ApiErrorBody(
            new ApiErrorDetail("INTERNAL_ERROR", "An unexpected error occurred.", null),
            correlationId);
        return Results.Json(fallbackBody, statusCode: 500);
    }
}
