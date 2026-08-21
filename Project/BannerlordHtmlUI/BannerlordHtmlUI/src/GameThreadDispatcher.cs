using System;
using System.Collections.Concurrent;
using System.Threading;

namespace BannerlordHtmlUI
{
    public sealed class GameThreadDispatcher
    {
        private readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();
        private long _lastDrainTimestamp;
        private int _drainActive;
        private int _hasDrained;

        public GameThreadDispatcher()
        {
            _lastDrainTimestamp = 0L;
        }

        public int QueueCount => _queue.Count;
        public bool IsDrainActive => Volatile.Read(ref _drainActive) != 0;
        public bool HasDrained => Volatile.Read(ref _hasDrained) != 0;
        public long LastDrainTimestamp => Interlocked.Read(ref _lastDrainTimestamp);

        public void Post(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            _queue.Enqueue(action);
        }

        public int Drain(int maxItems = 256)
        {
            if (maxItems <= 0) return 0;
            var processed = 0;
            Interlocked.Exchange(ref _drainActive, 1);
            try
            {
                while (processed < maxItems && _queue.TryDequeue(out var action))
                {
                    try { action(); }
                    catch (Exception ex) { HtmlUiLogger.Error("Game-thread action failed.", ex); }
                    processed++;
                }
                return processed;
            }
            finally
            {
                Interlocked.Exchange(ref _drainActive, 0);
                Interlocked.Exchange(ref _lastDrainTimestamp, StopwatchTicks());
                Volatile.Write(ref _hasDrained, 1);
            }
        }

        public int Clear()
        {
            var cleared = 0;
            while (_queue.TryDequeue(out _)) cleared++;
            return cleared;
        }

        private static long StopwatchTicks()
        {
            return System.Diagnostics.Stopwatch.GetTimestamp();
        }
    }
}
