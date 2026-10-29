package app.tickettool.sdk;

/**
 * The outcome of an in-game report sent with {@code /event}.
 *
 * @param error the server's error code on failure, or a local one
 *              ({@code network}, {@code not_running}, {@code not_enabled})
 */
public record ReportResult(Outcome outcome, String error) {
    public enum Outcome {
        OPENED,
        /** No automation rule on the Discord server took the report. */
        IGNORED,
        RATE_LIMITED,
        /** Reports are off in the plugin's config. */
        NOT_ENABLED,
        FAILED
    }

    public boolean success() {
        return outcome == Outcome.OPENED;
    }

    static ReportResult failed(Outcome outcome, String error) {
        return new ReportResult(outcome, error);
    }
}
