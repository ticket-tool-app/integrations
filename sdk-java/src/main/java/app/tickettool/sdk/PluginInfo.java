package app.tickettool.sdk;

import java.util.Objects;

/**
 * What the plugin reports about itself in {@code /hello} and {@code X-TT-Plugin}.
 *
 * @param type     the key's plugin type, e.g. {@code minecraft}
 * @param name     product name for the header, e.g. {@code ticket-tool-minecraft}
 * @param version  plugin version, at most 32 characters
 * @param platform server software, e.g. {@code Paper 1.21.1}; may be null
 */
public record PluginInfo(String type, String name, String version, String platform) {
    public PluginInfo {
        Objects.requireNonNull(type, "type");
        Objects.requireNonNull(name, "name");
        Objects.requireNonNull(version, "version");
        version = Text.clip(version, 32);
        platform = platform == null ? null : Text.clip(platform, 64);
    }

    /** The {@code X-TT-Plugin} value; the dashboard shows it next to the key. */
    public String userAgent() {
        String base = name + "/" + version;
        return Text.clip(platform == null || platform.isBlank() ? base : base + " (" + platform + ")", 100);
    }
}
