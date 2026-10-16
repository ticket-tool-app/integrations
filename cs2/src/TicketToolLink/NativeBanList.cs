using TicketTool.PluginSdk;

namespace TicketToolLink;

/// <summary>
/// The server's own ban list, cfg/banned_user.cfg, as written by writeid. Bans
/// made with banid but never written out live only in the server's memory,
/// which plugins can't read.
/// </summary>
internal sealed class NativeBanList
{
    public const string Provider = "server";

    private readonly IReadOnlyList<string> _paths;
    private readonly object _lock = new();
    private string? _loadedPath;
    private DateTime _loadedStamp;
    private HashSet<string> _banned = new();

    public NativeBanList(IReadOnlyList<string> candidatePaths)
    {
        _paths = candidatePaths;
    }

    public bool IsBanned(string steamId)
    {
        lock (_lock)
        {
            Refresh();
            return _banned.Contains(steamId);
        }
    }

    private void Refresh()
    {
        string? path = _paths.FirstOrDefault(File.Exists);
        if (path == null)
        {
            _banned = new();
            _loadedPath = null;
            return;
        }
        DateTime stamp = File.GetLastWriteTimeUtc(path);
        if (path == _loadedPath && stamp == _loadedStamp) return;
        _banned = Parse(File.ReadAllLines(path));
        _loadedPath = path;
        _loadedStamp = stamp;
    }

    /// <summary>Lines look like "banid 0 STEAM_1:0:123", "banid 0.0 [U:1:246]" or "banid 0 7656119...".</summary>
    internal static HashSet<string> Parse(IEnumerable<string> lines)
    {
        var ids = new HashSet<string>();
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            int comment = line.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0) line = line[..comment].Trim();
            string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3 || !parts[0].Equals("banid", StringComparison.OrdinalIgnoreCase)) continue;
            // writeid only writes permanent bans (duration 0); anything else has lapsed or will.
            if (!double.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double minutes) || minutes != 0)
                continue;
            string? id = SteamIds.Normalize(parts[2].Trim('"'));
            if (id != null) ids.Add(id);
        }
        return ids;
    }
}
