import { ConfigurationError } from "../shared/errors/domainErrors";
import { DEFAULT_DEADLINE_TARGET_MS } from "../shared/deadline/DeadlineBudget";
import type { LogLevel } from "../shared/logger/logger";

export type AiProvider = "mock" | "anthropic";

/**
 * Config every handler needs, regardless of which AWS resources it touches.
 * Loaded and validated once at module load time (= Lambda cold start), so a bad
 * deployment fails loudly before the first request rather than on it.
 */
export interface BaseConfig {
  awsRegion: string;
  deadlineTargetMs: number;
  presignTtlSeconds: number;
  aiProvider: AiProvider;
  aiApiKey: string | undefined;
  logLevel: LogLevel;
}

const LOG_LEVELS: readonly LogLevel[] = ["debug", "info", "warn", "error"];
const AI_PROVIDERS: readonly AiProvider[] = ["mock", "anthropic"];

export function loadBaseConfig(env: NodeJS.ProcessEnv = process.env): BaseConfig {
  return {
    awsRegion: env.AWS_REGION ?? "us-east-1",
    deadlineTargetMs: parsePositiveInt(env.DEADLINE_TARGET_MS, DEFAULT_DEADLINE_TARGET_MS, "DEADLINE_TARGET_MS"),
    presignTtlSeconds: parsePositiveInt(env.PRESIGN_TTL_SECONDS, 300, "PRESIGN_TTL_SECONDS"),
    aiProvider: parseEnum(env.AI_PROVIDER, AI_PROVIDERS, "mock", "AI_PROVIDER"),
    aiApiKey: env.AI_API_KEY,
    logLevel: parseEnum(env.LOG_LEVEL, LOG_LEVELS, "info", "LOG_LEVEL"),
  };
}

/**
 * A handler-specific resource identifier (table name, bucket name, ...) that IaC
 * injects as an env var. Call at module load time so a missing binding fails at cold
 * start, scoped only to the handlers that actually need that resource.
 */
export function requireEnv(name: string, env: NodeJS.ProcessEnv = process.env): string {
  const value = env[name];
  if (value === undefined || value.trim() === "") {
    throw new ConfigurationError(`Missing required environment variable: ${name}`);
  }
  return value;
}

function parsePositiveInt(raw: string | undefined, fallback: number, name: string): number {
  if (raw === undefined || raw.trim() === "") {
    return fallback;
  }
  const parsed = Number(raw);
  if (!Number.isInteger(parsed) || parsed <= 0) {
    throw new ConfigurationError(`Environment variable ${name} must be a positive integer, got: ${raw}`);
  }
  return parsed;
}

function parseEnum<T extends string>(
  raw: string | undefined,
  allowed: readonly T[],
  fallback: T,
  name: string,
): T {
  if (raw === undefined || raw.trim() === "") {
    return fallback;
  }
  if (!allowed.includes(raw as T)) {
    throw new ConfigurationError(`Environment variable ${name} must be one of [${allowed.join(", ")}], got: ${raw}`);
  }
  return raw as T;
}
