using Gweb.Shared.Clock;

namespace Gweb.Shared.Resilience;

/// <summary>
/// Controllable IDelay for tests. Completes instantly instead of really sleeping --
/// optionally advancing a shared FakeClock by the requested amount first, so a test
/// using a FakeClock-backed DeadlineBudget sees retry backoff actually consume budget,
/// the same way it would under SystemClock in production.
/// </summary>
public sealed class FakeDelay(FakeClock? clock = null) : IDelay
{
    public List<long> RequestedDelaysMs { get; } = [];

    public Task WaitAsync(long delayMs, CancellationToken cancellationToken)
    {
        RequestedDelaysMs.Add(delayMs);
        clock?.AdvanceBy(delayMs);
        return Task.CompletedTask;
    }
}
