package app.tickettool.sdk;

import java.time.Instant;
import java.util.Set;
import java.util.UUID;

/**
 * A {@code player.action} request from {@code /poll}: staff asked the plugin
 * to act on a player in game.
 *
 * @param kind      one of {@link #MESSAGE}, {@link #WARN}, {@link #KICK}, {@link #UNBAN}
 * @param message   plain text for the player, at most 500 characters; null for {@code unban}
 * @param actorName the staff member's Discord username, or {@code Ticket Tool} for an automation
 */
public record ActionRequest(String id, Instant expiresAt, UUID playerUuid, String playerName, String kind,
        String message, String actorName) {
    public static final String TYPE_PLAYER_ACTION = "player.action";
    public static final String MESSAGE = "message";
    public static final String WARN = "warn";
    public static final String KICK = "kick";
    public static final String UNBAN = "unban";
    public static final Set<String> KINDS = Set.of(MESSAGE, WARN, KICK, UNBAN);
    /** The order {@code /hello} lists them in. */
    static final java.util.List<String> ORDER = java.util.List.of(MESSAGE, WARN, KICK, UNBAN);
}
