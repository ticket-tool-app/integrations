package app.tickettool.minecraft.action;

import app.tickettool.minecraft.ChatMessages;
import app.tickettool.minecraft.bans.BanProvider;
import app.tickettool.minecraft.platform.Scheduler;
import app.tickettool.sdk.ActionHandler;
import app.tickettool.sdk.ActionRequest;
import app.tickettool.sdk.ActionResult;
import java.util.Map;
import java.util.UUID;
import java.util.concurrent.CompletableFuture;
import java.util.function.Supplier;
import java.util.logging.Logger;
import java.util.regex.Pattern;
import org.bukkit.Bukkit;
import org.bukkit.OfflinePlayer;
import org.bukkit.entity.Player;

/**
 * Carries out {@code player.action} requests. The SDK has already refused any
 * kind the owner left off in config.yml, so every kind here is one they chose.
 */
public final class PlayerActions implements ActionHandler {
    // A name goes into a console command line, so nothing but what Mojang allows.
    private static final Pattern SAFE_NAME = Pattern.compile("^[A-Za-z0-9_]{1,16}$");

    private final Scheduler scheduler;
    private final Supplier<ChatMessages> messages;
    private final Supplier<BanProvider> bans;
    private final Logger log;

    public PlayerActions(Scheduler scheduler, Supplier<ChatMessages> messages, Supplier<BanProvider> bans,
            Logger log) {
        this.scheduler = scheduler;
        this.messages = messages;
        this.bans = bans;
        this.log = log;
    }

    @Override
    public CompletableFuture<ActionResult> handle(ActionRequest request) {
        return switch (request.kind()) {
            case ActionRequest.UNBAN -> unban(request);
            default -> onOnlinePlayer(request);
        };
    }

    private CompletableFuture<ActionResult> onOnlinePlayer(ActionRequest request) {
        UUID uuid = request.playerUuid();
        return scheduler.callGlobal(() -> Bukkit.getPlayer(uuid)).thenCompose(player -> {
            if (player == null) {
                return CompletableFuture.completedFuture(ActionResult.failure(ActionResult.PLAYER_OFFLINE));
            }
            return scheduler.callForPlayer(player, () -> apply(player, request),
                    () -> ActionResult.failure(ActionResult.PLAYER_OFFLINE));
        });
    }

    @SuppressWarnings("deprecation") // kickPlayer(String): Paper's Component overload doesn't exist on Spigot
    private ActionResult apply(Player player, ActionRequest request) {
        ChatMessages m = messages.get();
        Map<String, String> values = Map.of("message", request.message() == null ? "" : request.message(),
                "actor", request.actorName());
        switch (request.kind()) {
            case ActionRequest.MESSAGE -> player.sendMessage(m.formatPlain("action.message", values));
            case ActionRequest.WARN -> player.sendMessage(m.formatPlain("action.warn", values));
            case ActionRequest.KICK -> player.kickPlayer(m.formatPlain(
                    request.message() == null ? "action.kick-no-reason" : "action.kick", values));
            default -> {
                return ActionResult.failure(ActionResult.UNSUPPORTED);
            }
        }
        log.info(request.actorName() + " (Ticket Tool) sent a " + request.kind() + " to " + player.getName());
        return ActionResult.done();
    }

    private CompletableFuture<ActionResult> unban(ActionRequest request) {
        BanProvider provider = bans.get();
        UUID uuid = request.playerUuid();
        return provider.lookup(uuid, request.playerName()).thenCompose(info -> {
            if (info == null || !info.banned()) {
                return CompletableFuture.completedFuture(ActionResult.failure(ActionResult.NOT_BANNED));
            }
            return scheduler.callGlobal(() -> {
                OfflinePlayer offline = Bukkit.getOfflinePlayer(uuid);
                String name = offline.getName() != null ? offline.getName() : request.playerName();
                if (name == null || !SAFE_NAME.matcher(name).matches()) {
                    return ActionResult.failure(ActionResult.UNKNOWN_PLAYER);
                }
                if (!Bukkit.dispatchCommand(Bukkit.getConsoleSender(), provider.unbanCommand(name))) {
                    return ActionResult.failure(ActionResult.ERROR,
                            provider.displayName() + " didn't accept the unban command");
                }
                log.info(request.actorName() + " (Ticket Tool) unbanned " + name + " through "
                        + provider.displayName());
                return ActionResult.done();
            });
        });
    }
}
