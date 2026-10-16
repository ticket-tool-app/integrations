package app.tickettool.minecraft.platform;

import org.bukkit.Bukkit;
import org.bukkit.OfflinePlayer;
import org.bukkit.plugin.Plugin;

/** What server software we're on, found by probing for methods rather than parsing version strings. */
public final class Platform {
    private static final boolean REGION_SCHEDULERS = methodExists(org.bukkit.Server.class, "getGlobalRegionScheduler");
    private static final boolean LAST_SEEN = methodExists(OfflinePlayer.class, "getLastSeen");

    private Platform() {}

    public static Scheduler scheduler(Plugin plugin) {
        return REGION_SCHEDULERS ? new PaperScheduler(plugin) : new BukkitScheduler(plugin);
    }

    /** e.g. "Paper 1.21.1", "Folia 1.21.1" or "CraftBukkit 1.20.6". */
    public static String describe() {
        String version;
        try {
            version = Bukkit.getMinecraftVersion();
        } catch (NoSuchMethodError e) {
            version = Bukkit.getBukkitVersion().split("-", 2)[0];
        }
        return Bukkit.getName() + " " + version;
    }

    /** Epoch millis of the player's last visit, 0 when unknown. Paper tracks logouts too, not only logins. */
    @SuppressWarnings("deprecation")
    public static long lastSeen(OfflinePlayer player) {
        return LAST_SEEN ? player.getLastSeen() : player.getLastPlayed();
    }

    private static boolean methodExists(Class<?> type, String name) {
        for (var m : type.getMethods()) {
            if (m.getName().equals(name)) {
                return true;
            }
        }
        return false;
    }
}
