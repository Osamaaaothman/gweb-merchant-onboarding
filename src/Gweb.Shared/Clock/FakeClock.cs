namespace Gweb.Shared.Clock;

/// <summary>
/// Controllable clock for tests. Lets deadline/timeout tests advance time in a single
/// call instead of actually sleeping, so a 45-second scenario runs in milliseconds.
/// </summary>
public sealed class FakeClock(long startAtMs = 0) : IClock
{
    private long _currentMs = startAtMs;

    public long NowMs() => _currentMs;

    public void AdvanceBy(long ms)
    {
        if (ms < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ms), ms, "FakeClock cannot advance by a negative duration");
        }
        _currentMs += ms;
    }

    public void SetTo(long ms) => _currentMs = ms;
}
