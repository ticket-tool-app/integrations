using System.Text.Json;
using MySqlConnector;
using TicketTool.PluginSdk;

namespace TicketToolLink;

/// <summary>Where CS2-SimpleAdmin keeps its bans, read from its own config file.</summary>
internal sealed record SimpleAdminSettings(string Host, int Port, string User, string Password, string Database,
    string? SslMode)
{
    /// <summary>
    /// Reads CS2-SimpleAdmin.json. Older releases keep the Database* keys at the
    /// top level; newer ones nest them under DatabaseConfig with a DatabaseType.
    /// Returns null when the database isn't a configured MySQL one.
    /// </summary>
    public static SimpleAdminSettings? FromConfigJson(string json, out string? why)
    {
        why = null;
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
            { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        JsonElement root = doc.RootElement;
        JsonElement db = root.TryGetProperty("DatabaseConfig", out var nested) && nested.ValueKind == JsonValueKind.Object
            ? nested
            : root;
        string type = Str(db, "DatabaseType") ?? "MySQL";
        if (!type.Equals("MySQL", StringComparison.OrdinalIgnoreCase)
            && !type.Equals("MariaDB", StringComparison.OrdinalIgnoreCase))
        {
            why = "CS2-SimpleAdmin uses " + type + "; only its MySQL database can be read";
            return null;
        }
        string? host = Str(db, "DatabaseHost");
        string? name = Str(db, "DatabaseName");
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(name))
        {
            why = "CS2-SimpleAdmin has no database configured";
            return null;
        }
        int port = db.TryGetProperty("DatabasePort", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : 3306;
        return new SimpleAdminSettings(host, port, Str(db, "DatabaseUser") ?? "", Str(db, "DatabasePassword") ?? "", name,
            Str(db, "DatabaseSSlMode") ?? Str(db, "DatabaseSslMode"));
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    // The password stays out of anything that might be logged.
    public override string ToString() => $"{User}@{Host}:{Port}/{Database}";
}

/// <summary>
/// Read-only lookups in CS2-SimpleAdmin's sa_bans table: one indexed query per
/// lookup, a small pool and short timeouts, so a slow database costs a lookup
/// its ban part and nothing else.
/// </summary>
internal sealed class SimpleAdminBans
{
    public const string Provider = "simpleadmin";

    private const string Query =
        "SELECT admin_name, reason, created, ends, duration FROM sa_bans "
        + "WHERE player_steamid = @steamid AND status = 'ACTIVE' ORDER BY created DESC LIMIT 25";

    private readonly string _connectionString;

    public SimpleAdminBans(SimpleAdminSettings settings)
    {
        var b = new MySqlConnectionStringBuilder
        {
            Server = settings.Host,
            Port = (uint)settings.Port,
            UserID = settings.User,
            Password = settings.Password,
            Database = settings.Database,
            ConnectionTimeout = 3,
            DefaultCommandTimeout = 3,
            Pooling = true,
            MinimumPoolSize = 0,
            MaximumPoolSize = 2,
        };
        if (settings.SslMode != null && Enum.TryParse(settings.SslMode, true, out MySqlSslMode mode)) b.SslMode = mode;
        _connectionString = b.ConnectionString;
    }

    public async Task<BanInfo> LookupAsync(string steamId, CancellationToken cancellation)
    {
        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync(cancellation);
        await using var cmd = new MySqlCommand(Query, conn);
        cmd.Parameters.AddWithValue("@steamid", steamId);
        await using var reader = await cmd.ExecuteReaderAsync(cancellation);
        var rows = new List<Row>();
        while (await reader.ReadAsync(cancellation))
        {
            rows.Add(new Row(
                reader.IsDBNull(0) ? null : reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetDateTime(2),
                reader.IsDBNull(3) ? null : reader.GetDateTime(3),
                reader.IsDBNull(4) ? 0 : Convert.ToInt64(reader.GetValue(4))));
        }
        return ToBan(rows, DateTimeOffset.UtcNow);
    }

    internal sealed record Row(string? Actor, string? Reason, DateTime? Created, DateTime? Ends, long Duration);

    /// <summary>
    /// SimpleAdmin writes DATETIME columns in the game server's local time and
    /// flips status to EXPIRED on its own timer, so a row can still say ACTIVE for
    /// a little while after it ends.
    /// </summary>
    internal static BanInfo ToBan(IReadOnlyList<Row> rows, DateTimeOffset now)
    {
        var active = rows.Where(r => r.Duration <= 0 || r.Ends == null || Local(r.Ends.Value) > now).ToList();
        if (active.Count == 0) return BanInfo.NotBanned(Provider);
        Row top = active[0];
        bool permanent = top.Duration <= 0 || top.Ends == null;
        return new BanInfo(true, Provider, top.Reason, top.Created == null ? null : Local(top.Created.Value),
            permanent ? null : Local(top.Ends!.Value), top.Actor, active.Count);
    }

    private static DateTimeOffset Local(DateTime t) =>
        new(DateTime.SpecifyKind(t, DateTimeKind.Local).ToUniversalTime(), TimeSpan.Zero);
}
