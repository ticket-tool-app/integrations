using System;
using System.Collections.Generic;

namespace TicketTool.PluginSdk
{
    /// <summary>A reply with ok: false or a non-2xx status.</summary>
    public sealed class ApiException : Exception
    {
        public ApiException(int status, string error, Dictionary<string, object> body)
            : base("HTTP " + status + (error == null ? "" : " " + error))
        {
            Status = status;
            Error = error;
            Body = body ?? new Dictionary<string, object>();
        }

        public int Status { get; private set; }

        /// <summary>The protocol error code; null when the reply wasn't JSON, e.g. a proxy's HTML page.</summary>
        public string Error { get; private set; }

        public Dictionary<string, object> Body { get; private set; }
    }

    /// <summary>A reply that failed signature, nonce or timestamp checks, or an unsigned success.</summary>
    public sealed class ReplyVerificationException : Exception
    {
        public ReplyVerificationException(string message) : base(message) { }
    }
}
