namespace TicketTool.PluginSdk.Tests;

public class MessagesTests
{
    private static LookupRequest Request(string? steamId = "76561198000000000", params string[] include) =>
        new("r1", "player.lookup", DateTimeOffset.UtcNow.AddSeconds(5),
            new RequestPlayer(null, steamId, null, "Josh"), include.Length == 0 ? new[] { "profile", "bans" } : include);

    [Fact]
    public void ReadsSteamAndDiscordIdentitiesFromPolls()
    {
        var body = Json.ParseObject("{\"ok\":true,\"requests\":[{\"id\":\"a\",\"type\":\"player.lookup\","
            + "\"expiresAt\":\"2026-10-15T12:00:04.000Z\",\"player\":{\"uuid\":null,\"steamId\":\"76561198000000000\","
            + "\"discordId\":\"123\",\"name\":null},\"include\":[\"profile\",\"inventory\"],\"extra\":1},"
            + "{\"id\":\"b\"}]}");
        var list = Messages.PollRequests(body);
        var r = Assert.Single(list);
        Assert.Equal("76561198000000000", r.Player.SteamId);
        Assert.Equal("123", r.Player.DiscordId);
        Assert.Null(r.Player.Name);
        Assert.Equal(new[] { "profile" }, r.Include);
        Assert.Equal(new DateTimeOffset(2026, 10, 15, 12, 0, 4, TimeSpan.Zero), r.ExpiresAt);
    }

    [Fact]
    public void FillsTheSteamIdAndCapsFields()
    {
        var p = new PlayerProfile { Name = "<color=red>Josh</color>", Online = true,
            LastSeen = new DateTimeOffset(2026, 10, 15, 11, 58, 0, TimeSpan.Zero), Server = "main" };
        for (int i = 0; i < 8; i++) p.AddField("Label " + i + new string('x', 40), new string('v', 150));
        var body = Messages.Respond(Request(), LookupResult.Answer(p, null), "rust");
        var profile = (Dictionary<string, object>)body["profile"];
        Assert.Equal(true, body["ok"]);
        Assert.Equal("76561198000000000", profile["steamId"]);
        Assert.False(profile.ContainsKey("uuid"));
        Assert.Equal("Josh", profile["name"]);
        Assert.Equal("2026-10-15T11:58:00.000Z", profile["lastSeen"]);
        Assert.Null(profile["firstSeen"]);
        var fields = (List<object>)profile["fields"];
        Assert.Equal(6, fields.Count);
        var f = (Dictionary<string, object>)fields[0];
        Assert.Equal(32, ((string)f["label"]).Length);
        Assert.Equal(100, ((string)f["value"]).Length);
        Assert.False(body.ContainsKey("bans"));
    }

    [Fact]
    public void RefusesAProfileForADifferentPlayer()
    {
        var p = new PlayerProfile { SteamId = "76561198000000001", Name = "Other" };
        var body = Messages.Respond(Request(), LookupResult.Answer(p, null), "cs2");
        Assert.Equal(false, body["ok"]);
        Assert.Equal("error", body["error"]);
    }

    [Fact]
    public void FallsBackToTheRequestNameAndThenTheId()
    {
        var body = Messages.Respond(Request(), LookupResult.Answer(new PlayerProfile(), null), "rust");
        Assert.Equal("Josh", ((Dictionary<string, object>)body["profile"])["name"]);
    }

    [Fact]
    public void WritesBansAndHidesDetailsWhenNotBanned()
    {
        var ban = new BanInfo(true, "SimpleAdmin", "<b>Aimbot</b>", DateTimeOffset.FromUnixTimeSeconds(1760000000),
            null, "Admin\u0004", 3);
        var bans = (Dictionary<string, object>)Messages.Respond(Request(), LookupResult.Answer(null, ban), "cs2")["bans"];
        Assert.Equal("simpleadmin", bans["provider"]);
        Assert.Equal("Aimbot", bans["reason"]);
        Assert.Equal("Admin", bans["actor"]);
        Assert.Null(bans["expiresAt"]);
        Assert.Equal(3, bans["activeCount"]);

        var clean = (Dictionary<string, object>)Messages.Respond(Request(),
            LookupResult.Answer(null, BanInfo.NotBanned("server")), "cs2")["bans"];
        Assert.Equal(false, clean["banned"]);
        Assert.Null(clean["reason"]);
        Assert.False(clean.ContainsKey("activeCount"));
    }

    [Fact]
    public void WritesFailures()
    {
        var body = Messages.Respond(Request(), LookupResult.Failure("no_ban_provider", "None installed"), "rust");
        Assert.Equal(false, body["ok"]);
        Assert.Equal("no_ban_provider", body["error"]);
        Assert.Equal("None installed", body["message"]);
    }

    [Fact]
    public void HelloListsBanProviders()
    {
        var body = Messages.Hello(new PluginInfo("rust", "ticket-tool-rust", "1.0.0", "Oxide"), new[] { "server" });
        Assert.Equal("{\"protocol\":1,\"plugin\":{\"type\":\"rust\",\"version\":\"1.0.0\",\"platform\":\"Oxide\"},"
                     + "\"banProviders\":[\"server\"]}", Json.Write(body));
    }

    [Theory]
    [InlineData("https://dash.ticket-tool.app", "https://dash.ticket-tool.app/api/plugin/v1")]
    [InlineData("https://staging.example.com/api/plugin/v1/", "https://staging.example.com/api/plugin/v1")]
    [InlineData("http://localhost:3001", "http://localhost:3001/api/plugin/v1")]
    [InlineData("http://127.0.0.1:3001", "http://127.0.0.1:3001/api/plugin/v1")]
    public void BuildsTheApiBase(string input, string expected)
    {
        Assert.Equal(expected, TicketToolClient.ApiBase(input).ToString().TrimEnd('/'));
    }

    [Theory]
    [InlineData("http://dash.ticket-tool.app")]
    [InlineData("ftp://localhost")]
    [InlineData("not a url")]
    public void RefusesPlainHttpExceptForLocalhost(string input)
    {
        Assert.Throws<ArgumentException>(() => TicketToolClient.ApiBase(input));
    }
}
