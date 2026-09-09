using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Gweb.Tests.Api.Evaluation;

public class ClassificationEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static async Task<Guid> CreateApplicationAsync(HttpClient client)
    {
        var response = await client.PostAsync(new Uri("/v1/applications", UriKind.Relative), content: null);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return body.GetProperty("id").GetGuid();
    }

    private static async Task FillInBusinessAsync(HttpClient client, Guid applicationId, string description = "A neighborhood grocery store selling fresh produce and dairy.")
    {
        await client.PatchAsJsonAsync(
            $"/v1/applications/{applicationId}/business",
            new { legalBusinessName = "Fresh Valley Grocers", entityType = "Llc", businessDescription = description });
    }

    [Fact]
    public async Task ClassifyReturnsCandidatesLabelledAsTheMockProvider()
    {
        var client = factory.CreateClient();
        var applicationId = await CreateApplicationAsync(client);
        await FillInBusinessAsync(client, applicationId);

        var response = await client.PostAsync($"/v1/applications/{applicationId}/classify", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("mock", body.GetProperty("proposedProvider").GetString());
        Assert.True(body.GetProperty("candidates").GetArrayLength() > 0);
    }

    [Fact]
    public async Task ClassifyReturnsBadRequestWhenBusinessHasNotBeenFilledInYet()
    {
        var client = factory.CreateClient();
        var applicationId = await CreateApplicationAsync(client);

        var response = await client.PostAsync($"/v1/applications/{applicationId}/classify", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ClassifyReturnsNotFoundForAnUnknownApplication()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsync($"/v1/applications/{Guid.NewGuid()}/classify", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ConfirmPersistsTheApplicantsSelfSelectedCode()
    {
        var client = factory.CreateClient();
        var applicationId = await CreateApplicationAsync(client);
        await FillInBusinessAsync(client, applicationId);

        var response = await client.PostAsJsonAsync($"/v1/applications/{applicationId}/classify/confirm", new { mccCode = "5411" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("5411", body.GetProperty("selfSelectedMccCode").GetString());
    }

    [Fact]
    public async Task ConfirmRejectsAnMccCodeThatDoesNotExistInTheCatalog()
    {
        var client = factory.CreateClient();
        var applicationId = await CreateApplicationAsync(client);

        var response = await client.PostAsJsonAsync($"/v1/applications/{applicationId}/classify/confirm", new { mccCode = "0000" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ClassifyThenConfirmADifferentCodeProducesAMismatch()
    {
        var client = factory.CreateClient();
        var applicationId = await CreateApplicationAsync(client);
        await FillInBusinessAsync(client, applicationId);
        var classifyBody = JsonDocument.Parse(await (await client.PostAsync($"/v1/applications/{applicationId}/classify", content: null)).Content.ReadAsStringAsync()).RootElement;
        var proposed = classifyBody.GetProperty("proposedMccCode").GetString()!;
        var differentCode = proposed == "5411" ? "7995" : "5411"; // a definitely-different real code

        var response = await client.PostAsJsonAsync($"/v1/applications/{applicationId}/classify/confirm", new { mccCode = differentCode });

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(proposed, body.GetProperty("proposedMccCode").GetString());
        Assert.Equal(differentCode, body.GetProperty("selfSelectedMccCode").GetString());
        Assert.True(body.GetProperty("hasMismatch").GetBoolean());
    }

    [Fact]
    public async Task GetReturnsNotFoundWhenNoClassificationExistsYet()
    {
        var client = factory.CreateClient();
        var applicationId = await CreateApplicationAsync(client);

        var response = await client.GetAsync($"/v1/applications/{applicationId}/classify");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetReturnsTheCurrentStateAfterClassify()
    {
        var client = factory.CreateClient();
        var applicationId = await CreateApplicationAsync(client);
        await FillInBusinessAsync(client, applicationId);
        await client.PostAsync($"/v1/applications/{applicationId}/classify", content: null);

        var response = await client.GetAsync($"/v1/applications/{applicationId}/classify");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.False(string.IsNullOrEmpty(body.GetProperty("proposedMccCode").GetString()));
    }

    [Fact]
    public async Task ClassifyReturnsBadRequestForAMalformedApplicationId()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsync("/v1/applications/not-a-guid/classify", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
