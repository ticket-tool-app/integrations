using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Core;
using TicketTool.PluginSdk;

namespace TicketToolLink;

/// <summary>addons/counterstrikesharp/configs/plugins/TicketToolLink/TicketToolLink.json</summary>
public sealed class PluginConfig : BasePluginConfig
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";

    [JsonPropertyName("api-url")] public string ApiUrl { get; set; } = TicketToolClient.DefaultApiUrl;

    /// <summary>The server name staff see; empty uses the key's name.</summary>
    [JsonPropertyName("server-name")] public string ServerName { get; set; } = "";

    /// <summary>auto reads CS2-SimpleAdmin's ban table when it's installed; off never does.</summary>
    [JsonPropertyName("simpleadmin")] public string SimpleAdmin { get; set; } = "auto";

    [JsonPropertyName("debug")] public bool Debug { get; set; }
}
