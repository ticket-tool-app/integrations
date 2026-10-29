using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Config;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.ValveConstants.Protobuf;
using Microsoft.Extensions.Logging;
using TicketTool.PluginSdk;

namespace TicketToolLink;

public sealed class TicketToolLinkPlugin : BasePlugin, IPluginConfig<PluginConfig>
{
    private static readonly string Version =
        typeof(TicketToolLinkPlugin).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion ?? "0.0.0-dev";

    public override string ModuleName => "Ticket Tool Link";
    public override string ModuleVersion => Version;
    public override string ModuleAuthor => "Ticket Tool";
    public override string ModuleDescription => "Shows staff a ticket opener's CS2 profile and bans in Ticket Tool";

    public PluginConfig Config { get; set; } = new();

    private TicketToolClient? _client;
    private string? _setupProblem;
    private SeenStore _seen = null!;
    private NativeBanList _nativeBans = null!;
    private SimpleAdminBans? _simpleAdmin;
    private string? _simpleAdminNote;
    private int _simpleAdminFailures;
    private List<string> _actions = new();
    private string _reportCommand = "report";
    private readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, DateTime> _lastReport = new();

    public void OnConfigParsed(PluginConfig config)
    {
        Config = config;
    }

    // Re-read on every (re)start; problems are logged, never fatal.
    private void ApplyActionSettings()
    {
        var problems = new List<string>();
        _actions = GameText.EnabledActions(Config.Actions, problems);
        foreach (string p in problems) Logger.LogWarning("{Problem}", p);
    }

    public override void Load(bool hotReload)
    {
        _seen = new SeenStore(Path.Combine(ModuleDirectory, "seen.json"));
        _seen.Load();
        string game = Server.GameDirectory;
        _nativeBans = new NativeBanList(new[]
        {
            Path.Combine(game, "csgo", "cfg", "banned_user.cfg"),
            Path.Combine(game, "cfg", "banned_user.cfg"),
        });

        RegisterEventHandler<EventPlayerConnectFull>((ev, _) =>
        {
            MarkSeen(ev.Userid);
            return HookResult.Continue;
        });
        RegisterEventHandler<EventPlayerDisconnect>((ev, _) =>
        {
            MarkSeen(ev.Userid);
            SaveSeenInBackground();
            return HookResult.Continue;
        });

        if (hotReload)
            foreach (CCSPlayerController p in Utilities.GetPlayers()) MarkSeen(p);

        // Registered once: a renamed command takes effect when the plugin is reloaded.
        string command = (Config.ReportCommand ?? "").Trim().TrimStart('!', '/').ToLowerInvariant();
        if (command.StartsWith("css_", StringComparison.Ordinal)) command = command[4..];
        if (!GameText.IsCommandName(command))
        {
            Logger.LogWarning("report-command \"{Command}\" isn't a valid command name; using report", Config.ReportCommand);
            command = "report";
        }
        _reportCommand = command;
        AddCommand("css_" + command, "Report a player or a problem to the server's staff on Discord", OnReportCommand);
        StartClient();
    }

    public override void Unload(bool hotReload)
    {
        StopClient();
        try
        {
            _seen.Save();
        }
        catch (IOException e)
        {
            Logger.LogWarning("Couldn't save seen.json: {Message}", e.Message);
        }
    }

    private void StartClient()
    {
        _setupProblem = null;
        SetUpSimpleAdmin();
        ApplyActionSettings();
        if (string.IsNullOrWhiteSpace(Config.Key))
        {
            _setupProblem = "No key set. Paste the key from the Ticket Tool dashboard into "
                            + "configs/plugins/TicketToolLink/TicketToolLink.json and run css_ticketlink reload.";
            Logger.LogWarning("{Problem}", _setupProblem);
            return;
        }
        try
        {
            _client = new TicketToolClient(new TicketToolClientOptions
            {
                Key = Config.Key,
                ApiUrl = Config.ApiUrl,
                Plugin = new PluginInfo("cs2", "ticket-tool-cs2", Version, Platform()),
                BanProviders = () => _simpleAdmin != null
                    ? new[] { SimpleAdminBans.Provider, NativeBanList.Provider }
                    : new[] { NativeBanList.Provider },
                Handler = HandleLookupAsync,
                ActionHandler = HandleActionAsync,
                Actions = () => _actions,
                ReportsEnabled = () => Config.Reports,
                Logger = new CssLogger(Logger, Config.Debug),
            });
        }
        catch (ArgumentException e)
        {
            // PluginKey and ApiBase messages never repeat the key.
            _setupProblem = e.Message;
            Logger.LogWarning("{Problem}", _setupProblem);
            return;
        }
        _client.Start();
    }

    private void StopClient()
    {
        _client?.Stop();
        _client = null;
    }

