using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Gweb.Tests.Api.Applications;

/// <summary>
/// The required security test for Phase 3 (docs/08-IMPLEMENTATION-PLAN.md: "logger
/// test proves no PII in logs"), run against the real HTTP endpoints rather than the
/// Redactor unit in isolation -- this proves the whole PATCH flow, not just that the
/// redaction function works when called correctly.
/// </summary>
public class PiiRedactionEndToEndTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static string CaptureStdout(Action action)
    {
        var original = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            action();
        }
        finally
        {
            Console.SetOut(original);
        }
        return writer.ToString();
    }

    [Fact]
    public async Task PatchingAFullyPopulatedApplicantAndBusinessNeverLogsAnySensitiveRawValue()
    {
        var client = factory.CreateClient();
        var createResponse = await client.PostAsync(new Uri("/v1/applications", UriKind.Relative), content: null);
        var applicationId = (await createResponse.Content.ReadFromJsonAsync<JsonElementWrapper>())!.Id;

        var logOutput = CaptureStdout(() =>
        {
            client.PatchAsJsonAsync(
                $"/v1/applications/{applicationId}/applicant",
                new
                {
                    legalFirstName = "Jane",
                    legalLastName = "Testerson",
                    dateOfBirth = "1985-06-15",
                    email = "jane@example.invalid",
                    phone = "+1 555-000-1234",
                    roleTitle = "CEO",
                    governmentId = new { type = "Passport", number = "X1234567" },
                    consentVersion = "v1",
                }).GetAwaiter().GetResult();

            client.PatchAsJsonAsync(
                $"/v1/applications/{applicationId}/business",
                new
                {
                    legalBusinessName = "Testerson Trading LLC",
                    registrationIdentifier = new { type = "Ein", value = "00-0000000" },
                    settlementBankAccount = new
                    {
                        accountHolder = "Testerson Trading LLC",
                        bankName = "Test Bank",
                        accountNumber = "000123456789",
                        statementDate = "2026-01-01",
                    },
                }).GetAwaiter().GetResult();
        });

        Assert.DoesNotContain("1985-06-15", logOutput);
        Assert.DoesNotContain("X1234567", logOutput);
        Assert.DoesNotContain("00-0000000", logOutput);
        Assert.DoesNotContain("000123456789", logOutput);
    }

    private sealed record JsonElementWrapper(Guid Id);
}
