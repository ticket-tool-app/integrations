package app.tickettool.sdk;

import java.time.Instant;
import java.util.Set;
import java.util.UUID;

/**
 * One question from {@code /poll}. Parts the SDK doesn't know are dropped from
 * {@link #include()} before the handler sees them.
 *
 * @param playerName the name Ticket Tool last saw; may be null
 */
public record LookupRequest(String id, String type, Instant expiresAt, UUID playerUuid, String playerName,
        Set<String> include) {
    public static final String TYPE_PLAYER_LOOKUP = "player.lookup";
    public static final String PART_PROFILE = "profile";
    public static final String PART_BANS = "bans";

    public boolean wantsProfile() {
        return include.contains(PART_PROFILE);
    }

    public boolean wantsBans() {
        return include.contains(PART_BANS);
    }
}
