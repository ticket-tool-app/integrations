namespace TicketToolLink.Tests;

public class NativeBanListTests
{
    [Fact]
    public void ParsesPermanentBansInEveryIdForm()
    {
        var ids = NativeBanList.Parse(new[]
        {
            "banid 0 76561198000000000",
            "banid 0.0 STEAM_1:0:19867136 // dupe of the first",
            "banid 0 [U:1:39734274]",
            "  BANID 0 \"STEAM_0:1:19867138\"",
            "banid 30 76561198000000009",
            "addip 0 10.0.0.1",
            "// banid 0 76561198000000008",
            "",
        });
        Assert.Equal(new[] { "76561198000000000", "76561198000000002", "76561198000000005" }, ids.OrderBy(x => x));
    }

    [Fact]
    public void ReadsTheFirstFileThatExistsAndNoticesChanges()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        string path = Path.Combine(dir, "banned_user.cfg");
        var list = new NativeBanList(new[] { Path.Combine(dir, "missing.cfg"), path });
        Assert.False(list.IsBanned("76561198000000000"));
        File.WriteAllText(path, "banid 0 76561198000000000\n");
        Assert.True(list.IsBanned("76561198000000000"));
        File.WriteAllText(path, "");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        Assert.False(list.IsBanned("76561198000000000"));
    }
}

public class SimpleAdminTests
{
    [Fact]
    public void ReadsTopLevelDatabaseSettings()
    {
        var s = SimpleAdminSettings.FromConfigJson("""
            { "ConfigVersion": 20, "DatabaseHost": "db.local", "DatabasePort": 3307, "DatabaseUser": "sa",
              "DatabasePassword": "hunter2", "DatabaseName": "cs2", "DatabaseSSlMode": "none", }
            """, out _);
        Assert.NotNull(s);
        Assert.Equal(("db.local", 3307, "sa", "cs2", "none"), (s!.Host, s.Port, s.User, s.Database, s.SslMode));
        Assert.DoesNotContain("hunter2", s.ToString());
    }

    [Fact]
    public void ReadsNestedSettingsAndSkipsSqlite()
    {
        var mysql = SimpleAdminSettings.FromConfigJson("""
            { "DatabaseConfig": { "DatabaseType": "MySQL", "DatabaseHost": "h", "DatabaseName": "n" } }
            """, out _);
        Assert.Equal(3306, mysql!.Port);

        var sqlite = SimpleAdminSettings.FromConfigJson("""
            { "DatabaseConfig": { "DatabaseType": "SQLite", "SqliteFilePath": "cs2-simpleadmin.sqlite" } }
            """, out string? why);
        Assert.Null(sqlite);
        Assert.Contains("SQLite", why);

        Assert.Null(SimpleAdminSettings.FromConfigJson("{ \"DatabaseHost\": \"\" }", out why));
        Assert.NotNull(why);
    }

    [Fact]
    public void PicksTheNewestActiveBanAndCountsThem()
    {
        var now = DateTimeOffset.UtcNow;
        DateTime Local(TimeSpan offset) => now.Add(offset).LocalDateTime;
        var ban = SimpleAdminBans.ToBan(new List<SimpleAdminBans.Row>
        {
            new("Alex", "Wallhack", Local(TimeSpan.FromHours(-1)), Local(TimeSpan.FromDays(6)), 10080),
            new("Sam", "Spam", Local(TimeSpan.FromDays(-3)), null, 0),
            new("Old", "Lapsed", Local(TimeSpan.FromDays(-9)), Local(TimeSpan.FromMinutes(-1)), 60),
        }, now);
        Assert.True(ban.Banned);
        Assert.Equal("simpleadmin", ban.Provider);
        Assert.Equal("Wallhack", ban.Reason);
        Assert.Equal("Alex", ban.Actor);
        Assert.Equal(2, ban.ActiveCount);
        Assert.InRange((ban.ExpiresAt!.Value - now).TotalDays, 5.99, 6.01);
    }

    [Fact]
    public void TreatsLapsedRowsAsNotBanned()
    {
        var now = DateTimeOffset.UtcNow;
        var ban = SimpleAdminBans.ToBan(new List<SimpleAdminBans.Row>
            { new("Old", "Lapsed", now.AddDays(-2).LocalDateTime, now.AddMinutes(-5).LocalDateTime, 60) }, now);
        Assert.False(ban.Banned);
        Assert.Equal("simpleadmin", ban.Provider);
    }
}

public class SeenStoreTests
{
    [Fact]
    public void KeepsFirstSeenAndSurvivesARestart()
    {
        string path = Path.Combine(Directory.CreateTempSubdirectory().FullName, "seen.json");
        var store = new SeenStore(path);
        store.Load();
        var first = DateTimeOffset.FromUnixTimeSeconds(1_760_000_000);
        store.Mark("76561198000000000", "Josh", first);
        store.Mark("76561198000000000", null, first.AddHours(3));
        store.Save();

        var reloaded = new SeenStore(path);
        reloaded.Load();
        var e = reloaded.Get("76561198000000000")!;
        Assert.Equal("Josh", e.Name);
        Assert.Equal(first.ToUnixTimeSeconds(), e.FirstSeen);
        Assert.Equal(first.AddHours(3).ToUnixTimeSeconds(), e.LastSeen);
        Assert.Null(reloaded.Get("76561198000000001"));
    }

    [Fact]
    public void StartsEmptyFromACorruptFile()
    {
        string path = Path.Combine(Directory.CreateTempSubdirectory().FullName, "seen.json");
        File.WriteAllText(path, "{ not json");
        var store = new SeenStore(path);
        store.Load();
        Assert.Null(store.Get("76561198000000000"));
    }
}
