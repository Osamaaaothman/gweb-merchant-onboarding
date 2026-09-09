using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Gweb.Adapters.Storage;
using Gweb.Domain.Documents;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Gweb.Tests.Api.Applications;

public class SubmitEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static async Task<Guid> CreateApplicationAsync(HttpClient client)
    {
        var response = await client.PostAsync(new Uri("/v1/applications", UriKind.Relative), content: null);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return body.GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> CompleteApplicantAsync(HttpClient client, Guid applicationId) =>
        client.PatchAsJsonAsync($"/v1/applications/{applicationId}/applicant", new
        {
            legalFirstName = "Jane",
            legalLastName = "Doe",
            dateOfBirth = "1990-01-01",
            residentialAddress = new { line1 = "1 Main St", city = "Town", state = "ST", postalCode = "00000", country = "US" },
            email = "jane@example.invalid",
            phone = "+10000000000",
            roleTitle = "CEO",
            governmentId = new { type = "Passport", number = "X1234567" },
            consentVersion = "v1",
        });

    private static Task<HttpResponseMessage> CompleteBusinessAsync(HttpClient client, Guid applicationId) =>
        client.PatchAsJsonAsync($"/v1/applications/{applicationId}/business", new
        {
            legalBusinessName = "Acme LLC",
            entityType = "Llc",
            formationCountry = "US",
            registrationIdentifier = new { type = "Ein", value = "12-3456789" },
            registeredAddress = new { line1 = "1 Main St", city = "Town", state = "ST", postalCode = "00000", country = "US" },
            operatingAddress = new { line1 = "1 Main St", city = "Town", state = "ST", postalCode = "00000", country = "US" },
            businessDescription = "A store.",
            businessStartDate = "2020-01-01",
            volumeProfile = new
            {
                expectedAnnualCardVolume = 100_000,
                averageTicket = 50,
                highestTicket = 200,
                monthlyTransactionCount = 100,
                cardPresentPercentage = 60,
                ecommercePercentage = 40,
            },
            settlementBankAccount = new { accountHolder = "Acme LLC", bankName = "Bank", accountNumber = "000123456789", statementDate = "2026-08-01" },
        });

    /// <summary>Uploads and completes a real document of the given type through the
    /// actual HTTP presign/complete flow -- same pattern as DocumentEndpointTests.</summary>
    private async Task UploadCompletedDocumentAsync(HttpClient client, Guid applicationId, string documentType)
    {
        var bytes = "%PDF-1.7 fake document"u8.ToArray();
        var checksum = Convert.ToBase64String(SHA256.HashData(bytes));
        var presignResponse = await client.PostAsJsonAsync(
            $"/v1/applications/{applicationId}/documents/presign",
            new { type = documentType, originalFilename = "doc.pdf", contentType = "application/pdf", declaredSizeBytes = bytes.Length, declaredChecksumSha256Base64 = checksum });
        var presignBody = JsonDocument.Parse(await presignResponse.Content.ReadAsStringAsync()).RootElement;
        var documentId = presignBody.GetProperty("document").GetProperty("id").GetGuid();
        var s3Key = presignBody.GetProperty("uploadFields").GetProperty("key").GetString()!;

        var storage = (InMemoryDocumentStorage)factory.Services.GetRequiredService<IDocumentStorage>();
        storage.SimulateUpload(s3Key, new UploadedObject(bytes.Length, checksum, bytes[..16]));

        await client.PostAsync($"/v1/applications/{applicationId}/documents/{documentId}/complete", content: null);
    }

    private async Task<Guid> CreateFullyReadyApplicationAsync(HttpClient client)
    {
        var applicationId = await CreateApplicationAsync(client);
        await CompleteApplicantAsync(client, applicationId);
        await CompleteBusinessAsync(client, applicationId);
        await UploadCompletedDocumentAsync(client, applicationId, "GovernmentId");
        await UploadCompletedDocumentAsync(client, applicationId, "BusinessRegistration");
        await UploadCompletedDocumentAsync(client, applicationId, "BankEvidence");
        return applicationId;
    }

    [Fact]
    public async Task SubmitReturnsBadRequestWithPreciseMissingItemsWhenNothingHasBeenFilledIn()
    {
        var client = factory.CreateClient();
        var applicationId = await CreateApplicationAsync(client);

        var response = await client.PostAsync($"/v1/applications/{applicationId}/submit", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var details = body.GetProperty("error").GetProperty("details");
        Assert.True(details.GetProperty("missingApplicantFields").GetArrayLength() > 0);
        Assert.True(details.GetProperty("missingBusinessFields").GetArrayLength() > 0);
        var missingDocs = details.GetProperty("missingRequiredDocuments").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("GovernmentId", missingDocs);
        Assert.Contains("BusinessRegistration", missingDocs);
        Assert.Contains("BankEvidence", missingDocs);
    }

    [Fact]
    public async Task SubmitReturnsBadRequestWhenOnlyDocumentsAreMissing()
    {
        var client = factory.CreateClient();
        var applicationId = await CreateApplicationAsync(client);
        await CompleteApplicantAsync(client, applicationId);
        await CompleteBusinessAsync(client, applicationId);

        var response = await client.PostAsync($"/v1/applications/{applicationId}/submit", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var details = body.GetProperty("error").GetProperty("details");
        Assert.Equal(0, details.GetProperty("missingApplicantFields").GetArrayLength());
        Assert.Equal(0, details.GetProperty("missingBusinessFields").GetArrayLength());
        Assert.Equal(3, details.GetProperty("missingRequiredDocuments").GetArrayLength());
    }

    [Fact]
    public async Task SubmitSucceedsAndReturnsTheNormalizedReviewPayloadWhenEverythingIsComplete()
    {
        var client = factory.CreateClient();
        var applicationId = await CreateFullyReadyApplicationAsync(client);

        var response = await client.PostAsync($"/v1/applications/{applicationId}/submit", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Submitted", body.GetProperty("status").GetString());
        Assert.Equal("Jane", body.GetProperty("applicant").GetProperty("legalFirstName").GetString());
        Assert.Equal("Acme LLC", body.GetProperty("business").GetProperty("legalBusinessName").GetString());
        Assert.Equal(3, body.GetProperty("documents").GetArrayLength());
    }

    [Fact]
    public async Task SubmitNeverExposesUnmaskedGovernmentIdOrBankAccountInTheReviewPayload()
    {
        var client = factory.CreateClient();
        var applicationId = await CreateFullyReadyApplicationAsync(client);

        var response = await client.PostAsync($"/v1/applications/{applicationId}/submit", content: null);

        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("X1234567", raw);
        Assert.DoesNotContain("000123456789", raw);
    }

    [Fact]
    public async Task SubmitTwiceReturnsConflictOnTheSecondCall()
    {
        var client = factory.CreateClient();
        var applicationId = await CreateFullyReadyApplicationAsync(client);
        await client.PostAsync($"/v1/applications/{applicationId}/submit", content: null);

        var response = await client.PostAsync($"/v1/applications/{applicationId}/submit", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task SubmitReturnsNotFoundForAnUnknownApplication()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsync($"/v1/applications/{Guid.NewGuid()}/submit", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SubmitReturnsBadRequestForAMalformedApplicationId()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsync("/v1/applications/not-a-guid/submit", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetApplicationStillReturnsInProgressBeforeSubmitting()
    {
        var client = factory.CreateClient();
        var applicationId = await CreateApplicationAsync(client);

        var response = await client.GetAsync($"/v1/applications/{applicationId}");

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("InProgress", body.GetProperty("status").GetString());
    }
}
