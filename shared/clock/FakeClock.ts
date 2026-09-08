import type { IClock } from "./IClock";

/**
 * Controllable clock for tests. Lets deadline/timeout tests advance time in a single
 * call instead of actually sleeping, so a 45-second scenario runs in milliseconds.
 */
export class FakeClock implements IClock {
  private currentMs: number;

  constructor(startAtMs = 0) {
    this.currentMs = startAtMs;
  }

  now(): number {
    return this.currentMs;
  }

  advanceBy(ms: number): void {
    if (ms < 0) {
      throw new RangeError("FakeClock cannot advance by a negative duration");
    }
    this.currentMs += ms;
  }

  setTo(ms: number): void {
    this.currentMs = ms;
  }
}
