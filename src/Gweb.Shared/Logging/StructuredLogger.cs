using System.Text.Json;
using System.Text.Json.Nodes;
using Gweb.Shared.Correlation;

namespace Gweb.Shared.Logging;

public enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warn = 2,
    Error = 3,
}

/// <summary>
/// Structured JSON logger. Every call routes its fields through <see cref="Redactor"/>
/// before the line is written, so redaction is enforced by code rather than developer
/// discipline. Writes one JSON line per call to the given TextWriter (stdout by
/// default) — CloudWatch captures Lambda stdout directly, one line per log entry.
/// </summary>
public sealed class StructuredLogger(TextWriter output, LogLevel minLevel = LogLevel.Info)
{
    public void Debug(string @event, object? fields = null) => Write(LogLevel.Debug, @event, fields);
    public void Info(string @event, object? fields = null) => Write(LogLevel.Info, @event, fields);
    public void Warn(string @event, object? fields = null) => Write(LogLevel.Warn, @event, fields);
    public void Error(string @event, object? fields = null) => Write(LogLevel.Error, @event, fields);

    private void Write(LogLevel level, string @event, object? fields)
    {
        if (level < minLevel)
        {
            return;
        }

        var context = CorrelationScope.GetCurrent();
        var payload = new JsonObject
        {
            ["level"] = level.ToString().ToUpperInvariant(),
            ["timestamp"] = DateTimeOffset.UtcNow.ToString("O"),
            ["correlationId"] = context?.CorrelationId,
            ["applicationId"] = context?.ApplicationId,
            ["handler"] = context?.Handler,
            ["event"] = @event,
        };

        if (fields is not null)
        {
            var redactedFields = Redactor.Redact(fields) as JsonObject;
            if (redactedFields is not null)
            {
                foreach (var (key, value) in redactedFields)
                {
                    payload[key] = value?.DeepClone();
                }
            }
        }

        output.WriteLine(payload.ToJsonString());
    }
}
