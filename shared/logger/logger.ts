import { getCorrelationContext } from "../correlation/correlationContext";
import { redact } from "./redact";

export type LogLevel = "debug" | "info" | "warn" | "error";

export interface LogFields {
  event: string;
  [key: string]: unknown;
}

const LEVEL_ORDER: Record<LogLevel, number> = { debug: 0, info: 1, warn: 2, error: 3 };

let minLevel: LogLevel = "info";

export function setLogLevel(level: LogLevel): void {
  minLevel = level;
}

function write(level: LogLevel, fields: LogFields): void {
  if (LEVEL_ORDER[level] < LEVEL_ORDER[minLevel]) {
    return;
  }
  const context = getCorrelationContext();
  const payload = {
    level,
    timestamp: new Date().toISOString(),
    correlationId: context?.correlationId,
    applicationId: context?.applicationId,
    handler: context?.handler,
    ...redact(fields),
  };
  // Structured JSON logs only, one line per event. Uses process.stdout.write rather
  // than console.* so the repo-wide no-console lint rule stays meaningful everywhere
  // else without needing a per-file override.
  process.stdout.write(`${JSON.stringify(payload)}\n`);
}

export const logger = {
  debug: (fields: LogFields): void => write("debug", fields),
  info: (fields: LogFields): void => write("info", fields),
  warn: (fields: LogFields): void => write("warn", fields),
  error: (fields: LogFields): void => write("error", fields),
};
