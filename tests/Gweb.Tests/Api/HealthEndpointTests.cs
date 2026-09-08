using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Gweb.Tests.Api;

public class HealthEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task ReturnsOkWithAnOkStatus()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/v1/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("ok", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task EchoesBackTheCallerSuppliedCorrelationId()
    {
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/health");
        request.Headers.Add("x-correlation-id", "req-42");

        var response = await client.SendAsync(request);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("req-42", body.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task GeneratesACorrelationIdWhenTheCallerDoesNotSupplyOne()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/v1/health", UriKind.Relative));

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var correlationId = body.GetProperty("correlationId").GetString();
        Assert.False(string.IsNullOrEmpty(correlationId));
    }

    [Fact]
    public async Task ReportsARemainingBudgetGreaterThanZero()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/v1/health", UriKind.Relative));

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.True(body.GetProperty("remainingBudgetMs").GetInt64() > 0);
    }
}
