package app.tickettool.sdk;

import java.time.Instant;

/**
 * A snapshot for status commands.
 *
 * @param lastError   the last problem, already phrased for an admin; null when there hasn't been one
 * @param hello       the last successful {@code /hello} reply; null before the first
 * @param answered    lookups answered since start
 */
public record ClientStatus(State state, String lastError, Instant lastErrorAt, HelloReply hello, Instant lastPollAt,
        long answered, long clockOffsetSeconds) {
    public enum State {
        STARTING,
        /** A poll is open or just returned. */
        CONNECTED,
        /** Network or server trouble; retrying with backoff. */
        RECONNECTING,
        /** The Discord server's plan doesn't include game lookups; retrying hourly. */
        PAUSED_PLAN,
        /** The key was revoked, rotated or never existed. Needs a new key. */
        STOPPED_INVALID_KEY,
        /** Another error that retrying won't fix, e.g. the key is for a different game. */
        STOPPED_ERROR,
        STOPPED;

        public boolean running() {
            return this == STARTING || this == CONNECTED || this == RECONNECTING || this == PAUSED_PLAN;
        }
    }
}
