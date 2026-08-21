using System;
using System.Threading;

namespace BannerlordHtmlUI
{
    internal static class HtmlUiHangWatchdog
    {
        private static readonly object Sync = new object();
        private static Thread _thread;
        private static CancellationTokenSource _cts;
        private static GameThreadDispatcher _dispatcher;
        private static HtmlUiHost _host;
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
            }
        }

        private static void WatchLoop(object state)
        {
            var token = (CancellationToken)state;
            long pendingUiProbeAt = 0;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    token.WaitHandle.WaitOne(IntervalMs);
                    if (token.IsCancellationRequested) break;

                    var dispatcher = _dispatcher;
                    var host = _host;
                    if (dispatcher == null || host == null || !HtmlUiService.IsInitialized) continue;

                    CheckGameThread(dispatcher);

                    var now = Environment.TickCount64;
                    if (pendingUiProbeAt == 0 || now - pendingUiProbeAt >= IntervalMs)
                    {
                        pendingUiProbeAt = now;
                        TryProbeUiThread(host);
                    }
                }
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Debug("Hang watchdog stopped unexpectedly: " + ex.GetBaseException().Message);
            }
        }

        private static void CheckGameThread(GameThreadDispatcher dispatcher)
        {
            var last = dispatcher.LastDrainTimestamp;
            var elapsedMs = TicksToMilliseconds(System.Diagnostics.Stopwatch.GetTimestamp() - last);
            if (elapsedMs < GameStallThresholdMs) return;

            var now = Environment.TickCount64;
            if (now - Interlocked.Read(ref _lastGameHangLog) < LogCooldownMs) return;
            Interlocked.Exchange(ref _lastGameHangLog, now);

            HtmlUiLogger.Warn(
                "HANG WATCHDOG: GameThread stall detected. " +
                "elapsedMs=" + elapsedMs +
                ", queueCount=" + dispatcher.QueueCount +
                ", drainActive=" + dispatcher.IsDrainActive +
                ", page=" + (HtmlUiService.Host.Pages.CurrentId ?? "<null>") +
                ", inputMode=" + HtmlUiService.Host.InputMode +
                ", visible=" + HtmlUiService.Host.IsVisible);
        }

        private static void TryProbeUiThread(HtmlUiHost host)
        {
            try
            {
                if (!host.IsHostCreated) return;
                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                host.ProbeUiThread(() =>
                {
                    var elapsedMs = TicksToMilliseconds(System.Diagnostics.Stopwatch.GetTimestamp() - started);
                    if (elapsedMs < UiStallThresholdMs) return;

                    var now = Environment.TickCount64;
                    if (now - Interlocked.Read(ref _lastUiHangLog) < LogCooldownMs) return;
                    Interlocked.Exchange(ref _lastUiHangLog, now);
                    HtmlUiLogger.Warn(
                        "HANG WATCHDOG: WebView UI thread stall detected. " +
                        "elapsedMs=" + elapsedMs +
                        ", page=" + (host.Pages.CurrentId ?? "<null>") +
                        ", inputMode=" + host.InputMode +
                        ", visible=" + host.IsVisible);
                });
            }
            catch { }
        }

        private static long TicksToMilliseconds(long ticks)
        {
            return ticks <= 0 ? 0 : ticks * 1000L / System.Diagnostics.Stopwatch.Frequency;
        }
    }
}
