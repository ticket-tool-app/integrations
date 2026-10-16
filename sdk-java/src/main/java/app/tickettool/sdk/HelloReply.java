package app.tickettool.sdk;

/**
 * The server's answer to {@code /hello}.
 *
 * @param keyName    the name the admin gave the key, e.g. "Survival"
 * @param serverId   the Discord server's id
 * @param serverName the Discord server's name; may be null
 */
public record HelloReply(int protocol, String pluginType, String keyName, String serverId, String serverName,
        int pollWaitSeconds) {}
