package app.tickettool.sdk;

import java.time.Duration;
import java.util.random.RandomGenerator;

/**
 * Exponential backoff with "equal jitter": each wait is a random point in the
 * upper half of the step, so a fleet of plugins that lost the server at the
 * same moment doesn't come back in lockstep, yet no wait collapses to zero.
 */
public final class Backoff {
    private final long baseMillis;
    private final long maxMillis;
    private final RandomGenerator random;
    private int attempt;

    public Backoff(Duration base, Duration max, RandomGenerator random) {
        this.baseMillis = base.toMillis();
        this.maxMillis = max.toMillis();
        this.random = random;
    }

    /** The default schedule from the protocol: 1, 2, 4 … 60 seconds. */
    public static Backoff standard(RandomGenerator random) {
        return new Backoff(Duration.ofSeconds(1), Duration.ofSeconds(60), random);
    }

    public Duration next() {
        long step = maxMillis;
        if (attempt < 30) {
            step = Math.min(maxMillis, baseMillis << attempt);
        }
        attempt++;
        long half = step / 2;
        return Duration.ofMillis(half + (half > 0 ? random.nextLong(half + 1) : 0));
    }

    public void reset() {
        attempt = 0;
    }

    public int attempts() {
        return attempt;
    }
}
