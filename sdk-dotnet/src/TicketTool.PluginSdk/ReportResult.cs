namespace TicketTool.PluginSdk
{
    public enum ReportOutcome
    {
        Opened,

        /// <summary>No automation rule on the Discord server took the report.</summary>
        Ignored,
        RateLimited,

        /// <summary>Reports are off in the plugin's config.</summary>
        NotEnabled,
        Failed,
    }

    /// <summary>The outcome of an in-game report sent with /event.</summary>
    public sealed class ReportResult
    {
        internal ReportResult(ReportOutcome outcome, string error)
        {
            Outcome = outcome;
            Error = error;
        }

        public ReportOutcome Outcome { get; private set; }

        /// <summary>The server's error code on failure, or a local one (network, not_running, not_enabled).</summary>
        public string Error { get; private set; }

        public bool Success
        {
            get { return Outcome == ReportOutcome.Opened; }
        }
    }
}
