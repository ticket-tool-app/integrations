package app.tickettool.minecraft.command;

import app.tickettool.minecraft.TicketToolLinkPlugin;
import app.tickettool.sdk.LinkResult;
import app.tickettool.sdk.TicketToolClient;
import java.time.Duration;
import java.util.List;
import java.util.Map;
import java.util.UUID;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.TimeUnit;
import org.bukkit.command.Command;
import org.bukkit.command.CommandSender;
import org.bukkit.command.TabExecutor;
import org.bukkit.entity.Player;

/**
 * {@code /ttlink <code>}. The UUID and name sent to Ticket Tool always come
 * from the player who ran the command, so nobody can link someone else's
 * account by naming it; console and command blocks are refused.
 */
public final class LinkCommand implements TabExecutor {
    static final Duration MOJANG_RETRY_DELAY = Duration.ofSeconds(3);

    private final TicketToolLinkPlugin plugin;
    private final Map<UUID, Long> lastAttempt = new ConcurrentHashMap<>();

    public LinkCommand(TicketToolLinkPlugin plugin) {
        this.plugin = plugin;
    }

    @Override
    public boolean onCommand(CommandSender sender, Command command, String label, String[] args) {
        var messages = plugin.messages();
        if (!(sender instanceof Player player)) {
            messages.send(sender, "link.players-only");
            return true;
        }
        if (args.length == 0) {
            messages.send(player, "link.usage");
            return true;
        }
        // "ABCD EFGH" arrives as two arguments; the server ignores spaces and dashes.
        String code = String.join("", args);
        if (!plugin.plausibleLinkCode(code)) {
            messages.send(player, "link.bad-format");
            return true;
        }
        TicketToolClient client = plugin.client();
        if (client == null) {
            messages.send(player, "link.not-configured");
            return true;
        }
        long now = System.currentTimeMillis();
        long cooldownMs = TimeUnit.SECONDS.toMillis(plugin.linkCooldownSeconds());
        Long last = lastAttempt.get(player.getUniqueId());
        if (last != null && now - last < cooldownMs) {
            long wait = Math.max(1, (cooldownMs - (now - last) + 999) / 1000);
            messages.send(player, "link.cooldown", Map.of("seconds", Long.toString(wait)));
            return true;
        }
        lastAttempt.put(player.getUniqueId(), now);

        UUID uuid = player.getUniqueId();
        String name = player.getName();
        messages.send(player, "link.checking");
        attempt(client, uuid, name, code, true).thenAccept(result ->
                plugin.scheduler().runForPlayer(player, () -> reply(player, result)));
        return true;
    }

    /** Mojang outages are usually brief and the code survives them, so one quiet retry first. */
    private CompletableFuture<LinkResult> attempt(TicketToolClient client, UUID uuid, String name, String code,
            boolean mayRetry) {
        return client.link(uuid, name, code).thenCompose(result -> {
            if (mayRetry && result.outcome() == LinkResult.Outcome.UNAVAILABLE) {
                return CompletableFuture.supplyAsync(() -> null,
                                CompletableFuture.delayedExecutor(MOJANG_RETRY_DELAY.toMillis(), TimeUnit.MILLISECONDS))
                        .thenCompose(ignored -> attempt(client, uuid, name, code, false));
            }
            return CompletableFuture.completedFuture(result);
        });
    }

    private void reply(Player player, LinkResult result) {
        var messages = plugin.messages();
        String username = result.discordUsername();
        switch (result.outcome()) {
            case LINKED -> messages.send(player, username == null ? "link.linked-no-username" : "link.linked",
                    Map.of("username", username == null ? "" : username, "player", nz(result.playerName())));
            case RELINKED -> messages.send(player, username == null ? "link.relinked-no-username" : "link.relinked",
                    Map.of("username", username == null ? "" : username, "player", nz(result.playerName())));
            case INVALID_CODE -> messages.send(player, "link.invalid-code");
            case UNKNOWN_PLAYER -> messages.send(player, "link.unknown-player");
            case UNAVAILABLE -> messages.send(player, "link.mojang-unavailable");
            case RATE_LIMITED -> messages.send(player, "link.rate-limited");
            case FAILED -> {
                if ("not_running".equals(result.error())) {
                    messages.send(player, "link.not-configured");
                } else {
                    plugin.getLogger().warning("Linking " + player.getName() + " failed: " + result.error());
                    messages.send(player, "link.failed");
                }
            }
        }
    }

    private static String nz(String s) {
        return s == null ? "" : s;
    }

    @Override
    public List<String> onTabComplete(CommandSender sender, Command command, String alias, String[] args) {
        return List.of();
    }
}
