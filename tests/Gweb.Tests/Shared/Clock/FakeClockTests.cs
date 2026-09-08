using Gweb.Shared.Clock;

namespace Gweb.Tests.Shared.Clock;

public class FakeClockTests
{
    [Fact]
    public void StartsAtTheGivenTimeAndNeverMovesOnItsOwn()
    {
        var clock = new FakeClock(1_000);

        Assert.Equal(1_000, clock.NowMs());
        Assert.Equal(1_000, clock.NowMs());
    }

    [Fact]
    public void AdvancesByTheGivenDuration()
    {
        var clock = new FakeClock(0);

        clock.AdvanceBy(500);

        Assert.Equal(500, clock.NowMs());
    }

    [Fact]
    public void RejectsAdvancingByANegativeDuration()
    {
        var clock = new FakeClock(0);

        Assert.Throws<ArgumentOutOfRangeException>(() => clock.AdvanceBy(-1));
    }

    [Fact]
    public void CanBeSetToAnArbitraryPointInTime()
    {
        var clock = new FakeClock(0);

        clock.SetTo(99_999);

        Assert.Equal(99_999, clock.NowMs());
    }
}
