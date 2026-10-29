using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace TicketTool.PluginSdk
{
    /// <summary>Protocol v1 bodies to and from plain dictionaries.</summary>
    internal static class Messages
    {
        private static readonly HashSet<string> KnownParts =
            new HashSet<string> { LookupRequest.PartProfile, LookupRequest.PartBans };

        internal const int MaxActionMessage = 500;
        internal const int MaxReportMessage = 1000;
        internal const string ReportEvent = "player.report";

        private static readonly Regex Snowflake = new Regex("^[0-9]{15,21}$", RegexOptions.CultureInvariant);

        internal static Dictionary<string, object> Hello(PluginInfo info, IList<string> banProviders)
        {
            return Hello(info, banProviders, null, false);
        }

        internal static Dictionary<string, object> Hello(PluginInfo info, IList<string> banProviders,
            IList<string> actions, bool reports)
        {
            var plugin = new Dictionary<string, object>
            {
                { "type", info.Type },
                { "version", info.Version },
            };
            if (info.Platform != null) plugin["platform"] = info.Platform;
            var body = new Dictionary<string, object>
            {
                { "protocol", TicketToolClient.ProtocolVersion },
                { "plugin", plugin },
            };
            if (banProviders != null && banProviders.Count > 0)
                body["banProviders"] = banProviders.Take(8).Select(p => Text.Clip(p, 32)).ToList();
            var on = ActionRequest.Kinds.Where(k => actions != null && actions.Contains(k)).Cast<object>().ToList();
            if (on.Count > 0) body["actions"] = on;
            if (reports) body["events"] = new List<object> { ReportEvent };
            return body;
        }

        internal static HelloReply HelloReply(Dictionary<string, object> body)
        {
            var key = JsonFields.Object(body, "key");
            var server = JsonFields.Object(body, "server");
            var poll = JsonFields.Object(body, "poll");
            long? protocol = JsonFields.Long(body, "protocol");
            long? wait = JsonFields.Long(poll, "waitSeconds");
            return new HelloReply(
                protocol.HasValue ? (int)protocol.Value : TicketToolClient.ProtocolVersion,
                JsonFields.String(body, "pluginType"),
                JsonFields.String(key, "name"),
                JsonFields.String(server, "id"),
                JsonFields.String(server, "name"),
                wait.HasValue ? (int)Math.Max(1, Math.Min(120, wait.Value)) : 25);
        }

        /// <summary>Requests the SDK can act on; malformed entries are skipped, not fatal.</summary>
        internal static List<LookupRequest> PollRequests(Dictionary<string, object> body)
        {
            var output = new List<LookupRequest>();
            foreach (object item in JsonFields.List(body, "requests"))
            {
                var req = item as Dictionary<string, object>;
                if (req == null) continue;
                string id = JsonFields.String(req, "id");
                string type = JsonFields.String(req, "type");
                DateTimeOffset? expiresAt = Instant(JsonFields.String(req, "expiresAt"));
                if (id == null || type == null || !expiresAt.HasValue) continue;
                var player = Player(JsonFields.Object(req, "player"));
                var include = new List<string>();
                foreach (object part in JsonFields.List(req, "include"))
                {
                    var s = part as string;
                    if (s != null && KnownParts.Contains(s)) include.Add(s);
                }
                output.Add(new LookupRequest(id, type, expiresAt.Value, player, include));
            }
            return output;
        }

        private static RequestPlayer Player(Dictionary<string, object> p)
        {
            return new RequestPlayer(JsonFields.String(p, "uuid"), JsonFields.String(p, "steamId"),
                JsonFields.String(p, "discordId"), JsonFields.String(p, "name"));
        }

        /// <summary>player.action requests; malformed entries and unknown kinds are skipped.</summary>
        internal static List<ActionRequest> PollActions(Dictionary<string, object> body)
        {
            var output = new List<ActionRequest>();
            foreach (object item in JsonFields.List(body, "requests"))
            {
                var req = item as Dictionary<string, object>;
                if (req == null || JsonFields.String(req, "type") != ActionRequest.TypePlayerAction) continue;
                string id = JsonFields.String(req, "id");
                DateTimeOffset? expiresAt = Instant(JsonFields.String(req, "expiresAt"));
                var action = JsonFields.Object(req, "action");
                string kind = JsonFields.String(action, "kind");
                if (id == null || !expiresAt.HasValue || kind == null || !ActionRequest.Kinds.Contains(kind)) continue;
                string message = kind == ActionRequest.Unban
                    ? null
                    : Text.Clip(Text.EmptyToNull(JsonFields.String(action, "message")), MaxActionMessage);
                string actor = Text.Clip(Text.EmptyToNull(JsonFields.String(JsonFields.Object(req, "actor"), "name")),
                    64);
                output.Add(new ActionRequest(id, expiresAt.Value, Player(JsonFields.Object(req, "player")), kind,
                    message, actor));
            }
            return output;
        }

        internal static Dictionary<string, object> RespondAction(ActionRequest request, ActionResult result)
        {
            var body = new Dictionary<string, object> { { "requestId", request.Id } };
            if (!result.IsFailure)
            {
                body["ok"] = true;
                return body;
            }
            body["ok"] = false;
            body["error"] = result.ErrorCode;
            if (result.Message != null) body["message"] = result.Message;
            return body;
        }

        internal static Dictionary<string, object> ReportPlayer(RequestPlayer p)
        {
            var m = new Dictionary<string, object>();
            if (p.Uuid != null) m["uuid"] = p.Uuid;
            if (SteamIds.IsSteamId64(p.SteamId)) m["steamId"] = p.SteamId;
            if (p.DiscordId != null && Snowflake.IsMatch(p.DiscordId)) m["discordId"] = p.DiscordId;
            m["name"] = Text.Clip(Text.EmptyToNull(p.Name) ?? "Unknown", 64);
            return m;
        }

        internal static Dictionary<string, object> Report(RequestPlayer from, string message, RequestPlayer target,
            string server)
        {
            return new Dictionary<string, object>
            {
                { "type", ReportEvent },
                { "player", ReportPlayer(from) },
                { "message", Text.Clip(message.Trim(), MaxReportMessage) },
                { "target", target != null && Text.EmptyToNull(target.Name) != null ? ReportPlayer(target) : null },
                { "server", Text.Clip(Text.EmptyToNull(server), 64) },
            };
        }

        internal static ReportResult ReportReply(Dictionary<string, object> body)
        {
            return JsonFields.String(body, "result") == "opened"
                ? new ReportResult(ReportOutcome.Opened, null)
                : new ReportResult(ReportOutcome.Ignored, null);
        }

        internal static ReportResult ReportError(ApiException e)
        {
            string error = e.Error ?? "http_" + e.Status;
            return new ReportResult(error == "rate_limited" ? ReportOutcome.RateLimited : ReportOutcome.Failed, error);
        }

        /// <summary>
        /// The identity field a plugin type is looked up by, or null for a type
        /// this SDK doesn't know (it then trusts the handler).
        /// </summary>
        internal static string IdentityField(string pluginType)
        {
            switch (pluginType)
            {
                case "minecraft": return "uuid";
                case "fivem": return "discordId";
                case "gmod":
                case "rust":
                case "cs2":
                    return "steamId";
                default: return null;
            }
        }

        internal static string RequestIdentity(LookupRequest request, string field)
        {
            return PlayerIdentity(request.Player, field);
        }

        internal static string PlayerIdentity(RequestPlayer player, string field)
        {
            switch (field)
            {
                case "uuid": return player.Uuid;
                case "discordId": return player.DiscordId;
                case "steamId": return SteamIds.IsSteamId64(player.SteamId) ? player.SteamId : null;
                default: return null;
            }
        }

        internal static Dictionary<string, object> Respond(LookupRequest request, LookupResult result,
            string pluginType)
        {
            var body = new Dictionary<string, object> { { "requestId", request.Id } };
            if (result.IsFailure)
            {
                body["ok"] = false;
                body["error"] = result.ErrorCode;
                if (result.Message != null) body["message"] = result.Message;
                return body;
            }
            Dictionary<string, object> profile = null;
            if (result.Profile != null && request.WantsProfile)
            {
                string mismatch;
                profile = Profile(request, result.Profile, pluginType, out mismatch);
                if (mismatch != null)
                    return Respond(request, LookupResult.Failure(LookupResult.Error, mismatch), pluginType);
            }
            body["ok"] = true;
            if (profile != null) body["profile"] = profile;
            if (result.Bans != null && request.WantsBans) body["bans"] = Bans(result.Bans);
            return body;
        }

        internal static Dictionary<string, object> Profile(LookupRequest request, PlayerProfile p, string pluginType,
            out string mismatch)
        {
            mismatch = null;
            string uuid = p.Uuid ?? request.Player.Uuid;
            string steamId = p.SteamId;
            string discordId = p.DiscordId;
            string field = IdentityField(pluginType);
            if (field == "steamId")
            {
                if (steamId == null) steamId = request.Player.SteamId;
                else if (steamId != request.Player.SteamId) mismatch = "The plugin answered for a different player";
            }
            else if (field == "discordId")
            {
                if (discordId == null) discordId = request.Player.DiscordId;
                else if (discordId != request.Player.DiscordId) mismatch = "The plugin answered for a different player";
            }

            string name = Text.Clean(p.Name, 64) ?? Text.Clean(request.Player.Name, 64)
                          ?? steamId ?? discordId ?? uuid ?? "unknown";
            var m = new Dictionary<string, object>();
            if (field == "uuid" || (field == null && uuid != null)) m["uuid"] = uuid;
            if (steamId != null) m["steamId"] = steamId;
            if (discordId != null) m["discordId"] = discordId;
            m["name"] = name;
            m["online"] = p.Online;
            m["lastSeen"] = Iso(p.LastSeen);
            m["firstSeen"] = Iso(p.FirstSeen);
            m["server"] = Text.Clean(p.Server, 64);
            var fields = p.Fields.Where(f => f.Label != null && f.Value != null).Take(PlayerProfile.MaxFields)
                .Select(f => (object)new Dictionary<string, object> { { "label", f.Label }, { "value", f.Value } })
                .ToList();
            if (fields.Count > 0 && field != "uuid") m["fields"] = fields;
            return m;
        }

        internal static Dictionary<string, object> Bans(BanInfo b)
        {
            var m = new Dictionary<string, object>
            {
                { "banned", b.Banned },
                { "provider", b.Provider },
                { "reason", b.Banned ? b.Reason : null },
                { "createdAt", b.Banned ? Iso(b.CreatedAt) : null },
                { "expiresAt", b.Banned ? Iso(b.ExpiresAt) : null },
                { "actor", b.Banned ? b.Actor : null },
            };
            if (b.Banned && b.ActiveCount.HasValue && b.ActiveCount.Value > 1) m["activeCount"] = b.ActiveCount.Value;
            return m;
        }

        internal static string Iso(DateTimeOffset? t)
        {
            if (!t.HasValue) return null;
            return t.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        }

        internal static DateTimeOffset? Instant(string s)
        {
            if (s == null) return null;
            DateTimeOffset t;
            if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out t))
                return t;
            return null;
        }
    }
}
