import { mapErrorToHttpResponse } from "../../../shared/errors/httpErrorMapper";
import {
  ValidationError,
  ConflictError,
  DependencyTimeoutError,
  NotFoundError,
  DependencyUnavailableError,
  PolicyViolationError,
} from "../../../shared/errors/domainErrors";
import { runWithCorrelationContext } from "../../../shared/correlation/correlationContext";

describe("mapErrorToHttpResponse", () => {
  it("maps a ValidationError to 400 with its code and message", () => {
    const response = mapErrorToHttpResponse(new ValidationError("email is required"));

    expect(response.statusCode).toBe(400);
    const body = JSON.parse(response.body);
    expect(body.error.code).toBe("VALIDATION_FAILED");
    expect(body.error.message).toBe("email is required");
  });

  it("maps a ConflictError to 409", () => {
    const response = mapErrorToHttpResponse(new ConflictError("version mismatch"));

    expect(response.statusCode).toBe(409);
    expect(JSON.parse(response.body).error.code).toBe("CONFLICT");
  });

  it("maps a DependencyTimeoutError to 504", () => {
    const response = mapErrorToHttpResponse(new DependencyTimeoutError("AI provider timed out"));

    expect(response.statusCode).toBe(504);
    expect(JSON.parse(response.body).error.code).toBe("DEPENDENCY_TIMEOUT");
  });

  it("maps a NotFoundError to 404", () => {
    const response = mapErrorToHttpResponse(new NotFoundError("application not found"));

    expect(response.statusCode).toBe(404);
    expect(JSON.parse(response.body).error.code).toBe("NOT_FOUND");
  });

  it("maps a DependencyUnavailableError to 503", () => {
    const response = mapErrorToHttpResponse(new DependencyUnavailableError("DynamoDB unreachable"));

    expect(response.statusCode).toBe(503);
    expect(JSON.parse(response.body).error.code).toBe("DEPENDENCY_UNAVAILABLE");
  });

  it("maps a PolicyViolationError to 422", () => {
    const response = mapErrorToHttpResponse(new PolicyViolationError("MCC 6051 is restricted"));

    expect(response.statusCode).toBe(422);
    expect(JSON.parse(response.body).error.code).toBe("POLICY_VIOLATION");
  });

  it("marks DependencyTimeoutError and DependencyUnavailableError as retryable, and the rest as not", () => {
    expect(new DependencyTimeoutError("x").retryable).toBe(true);
    expect(new DependencyUnavailableError("x").retryable).toBe(true);
    expect(new ValidationError("x").retryable).toBe(false);
    expect(new ConflictError("x").retryable).toBe(false);
    expect(new NotFoundError("x").retryable).toBe(false);
    expect(new PolicyViolationError("x").retryable).toBe(false);
  });

  it("includes the details payload when the error carries one", () => {
    const response = mapErrorToHttpResponse(
      new ValidationError("invalid fields", [{ field: "dob", reason: "must be in the past" }]),
    );

    expect(JSON.parse(response.body).error.details).toEqual([{ field: "dob", reason: "must be in the past" }]);
  });

  it("carries the active correlation ID", () => {
    const response = runWithCorrelationContext(
      { correlationId: "corr-abc", handler: "test" },
      () => mapErrorToHttpResponse(new ValidationError("bad input")),
    );

    expect(JSON.parse(response.body).correlationId).toBe("corr-abc");
  });

  it("maps an unrecognized error to 500 without leaking its message or stack", () => {
    const response = mapErrorToHttpResponse(new Error("connection string: postgres://user:pw@host/db"));

    expect(response.statusCode).toBe(500);
    const body = JSON.parse(response.body);
    expect(body.error.code).toBe("INTERNAL_ERROR");
    expect(response.body).not.toContain("postgres://user:pw@host/db");
  });

  it("maps a thrown non-Error value to a safe 500 as well", () => {
    const response = mapErrorToHttpResponse("just a string throw");

    expect(response.statusCode).toBe(500);
    expect(JSON.parse(response.body).error.code).toBe("INTERNAL_ERROR");
  });
});
