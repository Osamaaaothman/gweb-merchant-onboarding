using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;
using Gweb.Shared.Logging;

namespace Gweb.Config;

public enum AiProvider
{
    Mock,
    Gemini,
}

/// <summary>
/// Config every handler needs, regardless of which AWS resources it touches. Loaded
/// and validated once at process start (= Lambda cold start), so a bad deployment
/// fails loudly before the first request rather than on it.
/// </summary>
public sealed record BaseConfig(
    string AwsRegion,
    long DeadlineTargetMs,
    int PresignTtlSeconds,
    AiProvider AiProvider,
    string? AiApiKey,
    string GeminiModel,
    LogLevel LogLevel);

public static class AppConfigLoader
{
    public static BaseConfig LoadBaseConfig(Func<string, string?> getEnv)
    {
        return new BaseConfig(
            AwsRegion: getEnv("AWS_REGION") ?? "us-east-1",
            DeadlineTargetMs: ParsePositiveLong(getEnv("DEADLINE_TARGET_MS"), DeadlineBudget.DefaultTargetMs, "DEADLINE_TARGET_MS"),
            PresignTtlSeconds: (int)ParsePositiveLong(getEnv("PRESIGN_TTL_SECONDS"), 300, "PRESIGN_TTL_SECONDS"),
            AiProvider: ParseEnum(getEnv("AI_PROVIDER"), AiProvider.Mock, "AI_PROVIDER"),
            AiApiKey: getEnv("AI_API_KEY"),
            // gemini-3.6-flash confirmed working against the real API during this
            // phase's build (see docs/adr/0006-ai-evaluation-provider.md) -- an env
            // var, not a hardcoded model string, because Google's free-tier model
            // lineup has already moved once during this session and will again.
            GeminiModel: getEnv("GEMINI_MODEL") is { Length: > 0 } model ? model : "gemini-3.6-flash",
            LogLevel: ParseEnum(getEnv("LOG_LEVEL"), LogLevel.Info, "LOG_LEVEL"));
    }

    public static BaseConfig LoadBaseConfig(IDictionary<string, string?> env) =>
        LoadBaseConfig(name => env.TryGetValue(name, out var value) ? value : null);

    /// <summary>
    /// A handler-specific resource identifier (table name, bucket name, ...) that IaC
    /// injects as an env var. Call at process start so a missing binding fails at cold
    /// start, scoped only to the handlers that actually need that resource.
    /// </summary>
    public static string RequireEnv(string name, Func<string, string?> getEnv)
    {
        var value = getEnv(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ConfigurationException($"Missing required environment variable: {name}");
        }
        return value;
    }

    public static string RequireEnv(string name, IDictionary<string, string?> env) =>
        RequireEnv(name, n => env.TryGetValue(n, out var value) ? value : null);

    private static long ParsePositiveLong(string? raw, long fallback, string name)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }
        if (!long.TryParse(raw, out var parsed) || parsed <= 0)
        {
            throw new ConfigurationException($"Environment variable {name} must be a positive integer, got: {raw}");
        }
        return parsed;
    }

    private static TEnum ParseEnum<TEnum>(string? raw, TEnum fallback, string name) where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }
        if (!Enum.TryParse<TEnum>(raw, ignoreCase: true, out var parsed))
        {
            var allowed = string.Join(", ", Enum.GetNames<TEnum>());
            throw new ConfigurationException($"Environment variable {name} must be one of [{allowed}], got: {raw}");
        }
        return parsed;
    }
}
