using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TicketTool.PluginSdk
{
    /// <summary>One signed request and its verified reply. Knows nothing about the poll loop.</summary>
    internal sealed class Transport
    {
        internal const int SignatureWindowSeconds = 300;

        private readonly IHttpSender _http;
        private readonly string _base;
        private readonly PluginKey _key;
        private readonly string _userAgent;
        private readonly Func<DateTimeOffset> _clock;
        private readonly RandomNumberGenerator _random = RandomNumberGenerator.Create();
        private readonly object _randomLock = new object();
        // serverTime - localTime, learned from a stale_timestamp error.
        private long _offsetSeconds;

        internal sealed class Reply
        {
            internal int Status;
            internal Dictionary<string, object> Body;
            internal bool Signed;
        }

        internal Transport(IHttpSender http, Uri apiBase, PluginKey key, string userAgent, Func<DateTimeOffset> clock)
        {
            _http = http;
            _base = apiBase.ToString().TrimEnd('/');
            _key = key;
            _userAgent = userAgent;
            _clock = clock;
        }

        internal long OffsetSeconds
        {
            get { return Interlocked.Read(ref _offsetSeconds); }
        }

        internal long NowSeconds()
        {
            return _clock().ToUnixTimeSeconds() + OffsetSeconds;
        }

        internal void CorrectClock(long serverTime)
        {
            Interlocked.Exchange(ref _offsetSeconds, serverTime - _clock().ToUnixTimeSeconds());
        }

        internal string NewNonce()
        {
            var bytes = new byte[16];
            lock (_randomLock) _random.GetBytes(bytes);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        /// <summary>
        /// Sends payload to endpoint (e.g. /poll). The body is serialized once and
        /// those exact bytes are both hashed and sent.
        /// </summary>
        internal async Task<Reply> SendAsync(string endpoint, object payload, TimeSpan timeout,
            CancellationToken cancellation)
        {
            byte[] body = Encoding.UTF8.GetBytes(Json.Write(payload ?? new Dictionary<string, object>()));
            long ts = NowSeconds();
            string nonce = NewNonce();
            string tsText = ts.ToString(CultureInfo.InvariantCulture);
            var headers = new Dictionary<string, string>
            {
                { "Content-Type", "application/json" },
                { "Accept", "application/json" },
                { "X-TT-Key-Id", _key.KeyId },
                { "X-TT-Timestamp", tsText },
                { "X-TT-Nonce", nonce },
                { "X-TT-Signature", _key.Signer.SignRequest("POST", endpoint, ts, nonce, body) },
                { "X-TT-Protocol", TicketToolClient.ProtocolVersion.ToString(CultureInfo.InvariantCulture) },
                { "X-TT-Plugin", _userAgent },
                { "User-Agent", _userAgent },
            };
            HttpReply raw = await _http.PostAsync(new Uri(_base + endpoint), body, headers, timeout, cancellation)
                .ConfigureAwait(false);
            bool ok2xx = raw.Status >= 200 && raw.Status < 300;
            string signature = raw.Header("X-TT-Signature");
            bool signed = signature != null;
            if (signed)
                Verify(endpoint, nonce, raw, signature, ok2xx);
            else if (ok2xx)
                // Only pre-auth errors may be unsigned; an unsigned success could be anyone's.
                throw new ReplyVerificationException("Unsigned success reply from " + endpoint);

            Dictionary<string, object> parsed;
            try
            {
                parsed = raw.Body.Length == 0
                    ? new Dictionary<string, object>()
                    : Json.ParseObject(Encoding.UTF8.GetString(raw.Body));
            }
            catch (JsonException e)
            {
                if (ok2xx) throw new System.IO.IOException("Reply from " + endpoint + " wasn't JSON", e);
                parsed = new Dictionary<string, object>();
            }
            return new Reply { Status = raw.Status, Body = parsed, Signed = signed };
        }

        /// <summary>
        /// SendAsync plus error mapping: returns the body of an ok reply, retries
        /// once after correcting the clock from a stale_timestamp error, and throws
        /// ApiException for anything else.
        /// </summary>
        internal async Task<Dictionary<string, object>> CallAsync(string endpoint, object payload, TimeSpan timeout,
            CancellationToken cancellation)
        {
            for (int attempt = 0;; attempt++)
            {
                Reply reply = await SendAsync(endpoint, payload, timeout, cancellation).ConfigureAwait(false);
                bool success = reply.Status >= 200 && reply.Status < 300
                               && JsonFields.Bool(reply.Body, "ok") != false;
                if (success) return reply.Body;
                var error = new ApiException(reply.Status, JsonFields.String(reply.Body, "error"), reply.Body);
                long? serverTime = JsonFields.Long(reply.Body, "serverTime");
                if (attempt == 0 && error.Error == "stale_timestamp" && serverTime.HasValue)
                {
                    CorrectClock(serverTime.Value);
                    continue;
                }
                throw error;
            }
        }

        private void Verify(string endpoint, string nonce, HttpReply raw, string signature, bool success)
        {
            if (raw.Header("X-TT-Nonce") != nonce)
                throw new ReplyVerificationException("Reply nonce doesn't match the request");
            long replyTs;
            if (!long.TryParse((raw.Header("X-TT-Timestamp") ?? "").Trim(), NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out replyTs))
                throw new ReplyVerificationException("Reply timestamp missing or malformed");
            // A signed stale_timestamp error carries the server's clock, which is the
            // very skew it reports. The echoed nonce already ties an error to this
            // request, so the window is enforced on successes only.
            if (success && Math.Abs(NowSeconds() - replyTs) > SignatureWindowSeconds)
                throw new ReplyVerificationException("Reply timestamp outside the signature window");
            if (!_key.Signer.VerifyReply(endpoint, replyTs, nonce, raw.Body, signature))
                throw new ReplyVerificationException("Reply signature doesn't match");
        }
    }
}
