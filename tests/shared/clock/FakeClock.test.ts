import { FakeClock } from "../../../shared/clock/FakeClock";

describe("FakeClock", () => {
  it("starts at the given time and never moves on its own", () => {
    const clock = new FakeClock(1_000);

    expect(clock.now()).toBe(1_000);
    expect(clock.now()).toBe(1_000);
  });

  it("advances by the given duration", () => {
    const clock = new FakeClock(0);

    clock.advanceBy(500);

    expect(clock.now()).toBe(500);
  });

  it("rejects advancing by a negative duration", () => {
    const clock = new FakeClock(0);

    expect(() => clock.advanceBy(-1)).toThrow(RangeError);
  });

  it("can be set to an arbitrary point in time", () => {
    const clock = new FakeClock(0);

    clock.setTo(99_999);

    expect(clock.now()).toBe(99_999);
  });
});
