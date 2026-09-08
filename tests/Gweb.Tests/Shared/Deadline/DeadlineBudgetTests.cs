using Gweb.Shared.Clock;
using Gweb.Shared.Deadline;

namespace Gweb.Tests.Shared.Deadline;

public class DeadlineBudgetStartTests
{
    [Fact]
    public void RejectsATargetAboveTheFortyFiveSecondHardCeiling()
    {
        var clock = new FakeClock(0);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DeadlineBudget.Start(60_000, clock, targetMs: 46_000));
    }

    [Fact]
    public void RejectsANonPositiveTarget()
    {
        var clock = new FakeClock(0);

        Assert.Throws<ArgumentOutOfRangeException>(() => DeadlineBudget.Start(60_000, clock, targetMs: 0));
    }

    [Fact]
    public void UsesTheInternalTargetWhenLambdaHasMoreTimeRemainingThanTheTarget()
    {
        var clock = new FakeClock(0);

        var budget = DeadlineBudget.Start(60_000, clock, targetMs: 35_000);

        Assert.Equal(35_000, budget.RemainingMs());
    }

    [Fact]
    public void UsesTheRemainingLambdaTimeWhenItIsLessThanTheInternalTarget()
    {
        // e.g. a request arriving late into an already-long-running invocation.
        var clock = new FakeClock(0);

        var budget = DeadlineBudget.Start(10_000, clock, targetMs: 35_000);

        Assert.Equal(10_000, budget.RemainingMs());
    }

    [Fact]
    public void NeverDerivesAnEffectiveBudgetAboveTheHardCeilingEvenWithAGenerousTarget()
    {
        var clock = new FakeClock(0);

        var budget = DeadlineBudget.Start(DeadlineBudget.HardCeilingMs, clock, targetMs: DeadlineBudget.HardCeilingMs);

        Assert.True(budget.RemainingMs() <= DeadlineBudget.HardCeilingMs);
    }
}

public class DeadlineBudgetRemainingMsTests
{
    [Fact]
    public void DecreasesAsTheClockAdvances()
    {
        var clock = new FakeClock(0);
        var budget = DeadlineBudget.Start(35_000, clock, targetMs: 35_000);

        clock.AdvanceBy(10_000);

        Assert.Equal(25_000, budget.RemainingMs());
    }

    [Fact]
    public void NeverGoesNegativeOnceTheDeadlineHasPassed()
    {
        var clock = new FakeClock(0);
        var budget = DeadlineBudget.Start(5_000, clock, targetMs: 5_000);

        clock.AdvanceBy(50_000);

        Assert.Equal(0, budget.RemainingMs());
    }
}

public class DeadlineBudgetIsExpiredTests
{
    [Fact]
    public void IsFalseWhileTimeRemains()
    {
        var clock = new FakeClock(0);
        var budget = DeadlineBudget.Start(5_000, clock, targetMs: 5_000);

        Assert.False(budget.IsExpired());
    }

    [Fact]
    public void IsTrueOnceTheDeadlineHasPassed()
    {
        var clock = new FakeClock(0);
        var budget = DeadlineBudget.Start(5_000, clock, targetMs: 5_000);

        clock.AdvanceBy(5_000);

        Assert.True(budget.IsExpired());
    }
}

public class DeadlineBudgetForCallTests
{
    [Fact]
    public void SubtractsTheReserveFromTheRemainingTime()
    {
        var clock = new FakeClock(0);
        var budget = DeadlineBudget.Start(10_000, clock, targetMs: 10_000);

        Assert.Equal(8_000, budget.ForCall(2_000));
    }

    [Fact]
    public void CapsTheResultAtTheGivenPerCallMaximum()
    {
        var clock = new FakeClock(0);
        var budget = DeadlineBudget.Start(10_000, clock, targetMs: 10_000);

        Assert.Equal(3_000, budget.ForCall(1_000, 3_000));
    }

    [Fact]
    public void NeverReturnsANegativeBudgetWhenTheReserveExceedsRemainingTime()
    {
        var clock = new FakeClock(0);
        var budget = DeadlineBudget.Start(1_000, clock, targetMs: 1_000);

        Assert.Equal(0, budget.ForCall(5_000));
    }
}

public class DeadlineBudgetCanAttemptTests
{
    [Fact]
    public void IsTrueWhenRemainingTimeExceedsTheReserve()
    {
        var clock = new FakeClock(0);
        var budget = DeadlineBudget.Start(10_000, clock, targetMs: 10_000);

        Assert.True(budget.CanAttempt(2_000));
    }

    [Fact]
    public void IsFalseOnceRemainingTimeNoLongerExceedsTheReserve()
    {
        var clock = new FakeClock(0);
        var budget = DeadlineBudget.Start(2_000, clock, targetMs: 2_000);

        clock.AdvanceBy(1_500);

        Assert.False(budget.CanAttempt(1_000));
    }
}
