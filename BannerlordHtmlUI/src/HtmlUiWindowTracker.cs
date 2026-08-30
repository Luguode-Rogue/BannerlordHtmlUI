using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
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
        // SetWinEventHook hands an unmanaged callback pointer to _callback. ConditionalWeakTable only
        // holds a weak reference, so a tracker that is not strongly rooted can be collected while
        // Win32 still calls back into it (CallbackOnCollectedDelegate / hard crash). Every tracker
        // with a live hook is therefore kept here until the hook is removed in Dispose.
        private static readonly List<HtmlUiWindowTracker> HookedTrackers = new List<HtmlUiWindowTracker>();

        private readonly HtmlUiHost _host;
        private readonly WinEventDelegate _callback;
        private IntPtr _hook;
        private HtmlUiOverlayForm _form;
        private bool _disposed;
        private bool _hasState;
        private HtmlUiWindowState _lastState;
        // Resolved on the overlay UI thread and read from the WinEvent callback thread.
        private IntPtr _lastGameHwnd;
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
                    // SyncNow touches the overlay form, so route it through the form's thread
                    // rather than running it on whichever thread requested a re-install.
                    existing.PostToUi(existing.SyncNow);
                    return;
                }

                var tracker = new HtmlUiWindowTracker(host);
                Instances.Add(host, tracker);
                try { tracker.Start(); }
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

        /// <summary>
        /// Starts the tracker on the overlay form's thread. When the caller is already on that thread the
        /// start stays synchronous so installation ordering remains deterministic; otherwise it is
        /// dispatched and a late failure only tears the tracker down instead of throwing across threads.
        /// </summary>
        private void Start()
        {
            var form = GetForm();
            if (form == null || form.IsDisposed || !form.IsHandleCreated)
                throw new InvalidOperationException("HtmlUi overlay form is not ready.");

            if (!form.InvokeRequired) { StartCore(); return; }

            form.BeginInvoke(new Action(() =>
            {
                try { StartCore(); }
                catch (Exception ex)
                {
                    HtmlUiLogger.Error("Window tracker failed to start on the overlay UI thread.", ex);
                    Uninstall(_host);
                }
            }));
        }

        private void StartCore()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(HtmlUiWindowTracker));
            _form = GetForm();
            if (_form == null || _form.IsDisposed || !_form.IsHandleCreated)
                throw new InvalidOperationException("HtmlUi overlay form is not ready.");

            _hook = SetWinEventHook(EventSystemForeground, EventObjectHide, IntPtr.Zero, _callback, 0, 0, WineventOutOfContext);
            if (_hook == IntPtr.Zero) throw new InvalidOperationException("Failed to install WinEvent window tracking hook.");

            // Keep the tracker alive for as long as Win32 holds the callback pointer.
            lock (SyncLock)
            {
                if (!HookedTrackers.Contains(this)) HookedTrackers.Add(this);
            }

            SyncNow();
            HtmlUiLogger.Info("Event-driven Bannerlord window tracker started.");
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
            // Fast path: the handle resolved during the last sync performed on the UI thread.
            var last = Volatile.Read(ref _lastGameHwnd);
            if (last != IntPtr.Zero && hwnd == last) return true;

            // Fall back to a live resolve so a rebuilt Bannerlord window is still recognised instead
            // of being ignored forever. The overlay handle stays excluded (BUG_KNOWLEDGE_BASE: the
            // overlay exclusion must be preserved), otherwise the overlay counts as the game window.
            try
            {
                IntPtr resolved;
                return Win32.TryGetGameWindowHandle(_form == null ? IntPtr.Zero : _form.Handle, out resolved) && hwnd == resolved;
            }
            catch { return false; }
        }

        private void PostToUi(Action action)
        {
            // Fall back to the host's form while StartCore is still pending on the UI thread,
            // otherwise sync requests raised during an asynchronous start would be dropped.
            var form = _form ?? GetForm();
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

            IntPtr gameHwnd;
            if (!Win32.TryGetGameWindowHandle(form.Handle, out gameHwnd) || gameHwnd == IntPtr.Zero)
            {
                // A Bannerlord HWND that cannot be resolved right now does not mean the game exited
                // (BUG_KNOWLEDGE_BASE: HWND = 0). Keep the overlay exactly as it is and only report
                // the facts we can no longer confirm as false, instead of hiding a visible page.
                PublishState(new HtmlUiWindowState(
                    false,
                    _hasState ? _lastState.IsVisible : form.Visible,
                    false,
                    _hasState ? _lastState.Left : form.Left,
                    _hasState ? _lastState.Top : form.Top,
                    _hasState ? _lastState.Width : form.Width,
                    _hasState ? _lastState.Height : form.Height));
                return;
            }

            Volatile.Write(ref _lastGameHwnd, gameHwnd);

            Win32.RECT rect;
            Win32.GetWindowRect(gameHwnd, out rect);
            var minimized = Win32.IsIconic(gameHwnd);
            var gameVisible = Win32.IsWindowVisible(gameHwnd) && !minimized;

            // Sample the foreground window once: two separate calls can observe different windows
            // and make both the game and the overlay look non-foreground, hiding the overlay.
            var foregroundHwnd = Win32.GetForegroundWindow();
            var foreground = foregroundHwnd == gameHwnd;
            var overlayForeground = form.IsHandleCreated && foregroundHwnd == form.Handle;

            // Use the requested visibility, not HtmlUiHost.IsVisible. IsVisible depends on
            // form.Visible, which is exactly what showOverlay decides below, so reading it here
            // makes the tracker unable to bring a hidden overlay back on its own.
            var requestedVisible = _host.IsOverlayRequested;
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
            PublishState(new HtmlUiWindowState(
                foreground || overlayForeground,
                showOverlay && form.Visible,
                minimized,
                actualBounds.Left,
                actualBounds.Top,
                Math.Max(0, actualBounds.Width),
                Math.Max(0, actualBounds.Height)));
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
            if (_hook != IntPtr.Zero)
            {
                try { UnhookWinEvent(_hook); } catch { }
                _hook = IntPtr.Zero;
            }
            // Safe to drop the strong root only after the unmanaged callback is unhooked.
            lock (SyncLock) HookedTrackers.Remove(this);
            Volatile.Write(ref _lastGameHwnd, IntPtr.Zero);
            _form = null;
        }
    }
}
