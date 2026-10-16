package app.tickettool.minecraft;

import app.tickettool.sdk.TicketToolClient;
import org.bukkit.configuration.file.FileConfiguration;

/**
 * config.yml, read once per load or reload.
 *
 * @param key        the full integration key; never logged or shown
 * @param serverName shown to staff as the backend; null to use the key's name
 */
record Settings(String key, String apiUrl, String serverName, String banProvider, int linkCooldownSeconds,
        boolean debug) {

    static Settings from(FileConfiguration c) {
        String server = c.getString("server-name", "").trim();
        return new Settings(
                c.getString("key", "").trim(),
                c.getString("api-url", TicketToolClient.DEFAULT_API_URL).trim(),
                server.isEmpty() ? null : server,
                c.getString("ban-provider", "auto"),
                Math.max(0, c.getInt("link-cooldown-seconds", 5)),
                c.getBoolean("debug", false));
    }

    boolean hasKey() {
        return !key.isEmpty();
    }

    @Override
    public String toString() {
        return "Settings[apiUrl=" + apiUrl + ", serverName=" + serverName + ", banProvider=" + banProvider + "]";
    }
}
