import { describe, expect, it } from "vitest";
import { Backoff } from "../src";

describe("Backoff", () => {
  it("doubles from 1 s to a 60 s cap with equal jitter", () => {
    const low = Backoff.standard(() => 0);
    expect([1, 2, 3, 4, 5, 6, 7, 8].map(() => low.next())).toEqual([500, 1000, 2000, 4000, 8000, 16000, 30000, 30000]);
    const high = Backoff.standard(() => 0.999999);
    expect([1, 2, 3, 4, 5, 6, 7].map(() => high.next())).toEqual([1000, 2000, 4000, 8000, 16000, 32000, 60000]);
  });

  it("starts over after reset and never overflows", () => {
    const b = Backoff.standard(() => 0.5);
    for (let i = 0; i < 100; i++) expect(b.next()).toBeLessThanOrEqual(60000);
    expect(b.attempts).toBe(100);
    b.reset();
    expect(b.next()).toBeLessThanOrEqual(1000);
  });
});
