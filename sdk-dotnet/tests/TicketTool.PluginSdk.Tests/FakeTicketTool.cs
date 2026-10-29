using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace TicketTool.PluginSdk.Tests;

/// <summary>
/// A protocol v1 server for tests: checks signatures, the clock window and
/// nonce reuse the way Ticket Tool does, signs its replies, and lets a test
/// script what each endpoint returns.
/// </summary>
internal sealed class FakeTicketTool : IDisposable
{
    public const string Key = "ttp_0123456789abcdef.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    public sealed record Received(string Endpoint, Dictionary<string, object> Body, string Nonce, long Timestamp);

    /// <summary>What a scripted handler returns; Signed=false mimics pre-auth errors.</summary>
    public sealed record Canned(int Status, Dictionary<string, object> Body, bool Signed)
    {
        public static Canned Ok(Dictionary<string, object> body) => new(200, body, true);

        public static Canned Error(int status, string error, bool signed = true) =>
            new(status, new Dictionary<string, object> { ["ok"] = false, ["error"] = error }, signed);
    }

    private readonly HttpListener _listener = new();
    private readonly Signer _signer = new(Signer.DeriveSigningSecret(Key));
    private readonly ConcurrentDictionary<string, bool> _nonces = new();
    private readonly ConcurrentDictionary<string, Func<Received, Canned>> _overrides = new();
    private readonly BlockingCollection<Dictionary<string, object?>> _pollQueue = new();
    private readonly CancellationTokenSource _stop = new();

    public readonly ConcurrentQueue<Received> ReceivedAll = new();
    public readonly BlockingCollection<Received> Responses = new();
    public long ServerClockOffset;
    public volatile int PollWaitMillis = 200;
    public volatile bool TamperReplies;
    public volatile string PluginType = "rust";

    public FakeTicketTool()
    {
        int port = FreePort();
        Url = $"http://127.0.0.1:{port}";
        _listener.Prefixes.Add(Url + "/api/plugin/v1/");
        _listener.Start();
        _ = Task.Run(AcceptLoop);
    }

    public string Url { get; }

    public long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds() + Interlocked.Read(ref ServerClockOffset);

    public void Override(string endpoint, Func<Received, Canned> fn) => _overrides[endpoint] = fn;

    public void ClearOverride(string endpoint) => _overrides.TryRemove(endpoint, out _);

    public List<Received> Calls(string endpoint) => ReceivedAll.Where(r => r.Endpoint == endpoint).ToList();

    public void QueueLookup(Dictionary<string, object?> request) => _pollQueue.Add(request);

    public static Dictionary<string, object?> Lookup(string id, string steamId, int expiresInMillis,
        params string[] include) => new()
    {
        ["id"] = id,
        ["type"] = "player.lookup",
        ["expiresAt"] = DateTimeOffset.UtcNow.AddMilliseconds(expiresInMillis).ToString("o"),
        ["player"] = new Dictionary<string, object?>
            { ["uuid"] = null, ["steamId"] = steamId, ["discordId"] = null, ["name"] = "Josh" },
        ["include"] = include.Cast<object>().ToList(),
    };

