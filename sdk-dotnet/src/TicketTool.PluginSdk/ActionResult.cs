namespace TicketTool.PluginSdk
{
    /// <summary>What an ActionHandler returns for one ActionRequest: done or a failure code.</summary>
    public sealed class ActionResult
    {
        public const string PlayerOffline = "player_offline";
        public const string NotBanned = "not_banned";
        public const string NotAllowed = "not_allowed";
        public const string UnknownPlayer = "unknown_player";
        public const string Unsupported = "unsupported";
        public const string Error = "error";

        private ActionResult(string error, string message)
        {
            ErrorCode = error;
            Message = message == null ? null : Text.Clip(message, 200);
        }

        /// <summary>Null when the action was carried out; one of the constants on this class otherwise.</summary>
        public string ErrorCode { get; private set; }

        /// <summary>Short note for the server's admins on a failure; may be null.</summary>
        public string Message { get; private set; }

        public bool IsFailure
        {
            get { return ErrorCode != null; }
        }

        public static ActionResult Done()
        {
            return new ActionResult(null, null);
        }

        public static ActionResult Failure(string error, string message = null)
        {
            return new ActionResult(error ?? Error, message);
        }
    }
}
