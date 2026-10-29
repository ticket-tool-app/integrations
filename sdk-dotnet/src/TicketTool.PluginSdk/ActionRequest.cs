using System;
using System.Collections.Generic;

namespace TicketTool.PluginSdk
{
    /// <summary>A player.action request from /poll: staff asked the plugin to act on a player in game.</summary>
    public sealed class ActionRequest
    {
        public const string TypePlayerAction = "player.action";
        public const string Message = "message";
        public const string Warn = "warn";
        public const string Kick = "kick";
        public const string Unban = "unban";

        /// <summary>Every kind, in the order /hello lists them.</summary>
        public static readonly IList<string> Kinds = new[] { Message, Warn, Kick, Unban };

        public ActionRequest(string id, DateTimeOffset expiresAt, RequestPlayer player, string kind, string text,
            string actorName)
        {
            Id = id;
            ExpiresAt = expiresAt;
            Player = player ?? new RequestPlayer(null, null, null, null);
            Kind = kind;
            Text = text;
            ActorName = actorName ?? "Ticket Tool";
        }

        public string Id { get; private set; }
        public DateTimeOffset ExpiresAt { get; private set; }
        public RequestPlayer Player { get; private set; }

        /// <summary>One of Message, Warn, Kick or Unban.</summary>
        public string Kind { get; private set; }

        /// <summary>Plain text for the player, at most 500 characters; null for unban.</summary>
        public string Text { get; private set; }

        /// <summary>The staff member's Discord username, or "Ticket Tool" for an automation.</summary>
        public string ActorName { get; private set; }
    }
}
