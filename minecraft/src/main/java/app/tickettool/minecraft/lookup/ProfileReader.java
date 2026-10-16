package app.tickettool.minecraft.lookup;

import app.tickettool.minecraft.platform.Platform;
import app.tickettool.minecraft.platform.Scheduler;
import app.tickettool.sdk.PlayerProfile;
import java.time.Instant;
import java.util.Optional;
import java.util.UUID;
import java.util.concurrent.CompletableFuture;
import java.util.function.Supplier;
import org.bukkit.Bukkit;
import org.bukkit.OfflinePlayer;
import org.bukkit.entity.Player;

/** What this server knows about a player, read on the thread that owns that data. */
public final class ProfileReader {
    private final Scheduler scheduler;
    private final Supplier<String> serverName;

    /** @param serverName backend name for the profile, or null to leave it out */
    public ProfileReader(Scheduler scheduler, Supplier<String> serverName) {
        this.scheduler = scheduler;
        this.serverName = serverName;
    }

    /** Empty when the player has never joined this server. */
    public CompletableFuture<Optional<PlayerProfile>> read(UUID uuid, String fallbackName) {
        return scheduler.callGlobal(() -> Bukkit.getPlayer(uuid)).thenCompose(online -> {
            if (online == null) {
                return scheduler.callGlobal(() -> offline(uuid, fallbackName));
            }
            // An online player's data belongs to their region on Folia, so read it there.
            // Null means they logged off in between: fall back to the offline record.
            return scheduler.callForPlayer(online, () -> Optional.of(onlineProfile(uuid, online)), () -> null)
                    .thenCompose(p -> p != null ? CompletableFuture.completedFuture(p)
                            : scheduler.callGlobal(() -> offline(uuid, fallbackName)));
        });
    }

    private PlayerProfile onlineProfile(UUID uuid, Player player) {
        return new PlayerProfile(uuid, player.getName(), true, Instant.now(), millis(player.getFirstPlayed()),
                serverName.get());
    }

    private Optional<PlayerProfile> offline(UUID uuid, String fallbackName) {
        // getOfflinePlayer(UUID) reads only local player data; it never calls Mojang.
        OfflinePlayer offline = Bukkit.getOfflinePlayer(uuid);
        if (!offline.hasPlayedBefore()) {
            return Optional.empty();
        }
        String name = offline.getName() != null ? offline.getName() : fallbackName;
        if (name == null || name.isBlank()) {
            return Optional.empty();
        }
        return Optional.of(new PlayerProfile(uuid, name, false, millis(Platform.lastSeen(offline)),
                millis(offline.getFirstPlayed()), serverName.get()));
    }

    private static Instant millis(long epochMillis) {
        return epochMillis <= 0 ? null : Instant.ofEpochMilli(epochMillis);
    }
}
