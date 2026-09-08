import { getCorrelationContext } from "../correlation/correlationContext";
import { DomainError } from "./domainErrors";
import { logger } from "../logger/logger";

export interface ApiErrorBody {
  error: { code: string; message: string; details?: unknown };
  correlationId: string | undefined;
}

export interface ApiResponse {
  statusCode: number;
  headers: Record<string, string>;
  body: string;
}

/**
 * The one place domain errors become HTTP responses. Handlers never build error
 * bodies themselves. Never leaks stack traces, table/bucket names, or ARNs to the
 * client — those go to CloudWatch only, keyed by correlation ID.
 */
export function mapErrorToHttpResponse(error: unknown): ApiResponse {
  const correlationId = getCorrelationContext()?.correlationId;

  if (error instanceof DomainError) {
    const body: ApiErrorBody = {
      error: { code: error.code, message: error.message, details: error.details },
      correlationId,
    };
    return jsonResponse(error.httpStatus, body);
  }

  logger.error({ event: "unhandled_error", ...serializeUnknownError(error) });

  const body: ApiErrorBody = {
    error: { code: "INTERNAL_ERROR", message: "An unexpected error occurred." },
    correlationId,
  };
  return jsonResponse(500, body);
}

function jsonResponse(statusCode: number, body: unknown): ApiResponse {
  return {
    statusCode,
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  };
}

function serializeUnknownError(error: unknown): { errorName?: string; errorMessage?: string } {
  if (error instanceof Error) {
    return { errorName: error.name, errorMessage: error.message };
  }
  return { errorMessage: String(error) };
}
