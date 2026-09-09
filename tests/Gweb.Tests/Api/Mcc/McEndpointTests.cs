using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Gweb.Tests.Api.Mcc;

public class McEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task ReturnsMatchingCodesForAKnownDescriptionQuery()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/v1/mcc?query=grocery");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var results = body.GetProperty("results").EnumerateArray().ToList();
        Assert.Contains(results, r => r.GetProperty("code").GetString() == "5411");
    }

    [Fact]
    public async Task ReturnsTheExactCodeFirstWhenQueryingByCode()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/v1/mcc?query=6012");

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var first = body.GetProperty("results")[0];
        Assert.Equal("6012", first.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ReturnsABrowsingDefaultWhenNoQueryIsGiven()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/v1/mcc");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.True(body.GetProperty("results").GetArrayLength() > 0);
    }

    [Fact]
    public async Task RespectsAnExplicitLimit()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/v1/mcc?limit=2");

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(2, body.GetProperty("results").GetArrayLength());
    }

    [Fact]
    public async Task ReturnsEmptyResultsForAQueryThatMatchesNothing()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/v1/mcc?query=zzz-not-a-real-mcc-query-zzz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(0, body.GetProperty("results").GetArrayLength());
    }
}
