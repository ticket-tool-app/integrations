package app.tickettool.minecraft.platform;

import java.time.Duration;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.Executor;
import java.util.function.Supplier;
import org.bukkit.entity.Player;

/**
 * The few scheduling shapes the plugin needs, so the same code runs on Paper,
 * Folia (regionized, no main thread) and Spigot (BukkitScheduler only).
 */
public interface Scheduler {
    /** Global state: the main thread on Paper and Spigot, the global region on Folia. */
    void runGlobal(Runnable task);

    /** Whatever the player's region owns, such as sending them a message. Skipped if they left. */
    default void runForPlayer(Player player, Runnable task) {
        runForPlayer(player, task, () -> {});
    }

    /** As above, running {@code retired} instead when the player left before the task could run. */
    void runForPlayer(Player player, Runnable task, Runnable retired);

    /** Off every game thread; for blocking work such as a ban plugin's database. */
    void runAsync(Runnable task);

    void runAsyncLater(Runnable task, Duration delay);

    void cancelAll();

    default <T> CompletableFuture<T> callGlobal(Supplier<T> task) {
        CompletableFuture<T> future = new CompletableFuture<>();
        runGlobal(() -> complete(future, task));
        return future;
    }

    /** Reads the player on their own thread, or returns {@code ifGone} if they logged off meanwhile. */
    default <T> CompletableFuture<T> callForPlayer(Player player, Supplier<T> task, Supplier<T> ifGone) {
        CompletableFuture<T> future = new CompletableFuture<>();
        runForPlayer(player, () -> complete(future, task), () -> complete(future, ifGone));
        return future;
    }

    private static <T> void complete(CompletableFuture<T> future, Supplier<T> task) {
        try {
            future.complete(task.get());
        } catch (Throwable t) {
            future.completeExceptionally(t);
        }
    }

    default Executor asyncExecutor() {
        return this::runAsync;
    }
}
