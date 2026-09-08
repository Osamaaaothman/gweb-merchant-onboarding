import type { APIGatewayProxyEventV2, Context } from "aws-lambda";
import { handler } from "../../handlers/health";

function fakeContext(remainingTimeMs: number): Context {
  return {
    getRemainingTimeInMillis: () => remainingTimeMs,
  } as unknown as Context;
}

function fakeEvent(headers: Record<string, string> = {}): APIGatewayProxyEventV2 {
  return { headers } as unknown as APIGatewayProxyEventV2;
}

describe("health handler", () => {
  it("returns 200 with an ok status", async () => {
    const result = await handler(fakeEvent(), fakeContext(35_000));

    expect(result).toMatchObject({ statusCode: 200 });
    const body = JSON.parse((result as { body: string }).body);
    expect(body.status).toBe("ok");
  });

  it("echoes back the caller-supplied correlation ID", async () => {
    const result = await handler(fakeEvent({ "x-correlation-id": "req-42" }), fakeContext(35_000));

    const body = JSON.parse((result as { body: string }).body);
    expect(body.correlationId).toBe("req-42");
  });

  it("generates a correlation ID when the caller does not supply one", async () => {
    const result = await handler(fakeEvent(), fakeContext(35_000));

    const body = JSON.parse((result as { body: string }).body);
    expect(typeof body.correlationId).toBe("string");
    expect(body.correlationId.length).toBeGreaterThan(0);
  });

  it("reports a remaining budget bounded by the Lambda time actually left", async () => {
    const result = await handler(fakeEvent(), fakeContext(1_000));

    const body = JSON.parse((result as { body: string }).body);
    expect(body.remainingBudgetMs).toBeLessThanOrEqual(1_000);
  });
});
