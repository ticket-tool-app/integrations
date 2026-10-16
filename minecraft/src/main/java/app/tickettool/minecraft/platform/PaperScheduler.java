package app.tickettool.minecraft.platform;

import java.time.Duration;
import java.util.concurrent.TimeUnit;
import org.bukkit.Bukkit;
import org.bukkit.entity.Player;
import org.bukkit.plugin.Plugin;

/**
 * Paper's region-aware schedulers. They exist on Paper 1.19.4+ as well as
 * Folia, where Paper maps them onto the main thread, so one implementation
 * serves both. Only loaded after {@link Platform} has checked they exist.
 */
final class PaperScheduler implements Scheduler {
    private final Plugin plugin;

    PaperScheduler(Plugin plugin) {
        this.plugin = plugin;
    }

    @Override
    public void runGlobal(Runnable task) {
        Bukkit.getGlobalRegionScheduler().execute(plugin, task);
    }

    @Override
    public void runForPlayer(Player player, Runnable task, Runnable retired) {
        if (player.getScheduler().run(plugin, t -> task.run(), retired) == null) {
            retired.run();
        }
    }

    @Override
    public void runAsync(Runnable task) {
        Bukkit.getAsyncScheduler().runNow(plugin, t -> task.run());
    }

    @Override
    public void runAsyncLater(Runnable task, Duration delay) {
        Bukkit.getAsyncScheduler().runDelayed(plugin, t -> task.run(), Math.max(1, delay.toMillis()),
                TimeUnit.MILLISECONDS);
    }

    @Override
    public void cancelAll() {
        Bukkit.getGlobalRegionScheduler().cancelTasks(plugin);
        Bukkit.getAsyncScheduler().cancelTasks(plugin);
    }
}
