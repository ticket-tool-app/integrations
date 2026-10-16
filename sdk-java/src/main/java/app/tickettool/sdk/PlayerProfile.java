package app.tickettool.sdk;

import java.time.Instant;
import java.util.Objects;
import java.util.UUID;

/**
 * The {@code profile} part of an answer.
 *
 * @param lastSeen  last time on this server; null when unknown
 * @param firstSeen first join; null when unknown
 * @param server    backend or world name; null to leave it out
 */
public record PlayerProfile(UUID uuid, String name, boolean online, Instant lastSeen, Instant firstSeen,
        String server) {
    public PlayerProfile {
        Objects.requireNonNull(uuid, "uuid");
        Objects.requireNonNull(name, "name");
        name = Text.clip(name.trim(), 32);
        server = server == null ? null : Text.clip(Text.emptyToNull(server), 64);
    }
}
