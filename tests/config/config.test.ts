import { loadBaseConfig, requireEnv } from "../../config/config";
import { ConfigurationError } from "../../shared/errors/domainErrors";

describe("loadBaseConfig", () => {
  it("applies documented defaults when no environment variables are set", () => {
    const config = loadBaseConfig({});

    expect(config.awsRegion).toBe("us-east-1");
    expect(config.deadlineTargetMs).toBe(35_000);
    expect(config.presignTtlSeconds).toBe(300);
    expect(config.aiProvider).toBe("mock");
    expect(config.logLevel).toBe("info");
    expect(config.aiApiKey).toBeUndefined();
  });

  it("reads overrides from the provided environment", () => {
    const config = loadBaseConfig({
      AWS_REGION: "eu-west-1",
      DEADLINE_TARGET_MS: "20000",
      PRESIGN_TTL_SECONDS: "120",
      AI_PROVIDER: "anthropic",
      AI_API_KEY: "test-key-not-a-real-secret",
      LOG_LEVEL: "debug",
    });

    expect(config.awsRegion).toBe("eu-west-1");
    expect(config.deadlineTargetMs).toBe(20_000);
    expect(config.presignTtlSeconds).toBe(120);
    expect(config.aiProvider).toBe("anthropic");
    expect(config.aiApiKey).toBe("test-key-not-a-real-secret");
    expect(config.logLevel).toBe("debug");
  });

  it("fails fast when DEADLINE_TARGET_MS is not a positive integer", () => {
    expect(() => loadBaseConfig({ DEADLINE_TARGET_MS: "not-a-number" })).toThrow(ConfigurationError);
    expect(() => loadBaseConfig({ DEADLINE_TARGET_MS: "-5" })).toThrow(ConfigurationError);
    expect(() => loadBaseConfig({ DEADLINE_TARGET_MS: "0" })).toThrow(ConfigurationError);
  });

  it("fails fast when AI_PROVIDER is not a recognized value", () => {
    expect(() => loadBaseConfig({ AI_PROVIDER: "openai" })).toThrow(ConfigurationError);
  });

  it("fails fast when LOG_LEVEL is not a recognized value", () => {
    expect(() => loadBaseConfig({ LOG_LEVEL: "verbose" })).toThrow(ConfigurationError);
  });
});

describe("requireEnv", () => {
  it("returns the value when the variable is set", () => {
    expect(requireEnv("TABLE_NAME", { TABLE_NAME: "gweb-applications-dev" })).toBe(
      "gweb-applications-dev",
    );
  });

  it("throws ConfigurationError when the variable is missing", () => {
    expect(() => requireEnv("TABLE_NAME", {})).toThrow(ConfigurationError);
  });

  it("throws ConfigurationError when the variable is an empty string", () => {
    expect(() => requireEnv("TABLE_NAME", { TABLE_NAME: "   " })).toThrow(ConfigurationError);
  });
});
