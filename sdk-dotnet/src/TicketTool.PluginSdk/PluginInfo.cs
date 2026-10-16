using System;

namespace TicketTool.PluginSdk
{
    /// <summary>What the plugin reports about itself in /hello and X-TT-Plugin.</summary>
    public sealed class PluginInfo
    {
        /// <param name="type">The key's plugin type: minecraft, fivem, gmod, rust or cs2.</param>
        /// <param name="name">Product name for the header, e.g. ticket-tool-rust.</param>
        /// <param name="version">Plugin version, at most 32 characters.</param>
        /// <param name="platform">Server software, e.g. "Oxide 2.0 / Rust 2590"; may be null.</param>
        public PluginInfo(string type, string name, string version, string platform)
        {
            if (type == null) throw new ArgumentNullException("type");
            if (name == null) throw new ArgumentNullException("name");
            if (version == null) throw new ArgumentNullException("version");
            Type = type;
            Name = name;
            Version = Text.Clip(version, 32);
            Platform = platform == null ? null : Text.Clip(platform, 64);
        }

        public string Type { get; private set; }
        public string Name { get; private set; }
        public string Version { get; private set; }
        public string Platform { get; private set; }

        /// <summary>The X-TT-Plugin value; the dashboard shows it next to the key.</summary>
        public string UserAgent
        {
            get
            {
                string b = Name + "/" + Version;
                return Text.Clip(string.IsNullOrWhiteSpace(Platform) ? b : b + " (" + Platform + ")", 100);
            }
        }
    }
}
