import { DeadlineBudget, DEADLINE_HARD_CEILING_MS } from "../../../shared/deadline/DeadlineBudget";
import { FakeClock } from "../../../shared/clock/FakeClock";

describe("DeadlineBudget.start", () => {
  it("rejects a target above the 45-second hard ceiling", () => {
    const clock = new FakeClock(0);

    expect(() =>
      DeadlineBudget.start({ remainingLambdaTimeMs: 60_000, targetMs: 46_000, clock }),
    ).toThrow(RangeError);
  });

  it("rejects a non-positive target", () => {
    const clock = new FakeClock(0);

    expect(() => DeadlineBudget.start({ remainingLambdaTimeMs: 60_000, targetMs: 0, clock })).toThrow(
      RangeError,
    );
  });

  it("uses the internal target when Lambda has more time remaining than the target", () => {
    const clock = new FakeClock(0);

    const budget = DeadlineBudget.start({ remainingLambdaTimeMs: 60_000, targetMs: 35_000, clock });

    expect(budget.remainingMs()).toBe(35_000);
  });

  it("uses the remaining Lambda time when it is less than the internal target", () => {
    // e.g. a request arriving late into an already-long-running invocation.
    const clock = new FakeClock(0);

    const budget = DeadlineBudget.start({ remainingLambdaTimeMs: 10_000, targetMs: 35_000, clock });

    expect(budget.remainingMs()).toBe(10_000);
  });

  it("never derives an effective budget above the hard ceiling even with a generous target", () => {
    const clock = new FakeClock(0);

    const budget = DeadlineBudget.start({
      remainingLambdaTimeMs: DEADLINE_HARD_CEILING_MS,
      targetMs: DEADLINE_HARD_CEILING_MS,
      clock,
    });

    expect(budget.remainingMs()).toBeLessThanOrEqual(DEADLINE_HARD_CEILING_MS);
  });
});

describe("DeadlineBudget.remainingMs", () => {
  it("decreases as the clock advances", () => {
    const clock = new FakeClock(0);
    const budget = DeadlineBudget.start({ remainingLambdaTimeMs: 35_000, targetMs: 35_000, clock });

    clock.advanceBy(10_000);

    expect(budget.remainingMs()).toBe(25_000);
  });

  it("never goes negative once the deadline has passed", () => {
    const clock = new FakeClock(0);
    const budget = DeadlineBudget.start({ remainingLambdaTimeMs: 5_000, targetMs: 5_000, clock });

    clock.advanceBy(50_000);

    expect(budget.remainingMs()).toBe(0);
  });
});

describe("DeadlineBudget.isExpired", () => {
  it("is false while time remains", () => {
    const clock = new FakeClock(0);
    const budget = DeadlineBudget.start({ remainingLambdaTimeMs: 5_000, targetMs: 5_000, clock });

    expect(budget.isExpired()).toBe(false);
  });

  it("is true once the deadline has passed", () => {
    const clock = new FakeClock(0);
    const budget = DeadlineBudget.start({ remainingLambdaTimeMs: 5_000, targetMs: 5_000, clock });

    clock.advanceBy(5_000);

    expect(budget.isExpired()).toBe(true);
  });
});

describe("DeadlineBudget.forCall", () => {
  it("subtracts the reserve from the remaining time", () => {
    const clock = new FakeClock(0);
    const budget = DeadlineBudget.start({ remainingLambdaTimeMs: 10_000, targetMs: 10_000, clock });

    expect(budget.forCall(2_000)).toBe(8_000);
  });

  it("caps the result at the given per-call maximum", () => {
    const clock = new FakeClock(0);
    const budget = DeadlineBudget.start({ remainingLambdaTimeMs: 10_000, targetMs: 10_000, clock });

    expect(budget.forCall(1_000, 3_000)).toBe(3_000);
  });

  it("never returns a negative budget when the reserve exceeds remaining time", () => {
    const clock = new FakeClock(0);
    const budget = DeadlineBudget.start({ remainingLambdaTimeMs: 1_000, targetMs: 1_000, clock });

    expect(budget.forCall(5_000)).toBe(0);
  });
});

describe("DeadlineBudget.canAttempt", () => {
  it("is true when remaining time exceeds the reserve", () => {
    const clock = new FakeClock(0);
    const budget = DeadlineBudget.start({ remainingLambdaTimeMs: 10_000, targetMs: 10_000, clock });

    expect(budget.canAttempt(2_000)).toBe(true);
  });

  it("is false once remaining time no longer exceeds the reserve", () => {
    const clock = new FakeClock(0);
    const budget = DeadlineBudget.start({ remainingLambdaTimeMs: 2_000, targetMs: 2_000, clock });

    clock.advanceBy(1_500);

    expect(budget.canAttempt(1_000)).toBe(false);
  });
});
