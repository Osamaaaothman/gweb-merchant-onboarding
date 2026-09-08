namespace Gweb.Shared.Clock;

public interface IClock
{
    /// <summary>Current time in epoch milliseconds.</summary>
    long NowMs();
}
