namespace TicketTool.PluginSdk
{
    /// <summary>The server's answer to /hello.</summary>
    public sealed class HelloReply
    {
        public HelloReply(int protocol, string pluginType, string keyName, string serverId, string serverName,
            int pollWaitSeconds)
        {
            Protocol = protocol;
            PluginType = pluginType;
            KeyName = keyName;
            ServerId = serverId;
            ServerName = serverName;
            PollWaitSeconds = pollWaitSeconds;
        }

        public int Protocol { get; private set; }
        public string PluginType { get; private set; }

        /// <summary>The name the admin gave the key, e.g. "Main".</summary>
        public string KeyName { get; private set; }

        /// <summary>The Discord server's id.</summary>
        public string ServerId { get; private set; }

        /// <summary>The Discord server's name; may be null.</summary>
        public string ServerName { get; private set; }

        public int PollWaitSeconds { get; private set; }
    }
}
