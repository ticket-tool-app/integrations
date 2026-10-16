package app.tickettool.minecraft.bans;

import app.tickettool.minecraft.platform.Scheduler;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.logging.Logger;
import org.bukkit.Bukkit;
import org.bukkit.plugin.Plugin;

/** Picks the ban source: the configured one, or the first installed of LiteBans, AdvancedBan, LibertyBans. */
public final class BanProviders {
    private BanProviders() {}

    public record Choice(BanProvider active, List<String> detected) {}

    public static Choice choose(String configured, Scheduler scheduler, Logger log) {
        List<BanProvider> installed = new ArrayList<>();
        tryAdd(installed, "LiteBans", log, p -> new LiteBansProvider(loader(p), scheduler.asyncExecutor()));
        tryAdd(installed, "AdvancedBan", log, p -> new AdvancedBanProvider(loader(p), scheduler.asyncExecutor()));
        tryAdd(installed, "LibertyBans", log, p -> new LibertyBansProvider(loader(p), scheduler.asyncExecutor()));
        BanProvider vanilla = new VanillaBanProvider(scheduler);
        List<String> detected = installed.stream().map(BanProvider::id).toList();

        String want = configured == null ? "auto" : configured.trim().toLowerCase(Locale.ROOT);
        if (want.equals("vanilla")) {
            return new Choice(vanilla, detected);
        }
        if (!want.equals("auto") && !want.isEmpty()) {
            for (BanProvider p : installed) {
                if (p.id().equals(want)) {
                    return new Choice(p, detected);
                }
            }
            log.warning("ban-provider is set to \"" + configured + "\" but that plugin isn't installed or enabled. "
                    + "Falling back to " + (installed.isEmpty() ? "the server ban list" : installed.get(0).displayName())
                    + ".");
        }
        return new Choice(installed.isEmpty() ? vanilla : installed.get(0), detected);
    }

    @FunctionalInterface
    private interface Factory {
        BanProvider create(Plugin plugin) throws ClassNotFoundException;
    }

    private static void tryAdd(List<BanProvider> out, String pluginName, Logger log, Factory factory) {
        Plugin plugin = Bukkit.getPluginManager().getPlugin(pluginName);
        if (plugin == null || !plugin.isEnabled()) {
            return;
        }
        try {
            out.add(factory.create(plugin));
        } catch (ClassNotFoundException | LinkageError e) {
            log.warning(pluginName + " is installed but its API wasn't found (" + e.getMessage() + "). Skipping it.");
        }
    }

    private static ClassLoader loader(Plugin plugin) {
        return plugin.getClass().getClassLoader();
    }
}
