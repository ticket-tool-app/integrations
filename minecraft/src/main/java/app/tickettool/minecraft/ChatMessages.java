package app.tickettool.minecraft;

import java.io.File;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.nio.charset.StandardCharsets;
import java.util.Map;
import org.bukkit.command.CommandSender;
import org.bukkit.configuration.file.YamlConfiguration;
import org.bukkit.plugin.java.JavaPlugin;

/**
 * Player-facing text from messages.yml. Keys missing from the server's copy
 * fall back to the bundled defaults, so an old file keeps working after an
 * update adds messages.
 */
public final class ChatMessages {
    private final YamlConfiguration config;

    private ChatMessages(YamlConfiguration config) {
        this.config = config;
    }

    public static ChatMessages load(JavaPlugin plugin) {
        File file = new File(plugin.getDataFolder(), "messages.yml");
        if (!file.exists()) {
            plugin.saveResource("messages.yml", false);
        }
        YamlConfiguration config = YamlConfiguration.loadConfiguration(file);
        try (InputStream in = plugin.getResource("messages.yml")) {
            if (in != null) {
                config.setDefaults(YamlConfiguration.loadConfiguration(new InputStreamReader(in, StandardCharsets.UTF_8)));
            }
        } catch (java.io.IOException ignored) {
            // The bundled copy is inside our own jar; failing to read it leaves the server's file as the only source.
        }
        return new ChatMessages(config);
    }

    /** For tests and tools: messages from an already loaded file. */
    static ChatMessages of(YamlConfiguration config) {
        return new ChatMessages(config);
    }

    public String raw(String key) {
        return config.getString(key, key);
    }

    /** Colorized, with {placeholders} filled in, without the prefix. */
    public String format(String key, Map<String, String> values) {
        String text = raw(key);
        for (Map.Entry<String, String> e : values.entrySet()) {
            text = text.replace("{" + e.getKey() + "}", e.getValue() == null ? "" : e.getValue());
        }
        return Colors.translate(text);
    }

    /**
     * Like {@link #format(String, Map)}, but the values are text from Discord, so
     * their color codes are left as typed instead of being applied.
     */
    public String formatPlain(String key, Map<String, String> values) {
        String text = Colors.translate(raw(key));
        for (Map.Entry<String, String> e : values.entrySet()) {
            String value = e.getValue() == null ? "" : e.getValue().replace('§', '&');
            text = text.replace("{" + e.getKey() + "}", value);
        }
        return text;
    }

    public String format(String key) {
        return format(key, Map.of());
    }

    /** Sends one message with the prefix. An empty message in messages.yml means "don't send". */
    public void send(CommandSender to, String key, Map<String, String> values) {
        if (raw(key).isEmpty()) {
            return;
        }
        to.sendMessage(Colors.translate(raw("prefix")) + format(key, values));
    }

    public void send(CommandSender to, String key) {
        send(to, key, Map.of());
    }
}
