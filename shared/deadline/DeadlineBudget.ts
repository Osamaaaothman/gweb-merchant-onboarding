import type { IClock } from "../clock/IClock";

/**
 * Assessment-imposed hard ceiling: no synchronous request may exceed this, including
 * S3/AI/third-party handshakes. Not configurable — it is a contract with the caller,
 * not a tuning knob.
 */
export const DEADLINE_HARD_CEILING_MS = 45_000;

/** Internal default target, below the hard ceiling, leaving headroom for cleanup and
 * response serialization. Overridable via config (DEADLINE_TARGET_MS) but validated
 * against the ceiling in DeadlineBudget.start. */
export const DEFAULT_DEADLINE_TARGET_MS = 35_000;

export interface DeadlineBudgetOptions {
  /** From `context.getRemainingTimeInMillis()` at handler entry. */
  remainingLambdaTimeMs: number;
  /** Internal target in ms; must not exceed DEADLINE_HARD_CEILING_MS. */
  targetMs?: number;
  clock: IClock;
}

/**
 * The single deadline primitive every handler/service/adapter uses. Created once at
 * handler entry from Lambda's actual remaining execution time, then threaded down
 * explicitly through every call that does I/O — never re-derived, never a fresh
 * ad-hoc timeout at the call site.
 */
export class DeadlineBudget {
  readonly startedAtMs: number;
  readonly deadlineAtMs: number;
  private readonly clock: IClock;

  private constructor(startedAtMs: number, deadlineAtMs: number, clock: IClock) {
    this.startedAtMs = startedAtMs;
    this.deadlineAtMs = deadlineAtMs;
    this.clock = clock;
  }

  static start(options: DeadlineBudgetOptions): DeadlineBudget {
    const targetMs = options.targetMs ?? DEFAULT_DEADLINE_TARGET_MS;
    if (targetMs > DEADLINE_HARD_CEILING_MS) {
      throw new RangeError(
        `Deadline target (${targetMs}ms) must not exceed the hard ceiling (${DEADLINE_HARD_CEILING_MS}ms)`,
      );
    }
    if (targetMs <= 0) {
      throw new RangeError(`Deadline target must be positive, got ${targetMs}ms`);
    }

    // The Lambda invocation itself may have less time left than our internal target
    // (e.g. a retry deeper into an already-long invocation) — always take the smaller.
    const effectiveMs = Math.max(0, Math.min(targetMs, options.remainingLambdaTimeMs));
    const startedAtMs = options.clock.now();
    return new DeadlineBudget(startedAtMs, startedAtMs + effectiveMs, options.clock);
  }

  /** Milliseconds left until the internal deadline. Never negative. */
  remainingMs(): number {
    return Math.max(0, this.deadlineAtMs - this.clock.now());
  }

  /** Milliseconds elapsed since the budget was started. */
  elapsedMs(): number {
    return this.clock.now() - this.startedAtMs;
  }

  isExpired(): boolean {
    return this.remainingMs() <= 0;
  }

  /**
   * Timeout budget for a single outbound call: remaining time minus a reserve kept
   * for cleanup/response serialization, capped at an optional per-call maximum.
   * Never negative — callers should treat 0 as "do not attempt this call."
   */
  forCall(reserveMs: number, perCallMaxMs?: number): number {
    const available = this.remainingMs() - reserveMs;
    const capped = perCallMaxMs === undefined ? available : Math.min(available, perCallMaxMs);
    return Math.max(0, capped);
  }

  /** True if there is enough budget left to attempt another call plus the reserve. */
  canAttempt(reserveMs: number): boolean {
    return this.remainingMs() > reserveMs;
  }
}
