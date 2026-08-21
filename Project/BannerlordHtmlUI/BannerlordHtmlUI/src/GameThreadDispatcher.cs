using System;
using System.Collections.Concurrent;

namespace BannerlordHtmlUI
{
    public sealed class GameThreadDispatcher
    {
        private readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();

        public void Post(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            _queue.Enqueue(action);
        }

        public int Drain(int maxItems = 256)
        {
            var processed = 0;
            while (processed < maxItems && _queue.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception ex) { HtmlUiLogger.Error("Game-thread action failed.", ex); }
                processed++;
            }
            return processed;
        }

        public int Clear()
        {
            var cleared = 0;
            while (_queue.TryDequeue(out _)) cleared++;
            return cleared;
        }
    }
}
