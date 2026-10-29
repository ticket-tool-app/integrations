package app.tickettool.minecraft.command;

import app.tickettool.minecraft.TicketToolLinkPlugin;
import app.tickettool.sdk.ReportResult;
import app.tickettool.sdk.TicketToolClient;
import java.util.Arrays;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.UUID;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.TimeUnit;
import org.bukkit.Bukkit;
import org.bukkit.command.Command;
import org.bukkit.command.CommandSender;
import org.bukkit.command.TabExecutor;
import org.bukkit.entity.Player;

/**
 * {@code /report [player] <message>}. The reporter is always the player who
 * ran it. A first word that names an online player becomes the target.
 */
public final class ReportCommand implements TabExecutor {
    private final TicketToolLinkPlugin plugin;
    private final Map<UUID, Long> lastReport = new ConcurrentHashMap<>();

    public ReportCommand(TicketToolLinkPlugin plugin) {
        this.plugin = plugin;
    }

    @Override
    public boolean onCommand(CommandSender sender, Command command, String label, String[] args) {
        var messages = plugin.messages();
        if (!(sender instanceof Player player)) {
            messages.send(sender, "report.players-only");
            return true;
        }
        TicketToolClient client = plugin.client();
        if (!plugin.reportsEnabled() || client == null) {
            messages.send(player, "report.not-enabled");
            return true;
        }
        Player target = args.length > 1 ? Bukkit.getPlayerExact(args[0]) : null;
        String text = String.join(" ", target == null ? args : Arrays.copyOfRange(args, 1, args.length)).trim();
        if (text.isEmpty()) {
            messages.send(player, "report.usage", Map.of("label", label));
            return true;
        }
        long now = System.currentTimeMillis();
        long cooldownMs = TimeUnit.SECONDS.toMillis(plugin.reportCooldownSeconds());
        Long last = lastReport.get(player.getUniqueId());
        if (last != null && now - last < cooldownMs) {
            long wait = Math.max(1, (cooldownMs - (now - last) + 999) / 1000);
            messages.send(player, "report.cooldown", Map.of("seconds", Long.toString(wait)));
            return true;
        }
        lastReport.put(player.getUniqueId(), now);

        messages.send(player, "report.sending");
        client.report(player.getUniqueId(), player.getName(), text, target == null ? null : target.getName(),
                        plugin.serverName())
                .thenAccept(result -> plugin.scheduler().runForPlayer(player, () -> reply(player, result)));
        return true;
    }

    private void reply(Player player, ReportResult result) {
        var messages = plugin.messages();
        switch (result.outcome()) {
            case OPENED -> messages.send(player, "report.sent");
            case IGNORED -> messages.send(player, "report.not-taken");
            case RATE_LIMITED -> messages.send(player, "report.rate-limited");
            case NOT_ENABLED -> messages.send(player, "report.not-enabled");
            case FAILED -> {
                if ("not_running".equals(result.error())) {
                    messages.send(player, "report.not-enabled");
                    return;
                }
                if ("plan_not_entitled".equals(result.error())) {
                    plugin.getLogger().warning("Reports need the Community or Pro plan on the Discord server.");
                } else {
                    plugin.getLogger().warning("Sending " + player.getName() + "'s report failed: " + result.error());
                }
                lastReport.remove(player.getUniqueId());
                messages.send(player, "report.failed");
            }
        }
    }

    @Override
    public List<String> onTabComplete(CommandSender sender, Command command, String alias, String[] args) {
        if (args.length != 1) {
            return List.of();
        }
        String prefix = args[0].toLowerCase(Locale.ROOT);
        return Bukkit.getOnlinePlayers().stream().map(Player::getName)
                .filter(n -> n.toLowerCase(Locale.ROOT).startsWith(prefix)).limit(20).toList();
    }
}
