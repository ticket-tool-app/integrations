using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

#pragma warning disable SYSLIB0014 // HttpWebRequest is obsolete on .NET 8 but is what Oxide can rely on.

namespace TicketTool.PluginSdk
{
    /// <summary>
    /// A sender on HttpWebRequest, which lives in System.dll. Oxide compiles
    /// plugins against a fixed reference set that may not include the HttpClient
    /// assembly, and its own web request library hides the reply headers the
    /// signature check needs.
    /// </summary>
    public sealed class WebRequestSender : IHttpSender
    {
        public async Task<HttpReply> PostAsync(Uri url, byte[] body, IDictionary<string, string> headers,
            TimeSpan timeout, CancellationToken cancellation)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.AllowAutoRedirect = false;
            request.KeepAlive = true;
            // The synchronous timeouts don't apply to the async calls; the abort below does.
            request.Timeout = (int)timeout.TotalMilliseconds;
            request.ReadWriteTimeout = (int)timeout.TotalMilliseconds;
            // Mono allows two connections per host by default: the long-poll holds one,
            // and answers sent while it waits would queue behind it.
            if (request.ServicePoint.ConnectionLimit < 8) request.ServicePoint.ConnectionLimit = 8;
            request.ServicePoint.Expect100Continue = false;
            foreach (var h in headers)
            {
                if (string.Equals(h.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                    request.ContentType = h.Value;
                else if (string.Equals(h.Key, "Accept", StringComparison.OrdinalIgnoreCase))
                    request.Accept = h.Value;
                else if (string.Equals(h.Key, "User-Agent", StringComparison.OrdinalIgnoreCase))
                    request.UserAgent = h.Value;
                else
                    request.Headers[h.Key] = h.Value;
            }
            request.ContentLength = body.Length;

            using (var timeoutCts = new CancellationTokenSource(timeout))
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellation))
            using (linked.Token.Register(() =>
                   {
                       try { request.Abort(); }
                       catch (Exception) { }
                   }))
            {
                try
                {
                    using (Stream s = await request.GetRequestStreamAsync().ConfigureAwait(false))
                    {
                        await s.WriteAsync(body, 0, body.Length, linked.Token).ConfigureAwait(false);
                    }
                    HttpWebResponse response;
                    try
                    {
                        response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false);
                    }
                    catch (WebException e) when (e.Response is HttpWebResponse)
                    {
                        response = (HttpWebResponse)e.Response;
                    }
                    using (response)
                    {
                        byte[] raw;
                        using (Stream s = response.GetResponseStream())
                        {
                            raw = s == null
                                ? new byte[0]
                                : await HttpLimits.ReadCappedAsync(s, linked.Token).ConfigureAwait(false);
                        }
                        var replyHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (string name in response.Headers.AllKeys) replyHeaders[name] = response.Headers[name];
                        return new HttpReply((int)response.StatusCode, raw, replyHeaders);
                    }
                }
                catch (Exception e) when (linked.IsCancellationRequested && !(e is TimeoutException))
                {
                    if (cancellation.IsCancellationRequested) throw new OperationCanceledException(cancellation);
                    throw new TimeoutException("No reply within " + (int)timeout.TotalSeconds + " s");
                }
            }
        }

        public void Dispose()
        {
        }
    }
}
