using System.Text.Json;
using Gweb.Shared.Correlation;
using Gweb.Shared.Errors;
using Gweb.Shared.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Gweb.Tests.Shared.Errors;

public class HttpErrorMapperTests
{
    private static StructuredLogger NullLogger() => new(TextWriter.Null);

    // Results.Json(...) pulls IOptions<JsonOptions> from HttpContext.RequestServices at
    // execution time — a bare DefaultHttpContext has no RequestServices, so it needs a
    // minimal service provider wired up before ExecuteAsync can run.
    private static ServiceProvider CreateRequestServices()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddLogging();
        services.AddSingleton(Options.Create(new JsonOptions()));
        return services.BuildServiceProvider();
    }

    private static async Task<(int statusCode, JsonElement body)> Invoke(IResult result)
    {
        var httpContext = new DefaultHttpContext { RequestServices = CreateRequestServices() };
        httpContext.Response.Body = new MemoryStream();
        await result.ExecuteAsync(httpContext);
        httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await JsonDocument.ParseAsync(httpContext.Response.Body);
        return (httpContext.Response.StatusCode, body.RootElement.Clone());
    }

    [Fact]
    public async Task MapsAValidationExceptionTo400WithItsCodeAndMessage()
    {
        var (status, body) = await Invoke(HttpErrorMapper.Map(new ValidationException("email is required"), NullLogger()));

        Assert.Equal(400, status);
        Assert.Equal("VALIDATION_FAILED", body.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("email is required", body.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task MapsAConflictExceptionTo409()
    {
        var (status, body) = await Invoke(HttpErrorMapper.Map(new ConflictException("version mismatch"), NullLogger()));

        Assert.Equal(409, status);
        Assert.Equal("CONFLICT", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task MapsADependencyTimeoutExceptionTo504()
    {
        var (status, body) = await Invoke(HttpErrorMapper.Map(new DependencyTimeoutException("AI provider timed out"), NullLogger()));

        Assert.Equal(504, status);
        Assert.Equal("DEPENDENCY_TIMEOUT", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task MapsANotFoundExceptionTo404()
    {
        var (status, body) = await Invoke(HttpErrorMapper.Map(new NotFoundException("application not found"), NullLogger()));

        Assert.Equal(404, status);
        Assert.Equal("NOT_FOUND", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task MapsADependencyUnavailableExceptionTo503()
    {
        var (status, body) = await Invoke(HttpErrorMapper.Map(new DependencyUnavailableException("DynamoDB unreachable"), NullLogger()));

        Assert.Equal(503, status);
        Assert.Equal("DEPENDENCY_UNAVAILABLE", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task MapsAPolicyViolationExceptionTo422()
    {
        var (status, body) = await Invoke(HttpErrorMapper.Map(new PolicyViolationException("MCC 6051 is restricted"), NullLogger()));

        Assert.Equal(422, status);
        Assert.Equal("POLICY_VIOLATION", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void MarksDependencyExceptionsAsRetryableAndTheRestAsNot()
    {
        Assert.True(new DependencyTimeoutException("x").Retryable);
        Assert.True(new DependencyUnavailableException("x").Retryable);
        Assert.False(new ValidationException("x").Retryable);
        Assert.False(new ConflictException("x").Retryable);
        Assert.False(new NotFoundException("x").Retryable);
        Assert.False(new PolicyViolationException("x").Retryable);
    }

    [Fact]
    public async Task IncludesTheDetailsPayloadWhenTheExceptionCarriesOne()
    {
        var details = new[] { new { field = "dob", reason = "must be in the past" } };

        var (_, body) = await Invoke(HttpErrorMapper.Map(new ValidationException("invalid fields", details), NullLogger()));

        var detailsElement = body.GetProperty("error").GetProperty("details");
        Assert.Equal("dob", detailsElement[0].GetProperty("field").GetString());
    }

    [Fact]
    public async Task CarriesTheActiveCorrelationId()
    {
        var (_, body) = await CorrelationScope.Run(
            new CorrelationContext("corr-abc", "test"),
            () => Invoke(HttpErrorMapper.Map(new ValidationException("bad input"), NullLogger())));

        Assert.Equal("corr-abc", body.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task MapsAnUnrecognizedExceptionTo500WithoutLeakingItsMessage()
    {
        var (status, body) = await Invoke(
            HttpErrorMapper.Map(new InvalidOperationException("connection string: postgres://user:pw@host/db"), NullLogger()));

        Assert.Equal(500, status);
        Assert.Equal("INTERNAL_ERROR", body.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain("postgres://user:pw@host/db", body.GetRawText());
    }
}
