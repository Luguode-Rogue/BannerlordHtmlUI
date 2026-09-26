using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace BannerlordHtmlUI
{
    internal sealed class HtmlUiWindowTracker : IDisposable
    {
        private static readonly object SyncLock = new object();
        private static readonly ConditionalWeakTable<HtmlUiHost, HtmlUiWindowTracker> Instances = new ConditionalWeakTable<HtmlUiHost, HtmlUiWindowTracker>();

        private readonly HtmlUiHost _host;
        private Timer _pollTimer;
        private HtmlUiOverlayForm _form;
        private bool _disposed;
        private bool _inputSuspended;
        private IntPtr _ownerHwnd;
        private bool _hasState;
        private HtmlUiWindowState _lastState;
        private FieldInfo _timerField;
        private FieldInfo _windowStateChangedField;

        private HtmlUiWindowTracker(HtmlUiHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
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
                try { tracker.StartCore(); }
                catch
                {
                    tracker.Dispose();
                    Instances.Remove(host);
                    throw;
                }
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
                tracker.Dispose();
            }
        }

        private void StartCore()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(HtmlUiWindowTracker));
            _form = GetForm();
            if (_form == null || _form.IsDisposed || !_form.IsHandleCreated)
                throw new InvalidOperationException("HtmlUi overlay form is not ready.");

            // OnReady may run on a pool thread. Windows Forms timers and window state must
            // stay on the overlay UI thread. Polling also recovers missed Alt+Tab transitions.
            PostToUi(() =>
            {
                if (_disposed) return;
                StopLegacyFollowTimer();
                _pollTimer = new Timer { Interval = 250 };
                _pollTimer.Tick += OnPoll;
                _pollTimer.Start();
                SyncNow();
                HtmlUiLogger.Info("Bannerlord window tracker started with 250ms focus recovery.");
            });
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
            }
            catch (Exception ex) { HtmlUiLogger.Debug("Failed to disable legacy 100ms follow timer: " + ex.GetBaseException().Message); }
        }

        private void OnPoll(object sender, EventArgs e)
        {
            try { SyncNow(); }
            catch (Exception ex) { HtmlUiLogger.Debug("Window tracker poll failed: " + ex.GetBaseException().Message); }
        }

        private void PostToUi(Action action)
        {
            var form = _form;
            if (form == null || form.IsDisposed || !form.IsHandleCreated) return;
            try
            {
                if (form.InvokeRequired) form.BeginInvoke(action);
                else action();
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        public void SyncNow()
        {
            if (_disposed) return;
            var form = _form ?? GetForm();
            if (form == null || form.IsDisposed || !form.IsHandleCreated) return;
            if (_host.InputMode == HtmlUiInputMode.Hidden || _host.InputMode == HtmlUiInputMode.Passive)
                HtmlUiInputBlocker.SetBlocking(false, false);
            if (_host.InputMode == HtmlUiInputMode.Hidden)
            {
                _inputSuspended = false;
                _pollTimer?.Stop();
            }
            else if (_pollTimer != null && !_pollTimer.Enabled)
            {
                _pollTimer.Start();
            }

            IntPtr gameHwnd;
            if (!Win32.TryGetGameWindowHandle(form.Handle, out gameHwnd) || gameHwnd == IntPtr.Zero)
            {
                if (form.Visible) form.Hide();
                SuspendInputWhileOverlayUnavailable();
                _ownerHwnd = IntPtr.Zero;
                PublishState(new HtmlUiWindowState(false, false, false, 0, 0, 0, 0));
                return;
            }

            Win32.RECT rect;
            Win32.GetWindowRect(gameHwnd, out rect);
            var minimized = Win32.IsIconic(gameHwnd);
            var gameVisible = Win32.IsWindowVisible(gameHwnd) && !minimized;
            var foregroundHwnd = Win32.GetForegroundWindow();
            var foreground = foregroundHwnd == gameHwnd ||
                (foregroundHwnd != IntPtr.Zero && Win32.IsChild(gameHwnd, foregroundHwnd));
            var overlayForeground = foregroundHwnd == form.Handle ||
                (foregroundHwnd != IntPtr.Zero && Win32.IsChild(form.Handle, foregroundHwnd));
            // IsVisible includes form.Visible and becomes false after Windows hides an owned
            // overlay during Alt+Tab. The requested mode is the source of truth for recovery.
            var requestedVisible = _host.InputMode != HtmlUiInputMode.Hidden;
            var showOverlay = requestedVisible && gameVisible && (foreground || overlayForeground);
            var windowWidth = Math.Max(0, rect.Right - rect.Left);
            var windowHeight = Math.Max(0, rect.Bottom - rect.Top);

            if (showOverlay)
            {
                try
                {
                    if (_ownerHwnd != gameHwnd)
                    {
                        form.SetOwner(gameHwnd);
                        _ownerHwnd = gameHwnd;
                    }
                    var bounds = HtmlUiOverlayLayoutRegistry.GetBounds(_host, rect.Left, rect.Top, windowWidth, windowHeight);
                    if (form.Bounds != bounds) form.Bounds = bounds;
                }
                catch (Exception ex) { HtmlUiLogger.Debug("Overlay placement update failed: " + ex.GetBaseException().Message); }
                if (!form.Visible)
                {
                    try { form.Show(); } catch { }
                }
                if (!form.Visible)
                {
                    SuspendInputWhileOverlayUnavailable();
                }
                else if (_inputSuspended)
                {
                    _inputSuspended = false;
                    var mode = _host.InputMode;
                    // Defer until any active input transition has finished; nested mode
                    // changes would invalidate its generation and leave input unowned.
                    try { form.BeginInvoke(new Action(() => { if (!_disposed && _host.InputMode == mode) _host.SetInputMode(mode); })); }
                    catch (ObjectDisposedException) { }
                    catch (InvalidOperationException) { }
                }
            }
            else if (!requestedVisible || !gameVisible)
            {
                if (form.Visible) form.Hide();
                if (!gameVisible) SuspendInputWhileOverlayUnavailable();
            }

            // While another application owns the foreground, leave the overlay's requested
            // visibility alone. Hiding it here can strand Captured input when Bannerlord returns.

            // MouseCaptured intentionally lets the WebView2 child keep the foreground while the
            // user interacts with it: Chromium drops mouse input when its window is inactive.
            // The game keeps polling input in its own tick, so ESC/N keep working unfocused.
            // Returning the foreground to the game is handled on mode transitions (Passive).

            var actualBounds = form.Bounds;
            PublishState(new HtmlUiWindowState(
                foreground || overlayForeground,
                showOverlay && form.Visible,
                minimized,
                actualBounds.Left,
                actualBounds.Top,
                Math.Max(0, actualBounds.Width),
                Math.Max(0, actualBounds.Height)));
        }

        private void SuspendInputWhileOverlayUnavailable()
        {
            HtmlUiInputBlocker.SetBlocking(false, false);
            if (_host.InputMode == HtmlUiInputMode.Hidden || _host.InputMode == HtmlUiInputMode.Passive)
            {
                _inputSuspended = false;
                HtmlUiNativeMouseDispatcher.Stop();
                HtmlUiCursorController.SetOwned(_host, false);
                return;
            }
            if (_inputSuspended) return;
            _inputSuspended = true;
            HtmlUiNativeMouseDispatcher.Stop();
            HtmlUiCursorController.SetOwned(_host, false);
            HtmlUiInputTraceLogger.Event("WINDOW_TRACKER_INPUT_SUSPENDED mode=" + _host.InputMode);
        }

        private void PublishState(HtmlUiWindowState state)
        {
            if (_hasState && StateEquals(_lastState, state)) return;
            _hasState = true;
            _lastState = state;
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
            var timer = _pollTimer;
            _pollTimer = null;
            if (timer != null) PostToUi(() => { timer.Stop(); timer.Tick -= OnPoll; timer.Dispose(); });
            _form = null;
        }
    }
}
