using Gweb.Shared.Clock;
using Gweb.Shared.Deadline;
using Gweb.Shared.Errors;
using Gweb.Shared.Resilience;

namespace Gweb.Tests.Shared.Resilience;

public class BoundedRetryTests
{
    private static DeadlineBudget Budget(FakeClock clock, long remainingMs = 35_000) =>
        DeadlineBudget.Start(remainingMs, clock, targetMs: 35_000);

    [Fact]
    public async Task ReturnsTheResultOnTheFirstAttemptWithoutRetryingWhenItSucceeds()
    {
        var clock = new FakeClock(0);
        var delay = new FakeDelay(clock);
        var attempts = 0;

        var result = await BoundedRetry.ExecuteAsync(
            () => { attempts++; return Task.FromResult("ok"); },
            Budget(clock),
            delay,
            CancellationToken.None);

        Assert.Equal("ok", result);
        Assert.Equal(1, attempts);
        Assert.Empty(delay.RequestedDelaysMs);
    }

    [Fact]
    public async Task RetriesATransientFailureAndReturnsTheEventualSuccess()
    {
        var clock = new FakeClock(0);
        var delay = new FakeDelay(clock);
        var attempts = 0;

        var result = await BoundedRetry.ExecuteAsync(
            () =>
            {
                attempts++;
                if (attempts < 3)
                {
                    throw new DependencyUnavailableException("transient");
                }
                return Task.FromResult("ok");
            },
            Budget(clock),
            delay,
            CancellationToken.None);

        Assert.Equal("ok", result);
        Assert.Equal(3, attempts);
        Assert.Equal(2, delay.RequestedDelaysMs.Count);
    }

    [Fact]
    public async Task NeverRetriesANonRetryableDomainException()
    {
        var clock = new FakeClock(0);
        var delay = new FakeDelay(clock);
        var attempts = 0;

        await Assert.ThrowsAsync<ValidationException>(() => BoundedRetry.ExecuteAsync<string>(
            () => { attempts++; throw new ValidationException("bad input"); },
            Budget(clock),
            delay,
            CancellationToken.None));

        Assert.Equal(1, attempts);
        Assert.Empty(delay.RequestedDelaysMs);
    }

    [Fact]
    public async Task StopsAfterMaxAttemptsAndRethrowsTheLastFailure()
    {
        var clock = new FakeClock(0);
        var delay = new FakeDelay(clock);
        var attempts = 0;

        await Assert.ThrowsAsync<DependencyTimeoutException>(() => BoundedRetry.ExecuteAsync<string>(
            () => { attempts++; throw new DependencyTimeoutException("still slow"); },
            Budget(clock),
            delay,
            CancellationToken.None,
            maxAttempts: 3));

        Assert.Equal(3, attempts);
        Assert.Equal(2, delay.RequestedDelaysMs.Count);
    }

    [Fact]
    public async Task NeverAttemptsARetryOnceBackoffWouldConsumeMoreBudgetThanTheReserveAllows()
    {
        // A FakeDelay wired to the same FakeClock the budget reads from -- each
        // backoff actually consumes budget, the same way real elapsed time would in
        // production. With hardly any budget left, the very first retry's delay
        // exhausts it, so a second attempt never happens.
        var clock = new FakeClock(0);
        var delay = new FakeDelay(clock);
        var attempts = 0;

        await Assert.ThrowsAsync<DependencyUnavailableException>(() => BoundedRetry.ExecuteAsync<string>(
            () => { attempts++; throw new DependencyUnavailableException("transient"); },
            Budget(clock, remainingMs: 150),
            delay,
            CancellationToken.None));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task RejectsAMaxAttemptsBelowOne()
    {
        var clock = new FakeClock(0);
        var delay = new FakeDelay(clock);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => BoundedRetry.ExecuteAsync<string>(
            () => Task.FromResult("unreachable"),
            Budget(clock),
            delay,
            CancellationToken.None,
            maxAttempts: 0));
    }
}
