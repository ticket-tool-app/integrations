package app.tickettool.minecraft.command;

import app.tickettool.minecraft.TicketToolLinkPlugin;
import app.tickettool.sdk.ClientStatus;
import app.tickettool.sdk.HelloReply;
import app.tickettool.sdk.TicketToolClient;
import java.time.Duration;
import java.time.Instant;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import org.bukkit.command.Command;
import org.bukkit.command.CommandSender;
import org.bukkit.command.TabExecutor;
import org.bukkit.entity.Player;

/** {@code /ticketlink status|reload}, also reachable as {@code /tickettool}. */
public final class AdminCommand implements TabExecutor {
    private final TicketToolLinkPlugin plugin;

    public AdminCommand(TicketToolLinkPlugin plugin) {
        this.plugin = plugin;
    }

    @Override
    public boolean onCommand(CommandSender sender, Command command, String label, String[] args) {
        var messages = plugin.messages();
        String sub = args.length == 0 ? "status" : args[0].toLowerCase(Locale.ROOT);
        switch (sub) {
            case "status" -> status(sender);
            case "reload" -> {
                messages.send(sender, "admin.reloading");
                plugin.reload().thenRun(() -> {
                    Runnable report = () -> {
                        plugin.messages().send(sender, "admin.reloaded");
                        status(sender);
                    };
                    if (sender instanceof Player player) {
                        plugin.scheduler().runForPlayer(player, report);
                    } else {
                        plugin.scheduler().runGlobal(report);
                    }
                });
            }
            default -> messages.send(sender, "admin.usage", Map.of("label", label));
        }
        return true;
    }

    private void status(CommandSender sender) {
        var m = plugin.messages();
        m.send(sender, "admin.status.header", Map.of("version", plugin.version()));
        TicketToolClient client = plugin.client();
        if (client == null) {
            m.send(sender, plugin.keyProblem() == null ? "admin.status.no-key" : "admin.status.bad-key",
                    Map.of("problem", plugin.keyProblem() == null ? "" : plugin.keyProblem()));
            m.send(sender, "admin.status.ban-provider", Map.of("provider", plugin.banProviderName()));
            return;
        }
        ClientStatus s = client.status();
        HelloReply hello = s.hello();
        m.send(sender, "admin.status.state." + s.state().name().toLowerCase(Locale.ROOT));
        m.send(sender, "admin.status.key", Map.of("key", client.key().displayPrefix(),
                "name", hello == null || hello.keyName() == null ? "?" : hello.keyName()));
        if (hello != null) {
            m.send(sender, "admin.status.discord", Map.of("server",
                    hello.serverName() == null ? hello.serverId() : hello.serverName()));
        }
        m.send(sender, "admin.status.ban-provider", Map.of("provider", plugin.banProviderName()));
        m.send(sender, "admin.status.answered", Map.of("count", Long.toString(s.answered())));
        if (Math.abs(s.clockOffsetSeconds()) > 5) {
            m.send(sender, "admin.status.clock", Map.of("seconds", Long.toString(s.clockOffsetSeconds())));
        }
        if (s.lastError() != null) {
            m.send(sender, "admin.status.last-error", Map.of("error", s.lastError(), "ago", ago(s.lastErrorAt())));
        }
    }

    private static String ago(Instant at) {
        if (at == null) {
            return "";
        }
        long seconds = Math.max(0, Duration.between(at, Instant.now()).toSeconds());
        if (seconds < 60) {
            return seconds + "s";
        }
        if (seconds < 3600) {
            return (seconds / 60) + "m";
        }
        return (seconds / 3600) + "h";
    }

    @Override
    public List<String> onTabComplete(CommandSender sender, Command command, String alias, String[] args) {
        if (args.length == 1) {
            return List.of("status", "reload").stream().filter(s -> s.startsWith(args[0].toLowerCase(Locale.ROOT)))
                    .toList();
        }
        return List.of();
    }
}
