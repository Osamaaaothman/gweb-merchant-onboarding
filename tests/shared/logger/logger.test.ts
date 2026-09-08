import { logger, setLogLevel } from "../../../shared/logger/logger";
import { runWithCorrelationContext } from "../../../shared/correlation/correlationContext";

function captureStdout(fn: () => void): string[] {
  const lines: string[] = [];
  const original = process.stdout.write.bind(process.stdout);
  process.stdout.write = ((chunk: string) => {
    lines.push(chunk.toString());
    return true;
  }) as typeof process.stdout.write;

  try {
    fn();
  } finally {
    process.stdout.write = original;
  }
  return lines;
}

describe("logger", () => {
  afterEach(() => {
    setLogLevel("info");
  });

  it("writes a single JSON line per call, tagged with the given level", () => {
    const lines = captureStdout(() => logger.info({ event: "request_received" }));

    expect(lines).toHaveLength(1);
    const parsed = JSON.parse(lines[0] as string);
    expect(parsed.level).toBe("info");
    expect(parsed.event).toBe("request_received");
  });

  it("includes the active correlation context in every log line", () => {
    const lines = captureStdout(() =>
      runWithCorrelationContext({ correlationId: "corr-1", handler: "test", applicationId: "app-9" }, () =>
        logger.warn({ event: "something_odd" }),
      ),
    );

    const parsed = JSON.parse(lines[0] as string);
    expect(parsed.correlationId).toBe("corr-1");
    expect(parsed.applicationId).toBe("app-9");
    expect(parsed.handler).toBe("test");
  });

  it("suppresses levels below the configured minimum", () => {
    setLogLevel("warn");

    const lines = captureStdout(() => {
      logger.debug({ event: "should_be_dropped" });
      logger.info({ event: "should_also_be_dropped" });
      logger.error({ event: "should_appear" });
    });

    expect(lines).toHaveLength(1);
    expect(JSON.parse(lines[0] as string).event).toBe("should_appear");
  });

  it("routes arbitrary log fields through redaction", () => {
    const lines = captureStdout(() => logger.info({ event: "pii_test", ssn: "123-45-6789" }));

    expect(lines[0]).not.toContain("123-45-6789");
    expect(JSON.parse(lines[0] as string).ssn).toBe("[REDACTED]");
  });
});
