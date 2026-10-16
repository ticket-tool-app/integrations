package app.tickettool.sdk;

import java.time.Duration;

/** Waiting between retries; swapped out in tests so backoff runs instantly. */
@FunctionalInterface
public interface Sleeper {
    Sleeper SYSTEM = d -> Thread.sleep(d.toMillis());

    void sleep(Duration duration) throws InterruptedException;
}
