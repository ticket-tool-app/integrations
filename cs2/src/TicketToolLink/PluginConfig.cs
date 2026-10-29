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

    /// <summary>What staff can do from Discord; each stays off until listed: message, warn, kick, unban.</summary>
    [JsonPropertyName("actions")] public List<string> Actions { get; set; } = new();

    [JsonPropertyName("reports")] public bool Reports { get; set; }

    /// <summary>Players type !report or /report; registered as css_report.</summary>
    [JsonPropertyName("report-command")] public string ReportCommand { get; set; } = "report";

    [JsonPropertyName("report-cooldown-seconds")] public int ReportCooldownSeconds { get; set; } = 60;

    [JsonPropertyName("debug")] public bool Debug { get; set; }
}
