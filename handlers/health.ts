import type { APIGatewayProxyEventV2, APIGatewayProxyResultV2, Context } from "aws-lambda";
import { randomUUID } from "node:crypto";
import { runWithCorrelationContext } from "../shared/correlation/correlationContext";
import { logger } from "../shared/logger/logger";
import { DeadlineBudget } from "../shared/deadline/DeadlineBudget";
import { SystemClock } from "../shared/clock/SystemClock";
import { loadBaseConfig } from "../config/config";
import { mapErrorToHttpResponse } from "../shared/errors/httpErrorMapper";

// Loaded once at cold start, not per request.
const clock = new SystemClock();
const config = loadBaseConfig();

export async function handler(
  event: APIGatewayProxyEventV2,
  context: Context,
): Promise<APIGatewayProxyResultV2> {
  const correlationId = event.headers?.["x-correlation-id"] ?? randomUUID();

  return runWithCorrelationContext({ correlationId, handler: "health" }, async () => {
    try {
      const budget = DeadlineBudget.start({
        remainingLambdaTimeMs: context.getRemainingTimeInMillis(),
        targetMs: config.deadlineTargetMs,
        clock,
      });

      logger.info({ event: "request_received" });

      const body = {
        status: "ok",
        correlationId,
        remainingBudgetMs: budget.remainingMs(),
      };

      logger.info({ event: "request_completed", durationMs: budget.elapsedMs() });

      return {
        statusCode: 200,
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      };
    } catch (error) {
      return mapErrorToHttpResponse(error);
    }
  });
}