    private void SetUpSimpleAdmin()
    {
        _simpleAdmin = null;
        _simpleAdminNote = null;
        _simpleAdminFailures = 0;
        if (Config.SimpleAdmin.Equals("off", StringComparison.OrdinalIgnoreCase)) return;
        // plugins/TicketToolLink -> configs/plugins/CS2-SimpleAdmin/CS2-SimpleAdmin.json
        string root = Path.GetFullPath(Path.Combine(ModuleDirectory, "..", ".."));
        string configPath = Path.Combine(root, "configs", "plugins", "CS2-SimpleAdmin", "CS2-SimpleAdmin.json");
        if (!File.Exists(configPath)) return;
        try
        {
            var settings = SimpleAdminSettings.FromConfigJson(File.ReadAllText(configPath), out string? why);
            if (settings == null)
            {
                _simpleAdminNote = why;
                Logger.LogInformation("Not reading CS2-SimpleAdmin bans: {Why}", why);
                return;
            }
            _simpleAdmin = new SimpleAdminBans(settings);
            Logger.LogInformation("Reading bans from CS2-SimpleAdmin's database ({Target})", settings.ToString());
        }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException)
        {
            _simpleAdminNote = "CS2-SimpleAdmin's config couldn't be read: " + e.Message;
            Logger.LogWarning("{Note}", _simpleAdminNote);
        }
    }

    private static string Platform()
    {
        string css = typeof(BasePlugin).Assembly.GetName().Version?.ToString(3) ?? "?";
        return "CounterStrikeSharp " + css;
    }

    private sealed record GameState(bool Online, string? Name, string? AdminFlags);

    // Called on a pool thread. Player and admin state are read on the next game
    // frame; bans are file and database reads, so they run here in parallel.
    private async Task<LookupResult> HandleLookupAsync(LookupRequest request, CancellationToken cancellation)
    {
        string steamId = request.Player.SteamId;
        ulong id = ulong.Parse(steamId);
        Task<GameState> game = OnGameThread(() => ReadGameState(id));
        Task<BanInfo?> bans = request.WantsBans ? ReadBansAsync(steamId, cancellation) : Task.FromResult<BanInfo?>(null);
        GameState state = await game;
        BanInfo? ban = await bans;
        SeenStore.Entry? seen = _seen.Get(steamId);

        bool everJoined = state.Online || seen != null || state.AdminFlags != null;
        if (!everJoined && ban is not { Banned: true }) return LookupResult.Failure(LookupResult.UnknownPlayer);

        PlayerProfile? profile = null;
        if (request.WantsProfile)
        {
            profile = new PlayerProfile
            {
                SteamId = steamId,
                Name = state.Name ?? seen?.Name ?? request.Player.Name,
                Online = state.Online,
                LastSeen = state.Online ? DateTimeOffset.UtcNow : FromUnix(seen?.LastSeen ?? 0),
                FirstSeen = FromUnix(seen?.FirstSeen ?? 0),
                Server = ServerName(),
            };
            if (state.AdminFlags != null) profile.AddField("Admin", state.AdminFlags);
        }
        return LookupResult.Answer(profile, ban);
    }

    private static GameState ReadGameState(ulong id)
    {
        CCSPlayerController? player = Utilities.GetPlayerFromSteamId(id);
        bool online = player is { IsValid: true, IsBot: false }
                      && player.Connected == PlayerConnectedState.Connected;
        AdminData? admin = AdminManager.GetPlayerAdminData(new SteamID(id));
        string? flags = null;
        if (admin != null)
        {
            var all = admin.GetAllFlags().OrderBy(f => f, StringComparer.Ordinal).ToList();
            flags = all.Count == 0 ? null : string.Join(", ", all);
        }
        return new GameState(online, online ? player!.PlayerName : null, flags);
    }

    private async Task<BanInfo?> ReadBansAsync(string steamId, CancellationToken cancellation)
    {
        BanInfo? simpleAdmin = null;
        SimpleAdminBans? sa = _simpleAdmin;
        if (sa != null)
        {
            try
            {
                simpleAdmin = await sa.LookupAsync(steamId, cancellation);
                _simpleAdminFailures = 0;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Logged once per streak so a database outage doesn't flood the console.
                if (Interlocked.Increment(ref _simpleAdminFailures) == 1)
                    Logger.LogWarning("CS2-SimpleAdmin's database didn't answer; using the server ban list: {Message}",
                        e.Message);
            }
        }
        if (simpleAdmin is { Banned: true }) return simpleAdmin;
        if (_nativeBans.IsBanned(steamId))
            return new BanInfo(true, NativeBanList.Provider, null, null, null, null, null);
        return simpleAdmin ?? BanInfo.NotBanned(NativeBanList.Provider);
    }

    // Called on a pool thread. Unban checks the ban sources first so staff hear
    // not_banned; everything that touches a player runs on the next game frame.
    private async Task<ActionResult> HandleActionAsync(ActionRequest request, CancellationToken cancellation)
    {
        string steamId = request.Player.SteamId;
        ulong id = ulong.Parse(steamId);
        if (request.Kind == ActionRequest.Unban)
        {
            BanInfo? ban = await ReadBansAsync(steamId, cancellation);
            if (ban is not { Banned: true }) return ActionResult.Failure(ActionResult.NotBanned);
            // steamId is a validated SteamID64, so it is safe on a console line.
            string line = ban.Provider == SimpleAdminBans.Provider
                ? "css_unban " + steamId
                : "removeid " + new SteamID(id).SteamId3 + "; writeid";
            await OnGameThread(() =>
            {
                Server.ExecuteCommand(line);
                return true;
            });
            Logger.LogInformation("{Actor} (Ticket Tool) unbanned {SteamId}", request.ActorName, steamId);
            return ActionResult.Done();
        }
        return await OnGameThread(() => Act(request, id));
    }

    private ActionResult Act(ActionRequest request, ulong id)
    {
        CCSPlayerController? player = Utilities.GetPlayerFromSteamId(id);
        if (player is not { IsValid: true, IsBot: false } || player.Connected != PlayerConnectedState.Connected)
            return ActionResult.Failure(ActionResult.PlayerOffline);
        // Staff text is shown as typed: CS2 color codes are control characters, which this drops.
        string message = Text.StripFormatting(request.Text ?? "") ?? "";
        switch (request.Kind)
        {
            case ActionRequest.Message:
                player.PrintToChat(" " + ChatColors.Blue + "[Staff] " + Text.StripFormatting(request.ActorName) + ": "
                                   + ChatColors.Default + message);
                break;
            case ActionRequest.Warn:
                player.PrintToChat(" " + ChatColors.Red + "Warning from staff: " + ChatColors.Default + message);
                player.PrintToCenter("Warning from staff: " + message);
                break;
            case ActionRequest.Kick:
                if (player.UserId is int userId)
                {
                    string reason = GameText.ConsoleArg(message.Length > 0 ? "Kicked by staff: " + message : "Kicked by staff");
                    Server.ExecuteCommand("kickid " + userId + " \"" + reason + "\"");
                }
                else
                {
                    player.Disconnect(NetworkDisconnectionReason.NETWORK_DISCONNECT_KICKED);
                }
                break;
            default:
                return ActionResult.Failure(ActionResult.Unsupported);
        }
        Logger.LogInformation("{Actor} (Ticket Tool) sent a {Kind} to {Player}", request.ActorName, request.Kind,
            player.PlayerName);
        return ActionResult.Done();
    }

    private void OnReportCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (caller is not { IsValid: true, IsBot: false })
        {
            command.ReplyToCommand("Only players can send a report.");
            return;
        }
        TicketToolClient? client = _client;
        if (!Config.Reports || client == null)
        {
            command.ReplyToCommand(GameText.ReportReply(ReportOutcome.NotEnabled));
            return;
        }
        var args = new List<string>();
        for (int i = 1; i < command.ArgCount; i++) args.Add(command.GetArg(i));
        var (targetName, message) = GameText.SplitReport(args, name => Utilities.GetPlayers()
            .FirstOrDefault(p => p is { IsValid: true, IsBot: false }
                                 && string.Equals(p.PlayerName, name, StringComparison.OrdinalIgnoreCase))?.PlayerName);
        if (message.Length == 0)
        {
            command.ReplyToCommand("Type !" + _reportCommand + " <message>, or !" + _reportCommand
                                   + " <player> <message> to report someone.");
            return;
        }
        ulong sid = caller.SteamID;
        DateTime now = DateTime.UtcNow;
        if (_lastReport.TryGetValue(sid, out DateTime last) && (now - last).TotalSeconds < Config.ReportCooldownSeconds)
        {
            int wait = Math.Max(1, (int)Math.Ceiling(Config.ReportCooldownSeconds - (now - last).TotalSeconds));
            command.ReplyToCommand("You just sent a report. You can send another in " + wait + "s.");
            return;
        }
        _lastReport[sid] = now;
        command.ReplyToCommand("Sending your report...");
        string steamId = sid.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string name = caller.PlayerName;
        CCSPlayerController? target = targetName == null ? null : Utilities.GetPlayers()
            .FirstOrDefault(p => p is { IsValid: true, IsBot: false } && p.PlayerName == targetName);
        RequestPlayer? about = target == null
            ? null
            : new RequestPlayer(null, target.SteamID.ToString(System.Globalization.CultureInfo.InvariantCulture), null,
                target.PlayerName);
        _ = Task.Run(async () =>
        {
            ReportResult result = await client.ReportAsync(new RequestPlayer(null, steamId, null, name), message, about,
                ServerName());
            if (result.Outcome == ReportOutcome.Failed)
            {
                _lastReport.TryRemove(sid, out _);
                if (result.Error == "plan_not_entitled") Logger.LogWarning("Reports need the Pro plan on the Discord server.");
                else if (result.Error != "not_running")
                    Logger.LogWarning("Sending {Name}'s report failed: {Error}", name, result.Error);
            }
            Server.NextFrame(() =>
            {
                CCSPlayerController? p = Utilities.GetPlayerFromSteamId(sid);
                if (p is { IsValid: true }) p.PrintToChat(" " + GameText.ReportReply(result.Outcome));
            });
        });
    }

    private static Task<T> OnGameThread<T>(Func<T> read)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Server.NextFrame(() =>
        {
            try
            {
                tcs.TrySetResult(read());
            }
            catch (Exception e)
            {
                tcs.TrySetException(e);
            }
        });
        return tcs.Task;
    }

    private string? ServerName()
    {
        if (!string.IsNullOrWhiteSpace(Config.ServerName)) return Config.ServerName.Trim();
        return _client?.Status.Hello?.KeyName;
    }

    private void MarkSeen(CCSPlayerController? player)
    {
        if (player is not { IsValid: true, IsBot: false }) return;
        string id = player.SteamID.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!SteamIds.IsSteamId64(id)) return;
        _seen.Mark(id, player.PlayerName, DateTimeOffset.UtcNow);
    }

    private void SaveSeenInBackground()
    {
        Task.Run(() =>
        {
            try
            {
                _seen.Save();
            }
            catch (IOException e)
            {
                Logger.LogWarning("Couldn't save seen.json: {Message}", e.Message);
            }
        });
    }

    [ConsoleCommand("css_ticketlink", "Ticket Tool Link: status | reload")]
    [RequiresPermissions("@css/root")]
    public void OnTicketLinkCommand(CCSPlayerController? caller, CommandInfo command)
    {
        string sub = command.ArgCount > 1 ? command.GetArg(1).ToLowerInvariant() : "status";
        if (sub == "reload")
        {
            StopClient();
            try
            {
                Config = ConfigManager.Load<PluginConfig>("TicketToolLink");
            }
            catch (Exception e)
            {
                command.ReplyToCommand("Couldn't read TicketToolLink.json: " + e.Message);
                return;
            }
            StartClient();
            command.ReplyToCommand(_setupProblem ?? "Reloaded. Connecting to Ticket Tool...");
            return;
        }
        if (sub != "status")
        {
            command.ReplyToCommand("Usage: css_ticketlink status | css_ticketlink reload");
            return;
        }
        foreach (string line in StatusLines()) command.ReplyToCommand(line);
    }

    private IEnumerable<string> StatusLines()
    {
        yield return "Ticket Tool Link " + Version;
        if (_client == null)
        {
            yield return _setupProblem ?? "Not running.";
            yield break;
        }
        ClientStatus s = _client.Status;
        yield return s.State switch
        {
            ClientState.Connected => "Connected to Ticket Tool.",
            ClientState.Starting => "Connecting to Ticket Tool...",
            ClientState.Reconnecting => "Reconnecting to Ticket Tool.",
            ClientState.PausedPlan => "Paused: the Discord server's plan doesn't include game lookups.",
            ClientState.StoppedInvalidKey => "Stopped: Ticket Tool doesn't recognize this key.",
            _ => "Stopped.",
        };
        yield return "Key: " + _client.Key;
        if (s.Hello != null)
            yield return "Discord server: " + (s.Hello.ServerName ?? s.Hello.ServerId) + " (key \"" + s.Hello.KeyName + "\")";
        yield return "Ban sources: " + (_simpleAdmin != null ? "CS2-SimpleAdmin, then banned_user.cfg" : "banned_user.cfg")
                     + (_simpleAdminNote != null ? " (" + _simpleAdminNote + ")" : "");
        yield return "Actions from Discord: " + (_actions.Count > 0 ? string.Join(", ", _actions) : "none");
        yield return "In-game reports: " + (Config.Reports ? "on (!" + _reportCommand + ")" : "off");
        yield return "Lookups answered: " + s.Answered;
        if (s.LastError != null) yield return "Last problem: " + s.LastError;
    }

    private static DateTimeOffset? FromUnix(long seconds) =>
        seconds > 0 ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;

    private sealed class CssLogger(ILogger logger, bool debug) : ISdkLogger
    {
        public void Debug(string message)
        {
            if (debug) logger.LogInformation("[debug] {Message}", message);
        }

        public void Info(string message) => logger.LogInformation("{Message}", message);

        public void Warn(string message, Exception error)
        {
            if (error != null && debug) logger.LogWarning(error, "{Message}", message);
            else logger.LogWarning("{Message}", message);
        }
    }
}
