using Gweb.Config;
using Gweb.Shared.Errors;
using Gweb.Shared.Logging;

namespace Gweb.Tests.Config;

public class LoadBaseConfigTests
{
    [Fact]
    public void AppliesDocumentedDefaultsWhenNoEnvironmentVariablesAreSet()
    {
        var config = AppConfigLoader.LoadBaseConfig(new Dictionary<string, string?>());

        Assert.Equal("us-east-1", config.AwsRegion);
        Assert.Equal(35_000, config.DeadlineTargetMs);
        Assert.Equal(300, config.PresignTtlSeconds);
        Assert.Equal(AiProvider.Mock, config.AiProvider);
        Assert.Equal(LogLevel.Info, config.LogLevel);
        Assert.Null(config.AiApiKey);
        Assert.Equal("gemini-3.6-flash", config.GeminiModel);
    }

    [Fact]
    public void ReadsOverridesFromTheProvidedEnvironment()
    {
        var env = new Dictionary<string, string?>
        {
            ["AWS_REGION"] = "eu-west-1",
            ["DEADLINE_TARGET_MS"] = "20000",
            ["PRESIGN_TTL_SECONDS"] = "120",
            ["AI_PROVIDER"] = "Gemini",
            ["AI_API_KEY"] = "test-key-not-a-real-secret",
            ["GEMINI_MODEL"] = "gemini-test-model",
            ["LOG_LEVEL"] = "Debug",
        };

        var config = AppConfigLoader.LoadBaseConfig(env);

        Assert.Equal("eu-west-1", config.AwsRegion);
        Assert.Equal(20_000, config.DeadlineTargetMs);
        Assert.Equal(120, config.PresignTtlSeconds);
        Assert.Equal(AiProvider.Gemini, config.AiProvider);
        Assert.Equal("test-key-not-a-real-secret", config.AiApiKey);
        Assert.Equal("gemini-test-model", config.GeminiModel);
        Assert.Equal(LogLevel.Debug, config.LogLevel);
    }

    [Fact]
    public void FailsFastWhenDeadlineTargetMsIsNotAPositiveInteger()
    {
        Assert.Throws<ConfigurationException>(() =>
            AppConfigLoader.LoadBaseConfig(new Dictionary<string, string?> { ["DEADLINE_TARGET_MS"] = "not-a-number" }));
        Assert.Throws<ConfigurationException>(() =>
            AppConfigLoader.LoadBaseConfig(new Dictionary<string, string?> { ["DEADLINE_TARGET_MS"] = "-5" }));
        Assert.Throws<ConfigurationException>(() =>
            AppConfigLoader.LoadBaseConfig(new Dictionary<string, string?> { ["DEADLINE_TARGET_MS"] = "0" }));
    }

    [Fact]
    public void FailsFastWhenAiProviderIsNotARecognizedValue()
    {
        Assert.Throws<ConfigurationException>(() =>
            AppConfigLoader.LoadBaseConfig(new Dictionary<string, string?> { ["AI_PROVIDER"] = "openai" }));
    }

    [Fact]
    public void FailsFastWhenLogLevelIsNotARecognizedValue()
    {
        Assert.Throws<ConfigurationException>(() =>
            AppConfigLoader.LoadBaseConfig(new Dictionary<string, string?> { ["LOG_LEVEL"] = "verbose" }));
    }
}

public class RequireEnvTests
{
    [Fact]
    public void ReturnsTheValueWhenTheVariableIsSet()
    {
        var result = AppConfigLoader.RequireEnv("TABLE_NAME", new Dictionary<string, string?> { ["TABLE_NAME"] = "gweb-applications-dev" });

        Assert.Equal("gweb-applications-dev", result);
    }

    [Fact]
    public void ThrowsConfigurationExceptionWhenTheVariableIsMissing()
    {
        Assert.Throws<ConfigurationException>(() => AppConfigLoader.RequireEnv("TABLE_NAME", new Dictionary<string, string?>()));
    }

    [Fact]
    public void ThrowsConfigurationExceptionWhenTheVariableIsAnEmptyString()
    {
        Assert.Throws<ConfigurationException>(() =>
            AppConfigLoader.RequireEnv("TABLE_NAME", new Dictionary<string, string?> { ["TABLE_NAME"] = "   " }));
    }
}
