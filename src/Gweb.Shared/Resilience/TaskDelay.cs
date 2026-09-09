namespace Gweb.Shared.Resilience;

/// <summary>Real IDelay -- production default, wraps Task.Delay.</summary>
public sealed class TaskDelay : IDelay
{
    public Task WaitAsync(long delayMs, CancellationToken cancellationToken) =>
        Task.Delay(TimeSpan.FromMilliseconds(delayMs), cancellationToken);
}
