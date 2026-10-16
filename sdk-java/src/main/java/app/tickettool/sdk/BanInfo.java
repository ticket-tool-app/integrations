package app.tickettool.sdk;

import java.time.Instant;
import java.util.Objects;

/**
 * The {@code bans} part of an answer. The compact constructor strips color
 * codes and applies the server's length limits, so a provider adapter can pass
 * raw values straight through.
 *
 * @param provider    {@code litebans}, {@code advancedban}, {@code libertybans} or {@code vanilla}
 * @param expiresAt   null means permanent
 * @param activeCount active bans on record; only sent when more than one
 */
public record BanInfo(boolean banned, String provider, String reason, Instant createdAt, Instant expiresAt,
        String actor, Integer activeCount) {
    public static final int MAX_REASON = 2000;

    public BanInfo {
        Objects.requireNonNull(provider, "provider");
        provider = Text.clip(provider.trim().toLowerCase(java.util.Locale.ROOT), 32);
        reason = Text.clip(Text.emptyToNull(Text.stripColorCodes(reason)), MAX_REASON);
        actor = Text.clip(Text.emptyToNull(Text.stripColorCodes(actor)), 64);
        if (activeCount != null && (activeCount < 0 || activeCount > 100_000)) {
            activeCount = null;
        }
    }

    public static BanInfo notBanned(String provider) {
        return new BanInfo(false, provider, null, null, null, null, null);
    }
}
