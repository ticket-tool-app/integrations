using System.Collections.Concurrent;

namespace TicketTool.PluginSdk.Tests;

public sealed class ClientTests : IDisposable
{
    private const string SteamId = "76561198000000000";
    private readonly FakeTicketTool _server = new();
    private readonly ConcurrentQueue<TimeSpan> _waits = new();
    private readonly List<TicketToolClient> _clients = new();

    public void Dispose()
    {
        foreach (var c in _clients) c.Stop();
        _server.Dispose();
    }

    public static IEnumerable<object[]> Senders => new[] { new object[] { "httpclient" }, new object[] { "webrequest" } };

    private TicketToolClient Client(LookupHandler? handler = null, string sender = "httpclient",
        TimeSpan? budget = null, string type = "rust", ActionHandler? actions = null, string[]? enabled = null,
        bool reports = false)
    {
        var client = new TicketToolClient(new TicketToolClientOptions
        {
            Key = FakeTicketTool.Key,
            ApiUrl = _server.Url,
            Plugin = new PluginInfo(type, "ticket-tool-test", "1.0.0", "Test"),
            BanProviders = () => new[] { "server" },
            Handler = handler ?? ((r, _) => Task.FromResult(LookupResult.Answer(
                new PlayerProfile { Name = "Josh", Online = true }, BanInfo.NotBanned("server")))),
            AnswerBudget = budget ?? TimeSpan.FromSeconds(3),
            ActionHandler = actions,
            Actions = () => enabled ?? Array.Empty<string>(),
            ReportsEnabled = () => reports,
            Sender = sender == "webrequest" ? new WebRequestSender() : new HttpClientSender(),
            // Waits are recorded, not slept, so backoff tests run fast.
            Delay = (wait, token) =>
            {
                _waits.Enqueue(wait);
                return Task.Delay(TimeSpan.FromMilliseconds(Math.Min(20, wait.TotalMilliseconds)), token);
            },
        });
        _clients.Add(client);
        return client;
    }

