namespace Gweb.Shared.Resilience;

/// <summary>
/// Injectable sleep for retry backoff -- same testability shape as IClock/FakeClock.
/// Tests substitute a fake that completes instantly instead of really sleeping, so a
/// multi-second backoff scenario runs in milliseconds.
/// </summary>
public interface IDelay
{
    Task WaitAsync(long delayMs, CancellationToken cancellationToken);
}
