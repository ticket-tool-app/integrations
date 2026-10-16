using System;

namespace TicketTool.PluginSdk
{
    /// <summary>
    /// Exponential backoff with "equal jitter": each wait is a random point in the
    /// upper half of the step, so a fleet of plugins that lost the server at the
    /// same moment doesn't come back in lockstep, yet no wait collapses to zero.
    /// </summary>
    public sealed class Backoff
    {
        private readonly long _baseMillis;
        private readonly long _maxMillis;
        private readonly Random _random;
        private readonly object _lock = new object();
        private int _attempt;

        public Backoff(TimeSpan baseDelay, TimeSpan max, Random random)
        {
            _baseMillis = (long)baseDelay.TotalMilliseconds;
            _maxMillis = (long)max.TotalMilliseconds;
            _random = random;
        }

        /// <summary>The schedule from the protocol: 1, 2, 4 ... 60 seconds.</summary>
        public static Backoff Standard(Random random)
        {
            return new Backoff(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(60), random);
        }

        public int Attempts
        {
            get { lock (_lock) return _attempt; }
        }

        public TimeSpan Next()
        {
            lock (_lock)
            {
                long step = _maxMillis;
                if (_attempt < 30) step = Math.Min(_maxMillis, _baseMillis << _attempt);
                _attempt++;
                long half = step / 2;
                long jitter = half > 0 ? (long)(_random.NextDouble() * (half + 1)) : 0;
                return TimeSpan.FromMilliseconds(half + Math.Min(jitter, half));
            }
        }

        public void Reset()
        {
            lock (_lock) _attempt = 0;
        }
    }
}
