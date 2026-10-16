// Minimal stand-ins for Oxide.Core, Oxide.CSharp and the Rust extension. Member
// names and signatures follow the real assemblies; the bodies route to
// StubRuntime so rust/tests can drive the plugin like a server would.
using System;
using Oxide.Core.Configuration;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Core
{
    public struct VersionNumber
    {
        public override string ToString() { return "2.0.0"; }
    }

    public class DataFileSystem
    {
        public T ReadObject<T>(string name) { return StubRuntime.ReadData<T>(name); }
        public void WriteObject<T>(string name, T obj, bool sync = false) { StubRuntime.WriteData(name, obj); }
    }

    public class OxideMod
    {
        public static readonly VersionNumber Version = new VersionNumber();
        public DataFileSystem DataFileSystem { get; } = new DataFileSystem();
        public void NextTick(Action callback) { StubRuntime.NextTick(callback); }
    }

    public static class Interface
    {
        public static OxideMod Oxide { get; } = new OxideMod();
    }
}

namespace Oxide.Core.Configuration
{
    public class DynamicConfigFile
    {
        public T ReadObject<T>(string filename = null) { return StubRuntime.ReadConfig<T>(); }
        public void WriteObject<T>(T config, bool sync = false, string filename = null) { StubRuntime.WriteConfig(config); }
    }
}

namespace Oxide.Core.Plugins
{
    public abstract class Plugin
    {
        public bool IsLoaded { get; set; }
        public object Call(string hook, params object[] args) { return null; }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class PluginReferenceAttribute : Attribute { }
}

namespace Oxide.Core.Libraries
{
    public class Permission
    {
        public void RegisterPermission(string name, Oxide.Core.Plugins.Plugin owner) { }
    }
}

namespace Oxide.Core.Libraries.Covalence
{
    public interface IPlayer
    {
        string Id { get; }
        string Name { get; }
        bool IsConnected { get; }
        bool IsServer { get; }
        bool IsAdmin { get; }
        bool HasPermission(string perm);
        void Reply(string message);
    }

    public interface IServer
    {
        string Version { get; }
    }

    public interface IPlayerManager
    {
        IPlayer FindPlayerById(string id);
    }

    public class Covalence
    {
        public IPlayerManager Players { get; set; }
        public IServer Server { get; set; }
    }
}

namespace Oxide.Plugins
{
    [AttributeUsage(AttributeTargets.Class)]
    public class InfoAttribute : Attribute
    {
        public InfoAttribute(string Title, string Author, string Version) { }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class DescriptionAttribute : Attribute
    {
        public DescriptionAttribute(string description) { }
    }

    public abstract class CSPlugin : Oxide.Core.Plugins.Plugin
    {
        protected DynamicConfigFile Config { get; private set; } = new DynamicConfigFile();
        protected virtual void LoadConfig() { if (StubRuntime.ConfigJson == null) LoadDefaultConfig(); }
        protected virtual void LoadDefaultConfig() { }
        protected virtual void SaveConfig() { }
    }

    public abstract class CSharpPlugin : CSPlugin
    {
        protected Covalence covalence = new Covalence { Players = StubRuntime.Players, Server = StubRuntime.Server };
        protected Permission permission = new Permission();
        protected void Puts(string format, params object[] args) { StubRuntime.Log("info", string.Format(format, args)); }
        protected void PrintWarning(string format, params object[] args) { StubRuntime.Log("warn", string.Format(format, args)); }
        protected void PrintError(string format, params object[] args) { StubRuntime.Log("error", string.Format(format, args)); }
        protected void NextTick(Action callback) { StubRuntime.NextTick(callback); }
        protected void AddCovalenceCommand(string[] commands, string callback, string perm = null) { }
    }

    public abstract class RustPlugin : CSharpPlugin { }
}
