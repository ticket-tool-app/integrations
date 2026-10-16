package app.tickettool.minecraft;

import app.tickettool.minecraft.bans.BanProvider;
import app.tickettool.minecraft.bans.BanProviders;
import app.tickettool.minecraft.command.AdminCommand;
import app.tickettool.minecraft.command.LinkCommand;
import app.tickettool.minecraft.lookup.LookupHandler;
import app.tickettool.minecraft.lookup.ProfileReader;
import app.tickettool.minecraft.platform.Platform;
import app.tickettool.minecraft.platform.Scheduler;
import app.tickettool.sdk.PluginInfo;
import app.tickettool.sdk.PluginKey;
import app.tickettool.sdk.SdkLogger;
import app.tickettool.sdk.TicketToolClient;
import java.util.ArrayList;
import java.util.List;
import java.util.Objects;
import java.util.concurrent.CompletableFuture;
import java.util.logging.Level;
import org.bukkit.Bukkit;
import org.bukkit.command.PluginCommand;
import org.bukkit.command.TabExecutor;
import org.bukkit.plugin.java.JavaPlugin;

public final class TicketToolLinkPlugin extends JavaPlugin {
    static final String PLUGIN_TYPE = "minecraft";
    static final String PRODUCT = "ticket-tool-minecraft";

    private Scheduler scheduler;
    private String platform;
    private volatile Settings settings;
    private volatile ChatMessages messages;
    private volatile TicketToolClient client;
    private volatile BanProviders.Choice bans;
    private volatile String keyProblem;
    /** Serializes reloads, which stop one client (up to ~3 s for /goodbye) before starting the next. */
    private final Object lifecycle = new Object();

    @Override
    public void onEnable() {
        scheduler = Platform.scheduler(this);
        saveDefaultConfig();
        loadFiles();
        register("ttlink", new LinkCommand(this));
        register("ticketlink", new AdminCommand(this));

        if (!Bukkit.getOnlineMode()) {
            getLogger().warning("online-mode is off. Only real Java Edition accounts can link, so /ttlink works "
                    + "for players with Mojang UUIDs: fine behind a Velocity or BungeeCord proxy that forwards "
                    + "them, not on an offline-mode server.");
        }
        platform = Platform.describe();
        // First tick: every plugin is enabled by then, so ban plugins are visible whatever the load order.
        scheduler.runGlobal(() -> {
            BanProviders.Choice choice = BanProviders.choose(settings.banProvider(), scheduler, getLogger());
            scheduler.runAsync(() -> startClient(choice));
        });
    }

    @Override
    public void onDisable() {
        synchronized (lifecycle) {
            TicketToolClient old = client;
            client = null;
            if (old != null) {
                old.stop();
            }
        }
        if (scheduler != null) {
            scheduler.cancelAll();
        }
    }

    /**
     * Re-reads both files and probes ban plugins on the global thread, then
     * restarts the connection off it, since stopping sends /goodbye.
     */
    public CompletableFuture<Void> reload() {
        CompletableFuture<Void> done = new CompletableFuture<>();
        scheduler.runGlobal(() -> {
            BanProviders.Choice choice;
            try {
                reloadConfig();
                loadFiles();
                choice = BanProviders.choose(settings.banProvider(), scheduler, getLogger());
            } catch (RuntimeException e) {
                getLogger().log(Level.SEVERE, "Reload failed", e);
                done.completeExceptionally(e);
                return;
            }
            scheduler.runAsync(() -> {
                try {
                    startClient(choice);
                    done.complete(null);
                } catch (RuntimeException e) {
                    getLogger().log(Level.SEVERE, "Reload failed", e);
                    done.completeExceptionally(e);
                }
            });
        });
        return done;
    }

    private void loadFiles() {
        settings = Settings.from(getConfig());
        messages = ChatMessages.load(this);
    }

    /** Stops the running client (sending /goodbye) and starts one for the current settings. Blocking. */
    private void startClient(BanProviders.Choice choice) {
        synchronized (lifecycle) {
            Settings s = settings;
            bans = choice;
            TicketToolClient old = client;
            client = null;
            if (old != null) {
                old.stop();
            }
            keyProblem = null;
            if (!s.hasKey()) {
                getLogger().warning("No key set. Create a Minecraft key on the Ticket Tool dashboard "
                        + "(Automations > Connections > Game server plugins), paste it into "
                        + "plugins/" + getName() + "/config.yml and run /ticketlink reload.");
                return;
            }
            if (!PluginKey.looksLikeKey(s.key())) {
                keyProblem = "The key in config.yml isn't a valid Ticket Tool key. It should start with ttp_.";
                getLogger().warning(keyProblem);
                return;
            }
            TicketToolClient next;
            try {
                next = TicketToolClient.builder()
                        .key(s.key())
                        .apiUrl(s.apiUrl())
                        .plugin(new PluginInfo(PLUGIN_TYPE, PRODUCT, version(), platform))
                        .banProviders(this::reportedBanProviders)
                        .handler(new LookupHandler(new ProfileReader(scheduler, this::serverNameForProfile),
                                () -> bans.active(), getLogger()))
                        .logger(sdkLogger())
                        .build();
            } catch (IllegalArgumentException e) {
                keyProblem = e.getMessage();
                getLogger().warning(keyProblem);
                return;
            }
            getLogger().info("Connecting to Ticket Tool with key " + next.key() + ". Bans come from "
                    + bans.active().displayName() + ".");
            client = next;
            next.start();
        }
    }

    private List<String> reportedBanProviders() {
        BanProviders.Choice c = bans;
        List<String> out = new ArrayList<>(c.detected());
        if (out.isEmpty()) {
            out.add("vanilla");
        }
        return out;
    }

    /** The configured backend name, else the key's name from the dashboard. */
    private String serverNameForProfile() {
        Settings s = settings;
        if (s.serverName() != null) {
            return s.serverName();
        }
        TicketToolClient c = client;
        if (c != null && c.status().hello() != null) {
            return c.status().hello().keyName();
        }
        return null;
    }

    private SdkLogger sdkLogger() {
        return new SdkLogger() {
            @Override
            public void info(String message) {
                getLogger().info(message);
            }

            @Override
            public void warn(String message, Throwable error) {
                if (error != null && settings.debug()) {
                    getLogger().log(Level.WARNING, message, error);
                } else {
                    getLogger().warning(message);
                }
            }

            @Override
            public void debug(String message) {
                if (settings.debug()) {
                    getLogger().info("[debug] " + message);
                }
            }
        };
    }

    private void register(String name, TabExecutor executor) {
        PluginCommand command = Objects.requireNonNull(getCommand(name), name + " missing from plugin.yml");
        command.setExecutor(executor);
        command.setTabCompleter(executor);
    }

    @SuppressWarnings("deprecation")
    public String version() {
        return getDescription().getVersion();
    }

    public Scheduler scheduler() {
        return scheduler;
    }

    public ChatMessages messages() {
        return messages;
    }

    /** Null when no valid key is configured. */
    public TicketToolClient client() {
        return client;
    }

    public String keyProblem() {
        return keyProblem;
    }

    public String banProviderName() {
        BanProviders.Choice c = bans;
        BanProvider p = c == null ? null : c.active();
        return p == null ? "?" : p.displayName();
    }

    public int linkCooldownSeconds() {
        return settings.linkCooldownSeconds();
    }

    public boolean plausibleLinkCode(String code) {
        return LinkCodes.plausible(code);
    }
}
