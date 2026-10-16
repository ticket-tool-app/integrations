// Ticket Tool Link for Rust (Oxide/uMod and Carbon).
// https://github.com/ticket-tool-app/integrations - MIT License, JTF Labs Pty Ltd.
//
// The released TicketToolLink.cs has the plugin SDK appended by rust/build.py,
// because Oxide only loads plugins that are a single source file. Edit this file
// and sdk-dotnet/, not the release file.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using TicketTool.PluginSdk;

namespace Oxide.Plugins
{
    [Info("Ticket Tool Link", "Ticket Tool", "0.0.0")]
    [Description("Shows staff a ticket opener's Rust profile and bans in Ticket Tool")]
    public class TicketToolLink : RustPlugin
    {
        private const string PluginVersion = "0.0.0-dev";
        private const string PermAdmin = "tickettoollink.admin";
        private const string SeenDataFile = "TicketToolLink_seen";
        private const string BanProvider = "server";

        [PluginReference] private Plugin PlaytimeTracker;
        [PluginReference] private Plugin Clans;

        private PluginConfig _config;
        private TicketToolClient _client;
        private string _setupProblem;
        private Dictionary<string, SeenEntry> _seen = new Dictionary<string, SeenEntry>();
        private bool _seenDirty;

        private class PluginConfig
        {
            [JsonProperty("key")] public string Key = "";

            [JsonProperty("api-url")] public string ApiUrl = TicketToolClient.DefaultApiUrl;

            [JsonProperty("server-name")] public string ServerName = "";

            [JsonProperty("debug")] public bool Debug;
        }

        private class SeenEntry
        {
            [JsonProperty("name")] public string Name;
            [JsonProperty("firstSeen")] public long FirstSeen;
            [JsonProperty("lastSeen")] public long LastSeen;
        }

        protected override void LoadDefaultConfig()
        {
            _config = new PluginConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<PluginConfig>();
                if (_config == null) throw new InvalidOperationException("empty config");
            }
            catch (Exception)
            {
                PrintWarning("{0}", "The config file couldn't be read; using defaults. Check it is valid JSON.");
                LoadDefaultConfig();
            }
            if (_config.ApiUrl == null) _config.ApiUrl = TicketToolClient.DefaultApiUrl;
            SaveConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        private void Init()
        {
            permission.RegisterPermission(PermAdmin, this);
            AddCovalenceCommand(new[] { "ticketlink", "tickettool" }, nameof(CmdTicketLink));
            try
            {
                _seen = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<string, SeenEntry>>(SeenDataFile)
                        ?? new Dictionary<string, SeenEntry>();
            }
            catch (Exception)
            {
                _seen = new Dictionary<string, SeenEntry>();
            }
        }

