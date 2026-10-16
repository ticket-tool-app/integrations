package app.tickettool.sdk;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.time.Duration;
import java.util.SplittableRandom;
import org.junit.jupiter.api.RepeatedTest;
import org.junit.jupiter.api.Test;

class BackoffTest {
    @RepeatedTest(20)
    void doublesFromOneSecondToSixtyWithJitter() {
        Backoff backoff = Backoff.standard(new SplittableRandom());
        long[] steps = {1000, 2000, 4000, 8000, 16000, 32000, 60000, 60000, 60000};
        for (long step : steps) {
            long wait = backoff.next().toMillis();
            assertTrue(wait >= step / 2 && wait <= step, "wait " + wait + " outside [" + step / 2 + ", " + step + "]");
        }
    }

    @Test
    void resetStartsOver() {
        Backoff backoff = Backoff.standard(new SplittableRandom(1));
        for (int i = 0; i < 10; i++) {
            backoff.next();
        }
        backoff.reset();
        assertEquals(0, backoff.attempts());
        assertTrue(backoff.next().compareTo(Duration.ofSeconds(1)) <= 0);
    }

    @Test
    void neverOverflows() {
        Backoff backoff = Backoff.standard(new SplittableRandom(2));
        for (int i = 0; i < 200; i++) {
            assertTrue(backoff.next().toMillis() <= 60_000);
        }
    }
}
