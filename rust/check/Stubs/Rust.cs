// Minimal stand-ins for the Rust dedicated server's Assembly-CSharp. Member
// names and signatures follow the real game; state comes from StubRuntime.
using System.Collections.Generic;

public class ListHashSet<T> : List<T> { }

public class BasePlayer
{
    public static ListHashSet<BasePlayer> activePlayerList = new ListHashSet<BasePlayer>();
    public string displayName;
    public string userIdString;
    public bool connected;
    public string UserIDString { get { return userIdString; } }
    public bool IsConnected { get { return connected; } }

    public static BasePlayer FindAwakeOrSleeping(string strNameOrIDOrIP)
    {
        StubRuntime.AssertMainThread();
        foreach (BasePlayer p in StubRuntime.AllPlayers)
            if (p.userIdString == strNameOrIDOrIP) return p;
        return null;
    }
}

public static class ServerUsers
{
    public enum UserGroup
    {
        None,
        Owner,
        Moderator,
        Banned,
        SkipQueue
    }

    public class User
    {
        public ulong steamid;
        public UserGroup group;
        public string username;
        public string notes;
        public long expiry;
    }

    public static Dictionary<ulong, User> users = new Dictionary<ulong, User>();

    public static User Get(ulong uid)
    {
        StubRuntime.AssertMainThread();
        User u;
        return users.TryGetValue(uid, out u) ? u : null;
    }
}
