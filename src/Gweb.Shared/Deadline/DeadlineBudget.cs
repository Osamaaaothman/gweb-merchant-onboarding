using Gweb.Shared.Clock;

namespace Gweb.Shared.Deadline;

/// <summary>
/// The single deadline primitive every handler/service/adapter uses. Created once at
/// handler entry from Lambda's actual remaining execution time, then threaded down
/// explicitly through every call that does I/O — never re-derived, never a fresh
/// ad-hoc timeout at the call site.
/// </summary>
public sealed class DeadlineBudget
{
    /// <summary>
    /// Assessment-imposed hard ceiling: no synchronous request may exceed this,
    /// including S3/AI/third-party handshakes. Not configurable — it is a contract
    /// with the caller, not a tuning knob.
    /// </summary>
    public const long HardCeilingMs = 45_000;

    /// <summary>
    /// Internal default target, below the hard ceiling, leaving headroom for cleanup
    /// and response serialization. Overridable via config (DEADLINE_TARGET_MS) but
    /// validated against the ceiling in <see cref="Start"/>.
    /// </summary>
    public const long DefaultTargetMs = 35_000;

    private readonly IClock _clock;

    public long StartedAtMs { get; }
    public long DeadlineAtMs { get; }

    private DeadlineBudget(long startedAtMs, long deadlineAtMs, IClock clock)
    {
        StartedAtMs = startedAtMs;
        DeadlineAtMs = deadlineAtMs;
        _clock = clock;
    }

    public static DeadlineBudget Start(long remainingLambdaTimeMs, IClock clock, long? targetMs = null)
    {
        var target = targetMs ?? DefaultTargetMs;
        if (target > HardCeilingMs)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetMs), target, $"Deadline target ({target}ms) must not exceed the hard ceiling ({HardCeilingMs}ms)");
        }
        if (target <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetMs), target, "Deadline target must be positive");
        }

        // The Lambda invocation itself may have less time left than our internal target
        // (e.g. a retry deeper into an already-long invocation) — always take the smaller.
        var effectiveMs = Math.Max(0, Math.Min(target, remainingLambdaTimeMs));
        var startedAtMs = clock.NowMs();
        return new DeadlineBudget(startedAtMs, startedAtMs + effectiveMs, clock);
    }

    /// <summary>Milliseconds left until the internal deadline. Never negative.</summary>
    public long RemainingMs() => Math.Max(0, DeadlineAtMs - _clock.NowMs());

    /// <summary>Milliseconds elapsed since the budget was started.</summary>
    public long ElapsedMs() => _clock.NowMs() - StartedAtMs;

    public bool IsExpired() => RemainingMs() <= 0;

    /// <summary>
    /// Timeout budget for a single outbound call: remaining time minus a reserve kept
    /// for cleanup/response serialization, capped at an optional per-call maximum.
    /// Never negative — callers should treat 0 as "do not attempt this call."
    /// </summary>
    public long ForCall(long reserveMs, long? perCallMaxMs = null)
    {
        var available = RemainingMs() - reserveMs;
        var capped = perCallMaxMs is null ? available : Math.Min(available, perCallMaxMs.Value);
        return Math.Max(0, capped);
    }

    /// <summary>True if there is enough budget left to attempt another call plus the reserve.</summary>
    public bool CanAttempt(long reserveMs) => RemainingMs() > reserveMs;
}
