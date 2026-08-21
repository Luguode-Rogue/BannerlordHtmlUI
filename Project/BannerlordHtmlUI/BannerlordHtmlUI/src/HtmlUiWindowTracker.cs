using System;
using System.Reflection;
using System.Windows.Forms;

namespace BannerlordHtmlUI
{
    /// <summary>
    /// Owns only Bannerlord window facts: HWND resolution, geometry, foreground/minimized state,
    /// and overlay placement. It never changes input mode or foreground focus.
    /// </summary>
    internal sealed class HtmlUiWindowTracker : IDisposable
    {
        private const uint EventSystemForeground = 0x0003;
        private const uint EventSystemMinimizeStart = 0x0016;
        private const uint EventSystemMinimizeEnd = 0x0017;
        private const uint EventObjectLocationChange = 0x800B;
        private const uint EventObjectShow = 0x8002;
        private const uint EventObjectHide = 0x8003;
        private const uint WineventOutOfContext = 0x0000;
        private static readonly IntPtr ObjIdWindow = IntPtr.Zero;

        private delegate void WinEventDelegate(
            IntPtr hWinEventHook,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint idEventThread,
            uint msEventTime);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(
            uint eventMin,
            uint eventMax,
            IntPtr hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc,
            uint idProcess,
            uint idThread,
            uint flags);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        private readonly HtmlUiHost _host;
        private readonly WinEventDelegate _callback;
        private IntPtr _hook;
        private HtmlUiOverlayForm _form;
        private bool _disposed;
        private bool _hasState;
        private HtmlUiWindowState _lastState;
        private FieldInfo _timerField;

        public HtmlUiWindowTracker(HtmlUiHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _callback = OnWinEvent;
        }

        public void Start()
        {
            if (_disposed || _hook != IntPtr.Zero) return;

            _form = GetForm();
            if (_form == null || _form.IsDisposed || !_form.IsHandleCreated)
                throw new InvalidOperationException("HtmlUi overlay form is not ready.");

            StopLegacyFollowTimer();

            _hook = SetWinEventHook(
                EventSystemForeground,
                EventObjectHide,
                IntPtr.Zero,
                _callback,
                0,
                0,
                WineventOutOfContext);

            if (_hook == IntPtr.Zero)
                throw new InvalidOperationException("Failed to install WinEvent window tracking hook.");

            SyncNow();
            HtmlUiLogger.Info("Event-driven Bannerlord window tracker started; legacy 100ms follow timer disabled.");
        }

        private void StopLegacyFollowTimer()
        {
            try
            {
                _timerField = typeof(HtmlUiHost).GetField("_followTimer", BindingFlags.Instance | BindingFlags.NonPublic);
                var timer = _timerField?.GetValue(_host) as Timer;
                if (timer != null)
                {
                    timer.Stop();
                    timer.Dispose();
                    _timerField.SetValue(_host, null);
                }
            }
            catch (Exception ex)
            {
                HtmlUiLogger.Debug("Failed to disable legacy 100ms follow timer: " + ex.GetBaseException().Message);
            }
        }

        private void OnWinEvent(
            IntPtr hWinEventHook,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint idEventThread,
            uint msEventTime)
        {
            if (_disposed || hwnd == IntPtr.Zero) return;
            if (idObject != 0 && idObject != unchecked((int)ObjIdWindow.ToInt64())) return;

            if (eventType != EventSystemForeground && eventType != EventSystemMinimizeStart &&
                eventType != EventSystemMinimizeEnd && eventType != EventObjectLocationChange &&
                eventType != EventObjectShow && eventType != EventObjectHide)
                return;

            if (!IsRelevantGameWindow(hwnd)) return;
            PostToUi(SyncNow);
        }

        private bool IsRelevantGameWindow(IntPtr hwnd)
        {
            try
            {
                if (Win32.TryGetGameWindowHandle(_form?.Handle ?? IntPtr.Zero, out var gameHwnd))
                    return hwnd == gameHwnd;
            }
            catch { }
            return false;
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

            if (!Win32.TryGetGameWindowHandle(form.Handle, out var gameHwnd) || gameHwnd == IntPtr.Zero)
            {
                PublishState(new HtmlUiWindowState(false, false, false, 0, 0, 0, 0));
                if (!form.IsDisposed && form.Visible) form.Hide();
                return;
            }

            var rect = Win32.GetWindowRect(gameHwnd, out var value) ? value : default(Win32.RECT);
            var minimized = Win32.IsIconic(gameHwnd);
            var gameVisible = Win32.IsWindowVisible(gameHwnd) && !minimized;
            var foreground = Win32.GetForegroundWindow() == gameHwnd;
            var visible = _host.IsVisible && gameVisible;

            if (gameVisible)
            {
                try
                {
                    form.SetOwner(gameHwnd);
                    form.Bounds = new System.Drawing.Rectangle(
                        rect.Left,
                        rect.Top,
                        Math.Max(1, rect.Right - rect.Left),
                        Math.Max(1, rect.Bottom - rect.Top));
                }
                catch (Exception ex)
                {
                    HtmlUiLogger.Debug("Overlay placement update failed: " + ex.GetBaseException().Message);
                }
            }
            else if (form.Visible)
            {
                try { form.Hide(); } catch { }
            }

            PublishState(new HtmlUiWindowState(
                foreground || (form.IsHandleCreated && Win32.GetForegroundWindow() == form.Handle),
                visible,
                minimized,
                rect.Left,
                rect.Top,
                Math.Max(0, rect.Right - rect.Left),
                Math.Max(0, rect.Bottom - rect.Top)));
        }

        private void PublishState(HtmlUiWindowState state)
        {
            if (_hasState && StateEquals(_lastState, state)) return;
            _hasState = true;
            _lastState = state;
            try { _host.PublishWindowStateFromTracker(state); } catch (Exception ex) { HtmlUiLogger.Debug("Window state publication failed: " + ex.GetBaseException().Message); }
        }

        private static bool StateEquals(HtmlUiWindowState a, HtmlUiWindowState b)
        {
            return a.IsForeground == b.IsForeground &&
                   a.IsVisible == b.IsVisible &&
                   a.IsMinimized == b.IsMinimized &&
                   a.Left == b.Left &&
                   a.Top == b.Top &&
                   a.Width == b.Width &&
                   a.Height == b.Height;
        }

        private HtmlUiOverlayForm GetForm()
        {
            var field = typeof(HtmlUiHost).GetField("_form", BindingFlags.Instance | BindingFlags.NonPublic);
            return field?.GetValue(_host) as HtmlUiOverlayForm;
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
