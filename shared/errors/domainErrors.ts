/**
 * Base of the domain error taxonomy. One mapper (httpErrorMapper.ts) turns these into
 * HTTP responses — handlers never build error responses by hand.
 */
export abstract class DomainError extends Error {
  abstract readonly code: string;
  abstract readonly httpStatus: number;
  abstract readonly retryable: boolean;
  readonly details?: unknown;

  constructor(message: string, details?: unknown) {
    super(message);
    this.name = new.target.name;
    this.details = details;
  }
}

export class ValidationError extends DomainError {
  readonly code = "VALIDATION_FAILED";
  readonly httpStatus = 400;
  readonly retryable = false;
}

export class NotFoundError extends DomainError {
  readonly code = "NOT_FOUND";
  readonly httpStatus = 404;
  readonly retryable = false;
}

/** Optimistic-concurrency / idempotency conflicts — a version mismatch, a replayed
 * request with different content, an illegal state transition. */
export class ConflictError extends DomainError {
  readonly code = "CONFLICT";
  readonly httpStatus = 409;
  readonly retryable = false;
}

export class DependencyTimeoutError extends DomainError {
  readonly code = "DEPENDENCY_TIMEOUT";
  readonly httpStatus = 504;
  readonly retryable = true;
}

export class DependencyUnavailableError extends DomainError {
  readonly code = "DEPENDENCY_UNAVAILABLE";
  readonly httpStatus = 503;
  readonly retryable = true;
}

/** A request that is well-formed but violates business/risk policy (e.g. a restricted
 * MCC). Distinct from ValidationError, which is about malformed input. */
export class PolicyViolationError extends DomainError {
  readonly code = "POLICY_VIOLATION";
  readonly httpStatus = 422;
  readonly retryable = false;
}

/**
 * Deployment/ops misconfiguration (missing required env var, invalid config value).
 * Deliberately NOT a DomainError: it is never a client-facing response, only a
 * cold-start failure that should surface loudly in CloudWatch.
 */
export class ConfigurationError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "ConfigurationError";
  }
}
