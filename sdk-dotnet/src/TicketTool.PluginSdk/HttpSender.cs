using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TicketTool.PluginSdk
{
    public sealed class HttpReply
    {
        public HttpReply(int status, byte[] body, IDictionary<string, string> headers)
        {
            Status = status;
            Body = body ?? new byte[0];
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (headers != null)
                foreach (var h in headers) Headers[h.Key] = h.Value;
        }

        public int Status { get; private set; }
        public byte[] Body { get; private set; }
        public IDictionary<string, string> Headers { get; private set; }

        public string Header(string name)
        {
            string v;
            return Headers.TryGetValue(name, out v) ? v : null;
        }
    }

    /// <summary>
    /// Sends one POST with these exact body bytes and returns the raw reply. A
    /// non-2xx status is a reply, not an exception. Throws TimeoutException when
    /// the timeout passes, OperationCanceledException when the token fires.
    /// </summary>
    public interface IHttpSender : IDisposable
    {
        Task<HttpReply> PostAsync(Uri url, byte[] body, IDictionary<string, string> headers, TimeSpan timeout,
            CancellationToken cancellation);
    }

    internal static class HttpLimits
    {
        // Replies are small JSON; anything near this is not from Ticket Tool.
        internal const int MaxReplyBytes = 256 * 1024;

        internal static async Task<byte[]> ReadCappedAsync(Stream stream, CancellationToken cancellation)
        {
            var buffer = new MemoryStream();
            var chunk = new byte[8192];
            while (true)
            {
                int n = await stream.ReadAsync(chunk, 0, chunk.Length, cancellation).ConfigureAwait(false);
                if (n <= 0) break;
                buffer.Write(chunk, 0, n);
                if (buffer.Length > MaxReplyBytes)
                    throw new IOException("Reply larger than " + MaxReplyBytes + " bytes");
            }
            return buffer.ToArray();
        }
    }
}
