export interface IClock {
  /** Current time in epoch milliseconds. */
  now(): number;
}
