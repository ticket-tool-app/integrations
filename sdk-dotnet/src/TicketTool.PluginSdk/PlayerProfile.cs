using System;
using System.Collections.Generic;

namespace TicketTool.PluginSdk
{
    /// <summary>A label and value shown on the staff card, e.g. Playtime / 12h 30m.</summary>
    public sealed class ProfileField
    {
        public const int MaxLabel = 32;
        public const int MaxValue = 100;

        public ProfileField(string label, string value)
        {
            Label = Text.Clean(label, MaxLabel);
            Value = Text.Clean(value, MaxValue);
        }

        public string Label { get; private set; }
        public string Value { get; private set; }
    }

    /// <summary>
    /// The profile part of an answer. Leave the identity fields null to have the
    /// SDK copy them from the request; a value that differs from the request's is
    /// refused, because Ticket Tool rejects a mismatched identity.
    /// </summary>
    public sealed class PlayerProfile
    {
        public const int MaxFields = 6;

        public PlayerProfile()
        {
            Fields = new List<ProfileField>();
        }

        public string Uuid { get; set; }
        public string SteamId { get; set; }
        public string DiscordId { get; set; }

        /// <summary>In-game name, 1 to 64 characters.</summary>
        public string Name { get; set; }

        public bool Online { get; set; }

        /// <summary>Last time on this server; null when unknown.</summary>
        public DateTimeOffset? LastSeen { get; set; }

        /// <summary>First join; null when unknown.</summary>
        public DateTimeOffset? FirstSeen { get; set; }

        /// <summary>Server or world name staff see; null to leave it out.</summary>
        public string Server { get; set; }

        /// <summary>Up to six extra lines for the card; more are dropped.</summary>
        public List<ProfileField> Fields { get; private set; }

        public PlayerProfile AddField(string label, string value)
        {
            var f = new ProfileField(label, value);
            if (f.Label != null && f.Value != null && Fields.Count < MaxFields) Fields.Add(f);
            return this;
        }
    }
}
