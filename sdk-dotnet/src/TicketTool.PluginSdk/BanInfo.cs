using System;

namespace TicketTool.PluginSdk
{
    /// <summary>
    /// The bans part of an answer. The constructor strips formatting and applies
    /// the server's length limits, so a provider adapter can pass raw values
    /// straight through.
    /// </summary>
    public sealed class BanInfo
    {
        public const int MaxReason = 2000;

        /// <param name="provider">e.g. server, simpleadmin, ulx.</param>
        /// <param name="expiresAt">null means permanent.</param>
        /// <param name="activeCount">Active bans on record; only sent when more than one.</param>
        public BanInfo(bool banned, string provider, string reason, DateTimeOffset? createdAt,
            DateTimeOffset? expiresAt, string actor, int? activeCount)
        {
            if (provider == null) throw new ArgumentNullException("provider");
            Banned = banned;
            Provider = Text.Clip(provider.Trim().ToLowerInvariant(), 32);
            Reason = Text.Clean(reason, MaxReason);
            CreatedAt = createdAt;
            ExpiresAt = expiresAt;
            Actor = Text.Clean(actor, 64);
            ActiveCount = activeCount.HasValue && (activeCount.Value < 0 || activeCount.Value > 100000)
                ? null
                : activeCount;
        }

        public bool Banned { get; private set; }
        public string Provider { get; private set; }
        public string Reason { get; private set; }
        public DateTimeOffset? CreatedAt { get; private set; }
        public DateTimeOffset? ExpiresAt { get; private set; }
        public string Actor { get; private set; }
        public int? ActiveCount { get; private set; }

        public static BanInfo NotBanned(string provider)
        {
            return new BanInfo(false, provider, null, null, null, null, null);
        }
    }
}
