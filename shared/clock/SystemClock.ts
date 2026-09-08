import type { IClock } from "./IClock";

export class SystemClock implements IClock {
  now(): number {
    return Date.now();
  }
}
