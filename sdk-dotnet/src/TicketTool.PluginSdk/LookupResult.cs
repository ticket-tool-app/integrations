namespace TicketTool.PluginSdk
{
    /// <summary>What a handler returns for one LookupRequest: an answer or a failure code.</summary>
    public sealed class LookupResult
    {
        public const string UnknownPlayer = "unknown_player";
        public const string NoBanProvider = "no_ban_provider";
        public const string Unsupported = "unsupported";
        public const string Error = "error";

        private LookupResult(PlayerProfile profile, BanInfo bans, string error, string message)
        {
            Profile = profile;
            Bans = bans;
            ErrorCode = error;
            Message = message == null ? null : Text.Clip(message, 200);
        }

        public PlayerProfile Profile { get; private set; }
        public BanInfo Bans { get; private set; }

        /// <summary>Null for an answer; one of the constants on this class for a failure.</summary>
        public string ErrorCode { get; private set; }

        /// <summary>Short note for the server's admins on a failure; may be null.</summary>
        public string Message { get; private set; }

        public bool IsFailure
        {
            get { return ErrorCode != null; }
        }

        /// <summary>Fill each part that was asked for; null for a part that wasn't.</summary>
        public static LookupResult Answer(PlayerProfile profile, BanInfo bans)
        {
            return new LookupResult(profile, bans, null, null);
        }

        public static LookupResult Failure(string error, string message = null)
        {
            return new LookupResult(null, null, error ?? Error, message);
        }
    }
}
