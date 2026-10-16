using System.Text.Json;
using System.Text.Json.Serialization;

namespace TicketToolLink;

/// <summary>
/// First and last time each player was on this server, kept in seen.json next
/// to the plugin, because CS2 itself keeps no record of offline players.
/// Thread-safe; saves are written to a temp file and moved into place.
/// </summary>
internal sealed class SeenStore
{
    internal sealed class Entry
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("firstSeen")] public long FirstSeen { get; set; }
        [JsonPropertyName("lastSeen")] public long LastSeen { get; set; }
    }

    private readonly string _path;
    private readonly object _lock = new();
    private Dictionary<string, Entry> _entries = new();
    private bool _dirty;

    public SeenStore(string path)
    {
        _path = path;
    }

    public void Load()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_path))
                    _entries = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(_path)) ?? new();
            }
            catch (JsonException)
            {
                // A corrupt file is replaced on the next save rather than stopping the plugin.
                _entries = new();
            }
        }
    }

    public void Mark(string steamId, string? name, DateTimeOffset now)
    {
        long t = now.ToUnixTimeSeconds();
        lock (_lock)
        {
            if (!_entries.TryGetValue(steamId, out var e))
            {
                e = new Entry { FirstSeen = t };
                _entries[steamId] = e;
            }
            if (!string.IsNullOrWhiteSpace(name)) e.Name = name;
            e.LastSeen = t;
            _dirty = true;
        }
    }

    public Entry? Get(string steamId)
    {
        lock (_lock)
        {
            return _entries.TryGetValue(steamId, out var e)
                ? new Entry { Name = e.Name, FirstSeen = e.FirstSeen, LastSeen = e.LastSeen }
                : null;
        }
    }

    public void Save()
    {
        string json;
        lock (_lock)
        {
            if (!_dirty) return;
            json = JsonSerializer.Serialize(_entries);
            _dirty = false;
        }
        string tmp = _path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, _path, overwrite: true);
    }
}
