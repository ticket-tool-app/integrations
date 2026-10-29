// Test-side state behind the stubs: a single "server main thread" that runs
// NextTick callbacks, config and data files in memory, and captured log lines.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Newtonsoft.Json;
using Oxide.Core.Libraries.Covalence;

public static class StubRuntime
{
    private static readonly BlockingCollection<Action> Queue = new BlockingCollection<Action>();
    private static Thread _main;

    public static string ConfigJson;
    public static readonly ConcurrentDictionary<string, string> Data = new ConcurrentDictionary<string, string>();
    public static readonly ConcurrentQueue<string> Lines = new ConcurrentQueue<string>();
    public static readonly ConcurrentQueue<string> Chat = new ConcurrentQueue<string>();
    public static readonly List<BasePlayer> AllPlayers = new List<BasePlayer>();
    public static readonly StubPlayers Players = new StubPlayers();
    public static readonly StubServer Server = new StubServer();
    public static volatile bool OffMainThreadAccess;

    public static void StartMainThread()
    {
        if (_main != null) return;
        _main = new Thread(() =>
        {
            foreach (Action a in Queue.GetConsumingEnumerable()) a();
        }) { IsBackground = true, Name = "stub-main" };
        _main.Start();
    }

    public static void NextTick(Action a) { Queue.Add(a); }

    public static void RunOnMain(Action a)
    {
        var done = new ManualResetEventSlim();
        Exception error = null;
        NextTick(() =>
        {
            try { a(); }
            catch (Exception e) { error = e; }
            done.Set();
        });
        done.Wait();
        if (error != null) throw error;
    }

    public static void AssertMainThread()
    {
        if (_main != null && Thread.CurrentThread != _main) OffMainThreadAccess = true;
    }

    public static void Log(string level, string line) { Lines.Enqueue(level + ": " + line); }

    public static T ReadConfig<T>() { return ConfigJson == null ? default(T) : JsonConvert.DeserializeObject<T>(ConfigJson); }

    public static void WriteConfig(object config) { ConfigJson = JsonConvert.SerializeObject(config); }

    public static T ReadData<T>(string name)
    {
        string json;
        return Data.TryGetValue(name, out json) ? JsonConvert.DeserializeObject<T>(json) : default(T);
    }

    public static void WriteData(string name, object value) { Data[name] = JsonConvert.SerializeObject(value); }

    public sealed class StubPlayers : IPlayerManager
    {
        public readonly ConcurrentDictionary<string, IPlayer> Known = new ConcurrentDictionary<string, IPlayer>();

        public IPlayer FindPlayerById(string id)
        {
            IPlayer p;
            return Known.TryGetValue(id, out p) ? p : null;
        }
    }

    public sealed class StubServer : IServer
    {
        public string Version { get { return "2590"; } }
    }

    public sealed class Console : IPlayer
    {
        public readonly List<string> Replies = new List<string>();
        public string Id { get { return "server_console"; } }
        public string Name { get { return "Server Console"; } }
        public bool IsConnected { get { return true; } }
        public bool IsServer { get { return true; } }
        public bool IsAdmin { get { return true; } }
        public bool HasPermission(string perm) { return true; }
        public void Reply(string message) { Replies.Add(message); }
    }
}
