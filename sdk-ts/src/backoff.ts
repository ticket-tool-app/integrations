/**
 * Exponential backoff with "equal jitter": each wait is a random point in the
 * upper half of the step, so a fleet of plugins that lost the server at the
 * same moment doesn't come back in lockstep, yet no wait collapses to zero.
 */
export class Backoff {
  #attempt = 0;

  constructor(
    private readonly baseMs: number,
    private readonly maxMs: number,
    private readonly random: () => number = Math.random,
  ) {}

  /** The schedule from the protocol: 1, 2, 4 … 60 seconds. */
  static standard(random: () => number = Math.random): Backoff {
    return new Backoff(1000, 60_000, random);
  }

  next(): number {
    const step = this.#attempt < 30 ? Math.min(this.maxMs, this.baseMs * 2 ** this.#attempt) : this.maxMs;
    this.#attempt++;
    const half = Math.floor(step / 2);
    return half + (half > 0 ? Math.floor(this.random() * (half + 1)) : 0);
  }

  reset(): void {
    this.#attempt = 0;
  }

  get attempts(): number {
    return this.#attempt;
  }
}