    public static Dictionary<string, object?> Action(string id, string steamId, int expiresInMillis, string kind,
        string? message) => new()
    {
        ["id"] = id,
        ["type"] = "player.action",
        ["expiresAt"] = DateTimeOffset.UtcNow.AddMilliseconds(expiresInMillis).ToString("o"),
        ["player"] = new Dictionary<string, object?>
            { ["uuid"] = null, ["steamId"] = steamId, ["discordId"] = null, ["name"] = "Josh" },
        ["action"] = new Dictionary<string, object?> { ["kind"] = kind, ["message"] = message },
        ["actor"] = new Dictionary<string, object?> { ["name"] = "mod.alex" },
    };

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private async Task AcceptLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync();
            }
            catch (Exception)
            {
                return;
            }
            _ = Task.Run(() => Handle(ctx));
        }
    }

    private void Handle(HttpListenerContext ctx)
    {
        try
        {
            string endpoint = ctx.Request.Url!.AbsolutePath["/api/plugin/v1".Length..];
            byte[] body;
            using (var ms = new MemoryStream())
            {
                ctx.Request.InputStream.CopyTo(ms);
                body = ms.ToArray();
            }
            string? keyId = ctx.Request.Headers["X-TT-Key-Id"];
            string? tsHeader = ctx.Request.Headers["X-TT-Timestamp"];
            string? nonce = ctx.Request.Headers["X-TT-Nonce"];
            string? sig = ctx.Request.Headers["X-TT-Signature"];

            if (keyId != "0123456789abcdef")
            {
                Reply(ctx, endpoint, nonce, Canned.Error(401, "invalid_key", signed: false));
                return;
            }
            if (tsHeader == null || nonce == null || sig == null)
            {
                Reply(ctx, endpoint, nonce, Canned.Error(401, "missing_headers", signed: false));
                return;
            }
            if (!Regex.IsMatch(nonce, "^[A-Za-z0-9_-]{16,64}$"))
            {
                Reply(ctx, endpoint, nonce, Canned.Error(401, "bad_nonce", signed: false));
                return;
            }
            long ts = long.Parse(tsHeader);
            if (Math.Abs(Now() - ts) > 300)
            {
                Reply(ctx, endpoint, nonce, new Canned(401, new Dictionary<string, object>
                    { ["ok"] = false, ["error"] = "stale_timestamp", ["serverTime"] = Now() }, true));
                return;
            }
            if (_signer.SignRequest("POST", endpoint, ts, nonce, body) != sig)
            {
                Reply(ctx, endpoint, nonce, Canned.Error(401, "bad_signature", signed: false));
                return;
            }
            if (!_nonces.TryAdd(nonce, true))
            {
                Reply(ctx, endpoint, nonce, Canned.Error(401, "replayed", signed: false));
                return;
            }
            var parsed = Json.ParseObject(Encoding.UTF8.GetString(body));
            var r = new Received(endpoint, parsed, nonce, ts);
            ReceivedAll.Enqueue(r);

            if (_overrides.TryGetValue(endpoint, out var fn))
            {
                Reply(ctx, endpoint, nonce, fn(r));
                return;
            }
            Reply(ctx, endpoint, nonce, endpoint switch
            {
                "/hello" => Canned.Ok(new Dictionary<string, object>
                {
                    ["ok"] = true, ["protocol"] = 1, ["pluginType"] = PluginType,
                    ["key"] = new Dictionary<string, object> { ["name"] = "Main" },
                    ["server"] = new Dictionary<string, object>
                        { ["id"] = "123456789012345678", ["name"] = "My Discord server" },
                    ["poll"] = new Dictionary<string, object> { ["waitSeconds"] = 25 },
                }),
                "/poll" => Poll(),
                "/respond" => Respond(r),
                "/event" => Canned.Ok(new Dictionary<string, object> { ["ok"] = true, ["result"] = "opened" }),
                "/goodbye" => Canned.Ok(new Dictionary<string, object> { ["ok"] = true }),
                _ => Canned.Error(404, "not_found", signed: false),
            });
        }
        catch (Exception)
        {
            try { ctx.Response.Abort(); } catch (Exception) { }
        }
    }

    private Canned Poll()
    {
        var requests = new List<object>();
        if (_pollQueue.TryTake(out var first, PollWaitMillis))
        {
            requests.Add(first);
            while (requests.Count < 20 && _pollQueue.TryTake(out var more)) requests.Add(more);
        }
        return Canned.Ok(new Dictionary<string, object> { ["ok"] = true, ["requests"] = requests });
    }

    private Canned Respond(Received r)
    {
        Responses.Add(r);
        return Canned.Ok(new Dictionary<string, object> { ["ok"] = true });
    }

    private void Reply(HttpListenerContext ctx, string endpoint, string? nonce, Canned canned)
    {
        byte[] output = Encoding.UTF8.GetBytes(Json.Write(canned.Body));
        ctx.Response.StatusCode = canned.Status;
        ctx.Response.ContentType = "application/json";
        if (canned.Signed && nonce != null)
        {
            long ts = Now();
            string signature = _signer.SignReply(endpoint, ts, nonce, output);
            if (TamperReplies) signature = signature[..^1] + (signature.EndsWith('0') ? "1" : "0");
            ctx.Response.Headers["X-TT-Timestamp"] = ts.ToString();
            ctx.Response.Headers["X-TT-Nonce"] = nonce;
            ctx.Response.Headers["X-TT-Signature"] = signature;
        }
        ctx.Response.ContentLength64 = output.Length;
        ctx.Response.OutputStream.Write(output);
        ctx.Response.Close();
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _listener.Stop(); } catch (Exception) { }
        _listener.Close();
    }
}
