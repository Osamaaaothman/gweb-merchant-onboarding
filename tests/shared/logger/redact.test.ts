import { redact } from "../../../shared/logger/redact";

describe("redact", () => {
  it("masks a known sensitive key regardless of case", () => {
    const result = redact({ GovernmentId: "P1234567", ssn: "123-45-6789" }) as Record<string, unknown>;

    expect(result.GovernmentId).toBe("[REDACTED]");
    expect(result.ssn).toBe("[REDACTED]");
  });

  it("masks sensitive keys nested inside other objects", () => {
    const result = redact({
      applicant: { dateOfBirth: "1990-01-01", firstName: "Jane" },
    }) as Record<string, Record<string, unknown>>;

    expect(result.applicant?.dateOfBirth).toBe("[REDACTED]");
    expect(result.applicant?.firstName).toBe("Jane");
  });

  it("masks a string value that looks like a pre-signed S3 URL, even under a non-sensitive key", () => {
    const url =
      "https://bucket.s3.amazonaws.com/key?X-Amz-Signature=abc123&X-Amz-Credential=xyz";

    const result = redact({ uploadUrl: url }) as Record<string, unknown>;

    expect(result.uploadUrl).toBe("[REDACTED]");
  });

  it("leaves non-sensitive fields untouched", () => {
    const result = redact({ applicationId: "app-1", status: "SUBMITTED" }) as Record<string, unknown>;

    expect(result).toEqual({ applicationId: "app-1", status: "SUBMITTED" });
  });

  it("redacts every element of an array under a sensitive key", () => {
    const result = redact({ bankAccountNumbers: ["1111", "2222"] }) as Record<string, unknown>;

    expect(result.bankAccountNumbers).toBe("[REDACTED]");
  });

  it("recurses into arrays of objects", () => {
    const result = redact({
      owners: [{ taxId: "12-3456789", name: "Jane" }, { taxId: "98-7654321", name: "John" }],
    }) as { owners: Array<Record<string, unknown>> };

    expect(result.owners[0]?.taxId).toBe("[REDACTED]");
    expect(result.owners[0]?.name).toBe("Jane");
    expect(result.owners[1]?.taxId).toBe("[REDACTED]");
  });

  it("does not throw on a circular reference and marks it instead", () => {
    const circular: Record<string, unknown> = { name: "Jane" };
    circular.self = circular;

    expect(() => redact(circular)).not.toThrow();
    const result = redact(circular) as Record<string, unknown>;
    expect(result.self).toBe("[CIRCULAR]");
  });

  it("passes through primitives unchanged", () => {
    expect(redact(42)).toBe(42);
    expect(redact(true)).toBe(true);
    expect(redact(null)).toBeNull();
  });
});

describe("redact — fully populated application fixture", () => {
  // Mirrors docs/04-SECURITY-RULES.md §2's "never appears in logs" list. This is the
  // required security test: proves the redaction utility, not developer discipline,
  // is what keeps sensitive values out of logs.
  const fixture = {
    applicationId: "app-123",
    applicant: {
      firstName: "Jane",
      lastName: "Testerson",
      dateOfBirth: "1985-06-15",
      governmentId: { type: "PASSPORT", number: "X1234567" },
    },
    business: {
      legalName: "Testerson Trading LLC",
      taxId: "00-0000000",
      bankAccount: { accountNumber: "000123456789", routingNumber: "021000021" },
    },
    presignedUrl: "https://bucket.s3.amazonaws.com/key?X-Amz-Signature=deadbeef",
    aiPrompt: "Summarize this business: sells widgets online",
  };

  it("produces output containing none of the sensitive raw values", () => {
    const output = JSON.stringify(redact(fixture));

    expect(output).not.toContain("1985-06-15");
    expect(output).not.toContain("X1234567");
    expect(output).not.toContain("00-0000000");
    expect(output).not.toContain("000123456789");
    expect(output).not.toContain("021000021");
    expect(output).not.toContain("X-Amz-Signature=deadbeef");
  });

  it("still preserves non-sensitive fields needed for debugging", () => {
    const output = JSON.stringify(redact(fixture));

    expect(output).toContain("app-123");
    expect(output).toContain("Testerson Trading LLC");
  });
});
