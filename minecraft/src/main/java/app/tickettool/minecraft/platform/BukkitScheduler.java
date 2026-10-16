package app.tickettool.minecraft.platform;

import java.time.Duration;
import org.bukkit.Bukkit;
import org.bukkit.entity.Player;
import org.bukkit.plugin.Plugin;

/** Spigot, which has only the classic main-thread scheduler. */
final class BukkitScheduler implements Scheduler {
    private final Plugin plugin;

    BukkitScheduler(Plugin plugin) {
        this.plugin = plugin;
    }

    @Override
    public void runGlobal(Runnable task) {
        if (Bukkit.isPrimaryThread()) {
            task.run();
        } else {
            Bukkit.getScheduler().runTask(plugin, task);
        }
    }

    @Override
    public void runForPlayer(Player player, Runnable task, Runnable retired) {
        runGlobal(() -> {
            if (player.isOnline()) {
                task.run();
            } else {
                retired.run();
            }
        });
    }

    @Override
    public void runAsync(Runnable task) {
        Bukkit.getScheduler().runTaskAsynchronously(plugin, task);
    }

    @Override
    public void runAsyncLater(Runnable task, Duration delay) {
        Bukkit.getScheduler().runTaskLaterAsynchronously(plugin, task, Math.max(1, delay.toMillis() / 50));
    }

    @Override
    public void cancelAll() {
        Bukkit.getScheduler().cancelTasks(plugin);
    }
}
