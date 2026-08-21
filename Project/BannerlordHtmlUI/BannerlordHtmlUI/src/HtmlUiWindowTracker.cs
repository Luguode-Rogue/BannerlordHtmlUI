using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace BannerlordHtmlUI
{
    internal sealed class HtmlUiWindowTracker : IDisposable
    {
        private const uint EventSystemForeground = 0x0003;
        private const uint EventSystemMinimizeStart = 0x0016;
        private const uint EventSystemMinimizeEnd = 0x0017;
        private const uint EventObjectLocationChange = 0x800B;
        private const uint EventObjectShow = 0x8002;
        private const uint EventObjectHide = 0x8003;
        private const uint WineventOutOfContext = 0x0000;
        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint idEventThread, uint msEventTime);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint flags);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        private static readonly object SyncLock = new object();
        private static readonly ConditionalWeakTable<HtmlUiHost, HtmlUiWindowTracker> Instances = new ConditionalWeakTable<HtmlUiHost, HtmlUiWindowTracker>();

        private readonly HtmlUiHost _host;
        private readonly WinEventDelegate _callback;
        private IntPtr _hook;
        private HtmlUiOverlayForm _form;
        private bool _disposed;
        private bool _hasState;
        private HtmlUiWindowState _lastState;
        private FieldInfo _timerField;
        private FieldInfo _windowStateChangedField;

        private HtmlUiWindowTracker(HtmlUiHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _callback = OnWinEvent;
        }

        public static void Install(HtmlUiHost host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            lock (SyncLock)
            {
                HtmlUiWindowTracker existing;
                if (Instances.TryGetValue(host, out existing))
                {
                    existing.PostToUi(existing.SyncNow);
                    return;
                }

                var tracker = new HtmlUiWindowTracker(host);
                Instances.Add(host, tracker);
                tracker._form = tracker.GetForm();
                if (tracker._form == null)
                {
                    Instances.Remove(host);
                    throw new InvalidOperationException("HtmlUi overlay form was not created.");
                }

                tracker.PostToUi(tracker.StartCore);
            }
        }

        public static void RequestSync(HtmlUiHost host)
        {
            if (host == null) return;
            HtmlUiWindowTracker tracker;
            if (Instances.TryGetValue(host, out tracker)) tracker.PostToUi(tracker.SyncNow);
        }

        public static HtmlUiWindowState GetState(HtmlUiHost host)
        {
            if (host == null) return default(HtmlUiWindowState);
            HtmlUiWindowTracker tracker;
            return Instances.TryGetValue(host, out tracker) ? tracker._lastState : default(HtmlUiWindowState);
        }

        public static void Uninstall(HtmlUiHost host)
        {
            if (host == null) return;
            lock (SyncLock)
            {
                HtmlUiWindowTracker tracker;
                if (!Instances.TryGetValue(host, out tracker)) return;
                Instances.Remove(host);
                tracker.PostToUi(tracker.Dispose);
            }
        }

        private void StartCore()
        {
            if (_disposed) return;
            if (_form == null) throw new InvalidOperationException("HtmlUi overlay form was not created.");

            StopLegacyFollowTimer();
            _hook = SetWinEventHook(EventSystemForeground, EventObjectHide, IntPtr.Zero, _callback, 0, 0, WineventOutOfContext);
            if (_hook == IntPtr.Zero)
            {
                HtmlUiLogger.Error("Failed to install WinEvent window tracking hook.");
                return;
            }

            SyncNow();
            HtmlUiInputTraceLogger.Event("WINDOW_TRACKER_STARTED uiThread=" + ThreadId());
            HtmlUiLogger.Info("Event-driven Bannerlord window tracker started; legacy 100ms follow timer disabled.");
        }

        private void StopLegacyFollowTimer()
        {
            try
            {
                _timerField = typeof(HtmlUiHost).GetField("_followTimer", BindingFlags.Instance | BindingFlags.NonPublic);
                var timer = _timerField == null ? null : _timerField.GetValue(_host) as Timer;
                if (timer == null) return;
                timer.Stop();
                timer.Dispose();
                _timerField.SetValue(_host, null);
                HtmlUiInputTraceLogger.Event("WINDOW_TRACKER_LEGACY_TIMER_DISABLED");
            }
            catch (Exception ex) { HtmlUiLogger.Debug("Failed to disable legacy 100ms follow timer: " + ex.GetBaseException().Message); }
        }

        private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint idEventThread, uint msEventTime)
        {
            if (_disposed || idObject != 0 || idChild != 0) return;
            if (eventType != EventSystemForeground && eventType != EventSystemMinimizeStart && eventType != EventSystemMinimizeEnd && eventType != EventObjectLocationChange && eventType != EventObjectShow && eventType != EventObjectHide) return;

            if (eventType == EventSystemForeground)
            {
                PostToUi(SyncNow);
                return;
            }

            if (hwnd == IntPtr.Zero || !IsRelevantGameWindow(hwnd)) return;
            PostToUi(SyncNow);
        }

        private bool IsRelevantGameWindow(IntPtr hwnd)
        {
            try
            {
                IntPtr gameHwnd;
                return Win32.TryGetGameWindowHandle(IntPtr.Zero, out gameHwnd) && hwnd == gameHwnd;
            }
            catch { return false; }
        }

        private void PostToUi(Action action)
        {
            var form = _form;
            if (form == null) return;
            try
            {
                form.BeginInvoke(action);
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }

        private static int ThreadId() { return System.Threading.Thread.CurrentThread.ManagedThreadId; }

        public void SyncNow()
        {
            if (_disposed) return;
            var form = _form ?? GetForm();
            if (form == null || form.IsDisposed || !form.IsHandleCreated) return;

            IntPtr gameHwnd;
            if (!Win32.TryGetGameWindowHandle(IntPtr.Zero, out gameHwnd) || gameHwnd == IntPtr.Zero)
            {
                if (form.Visible) form.Hide();
                PublishState(new HtmlUiWindowState(false, false, false, 0, 0, 0, 0));
                HtmlUiInputTraceLogger.Event("WINDOW_TRACKER_STATE game=0 visible=false reason=hwnd-unresolved");
                return;
            }

            Win32.RECT rect;
            Win32.GetWindowRect(gameHwnd, out rect);
            var minimized = Win32.IsIconic(gameHwnd);
            var gameVisible = Win32.IsWindowVisible(gameHwnd) && !minimized;
            var foreground = Win32.GetForegroundWindow() == gameHwnd;
            var overlayForeground = _host.InputMode != HtmlUiInputMode.Passive && form.IsHandleCreated && Win32.GetForegroundWindow() == form.Handle;
            var requestedVisible = _host.IsVisible;
            var showOverlay = requestedVisible && gameVisible && (foreground || overlayForeground);
            var windowWidth = Math.Max(0, rect.Right - rect.Left);
            var windowHeight = Math.Max(0, rect.Bottom - rect.Top);

            if (gameVisible && showOverlay)
            {
                try
                {
                    form.SetOwner(gameHwnd);
                    var bounds = HtmlUiOverlayLayoutRegistry.GetBounds(_host, rect.Left, rect.Top, windowWidth, windowHeight);
                    form.Bounds = bounds;
                }
                catch (Exception ex) { HtmlUiLogger.Debug("Overlay placement update failed: " + ex.GetBaseException().Message); }
            }

            if (showOverlay)
            {
                if (!form.Visible)
                {
                    try { form.Show(); } catch { }
                }
            }
            else if (form.Visible)
            {
                try { form.Hide(); } catch { }
            }

            var actualBounds = form.Bounds;
            var state = new HtmlUiWindowState(
                foreground || overlayForeground,
                showOverlay && form.Visible,
                minimized,
                actualBounds.Left,
                actualBounds.Top,
                Math.Max(0, actualBounds.Width),
                Math.Max(0, actualBounds.Height));
            PublishState(state);
        }

        private void PublishState(HtmlUiWindowState state)
        {
            if (_hasState && StateEquals(_lastState, state)) return;
            _hasState = true;
            _lastState = state;
            HtmlUiInputTraceLogger.Event(
                "WINDOW_TRACKER_STATE foreground=" + state.IsForeground +
                " visible=" + state.IsVisible +
                " minimized=" + state.IsMinimized +
                " bounds=" + state.Left + "," + state.Top + "," + state.Width + "x" + state.Height);
            try
            {
                if (_windowStateChangedField == null)
                    _windowStateChangedField = typeof(HtmlUiHost).GetField("WindowStateChanged", BindingFlags.Instance | BindingFlags.NonPublic);
                var handler = _windowStateChangedField == null ? null : _windowStateChangedField.GetValue(_host) as Action<HtmlUiWindowState>;
                if (handler != null) handler(state);
            }
            catch (Exception ex) { HtmlUiLogger.Debug("Window state publication failed: " + ex.GetBaseException().Message); }
        }

        private static bool StateEquals(HtmlUiWindowState a, HtmlUiWindowState b)
        {
            return a.IsForeground == b.IsForeground && a.IsVisible == b.IsVisible && a.IsMinimized == b.IsMinimized && a.Left == b.Left && a.Top == b.Top && a.Width == b.Width && a.Height == b.Height;
        }

        private HtmlUiOverlayForm GetForm()
        {
            var field = typeof(HtmlUiHost).GetField("_form", BindingFlags.Instance | BindingFlags.NonPublic);
            return field == null ? null : field.GetValue(_host) as HtmlUiOverlayForm;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_hook != IntPtr.Zero)
            {
                try { UnhookWinEvent(_hook); } catch { }
                _hook = IntPtr.Zero;
            }
            _form = null;
        }
    }
}