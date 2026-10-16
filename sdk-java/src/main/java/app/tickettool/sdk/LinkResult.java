package app.tickettool.sdk;

/**
 * The outcome of {@code /link}.
 *
 * @param discordUsername on success, the Discord username the account was linked to; may be null
 * @param playerName      on success, the name Mojang holds for the UUID
 * @param error           the server's error code on failure, or a local one
 *                        ({@code network}, {@code not_running})
 */
public record LinkResult(Outcome outcome, String discordUsername, String playerName, String error) {
    public enum Outcome {
        LINKED,
        RELINKED,
        INVALID_CODE,
        UNKNOWN_PLAYER,
        /** Mojang didn't answer; the code is still good. */
        UNAVAILABLE,
        RATE_LIMITED,
        FAILED
    }

    public boolean success() {
        return outcome == Outcome.LINKED || outcome == Outcome.RELINKED;
    }

    static LinkResult failed(Outcome outcome, String error) {
        return new LinkResult(outcome, null, null, error);
    }
}
