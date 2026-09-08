namespace Gweb.Shared.Clock;

public sealed class SystemClock : IClock
{
    public long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
