namespace Gweb.Shared.Correlation;

public sealed record CorrelationContext(string CorrelationId, string Handler, string? ApplicationId = null);

/// <summary>
/// One execution environment handles one invocation at a time (AWS Lambda's execution
/// model), so a static AsyncLocal is safe: it never leaks context between concurrent
/// requests the way a naive static mutable field would on a real multi-request server.
/// </summary>
public static class CorrelationScope
{
    private static readonly AsyncLocal<CorrelationContext?> Current = new();

    public static T Run<T>(CorrelationContext context, Func<T> fn)
    {
        var previous = Current.Value;
        Current.Value = context;
        try
        {
            return fn();
        }
        finally
        {
            Current.Value = previous;
        }
    }

    public static CorrelationContext? GetCurrent() => Current.Value;
}
