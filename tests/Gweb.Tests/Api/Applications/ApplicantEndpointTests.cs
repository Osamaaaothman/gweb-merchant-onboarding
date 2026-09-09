using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Gweb.Tests.Api.Applications;

public class PatchApplicantEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static async Task<Guid> CreateApplicationAsync(HttpClient client)
    {
        var response = await client.PostAsync(new Uri("/v1/applications", UriKind.Relative), content: null);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return body.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task AcceptsAValidPatchAndReturnsTheMergedApplicant()
    {
        var client = factory.CreateClient();
        var applicationId = await CreateApplicationAsync(client);

        var response = await client.PatchAsJsonAsync(
            $"/v1/applications/{applicationId}/applicant",
            new { legalFirstName = "Jane", legalLastName = "Testerson", roleTitle = "CEO" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Jane", body.GetProperty("legalFirstName").GetString());
        Assert.Equal("Testerson", body.GetProperty("legalLastName").GetString());
    }

    [Fact]
    public async Task MasksTheGovernmentIdInTheResponse()
    {
        var client = factory.CreateClient();
        var applicationId = await CreateApplicationAsync(client);

        var response = await client.PatchAsJsonAsync(
            $"/v1/applications/{applicationId}/applicant",
            new { governmentId = new { type = "Passport", number = "X1234567" } });

        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("X1234567", raw);
        var body = JsonDocument.Parse(raw).RootElement;
        Assert.Equal("4567", body.GetProperty("governmentId").GetProperty("last4").GetString());
    }

    [Fact]
    public async Task ReturnsBadRequestWithFieldErrorsForAMalformedEmail()
    {
        var client = factory.CreateClient();
        var applicationId = await CreateApplicationAsync(client);

        var response = await client.PatchAsJsonAsync($"/v1/applications/{applicationId}/applicant", new { email = "not-an-email" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("VALIDATION_FAILED", body.GetProperty("error").GetProperty("code").GetString());
        var details = body.GetProperty("error").GetProperty("details");
        Assert.Contains(details.EnumerateArray(), e => e.GetProperty("field").GetString() == "email");
    }

    [Fact]
    public async Task ReturnsNotFoundWhenTheApplicationDoesNotExist()
    {
        var client = factory.CreateClient();

        var response = await client.PatchAsJsonAsync($"/v1/applications/{Guid.NewGuid()}/applicant", new { legalFirstName = "Jane" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReturnsBadRequestForAnUnknownField()
    {
        var client = factory.CreateClient();
        var applicationId = await CreateApplicationAsync(client);

        var response = await client.PatchAsJsonAsync($"/v1/applications/{applicationId}/applicant", new { notARealField = "x" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReturnsBadRequestForMalformedJsonRatherThanACrash()
    {
        var client = factory.CreateClient();
        var applicationId = await CreateApplicationAsync(client);
        using var content = new StringContent("{ not valid json", Encoding.UTF8, "application/json");

        var response = await client.PatchAsync($"/v1/applications/{applicationId}/applicant", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SecondPatchMergesOntoTheFirstThroughRealHttpCalls()
    {
        var client = factory.CreateClient();
        var applicationId = await CreateApplicationAsync(client);
        await client.PatchAsJsonAsync($"/v1/applications/{applicationId}/applicant", new { legalFirstName = "Jane" });

        var response = await client.PatchAsJsonAsync($"/v1/applications/{applicationId}/applicant", new { legalLastName = "Testerson" });

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Jane", body.GetProperty("legalFirstName").GetString());
        Assert.Equal("Testerson", body.GetProperty("legalLastName").GetString());
    }
}