        private void OnServerInitialized()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList) MarkSeen(player);
            StartClient();
        }

        private void Unload()
        {
            StopClient();
            foreach (BasePlayer player in BasePlayer.activePlayerList) MarkSeen(player);
            SaveSeen();
        }

        private void OnServerSave()
        {
            SaveSeen();
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            MarkSeen(player);
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            MarkSeen(player);
        }

        private void StartClient()
        {
            _setupProblem = null;
            if (string.IsNullOrWhiteSpace(_config.Key))
            {
                _setupProblem = "No key set. Paste the key from the Ticket Tool dashboard into oxide/config/TicketToolLink.json "
                                + "(carbon/configs on Carbon) and run ticketlink reload.";
                PrintWarning("{0}", _setupProblem);
                return;
            }
            try
            {
                _client = new TicketToolClient(new TicketToolClientOptions
                {
                    Key = _config.Key,
                    ApiUrl = _config.ApiUrl,
                    Plugin = new PluginInfo("rust", "ticket-tool-rust", PluginVersion, Platform()),
                    BanProviders = () => new[] { BanProvider },
                    Handler = HandleLookup,
                    Logger = new OxideLogger(this, _config.Debug),
                    Sender = new WebRequestSender(),
                });
            }
            catch (ArgumentException e)
            {
                // PluginKey and ApiBase messages never repeat the key.
                _setupProblem = e.Message;
                PrintWarning("{0}", _setupProblem);
                return;
            }
            _client.Start();
        }

        private void StopClient()
        {
            if (_client == null) return;
            _client.Stop();
            _client = null;
        }

        private string Platform()
        {
            string version = "";
            try
            {
                version = covalence.Server.Version;
            }
            catch (Exception)
            {
            }
#if CARBON
            return ("Carbon / Rust " + version).Trim();
#else
            return ("Oxide " + OxideMod.Version + " / Rust " + version).Trim();
#endif
        }

        // Called on a pool thread; game state is read on the next server tick.
        private Task<LookupResult> HandleLookup(LookupRequest request, CancellationToken cancellation)
        {
            var answer = new TaskCompletionSource<LookupResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            NextTick(() =>
            {
                if (cancellation.IsCancellationRequested) return;
                try
                {
                    answer.TrySetResult(Answer(request));
                }
                catch (Exception e)
                {
                    answer.TrySetException(e);
                }
            });
            return answer.Task;
        }

        private LookupResult Answer(LookupRequest request)
        {
            string steamId = request.Player.SteamId;
            ulong id;
            if (!ulong.TryParse(steamId, NumberStyles.None, CultureInfo.InvariantCulture, out id))
                return LookupResult.Failure(LookupResult.Unsupported);

            BanInfo bans = request.WantsBans ? ReadBan(id) : null;
            BasePlayer player = BasePlayer.FindAwakeOrSleeping(steamId);
            IPlayer known = covalence.Players.FindPlayerById(steamId);
            SeenEntry seen;
            _seen.TryGetValue(steamId, out seen);

            bool everJoined = player != null || known != null || seen != null;
            if (!everJoined && (bans == null || !bans.Banned))
                return LookupResult.Failure(LookupResult.UnknownPlayer);

            PlayerProfile profile = null;
            if (request.WantsProfile)
            {
                bool online = player != null && player.IsConnected;
                profile = new PlayerProfile
                {
                    SteamId = steamId,
                    Name = FirstNonEmpty(player != null ? player.displayName : null, known != null ? known.Name : null,
                        seen != null ? seen.Name : null, request.Player.Name),
                    Online = online,
                    LastSeen = online ? DateTimeOffset.UtcNow : FromUnix(seen != null ? seen.LastSeen : 0),
                    FirstSeen = FromUnix(seen != null ? seen.FirstSeen : 0),
                    Server = ServerName(),
                };
                AddFields(profile, id, steamId);
            }
            return LookupResult.Answer(profile, bans);
        }

        private void AddFields(PlayerProfile profile, ulong id, string steamId)
        {
            ServerUsers.User user = ServerUsers.Get(id);
            if (user != null && user.group == ServerUsers.UserGroup.Owner) profile.AddField("Role", "Owner");
            else if (user != null && user.group == ServerUsers.UserGroup.Moderator) profile.AddField("Role", "Moderator");

            string playtime = Playtime(steamId);
            if (playtime != null) profile.AddField("Playtime", playtime);

            if (Clans != null && Clans.IsLoaded)
            {
                try
                {
                    string tag = Clans.Call("GetClanOf", steamId) as string;
                    if (!string.IsNullOrEmpty(tag)) profile.AddField("Clan", tag);
                }
                catch (Exception e)
                {
                    if (_config.Debug) Puts("{0}", "Clans lookup failed: " + e.Message);
                }
            }
        }

        private string Playtime(string steamId)
        {
            if (PlaytimeTracker == null || !PlaytimeTracker.IsLoaded) return null;
            try
            {
                object seconds = PlaytimeTracker.Call("GetPlayTime", steamId);
                if (seconds == null) return null;
                double total = Convert.ToDouble(seconds, CultureInfo.InvariantCulture);
                if (total <= 0) return null;
                long minutes = (long)(total / 60);
                return (minutes / 60).ToString(CultureInfo.InvariantCulture) + "h "
                       + (minutes % 60).ToString(CultureInfo.InvariantCulture) + "m";
            }
            catch (Exception e)
            {
                if (_config.Debug) Puts("{0}", "PlaytimeTracker lookup failed: " + e.Message);
                return null;
            }
        }

        private static BanInfo ReadBan(ulong id)
        {
            ServerUsers.User user = ServerUsers.Get(id);
            if (user == null || user.group != ServerUsers.UserGroup.Banned) return BanInfo.NotBanned(BanProvider);
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            // A lapsed temporary ban stays in the list until the player next joins.
            if (user.expiry > 0 && user.expiry <= now) return BanInfo.NotBanned(BanProvider);
            return new BanInfo(true, BanProvider, user.notes, null,
                user.expiry > 0 ? DateTimeOffset.FromUnixTimeSeconds(user.expiry) : (DateTimeOffset?)null, null, null);
        }

        private string ServerName()
        {
            if (!string.IsNullOrWhiteSpace(_config.ServerName)) return _config.ServerName.Trim();
            TicketToolClient client = _client;
            HelloReply hello = client == null ? null : client.Status.Hello;
            return hello == null ? null : hello.KeyName;
        }

        private void MarkSeen(BasePlayer player)
        {
            if (player == null) return;
            string id = player.UserIDString;
            if (!SteamIds.IsSteamId64(id)) return;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            SeenEntry entry;
            if (!_seen.TryGetValue(id, out entry))
            {
                entry = new SeenEntry { FirstSeen = now };
                _seen[id] = entry;
            }
            entry.Name = player.displayName;
            entry.LastSeen = now;
            _seenDirty = true;
        }

        private void SaveSeen()
        {
            if (!_seenDirty) return;
            Interface.Oxide.DataFileSystem.WriteObject(SeenDataFile, _seen);
            _seenDirty = false;
        }

        private void CmdTicketLink(IPlayer player, string command, string[] args)
        {
            if (!player.IsServer && !player.IsAdmin && !player.HasPermission(PermAdmin))
            {
                player.Reply("You don't have permission to use this command.");
                return;
            }
            string sub = args.Length > 0 ? args[0].ToLowerInvariant() : "status";
            if (sub == "reload")
            {
                StopClient();
                LoadConfig();
                StartClient();
                player.Reply(_setupProblem ?? "Reloaded. Connecting to Ticket Tool...");
                return;
            }
            if (sub != "status")
            {
                player.Reply("Usage: ticketlink status | ticketlink reload");
                return;
            }
            player.Reply(StatusText());
        }

        private string StatusText()
        {
            if (_client == null) return "Ticket Tool Link: " + (_setupProblem ?? "not running.");
            ClientStatus s = _client.Status;
            var lines = new List<string> { "Ticket Tool Link " + PluginVersion };
            switch (s.State)
            {
                case ClientState.Connected: lines.Add("Connected to Ticket Tool."); break;
                case ClientState.Starting: lines.Add("Connecting to Ticket Tool..."); break;
                case ClientState.Reconnecting: lines.Add("Reconnecting to Ticket Tool."); break;
                case ClientState.PausedPlan: lines.Add("Paused: the Discord server's plan doesn't include game lookups."); break;
                case ClientState.StoppedInvalidKey: lines.Add("Stopped: Ticket Tool doesn't recognize this key."); break;
                default: lines.Add("Stopped."); break;
            }
            lines.Add("Key: " + _client.Key);
            if (s.Hello != null)
                lines.Add("Discord server: " + (s.Hello.ServerName ?? s.Hello.ServerId) + " (key \"" + s.Hello.KeyName + "\")");
            lines.Add("Ban source: Rust server ban list");
            lines.Add("Lookups answered: " + s.Answered);
            if (s.LastError != null) lines.Add("Last problem: " + s.LastError);
            return string.Join("\n", lines.ToArray());
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string v in values)
                if (!string.IsNullOrWhiteSpace(v)) return v;
            return null;
        }

        private static DateTimeOffset? FromUnix(long seconds)
        {
            return seconds > 0 ? DateTimeOffset.FromUnixTimeSeconds(seconds) : (DateTimeOffset?)null;
        }

        // The SDK logs from pool threads; Oxide's logger queues writes, so this is safe off the main thread.
        private sealed class OxideLogger : ISdkLogger
        {
            private readonly TicketToolLink _plugin;
            private readonly bool _debug;

            public OxideLogger(TicketToolLink plugin, bool debug)
            {
                _plugin = plugin;
                _debug = debug;
            }

            public void Debug(string message)
            {
                if (_debug) _plugin.Puts("{0}", "[debug] " + message);
            }

            public void Info(string message)
            {
                _plugin.Puts("{0}", message);
            }

            public void Warn(string message, Exception error)
            {
                _plugin.PrintWarning("{0}", error == null || !_debug ? message : message + "\n" + error);
            }
        }
    }
}

// @inline-sdk
