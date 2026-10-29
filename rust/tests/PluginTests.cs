using System.Reflection;
using TicketTool.PluginSdk.Tests;

namespace TicketToolLink.Rust.Tests;

public sealed class PluginTests : IDisposable
{
    private const string Online = "76561198000000000";
    private const string Banned = "76561198000000001";
    private const string Stranger = "76561198000000002";
    private readonly FakeTicketTool _server = new();

    public PluginTests()
    {
        StubRuntime.StartMainThread();
        StubRuntime.ConfigJson = Newtonsoft.Json.JsonConvert.SerializeObject(new Dictionary<string, object>
        {
            ["key"] = FakeTicketTool.Key, ["api-url"] = _server.Url, ["server-name"] = "main", ["debug"] = true,
        });
        StubRuntime.AllPlayers.Clear();
        BasePlayer.activePlayerList.Clear();
        var josh = new BasePlayer { displayName = "Josh", userIdString = Online, connected = true };
        StubRuntime.AllPlayers.Add(josh);
        BasePlayer.activePlayerList.Add(josh);
        StubRuntime.Players.Known[Banned] = new KnownPlayer(Banned, "Griefer");
        ServerUsers.users[ulong.Parse(Banned)] = new ServerUsers.User
        {
            steamid = ulong.Parse(Banned), group = ServerUsers.UserGroup.Banned,
            notes = "<color=red>Cheating</color>", expiry = 0,
        };
        ServerUsers.users[ulong.Parse(Online)] = new ServerUsers.User
            { steamid = ulong.Parse(Online), group = ServerUsers.UserGroup.Moderator };
    }

    public void Dispose() => _server.Dispose();

    private static void Hook(object plugin, string name, params object[] args) =>
        StubRuntime.RunOnMain(() => plugin.GetType()
            .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!
            .Invoke(plugin, args));

    private Dictionary<string, object> Answer(string id, string steamId)
    {
        _server.QueueLookup(FakeTicketTool.Lookup(id, steamId, 5000, "profile", "bans"));
        Assert.True(_server.Responses.TryTake(out var r, 10000), "no answer for " + id);
        return r!.Body;
    }

    [Fact]
    public async Task AnswersLookupsFromGameStateOnTheMainThread()
    {
        var plugin = new Oxide.Plugins.TicketToolLink();
        Hook(plugin, "Init");
        Hook(plugin, "LoadConfig");
        Hook(plugin, "OnServerInitialized");

        var until = DateTime.UtcNow.AddSeconds(10);
        while (_server.Calls("/poll").Count == 0 && DateTime.UtcNow < until) await Task.Delay(20);
        var hello = _server.Calls("/hello").Single().Body;
        var info = (Dictionary<string, object>)hello["plugin"];
        Assert.Equal("rust", info["type"]);
        Assert.Contains("Rust 2590", (string)info["platform"]);
        Assert.Equal(new List<object> { "server" }, hello["banProviders"]);

        var online = Answer("a", Online);
        var profile = (Dictionary<string, object>)online["profile"];
        Assert.Equal(Online, profile["steamId"]);
        Assert.Equal("Josh", profile["name"]);
        Assert.Equal(true, profile["online"]);
        Assert.Equal("main", profile["server"]);
        Assert.NotNull(profile["firstSeen"]);
        var role = (Dictionary<string, object>)((List<object>)profile["fields"]).Single();
        Assert.Equal("Moderator", role["value"]);
        Assert.Equal(false, ((Dictionary<string, object>)online["bans"])["banned"]);

        var banned = Answer("b", Banned);
        Assert.Equal("Griefer", ((Dictionary<string, object>)banned["profile"])["name"]);
        Assert.Equal(false, ((Dictionary<string, object>)banned["profile"])["online"]);
        var bans = (Dictionary<string, object>)banned["bans"];
        Assert.Equal(true, bans["banned"]);
        Assert.Equal("server", bans["provider"]);
        Assert.Equal("Cheating", bans["reason"]);
        Assert.Null(bans["expiresAt"]);

        var stranger = Answer("c", Stranger);
        Assert.Equal(false, stranger["ok"]);
        Assert.Equal("unknown_player", stranger["error"]);

        Assert.False(StubRuntime.OffMainThreadAccess, "game state was read off the main thread");

        var console = new StubRuntime.Console();
        Hook(plugin, "CmdTicketLink", console, "ticketlink", new[] { "status" });
        string status = string.Join("\n", console.Replies);
        Assert.Contains("Connected to Ticket Tool.", status);
        Assert.Contains("ttp_0123456789abcdef", status);

        Hook(plugin, "Unload");
        Assert.Single(_server.Calls("/goodbye"));
        Assert.Contains(Online, StubRuntime.Data["TicketToolLink_seen"]);

        string secret = FakeTicketTool.Key.Split('.')[1];
        Assert.DoesNotContain(StubRuntime.Lines, l => l.Contains(secret));
        Assert.DoesNotContain(secret, status);
    }

