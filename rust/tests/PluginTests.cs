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