    private static async Task Await(Func<bool> condition, string what)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > until) throw new TimeoutException("Timed out waiting for " + what);
            await Task.Delay(10);
        }
    }

    private FakeTicketTool.Received NextResponse()
    {
        Assert.True(_server.Responses.TryTake(out var r, 10000), "no /respond arrived");
        return r!;
    }

    [Theory]
    [MemberData(nameof(Senders))]
    public async Task HelloPollRespondRoundTrip(string sender)
    {
        var client = Client((r, _) =>
        {
            var p = new PlayerProfile { Name = "Josh", Online = true, Server = "main" }.AddField("Playtime", "12h 30m");
            return Task.FromResult(LookupResult.Answer(p, new BanInfo(true, "server", "Cheating", null, null, null, null)));
        }, sender);
        client.Start();
        await Await(() => client.Status.State == ClientState.Connected, "connected");

        var hello = _server.Calls("/hello").Single().Body;
        Assert.Equal(1L, hello["protocol"]);
        Assert.Equal("rust", ((Dictionary<string, object>)hello["plugin"])["type"]);
        Assert.Equal(new List<object> { "server" }, hello["banProviders"]);

        _server.QueueLookup(FakeTicketTool.Lookup("req-1", SteamId, 5000, "profile", "bans"));
        var resp = NextResponse().Body;
        Assert.Equal("req-1", resp["requestId"]);
        Assert.Equal(true, resp["ok"]);
        var profile = (Dictionary<string, object>)resp["profile"];
        Assert.Equal(SteamId, profile["steamId"]);
        Assert.Equal("Josh", profile["name"]);
        Assert.Equal("main", profile["server"]);
        Assert.Single((List<object>)profile["fields"]);
        var bans = (Dictionary<string, object>)resp["bans"];
        Assert.Equal(true, bans["banned"]);
        Assert.Equal("Cheating", bans["reason"]);
        await Await(() => client.Status.Answered == 1, "answered count");
        Assert.Equal("Main", client.Status.Hello!.KeyName);
    }

    [Fact]
    public void SendsOnlyThePartsAskedFor()
    {
        Client().Start();
        _server.QueueLookup(FakeTicketTool.Lookup("req-1", SteamId, 5000, "bans"));
        var resp = NextResponse().Body;
        Assert.False(resp.ContainsKey("profile"));
        Assert.True(resp.ContainsKey("bans"));
    }

    [Fact]
    public void SkipsExpiredAndUnknownRequests()
    {
        int handled = 0;
        Client((r, _) =>
        {
            Interlocked.Increment(ref handled);
            return Task.FromResult(LookupResult.Answer(new PlayerProfile { Name = "Josh" }, null));
        }).Start();
        _server.QueueLookup(FakeTicketTool.Lookup("expired", SteamId, -1000, "profile"));
        var other = FakeTicketTool.Lookup("other-type", SteamId, 5000, "profile");
        other["type"] = "server.stats";
        _server.QueueLookup(other);
        _server.QueueLookup(FakeTicketTool.Lookup("unknown-part", SteamId, 5000, "inventory"));
        _server.QueueLookup(FakeTicketTool.Lookup("no-steam", "not-a-steam-id", 5000, "profile"));
        _server.QueueLookup(FakeTicketTool.Lookup("good", SteamId, 5000, "profile"));

        var got = new Dictionary<string, Dictionary<string, object>>();
        for (int i = 0; i < 3; i++)
        {
            var r = NextResponse().Body;
            got[(string)r["requestId"]] = r;
        }
        Assert.Equal("unsupported", got["unknown-part"]["error"]);
        Assert.Equal("unsupported", got["no-steam"]["error"]);
        Assert.Equal(true, got["good"]["ok"]);
        Assert.False(_server.Responses.TryTake(out _, 300));
        Assert.Equal(1, handled);
    }

    [Fact]
    public void ReportsASlowHandlerAsAnErrorBeforeExpiry()
    {
        Client(async (r, ct) =>
        {
            await Task.Delay(5000, ct);
            return LookupResult.Answer(null, null);
        }, budget: TimeSpan.FromMilliseconds(300)).Start();
        _server.QueueLookup(FakeTicketTool.Lookup("slow", SteamId, 4000, "profile"));
        var resp = NextResponse().Body;
        Assert.Equal(false, resp["ok"]);
        Assert.Equal("error", resp["error"]);
        Assert.Contains("too long", (string)resp["message"]);
    }

    [Fact]
    public void ReportsAThrowingHandlerAsAnError()
    {
        Client((r, _) => throw new InvalidOperationException("boom")).Start();
        _server.QueueLookup(FakeTicketTool.Lookup("throws", SteamId, 4000, "profile"));
        var resp = NextResponse().Body;
        Assert.Equal("error", resp["error"]);
        Assert.DoesNotContain("boom", (string)resp["message"]);
    }

    [Theory]
    [MemberData(nameof(Senders))]
    public async Task CorrectsClockSkewFromServerTime(string sender)
    {
        _server.ServerClockOffset = 1000;
        var client = Client(sender: sender);
        client.Start();
        await Await(() => client.Status.State == ClientState.Connected, "connected");
        Assert.InRange(client.Status.ClockOffsetSeconds, 998, 1002);
        // Requests carry the server's clock, so a lookup's expiry is judged correctly.
        var lookup = FakeTicketTool.Lookup("skewed", SteamId, 5000, "profile");
        lookup["expiresAt"] = DateTimeOffset.UtcNow.AddSeconds(1005).ToString("o");
        _server.QueueLookup(lookup);
        Assert.Equal(true, NextResponse().Body["ok"]);
    }

    [Fact]
    public async Task RetriesStaleTimestampOnlyOnce()
    {
        _server.Override("/hello", _ => new FakeTicketTool.Canned(401, new Dictionary<string, object>
            { ["ok"] = false, ["error"] = "stale_timestamp", ["serverTime"] = _server.Now() }, true));
        var client = Client();
        client.Start();
        await Await(() => client.Status.State == ClientState.Reconnecting, "reconnecting");
        await Await(() => _waits.Count >= 1, "a backoff");
        Assert.Contains("stale_timestamp", client.Status.LastError);
        client.Stop();
        // Each failed attempt is one send plus one retry after the correction, never more.
        int hellos = _server.Calls("/hello").Count, waits = _waits.Count;
        Assert.True(hellos == 2 * waits || hellos == 2 * (waits + 1), $"{hellos} hellos for {waits} waits");
    }

    [Fact]
    public async Task BacksOffOnServerErrorsAndResetsAfterSuccess()
    {
        int failures = 0;
        _server.Override("/poll", _ =>
            Interlocked.Increment(ref failures) <= 3
                ? FakeTicketTool.Canned.Error(503, "unavailable")
                : FakeTicketTool.Canned.Ok(new Dictionary<string, object> { ["ok"] = true, ["requests"] = new List<object>() }));
        var client = Client();
        client.Start();
        await Await(() => failures > 5, "recovery");
        var waits = _waits.Take(3).Select(w => w.TotalMilliseconds).ToArray();
        Assert.InRange(waits[0], 500, 1000);
        Assert.InRange(waits[1], 1000, 2000);
        Assert.InRange(waits[2], 2000, 4000);
        Assert.Equal(ClientState.Connected, client.Status.State);
    }

    [Fact]
    public async Task WaitsOutPollLeaseErrors()
    {
        _server.Override("/poll", _ => FakeTicketTool.Canned.Error(429, "too_many_polls"));
        var client = Client();
        client.Start();
        await Await(() => _waits.Count >= 1, "a wait");
        Assert.True(_waits.First() >= TimeSpan.FromSeconds(15));
        Assert.Contains("Three polls", client.Status.LastError);
    }

    [Fact]
    public async Task WaitsAMinuteWhenRateLimited()
    {
        _server.Override("/poll", _ => FakeTicketTool.Canned.Error(429, "rate_limited", signed: false));
        Client().Start();
        await Await(() => _waits.Count >= 1, "a wait");
        Assert.True(_waits.First() >= TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task StopsForGoodOnInvalidKey()
    {
        _server.Override("/hello", _ => FakeTicketTool.Canned.Error(401, "invalid_key", signed: false));
        var client = Client();
        client.Start();
        await Await(() => client.Status.State == ClientState.StoppedInvalidKey, "stopped");
        await Task.Delay(200);
        Assert.Single(_server.Calls("/hello"));
        Assert.Contains("ttp_0123456789abcdef", client.Status.LastError);
        Assert.DoesNotContain("AAAA", client.Status.LastError);
        client.Stop();
        Assert.Empty(_server.Calls("/goodbye"));
    }

    [Fact]
    public async Task StopsOnWrongPluginType()
    {
        _server.Override("/hello", _ => new FakeTicketTool.Canned(400, new Dictionary<string, object>
            { ["ok"] = false, ["error"] = "wrong_plugin_type", ["expected"] = "gmod" }, true));
        var client = Client();
        client.Start();
        await Await(() => client.Status.State == ClientState.StoppedError, "stopped");
        Assert.Contains("gmod", client.Status.LastError);
    }

    [Fact]
    public async Task PausesHourlyWhenThePlanLapses()
    {
        int calls = 0;
        _server.Override("/poll", _ => Interlocked.Increment(ref calls) == 1
            ? FakeTicketTool.Canned.Error(403, "plan_not_entitled")
            : FakeTicketTool.Canned.Ok(new Dictionary<string, object> { ["ok"] = true, ["requests"] = new List<object>() }));
        var client = Client();
        client.Start();
        await Await(() => _server.Calls("/hello").Count >= 2, "a second hello");
        Assert.Equal(TimeSpan.FromHours(1), _waits.First());
    }

    [Theory]
    [MemberData(nameof(Senders))]
    public async Task RefusesUnsignedAndTamperedSuccessReplies(string sender)
    {
        _server.Override("/hello", _ => new FakeTicketTool.Canned(200, new Dictionary<string, object> { ["ok"] = true }, false));
        var client = Client(sender: sender);
        client.Start();
        await Await(() => client.Status.LastError != null, "an error");
        Assert.Contains("Unsigned", client.Status.LastError);
        Assert.NotEqual(ClientState.Connected, client.Status.State);

        _server.ClearOverride("/hello");
        _server.TamperReplies = true;
        await Await(() => client.Status.LastError!.Contains("signature"), "a signature error");
        Assert.NotEqual(ClientState.Connected, client.Status.State);
        _server.TamperReplies = false;
        await Await(() => client.Status.State == ClientState.Connected, "connected");
    }

    [Fact]
    public async Task UsesAFreshNoncePerRequestIncludingRetries()
    {
        _server.ServerClockOffset = 1000;
        var client = Client();
        client.Start();
        await Await(() => _server.Calls("/poll").Count >= 3, "polls");
        var nonces = _server.ReceivedAll.Select(r => r.Nonce).ToList();
        Assert.Equal(nonces.Count, nonces.Distinct().Count());
        Assert.All(nonces, n => Assert.Equal(22, n.Length));
    }

    [Theory]
    [MemberData(nameof(Senders))]
    public async Task SendsGoodbyeOnStop(string sender)
    {
        var client = Client(sender: sender);
        client.Start();
        await Await(() => client.Status.State == ClientState.Connected, "connected");
        var started = DateTime.UtcNow;
        client.Stop();
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(4));
        Assert.Single(_server.Calls("/goodbye"));
        Assert.Equal(ClientState.Stopped, client.Status.State);
        Assert.Throws<InvalidOperationException>(() => client.Start());
    }

    [Fact]
    public async Task IgnoresAnUnknownRequestReply()
    {
        _server.Override("/respond", _ => FakeTicketTool.Canned.Error(410, "unknown_request"));
        var client = Client();
        client.Start();
        _server.QueueLookup(FakeTicketTool.Lookup("gone", SteamId, 5000, "profile"));
        await Await(() => _server.Calls("/respond").Count == 1, "respond");
        await Task.Delay(100);
        Assert.Equal(ClientState.Connected, client.Status.State);
        Assert.Equal(0, client.Status.Answered);
    }

    [Fact]
    public async Task HelloListsOnlyEnabledActionsAndReports()
    {
        var client = Client(actions: (r, _) => Task.FromResult(ActionResult.Done()),
            enabled: new[] { "kick", "message", "ban" }, reports: true);
        client.Start();
        await Await(() => client.Status.State == ClientState.Connected, "connected");
        var hello = _server.Calls("/hello").Single().Body;
        Assert.Equal(new List<object> { "message", "kick" }, hello["actions"]);
        Assert.Equal(new List<object> { "player.report" }, hello["events"]);
    }

    [Fact]
    public async Task HelloLeavesActionsOutWithoutAHandler()
    {
        var client = Client(enabled: new[] { "kick" });
        client.Start();
        await Await(() => client.Status.State == ClientState.Connected, "connected");
        var hello = _server.Calls("/hello").Single().Body;
        Assert.False(hello.ContainsKey("actions"));
        Assert.False(hello.ContainsKey("events"));
        _server.QueueLookup(FakeTicketTool.Action("act-1", SteamId, 5000, "kick", "Bye"));
        var resp = NextResponse().Body;
        Assert.Equal(false, resp["ok"]);
        Assert.Equal("not_allowed", resp["error"]);
    }

    [Fact]
    public void CarriesOutAnEnabledAction()
    {
        ActionRequest? seen = null;
        Client(actions: (r, _) =>
        {
            seen = r;
            return Task.FromResult(ActionResult.Done());
        }, enabled: new[] { "warn" }).Start();
        _server.QueueLookup(FakeTicketTool.Action("act-1", SteamId, 5000, "warn", "Stop spawn camping"));
        var resp = NextResponse().Body;
        Assert.Equal("act-1", resp["requestId"]);
        Assert.Equal(true, resp["ok"]);
        Assert.Equal("warn", seen!.Kind);
        Assert.Equal("Stop spawn camping", seen.Text);
        Assert.Equal("mod.alex", seen.ActorName);
        Assert.Equal(SteamId, seen.Player.SteamId);
    }

    [Fact]
    public void RefusesADisabledActionWithoutCallingTheHandler()
    {
        int called = 0;
        Client(actions: (r, _) =>
        {
            Interlocked.Increment(ref called);
            return Task.FromResult(ActionResult.Done());
        }, enabled: new[] { "message" }).Start();
        _server.QueueLookup(FakeTicketTool.Action("act-1", SteamId, 5000, "kick", "Bye"));
        Assert.Equal("not_allowed", NextResponse().Body["error"]);
        Assert.Equal(0, called);
    }

    [Fact]
    public void PassesFailuresThroughAndSkipsUnknownKinds()
    {
        Client(actions: (r, _) => Task.FromResult(ActionResult.Failure(ActionResult.PlayerOffline, "Not on")),
            enabled: new[] { "message", "kick" }).Start();
        _server.QueueLookup(FakeTicketTool.Action("odd", SteamId, 5000, "slay", null));
        _server.QueueLookup(FakeTicketTool.Action("no-steam", "nope", 5000, "kick", "Bye"));
        _server.QueueLookup(FakeTicketTool.Action("act-1", SteamId, 5000, "message", "Hi"));
        var got = new Dictionary<string, Dictionary<string, object>>();
        for (int i = 0; i < 2; i++)
        {
            var r = NextResponse().Body;
            got[(string)r["requestId"]] = r;
        }
        Assert.Equal("unknown_player", got["no-steam"]["error"]);
        Assert.Equal("player_offline", got["act-1"]["error"]);
        Assert.Equal("Not on", got["act-1"]["message"]);
        Assert.False(_server.Responses.TryTake(out _, 300));
    }

    [Fact]
    public async Task SendsReportsOnlyWhenEnabled()
    {
        var off = Client();
        off.Start();
        var player = new RequestPlayer(null, SteamId, null, "Josh");
        var skipped = await off.ReportAsync(player, "Someone is flying");
        Assert.Equal(ReportOutcome.NotEnabled, skipped.Outcome);
        Assert.Empty(_server.Calls("/event"));

        var on = Client(reports: true);
        on.Start();
        var sent = await on.ReportAsync(player, "  Someone is flying  ",
            new RequestPlayer(null, null, null, "Flyer"), "main");
        Assert.True(sent.Success);
        var body = _server.Calls("/event").Single().Body;
        Assert.Equal("player.report", body["type"]);
        Assert.Equal("Someone is flying", body["message"]);
        Assert.Equal(SteamId, ((Dictionary<string, object>)body["player"])["steamId"]);
        Assert.Equal("Flyer", ((Dictionary<string, object>)body["target"])["name"]);
        Assert.Equal("main", body["server"]);

        _server.Override("/event", _ => FakeTicketTool.Canned.Error(429, "rate_limited"));
        Assert.Equal(ReportOutcome.RateLimited, (await on.ReportAsync(player, "Again")).Outcome);
    }
}
