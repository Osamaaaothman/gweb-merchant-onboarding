using System.Text.Json.Nodes;
using Gweb.Shared.Logging;

namespace Gweb.Tests.Shared.Logging;

public class RedactorTests
{
    [Fact]
    public void MasksAKnownSensitiveKeyRegardlessOfCase()
    {
        var result = Redactor.Redact(new { GovernmentId = "P1234567", ssn = "123-45-6789" }) as JsonObject;

        Assert.Equal("[REDACTED]", result!["GovernmentId"]!.GetValue<string>());
        Assert.Equal("[REDACTED]", result["ssn"]!.GetValue<string>());
    }

    [Fact]
    public void MasksSensitiveKeysNestedInsideOtherObjects()
    {
        var result = Redactor.Redact(new { applicant = new { dateOfBirth = "1990-01-01", firstName = "Jane" } }) as JsonObject;

        var applicant = result!["applicant"] as JsonObject;
        Assert.Equal("[REDACTED]", applicant!["dateOfBirth"]!.GetValue<string>());
        Assert.Equal("Jane", applicant["firstName"]!.GetValue<string>());
    }

    [Fact]
    public void MasksAPreSignedUrlStringEvenUnderANonSensitiveKey()
    {
        const string url = "https://bucket.s3.amazonaws.com/key?X-Amz-Signature=abc123&X-Amz-Credential=xyz";

        var result = Redactor.Redact(new { uploadUrl = url }) as JsonObject;

        Assert.Equal("[REDACTED]", result!["uploadUrl"]!.GetValue<string>());
    }

    [Fact]
    public void LeavesNonSensitiveFieldsUntouched()
    {
        var result = Redactor.Redact(new { applicationId = "app-1", status = "SUBMITTED" }) as JsonObject;

        Assert.Equal("app-1", result!["applicationId"]!.GetValue<string>());
        Assert.Equal("SUBMITTED", result["status"]!.GetValue<string>());
    }

    [Fact]
    public void RedactsEveryElementOfAnArrayUnderASensitiveKeyAsAWhole()
    {
        string[] accountNumbers = ["1111", "2222"];

        var result = Redactor.Redact(new { bankAccountNumbers = accountNumbers }) as JsonObject;

        Assert.Equal("[REDACTED]", result!["bankAccountNumbers"]!.GetValue<string>());
    }

    [Fact]
    public void RecursesIntoArraysOfObjects()
    {
        var result = Redactor.Redact(new
        {
            owners = new[]
            {
                new { taxId = "12-3456789", name = "Jane" },
                new { taxId = "98-7654321", name = "John" },
            },
        }) as JsonObject;

        var owners = result!["owners"] as JsonArray;
        Assert.Equal("[REDACTED]", owners![0]!["taxId"]!.GetValue<string>());
        Assert.Equal("Jane", owners[0]!["name"]!.GetValue<string>());
        Assert.Equal("[REDACTED]", owners[1]!["taxId"]!.GetValue<string>());
    }

    [Fact]
    public void PassesThroughPrimitivesUnchanged()
    {
        Assert.Equal(42, Redactor.Redact(42)!.GetValue<int>());
        Assert.True(Redactor.Redact(true)!.GetValue<bool>());
        Assert.Null(Redactor.Redact<string?>(null));
    }
}

public class RedactorFullyPopulatedApplicationFixtureTests
{
    // Mirrors docs/04-SECURITY-RULES.md §2's "never appears in logs" list. This is the
    // required security test: proves the redaction utility, not developer discipline,
    // is what keeps sensitive values out of logs.
    private static readonly object Fixture = new
    {
        applicationId = "app-123",
        applicant = new
        {
            firstName = "Jane",
            lastName = "Testerson",
            dateOfBirth = "1985-06-15",
            governmentId = new { type = "PASSPORT", number = "X1234567" },
        },
        business = new
        {
            legalName = "Testerson Trading LLC",
            taxId = "00-0000000",
            bankAccount = new { accountNumber = "000123456789", routingNumber = "021000021" },
        },
        presignedUrl = "https://bucket.s3.amazonaws.com/key?X-Amz-Signature=deadbeef",
        aiPrompt = "Summarize this business: sells widgets online",
    };

    [Fact]
    public void ProducesOutputContainingNoneOfTheSensitiveRawValues()
    {
        var output = Redactor.Redact(Fixture)!.ToJsonString();

        Assert.DoesNotContain("1985-06-15", output);
        Assert.DoesNotContain("X1234567", output);
        Assert.DoesNotContain("00-0000000", output);
        Assert.DoesNotContain("000123456789", output);
        Assert.DoesNotContain("021000021", output);
        Assert.DoesNotContain("X-Amz-Signature=deadbeef", output);
    }

    [Fact]
    public void StillPreservesNonSensitiveFieldsNeededForDebugging()
    {
        var output = Redactor.Redact(Fixture)!.ToJsonString();

        Assert.Contains("app-123", output);
        Assert.Contains("Testerson Trading LLC", output);
    }
}
