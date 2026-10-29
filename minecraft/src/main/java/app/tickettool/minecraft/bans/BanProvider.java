package app.tickettool.minecraft.bans;

import app.tickettool.sdk.BanInfo;
import java.time.Instant;
import java.util.UUID;
import java.util.concurrent.CompletableFuture;

/** One source of ban records. Implementations must not block the calling thread. */
public interface BanProvider {
    /** Protocol id: litebans, advancedban, libertybans or vanilla. */
    String id();

    /** Display name for status output. */
    String displayName();

    CompletableFuture<BanInfo> lookup(UUID uuid, String name);

    /** The console command that lifts the ban. Namespaced, so another plugin's /unban can't take it. */
    String unbanCommand(String name);

    /** Ban plugins store times as epoch millis with 0 or -1 for "none"/"permanent". */
    static Instant millis(long epochMillis) {
        return epochMillis <= 0 ? null : Instant.ofEpochMilli(epochMillis);
    }
}
