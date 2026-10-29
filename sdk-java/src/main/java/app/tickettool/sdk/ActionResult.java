package app.tickettool.sdk;

/** What an {@link ActionHandler} returns for one {@link ActionRequest}. */
public sealed interface ActionResult {
    String PLAYER_OFFLINE = "player_offline";
    String NOT_BANNED = "not_banned";
    String NOT_ALLOWED = "not_allowed";
    String UNKNOWN_PLAYER = "unknown_player";
    String UNSUPPORTED = "unsupported";
    String ERROR = "error";

    record Done() implements ActionResult {}

    /**
     * @param error   one of the constants on {@link ActionResult}
     * @param message short note for the server's admins, at most 200 characters; may be null
     */
    record Failure(String error, String message) implements ActionResult {
        public Failure {
            message = message == null ? null : Text.clip(message, 200);
        }
    }

    static Done done() {
        return new Done();
    }

    static Failure failure(String error) {
        return new Failure(error, null);
    }

    static Failure failure(String error, String message) {
        return new Failure(error, message);
    }
}
