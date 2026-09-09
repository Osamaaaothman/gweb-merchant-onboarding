using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Gweb.Adapters.Storage;
using Gweb.Domain.Documents;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Gweb.Tests.Api.Evaluation;

public class EvaluationEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static async Task<Guid> CreateApplicationAsync(HttpClient client)
    {
        var response = await client.PostAsync(new Uri("/v1/applications", UriKind.Relative), content: null);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return body.GetProperty("id").GetGuid();
    }

    private static async Task FillInBusinessAsync(HttpClient client, Guid applicationId)
    {
        await client.PatchAsJsonAsync(
            $"/v1/applications/{applicationId}/business",
            new { legalBusinessName = "Fresh Valley Grocers", entityType = "Llc", businessDescription = "A neighborhood grocery store." });
    }

    /// <summary>Uploads and completes a real ProcessingStatement document through the
    /// actual HTTP presign/complete flow (not a shortcut), then simulates the direct-
    /// to-S3 upload the client would have done -- same pattern as DocumentEndpointTests.</summary>
    private async Task<Guid> UploadCompletedStatementAsync(HttpClient client, Guid applicationId)
    {
        var bytes = "%PDF-1.7 fake statement"u8.ToArray();
        var checksum = Convert.ToBase64String(SHA256.HashData(bytes));
        var presignResponse = await client.PostAsJsonAsync(
            $"/v1/applications/{applicationId}/documents/presign",
            new
            {
                type = "ProcessingStatement",
                originalFilename = "statement.pdf",
                contentType = "application/pdf",
                declaredSizeBytes = bytes.Length,
                declaredChecksumSha256Base64 = checksum,
            });
        var presignBody = JsonDocument.Parse(await presignResponse.Content.ReadAsStringAsync()).RootElement;
        var documentId = presignBody.GetProperty("document").GetProperty("id").GetGuid();
        var s3Key = presignBody.GetProperty("uploadFields").GetProperty("key").GetString()!;

        var storage = (InMemoryDocumentStorage)factory.Services.GetRequiredService<IDocumentStorage>();
        storage.SimulateUpload(s3Key, new UploadedObject(bytes.Length, checksum, bytes[..16]));
        storage.SimulateFullUpload(s3Key, bytes);

        await client.PostAsync($"/v1/applications/{applicationId}/documents/{documentId}/complete", content: null);
        return documentId;
    }

    [Fact]
    public async Task EvaluateWithNoStatementReturnsCompletedWithNoCalculatedRateAndAMissingStatementSignal()
    {
        var client = factory.CreateClient();
        var applicationId = await CreateApplicationAsync(client);
        await FillInBusinessAsync(client, applicationId);

        var response = await client.PostAsJsonAsync($"/v1/applications/{applicationId}/evaluate", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Completed", body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("calculated").ValueKind);
        Assert.Contains(
            body.GetProperty("riskSignals").EnumerateArray(),
            s => s.GetProperty("code").GetString() == "MISSING_PROCESSING_STATEMENT");
    }

    [Fact]
    public async Task EvaluateWithARealCompletedStatementReturnsExtractedAndCalculatedData()
    {
        var client = factory.CreateClient();
        var applicationId = await CreateApplicationAsync(client);
        await FillInBusinessAsync(client, applicationId);
        var documentId = await UploadCompletedStatementAsync(client, applicationId);

        var response = await client.PostAsJsonAsync(
            $"/v1/applications/{applicationId}/evaluate", new { processingStatementDocumentId = documentId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Completed", body.GetProperty("status").GetString());
        Assert.Equal("mock", body.GetProperty("extracted").GetProperty("provider").GetString());
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("calculated").ValueKind);
        Assert.Equal(documentId, body.GetProperty("processingStatementDocumentId").GetGuid());
    }

    [Fact]
    public async Task EvaluateReturnsBadRequestWhenBusinessHasNotBeenFilledInYet()
    {
        var client = factory.CreateClient();
        var applicationId = await CreateApplicationAsync(client);

        var response = await client.PostAsJsonAsync($"/v1/applications/{applicationId}/evaluate", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task EvaluateReturnsNotFoundForAnUnknownApplication()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/v1/applications/{Guid.NewGuid()}/evaluate", new { });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task EvaluateReturnsNotFoundWhenTheStatementDocumentIdDoesNotExist()
    {
        var client = factory.CreateClient();
        var applicationId = await CreateApplicationAsync(client);
        await FillInBusinessAsync(client, applicationId);

        var response = await client.PostAsJsonAsync(
            $"/v1/applications/{applicationId}/evaluate", new { processingStatementDocumentId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetReturnsNotFoundWhenNoEvaluationExistsYet()
    {
        var client = factory.CreateClient();
        var applicationId = await CreateApplicationAsync(client);

        var response = await client.GetAsync($"/v1/applications/{applicationId}/evaluation");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetReturnsTheCurrentStateAfterEvaluate()
    {
        var client = factory.CreateClient();
        var applicationId = await CreateApplicationAsync(client);
        await FillInBusinessAsync(client, applicationId);
        await client.PostAsJsonAsync($"/v1/applications/{applicationId}/evaluate", new { });

        var response = await client.GetAsync($"/v1/applications/{applicationId}/evaluation");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Completed", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task EvaluateReturnsBadRequestForAMalformedApplicationId()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/v1/applications/not-a-guid/evaluate", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task EveryRiskSignalInTheResponseCarriesANonEmptySourceField()
    {
        var client = factory.CreateClient();
        var applicationId = await CreateApplicationAsync(client);
        await FillInBusinessAsync(client, applicationId);

        var response = await client.PostAsJsonAsync($"/v1/applications/{applicationId}/evaluate", new { });

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var signals = body.GetProperty("riskSignals").EnumerateArray().ToList();
        Assert.NotEmpty(signals);
        Assert.All(signals, s => Assert.False(string.IsNullOrWhiteSpace(s.GetProperty("sourceField").GetString())));
    }
}
