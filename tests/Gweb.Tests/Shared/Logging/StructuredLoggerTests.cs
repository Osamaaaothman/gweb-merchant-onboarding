using System.Text.Json;
using Gweb.Shared.Correlation;
using Gweb.Shared.Logging;

namespace Gweb.Tests.Shared.Logging;

public class StructuredLoggerTests
{
    private static (StructuredLogger logger, StringWriter output) CreateLogger(LogLevel minLevel = LogLevel.Info)
    {
        var writer = new StringWriter();
        return (new StructuredLogger(writer, minLevel), writer);
    }

    private static List<string> Lines(StringWriter writer) =>
        writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).ToList();

    [Fact]
    public void WritesASingleJsonLinePerCallTaggedWithTheGivenLevel()
    {
        var (logger, output) = CreateLogger();

        logger.Info("request_received");

        var lines = Lines(output);
        Assert.Single(lines);
        var parsed = JsonDocument.Parse(lines[0]).RootElement;
        Assert.Equal("INFO", parsed.GetProperty("level").GetString());
        Assert.Equal("request_received", parsed.GetProperty("event").GetString());
    }

    [Fact]
    public void IncludesTheActiveCorrelationContextInEveryLogLine()
    {
        var (logger, output) = CreateLogger();

        CorrelationScope.Run(new CorrelationContext("corr-1", "test", "app-9"), () =>
        {
            logger.Warn("something_odd");
            return true;
        });

        var parsed = JsonDocument.Parse(Lines(output)[0]).RootElement;
        Assert.Equal("corr-1", parsed.GetProperty("correlationId").GetString());
        Assert.Equal("app-9", parsed.GetProperty("applicationId").GetString());
        Assert.Equal("test", parsed.GetProperty("handler").GetString());
    }

    [Fact]
    public void SuppressesLevelsBelowTheConfiguredMinimum()
    {
        var (logger, output) = CreateLogger(LogLevel.Warn);

        logger.Debug("should_be_dropped");
        logger.Info("should_also_be_dropped");
        logger.Error("should_appear");

        var lines = Lines(output);
        Assert.Single(lines);
        Assert.Equal("should_appear", JsonDocument.Parse(lines[0]).RootElement.GetProperty("event").GetString());
    }

    [Fact]
    public void RoutesArbitraryLogFieldsThroughRedaction()
    {
        var (logger, output) = CreateLogger();

        logger.Info("pii_test", new { ssn = "123-45-6789" });

        var line = Lines(output)[0];
        Assert.DoesNotContain("123-45-6789", line);
        Assert.Equal("[REDACTED]", JsonDocument.Parse(line).RootElement.GetProperty("ssn").GetString());
    }
}
