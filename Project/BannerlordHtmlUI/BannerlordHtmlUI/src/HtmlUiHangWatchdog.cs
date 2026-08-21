using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace BannerlordHtmlUI
{
    internal static class HtmlUiHangWatchdog
    {
        private static readonly object Sync = new object();
        private static Thread _thread;
        private static CancellationTokenSource _cts;
        private static GameThreadDispatcher _dispatcher;
        private static HtmlUiHost _host;
        private static FieldInfo _formField;
        private static long _lastGameHangLog;
        private static long _lastUiHangLog;
        private static int _running;

        private const int IntervalMs = 500;
        private const int GameStallThresholdMs = 1500;
        private const int UiStallThresholdMs = 1500;
        private const int LogCooldownMs = 3000;

        public static void Start(GameThreadDispatcher dispatcher, HtmlUiHost host)
        {
            if (dispatcher == null || host == null) return;
            lock (Sync)
            {
                if (_running != 0) return;
                _dispatcher = dispatcher;
                _host = host;
                _formField = typeof(HtmlUiHost).GetField("_form", BindingFlags.Instance | BindingFlags.NonPublic);
                _cts = new CancellationTokenSource();
                _thread = new Thread(WatchLoop)
                {
                    IsBackground = true,
                    Name = "BannerlordHtmlUI.HangWatchdog"
                };
                _running = 1;
                _thread.Start(_cts.Token);
            }
        }

        public static void Stop()
        {
            lock (Sync)
            {
                if (_running == 0) return;
                _running = 0;
                try { _cts?.Cancel(); } catch { }
                _cts = null;
                _dispatcher = null;
                _host = null;
                _formField = null;
            }
        }

        private static void WatchLoop(object state)
        {
            var token = (CancellationToken)state;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    token.WaitHandle.WaitOne(IntervalMs);
                    if (token.IsCancellationRequested) break;

                    var dispatcher = _dispatcher;
                    var host = _host;
                    if (dispatcher == null || host == null || !HtmlUiService.IsInitialized) continue;

                    CheckGameThread(dispatcher, host);
                    ProbeUiThread(host);
                }
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Debug("Hang watchdog stopped unexpectedly: " + ex.GetBaseException().Message);
            }
        }

        private static void CheckGameThread(GameThreadDispatcher dispatcher, HtmlUiHost host)
        {
            var tickStart = HtmlUiInputTraceLogger.LastTickStartTimestamp;
            if (tickStart <= 0) return;

            var elapsedSinceTickStart = TicksToMilliseconds(System.Diagnostics.Stopwatch.GetTimestamp() - tickStart);
            if (elapsedSinceTickStart < GameStallThresholdMs) return;

            var afterInput = HtmlUiInputTraceLogger.LastTickAfterInputTimestamp;
            var afterService = HtmlUiInputTraceLogger.LastTickAfterServiceTimestamp;
            var lastDrain = dispatcher.LastDrainTimestamp;
            var now = MonotonicMilliseconds();
            if (now - Interlocked.Read(ref _lastGameHangLog) < LogCooldownMs) return;
            Interlocked.Exchange(ref _lastGameHangLog, now);

            string phase;
            if (afterInput < tickStart)
                phase = "BannerlordInputTrace";
            else if (afterService < afterInput)
                phase = "HtmlUiService.Tick";
            else if (lastDrain < afterService)
                phase = "Dispatcher.Drain/after-Tick bookkeeping";
            else
                phase = "OnApplicationTick after HtmlUiService.Tick";

            HtmlUiLogger.Warn(
                "HANG WATCHDOG: SubModule OnApplicationTick stall detected. " +
                "elapsedMs=" + elapsedSinceTickStart +
                ", phase=" + phase +
                ", tickCount=" + HtmlUiInputTraceLogger.TickCount +
                ", queueCount=" + dispatcher.QueueCount +
                ", drainActive=" + dispatcher.IsDrainActive +
                ", page=" + (host.Pages.CurrentId ?? "<null>") +
                ", inputMode=" + host.InputMode +
                ", visible=" + host.IsVisible);
        }

        private static void ProbeUiThread(HtmlUiHost host)
        {
            try
            {
                var form = _formField?.GetValue(host) as Control;
                if (form == null || form.IsDisposed || !form.IsHandleCreated) return;

                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                form.BeginInvoke(new Action(() =>
                {
                    var elapsedMs = TicksToMilliseconds(System.Diagnostics.Stopwatch.GetTimestamp() - started);
                    if (elapsedMs < UiStallThresholdMs) return;

                    var now = MonotonicMilliseconds();
                    if (now - Interlocked.Read(ref _lastUiHangLog) < LogCooldownMs) return;
                    Interlocked.Exchange(ref _lastUiHangLog, now);
                    HtmlUiLogger.Warn(
                        "HANG WATCHDOG: WebView UI thread stall detected. " +
                        "elapsedMs=" + elapsedMs +
                        ", page=" + (host.Pages.CurrentId ?? "<null>") +
                        ", inputMode=" + host.InputMode +
                        ", visible=" + host.IsVisible);
                }));
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
            catch { }
        }

        private static long MonotonicMilliseconds()
        {
            return TicksToMilliseconds(System.Diagnostics.Stopwatch.GetTimestamp());
        }

        private static long TicksToMilliseconds(long ticks)
        {
            return ticks <= 0 ? 0 : ticks * 1000L / System.Diagnostics.Stopwatch.Frequency;
        }
    }
}
