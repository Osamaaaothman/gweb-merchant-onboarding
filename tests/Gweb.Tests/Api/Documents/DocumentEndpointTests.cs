using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Gweb.Adapters.Storage;
using Gweb.Domain.Documents;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Gweb.Tests.Api.Documents;

public class DocumentEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly byte[] ValidPdfBytes = [.. "%PDF-1.7 rest of a fake pdf"u8];

    private static string Sha256Base64(byte[] bytes) => Convert.ToBase64String(SHA256.HashData(bytes));

    private static async Task<Guid> CreateApplicationAsync(HttpClient client)
    {
        var response = await client.PostAsync(new Uri("/v1/applications", UriKind.Relative), content: null);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return body.GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> PresignAsync(HttpClient client, Guid applicationId, long declaredSizeBytes, string checksum)
    {
        var response = await client.PostAsJsonAsync(
            $"/v1/applications/{applicationId}/documents/presign",
            new
            {
                type = "BankEvidence",
                originalFilename = "voided-check.pdf",
                contentType = "application/pdf",
                declaredSizeBytes,
                declaredChecksumSha256Base64 = checksum,
            });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    [Fact]
    public async Task PresignReturnsAnUploadUrlAndFieldsAndLeavesTheDocumentInUploadingState()
    {
        var client = factory.CreateClient();
        var applicationId = await CreateApplicationAsync(client);

        var body = await PresignAsync(client, applicationId, ValidPdfBytes.Length, Sha256Base64(ValidPdfBytes));

        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("uploadUrl").GetString()));
        Assert.Equal("Uploading", body.GetProperty("document").GetProperty("status").GetString());
        Assert.Equal(applicationId, body.GetProperty("document").GetProperty("applicationId").GetGuid());
    }

    [Fact]
    public async Task PresignRejectsAContentTypeThatIsNotOnTheAllowlist()
    {
        var client = factory.CreateClient();
        var applicationId = await CreateApplicationAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/v1/applications/{applicationId}/documents/presign",
            new
            {
                type = "BankEvidence",
                originalFilename = "evidence.exe",
                contentType = "application/x-msdownload",
                declaredSizeBytes = 1024,
                declaredChecksumSha256Base64 = "ZGVjbGFyZWQ=",
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("VALIDATION_FAILED", body.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task PresignRejectsADeclaredSizeAboveTheConfiguredLimit()
    {
        var client = factory.CreateClient();
        var applicationId = await CreateApplicationAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/v1/applications/{applicationId}/documents/presign",
            new
            {
                type = "BankEvidence",
                originalFilename = "huge.pdf",
                contentType = "application/pdf",
                declaredSizeBytes = DocumentUploadLimits.MaxSizeBytes + 1,
                declaredChecksumSha256Base64 = "ZGVjbGFyZWQ=",
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PresignReturnsNotFoundWhenTheApplicationDoesNotExist()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/v1/applications/{Guid.NewGuid()}/documents/presign",
            new
            {
                type = "BankEvidence",
                originalFilename = "voided-check.pdf",
                contentType = "application/pdf",
                declaredSizeBytes = 1024,
                declaredChecksumSha256Base64 = "ZGVjbGFyZWQ=",
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CompleteReturnsBadRequestWhenNoBytesHaveBeenUploadedYet()
    {
        var client = factory.CreateClient();
        var applicationId = await CreateApplicationAsync(client);
        var presign = await PresignAsync(client, applicationId, ValidPdfBytes.Length, Sha256Base64(ValidPdfBytes));
        var documentId = presign.GetProperty("document").GetProperty("id").GetGuid();

        var response = await client.PostAsync($"/v1/applications/{applicationId}/documents/{documentId}/complete", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CompleteTransitionsToReceivedWhenTheUploadedBytesMatchWhatWasDeclared()
    {
        var client = factory.CreateClient();
        var applicationId = await CreateApplicationAsync(client);
        var checksum = Sha256Base64(ValidPdfBytes);
        var presign = await PresignAsync(client, applicationId, ValidPdfBytes.Length, checksum);
        var documentId = presign.GetProperty("document").GetProperty("id").GetGuid();
        var s3Key = presign.GetProperty("uploadFields").GetProperty("key").GetString()!;
        SimulateRealUpload(s3Key, ValidPdfBytes, checksum);

        var response = await client.PostAsync($"/v1/applications/{applicationId}/documents/{documentId}/complete", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Received", body.GetProperty("status").GetString());
        Assert.Equal(ValidPdfBytes.Length, body.GetProperty("actualSizeBytes").GetInt64());
    }

    [Fact]
    public async Task CompleteIsIdempotentOnASecondCallAfterAlreadyReceived()
    {
        var client = factory.CreateClient();
        var applicationId = await CreateApplicationAsync(client);
        var checksum = Sha256Base64(ValidPdfBytes);
        var presign = await PresignAsync(client, applicationId, ValidPdfBytes.Length, checksum);
        var documentId = presign.GetProperty("document").GetProperty("id").GetGuid();
        var s3Key = presign.GetProperty("uploadFields").GetProperty("key").GetString()!;
        SimulateRealUpload(s3Key, ValidPdfBytes, checksum);
        var first = await client.PostAsync($"/v1/applications/{applicationId}/documents/{documentId}/complete", content: null);

        var second = await client.PostAsync($"/v1/applications/{applicationId}/documents/{documentId}/complete", content: null);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var firstBody = JsonDocument.Parse(await first.Content.ReadAsStringAsync()).RootElement;
        var secondBody = JsonDocument.Parse(await second.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(firstBody.GetProperty("version").GetInt64(), secondBody.GetProperty("version").GetInt64());
    }

    [Fact]
    public async Task CompleteRejectsTheDocumentWhenTheUploadedChecksumDoesNotMatchWhatWasDeclared()
    {
        var client = factory.CreateClient();
        var applicationId = await CreateApplicationAsync(client);
        var presign = await PresignAsync(client, applicationId, ValidPdfBytes.Length, Sha256Base64(ValidPdfBytes));
        var documentId = presign.GetProperty("document").GetProperty("id").GetGuid();
        var s3Key = presign.GetProperty("uploadFields").GetProperty("key").GetString()!;
        // A different (tampered) checksum than what was declared at presign time.
        SimulateRealUpload(s3Key, ValidPdfBytes, "dGFtcGVyZWQ=");

        var response = await client.PostAsync($"/v1/applications/{applicationId}/documents/{documentId}/complete", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Rejected", body.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("rejectionReason").GetString()));
    }

    [Fact]
    public async Task CompleteReturnsNotFoundForAnUnknownDocument()
    {
        var client = factory.CreateClient();
        var applicationId = await CreateApplicationAsync(client);

        var response = await client.PostAsync($"/v1/applications/{applicationId}/documents/{Guid.NewGuid()}/complete", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetDocumentReturnsTheCurrentRecordAfterPresign()
    {
        var client = factory.CreateClient();
        var applicationId = await CreateApplicationAsync(client);
        var presign = await PresignAsync(client, applicationId, ValidPdfBytes.Length, Sha256Base64(ValidPdfBytes));
        var documentId = presign.GetProperty("document").GetProperty("id").GetGuid();

        var response = await client.GetAsync($"/v1/applications/{applicationId}/documents/{documentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(documentId, body.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task GetDocumentReturnsNotFoundForAnUnknownDocument()
    {
        var client = factory.CreateClient();
        var applicationId = await CreateApplicationAsync(client);

        var response = await client.GetAsync($"/v1/applications/{applicationId}/documents/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Stands in for a client actually uploading to the presigned URL -- reaches into
    /// the same singleton IDocumentStorage instance the running app resolved (the
    /// in-memory adapter under PERSISTENCE_PROVIDER=inmemory) and records the bytes
    /// under the real, server-generated S3 key so `complete` has something to verify.
    /// </summary>
    private void SimulateRealUpload(string s3Key, byte[] bytes, string checksumSha256Base64)
    {
        var storage = (InMemoryDocumentStorage)factory.Services.GetRequiredService<IDocumentStorage>();
        storage.SimulateUpload(s3Key, new UploadedObject(bytes.Length, checksumSha256Base64, bytes[..Math.Min(16, bytes.Length)]));
    }
}
