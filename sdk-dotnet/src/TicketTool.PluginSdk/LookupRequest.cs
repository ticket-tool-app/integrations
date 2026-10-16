using System;
using System.Collections.Generic;

namespace TicketTool.PluginSdk
{
    /// <summary>The player a request is about. A plugin reads only the identity its type uses.</summary>
    public sealed class RequestPlayer
    {
        public RequestPlayer(string uuid, string steamId, string discordId, string name)
        {
            Uuid = uuid;
            SteamId = steamId;
            DiscordId = discordId;
            Name = name;
        }

        public string Uuid { get; private set; }

        /// <summary>SteamID64 as a decimal string, for gmod, rust and cs2 keys.</summary>
        public string SteamId { get; private set; }

        public string DiscordId { get; private set; }

        /// <summary>The name Ticket Tool last saw; not authoritative and may be null.</summary>
        public string Name { get; private set; }
    }

    /// <summary>One question from /poll. Parts the SDK doesn't know are dropped from Include.</summary>
    public sealed class LookupRequest
    {
        public const string TypePlayerLookup = "player.lookup";
        public const string PartProfile = "profile";
        public const string PartBans = "bans";

        public LookupRequest(string id, string type, DateTimeOffset expiresAt, RequestPlayer player,
            ICollection<string> include)
        {
            Id = id;
            Type = type;
            ExpiresAt = expiresAt;
            Player = player ?? new RequestPlayer(null, null, null, null);
            Include = new HashSet<string>(include ?? new string[0]);
        }

        public string Id { get; private set; }
        public string Type { get; private set; }
        public DateTimeOffset ExpiresAt { get; private set; }
        public RequestPlayer Player { get; private set; }
        public ISet<string> Include { get; private set; }

        public bool WantsProfile
        {
            get { return Include.Contains(PartProfile); }
        }

        public bool WantsBans
        {
            get { return Include.Contains(PartBans); }
        }
    }
}
