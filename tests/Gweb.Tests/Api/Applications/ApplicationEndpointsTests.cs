using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Gweb.Tests.Api.Applications;

public class CreateApplicationEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task ReturnsCreatedWithAnInProgressApplication()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsync(new Uri("/v1/applications", UriKind.Relative), content: null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("InProgress", body.GetProperty("status").GetString());
        Assert.Equal(1, body.GetProperty("version").GetInt64());
    }

    [Fact]
    public async Task SetsALocationHeaderPointingAtTheNewApplication()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsync(new Uri("/v1/applications", UriKind.Relative), content: null);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var id = body.GetProperty("id").GetGuid();
        Assert.Equal($"/v1/applications/{id}", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task GeneratesADifferentApplicationIdOnEachCall()
    {
        var client = factory.CreateClient();

        var first = await client.PostAsync(new Uri("/v1/applications", UriKind.Relative), content: null);
        var second = await client.PostAsync(new Uri("/v1/applications", UriKind.Relative), content: null);

        var firstId = JsonDocument.Parse(await first.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        var secondId = JsonDocument.Parse(await second.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        Assert.NotEqual(firstId, secondId);
    }
}

public class GetApplicationEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task ResumesAPreviouslyCreatedApplicationById()
    {
        var client = factory.CreateClient();
        var createResponse = await client.PostAsync(new Uri("/v1/applications", UriKind.Relative), content: null);
        var createdId = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        var getResponse = await client.GetAsync(new Uri($"/v1/applications/{createdId}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var body = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(createdId, body.GetProperty("id").GetGuid());
        Assert.Equal("InProgress", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task ReturnsNotFoundForAWellFormedButUnknownId()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri($"/v1/applications/{Guid.NewGuid()}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("NOT_FOUND", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task ReturnsBadRequestForAMalformedId()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/v1/applications/not-a-guid", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("VALIDATION_FAILED", body.GetProperty("error").GetProperty("code").GetString());
    }
}