    private async Task<object> Started(string extraConfig)
    {
        var config = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(StubRuntime.ConfigJson)!;
        foreach (var kv in Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(extraConfig)!)
            config[kv.Key] = kv.Value;
        StubRuntime.ConfigJson = Newtonsoft.Json.JsonConvert.SerializeObject(config);
        var plugin = new Oxide.Plugins.TicketToolLink();
        Hook(plugin, "LoadConfig");
        Hook(plugin, "Init");
        Hook(plugin, "OnServerInitialized");
        var until = DateTime.UtcNow.AddSeconds(10);
        while (_server.Calls("/poll").Count == 0 && DateTime.UtcNow < until) await Task.Delay(20);
        return plugin;
    }

    private Dictionary<string, object> Act(string id, string steamId, string kind, string? message)
    {
        _server.QueueLookup(FakeTicketTool.Action(id, steamId, 5000, kind, message));
        Assert.True(_server.Responses.TryTake(out var r, 10000), "no answer for " + id);
        return r!.Body;
    }

    [Fact]
    public async Task CarriesOutOnlyTheActionsTurnedOn()
    {
        var plugin = await Started("{\"actions\":[\"kick\",\"message\",\"unban\",\"slay\"]}");
        var hello = _server.Calls("/hello").Single().Body;
        Assert.Equal(new List<object> { "message", "kick", "unban" }, hello["actions"]);
        Assert.False(hello.ContainsKey("events"));
        Assert.Contains(StubRuntime.Lines, l => l.Contains("\"slay\" isn't one of"));

        Assert.Equal(true, Act("m", Online, "message", "<b>Hi</b>")["ok"]);
        Assert.Contains(StubRuntime.Chat, l => l == Online + ": <color=#5865f2>[Staff] mod.alex:</color> Hi");
        Assert.Equal("not_allowed", Act("w", Online, "warn", "Stop")["error"]);
        Assert.Equal("player_offline", Act("k1", Stranger, "kick", "Bye")["error"]);
        Assert.Equal("not_banned", Act("u1", Online, "unban", null)["error"]);
        Assert.Equal(true, Act("u2", Banned, "unban", null)["ok"]);
        Assert.False(ServerUsers.users.ContainsKey(ulong.Parse(Banned)));
        Assert.Equal(true, Act("k2", Online, "kick", "Griefing")["ok"]);
        Assert.Contains(StubRuntime.Chat, l => l == Online + " kicked: Kicked by staff: Griefing");
        Assert.False(StubRuntime.OffMainThreadAccess, "game state was touched off the main thread");
        Hook(plugin, "Unload");
    }

    [Fact]
    public async Task SendsReportsFromTheChatCommand()
    {
        var plugin = await Started("{\"reports\":true}");
        Assert.Equal(new List<object> { "player.report" }, _server.Calls("/hello").Single().Body["events"]);
        var reporter = new ChatPlayer(Online, "Josh");
        Hook(plugin, "CmdReport", reporter, "report", new[] { "josh", "is", "flying" });
        var until = DateTime.UtcNow.AddSeconds(10);
        while (reporter.Replies.Count < 2 && DateTime.UtcNow < until) await Task.Delay(20);
        var body = _server.Calls("/event").Single().Body;
        Assert.Equal("is flying", body["message"]);
        Assert.Equal(Online, ((Dictionary<string, object>)body["player"])["steamId"]);
        Assert.Equal("Josh", ((Dictionary<string, object>)body["target"])["name"]);
        Assert.Equal("main", body["server"]);
        Assert.Contains("reached the staff team", reporter.Replies.Last());

        Hook(plugin, "CmdReport", reporter, "report", new[] { "again" });
        Assert.Contains("You just sent a report", reporter.Replies.Last());
        Assert.Single(_server.Calls("/event"));
        Hook(plugin, "Unload");
    }

    private sealed class ChatPlayer(string id, string name) : Oxide.Core.Libraries.Covalence.IPlayer
    {
        public readonly List<string> Replies = new();
        public string Id => id;
        public string Name => name;
        public bool IsConnected => true;
        public bool IsServer => false;
        public bool IsAdmin => false;
        public bool HasPermission(string perm) => false;
        public void Reply(string message) { lock (Replies) Replies.Add(message); }
    }

    private sealed class KnownPlayer(string id, string name) : Oxide.Core.Libraries.Covalence.IPlayer
    {
        public string Id => id;
        public string Name => name;
        public bool IsConnected => false;
        public bool IsServer => false;
        public bool IsAdmin => false;
        public bool HasPermission(string perm) => false;
        public void Reply(string message) { }
    }
}
