using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace BannerlordHtmlUI
{
    public sealed class GameThreadDispatcher
    {
        private readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();
        private long _lastDrainTimestamp;
        private int _drainActive;
        private int _hasDrained;
        private GameThreadTaskScheduler _scheduler;

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

        /// <summary>
        /// A TaskScheduler that executes continuations on the game thread through Drain.
        /// Use it for ContinueWith, or as the target scheduler of consumer-side task plumbing.
        /// </summary>
        public TaskScheduler Scheduler
        {
            get
            {
                var scheduler = _scheduler;
                if (scheduler != null) return scheduler;
                var created = new GameThreadTaskScheduler(this);
                var previous = Interlocked.CompareExchange(ref _scheduler, created, null);
                return previous ?? created;
            }
        }

        /// <summary>
        /// Awaitable that moves execution back to the game thread. The game thread has no
        /// SynchronizationContext, so any real await inside a request handler continues on the
        /// thread pool. Touch game state only after switching back:
        /// <code>
        /// var data = await LoadDataAsync();
        /// await HtmlUiService.SwitchToGameThread();
        /// // game-thread only from here on
        /// </code>
        /// </summary>
        public GameThreadAwaiter SwitchToGameThread() => new GameThreadAwaiter(this);

        private static long StopwatchTicks()
        {
            return System.Diagnostics.Stopwatch.GetTimestamp();
        }
    }

    /// <summary>Awaiter that resumes execution on the game thread via Dispatcher.Post.</summary>
    public readonly struct GameThreadAwaiter : ICriticalNotifyCompletion
    {
        private readonly GameThreadDispatcher _dispatcher;

        internal GameThreadAwaiter(GameThreadDispatcher dispatcher)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        public GameThreadAwaiter GetAwaiter() => this;

        // Always false: resuming through the queue is safe even when already on the game thread,
        // and it keeps the semantics independent of who is calling.
        public bool IsCompleted => false;

        public void OnCompleted(Action continuation) => _dispatcher.Post(continuation);
        public void UnsafeOnCompleted(Action continuation) => _dispatcher.Post(continuation);
        public void GetResult() { }
    }

    /// <summary>Executes scheduled tasks on the game thread through Dispatcher.Post.</summary>
    internal sealed class GameThreadTaskScheduler : TaskScheduler
    {
        private readonly GameThreadDispatcher _dispatcher;

        internal GameThreadTaskScheduler(GameThreadDispatcher dispatcher)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        protected override void QueueTask(Task task) => _dispatcher.Post(() => TryExecuteTask(task));

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => false;

        protected override IEnumerable<Task> GetScheduledTasks() => Array.Empty<Task>();

        public override int MaximumConcurrencyLevel => 1;
    }
}
