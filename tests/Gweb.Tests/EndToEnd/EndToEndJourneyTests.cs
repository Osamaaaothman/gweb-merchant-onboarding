using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Gweb.Adapters.Storage;
using Gweb.Domain.Documents;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Gweb.Tests.EndToEnd;

/// <summary>
/// The brief's mandatory single integration test (docs/08-IMPLEMENTATION-PLAN.md Phase
/// 12, docs/02-ENGINEERING-STANDARDS.md): "create application -> update business ->
/// pre-sign upload -> mock upload completion -> classify -> evaluate -> submit,"
/// exercised end-to-end through the real ASP.NET Core pipeline (WebApplicationFactory),
/// never shortcut by calling a service directly. Every other endpoint test file covers
/// one feature in isolation with its own edge cases; this file exists specifically to
/// prove the *whole* journey holds together across every phase's own state transitions
/// in one continuous run, matching the exact chain the brief names.
///
/// Runnable with one documented command: `dotnet test --filter FullyQualifiedName~EndToEndJourneyTests`
/// (or just `dotnet test`, which runs this alongside the rest of the suite -- see
/// README.md "Running tests").
/// </summary>
public class EndToEndJourneyTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task TheFullMerchantOnboardingJourneySucceedsEndToEnd()
    {
        var client = factory.CreateClient();

        // 1. Create application.
        var createResponse = await client.PostAsync(new Uri("/v1/applications", UriKind.Relative), content: null);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var applicationId = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetGuid();

        // Resuming an in-progress application (brief "preserve state so a partially
        // completed application can be resumed") works before anything else has been
        // filled in.
        var resumeResponse = await client.GetAsync($"/v1/applications/{applicationId}");
        Assert.Equal(HttpStatusCode.OK, resumeResponse.StatusCode);
        var resumeBody = JsonDocument.Parse(await resumeResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("InProgress", resumeBody.GetProperty("status").GetString());
        Assert.False(resumeBody.GetProperty("completeness").GetProperty("isComplete").GetBoolean());

        // 2. Update applicant. Government ID masked to last4 in the very same response
        // -- the full number never appears anywhere past this call.
        var applicantResponse = await client.PatchAsJsonAsync(
            $"/v1/applications/{applicationId}/applicant",
            new
            {
                legalFirstName = "Jane",
                legalLastName = "Doe",
                dateOfBirth = "1985-01-15",
                residentialAddress = new { line1 = "1 Main St", city = "Springfield", state = "IL", postalCode = "62701", country = "US" },
                email = "jane@freshvalleygrocers.example",
                phone = "+1 555 100 2000",
                roleTitle = "CEO",
                governmentId = new { type = "Passport", number = "X1234567" },
                consentVersion = "v1.0",
            });
        Assert.Equal(HttpStatusCode.OK, applicantResponse.StatusCode);
        var applicantBody = JsonDocument.Parse(await applicantResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("4567", applicantBody.GetProperty("governmentId").GetProperty("last4").GetString());

        // 3. Update business. Same masking guarantee for the EIN and settlement
        // account number.
        var businessResponse = await client.PatchAsJsonAsync(
            $"/v1/applications/{applicationId}/business",
            new
            {
                legalBusinessName = "Fresh Valley Grocers LLC",
                entityType = "Llc",
                formationCountry = "US",
                registrationIdentifier = new { type = "Ein", value = "123456789" },
                registeredAddress = new { line1 = "100 Market St", city = "Springfield", state = "IL", postalCode = "62701", country = "US" },
                operatingAddress = new { line1 = "100 Market St", city = "Springfield", state = "IL", postalCode = "62701", country = "US" },
                businessDescription = "A neighborhood grocery store selling fresh produce, dairy, and packaged foods.",
                businessStartDate = "2020-03-01",
                volumeProfile = new
                {
                    expectedAnnualCardVolume = 500_000,
                    averageTicket = 35,
                    highestTicket = 150,
                    monthlyTransactionCount = 2000,
                    cardPresentPercentage = 70,
                    ecommercePercentage = 30,
                },
                settlementBankAccount = new { accountHolder = "Fresh Valley Grocers LLC", bankName = "First National Bank", accountNumber = "000123456789", statementDate = "2026-08-01" },
            });
        Assert.Equal(HttpStatusCode.OK, businessResponse.StatusCode);
        var businessBody = JsonDocument.Parse(await businessResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("6789", businessBody.GetProperty("settlementBankAccount").GetProperty("last4").GetString());

        // Confirm the aggregate GET now reflects both halves and reports the applicant
        // and business as complete, while documents are still missing.
        var midpointResponse = await client.GetAsync($"/v1/applications/{applicationId}");
        var midpointBody = JsonDocument.Parse(await midpointResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(0, midpointBody.GetProperty("completeness").GetProperty("missingApplicantFields").GetArrayLength());
        Assert.Equal(0, midpointBody.GetProperty("completeness").GetProperty("missingBusinessFields").GetArrayLength());

        // Submitting now must be blocked -- no documents yet.
        var earlySubmitResponse = await client.PostAsync($"/v1/applications/{applicationId}/submit", content: null);
        Assert.Equal(HttpStatusCode.BadRequest, earlySubmitResponse.StatusCode);
        var earlySubmitBody = JsonDocument.Parse(await earlySubmitResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(3, earlySubmitBody.GetProperty("error").GetProperty("details").GetProperty("missingRequiredDocuments").GetArrayLength());

        // 4-5. Pre-sign and (mock) complete every required document: Government ID,
        // Business Registration, Bank Evidence.
        foreach (var documentType in new[] { "GovernmentId", "BusinessRegistration", "BankEvidence" })
        {
            var (documentId, status) = await UploadAndCompleteDocumentAsync(client, applicationId, documentType);
            Assert.Equal("Received", status);
            Assert.NotEqual(Guid.Empty, documentId);
        }

        // The new (Phase 11) list-documents endpoint reflects all three, scoped to
        // this application.
        var listResponse = await client.GetAsync($"/v1/applications/{applicationId}/documents");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listBody = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(3, listBody.GetArrayLength());

        // 6. Classify -- proposes a real, catalog-grounded MCC.
        var classifyResponse = await client.PostAsync($"/v1/applications/{applicationId}/classify", content: null);
        Assert.Equal(HttpStatusCode.OK, classifyResponse.StatusCode);
        var classifyBody = JsonDocument.Parse(await classifyResponse.Content.ReadAsStringAsync()).RootElement;
        var proposedMccCode = classifyBody.GetProperty("proposedMccCode").GetString();
        Assert.False(string.IsNullOrWhiteSpace(proposedMccCode));
        Assert.Equal("mock", classifyBody.GetProperty("proposedProvider").GetString());

        // The applicant confirms the proposal (brief "applicant confirms or corrects
        // it").
        var confirmResponse = await client.PostAsJsonAsync(
            $"/v1/applications/{applicationId}/classify/confirm", new { mccCode = proposedMccCode });
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);
        var confirmBody = JsonDocument.Parse(await confirmResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(proposedMccCode, confirmBody.GetProperty("selfSelectedMccCode").GetString());
        Assert.False(confirmBody.GetProperty("hasMismatch").GetBoolean());

        // 7. Evaluate -- no processing statement uploaded, so this exercises the
        // risk-signals-only path (still a real evaluation, not skipped).
        var evaluateResponse = await client.PostAsJsonAsync($"/v1/applications/{applicationId}/evaluate", new { });
        Assert.Equal(HttpStatusCode.OK, evaluateResponse.StatusCode);
        var evaluateBody = JsonDocument.Parse(await evaluateResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Completed", evaluateBody.GetProperty("status").GetString());
        Assert.True(evaluateBody.GetProperty("riskSignals").GetArrayLength() > 0);

        // 8. Submit -- everything required is now in place, so this succeeds and
        // returns the normalized review payload, locking the application.
        var submitResponse = await client.PostAsync($"/v1/applications/{applicationId}/submit", content: null);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);
        var submitBody = JsonDocument.Parse(await submitResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Submitted", submitBody.GetProperty("status").GetString());
        Assert.Equal(3, submitBody.GetProperty("documents").GetArrayLength());
        Assert.Equal(proposedMccCode, submitBody.GetProperty("classification").GetProperty("selfSelectedMccCode").GetString());
        Assert.Equal("Completed", submitBody.GetProperty("evaluation").GetProperty("status").GetString());
        // Still no unmasked PII anywhere in the final payload.
        var submitRaw = await submitResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("X1234567", submitRaw);
        Assert.DoesNotContain("000123456789", submitRaw);

        // The application is now locked -- a second submit is a real conflict, not a
        // silent no-op or a second 200.
        var secondSubmitResponse = await client.PostAsync($"/v1/applications/{applicationId}/submit", content: null);
        Assert.Equal(HttpStatusCode.Conflict, secondSubmitResponse.StatusCode);

        // And the aggregate GET now reflects the terminal state -- still no "Approved"
        // value anywhere in this codebase (NoAutoApprovalPathTests enforces this
        // structurally; this is the same guarantee observed from the outside, through
        // the actual HTTP response).
        var finalGetResponse = await client.GetAsync($"/v1/applications/{applicationId}");
        var finalGetBody = JsonDocument.Parse(await finalGetResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Submitted", finalGetBody.GetProperty("status").GetString());
    }

    /// <summary>
    /// Presign -> simulate the client's direct-to-S3 upload -> complete, the same real
    /// HTTP round trip every document-upload test in this suite uses (never a
    /// service-layer shortcut). Returns the document ID and its post-complete status.
    /// </summary>
    private async Task<(Guid DocumentId, string Status)> UploadAndCompleteDocumentAsync(
        HttpClient client, Guid applicationId, string documentType)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes($"%PDF-1.7 fake {documentType} content for the end-to-end journey test");
        var checksum = Convert.ToBase64String(SHA256.HashData(bytes));

        var presignResponse = await client.PostAsJsonAsync(
            $"/v1/applications/{applicationId}/documents/presign",
            new
            {
                type = documentType,
                originalFilename = $"{documentType}.pdf",
                contentType = "application/pdf",
                declaredSizeBytes = bytes.Length,
                declaredChecksumSha256Base64 = checksum,
            });
        Assert.Equal(HttpStatusCode.Created, presignResponse.StatusCode);
        var presignBody = JsonDocument.Parse(await presignResponse.Content.ReadAsStringAsync()).RootElement;
        var documentId = presignBody.GetProperty("document").GetProperty("id").GetGuid();
        var s3Key = presignBody.GetProperty("uploadFields").GetProperty("key").GetString()!;
        Assert.Equal("Uploading", presignBody.GetProperty("document").GetProperty("status").GetString());

        // Mock upload completion: stands in for the client's real direct-to-S3 upload
        // (already verified for real against MinIO in Phase 11 -- see
        // docs/adr/0010-frontend-architecture.md) by recording the bytes under the
        // real, server-generated S3 key against the same IDocumentStorage singleton
        // the running app resolved.
        var storage = (InMemoryDocumentStorage)factory.Services.GetRequiredService<IDocumentStorage>();
        storage.SimulateUpload(s3Key, new UploadedObject(bytes.Length, checksum, bytes[..16]));

        var completeResponse = await client.PostAsync($"/v1/applications/{applicationId}/documents/{documentId}/complete", content: null);
        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);
        var completeBody = JsonDocument.Parse(await completeResponse.Content.ReadAsStringAsync()).RootElement;

        return (documentId, completeBody.GetProperty("status").GetString()!);
    }
}
