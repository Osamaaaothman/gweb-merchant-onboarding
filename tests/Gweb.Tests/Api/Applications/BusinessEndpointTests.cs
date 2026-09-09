using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Gweb.Tests.Api.Applications;

public class PatchBusinessEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static async Task<Guid> CreateApplicationAsync(HttpClient client)
    {
        var response = await client.PostAsync(new Uri("/v1/applications", UriKind.Relative), content: null);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return body.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task AcceptsAValidPatchAndReturnsTheMergedBusiness()
    {
        var client = factory.CreateClient();
        var applicationId = await CreateApplicationAsync(client);

        var response = await client.PatchAsJsonAsync(
            $"/v1/applications/{applicationId}/business",
            new { legalBusinessName = "Testerson Trading LLC", entityType = "Llc" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Testerson Trading LLC", body.GetProperty("legalBusinessName").GetString());
    }

    [Fact]
    public async Task MasksTheSettlementBankAccountInTheResponse()
    {
        var client = factory.CreateClient();
        var applicationId = await CreateApplicationAsync(client);

        var response = await client.PatchAsJsonAsync(
            $"/v1/applications/{applicationId}/business",
            new
            {
                settlementBankAccount = new
                {
                    accountHolder = "Testerson Trading LLC",
                    bankName = "Test Bank",
                    accountNumber = "000123456789",
                    statementDate = "2026-01-01",
                },
            });

        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("000123456789", raw);
        var body = JsonDocument.Parse(raw).RootElement;
        Assert.Equal("6789", body.GetProperty("settlementBankAccount").GetProperty("last4").GetString());
    }

    [Fact]
    public async Task ReturnsBadRequestWhenCombinedBeneficialOwnershipExceedsOneHundredPercent()
    {
        var client = factory.CreateClient();
        var applicationId = await CreateApplicationAsync(client);

        var response = await client.PatchAsJsonAsync(
            $"/v1/applications/{applicationId}/business",
            new { beneficialOwners = new[] { new { name = "A", roleTitle = "Owner", ownershipPercentage = 60 }, new { name = "B", roleTitle = "Owner", ownershipPercentage = 50 } } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReturnsBadRequestWhenApplicantAndBeneficialOwnersCombinedExceedOneHundredPercent()
    {
        var client = factory.CreateClient();
        var applicationId = await CreateApplicationAsync(client);
        await client.PatchAsJsonAsync($"/v1/applications/{applicationId}/applicant", new { ownershipPercentage = 60 });

        var response = await client.PatchAsJsonAsync(
            $"/v1/applications/{applicationId}/business",
            new { beneficialOwners = new[] { new { name = "A", roleTitle = "Owner", ownershipPercentage = 50 } } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReturnsNotFoundWhenTheApplicationDoesNotExist()
    {
        var client = factory.CreateClient();

        var response = await client.PatchAsJsonAsync($"/v1/applications/{Guid.NewGuid()}/business", new { legalBusinessName = "X" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

public class GetApplicationWithSubResourcesTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task ReportsIncompleteWithMissingFieldsWhenNeitherApplicantNorBusinessHasBeenPatched()
    {
        var client = factory.CreateClient();
        var createResponse = await client.PostAsync(new Uri("/v1/applications", UriKind.Relative), content: null);
        var applicationId = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        var response = await client.GetAsync(new Uri($"/v1/applications/{applicationId}", UriKind.Relative));

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.False(body.GetProperty("completeness").GetProperty("isComplete").GetBoolean());
        Assert.True(body.GetProperty("applicant").ValueKind == JsonValueKind.Null);
        Assert.True(body.GetProperty("business").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task ReflectsPatchedApplicantAndBusinessDataInTheAggregateView()
    {
        var client = factory.CreateClient();
        var createResponse = await client.PostAsync(new Uri("/v1/applications", UriKind.Relative), content: null);
        var applicationId = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        await client.PatchAsJsonAsync($"/v1/applications/{applicationId}/applicant", new { legalFirstName = "Jane" });
        await client.PatchAsJsonAsync($"/v1/applications/{applicationId}/business", new { legalBusinessName = "Testerson Trading LLC" });

        var response = await client.GetAsync(new Uri($"/v1/applications/{applicationId}", UriKind.Relative));

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Jane", body.GetProperty("applicant").GetProperty("legalFirstName").GetString());
        Assert.Equal("Testerson Trading LLC", body.GetProperty("business").GetProperty("legalBusinessName").GetString());
        Assert.Contains("email", body.GetProperty("completeness").GetProperty("missingApplicantFields").EnumerateArray().Select(e => e.GetString()));
    }
}
