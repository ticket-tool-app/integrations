using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace TicketTool.PluginSdk
{
    /// <summary>The default sender, on System.Net.Http.HttpClient.</summary>
    public sealed class HttpClientSender : IHttpSender
    {
        private readonly HttpClient _http;

        public HttpClientSender()
        {
#if NET8_0_OR_GREATER
            var handler = new SocketsHttpHandler
            {
                ConnectTimeout = TimeSpan.FromSeconds(10),
                // DNS changes on the API host are picked up without a restart.
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                AllowAutoRedirect = false,
            };
#else
            var handler = new HttpClientHandler { AllowAutoRedirect = false };
#endif
            _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        }

        public async Task<HttpReply> PostAsync(Uri url, byte[] body, IDictionary<string, string> headers,
            TimeSpan timeout, CancellationToken cancellation)
        {
            using (var timeoutCts = new CancellationTokenSource(timeout))
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellation))
            using (var request = new HttpRequestMessage(HttpMethod.Post, url))
            {
                var content = new ByteArrayContent(body);
                foreach (var h in headers)
                {
                    if (string.Equals(h.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                        content.Headers.TryAddWithoutValidation(h.Key, h.Value);
                    else
                        request.Headers.TryAddWithoutValidation(h.Key, h.Value);
                }
                request.Content = content;
                try
                {
                    using (var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                               linked.Token).ConfigureAwait(false))
                    {
                        byte[] raw;
                        using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        {
                            raw = await HttpLimits.ReadCappedAsync(stream, linked.Token).ConfigureAwait(false);
                        }
                        var replyHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var h in response.Headers) replyHeaders[h.Key] = h.Value.FirstOrDefault();
                        return new HttpReply((int)response.StatusCode, raw, replyHeaders);
                    }
                }
                catch (OperationCanceledException) when (!cancellation.IsCancellationRequested
                                                         && timeoutCts.IsCancellationRequested)
                {
                    throw new TimeoutException("No reply within " + (int)timeout.TotalSeconds + " s");
                }
            }
        }

        public void Dispose()
        {
            _http.Dispose();
        }
    }
}
