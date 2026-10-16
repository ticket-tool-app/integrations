using System;

namespace TicketTool.PluginSdk
{
    public enum ClientState
    {
        Stopped,
        Starting,
        Connected,
        Reconnecting,
        PausedPlan,
        StoppedInvalidKey,
        StoppedError
    }

    /// <summary>A snapshot for a status command.</summary>
    public sealed class ClientStatus
    {
        internal ClientStatus(ClientState state, string lastError, DateTimeOffset? lastErrorAt, HelloReply hello,
            DateTimeOffset? lastPollAt, long answered, long clockOffsetSeconds)
        {
            State = state;
            LastError = lastError;
            LastErrorAt = lastErrorAt;
            Hello = hello;
            LastPollAt = lastPollAt;
            Answered = answered;
            ClockOffsetSeconds = clockOffsetSeconds;
        }

        public ClientState State { get; private set; }

        /// <summary>The last problem, phrased for a server admin; never contains the key.</summary>
        public string LastError { get; private set; }

        public DateTimeOffset? LastErrorAt { get; private set; }
        public HelloReply Hello { get; private set; }
        public DateTimeOffset? LastPollAt { get; private set; }
        public long Answered { get; private set; }
        public long ClockOffsetSeconds { get; private set; }

        public bool Running
        {
            get
            {
                return State == ClientState.Starting || State == ClientState.Connected
                       || State == ClientState.Reconnecting || State == ClientState.PausedPlan;
            }
        }
    }

    /// <summary>Where the SDK writes its log lines. Implementations must be thread-safe.</summary>
    public interface ISdkLogger
    {
        void Debug(string message);
        void Info(string message);
        void Warn(string message, Exception error);
    }

    public sealed class NullSdkLogger : ISdkLogger
    {
        public static readonly NullSdkLogger Instance = new NullSdkLogger();
        public void Debug(string message) { }
        public void Info(string message) { }
        public void Warn(string message, Exception error) { }
    }
}
