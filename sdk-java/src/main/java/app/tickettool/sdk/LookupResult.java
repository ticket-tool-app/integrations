package app.tickettool.sdk;

/** What a {@link RequestHandler} returns for one {@link LookupRequest}. */
public sealed interface LookupResult {
    String UNKNOWN_PLAYER = "unknown_player";
    String NO_BAN_PROVIDER = "no_ban_provider";
    String UNSUPPORTED = "unsupported";
    String ERROR = "error";

    /** Fill each part that was asked for; null for a part that wasn't. */
    record Answer(PlayerProfile profile, BanInfo bans) implements LookupResult {}

    /**
     * @param error   one of the constants on {@link LookupResult}
     * @param message short note for the server's admins, at most 200 characters; may be null
     */
    record Failure(String error, String message) implements LookupResult {
        public Failure {
            message = message == null ? null : Text.clip(message, 200);
        }
    }

    static Answer answer(PlayerProfile profile, BanInfo bans) {
        return new Answer(profile, bans);
    }

    static Failure failure(String error) {
        return new Failure(error, null);
    }

    static Failure failure(String error, String message) {
        return new Failure(error, message);
    }
}
